// Bare console proof of real hardware control -- Phase 2 of the C#
// port (see windows/HANDOFF.md). No effects/presets/service/GUI layer
// here on purpose: the only goal is confirming the ported protocol
// byte values actually produce light through NEW .NET transports
// (HidSharp for the keyboard, System.Management for the lightbar),
// and resolving the COM/WMI API-choice risk empirically.
//
// Usage: dotnet run --project src/JmaStudio.HardwareTest -- <command> [args]

using JmaStudio.Effects;
using JmaStudio.Hardware;
using JmaStudio.HardwareTest;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

string command = args[0].ToLowerInvariant();
try
{
    switch (command)
    {
        case "keyboard-find":
            KeyboardFind();
            break;
        case "keyboard-static":
            KeyboardStatic(args);
            break;
        case "keyboard-frame":
            KeyboardFrame();
            break;
        case "keyboard-off":
            KeyboardStatic(new[] { "keyboard-static", "0", "0", "0" });
            break;
        case "lightbar-find":
            LightbarFind();
            break;
        case "lightbar-static":
            LightbarStatic(args);
            break;
        case "lightbar-all":
            LightbarAll(args);
            break;
        case "lightbar-off":
            LightbarAll(new[] { "lightbar-all", "0", "0", "0" });
            break;
        case "lightbar-mode":
            LightbarMode(args);
            break;
        case "lightbar-thread-test":
            await LightbarThreadTest(args);
            break;
        case "effects-list":
            EffectsList(args);
            break;
        case "effect-demo":
            EffectDemoOne(args);
            break;
        case "effect-demo-all":
            EffectDemoAll(args);
            break;
        case "effect-demo-typing-with-base":
            EffectDemoTypingWithBase(args);
            break;
        case "effect-demo-from-status":
            EffectDemoFromStatus(args);
            break;
        case "effect-live":
            EffectLive(args);
            break;
        case "migrate-presets":
            MigratePresets(args);
            break;
        case "preset-demo":
            PresetDemo(args);
            break;
        case "preset-list":
            PresetList(args);
            break;
        case "lightbar-preset-demo":
            LightbarPresetDemo(args);
            break;
        default:
            Console.WriteLine($"Unknown command: {command}");
            PrintUsage();
            return 1;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"FAILED: {ex.GetType().Name}: {ex.Message}");
    if (ex is System.Management.ManagementException me)
    {
        Console.WriteLine($"  ManagementException.ErrorCode: {me.ErrorCode}");
    }
    return 1;
}

return 0;

void PrintUsage()
{
    Console.WriteLine("""
        Commands:
          keyboard-find                          Locate the PH16-71 lighting HID interface (no elevation needed)
          keyboard-static <r> <g> <b> [brightness]  Solid color across whole keyboard
          keyboard-frame                         Per-key test pattern (proves the 512-byte frame path)
          keyboard-off                           Shortcut for keyboard-static 0 0 0

          lightbar-find                          Confirm AcerGamingFunction WMI class exists (no elevation needed)
          lightbar-static <zone 1-3> <r> <g> <b> [brightness]   REQUIRES ADMINISTRATOR
          lightbar-all <r> <g> <b> [brightness]  All 3 zones at once. REQUIRES ADMINISTRATOR
          lightbar-off                           Shortcut for lightbar-all 0 0 0
          lightbar-mode <mode> <r> <g> <b> [speed] [brightness]  mode = breathing|neon|rainbow|wave|ripple|scanner|strobe. REQUIRES ADMINISTRATOR
          lightbar-thread-test [threadCount=8]   Diagnostic -- see HANDOFF.md. REQUIRES ADMINISTRATOR

          effects-list [--keymap path]                       List every registered Phase 3 effect
          effect-demo <name> [seconds=5] [--keymap path] [--synthetic-key idx]
                                                              Run one effect live on the real keyboard
          effect-demo-all [secondsPerEffect=3] [--keymap path]
                                                              Cycle every registered effect on the real keyboard
          effect-demo-from-status <statusJsonPath> [seconds=300] [--keymap path]
                                                              Ad hoc: load a preset from a saved GET /status JSON
                                                              blob (typing_reactive/custom_keys only for now)
          effect-live <statusJsonPath> [maxSeconds=1800] [--keymap path]
                                                              Load a preset from a saved GET /status JSON blob
                                                              and react to REAL keystrokes via a global keyboard
                                                              hook -- no elevation needed. Ctrl+C to stop early.

          migrate-presets [--python-root path] [--out-dir path]
                                                              Phase 4: migrate the real Python presets.json/
                                                              lightbar_presets.json/lightbar_reactive.json/
                                                              config.json into the new C# preset store
          preset-list [--out-dir path]                       List migrated keyboard presets
          preset-demo <name> [seconds=8] [--out-dir path] [--keymap path]
                                                              Apply a migrated keyboard preset live on the real
                                                              keyboard (typing_reactive gets a synthetic keypress)
          lightbar-preset-demo <name> [--out-dir path]       Apply a migrated lightbar preset via
                                                              Lightbar.ApplyState(). REQUIRES ADMINISTRATOR
        """);
}

