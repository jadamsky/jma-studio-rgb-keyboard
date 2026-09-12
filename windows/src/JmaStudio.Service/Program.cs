// JMA Studio background service -- C# port of daemon/server.py's role.
// Owns the HID keyboard, WMI lightbar, and DualSense controller
// connections; the GUI (Phase 6) and any other client talk to this
// over HTTP, never touching hardware directly. Runs as a real Windows
// Service (settled decision #2) via UseWindowsService() below, but the
// exact same binary also runs fine as a plain console app for
// development (WindowsServiceLifetime just doesn't attach when not
// actually launched by the Service Control Manager) -- that's how this
// was tested throughout Phase 5, before any OS-level service
// registration exists (that's Phase 7's job).
//
// HTTP API deliberately modernized (settled decision #7): real status
// codes / ProblemDetails-shaped errors, not Python's always-200
// {"ok":...,"error":...} convention -- there's no old JS frontend to
// stay compatible with, the WPF client is new.

using JmaStudio.Effects;
using JmaStudio.Hardware;
using JmaStudio.Presets;
using JmaStudio.Service;

DateTime serviceStartedAtUtc = DateTime.UtcNow;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();

// Match PresetJsonOptions.Default (JmaStudio.Presets) so the HTTP API
// and the on-disk preset files serialize EffectParams consistently --
// otherwise ASP.NET Core's own default JsonOptions apply instead, which
// don't include ValueTuple fields (TypingReactiveParams.BoltDirections
// would come back as empty {} entries) or stringify enums.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.IncludeFields = true;
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    // ASP.NET Core's own minimal-API default is camelCase property
    // names, which doesn't match PresetJsonOptions.Default (JmaStudio.
    // Presets -- PascalCase, no naming policy, matching the on-disk
    // preset files). The GUI client deserializes HTTP responses using
    // THAT SAME PresetJsonOptions.Default for consistency, so the two
    // layers need to agree -- null it out here rather than making the
    // GUI carry a second, HTTP-specific JsonSerializerOptions. Hit this
    // live: without this, every HTTP response silently deserialized to
    // all-default/null property values (case-sensitive exact-name
    // matching with no policy just skips every non-matching property),
    // which surfaced downstream as a NullReferenceException instead of
    // a clear deserialization error.
    options.SerializerOptions.PropertyNamingPolicy = null;
});

// ---- data/config locations ---------------------------------------
// Overridable via environment variables for now; the Installer phase
// (7) will decide the real production defaults (likely ProgramData).
// Defaulting to the same windows/data/ + repo-root keymap.json already
// used by JmaStudio.HardwareTest during Phases 3-4 keeps this testable
// against the same real migrated data without extra setup.
string dataDir = Environment.GetEnvironmentVariable("JMASTUDIO_DATA_DIR")
    ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data"));
string keymapPath = Environment.GetEnvironmentVariable("JMASTUDIO_KEYMAP_PATH")
    ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "keymap.json"));
Directory.CreateDirectory(dataDir);

// keymapPath resolves to <python repo root>/keymap.json (see above) --
// its directory IS the Python repo root, needed by the Diagnostics
// window's install-detection and by switch-to-python/switch-to-csharp
// (JmaStudio.HardwareTest) to find daemon/server.py, start_all.bat, etc.
string pythonRepoRoot = Path.GetDirectoryName(keymapPath)!;

// File logging for the Diagnostics window's "Logs panel" -- the Service
// had no persistent log output at all before this (see
// FileLoggerProvider.cs's header comment). One provider instance shared
// by every LoggerFactory below so everything ends up in the same file.
string logFilePath = Path.Combine(dataDir, "logs", "service.log");
var fileLoggerProvider = new FileLoggerProvider(logFilePath);
builder.Logging.AddProvider(fileLoggerProvider);

var logger = LoggerFactory.Create(b => { b.AddConsole(); b.AddProvider(fileLoggerProvider); }).CreateLogger("Startup");
logger.LogInformation("Data dir: {DataDir}", dataDir);
logger.LogInformation("Keymap:   {KeymapPath} (exists: {Exists})", keymapPath, File.Exists(keymapPath));

