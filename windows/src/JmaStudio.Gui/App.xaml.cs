// Single-instance enforcement, ported from gui.py's _acquire_single_instance_lock()/
// _focus_existing_instance(): a named Mutex detects an already-running
// instance, and rather than opening a duplicate window, this focuses the
// existing one and exits immediately.
//
// Also owns the tray icon (TrayIconManager) -- see that file's header
// comment for the full design. ShutdownMode is OnExplicitShutdown (set
// in App.xaml) since closing MainWindow now hides it rather than
// exiting the app (see MainWindow's Closing handler) -- the app only
// really exits via the tray's "Close and End Service", which sets
// IsShuttingDown first so MainWindow's Closing handler lets the real
// close through instead of cancelling it.

using System.Runtime.InteropServices;
using System.Windows;

namespace JmaStudio.Gui;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "JmaStudioGui_SingleInstance";
    private const string MainWindowTitle = "JMA Studio";
    private const int SwRestore = 9;

    // Must be kept referenced for the whole process lifetime -- a local
    // variable here would let the GC finalize (and thus release) the
    // underlying OS mutex almost immediately after OnStartup returns,
    // silently defeating the single-instance check for any later launch.
    // Same failure mode gui.py's own _acquire_single_instance_lock()
    // documents hitting and fixing the same way.
    private Mutex? _instanceMutex;
    private TrayIconManager? _trayIcon;
    private KeypressForwarder? _keypressForwarder;
    private IdleActivityMonitor? _idleActivityMonitor;

    /// <summary>True only while the tray's "Close and End Service" is
    /// actually tearing the app down. MainWindow.Closing checks this to
    /// tell a real shutdown-driven close apart from the user clicking
    /// the window's own X button (which should just hide it).</summary>
    public static bool IsShuttingDown { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            FocusExistingInstance();
            Environment.Exit(0);
            return;
        }

        // Safety net for anything Debouncer.cs's fix doesn't already
        // cover (a genuinely unexpected bug, not just a transient
        // Service-unreachable moment). Before the tray icon existed, an
        // unhandled exception crashing the app just meant re-opening a
        // window; now it also silently kills the tray icon the user
        // relies on as their "is the Service still running" signal, so
        // this app should never crash from a routine exception if it can
        // help it. Logged to Debug output rather than shown to the user
        // -- there's no guaranteed window open to show a toast in.
        DispatcherUnhandledException += (_, ex) =>
        {
            System.Diagnostics.Debug.WriteLine($"[UnhandledException] {ex.Exception}");
            ex.Handled = true;
        };

        base.OnStartup(e);
        _trayIcon = new TrayIconManager(new ApiClient());

        // Phase 7 fix (see HANDOFF.md's "Critical finding"): a real
        // installed Windows Service runs in Session 0 and can't see
        // interactive-desktop keystrokes, so this GUI captures the
        // global keyboard hook itself and forwards real keydowns to the
        // Service over POST /keypress. Runs for the whole GUI lifetime,
        // same as the tray icon -- not tied to MainWindow being visible,
        // since the point is for reactive effects to work even while the
        // window is hidden in the tray.
        _keypressForwarder = new KeypressForwarder(new ApiClient());
        _keypressForwarder.Start();

        // Phase 8 (V2) idle screensaver: keyboard/mouse activity source.
        // Same whole-lifetime pattern as the tray icon and keypress
        // forwarder above -- needs to keep pinging even while MainWindow
        // is hidden in the tray, since that's exactly when the
        // screensaver matters most.
        _idleActivityMonitor = new IdleActivityMonitor(new ApiClient());
        _idleActivityMonitor.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _idleActivityMonitor?.Dispose();
        _keypressForwarder?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    private static void FocusExistingInstance()
    {
        // Best effort, matching gui.py's own comment: if the window can't
        // be found for some reason, this just silently does nothing
        // rather than failing the "only one instance" check itself.
        try
        {
            IntPtr hwnd = FindWindow(null, MainWindowTitle);
            if (hwnd == IntPtr.Zero) return;
            if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
            SetForegroundWindow(hwnd);
        }
        catch
        {
            // Ignored -- see comment above.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
