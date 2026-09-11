// Diagnostics window (windows/HANDOFF.md settled decision #11): the
// emergency-action row, live hardware/service health tiles, a mini
// preview, perf stats, and a logs panel. Self-tests + the live
// controller viewer are in DiagnosticsWindow.SelfTest.cs.
//
// The 3 emergency buttons launch separate elevated `dotnet run`
// processes (JmaStudio.HardwareTest's reassert-dominance/switch-to-
// python/switch-to-csharp commands) rather than calling the Service's
// own HTTP API -- see JmaStudio.Service/DiagnosticsManager.cs's header
// comment for why (in short: each click needs its own real UAC prompt,
// and switch-to-python needs to end by killing the very Service process
// that would otherwise be handling the HTTP request).

using System.Diagnostics;
using System.IO;
using System.Management;
// System.Windows.Shapes also declares a Path type (a shape) -- alias to
// disambiguate every System.IO.Path call below.
using IOPath = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using JmaStudio.Hardware;

namespace JmaStudio.Gui;

public partial class DiagnosticsWindow : Window
{
    private readonly ApiClient _api = new();
    private readonly DispatcherTimer _statusPollTimer;
    private readonly DispatcherTimer _previewPollTimer;
    private bool _statusInFlight;
    private bool _previewInFlight;
    private LayoutResponse? _layout;
    private readonly Dictionary<int, Rectangle> _miniCells = new();

    public DiagnosticsWindow()
    {
        InitializeComponent();
        WindowPlacement.PlaceTopCentered(this);
        WindowPlacement.ReactivateOwnerOnClose(this);

        _statusPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _statusPollTimer.Tick += async (_, _) => await PollStatusAsync();
        // Faster than the status tick -- this is what makes the mini
        // preview actually look "live" rather than a slideshow, same
        // reasoning as MainWindow's own separate frame/status timers.
        _previewPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _previewPollTimer.Tick += async (_, _) => await PollPreviewAsync();

        Loaded += DiagnosticsWindow_Loaded;
        Closed += DiagnosticsWindow_Closed;
    }

