// Bare console proof of real hardware control -- Phase 2 of the C#
// port (see windows/HANDOFF.md). No effects/presets/service/GUI layer
// here on purpose: the only goal is confirming the ported protocol
// byte values actually produce light through NEW .NET transports
// (HidSharp for the keyboard, System.Management for the lightbar),
// and resolving the COM/WMI API-choice risk empirically.
//
// Usage: dotnet run --project src/JmaStudio.HardwareTest -- <command> [args]

using JmaStudio.Hardware;

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
