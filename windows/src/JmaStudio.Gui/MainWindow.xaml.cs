// First-slice WPF main window: live keyboard preview, presets
// (list/apply/delete/save/set-default), and a quick-effects grid --
// the core of gui/index.html's UX (see windows/HANDOFF.md's Phase 6
// section for what's deliberately deferred: the Gradient/Reactive
// Typing tuning panels and the full Custom Key Colors painter, plus
// the Lightbar/Controller Reactive/Diagnostics windows, which are
// stubbed here as placeholders for now).

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

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
    private bool _frameInFlight;
    private bool _statusInFlight;
    private LayoutResponse? _layout;
    private string? _activePresetName;

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
        _framePollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _framePollTimer.Tick += async (_, _) => await PollFrameAsync();
        _statusPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _statusPollTimer.Tick += async (_, _) => await PollStatusAsync();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await InitializeAsync();
        _framePollTimer.Start();
        _statusPollTimer.Start();
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
    // the Python reference.
    private void BuildKeyboardCanvas()
    {
        if (_layout is null || _layout.Cells.Length == 0) return;
        KeyboardCanvas.Children.Clear();
        _cellBorders.Clear();

        double maxCol = _layout.Cells.Max(c => c.Col);
        double maxRow = _layout.Cells.Max(c => c.Row);
        double totalCols = maxCol + 3;
        double totalRows = maxRow + 1.4;

        double availableWidth = PreviewBoardHost.ActualWidth - 40;
        if (availableWidth <= 0) availableWidth = 900;
        double unit = Math.Max(16, Math.Min(34, availableWidth / totalCols));

        KeyboardCanvas.Width = totalCols * unit;
        KeyboardCanvas.Height = totalRows * unit;

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
            KeyboardCanvas.Children.Add(border);
            _cellBorders[cell.Index] = border;
        }
    }

    private void PreviewBoardHost_SizeChanged(object sender, SizeChangedEventArgs e) => BuildKeyboardCanvas();

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

    private void ReportUnreachable(Exception ex)
    {
        HwStatusDot.Fill = Brushes.OrangeRed;
        HwStatusLabel.Text = "service unreachable";
        System.Diagnostics.Debug.WriteLine($"[Poll] {ex}");
        ToastText.Text = $"{ex.GetType().Name}: {ex.Message}" + (ex.InnerException is { } inner ? $" -> {inner.GetType().Name}: {inner.Message}" : "");
    }

    private async Task RefreshPresetsAsync()
    {
        var presets = await _api.GetPresetsAsync();
        PresetsList.ItemsSource = presets.Keys.OrderBy(n => n).Select(name => new PresetRow(name)).ToList();
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

    private async void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
        {
            await _api.ApplyPresetAsync(name);
            _activePresetName = name;
            ShowToast($"Applied \"{name}\"");
        }
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
        ShowToast(ok ? $"\"{_activePresetName}\" is now the startup default" : "Failed to set default");
    }

    private void LightbarBtn_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this, "Lightbar window is a follow-up piece of Phase 6, not built in this pass yet.", "Coming soon");

    private void ControllerReactiveBtn_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this, "Controller Reactive window is a follow-up piece of Phase 6, not built in this pass yet.", "Coming soon");

    private void DiagnosticsBtn_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this, "Diagnostics window is a follow-up piece of Phase 6, not built in this pass yet.", "Coming soon");
}

public sealed record PresetRow(string Name);

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
