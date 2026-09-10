// Lightbar control window -- ported from gui/lightbar.html + its inline
// script. Two deliberate simplifications versus the Python reference,
// both purely cosmetic (no functional gap): the recolored bar
// illustration (a CSS-masked/hue-rotated PNG) is replaced with three
// plain color-preview swatches (Zone1/2/3PreviewSwatch) showing each
// zone's current color, and the "zone pin" row under the illustration
// is dropped since the ZoneCombo already selects the zone. Everything
// else -- mode toggle, color wheel, brightness, swatches, presets,
// reactive keyboard-flash section -- is full parity.

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JmaStudio.Hardware;

namespace JmaStudio.Gui;

public partial class LightbarWindow : Window
{
    private readonly ApiClient _api = new();
    private readonly bool _uiReady;

    // See MainWindow.xaml.cs's _suppressLiveApply/_uiReady comments for why
    // both exist and why they're separate concerns -- same patterns reused
    // here rather than reinvented.
    private bool _suppressLiveApply = true;

    private static readonly string[] LightbarModeNames =
        { "Breathing", "Neon", "Rainbow", "Wave", "Ripple", "Scanner", "Strobe" };

    private readonly Dictionary<int, RgbColor> _zoneColors = new()
    {
        [1] = new RgbColor(255, 0, 0), [2] = new RgbColor(255, 0, 0), [3] = new RgbColor(255, 0, 0),
    };
    private int? _selectedZone = 1; // null means "All Zones"
    private bool _dynamicMode;
    private LightbarMode? _activeMode;
    private Debouncer? _colorDebounce;
    private Debouncer? _brightnessDebounce;

    public LightbarWindow()
    {
        InitializeComponent();
        _uiReady = true;
        WindowPlacement.PlaceTopCentered(this);
        WindowPlacement.ReactivateOwnerOnClose(this);

        Wheel.ColorChanged += c => OnWheelColorChanged(c, fromUser: true);
        _colorDebounce = new Debouncer(120, async () => await SendZoneColorAsync());
        _brightnessDebounce = new Debouncer(150, async () => await SendBrightnessAsync());

        BuildModeChips();
        LoadSwatches();
        RenderSwatches();
        InitializeReactivePanel();

        Loaded += LightbarWindow_Loaded;
    }

