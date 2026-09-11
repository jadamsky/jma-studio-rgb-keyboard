// Controller Reactive window -- ported from gui/controller_reactive.html.
// Simpler than the Lightbar window: no canvas widgets, just color
// swatches, one slider, and checkboxes, all backed by the Phase 5
// ControllerReactiveManager + its /controller-reactive/* endpoints
// (extended this session with GET /controller-reactive/defaults and a
// "connected" field on /controller-reactive/status, both needed by this
// window but not by anything before it).

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JmaStudio.Effects;
using JmaStudio.Hardware;

namespace JmaStudio.Gui;

public partial class ControllerReactiveWindow : Window
{
    private readonly ApiClient _api = new();
    private readonly bool _uiReady;
    private bool _suppressLiveApply = true;

    private static readonly (string Key, string Label)[] FaceGroup =
        { ("cross", "Cross"), ("square", "Square"), ("circle", "Circle"), ("triangle", "Triangle") };
    private static readonly (string Key, string Label)[] DpadGroup =
        { ("dpad_up", "D-Pad Up"), ("dpad_down", "D-Pad Down"), ("dpad_left", "D-Pad Left"), ("dpad_right", "D-Pad Right") };
    private static readonly (string Key, string Label)[] ShouldersGroup =
        { ("l1", "L1"), ("r1", "R1"), ("l2", "L2 (100%)"), ("r2", "R2 (100%)") };
    private static readonly (string Key, string Label)[] PaddlesGroup =
        { ("left_paddle", "Left Paddle"), ("right_paddle", "Right Paddle"), ("left_fn", "Left Fn"), ("right_fn", "Right Fn") };

    private readonly Dictionary<string, ColorSwatchButton> _groupSwatches = new();
    private Debouncer? _liveUpdateDebounce;

    public ControllerReactiveWindow()
    {
        InitializeComponent();
        _uiReady = true;
        WindowPlacement.PlaceTopCentered(this);
        WindowPlacement.ReactivateOwnerOnClose(this);

        _liveUpdateDebounce = new Debouncer(150, async () => await SendLiveUpdateAsync());

        BuildGroupGrid(FaceGroupPanel, FaceGroup);
        BuildGroupGrid(DpadGroupPanel, DpadGroup);
        BuildGroupGrid(ShouldersGroupPanel, ShouldersGroup);
        BuildGroupGrid(PaddlesGroupPanel, PaddlesGroup);

        // Set here rather than as a XAML IsChecked="True" attribute --
        // see MainWindow.TypingReactive.cs's InitializeTypingReactivePanel
        // for why (a non-default IsChecked with Checked/Unchecked handlers
        // wired in the same file fires those handlers mid-BAML-parse,
        // before this window's own fields all exist yet).
        BgEnabledCheck.IsChecked = true;

        Loaded += ControllerReactiveWindow_Loaded;
    }

    private void BuildGroupGrid(WrapPanel container, (string Key, string Label)[] group)
    {
        foreach ((string key, string label) in group)
        {
            var swatch = new ColorSwatchButton { Color = Color.FromRgb(0, 100, 0) };
            swatch.ColorPicked += _ => FireLiveUpdate();
            var stack = new StackPanel { Margin = new Thickness(0, 0, 20, 8) };
            stack.Children.Add(new TextBlock
            {
                Text = label, FontSize = 11, Foreground = (Brush)FindResource("SubTextBrush"), Margin = new Thickness(0, 0, 0, 4),
            });
            stack.Children.Add(swatch);
            container.Children.Add(stack);
            _groupSwatches[key] = swatch;
        }
    }

