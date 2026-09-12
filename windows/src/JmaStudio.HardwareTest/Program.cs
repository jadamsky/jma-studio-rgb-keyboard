// Bare console proof of real hardware control -- Phase 2 of the C#
// port (see windows/HANDOFF.md). No effects/presets/service/GUI layer
// here on purpose: the only goal is confirming the ported protocol
// byte values actually produce light through NEW .NET transports
// (HidSharp for the keyboard, System.Management for the lightbar),
// and resolving the COM/WMI API-choice risk empirically.
//
// Usage: dotnet run --project src/JmaStudio.HardwareTest -- <command> [args]

using System.Diagnostics;
using System.Management;
using HidSharp;
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
        case "reassert-dominance":
            ReassertDominance(args);
            break;
        case "switch-to-python":
            SwitchToPython(args);
            break;
        case "switch-to-csharp":
            SwitchToCSharp(args);
            break;
        case "controller-raw-dump":
            ControllerRawDump(args);
            break;
        case "controller-enhance-test":
            ControllerEnhanceTest(args);
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

          reassert-dominance [--out-dir path]                Diagnostics window's emergency button #1: re-stop
                                                              +disable AcerLightingService and re-push the last
                                                              persisted lightbar state. REQUIRES ADMINISTRATOR
          switch-to-python [--python-root path]              Diagnostics window's emergency button #2: kill the
                                                              C# GUI/Service, re-enable Python's autostart task,
                                                              launch start_all.bat. REQUIRES ADMINISTRATOR
          switch-to-csharp                                   Diagnostics window's emergency button #3: the exact
                                                              inverse -- kill the Python daemon/tray/GUI, disable
                                                              its autostart task, launch the C# Service+GUI.
                                                              REQUIRES ADMINISTRATOR

          controller-raw-dump [seconds]                      Phase 8 Feature 3 investigation: lists every matching
                                                              HID device (USB and Bluetooth-paired both show up here)
                                                              and dumps raw report bytes live for [seconds] (default
                                                              20) so the DualSense's Bluetooth report format can be
                                                              confirmed empirically.
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