// ---- AcerLightingService: stop + disable, unconditionally, every startup ----
try
{
    AcerLightingServiceManager.StopAndDisable();
    logger.LogInformation("AcerLightingService stop+disable check done.");
}
catch (Exception ex)
{
    logger.LogWarning(ex, "AcerLightingService stop+disable failed (likely not elevated) -- continuing anyway.");
}

// ---- hardware (graceful degradation, matching the Python daemon) ----
Keyboard? keyboard = TryOpen("keyboard", Keyboard.Open, logger);
Lightbar? lightbar = TryOpen("lightbar", Lightbar.Open, logger);
Controller? controller = TryOpen("controller", Controller.Open, logger);

var effectRegistry = new EffectRegistry(keymapPath);
var presetStore = new PresetStore(dataDir);
var indexByName = Layout.NameToIndex(keymapPath);

var inputListener = new InputListener(indexByName);
// The Service's own local hook only ever worked because dev-mode testing
// ran this as a plain console app in the interactive session (Session
// 1), same as the desktop -- a real installed Windows Service runs in
// Session 0, which cannot see interactive-desktop keystrokes at all
// (Session 0 Isolation, see HANDOFF.md's Phase 7 "Critical finding").
// Production key capture now comes from JmaStudio.Gui's
// KeypressForwarder via POST /keypress (Endpoints.MapInput) instead --
// NOT starting this local hook by default avoids double-counting every
// keystroke when the GUI is also open and forwarding. Opt in for the
// narrow case of testing the Service's reactive effects with no GUI
// open at all.
if (Environment.GetEnvironmentVariable("JMASTUDIO_LOCAL_KEY_HOOK") == "1")
{
    inputListener.Start();
    logger.LogInformation("Local keyboard hook started (JMASTUDIO_LOCAL_KEY_HOOK=1).");
}

(string startupEffect, EffectParams startupParams) = ResolveStartupEffect(presetStore);
var daemonState = new DaemonState(presetStore.LiveKeyboardState, startupEffect, startupParams);

var lightbarController = new LightbarController(lightbar, presetStore.LiveLightbarState);
lightbarController.RestorePersistedState();

var controllerReactiveManager = new ControllerReactiveManager(daemonState, presetStore.ControllerReactiveSettings);

builder.Services.AddSingleton(effectRegistry);
builder.Services.AddSingleton(presetStore);
builder.Services.AddSingleton(daemonState);
builder.Services.AddSingleton(lightbarController);
builder.Services.AddSingleton(controllerReactiveManager);
builder.Services.AddSingleton(inputListener);
var selfTestGate = new SelfTestGate();
builder.Services.AddSingleton(selfTestGate);
// Keyboard/Controller are NOT registered in DI -- both can legitimately
// be null (hardware not present), and nothing resolves them via
// constructor injection; they're passed directly to the endpoint
// mapping methods and RenderLoopService's factory below instead.
builder.Services.AddSingleton<IHostedService>(sp => new RenderLoopService(
    daemonState, effectRegistry, keyboard, inputListener, controller, selfTestGate,
    sp.GetRequiredService<ILogger<RenderLoopService>>()));
// LightbarReactiveManager is constructed here (not via a DI factory) so
// the same instance can be both started as a background loop AND handed
// directly to Endpoints.MapLightbar for its ResetDedup() call -- same
// pattern as daemonState/lightbarController/controllerReactiveManager
// above.
var lightbarReactiveManager = new LightbarReactiveManager(
    lightbarController, inputListener, presetStore, keymapPath,
    LoggerFactory.Create(b => { b.AddConsole(); b.AddProvider(fileLoggerProvider); }).CreateLogger<LightbarReactiveManager>());
builder.Services.AddSingleton<IHostedService>(lightbarReactiveManager);

