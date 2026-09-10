// Single-instance enforcement, ported from gui.py's _acquire_single_instance_lock()/
// _focus_existing_instance(): a named Mutex detects an already-running
// instance, and rather than opening a duplicate window, this focuses the
// existing one and exits immediately.

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

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            FocusExistingInstance();
            Environment.Exit(0);
            return;
        }
        base.OnStartup(e);
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