// Phase 8 (V2) Feature 3 investigation tool: lists every HID device
// matching the DualSense's VID/PID (both USB and Bluetooth-paired
// devices show up in the same HidSharp enumeration, per JmaStudio.
// Hardware.Controller's own FindControllerDevice -- this just doesn't
// stop at the first match), then dumps raw report bytes live so the
// Bluetooth report format (different report ID, extra framing per
// community reverse-engineering) can be confirmed empirically against
// this real controller/machine rather than assumed. Not part of the
// shipped Controller.cs parsing logic -- purely a one-off diagnostic.
// Tests the Bluetooth "enhanced report" trick (reading feature report
// 0x05 -- see Controller.TryEnableBluetoothEnhancedMode's own header
// comment for the community sources) WITHOUT needing anyone to touch
// the controller: if it works, the accelerometer/gyro bytes should
// start showing real non-zero values within a couple of seconds just
// from gravity/ambient vibration, even with the controller sitting
// still on a desk -- a self-verifying test, no button presses needed.
static void ControllerEnhanceTest(string[] args)
{
    const int vendorId = 0x054C;
    int[] productIds = { 0x0CE6, 0x0DF2 };
    const int usbMaxReportLength = 64;

    HidDevice? device = null;
    foreach (int pid in productIds)
    {
        foreach (HidDevice d in DeviceList.Local.GetHidDevices(vendorId, pid))
        {
            if (d.GetMaxInputReportLength() > usbMaxReportLength) { device = d; break; }
        }
        if (device is not null) break;
    }

    if (device is null)
    {
        Console.WriteLine("NOT FOUND: no Bluetooth-paired DualSense/Edge (checked by maxInputReportLength > 64).");
        return;
    }

    Console.WriteLine($"Found Bluetooth device: {device.DevicePath}");
    Console.WriteLine($"maxInputReportLength={device.GetMaxInputReportLength()} maxFeatureReportLength={device.GetMaxFeatureReportLength()}");

    if (!device.TryOpen(out HidStream? stream) || stream is null)
    {
        Console.WriteLine("FAILED to open the device.");
        return;
    }
    using (stream)
    {
        stream.ReadTimeout = 200;

        Console.WriteLine();
        Console.WriteLine("--- Reading feature report 0x05 (calibration) ---");
        try
        {
            int len = device.GetMaxFeatureReportLength();
            byte[] featureBuf = new byte[len];
            featureBuf[0] = 0x05;
            stream.GetFeature(featureBuf);
            Console.WriteLine($"SUCCESS: GetFeature(0x05) returned {len} bytes: {Convert.ToHexString(featureBuf)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILED: GetFeature(0x05) threw {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("--- Watching bytes 10+ for 8s (should go non-zero if enhanced mode is now active, even at rest) ---");
        int bufLen = Math.Max(64, device.GetMaxInputReportLength());
        DateTime until = DateTime.UtcNow.AddSeconds(8);
        bool sawNonZeroTail = false;
        while (DateTime.UtcNow < until)
        {
            byte[] buf = new byte[bufLen];
            int read;
            try { read = stream.Read(buf, 0, buf.Length); }
            catch { continue; }
            if (read <= 10) continue;
            for (int i = 10; i < read; i++)
            {
                if (buf[i] != 0) { sawNonZeroTail = true; break; }
            }
            if (sawNonZeroTail)
            {
                Console.WriteLine($"NON-ZERO TAIL SEEN: len={read}: {Convert.ToHexString(buf[..read])}");
                break;
            }
        }
        Console.WriteLine(sawNonZeroTail
            ? "RESULT: enhanced mode looks ACTIVE -- bytes past offset 10 are populated."
            : "RESULT: enhanced mode does NOT look active -- bytes past offset 10 stayed all-zero for 8s.");
    }
}

static void ControllerRawDump(string[] args)
{
    int seconds = args.Length > 1 ? int.Parse(args[1]) : 20;
    const int vendorId = 0x054C;
    int[] productIds = { 0x0CE6, 0x0DF2 };
    const int usagePage = 1;
    const int usage = 5;

    var matches = new List<HidDevice>();
    foreach (int pid in productIds)
    {
        matches.AddRange(DeviceList.Local.GetHidDevices(vendorId, pid));
    }

    if (matches.Count == 0)
    {
        Console.WriteLine("NOT FOUND: no HID device matched VID 0x054C / PID 0x0CE6|0x0DF2 (USB or Bluetooth).");
        return;
    }

    Console.WriteLine($"Found {matches.Count} matching HID device(s):");
    for (int i = 0; i < matches.Count; i++)
    {
        HidDevice device = matches[i];
        Console.WriteLine($"[{i}] path={device.DevicePath}");
        Console.WriteLine($"    product={device.GetProductName()} manufacturer={device.GetManufacturer()} maxInputReportLength={device.GetMaxInputReportLength()}");
        try
        {
            var descriptor = device.GetReportDescriptor();
            bool usageMatch = descriptor.DeviceItems
                .SelectMany(di => di.Usages.GetAllValues())
                .Any(u => ((u >> 16) & 0xFFFF) == usagePage && (u & 0xFFFF) == usage);
            Console.WriteLine($"    gamepadUsageMatch={usageMatch}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    (couldn't read report descriptor: {ex.Message})");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Dumping raw reports from each device for {seconds}s -- move sticks / press buttons now.");
    Console.WriteLine("Format: [deviceIndex] len=N: hex bytes (only printed when the report changes)");

    bool stop = false;
    var threads = new List<Thread>();
    for (int i = 0; i < matches.Count; i++)
    {
        int idx = i;
        HidDevice device = matches[i];
        var thread = new Thread(() =>
        {
            if (!device.TryOpen(out HidStream? stream) || stream is null)
            {
                Console.WriteLine($"[{idx}] could not open device.");
                return;
            }
            using (stream)
            {
                stream.ReadTimeout = 200;
                byte[] last = Array.Empty<byte>();
                int bufLen = Math.Max(64, device.GetMaxInputReportLength());
                while (!stop)
                {
                    byte[] buf = new byte[bufLen];
                    int read;
                    try { read = stream.Read(buf, 0, buf.Length); }
                    catch { continue; }
                    if (read <= 0) continue;
                    byte[] actual = buf[..read];
                    if (!actual.SequenceEqual(last))
                    {
                        last = actual;
                        Console.WriteLine($"[{idx}] len={read}: {Convert.ToHexString(actual)}");
                    }
                }
            }
        }) { IsBackground = true };
        threads.Add(thread);
        thread.Start();
    }

    Thread.Sleep(TimeSpan.FromSeconds(seconds));
    stop = true;
    Console.WriteLine("Done.");
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

// ---- Diagnostics window emergency-action row (windows/HANDOFF.md
// settled decision #11) -- run here, as separate one-shot elevated
// processes, rather than as HTTP endpoints on the already-running
// Service. See JmaStudio.Service/DiagnosticsManager.cs's header comment
// for why: (1) the GUI launches each of these via Verb="runas" so every
// click genuinely triggers its own UAC prompt, matching the shield-icon
// convention -- routing through an already-elevated Service would never
// prompt at all; (2) switch-to-python needs to end by killing the very
// Service process that would otherwise be handling the HTTP request;
// (3) switch-to-csharp needs to work even when the Service isn't
// running at all (Python is the active stack). All three assume they're
// invoked the same way every other elevated command here is: from
// `windows/` (so DefaultPythonRoot()/DefaultPresetsOutDir() resolve
// correctly), already elevated.

static void StopAndDisableAcerLightingService()
{
    // Small intentional duplication of JmaStudio.Service's
    // AcerLightingServiceManager.StopAndDisable() -- referencing that
    // ASP.NET Core Web SDK project from this lightweight console tool
    // would pull in the whole web framework reference for ~15 lines of
    // logic that never changes independently of this file anyway.
    const string serviceName = "AcerLightingService";
    try
    {
        using var controller = new System.ServiceProcess.ServiceController(serviceName);
        _ = controller.Status; // throws if the service doesn't exist on this machine
        if (controller.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
        {
            controller.Stop();
            controller.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
        }
    }
    catch
    {
        // Not present on this machine, or already stopped -- fine either way.
    }
    try
    {
        using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Service WHERE Name='{serviceName}'");
        foreach (ManagementBaseObject result in searcher.Get())
        {
            using var service = (ManagementObject)result;
            service.InvokeMethod("ChangeStartMode", new object[] { "Disabled" });
        }
    }
    catch
    {
        // Best-effort -- even if this fails, the Stop above already helps.
    }
}

static void KillMatchingProcesses(string commandLineSubstring, string label)
{
    using var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process");
    int killed = 0;
    foreach (ManagementBaseObject obj in searcher.Get())
    {
        if (obj["CommandLine"] is not string cmdLine
            || !cmdLine.Contains(commandLineSubstring, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }
        int pid = Convert.ToInt32(obj["ProcessId"]);
        try
        {
            Process.GetProcessById(pid).Kill();
            Console.WriteLine($"Killed {label} (PID {pid}).");
            killed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not kill PID {pid} ({label}): {ex.Message}");
        }
    }
    if (killed == 0) Console.WriteLine($"No running {label} process found.");
}

static void SetScheduledTaskEnabled(string taskName, bool enabled)
{
    try
    {
        var psi = new ProcessStartInfo("schtasks.exe", $"/Change /TN \"{taskName}\" /{(enabled ? "Enable" : "Disable")}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using Process? proc = Process.Start(psi);
        proc?.WaitForExit(5000);
        Console.WriteLine(proc?.ExitCode == 0
            ? $"Scheduled task '{taskName}' {(enabled ? "enabled" : "disabled")}."
            : $"Could not change scheduled task '{taskName}' -- it may not exist on this machine.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Could not change scheduled task '{taskName}': {ex.Message}");
    }
}

static void ReassertDominance(string[] args)
{
    var (_, flags) = SplitArgs(args, 1);
    string outDir = flags.GetValueOrDefault("out-dir", DefaultPresetsOutDir());

    Console.WriteLine("Stopping + disabling AcerLightingService...");
    StopAndDisableAcerLightingService();
    Console.WriteLine("Done.");

    var store = new JmaStudio.Presets.PresetStore(outDir);
    var savedLightbar = store.LiveLightbarState.Load();
    if (savedLightbar is not null)
    {
        try
        {
            Lightbar lb = Lightbar.Open();
            lb.ApplyState(savedLightbar);
            Console.WriteLine("Re-applied the last-known lightbar state.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not re-apply lightbar state: {ex.Message}");
        }
    }
    else
    {
        Console.WriteLine("No persisted lightbar state found to re-apply.");
    }
    Console.WriteLine("If JmaStudio.Service is still running, its own render loop keeps driving the keyboard every frame -- nothing else to nudge there.");
}

static void SwitchToPython(string[] args)
{
    var (_, flags) = SplitArgs(args, 1);
    string pythonRoot = flags.GetValueOrDefault("python-root", DefaultPythonRoot());
    string startAllBat = Path.Combine(pythonRoot, "start_all.bat");
    if (!File.Exists(startAllBat))
    {
        Console.WriteLine($"ERROR: {startAllBat} not found -- the Python version doesn't appear to be installed on this machine. Aborting, nothing was changed.");
        return;
    }

    Console.WriteLine("Switching to the Python version...");
    KillMatchingProcesses("JmaStudio.Gui", "the C# GUI");
    KillMatchingProcesses("JmaStudio.Service", "the C# Service");
    SetScheduledTaskEnabled("JMA Studio Autostart", enabled: true);

    Console.WriteLine($"Starting {startAllBat}...");
    Process.Start(new ProcessStartInfo(startAllBat) { WorkingDirectory = pythonRoot, UseShellExecute = true });
    Console.WriteLine("Done. The Python daemon/tray/GUI should be starting now -- this process is already " +
                       "elevated, so start_all.ps1's own self-elevation check should not prompt again.");
}

static void SwitchToCSharp(string[] args)
{
    string windowsRoot = Directory.GetCurrentDirectory();

    Console.WriteLine("Switching to the C# version...");
    KillMatchingProcesses("uvicorn", "the Python daemon");
    KillMatchingProcesses("tray.py", "the Python tray icon");
    KillMatchingProcesses("gui.py", "the Python GUI");
    SetScheduledTaskEnabled("JMA Studio Autostart", enabled: false);

    // No installer/published exe exists yet (Phase 7) -- this launches
    // the exact same `dotnet run` dev commands documented in
    // HANDOFF.md's "How to build / run / test". Once Phase 7 ships a
    // real installed exe, this should launch that instead. Also note:
    // since this whole process is already elevated (needed for the
    // scheduled-task/process changes above), both children inherit that
    // elevation -- including the GUI, which normally runs unelevated.
    // Acceptable for now; the Phase 7 installer's real GUI autostart
    // entry should launch it unelevated as usual.
    Console.WriteLine("Starting JmaStudio.Service...");
    Process.Start(new ProcessStartInfo("dotnet", $"run --project \"{Path.Combine(windowsRoot, "src", "JmaStudio.Service")}\"")
    {
        WorkingDirectory = windowsRoot,
        UseShellExecute = true,
    });
    Console.WriteLine("Starting JmaStudio.Gui...");
    Process.Start(new ProcessStartInfo("dotnet", $"run --project \"{Path.Combine(windowsRoot, "src", "JmaStudio.Gui")}\"")
    {
        WorkingDirectory = windowsRoot,
        UseShellExecute = true,
    });
    Console.WriteLine("Done.");
}
