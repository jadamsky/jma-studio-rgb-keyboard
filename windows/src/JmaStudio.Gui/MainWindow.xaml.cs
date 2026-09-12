// Main WPF window: live keyboard preview, presets (list/apply/delete/
// save/set-default), a quick-effects grid, and the Gradient/Reactive
// Typing/Custom Key Colors tuning panels -- the core of gui/index.html's
// UX. Opens the Lightbar/Controller Reactive/Diagnostics windows as
// separate owned windows (see windows/HANDOFF.md's Phase 6 sections).

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JmaStudio.Presets;

namespace JmaStudio.Gui;

public partial class MainWindow : Window
{
    private readonly ApiClient _api = new();
    private readonly Dictionary<int, Border> _cellBorders = new();
    // Two independent timers, not one shared 150ms tick: the render loop
    // (RenderLoopService) writes a new frame ~30fps (every ~33ms), so a
    // single slower poll was the main reason the GUI looked laggy/behind
    // real keystrokes -- it physically could not show more than ~6-7fps.
    // Status (current effect name, connection dot, frame counters) only
    // changes when the user picks a new effect/preset, so it's polled far
    // less often and, critically, on its OWN timer -- previously it was
    // awaited sequentially before the frame fetch every single tick,
    // doubling the round-trip latency in front of every color update.
    private readonly DispatcherTimer _framePollTimer;
    private readonly DispatcherTimer _statusPollTimer;
    // Slow poll (these only change when the user toggles a feature, not
    // every frame) for the top-bar buttons' green-checkmark indicators --
    // deliberately its own timer, not folded into the 750ms status poll
    // above, matching this app's own established pattern (see
    // KeypressForwarder's 3s gating poll) of keeping infrequent-change
    // checks off the more frequent polling loops.
    private readonly DispatcherTimer _featureStatusPollTimer;
    private bool _frameInFlight;
    private bool _statusInFlight;
    private LayoutResponse? _layout;
    private string? _activePresetName;

    // True only during InitializeAsync's panel setup. The Gradient/Reactive
    // Typing/Custom Key Colors panels' live-apply is debounced ~120ms out,
    // so merely checking a flag inside the eventual apply is too late --
    // by the time the timer fires, startup has already finished and the
    // flag would read false again, letting a stale default (e.g. a blank
    // 2-zone gradient) silently overwrite whatever's actually live on the
    // keyboard a moment after the window opens. Each panel's Fire*Live()
    // method checks this BEFORE starting its debounce timer, so nothing
    // gets scheduled in the first place while this is true.
    private bool _suppressLiveApply = true;

    // False until InitializeComponent() returns. WPF can fire a XAML-wired
    // event handler DURING BAML parsing itself -- not just from an explicit
    // non-default attribute like IsChecked="True" (which fires Checked
    // immediately), but also as a side effect of property coercion (e.g.
    // setting a Slider's Minimum coerces its still-default Value into
    // range, firing ValueChanged) -- before every named element later in
    // the same XAML file has been assigned to its field yet. Every
    // XAML-wired handler in the tuning panels checks this first and
    // no-ops if the UI isn't fully built yet; hit this live twice (once
    // via a CheckBox, once via a Slider) before adding the guard.
    private readonly bool _uiReady;

    // Same exclusion set as gui/app.js's HIDDEN_FROM_CHIPS: these effects
    // are either diagnostic-only (probe/mask), meaningless with their
    // bare defaults (custom_keys defaults to an empty color map + black
    // fallback -- clicking it "applies" an all-off keyboard, which is
    // exactly the "keyboard went dark" bug this fixes; static/gradient
    // are just a flat/blank color with nothing configured), or need
    // dedicated tuning UI not yet built (typing_reactive, gradient).
    // controller_reactive is a C#-only addition (not in Python's set)
    // excluded for the same reason: it has its own enable/disable
    // lifecycle (ControllerReactiveManager) and must not be poked via a
    // raw one-click /effect apply that bypasses that stash/restore logic.
    private static readonly HashSet<string> HiddenFromQuickEffects = new(StringComparer.OrdinalIgnoreCase)
    {
        "probe", "mask", "gradient", "typing_reactive", "static", "custom_keys", "controller_reactive",
    };