// Phase 8 (V2): idle screensaver + low-battery override. Both are
// constructed directly (not via a DI factory) and registered as
// IHostedService, same pattern as lightbarReactiveManager above --
// Endpoints.MapIdleScreensaver needs the same instance for
// RecordExternalActivity(). The two managers share ONE
// EffectOverrideCoordinator instance so "battery wins" (confirmed by
// the user) can actually be enforced -- see EffectOverrideCoordinator's
// own header comment for why two fully-independent BackgroundServices
// would otherwise risk fighting over DaemonState.SetEffect().
var effectOverrideCoordinator = new EffectOverrideCoordinator();
var idleScreensaverManager = new IdleScreensaverManager(
    daemonState, lightbarController, presetStore, controller, effectOverrideCoordinator,
    LoggerFactory.Create(b => { b.AddConsole(); b.AddProvider(fileLoggerProvider); }).CreateLogger<IdleScreensaverManager>());
builder.Services.AddSingleton<IHostedService>(idleScreensaverManager);
var lowBatteryOverrideManager = new LowBatteryOverrideManager(
    daemonState, lightbarController, presetStore, effectOverrideCoordinator,
    LoggerFactory.Create(b => { b.AddConsole(); b.AddProvider(fileLoggerProvider); }).CreateLogger<LowBatteryOverrideManager>());
builder.Services.AddSingleton<IHostedService>(lowBatteryOverrideManager);
// Hardcoded, always-on plug/unplug red/green flash -- not a selectable
// feature (no config, no endpoint), per the user's explicit request. See
// PowerStateFlashManager's own header comment.
var powerStateFlashManager = new PowerStateFlashManager(
    daemonState, effectOverrideCoordinator,
    LoggerFactory.Create(b => { b.AddConsole(); b.AddProvider(fileLoggerProvider); }).CreateLogger<PowerStateFlashManager>());
builder.Services.AddSingleton<IHostedService>(powerStateFlashManager);

var diagnosticsManager = new DiagnosticsManager(
    lightbarController, daemonState, keyboard, controller, selfTestGate,
    pythonRepoRoot, serviceStartedAtUtc, logFilePath);

var app = builder.Build();

// Minimal request log -- added after a live incident where the keyboard
// effect changed (Red Chase -> spectrum_cycle) with no way to tell
// whether a client actually called the API or something in-process did
// it. There was no logging of any kind for incoming requests before
// this, making that undiagnosable after the fact. Logs method+path only
// (not bodies) to keep this cheap on the 30fps-adjacent hot path.
app.Use(async (context, next) =>
{
    var reqLogger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    reqLogger.LogInformation("{Method} {Path}", context.Request.Method, context.Request.Path);
    await next();
});

Endpoints.MapKeyboard(app, daemonState, effectRegistry, presetStore, keyboard, controller);
Endpoints.MapLightbar(app, lightbarController, presetStore, daemonState, lightbarReactiveManager);
Endpoints.MapControllerReactive(app, controllerReactiveManager, presetStore, controller);
Endpoints.MapLayout(app, keymapPath);
Endpoints.MapDiagnostics(app, diagnosticsManager, controller, logFilePath);
Endpoints.MapSystem(app);
Endpoints.MapInput(app, inputListener);
Endpoints.MapIdleScreensaver(app, idleScreensaverManager, presetStore);
Endpoints.MapLowBatteryOverride(app, presetStore);

// Loopback-only, same port the Python daemon used -- no auth either
// way, trusted purely by being on 127.0.0.1, matching daemon/server.py.
app.Run("http://127.0.0.1:8420");

static T? TryOpen<T>(string label, Func<T> open, ILogger logger) where T : class
{
    try
    {
        T result = open();
        logger.LogInformation("{Label} initialized.", label);
        return result;
    }
    catch (Exception ex)
    {
        logger.LogWarning("Running without {Label}: {Message}", label, ex.Message);
        return null;
    }
}

// Applied unconditionally on every boot -- NOT just a fresh-install
// fallback (see DaemonState.cs's own updated header comment for why
// this changed 2026-09-10). Falls through to plain black only if no
// default preset is configured at all, or the configured one no longer
// exists (e.g. deleted since being set as default).
static (string Effect, EffectParams Params) ResolveStartupEffect(PresetStore store)
{
    AppConfig config = store.AppConfig.Load();
    if (config.DefaultPreset is { } name
        && store.KeyboardPresets.Load().TryGetValue(name, out KeyboardPreset? preset))
    {
        return (preset.Effect, preset.Params);
    }
    return ("static", new StaticParams());
}