static void KeyboardFind()
{
    var device = Keyboard.FindLightingDevice();
    if (device is null)
    {
        Console.WriteLine("NOT FOUND: no 04F2:0117 lighting (usage page 0xFF02) interface present.");
        return;
    }
    Console.WriteLine($"FOUND: {device.DevicePath}");
    Console.WriteLine($"  Product: {device.GetProductName()}");
    Console.WriteLine($"  Manufacturer: {device.GetManufacturer()}");
}

static void KeyboardStatic(string[] args)
{
    byte r = byte.Parse(args[1]);
    byte g = byte.Parse(args[2]);
    byte b = byte.Parse(args[3]);
    byte brightness = args.Length > 4 ? byte.Parse(args[4]) : (byte)255;
    using var kb = Keyboard.Open();
    kb.SetStaticColor(r, g, b, brightness);
    Console.WriteLine($"Sent static color ({r},{g},{b}) @ brightness {brightness}. Check the physical keyboard now.");
}

static void KeyboardFrame()
{
    var colors = new RgbColor[KeyboardConstants.NumCells];
    for (int i = 0; i < colors.Length; i++)
    {
        // Alternating red/blue halves -- easy to visually confirm the
        // per-key buffer (not just a single solid-color command) is
        // what's actually driving the result.
        colors[i] = i < colors.Length / 2 ? new RgbColor(255, 0, 0) : new RgbColor(0, 0, 255);
    }
    using var kb = Keyboard.Open();
    kb.SendFrame(colors);
    Console.WriteLine("Sent per-key test frame (first half red, second half blue). Check the physical keyboard now.");
}

static void LightbarFind()
{
    var instance = LightbarDiagnostics.TryFindRawInstance(Console.Out);
    if (instance is null)
    {
        Console.WriteLine("NOT FOUND: AcerGamingFunction WMI class not present via System.Management.");
        return;
    }
    Console.WriteLine("FOUND: AcerGamingFunction instance resolved via System.Management (ManagementObjectSearcher).");
    instance.Dispose();
}

static void LightbarStatic(string[] args)
{
    int zone = int.Parse(args[1]);
    byte r = byte.Parse(args[2]);
    byte g = byte.Parse(args[3]);
    byte b = byte.Parse(args[4]);
    byte brightness = args.Length > 5 ? byte.Parse(args[5]) : (byte)100;
    var lb = Lightbar.Open();
    lb.SetBrightness(brightness);
    lb.SetZone(zone, r, g, b);
    Console.WriteLine($"Sent zone {zone} static color ({r},{g},{b}) @ brightness {brightness}. Check the physical lightbar now.");
}

static void LightbarAll(string[] args)
{
    byte r = byte.Parse(args[1]);
    byte g = byte.Parse(args[2]);
    byte b = byte.Parse(args[3]);
    byte brightness = args.Length > 4 ? byte.Parse(args[4]) : (byte)100;
    var lb = Lightbar.Open();
    lb.SetBrightness(brightness);
    lb.SetAll(r, g, b);
    Console.WriteLine($"Sent all-zone static color ({r},{g},{b}) @ brightness {brightness}. Check the physical lightbar now.");
}

static void LightbarMode(string[] args)
{
    var mode = Enum.Parse<LightbarMode>(args[1], ignoreCase: true);
    byte r = byte.Parse(args[2]);
    byte g = byte.Parse(args[3]);
    byte b = byte.Parse(args[4]);
    int speed = args.Length > 5 ? int.Parse(args[5]) : 5;
    int brightness = args.Length > 6 ? int.Parse(args[6]) : 100;
    var lb = Lightbar.Open();
    lb.SetMode(mode, r, g, b, speed, brightness);
    Console.WriteLine($"Sent mode {mode} color ({r},{g},{b}) speed {speed} brightness {brightness}. Check the physical lightbar now.");
}