    public MainWindow()
    {
        InitializeComponent();
        _uiReady = true; // every named element now exists; see the field's comment

        // Centered left-to-right, flush against the top of the screen's
        // usable work area -- matches gui.py's _initial_position() (its
        // own comment there: the OS/toolkit default placement left the
        // window too low, needing a manual drag up every time it opened).
        WindowPlacement.PlaceTopCentered(this);
        _framePollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _framePollTimer.Tick += async (_, _) => await PollFrameAsync();
        _statusPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _statusPollTimer.Tick += async (_, _) => await PollStatusAsync();
        _featureStatusPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _featureStatusPollTimer.Tick += async (_, _) => await PollFeatureStatusAsync();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    // The tray icon is now the thing that keeps running when this window
    // isn't open (see TrayIconManager.cs) -- the X button minimizes to
    // tray rather than exiting, matching the user's explicit design
    // (2026-09-10). Only the tray's "Close and End Service" should
    // actually end the process, which sets App.IsShuttingDown first so
    // this lets that real close through instead of cancelling it.
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (App.IsShuttingDown) return;
        e.Cancel = true;
        Hide();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await InitializeAsync();
        _framePollTimer.Start();
        _statusPollTimer.Start();
        _featureStatusPollTimer.Start();
        await PollFeatureStatusAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _layout = await _api.GetLayoutAsync();
            BuildKeyboardCanvas();

            string[] effects = await _api.GetEffectsAsync();
            EffectsList.ItemsSource = effects
                .Where(name => !HiddenFromQuickEffects.Contains(name))
                .Select(name => new EffectChip(name, name.Replace('_', ' ')))
                .ToArray();

            await RefreshPresetsAsync();

            InitializeGradientPanel();
            InitializeTypingReactivePanel();
            InitializeCustomKeysPanel();
            BuildCustomKeysCanvas();
            await InitializeBatteryOverridePanelAsync();
            _suppressLiveApply = false;

            await PollStatusAsync();
            await PollFrameAsync();
        }
        catch (Exception ex)
        {
            ShowToast($"Startup error: {ex.Message}");
        }
    }

    private static readonly SolidColorBrush KeyBaseBrush = new(Color.FromRgb(0x1C, 0x1C, 0x26));
    private static readonly SolidColorBrush KeyBorderBrush = new(Color.FromArgb(20, 255, 255, 255));
    private static readonly SolidColorBrush KeyLabelBrush = new(Color.FromArgb(140, 255, 255, 255));

    // Ported from gui/app.js's buildKeyboardGrid(): scales the whole
    // board to fit the panel's available width (clamped to a sane
    // per-key pixel range) rather than a fixed px-per-unit, and gives
    // each key its real width/height/label instead of a uniform square
    // -- the "layout sucks" feedback was about exactly this gap versus
    // the Python reference. Shared by the live-preview board (this method)
    // and the Custom Key Colors editor grid (MainWindow.CustomKeys.cs's
    // BuildCustomKeysCanvas) via BuildKeyGrid below -- both lay out the
    // same physical key positions, just with different per-cell behavior.
    private void BuildKeyboardCanvas() => BuildKeyGrid(KeyboardCanvas, PreviewBoardHost, _cellBorders, null);

    private void BuildKeyGrid(Canvas canvas, Border host, Dictionary<int, Border> cellMap, Action<Border, LayoutCell>? decorate)
    {
        if (_layout is null || _layout.Cells.Length == 0) return;
        canvas.Children.Clear();
        cellMap.Clear();

        double maxCol = _layout.Cells.Max(c => c.Col);
        double maxRow = _layout.Cells.Max(c => c.Row);
        double totalCols = maxCol + 3;
        double totalRows = maxRow + 1.4;

        double availableWidth = host.ActualWidth - 40;
        if (availableWidth <= 0) availableWidth = 900;
        double unit = Math.Max(16, Math.Min(34, availableWidth / totalCols));

        canvas.Width = totalCols * unit;
        canvas.Height = totalRows * unit;

        foreach (LayoutCell cell in _layout.Cells)
        {
            double widthUnits = KeyboardKeyStyle.KeyWidth.GetValueOrDefault(cell.Name, 1);
            double heightUnits = KeyboardKeyStyle.KeyHeight.GetValueOrDefault(cell.Name, 1);
            var border = new Border
            {
                Width = Math.Max(1, widthUnits * unit - 4),
                Height = Math.Max(1, heightUnits * unit - 4),
                CornerRadius = new CornerRadius(5),
                Background = KeyBaseBrush,
                BorderBrush = KeyBorderBrush,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = KeyboardKeyStyle.FriendlyLabel(cell.Name),
                    Foreground = KeyLabelBrush,
                    FontSize = Math.Max(7, Math.Min(11, unit * 0.28)),
                    FontWeight = FontWeights.Medium,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            };
            Canvas.SetLeft(border, cell.Col * unit);
            Canvas.SetTop(border, cell.Row * unit);
            canvas.Children.Add(border);
            cellMap[cell.Index] = border;
            decorate?.Invoke(border, cell);
        }
    }

    private void PreviewBoardHost_SizeChanged(object sender, SizeChangedEventArgs e) => BuildKeyboardCanvas();

    // See ScrollBehavior.cs for why this is needed (touchpad two-finger
    // scroll was reported "really fast" -- WPF's default ScrollViewer
    // wheel handling scales scroll distance directly by wheel delta,
    // which touchpad drivers report far larger than a physical notch).
    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        ScrollBehavior.HandleMouseWheel(MainScrollViewer, e);

    private async Task PollFrameAsync()
    {
        // Guard against overlapping calls: if the service is briefly slow
        // and a request is still in flight when the next 33ms tick fires,
        // skip this tick rather than piling up concurrent HTTP requests.
        if (_frameInFlight) return;
        _frameInFlight = true;
        try
        {
            FrameResponse? frame = await _api.GetFrameAsync();
            if (frame is not null)
            {
                for (int i = 0; i < frame.Colors.Length; i++)
                {
                    if (_cellBorders.TryGetValue(i, out Border? cellBorder))
                    {
                        var c = frame.Colors[i];
                        cellBorder.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ReportUnreachable(ex);
        }
        finally
        {
            _frameInFlight = false;
        }
    }

    private async Task PollStatusAsync()
    {
        if (_statusInFlight) return;
        _statusInFlight = true;
        try
        {
            StatusResponse? status = await _api.GetStatusAsync();
            if (status is not null)
            {
                CurrentEffectLabel.Text = status.CurrentEffect;
                HwStatusDot.Fill = status.KeyboardConnected ? Brushes.LimeGreen : Brushes.OrangeRed;
                HwStatusLabel.Text = status.KeyboardConnected ? "connected" : "no keyboard";
            }
        }
        catch (Exception ex)
        {
            ReportUnreachable(ex);
        }
        finally
        {
            _statusInFlight = false;
        }
    }

    /// <summary>Green checkmark overlays on the Controller Reactive and
    /// Screensaver top-bar buttons, indicating each is currently enabled --
    /// requested live after the user built the screensaver window and
    /// wanted an at-a-glance signal without opening either window.</summary>
    private async Task PollFeatureStatusAsync()
    {
        try
        {
            ControllerReactiveStatusResponse? crStatus = await _api.GetControllerReactiveStatusAsync();
            ControllerReactiveEnabledCheck.Visibility = crStatus?.Enabled == true ? Visibility.Visible : Visibility.Collapsed;
        }
        catch { /* leave whatever was last shown -- a transient failure here isn't worth a toast */ }

        try
        {
            IdleScreensaverConfig? screensaverConfig = await _api.GetIdleScreensaverConfigAsync();
            IdleScreensaverEnabledCheck.Visibility = screensaverConfig?.Enabled == true ? Visibility.Visible : Visibility.Collapsed;
        }
        catch { }

        try
        {
            BatteryStatusResponse? battery = await _api.GetBatteryStatusAsync();
            BatteryStatusText.Text = battery is not { HasBattery: true }
                ? "Battery: not detected"
                : $"Battery: {battery.Percent}% -- {(battery.OnBattery ? "on battery" : "plugged in")}";
        }
        catch { }
    }

    private void ReportUnreachable(Exception ex)
    {
        HwStatusDot.Fill = Brushes.OrangeRed;
        HwStatusLabel.Text = "service unreachable";
        System.Diagnostics.Debug.WriteLine($"[Poll] {ex}");
        ToastText.Text = $"{ex.GetType().Name}: {ex.Message}" + (ex.InnerException is { } inner ? $" -> {inner.GetType().Name}: {inner.Message}" : "");
    }

    // Cached so ApplyPreset_Click can look up the just-applied preset's
    // real typed params (for SyncTuningPanelsFromPreset) without a second
    // round trip -- GetPresetsAsync already returns them fully deserialized.
    private Dictionary<string, KeyboardPreset> _presets = new();

    private async Task RefreshPresetsAsync()
    {
        _presets = await _api.GetPresetsAsync();
        string? defaultName = await _api.GetDefaultPresetAsync();
        PresetsList.ItemsSource = _presets
            .OrderBy(kv => kv.Key)
            .Select(kv => new PresetRow(kv.Key, kv.Value.Effect, kv.Key == defaultName))
            .ToList();
    }

    // Ported from app.js's syncTuningPanelsFromPreset: after applying a
    // preset, reflect its params into whichever tuning panel(s) they
    // belong to, so re-opening Gradient/Reactive Typing/Custom Key Colors
    // shows what's actually live instead of stale leftover UI state.
    // Suppressed while running -- each Load*Params call flips several
    // checkboxes/combos that would otherwise each schedule their own
    // redundant live-reapply of the preset we just applied a moment ago
    // (app.js's equivalent doesn't have this problem: setting `.checked`
    // programmatically in JS never fires a change/input event the way a
    // WPF dependency-property assignment fires its RoutedEvent).
    private void SyncTuningPanelsFromPreset(KeyboardPreset preset)
    {
        _suppressLiveApply = true;
        try
        {
            TrEnabledCheck.IsChecked = preset.Effect == "typing_reactive";
            switch (preset.Params)
            {
                case JmaStudio.Effects.TypingReactiveParams trp:
                    LoadTypingReactiveParams(trp);
                    break;
                case JmaStudio.Effects.GradientParams gp:
                    LoadGradientParams(gp);
                    break;
                case JmaStudio.Effects.CustomKeysParams ckp:
                    LoadCustomKeysParams(ckp);
                    break;
            }
            UpdateTypingReactiveLabels();
        }
        finally
        {
            _suppressLiveApply = false;
        }
    }

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            ToastText.Text = "";
            timer.Stop();
        };
        timer.Start();
    }

    private async void OffBtn_Click(object sender, RoutedEventArgs e)
    {
        await _api.TurnOffAsync();
        ShowToast("Off");
    }

    private async void QuickEffect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
        {
            await _api.ApplyEffectDefaultAsync(name);
            _activePresetName = null;
            ShowToast($"Applied {name}");
        }
    }

    // The whole preset card is clickable (matches gui/app.js exactly --
    // there's no separate "Apply" button). Wired to the card Border's
    // MouseLeftButtonUp rather than a Click, since Border has no Click
    // event; the small delete "x" is a real Button, so its own Click
    // marks the underlying mouse-up handled before it bubbles here,
    // which is what keeps a delete-click from also applying the preset.
    private async void PresetCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string name }) return;
        await _api.ApplyPresetAsync(name);
        _activePresetName = name;
        if (_presets.TryGetValue(name, out KeyboardPreset? preset))
        {
            SyncTuningPanelsFromPreset(preset);
        }
        ShowToast($"Applied \"{name}\"");
    }

    private async void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
        {
            if (MessageBox.Show(this, $"Delete preset \"{name}\"?", "Confirm",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }
            await _api.DeletePresetAsync(name);
            await RefreshPresetsAsync();
            ShowToast($"Deleted \"{name}\"");
        }
    }

    private async void SavePresetBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NamePromptWindow("Save current as preset") { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await _api.SavePresetAsync(dialog.EnteredName);
            _activePresetName = dialog.EnteredName;
            await RefreshPresetsAsync();
            ShowToast($"Saved \"{dialog.EnteredName}\"");
        }
    }

    private async void SetDefaultBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_activePresetName is null)
        {
            ShowToast("Apply or save a preset first");
            return;
        }
        bool ok = await _api.SetDefaultPresetAsync(_activePresetName);
        if (ok) await RefreshPresetsAsync();
        ShowToast(ok ? $"\"{_activePresetName}\" is now the startup default" : "Failed to set default");
    }

    private LightbarWindow? _lightbarWindow;

    private void LightbarBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_lightbarWindow is null || !_lightbarWindow.IsLoaded)
        {
            _lightbarWindow = new LightbarWindow { Owner = this };
            _lightbarWindow.Show();
        }
        else
        {
            _lightbarWindow.Activate();
        }
    }

    private ControllerReactiveWindow? _controllerReactiveWindow;

    private void ControllerReactiveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_controllerReactiveWindow is null || !_controllerReactiveWindow.IsLoaded)
        {
            _controllerReactiveWindow = new ControllerReactiveWindow { Owner = this };
            _controllerReactiveWindow.Show();
        }
        else
        {
            _controllerReactiveWindow.Activate();
        }
    }

    private IdleScreensaverWindow? _idleScreensaverWindow;

    private void IdleScreensaverBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_idleScreensaverWindow is null || !_idleScreensaverWindow.IsLoaded)
        {
            _idleScreensaverWindow = new IdleScreensaverWindow { Owner = this };
            _idleScreensaverWindow.Show();
        }
        else
        {
            _idleScreensaverWindow.Activate();
        }
    }

    private DiagnosticsWindow? _diagnosticsWindow;

    private void DiagnosticsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_diagnosticsWindow is null || !_diagnosticsWindow.IsLoaded)
        {
            _diagnosticsWindow = new DiagnosticsWindow { Owner = this };
            _diagnosticsWindow.Show();
        }
        else
        {
            _diagnosticsWindow.Activate();
        }
    }
}

public sealed record PresetRow(string Name, string Effect, bool IsDefault);

// DisplayName has underscores replaced with spaces -- WPF's Button.Content
// treats a literal "_" as an access-key (mnemonic) marker, turning the
// character after it into a hidden Alt+key shortcut. Effect names are
// snake_case (e.g. "spectrum_cycle"), so binding them to Content directly
// silently wired every quick-effect button to a keyboard shortcut nobody
// asked for -- confirmed live: once Alt was pressed at any point (even
// incidentally, browsing the window), a bare "C" keypress fired
// "spectrum_cycle"'s apply-default endpoint with no click at all. Name
// (the real, raw effect id) stays bound to Tag for the API calls.
public sealed record EffectChip(string Name, string DisplayName);
