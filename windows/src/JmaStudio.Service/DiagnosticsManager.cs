// Backend for the WPF Diagnostics window (windows/HANDOFF.md settled
// decision #11). Bundles hardware/service health for the dashboard, and
// the two non-destructive self-tests (test-keyboard/test-lightbar) and
// the presence-only rescan. The three genuinely destructive
// "emergency-action row" buttons (re-assert dominance, switch to
// Python, switch to C#) are deliberately NOT here -- see
// JmaStudio.HardwareTest's reassert-dominance/switch-to-python/
// switch-to-csharp commands instead, invoked directly by the GUI via an
// elevated `dotnet run`/published-exe launch (Verb="runas") so each one
// genuinely triggers its own UAC prompt at click time, matching the
// shield-icon convention the design calls for -- routing them through
// this already-elevated Service would mean no fresh prompt ever shows,
// which would make that icon a lie. Also, "switch to Python" needs to
// end by shutting THIS process down (to release the keyboard/lightbar
// handles) and "switch to C#" needs to work even when this Service
// isn't running at all (Python is currently the active stack) -- both
// are a much more natural fit for a separate one-shot process than
// something this Service does to itself mid-request.

using System.Diagnostics;
using JmaStudio.Hardware;

namespace JmaStudio.Service;

public sealed class DiagnosticsManager
{
    // Names checked for the "is something else fighting for control of
    // the lighting again" early warning (settled decision #11's
    // service-health panel ask). Best-effort -- the exact hidden
    // predatorservice process name from LIGHTBAR_REVERSE_ENGINEERING.md
    // §6 (main branch) was never pinned down precisely, so this checks
    // every plausible name rather than one exact match. Deliberately
    // does NOT include plain "PredatorSense" (the ordinary companion
    // app/tray icon) -- settled decision #10 already confirmed live
    // that having it open does nothing while AcerLightingService stays
    // stopped, so flagging it here would just be a false alarm every
    // time the user has it open for unrelated reasons.
    private static readonly string[] SuspiciousProcessNames = { "OpenRGB", "PredatorSenseService" };

    private const string AutostartTaskName = "JMA Studio Autostart";

    private readonly LightbarController _lightbar;
    private readonly DaemonState _daemon;
    private readonly Keyboard? _keyboard;
    private readonly ControllerHolder _controllerHolder;
    private readonly SelfTestGate _gate;
    private readonly string _pythonRepoRoot;
    private readonly DateTime _startedAtUtc;
    private readonly string _logFilePath;

    public DiagnosticsManager(
        LightbarController lightbar, DaemonState daemon, Keyboard? keyboard, ControllerHolder controllerHolder,
        SelfTestGate gate, string pythonRepoRoot, DateTime startedAtUtc, string logFilePath)
    {
        _lightbar = lightbar;
        _daemon = daemon;
        _keyboard = keyboard;
        _controllerHolder = controllerHolder;
        _gate = gate;
        _pythonRepoRoot = pythonRepoRoot;
        _startedAtUtc = startedAtUtc;
        _logFilePath = logFilePath;
    }

    public object GetStatus()
    {
        (long rendered, long written) = _daemon.Stats;
        (double hidMin, double hidAvg, double hidMax, long hidCount) = Keyboard.HidLatency.Snapshot();
        (double wmiMin, double wmiAvg, double wmiMax, long wmiCount) = Lightbar.WmiLatency.Snapshot();
        (string acerStatus, string acerStartMode) = AcerLightingServiceManager.GetStatus();
        string[] suspiciousProcesses = SuspiciousProcessNames.Where(n => Process.GetProcessesByName(n).Length > 0).ToArray();
        (bool pyInstalled, bool taskExists, bool taskEnabled) = DetectPythonInstall();

        return new
        {
            uptimeSeconds = (DateTime.UtcNow - _startedAtUtc).TotalSeconds,
            logFilePath = _logFilePath,
            keyboard = new
            {
                connected = _keyboard is not null,
                detected = Keyboard.FindLightingDevice() is not null,
                vendorId = KeyboardConstants.VendorId,
                productId = KeyboardConstants.ProductId,
            },
            lightbar = new
            {
                connected = _lightbar.Available,
                detected = Lightbar.IsPresent(),
                state = _lightbar.GetState(),
            },
            controller = new
            {
                connected = _controllerHolder.Current?.IsConnected ?? false,
                detected = Controller.IsPresent(),
            },
            perf = new
            {
                framesRendered = rendered,
                framesWritten = written,
                hidLatencyMs = new { min = hidMin, avg = hidAvg, max = hidMax, count = hidCount },
                wmiLatencyMs = new { min = wmiMin, avg = wmiAvg, max = wmiMax, count = wmiCount },
            },
            acerLightingService = new { status = acerStatus, startMode = acerStartMode },
            suspiciousProcesses,
            python = new
            {
                installed = pyInstalled,
                scheduledTaskExists = taskExists,
                scheduledTaskEnabled = taskEnabled,
                repoRoot = _pythonRepoRoot,
            },
        };
    }