static async Task LightbarThreadTest(string[] args)
{
    int threadCount = args.Length > 1 ? int.Parse(args[1]) : 8;
    Console.WriteLine($"Resolving one AcerGamingFunction instance, then calling SetGamingLED from {threadCount} concurrent Task.Run threads...");
    await LightbarDiagnostics.ProbeThreadAffinityAsync(threadCount, Console.Out);
    Console.WriteLine("Done -- any THREW lines above indicate a real thread-affinity problem that needs handling (see HANDOFF.md item 3).");
}

// ---- Phase 3: effects -------------------------------------------------

static (List<string> positional, Dictionary<string, string> flags) SplitArgs(string[] args, int skip)
{
    var positional = new List<string>();
    var flags = new Dictionary<string, string>();
    for (int i = skip; i < args.Length; i++)
    {
        if (args[i].StartsWith("--") && i + 1 < args.Length)
        {
            flags[args[i][2..]] = args[i + 1];
            i++;
        }
        else
        {
            positional.Add(args[i]);
        }
    }
    return (positional, flags);
}

static string DefaultKeymapPath() =>
    Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "keymap.json"));

static void EffectsList(string[] args)
{
    var (_, flags) = SplitArgs(args, 1);
    string keymapPath = flags.GetValueOrDefault("keymap", DefaultKeymapPath());
    Console.WriteLine($"Using keymap: {keymapPath} (exists: {File.Exists(keymapPath)})");
    var registry = new EffectRegistry(keymapPath);
    foreach (string name in registry.All.Keys.OrderBy(n => n))
    {
        Console.WriteLine($"  {name}");
    }
    Console.WriteLine($"{registry.All.Count} effects registered.");
}

static void EffectDemoOne(string[] args)
{
    var (positional, flags) = SplitArgs(args, 1);
    string name = positional[0];
    double seconds = positional.Count > 1 ? double.Parse(positional[1]) : 5.0;
    string keymapPath = flags.GetValueOrDefault("keymap", DefaultKeymapPath());
    int? syntheticKey = flags.TryGetValue("synthetic-key", out string? sk) ? int.Parse(sk) : null;

    var registry = new EffectRegistry(keymapPath);
    IEffect? effect = registry.TryGet(name);
    if (effect is null)
    {
        Console.WriteLine($"Unknown effect '{name}'. Known effects: {string.Join(", ", registry.All.Keys.OrderBy(n => n))}");
        return;
    }

    // typing_reactive is the only effect that reacts to key_state -- give
    // it a synthetic repeating press by default so the demo actually
    // shows the flash/bolt behavior instead of a static flat background.
    if (syntheticKey is null && name == "typing_reactive")
    {
        syntheticKey = KeyboardConstants.NumCells / 2;
    }

    Console.WriteLine($"Running '{name}' for {seconds}s" + (syntheticKey is int sk2 ? $" (synthetic key press at cell {sk2})" : "") + "...");
    using var kb = Keyboard.Open();
    EffectDemo.Run(kb, effect, effect.DefaultParams, seconds, registry, syntheticKey);
    Console.WriteLine("Done.");
}

static void EffectDemoAll(string[] args)
{
    var (positional, flags) = SplitArgs(args, 1);
    double secondsPerEffect = positional.Count > 0 ? double.Parse(positional[0]) : 3.0;
    string keymapPath = flags.GetValueOrDefault("keymap", DefaultKeymapPath());

    var registry = new EffectRegistry(keymapPath);
    using var kb = Keyboard.Open();
    foreach ((string name, IEffect effect) in registry.All.OrderBy(kv => kv.Key))
    {
        int? syntheticKey = name == "typing_reactive" ? KeyboardConstants.NumCells / 2 : null;
        Console.WriteLine($"[{name}] running for {secondsPerEffect}s...");
        EffectDemo.Run(kb, effect, effect.DefaultParams, secondsPerEffect, registry, syntheticKey);
    }
    Console.WriteLine("All effects demoed.");
}

