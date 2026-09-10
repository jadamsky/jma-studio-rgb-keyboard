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
using System.Windows.Shapes;
using System.Windows.Threading;

namespace JmaStudio.Gui;

public partial class MainWindow : Window
{
    private readonly ApiClient _api = new();
    private readonly Dictionary<int, Rectangle> _cellRectangles = new();
    private readonly DispatcherTimer _pollTimer;
    private LayoutResponse? _layout;
    private string? _activePresetName;

    public MainWindow()
    {
        InitializeComponent();
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _pollTimer.Tick += async (_, _) => await PollAsync();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await InitializeAsync();
        _pollTimer.Start();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _layout = await _api.GetLayoutAsync();
            BuildKeyboardCanvas();

            string[] effects = await _api.GetEffectsAsync();
            EffectsList.ItemsSource = effects;

            await RefreshPresetsAsync();
            await PollAsync();
        }
        catch (Exception ex)
        {
            ShowToast($"Startup error: {ex.Message}");
        }
    }

    private void BuildKeyboardCanvas()
    {
        if (_layout is null || _layout.Cells.Length == 0) return;
        KeyboardCanvas.Children.Clear();
        _cellRectangles.Clear();

        double maxCol = _layout.Cells.Max(c => c.Col);
        double maxRow = _layout.Cells.Max(c => c.Row);
        const double cellSize = 20;
        const double gap = 3;

        KeyboardCanvas.Width = (maxCol + 1.5) * (cellSize + gap);
        KeyboardCanvas.Height = (maxRow + 1) * (cellSize + gap);

        foreach (LayoutCell cell in _layout.Cells)
        {
            var rect = new Rectangle
            {
                Width = cellSize,
                Height = cellSize,
                RadiusX = 3,
                RadiusY = 3,
                Fill = Brushes.Black,
            };
            Canvas.SetLeft(rect, cell.Col * (cellSize + gap));
            Canvas.SetTop(rect, cell.Row * (cellSize + gap));
            KeyboardCanvas.Children.Add(rect);
            _cellRectangles[cell.Index] = rect;
        }
    }

    private async Task PollAsync()
    {
        try
        {
            StatusResponse? status = await _api.GetStatusAsync();
            if (status is not null)
            {
                CurrentEffectLabel.Text = status.CurrentEffect;
                HwStatusDot.Fill = status.KeyboardConnected ? Brushes.LimeGreen : Brushes.OrangeRed;
                HwStatusLabel.Text = status.KeyboardConnected ? "connected" : "no keyboard";
            }

            FrameResponse? frame = await _api.GetFrameAsync();
            if (frame is not null)
            {
                for (int i = 0; i < frame.Colors.Length; i++)
                {
                    if (_cellRectangles.TryGetValue(i, out Rectangle? rect))
                    {
                        var c = frame.Colors[i];
                        rect.Fill = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            HwStatusDot.Fill = Brushes.OrangeRed;
            HwStatusLabel.Text = "service unreachable";
            System.Diagnostics.Debug.WriteLine($"[PollAsync] {ex}");
            ToastText.Text = $"{ex.GetType().Name}: {ex.Message}" + (ex.InnerException is { } inner ? $" -> {inner.GetType().Name}: {inner.Message}" : "");
        }
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