    /// <summary>Keyboard/lightbar stay presence-only re-scans (the
    /// handles opened at Program.cs startup aren't behind a mutable
    /// holder -- rebuilding them live would need the same refactor
    /// ControllerHolder just did for the controller specifically, not
    /// justified for those two yet). The controller IS now a true
    /// reconnect, not just presence-only, per Phase 8 (V2) Feature 3's
    /// "nice bonus" -- if nothing is currently connected, this attempts
    /// ControllerHolder.TryDiscover() (same USB-or-Bluetooth scan the
    /// GUI's "Discover" button uses) so a controller connected after
    /// the Service started can be picked up from Diagnostics too, not
    /// just controller-reactive's own Discover button.</summary>
    public object Rescan()
    {
        if (_controllerHolder.Current is not { IsConnected: true })
        {
            _controllerHolder.TryDiscover();
        }
        return new
        {
            keyboardDetected = Keyboard.FindLightingDevice() is not null,
            lightbarDetected = Lightbar.IsPresent(),
            controllerDetected = Controller.IsPresent(),
            controllerConnected = _controllerHolder.Current?.IsConnected ?? false,
        };
    }

    /// <summary>Brief red/green/blue flash across the whole keyboard.
    /// Gates the render loop (SelfTestGate) for the duration so the two
    /// don't interleave raw writes to the same non-thread-safe HidStream
    /// -- restores the last-rendered frame directly afterward rather
    /// than waiting for the render loop's own next tick, since a preset
    /// like typing_reactive would otherwise briefly show a stale frame
    /// from before the test ran.</summary>
    public async Task<bool> TestKeyboardAsync()
    {
        if (_keyboard is null) return false;
        _gate.InProgress = true;
        try
        {
            foreach ((byte r, byte g, byte b) in new (byte, byte, byte)[] { (255, 0, 0), (0, 255, 0), (0, 0, 255) })
            {
                _keyboard.SetStaticColor(r, g, b, 200);
                await Task.Delay(350);
            }
            RgbColor[] lastFrame = _daemon.LastFrame;
            if (lastFrame.Length == KeyboardConstants.NumCells)
            {
                _keyboard.SendFrame(lastFrame);
            }
            return true;
        }
        finally
        {
            _gate.InProgress = false;
        }
    }

    /// <summary>Cycles each of the 3 zones through red/green/blue, one
    /// zone at a time (the other two held off) so each is individually
    /// confirmable, then restores whatever was actually persisted.</summary>
    public async Task<bool> TestLightbarAsync()
    {
        if (!_lightbar.Available) return false;
        var black = new RgbColor(0, 0, 0);
        _lightbar.SetAll(black);
        RgbColor[] colors = { new(255, 0, 0), new(0, 255, 0), new(0, 0, 255) };
        for (int zone = 1; zone <= Lightbar.NumZones; zone++)
        {
            foreach (RgbColor c in colors)
            {
                _lightbar.SetZone(zone, c);
                await Task.Delay(220);
            }
            _lightbar.SetZone(zone, black);
        }
        _lightbar.RestorePersistedState();
        return true;
    }

    private (bool Installed, bool TaskExists, bool TaskEnabled) DetectPythonInstall()
    {
        bool installed = File.Exists(Path.Combine(_pythonRepoRoot, "daemon", "server.py"))
            && File.Exists(Path.Combine(_pythonRepoRoot, "start_all.bat"));
        (bool exists, bool enabled) = QueryScheduledTask(AutostartTaskName);
        return (installed, exists, enabled);
    }

    private static (bool Exists, bool Enabled) QueryScheduledTask(string taskName)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{taskName}\" /FO LIST")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using Process? proc = Process.Start(psi);
            if (proc is null) return (false, false);
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            if (proc.ExitCode != 0) return (false, false);
            bool enabled = !output.Contains("Disabled", StringComparison.OrdinalIgnoreCase);
            return (true, enabled);
        }
        catch
        {
            // schtasks.exe missing/blocked, or some other environment
            // quirk -- report "not detected" rather than throwing, same
            // spirit as every other best-effort hardware/process probe
            // in this file.
            return (false, false);
        }
    }
}