static void EffectDemoTypingWithBase(string[] args)
{
    var (positional, flags) = SplitArgs(args, 1);
    string baseEffectName = positional.Count > 0 ? positional[0] : "gradient";
    double seconds = positional.Count > 1 ? double.Parse(positional[1]) : 6.0;
    string keymapPath = flags.GetValueOrDefault("keymap", DefaultKeymapPath());

    var registry = new EffectRegistry(keymapPath);
    IEffect? typingReactive = registry.TryGet("typing_reactive");
    if (typingReactive is null)
    {
        Console.WriteLine("typing_reactive not registered.");
        return;
    }
    if (registry.TryGet(baseEffectName) is null)
    {
        Console.WriteLine($"base effect '{baseEffectName}' not registered.");
        return;
    }

    var parameters = new TypingReactiveParams { BaseEffectName = baseEffectName };
    int syntheticKey = KeyboardConstants.NumCells / 2;
    Console.WriteLine($"Running typing_reactive with base_effect='{baseEffectName}' for {seconds}s " +
                       $"(synthetic key press at cell {syntheticKey}) -- this proves the recursive " +
                       "base_effect dispatch actually renders the named effect's live output as the " +
                       "background, not a silent fallback to the flat base_color...");
    using var kb = Keyboard.Open();
    EffectDemo.Run(kb, typingReactive, parameters, seconds, registry, syntheticKey);
    Console.WriteLine("Done. If the background showed the base effect's real look (not flat gray) with a bright bolt pulsing through it, this path works.");
}

static void EffectLive(string[] args)
{
    var (positional, flags) = SplitArgs(args, 1);
    string jsonPath = positional[0];
    double maxSeconds = positional.Count > 1 ? double.Parse(positional[1]) : 1800.0;
    string keymapPath = flags.GetValueOrDefault("keymap", DefaultKeymapPath());

    (string effectName, EffectParams parameters) = AdHocPresetLoader.LoadFromStatusJson(jsonPath);
    var registry = new EffectRegistry(keymapPath);
    IEffect? effect = registry.TryGet(effectName);
    if (effect is null)
    {
        Console.WriteLine($"Effect '{effectName}' not registered.");
        return;
    }

    var indexByName = Layout.NameToIndex(keymapPath);
    var keyState = new LiveKeyState(indexByName);
    using var hook = new GlobalKeyboardHook();
    hook.KeyDown += name =>
    {
        keyState.OnKeyDown(name);
        Console.WriteLine($"  key: {name}");
    };
    hook.Start();

    Console.WriteLine($"Loaded '{effectName}' from {jsonPath}. Reacting to REAL keystrokes (global hook, " +
                       $"works even if this console isn't focused) for up to {maxSeconds}s. Type on the keyboard now.");

    const double fps = 30.0;
    const double keyStateMaxAge = 5.0; // matches daemon/server.py's _KEY_STATE_MAX_AGE
    TimeSpan frameInterval = TimeSpan.FromSeconds(1.0 / fps);
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

    using var kb = Keyboard.Open();
    while (stopwatch.Elapsed.TotalSeconds < maxSeconds)
    {
        double t = stopwatch.Elapsed.TotalSeconds;
        var context = new EffectContext { KeyState = keyState.Snapshot(keyStateMaxAge), Registry = registry };
        RgbColor[] colors = effect.Render(t, KeyboardConstants.NumCells, parameters, context);
        kb.SendFrame(colors);

        double workSeconds = stopwatch.Elapsed.TotalSeconds - t;
        TimeSpan sleep = frameInterval - TimeSpan.FromSeconds(workSeconds);
        if (sleep > TimeSpan.Zero) Thread.Sleep(sleep);
    }
    Console.WriteLine("Done (max duration reached).");
}

static string DefaultPythonRoot() =>
    Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));

static string DefaultPresetsOutDir()
{
    string dir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "data"));
    Directory.CreateDirectory(dir);
    return dir;
}

static void MigratePresets(string[] args)
{
    var (_, flags) = SplitArgs(args, 1);
    string pythonRoot = flags.GetValueOrDefault("python-root", DefaultPythonRoot());
    string outDir = flags.GetValueOrDefault("out-dir", DefaultPresetsOutDir());

    Console.WriteLine($"Python root: {pythonRoot}");
    Console.WriteLine($"Output dir:  {outDir}");

    var store = new JmaStudio.Presets.PresetStore(outDir);
    JmaStudio.Presets.PythonPresetMigrator.MigrateAll(pythonRoot, store);

    var kb = store.KeyboardPresets.Load();
    var lb = store.LightbarPresets.Load();
    var reactive = store.LightbarReactiveConfig.Load();
    var config = store.AppConfig.Load();

    Console.WriteLine($"Migrated {kb.Count} keyboard presets: {string.Join(", ", kb.Keys)}");
    Console.WriteLine($"Migrated {lb.Count} lightbar presets: {string.Join(", ", lb.Keys)}");
    Console.WriteLine($"Lightbar reactive config: enabled={reactive.Enabled}, {reactive.ZoneBoundaries.Count} zone boundaries");
    Console.WriteLine($"App config: default_preset={config.DefaultPreset}, lightbar_default_preset={config.LightbarDefaultPreset}");
}