    private async void LightbarWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ZoneCombo.SelectedIndex = 0;
            SetWheelFromRgb(255, 0, 0, fromUser: false);
            await RefreshPresetsAsync();
            await LoadReactiveConfigAsync();
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

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => { ToastText.Text = ""; timer.Stop(); };
        timer.Start();
    }

    // ---- mode toggle (Static/Dynamic) ----

    private void BuildModeChips()
    {
        ModeChipsList.ItemsSource = LightbarModeNames;
    }

    private void SetUiMode(bool isDynamic)
    {
        _dynamicMode = isDynamic;
        ModeStaticBtn.Style = (Style)FindResource(isDynamic ? "BtnGhost" : "BtnAccent");
        ModeDynamicBtn.Style = (Style)FindResource(isDynamic ? "BtnAccent" : "BtnGhost");
        ZoneCombo.IsEnabled = !isDynamic;
        DynamicModeRow.Visibility = isDynamic ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ModeStaticBtn_Click(object sender, RoutedEventArgs e) => await SetDynamicModeAsync(false);
    private async void ModeDynamicBtn_Click(object sender, RoutedEventArgs e) => await SetDynamicModeAsync(true);

    private async Task SetDynamicModeAsync(bool isDynamic)
    {
        if (isDynamic)
        {
            // The wheel becomes "the one color for the effect" in Dynamic
            // mode -- pin it to zone 1's color so dragging it can't
            // silently target a zone that isn't the one actually being
            // sent to the firmware.
            _selectedZone = 1;
            SetUiMode(true);
            SetWheelFromRgb(_zoneColors[1].R, _zoneColors[1].G, _zoneColors[1].B, fromUser: false);
        }
        else
        {
            SetUiMode(false);
            _activeMode = null;
            BuildModeChips();
            // A real static commit also resets the firmware mode byte back
            // to 0, which is what actually cancels the animated mode.
            RgbColor color = _selectedZone is int zone ? _zoneColors[zone] : _zoneColors[1];
            if (_selectedZone is null) await _api.SetLightbarAllAsync(color);
            else await _api.SetLightbarZoneAsync(_selectedZone.Value, color);
        }
    }

    private async void ModeChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string modeName }) return;
        if (!Enum.TryParse(modeName, out LightbarMode mode)) return;
        _activeMode = mode;
        RgbColor color = _zoneColors[1];
        int brightness = (int)BrightnessSlider.Value;
        await _api.SetLightbarModeAsync(mode, color, speed: 5, brightness);
    }

    // ---- zone selection ----

    private void ZoneCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady) return;
        if (ZoneCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;
        _selectedZone = tag == "all" ? null : int.Parse(tag);
        RgbColor c = _selectedZone is int zone ? _zoneColors[zone] : _zoneColors[1];
        SetWheelFromRgb(c.R, c.G, c.B, fromUser: false);
    }

    // ---- color wheel / value slider / RGB inputs, kept in sync ----

    private void SetWheelFromRgb(byte r, byte g, byte b, bool fromUser)
    {
        Wheel.SetFromRgb(r, g, b);
        (double h, double s, double v) = RgbToHsv(r, g, b);
        ValueSlider.Value = Math.Round(v * 100);
        Wheel.Value = v;
        ApplyColor(r, g, b, fromUser);
    }

    private void ApplyColor(byte r, byte g, byte b, bool fromUser)
    {
        var color = new RgbColor(r, g, b);
        if (_selectedZone is null)
        {
            _zoneColors[1] = color;
            _zoneColors[2] = color;
            _zoneColors[3] = color;
            Illustration.SetAllZonesColor(color);
        }
        else
        {
            _zoneColors[_selectedZone.Value] = color;
            Illustration.SetZoneColor(_selectedZone.Value, color);
        }
        RInput.Text = r.ToString();
        GInput.Text = g.ToString();
        BInput.Text = b.ToString();
        if (fromUser) FireColorChange();
    }

    private void UpdateZonePreviews()
    {
        Illustration.SetZoneColor(1, _zoneColors[1]);
        Illustration.SetZoneColor(2, _zoneColors[2]);
        Illustration.SetZoneColor(3, _zoneColors[3]);
    }

    private void OnWheelColorChanged(Color color, bool fromUser) => ApplyColor(color.R, color.G, color.B, fromUser);

    private void ValueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady) return;
        Wheel.Value = ValueSlider.Value / 100.0;
    }

    private void RgbInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnRgbInputChanged();
    }

    private void RgbInput_Changed(object sender, RoutedEventArgs e) => OnRgbInputChanged();

    private void OnRgbInputChanged()
    {
        byte r = ClampByte(RInput.Text);
        byte g = ClampByte(GInput.Text);
        byte b = ClampByte(BInput.Text);
        SetWheelFromRgb(r, g, b, fromUser: true);
    }

    private static byte ClampByte(string text) =>
        (byte)Math.Clamp(int.TryParse(text, out int v) ? v : 0, 0, 255);

    private void FireColorChange()
    {
        if (_suppressLiveApply) return;
        _colorDebounce?.Fire();
    }

    private async Task SendZoneColorAsync()
    {
        if (_dynamicMode)
        {
            if (_activeMode is { } mode)
            {
                await _api.SetLightbarModeAsync(mode, _zoneColors[1], speed: 5, (int)BrightnessSlider.Value);
            }
            return;
        }
        if (_selectedZone is null) await _api.SetLightbarAllAsync(_zoneColors[1]);
        else await _api.SetLightbarZoneAsync(_selectedZone.Value, _zoneColors[_selectedZone.Value]);
    }

    // ---- brightness ----

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        BrightnessLabel.Text = $"{(int)BrightnessSlider.Value}%";
        if (!_uiReady || _suppressLiveApply) return;
        _brightnessDebounce?.Fire();
    }

    private async Task SendBrightnessAsync()
    {
        if (_dynamicMode && _activeMode is { } mode)
        {
            await _api.SetLightbarModeAsync(mode, _zoneColors[1], speed: 5, (int)BrightnessSlider.Value);
        }
        else
        {
            await _api.SetLightbarBrightnessAsync((int)BrightnessSlider.Value);
        }
    }

    // ---- off / swatches ----

    private async void OffBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_dynamicMode)
        {
            _activeMode = null;
            BuildModeChips();
        }
        if (_dynamicMode || _selectedZone is null)
        {
            await _api.TurnOffLightbarAsync();
            _zoneColors[1] = new RgbColor(0, 0, 0);
            _zoneColors[2] = new RgbColor(0, 0, 0);
            _zoneColors[3] = new RgbColor(0, 0, 0);
        }
        else
        {
            _zoneColors[_selectedZone.Value] = new RgbColor(0, 0, 0);
            await _api.SetLightbarZoneAsync(_selectedZone.Value, new RgbColor(0, 0, 0));
        }
        UpdateZonePreviews();
        RInput.Text = "0"; GInput.Text = "0"; BInput.Text = "0";
    }

    private void AddSwatchBtn_Click(object sender, RoutedEventArgs e)
    {
        string hex = ColorToHex(Color.FromRgb(ClampByte(RInput.Text), ClampByte(GInput.Text), ClampByte(BInput.Text)));
        if (!_swatches.Contains(hex))
        {
            _swatches.Add(hex);
            SaveSwatches();
            RenderSwatches();
        }
    }

    private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static readonly string[] DefaultSwatchHexes =
        { "#4F7BFF", "#B544E8", "#FF3FA4", "#FF5470", "#3DDC84", "#FFFFFF" };

    private static readonly string SwatchesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JmaStudio", "gui-lightbar-swatches.json");

    private readonly List<string> _swatches = new();

    private void LoadSwatches()
    {
        try
        {
            if (File.Exists(SwatchesPath))
            {
                var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(SwatchesPath));
                if (list is { Count: > 0 })
                {
                    _swatches.AddRange(list);
                    return;
                }
            }
        }
        catch
        {
            // Corrupt/inaccessible file -- fall through to defaults.
        }
        _swatches.AddRange(DefaultSwatchHexes);
    }

    private void SaveSwatches()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SwatchesPath)!);
            File.WriteAllText(SwatchesPath, JsonSerializer.Serialize(_swatches));
        }
        catch
        {
            // Best-effort, matches app.js's own try/catch around localStorage.
        }
    }

    private void RenderSwatches()
    {
        UserSwatchesList.Items.Clear();
        foreach (string hex in _swatches)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var swatch = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand, ToolTip = hex,
            };
            swatch.MouseLeftButtonUp += (_, _) => SetWheelFromRgb(color.R, color.G, color.B, fromUser: true);
            UserSwatchesList.Items.Add(swatch);
        }
    }

    // ---- shared HSV math (same formulas as gui/lightbar.html's inline script) ----

    private static (double H, double S, double V) RgbToHsv(byte r8, byte g8, byte b8)
    {
        double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double h = 0;
        if (d != 0)
        {
            if (max == r) h = 60 * ((g - b) / d % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        double s = max == 0 ? 0 : d / max;
        return (h, s, max);
    }

    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        ScrollBehavior.HandleMouseWheel(MainScrollViewer, e);
}
