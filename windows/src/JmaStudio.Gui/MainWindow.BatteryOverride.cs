// Phase 8 (V2) low-battery override panel -- moved here from its own
// IdleScreensaverWindow settings screen at the user's request, so it
// sits on the main window between Live Preview and Presets (its own
// panel, not a tab of either). Unlike the screensaver's remaining
// Save-button settings, this panel live-applies on every change,
// matching the Gradient/Reactive Typing/Custom Key Colors panels
// further down this same window -- same debounced-POST pattern as
// those (see MainWindow.TypingReactive.cs's FireTrLive/OnTrLiveApplyAsync
// for the pattern this mirrors), since it's now living among live-apply
// panels rather than in a dedicated settings window with its own Save.
//
// Keyboard and lightbar each get their own color + brightness (2026-09-12,
// per the user's explicit request) rather than one shared color/
// brightness applied to both.
//
// Slider-label init uses the same explicit-Text-assignment fix as
// IdleScreensaverWindow's ThresholdSlider (see that file's header
// comment): every slider here has Minimum set with no Value in XAML, so
// WPF coerces Value into range during BAML parsing and fires
// ValueChanged before _uiReady is true -- harmless (the handler
// no-ops), but it also means a LOADED value that happens to equal the
// coerced default won't re-fire ValueChanged, so every label is set
// explicitly here rather than relying on the event.

using System.Windows;
using JmaStudio.Presets;

namespace JmaStudio.Gui;

public partial class MainWindow
{
    private Debouncer? _batteryDebounce;

    private async Task InitializeBatteryOverridePanelAsync()
    {
        _batteryDebounce = new Debouncer(120, async () => await OnBatteryLiveApplyAsync());
        BatteryKeyboardSwatch.ColorPicked += _ => FireBatteryLive();
        BatteryLightbarSwatch.ColorPicked += _ => FireBatteryLive();

        LowBatteryOverrideConfig config = await _api.GetLowBatteryOverrideConfigAsync() ?? new LowBatteryOverrideConfig();
        BatteryEnabledCheck.IsChecked = config.Enabled;
        BatteryThresholdSlider.Value = Math.Clamp(config.ThresholdPercent, BatteryThresholdSlider.Minimum, BatteryThresholdSlider.Maximum);
        BatteryThresholdLabel.Text = $"{(int)BatteryThresholdSlider.Value}%";

        BatteryKeyboardBrightnessSlider.Value = Math.Clamp(config.KeyboardBrightness * 100, BatteryKeyboardBrightnessSlider.Minimum, BatteryKeyboardBrightnessSlider.Maximum);
        BatteryKeyboardBrightnessHeader.Text = $"Keyboard brightness -- {(int)BatteryKeyboardBrightnessSlider.Value}%";
        BatteryKeyboardSwatch.Color = config.KeyboardColor.ToMediaColor();

        BatteryLightbarBrightnessSlider.Value = Math.Clamp(config.LightbarBrightness * 100, BatteryLightbarBrightnessSlider.Minimum, BatteryLightbarBrightnessSlider.Maximum);
        BatteryLightbarBrightnessHeader.Text = $"Lightbar brightness -- {(int)BatteryLightbarBrightnessSlider.Value}%";
        BatteryLightbarSwatch.Color = config.LightbarColor.ToMediaColor();
    }

    private void BatteryThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady) return;
        BatteryThresholdLabel.Text = $"{(int)BatteryThresholdSlider.Value}%";
        FireBatteryLive();
    }

    private void BatteryKeyboardBrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady) return;
        BatteryKeyboardBrightnessHeader.Text = $"Keyboard brightness -- {(int)BatteryKeyboardBrightnessSlider.Value}%";
        FireBatteryLive();
    }

    private void BatteryLightbarBrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady) return;
        BatteryLightbarBrightnessHeader.Text = $"Lightbar brightness -- {(int)BatteryLightbarBrightnessSlider.Value}%";
        FireBatteryLive();
    }

    private void BatteryOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady) return;
        FireBatteryLive();
    }

    private void FireBatteryLive()
    {
        if (_suppressLiveApply) return;
        _batteryDebounce?.Fire();
    }

    private async Task OnBatteryLiveApplyAsync()
    {
        var config = new LowBatteryOverrideConfig
        {
            Enabled = BatteryEnabledCheck.IsChecked == true,
            ThresholdPercent = (int)BatteryThresholdSlider.Value,
            KeyboardColor = BatteryKeyboardSwatch.Color.ToRgbColor(),
            KeyboardBrightness = BatteryKeyboardBrightnessSlider.Value / 100.0,
            LightbarColor = BatteryLightbarSwatch.Color.ToRgbColor(),
            LightbarBrightness = BatteryLightbarBrightnessSlider.Value / 100.0,
        };
        await _api.SetLowBatteryOverrideConfigAsync(config);
    }
}