    private async void ControllerReactiveWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ControllerReactiveParams? settings = await _api.GetControllerReactiveSettingsAsync();
            ControllerReactiveStatusResponse? status = await _api.GetControllerReactiveStatusAsync();
            if (settings is not null) LoadSettingsIntoUi(settings);
            if (status is not null)
            {
                EnabledCheck.IsChecked = status.Enabled;
                ConnectionStatusText.Text = status.Connected
                    ? "Controller connected"
                    : "No controller detected (USB only for now)";
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Startup error: {ex.Message}");
        }
        finally
        {
            _suppressLiveApply = false;
        }
    }

    private void LoadSettingsIntoUi(ControllerReactiveParams settings)
    {
        BgEnabledCheck.IsChecked = settings.BackgroundEnabled;
        BgSwatch.Color = settings.BackgroundColor.ToMediaColor();
        LeftIdleSwatch.Color = settings.LeftStick.Idle.ToMediaColor();
        LeftTier1Swatch.Color = settings.LeftStick.Tier1.ToMediaColor();
        LeftTier2Swatch.Color = settings.LeftStick.Tier2.ToMediaColor();
        RightIdleSwatch.Color = settings.RightStick.Idle.ToMediaColor();
        RightTier1Swatch.Color = settings.RightStick.Tier1.ToMediaColor();
        RightTier2Swatch.Color = settings.RightStick.Tier2.ToMediaColor();
        int deadzonePct = (int)Math.Round(settings.Deadzone * 100);
        DeadzoneSlider.Value = deadzonePct;
        DeadzoneLabel.Text = $"{deadzonePct}%";
        foreach ((string key, ColorSwatchButton swatch) in _groupSwatches)
        {
            swatch.Color = settings.ButtonColors.GetValueOrDefault(key, new RgbColor(0, 100, 0)).ToMediaColor();
        }
    }

    private ControllerReactiveParams ReadSettings() => new()
    {
        BackgroundEnabled = BgEnabledCheck.IsChecked == true,
        BackgroundColor = BgSwatch.Color.ToRgbColor(),
        LeftStick = new StickColors
        {
            Idle = LeftIdleSwatch.Color.ToRgbColor(), Tier1 = LeftTier1Swatch.Color.ToRgbColor(), Tier2 = LeftTier2Swatch.Color.ToRgbColor(),
        },
        RightStick = new StickColors
        {
            Idle = RightIdleSwatch.Color.ToRgbColor(), Tier1 = RightTier1Swatch.Color.ToRgbColor(), Tier2 = RightTier2Swatch.Color.ToRgbColor(),
        },
        ButtonColors = _groupSwatches.ToDictionary(kv => kv.Key, kv => kv.Value.Color.ToRgbColor()),
        Deadzone = DeadzoneSlider.Value / 100.0,
    };

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => { ToastText.Text = ""; timer.Stop(); };
        timer.Start();
    }

    private void FireLiveUpdate()
    {
        if (_suppressLiveApply) return;
        _liveUpdateDebounce?.Fire();
    }

    private async Task SendLiveUpdateAsync() => await _api.SetControllerReactiveSettingsAsync(ReadSettings());

    private void LiveOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady) return;
        FireLiveUpdate();
    }

    private void DeadzoneSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady) return;
        DeadzoneLabel.Text = $"{(int)DeadzoneSlider.Value}%";
        FireLiveUpdate();
    }

    private async void EnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressLiveApply) return;
        if (EnabledCheck.IsChecked == true)
        {
            bool ok = await _api.EnableControllerReactiveAsync();
            if (!ok)
            {
                _suppressLiveApply = true;
                EnabledCheck.IsChecked = false;
                _suppressLiveApply = false;
                ShowToast("Failed to enable");
            }
        }
        else
        {
            await _api.DisableControllerReactiveAsync();
        }
    }

    private async void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        bool ok = await _api.SaveControllerReactiveSettingsAsync();
        ShowToast(ok ? "Settings saved" : "Failed to save");
    }

    private async void DefaultBtn_Click(object sender, RoutedEventArgs e)
    {
        ControllerReactiveDefaultsResponse? defaults = await _api.GetControllerReactiveDefaultsAsync();
        if (defaults is null)
        {
            ShowToast("Failed to load defaults");
            return;
        }
        _suppressLiveApply = true;
        try
        {
            BgEnabledCheck.IsChecked = defaults.BackgroundEnabled;
            BgSwatch.Color = defaults.BackgroundColor.ToMediaColor();
            Color groupColor = defaults.GroupColor.ToMediaColor();
            LeftIdleSwatch.Color = groupColor;
            LeftTier1Swatch.Color = groupColor;
            LeftTier2Swatch.Color = groupColor;
            RightIdleSwatch.Color = groupColor;
            RightTier1Swatch.Color = groupColor;
            RightTier2Swatch.Color = groupColor;
            int deadzonePct = (int)Math.Round(defaults.Deadzone * 100);
            DeadzoneSlider.Value = deadzonePct;
            DeadzoneLabel.Text = $"{deadzonePct}%";
            foreach (ColorSwatchButton swatch in _groupSwatches.Values) swatch.Color = groupColor;
        }
        finally
        {
            _suppressLiveApply = false;
        }
        await SendLiveUpdateAsync();
        ShowToast("Reset to default -- click Save to keep it");
    }

    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        ScrollBehavior.HandleMouseWheel(MainScrollViewer, e);
}
