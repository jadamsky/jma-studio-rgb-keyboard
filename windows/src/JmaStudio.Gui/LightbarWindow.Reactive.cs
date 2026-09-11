// "Reactive (keyboard -> lightbar)" panel -- ported from
// gui/lightbar.html's reactive section (readReactiveConfig/
// loadReactiveConfig/saveReactiveConfig/capture_zones). Backed by the
// Service's new /lightbar/reactive* endpoints (JmaStudio.Service's
// LightbarReactiveManager), built specifically to support this window.

using System.Windows;
using JmaStudio.Hardware;
using JmaStudio.Presets;

namespace JmaStudio.Gui;

public partial class LightbarWindow
{
    private Debouncer? _reactiveDebounce;

    private void InitializeReactivePanel()
    {
        _reactiveDebounce = new Debouncer(150, async () => await SaveReactiveConfigAsync());
        ReactiveBgSwatch.Color = System.Windows.Media.Colors.Black;
        ReactiveZ1Swatch.Color = System.Windows.Media.Colors.Red;
        ReactiveZ2Swatch.Color = System.Windows.Media.Colors.Lime;
        ReactiveZ3Swatch.Color = System.Windows.Media.Colors.Blue;
        ReactiveAllSwatch.Color = System.Windows.Media.Color.FromRgb(255, 220, 0);
        ReactiveBgSwatch.ColorPicked += _ => FireReactiveSave();
        ReactiveZ1Swatch.ColorPicked += _ => FireReactiveSave();
        ReactiveZ2Swatch.ColorPicked += _ => FireReactiveSave();
        ReactiveZ3Swatch.ColorPicked += _ => FireReactiveSave();
        ReactiveAllSwatch.ColorPicked += _ => FireReactiveSave();
    }

    private async Task LoadReactiveConfigAsync()
    {
        LightbarReactiveConfig? config = await _api.GetLightbarReactiveConfigAsync();
        if (config is null) return;
        _suppressLiveApply = true;
        try
        {
            ReactiveEnabledCheck.IsChecked = config.Enabled;
            ReactiveBgSwatch.Color = config.BackgroundColor.ToMediaColor();
            ReactiveZ1Swatch.Color = config.ZoneFlashColors.GetValueOrDefault(1, new RgbColor(255, 0, 0)).ToMediaColor();
            ReactiveZ2Swatch.Color = config.ZoneFlashColors.GetValueOrDefault(2, new RgbColor(0, 255, 0)).ToMediaColor();
            ReactiveZ3Swatch.Color = config.ZoneFlashColors.GetValueOrDefault(3, new RgbColor(0, 0, 255)).ToMediaColor();
            ReactiveAllSwatch.Color = config.AllFlashColor.ToMediaColor();
            CaptureStatusText.Text = config.ZoneBoundaries.Count > 0
                ? $"Zones captured ({config.ZoneBoundaries.Count + 1} zones)."
                : "No keyboard zones captured yet -- click Capture first.";
        }
        finally
        {
            _suppressLiveApply = false;
        }
    }

    private void FireReactiveSave()
    {
        if (_suppressLiveApply) return;
        _reactiveDebounce?.Fire();
    }

    private void ReactiveOption_Changed(object sender, RoutedEventArgs e) => FireReactiveSave();

    private async Task SaveReactiveConfigAsync()
    {
        await _api.SetLightbarReactiveConfigAsync(
            enabled: ReactiveEnabledCheck.IsChecked == true,
            backgroundColor: ReactiveBgSwatch.Color.ToRgbColor(),
            zoneFlashColors: new Dictionary<int, RgbColor>
            {
                [1] = ReactiveZ1Swatch.Color.ToRgbColor(),
                [2] = ReactiveZ2Swatch.Color.ToRgbColor(),
                [3] = ReactiveZ3Swatch.Color.ToRgbColor(),
            },
            allFlashColor: ReactiveAllSwatch.Color.ToRgbColor());
    }

    private async void CaptureZonesBtn_Click(object sender, RoutedEventArgs e)
    {
        CaptureZonesResponse? result = await _api.CaptureLightbarReactiveZonesAsync();
        if (result is null)
        {
            CaptureStatusText.Text = "Capture failed -- the keyboard isn't currently running a multi-zone gradient.";
            ShowToast("Capture failed");
            return;
        }
        CaptureStatusText.Text = $"Zones captured ({result.NumZones} zones).";
        ShowToast("Captured current keyboard zones");
    }
}