static void PresetList(string[] args)
{
    var (_, flags) = SplitArgs(args, 1);
    string outDir = flags.GetValueOrDefault("out-dir", DefaultPresetsOutDir());
    var store = new JmaStudio.Presets.PresetStore(outDir);
    foreach ((string name, var preset) in store.KeyboardPresets.Load())
    {
        Console.WriteLine($"  {name}  (effect: {preset.Effect})");
    }
}

static void PresetDemo(string[] args)
{
    var (positional, flags) = SplitArgs(args, 1);
    string name = positional[0];
    double seconds = positional.Count > 1 ? double.Parse(positional[1]) : 8.0;
    string outDir = flags.GetValueOrDefault("out-dir", DefaultPresetsOutDir());
    string keymapPath = flags.GetValueOrDefault("keymap", DefaultKeymapPath());

    var store = new JmaStudio.Presets.PresetStore(outDir);
    var presets = store.KeyboardPresets.Load();
    if (!presets.TryGetValue(name, out var preset))
    {
        Console.WriteLine($"Unknown preset '{name}'. Known: {string.Join(", ", presets.Keys)}");
        return;
    }

    var registry = new EffectRegistry(keymapPath);
    IEffect? effect = registry.TryGet(preset.Effect);
    if (effect is null)
    {
        Console.WriteLine($"Preset '{name}' uses unregistered effect '{preset.Effect}'.");
        return;
    }

    int? syntheticKey = preset.Effect == "typing_reactive" ? KeyboardConstants.NumCells / 2 : null;
    Console.WriteLine($"Applying preset '{name}' (effect: {preset.Effect}) for {seconds}s...");
    using var kb = Keyboard.Open();
    EffectDemo.Run(kb, effect, preset.Params, seconds, registry, syntheticKey);
    Console.WriteLine("Done.");
}

static void LightbarPresetDemo(string[] args)
{
    var (positional, flags) = SplitArgs(args, 1);
    string name = positional[0];
    string outDir = flags.GetValueOrDefault("out-dir", DefaultPresetsOutDir());

    var store = new JmaStudio.Presets.PresetStore(outDir);
    var presets = store.LightbarPresets.Load();
    if (!presets.TryGetValue(name, out var preset))
    {
        Console.WriteLine($"Unknown lightbar preset '{name}'. Known: {string.Join(", ", presets.Keys)}");
        return;
    }

    var lb = Lightbar.Open();
    Console.WriteLine($"Applying lightbar preset '{name}' via Lightbar.ApplyState()...");
    lb.ApplyState(preset.Lightbar);
    Console.WriteLine("Done. Check the physical lightbar now.");
}

static void EffectDemoFromStatus(string[] args)
{
    var (positional, flags) = SplitArgs(args, 1);
    string jsonPath = positional[0];
    double seconds = positional.Count > 1 ? double.Parse(positional[1]) : 300.0;
    string keymapPath = flags.GetValueOrDefault("keymap", DefaultKeymapPath());

    (string effectName, EffectParams parameters) = AdHocPresetLoader.LoadFromStatusJson(jsonPath);
    var registry = new EffectRegistry(keymapPath);
    IEffect? effect = registry.TryGet(effectName);
    if (effect is null)
    {
        Console.WriteLine($"Effect '{effectName}' not registered.");
        return;
    }

    // Rotate a synthetic "keypress" across a handful of cells scattered
    // around the board -- closer to real typing than hammering one spot,
    // which matters for THIS preset since its bolt settings (radial,
    // short max distance, fast speed) are tuned to look good on
    // scattered real keystrokes, not a single repeated point.
    int[] syntheticKeys = { 20, 35, 50, 65, 80, 95, 45, 60 };

    Console.WriteLine($"Loaded '{effectName}' from {jsonPath}. Running for {seconds}s with a rotating synthetic keypress across {syntheticKeys.Length} cells...");
    using var kb = Keyboard.Open();
    EffectDemo.Run(kb, effect, parameters, seconds, registry, syntheticKeys);
    Console.WriteLine("Done.");
}
