// Self-test buttons (test keyboard/lightbar, re-scan hardware) and the
// live controller viewer -- split out of DiagnosticsWindow.xaml.cs for
// organization, same pattern as LightbarWindow's .Presets.cs/.Reactive.cs
// partials.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using JmaStudio.Hardware;

namespace JmaStudio.Gui;

public partial class DiagnosticsWindow
{
    private async void TestKeyboardBtn_Click(object sender, RoutedEventArgs e)
    {
        TestKeyboardBtn.IsEnabled = false;
        try
        {
            bool ok = await _api.TestKeyboardAsync();
            ToastText.Text = ok ? "Keyboard test complete." : "Keyboard not available.";
        }
        catch (Exception ex)
        {
            ToastText.Text = $"Keyboard test failed: {ex.Message}";
        }
        finally
        {
            TestKeyboardBtn.IsEnabled = true;
        }
    }

    private async void TestLightbarBtn_Click(object sender, RoutedEventArgs e)
    {
        TestLightbarBtn.IsEnabled = false;
        try
        {
            bool ok = await _api.TestLightbarAsync();
            ToastText.Text = ok ? "Lightbar test complete." : "Lightbar not available.";
        }
        catch (Exception ex)
        {
            ToastText.Text = $"Lightbar test failed: {ex.Message}";
        }
        finally
        {
            TestLightbarBtn.IsEnabled = true;
        }
    }

    private async void RescanBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RescanResponse? result = await _api.RescanAsync();
            ToastText.Text = result is null
                ? "Rescan failed."
                : $"Detected now -- keyboard: {result.KeyboardDetected}, lightbar: {result.LightbarDetected}, controller: {result.ControllerDetected} (connected: {result.ControllerConnected})";
        }
        catch (Exception ex)
        {
            ToastText.Text = $"Rescan failed: {ex.Message}";
        }
    }

    // ---- live controller viewer ----

    private static readonly (string Label, Func<ControllerState, bool> IsPressed)[] ControllerButtonDefs =
    {
        ("Square", s => s.Square), ("Cross", s => s.Cross), ("Circle", s => s.Circle), ("Triangle", s => s.Triangle),
        ("L1", s => s.L1), ("R1", s => s.R1),
        ("D-Up", s => s.DpadUp), ("D-Right", s => s.DpadRight), ("D-Down", s => s.DpadDown), ("D-Left", s => s.DpadLeft),
        ("L-Fn", s => s.LeftFn), ("R-Fn", s => s.RightFn), ("L-Paddle", s => s.LeftPaddle), ("R-Paddle", s => s.RightPaddle),
    };

    private readonly Dictionary<string, Border> _controllerButtonBorders = new();
    private DispatcherTimer? _controllerLiveTimer;
    private bool _controllerPollInFlight;

    private static readonly SolidColorBrush ButtonIdleBrush = new(Color.FromRgb(0x22, 0x22, 0x2E));

    private void BuildControllerButtons()
    {
        ControllerButtonsPanel.Children.Clear();
        _controllerButtonBorders.Clear();
        foreach ((string label, _) in ControllerButtonDefs)
        {
            var border = new Border
            {
                Background = ButtonIdleBrush,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(3),
                Child = new TextBlock { Text = label, FontSize = 11 },
            };
            _controllerButtonBorders[label] = border;
            ControllerButtonsPanel.Children.Add(border);
        }
    }

    private void ToggleControllerViewerBtn_Click(object sender, RoutedEventArgs e)
    {
        bool showing = ControllerViewerHost.Visibility == Visibility.Visible;
        if (showing)
        {
            ControllerViewerHost.Visibility = Visibility.Collapsed;
            StopControllerLiveTimer();
            ToggleControllerViewerBtn.Content = "Test controller (live viewer)";
        }
        else
        {
            if (_controllerButtonBorders.Count == 0) BuildControllerButtons();
            ControllerViewerHost.Visibility = Visibility.Visible;
            ToggleControllerViewerBtn.Content = "Hide controller viewer";
            StartControllerLiveTimer();
        }
    }

    private void StartControllerLiveTimer()
    {
        if (_controllerLiveTimer is null)
        {
            _controllerLiveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            _controllerLiveTimer.Tick += ControllerLiveTimer_Tick;
        }
        _controllerLiveTimer.Start();
    }

    private void StopControllerLiveTimer() => _controllerLiveTimer?.Stop();

    private async void ControllerLiveTimer_Tick(object? sender, EventArgs e)
    {
        if (_controllerPollInFlight) return;
        _controllerPollInFlight = true;
        try
        {
            ControllerLiveResponse? live = await _api.GetControllerLiveAsync();
            if (live?.State is { } s)
            {
                PositionStickDot(LeftStickCanvas, LeftStickDot, s.LeftX, s.LeftY);
                PositionStickDot(RightStickCanvas, RightStickDot, s.RightX, s.RightY);
                ControllerTriggersText.Text = $"L2: {s.LeftTrigger:P0}  R2: {s.RightTrigger:P0}";
                foreach ((string label, Func<ControllerState, bool> isPressed) in ControllerButtonDefs)
                {
                    if (_controllerButtonBorders.TryGetValue(label, out Border? b))
                    {
                        b.Background = isPressed(s) ? (Brush)FindResource("AccentABrush") : ButtonIdleBrush;
                    }
                }
            }
        }
        catch
        {
            // Transient -- the next tick will retry.
        }
        finally
        {
            _controllerPollInFlight = false;
        }
    }

    private static void PositionStickDot(Canvas canvas, Ellipse dot, double x, double y)
    {
        double cx = (canvas.Width - dot.Width) / 2 * (1 + Math.Clamp(x, -1.0, 1.0));
        double cy = (canvas.Height - dot.Height) / 2 * (1 + Math.Clamp(y, -1.0, 1.0));
        Canvas.SetLeft(dot, cx);
        Canvas.SetTop(dot, cy);
    }
}
