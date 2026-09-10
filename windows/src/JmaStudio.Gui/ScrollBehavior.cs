using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace JmaStudio.Gui;

// Shared by every scrollable window in the app. Precision-touchpad
// two-finger scroll drivers report a much larger per-event wheel delta
// than a physical mouse wheel's fixed 120-per-notch (scaled to swipe
// speed, not quantized), and WPF's default ScrollViewer wheel handling
// scales scroll distance directly by that delta, turning a fast swipe
// into a huge jump. A real wheel notch (delta exactly 120) isn't
// affected by this at all -- only touchpad's inflated values are.
// Clamping the number of "lines" scrolled per event to a normal single
// notch's worth tames the touchpad case while leaving real wheel
// scrolling unchanged. Originally lived only in MainWindow.xaml.cs;
// extracted here once the Lightbar window needed the identical fix.
public static class ScrollBehavior
{
    private const double LineHeight = 16.0; // WPF's default line-scroll unit
    private const double MaxLinesPerEvent = 3.0; // matches SystemParameters.WheelScrollLines' usual default

    public static void HandleMouseWheel(ScrollViewer scrollViewer, MouseWheelEventArgs e)
    {
        e.Handled = true;
        double rawLines = e.Delta / 120.0 * SystemParameters.WheelScrollLines;
        double clampedLines = Math.Clamp(rawLines, -MaxLinesPerEvent, MaxLinesPerEvent);
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - clampedLines * LineHeight);
    }
}
