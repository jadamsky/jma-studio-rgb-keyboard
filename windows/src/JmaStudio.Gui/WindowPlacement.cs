using System.Windows;

namespace JmaStudio.Gui;

// Shared window-open behavior for every top-level window in the app.
public static class WindowPlacement
{
    /// <summary>Centered left-to-right, flush against the top of the
    /// screen's usable work area (excludes the taskbar), with Height
    /// clamped to actually fit that work area. A window declared with a
    /// fixed XAML Height (e.g. 900) can exceed the real usable screen
    /// height on a shorter display, which previously opened it partly
    /// off-screen -- the user had to drag it up and shrink it manually
    /// every time. SystemParameters.WorkArea (not the raw screen size
    /// MainWindow's own placement originally used) is what actually
    /// accounts for the taskbar.</summary>
    public static void PlaceTopCentered(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        Rect workArea = SystemParameters.WorkArea;
        if (window.Height > workArea.Height) window.Height = workArea.Height;
        window.Top = workArea.Top;
        window.Left = Math.Max(workArea.Left, workArea.Left + (workArea.Width - window.Width) / 2);
    }

    /// <summary>WPF doesn't automatically reactivate a window's Owner when
    /// an owned window closes -- without this, closing the Lightbar or
    /// Controller Reactive window could leave a completely unrelated
    /// window (e.g. VS Code) as the OS's next-activated window instead of
    /// returning focus to the main window, since the owned window was on
    /// top of it a moment before. Call once, right after the owned
    /// window's constructor sets Owner.</summary>
    public static void ReactivateOwnerOnClose(Window window)
    {
        window.Closed += (_, _) => window.Owner?.Activate();
    }
}