    private async void DiagnosticsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _layout = await _api.GetLayoutAsync();
            BuildMiniKeyboardCanvas();
        }
        catch
        {
            // Mini preview is a bonus, not core functionality -- the rest
            // of the window still works without a keyboard layout.
        }

        await PollStatusAsync();
        _statusPollTimer.Start();
        _previewPollTimer.Start();
    }

    private void DiagnosticsWindow_Closed(object? sender, EventArgs e)
    {
        _statusPollTimer.Stop();
        _previewPollTimer.Stop();
        StopControllerLiveTimer();
    }

    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        ScrollBehavior.HandleMouseWheel(MainScrollViewer, e);

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await PollStatusAsync();

    // ---- status polling ----

    private async Task PollStatusAsync()
    {
        if (_statusInFlight) return;
        _statusInFlight = true;
        try
        {
            DiagnosticsStatusResponse? status = await _api.GetDiagnosticsStatusAsync();
            // Win32_Process enumeration can take a couple hundred ms on a
            // busy machine -- off the UI thread so a 2s poll tick can't
            // cause a visible micro-freeze while this window is open.
            string activeStack = await Task.Run(DetectActiveStackLocally);
            UpdateUi(status, activeStack);

            LogsResponse? logs = await _api.GetLogsAsync();
            if (logs is not null) LogText.Text = string.Join(Environment.NewLine, logs.Lines);
        }
        catch (Exception ex)
        {
            ToastText.Text = $"Service unreachable: {ex.Message}";
            UpdateUi(null, await Task.Run(DetectActiveStackLocally));
        }
        finally
        {
            _statusInFlight = false;
        }
    }

    private void UpdateUi(DiagnosticsStatusResponse? status, string activeStack)
    {
        if (status is not null)
        {
            UptimeText.Text = $"Service uptime: {FormatUptime(status.UptimeSeconds)}";

            KbStatusDot.Fill = status.Keyboard.Connected ? Brushes.LimeGreen : status.Keyboard.Detected ? Brushes.Orange : Brushes.OrangeRed;
            KbDetailText.Text = $"VID {status.Keyboard.VendorId:X4} / PID {status.Keyboard.ProductId:X4} -- " +
                (status.Keyboard.Connected ? "open and driving the keyboard." : status.Keyboard.Detected ? "detected, but not currently open." : "not detected.");

            LightbarState? lbState = status.Lightbar.State;
            LbStatusDot.Fill = status.Lightbar.Connected ? Brushes.LimeGreen : status.Lightbar.Detected ? Brushes.Orange : Brushes.OrangeRed;
            LbDetailText.Text = status.Lightbar.Connected
                ? (lbState?.Mode is not null ? $"Mode: {lbState.Mode}, brightness {lbState.Brightness}%" : $"Static color, brightness {lbState?.Brightness ?? 0}%")
                : status.Lightbar.Detected ? "Detected, but not currently open." : "Not detected.";
            UpdateLightbarZoneSwatches(lbState);

            CtrlStatusDot.Fill = status.Controller.Connected ? Brushes.LimeGreen : status.Controller.Detected ? Brushes.Orange : Brushes.Gray;
            CtrlDetailText.Text = status.Controller.Connected ? "Connected and streaming." :
                status.Controller.Detected ? "Detected, but not streaming yet." : "No DualSense found over USB.";

            FramesText.Text = $"{status.Perf.FramesRendered:N0} / {status.Perf.FramesWritten:N0}";
            HidLatencyText.Text = FormatLatency(status.Perf.HidLatencyMs);
            WmiLatencyText.Text = FormatLatency(status.Perf.WmiLatencyMs);

            bool acerHealthy = status.AcerLightingService.Status is "Stopped" or "NotFound";
            AcerServiceText.Text = $"{status.AcerLightingService.Status} / start mode: {status.AcerLightingService.StartMode}";
            AcerServiceText.Foreground = acerHealthy ? (Brush)FindResource("SubTextBrush") : Brushes.OrangeRed;

            SuspiciousProcessesText.Text = status.SuspiciousProcesses.Length == 0 ? "None detected" : string.Join(", ", status.SuspiciousProcesses);
            SuspiciousProcessesText.Foreground = status.SuspiciousProcesses.Length == 0 ? (Brush)FindResource("SubTextBrush") : Brushes.OrangeRed;

            UpdateEmergencyGating(status.Python.Installed, activeStack);
        }
        else
        {
            UptimeText.Text = "Service unreachable";
            KbStatusDot.Fill = LbStatusDot.Fill = CtrlStatusDot.Fill = Brushes.Gray;
            KbDetailText.Text = LbDetailText.Text = CtrlDetailText.Text = "Service unreachable.";
            FramesText.Text = HidLatencyText.Text = WmiLatencyText.Text = "--";
            AcerServiceText.Text = "--";
            SuspiciousProcessesText.Text = "--";
            UpdateLightbarZoneSwatches(null);
            UpdateEmergencyGating(DetectPythonInstalledLocally(), activeStack);
        }
    }

    private void UpdateLightbarZoneSwatches(LightbarState? state)
    {
        Rectangle[] rects = { LbZone1Rect, LbZone2Rect, LbZone3Rect };
        for (int i = 0; i < rects.Length; i++)
        {
            int zone = i + 1;
            rects[i].Fill = state?.ZoneColors is not null && state.ZoneColors.TryGetValue(zone, out RgbColor c)
                ? new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B))
                : new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        }
    }

    private void UpdateEmergencyGating(bool pythonInstalled, string activeStack)
    {
        // "Only the button for the inactive stack should be enabled at a
        // time" -- settled decision #11. Re-assert dominance is always
        // enabled (idempotent/standalone-safe regardless of which stack
        // is active, see JmaStudio.HardwareTest's reassert-dominance).
        SwitchToPythonBtn.IsEnabled = pythonInstalled && activeStack != "python";
        SwitchToPythonBtn.ToolTip = !pythonInstalled
            ? "Python version not detected on this machine."
            : activeStack == "python"
                ? "Python already appears to be the active stack."
                : "Kills the C# GUI/Service, re-enables Python's autostart task, and launches start_all.bat.";

        SwitchToCSharpBtn.IsEnabled = activeStack != "csharp";
        SwitchToCSharpBtn.ToolTip = activeStack == "csharp"
            ? "C# already appears to be the active stack."
            : "Kills the Python daemon/tray/GUI, disables its autostart task, and launches the C# Service+GUI.";

        StackStatusText.Text = (activeStack switch
        {
            "csharp" => "Currently active: C# (this Service).",
            "python" => "Currently active: Python.",
            _ => "Could not determine which stack is currently active.",
        }) + (pythonInstalled ? "" : " Python version not detected on this machine.");
    }

    private static string FormatUptime(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalDays >= 1 ? $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m" : $"{span.Hours}h {span.Minutes}m {span.Seconds}s";
    }

    private static string FormatLatency(LatencySnapshot s) =>
        s.Count == 0 ? "no data yet" : $"{s.Min:F1} / {s.Avg:F1} / {s.Max:F1} ms ({s.Count} calls)";

    // Independent of the Service's own reachability -- this is what lets
    // the emergency-action gating make sense even when the Service is
    // down (Python may currently be the one actually running).
    private static string DetectActiveStackLocally()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process");
            bool csharp = false, python = false;
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                if (obj["CommandLine"] is not string cmd) continue;
                if (cmd.Contains("JmaStudio.Service", StringComparison.OrdinalIgnoreCase)) csharp = true;
                else if (cmd.Contains("daemon.server", StringComparison.OrdinalIgnoreCase) || cmd.Contains("uvicorn", StringComparison.OrdinalIgnoreCase)) python = true;
            }
            if (csharp) return "csharp";
            if (python) return "python";
            return "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    // Same resolution DefaultPythonRoot() uses in JmaStudio.HardwareTest
    // -- one level up from the current directory, which every documented
    // invocation of this GUI assumes is `windows/`. Only used as a
    // fallback when the Service (which has its own, Service-side
    // detection) is unreachable.
    private static bool DetectPythonInstalledLocally()
    {
        string root = IOPath.GetFullPath(IOPath.Combine(Directory.GetCurrentDirectory(), ".."));
        return File.Exists(IOPath.Combine(root, "daemon", "server.py")) && File.Exists(IOPath.Combine(root, "start_all.bat"));
    }

    // ---- mini live preview ----

    private void BuildMiniKeyboardCanvas()
    {
        if (_layout is null || _layout.Cells.Length == 0) return;
        MiniKeyboardCanvas.Children.Clear();
        _miniCells.Clear();

        double maxCol = _layout.Cells.Max(c => c.Col);
        double maxRow = _layout.Cells.Max(c => c.Row);
        double availableWidth = Math.Max(200, MiniPreviewHost.ActualWidth - 20);
        double unit = Math.Max(4, Math.Min(10, availableWidth / (maxCol + 2)));

        MiniKeyboardCanvas.Width = (maxCol + 2) * unit;
        MiniKeyboardCanvas.Height = (maxRow + 1.4) * unit;

        foreach (LayoutCell cell in _layout.Cells)
        {
            var rect = new Rectangle
            {
                Width = Math.Max(1, unit - 1),
                Height = Math.Max(1, unit - 1),
                Fill = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x26)),
                RadiusX = 1,
                RadiusY = 1,
            };
            Canvas.SetLeft(rect, cell.Col * unit);
            Canvas.SetTop(rect, cell.Row * unit);
            MiniKeyboardCanvas.Children.Add(rect);
            _miniCells[cell.Index] = rect;
        }
    }

    private void MiniPreviewHost_SizeChanged(object sender, SizeChangedEventArgs e) => BuildMiniKeyboardCanvas();

    private async Task PollPreviewAsync()
    {
        if (_previewInFlight) return;
        _previewInFlight = true;
        try
        {
            FrameResponse? frame = await _api.GetFrameAsync();
            if (frame is not null)
            {
                for (int i = 0; i < frame.Colors.Length; i++)
                {
                    if (_miniCells.TryGetValue(i, out Rectangle? rect))
                    {
                        RgbColor c = frame.Colors[i];
                        rect.Fill = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
                    }
                }
            }
        }
        catch
        {
            // Transient -- the next 100ms tick will retry.
        }
        finally
        {
            _previewInFlight = false;
        }
    }

    // ---- emergency actions ----

    private void LaunchElevatedHardwareTestCommand(string command)
    {
        string windowsRoot = Directory.GetCurrentDirectory();
        string hardwareTestProject = IOPath.Combine(windowsRoot, "src", "JmaStudio.HardwareTest");
        try
        {
            var psi = new ProcessStartInfo("dotnet", $"run --project \"{hardwareTestProject}\" -- {command}")
            {
                WorkingDirectory = windowsRoot,
                UseShellExecute = true,
                Verb = "runas",
            };
            Process.Start(psi);
            ToastText.Text = $"Launched '{command}' (elevated) -- check the console window it opened.";
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED -- the user declined the UAC prompt.
            ToastText.Text = "Cancelled -- the UAC prompt was declined.";
        }
        catch (Exception ex)
        {
            ToastText.Text = $"Failed to launch '{command}': {ex.Message}";
        }
    }

    private void ReassertBtn_Click(object sender, RoutedEventArgs e) => LaunchElevatedHardwareTestCommand("reassert-dominance");

    private void SwitchToPythonBtn_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "This closes the C# GUI and Service and starts the Python version instead. Continue?",
                "Switch to Python Version", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        LaunchElevatedHardwareTestCommand("switch-to-python");
    }

    private void SwitchToCSharpBtn_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "This closes the Python daemon/tray/GUI and starts the C# version instead. Continue?",
                "Switch to C# Version", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        LaunchElevatedHardwareTestCommand("switch-to-csharp");
    }

    // ---- logs ----

    private async void OpenLogsFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DiagnosticsStatusResponse? status = await _api.GetDiagnosticsStatusAsync();
            string? folder = status is not null ? IOPath.GetDirectoryName(status.LogFilePath) : null;
            if (folder is not null && Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
            }
            else
            {
                ToastText.Text = "Could not determine the logs folder (service unreachable?).";
            }
        }
        catch (Exception ex)
        {
            ToastText.Text = $"Could not open logs folder: {ex.Message}";
        }
    }

    private async void CopyReportBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DiagnosticsStatusResponse? status = await _api.GetDiagnosticsStatusAsync();
            LogsResponse? logs = await _api.GetLogsAsync(50);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== JMA Studio diagnostics report ===");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            if (status is not null)
            {
                sb.AppendLine($"Service uptime: {FormatUptime(status.UptimeSeconds)}");
                sb.AppendLine($"Keyboard: connected={status.Keyboard.Connected} detected={status.Keyboard.Detected}");
                sb.AppendLine($"Lightbar: connected={status.Lightbar.Connected} detected={status.Lightbar.Detected}");
                sb.AppendLine($"Controller: connected={status.Controller.Connected} detected={status.Controller.Detected}");
                sb.AppendLine($"Frames rendered/written: {status.Perf.FramesRendered}/{status.Perf.FramesWritten}");
                sb.AppendLine($"HID latency: {FormatLatency(status.Perf.HidLatencyMs)}");
                sb.AppendLine($"WMI latency: {FormatLatency(status.Perf.WmiLatencyMs)}");
                sb.AppendLine($"AcerLightingService: {status.AcerLightingService.Status} / {status.AcerLightingService.StartMode}");
                sb.AppendLine($"Suspicious processes: {(status.SuspiciousProcesses.Length == 0 ? "none" : string.Join(", ", status.SuspiciousProcesses))}");
            }
            else
            {
                sb.AppendLine("Service unreachable.");
            }
            if (logs is not null)
            {
                sb.AppendLine();
                sb.AppendLine("--- last 50 log lines ---");
                foreach (string line in logs.Lines) sb.AppendLine(line);
            }
            Clipboard.SetText(sb.ToString());
            ToastText.Text = "Diagnostics report copied to clipboard.";
        }
        catch (Exception ex)
        {
            ToastText.Text = $"Could not build report: {ex.Message}";
        }
    }
}
