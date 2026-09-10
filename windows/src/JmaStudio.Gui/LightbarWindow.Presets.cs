// Lightbar presets panel -- ported from gui/lightbar.html's
// renderLightbarPresets/applyLightbarPreset/loadLightbarPresets. Reuses
// MainWindow's whole-card-click-to-apply preset card pattern (same
// PresetCardStyle/PresetDeleteButtonStyle from App.xaml).

using System.Windows;
using System.Windows.Input;
using JmaStudio.Hardware;
using JmaStudio.Presets;

namespace JmaStudio.Gui;

public sealed record LightbarPresetRow(string Name, string Subtitle);

public partial class LightbarWindow
{
    private Dictionary<string, LightbarPreset> _presets = new();
    private string? _activePresetName;

    private async Task RefreshPresetsAsync()
    {
        _presets = await _api.GetLightbarPresetsAsync();
        PresetsList.ItemsSource = _presets
            .OrderBy(kv => kv.Key)
            .Select(kv => new LightbarPresetRow(kv.Key, PresetSubtitle(kv.Value)))
            .ToList();
    }

    private static string PresetSubtitle(LightbarPreset preset) =>
        preset.Lightbar.Mode is { } mode ? mode.ToString() : $"brightness {preset.Lightbar.Brightness}";

    private async void PresetCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border { Tag: string name }) return;
        bool ok = await _api.ApplyLightbarPresetAsync(name);
        if (!ok)
        {
            ShowToast($"Failed to apply \"{name}\"");
            return;
        }
        _activePresetName = name;
        if (_presets.TryGetValue(name, out LightbarPreset? preset))
        {
            ApplyPresetToUi(preset);
        }
        ShowToast($"Applied \"{name}\"");
    }

    private void ApplyPresetToUi(LightbarPreset preset)
    {
        _suppressLiveApply = true;
        try
        {
            LightbarState lb = preset.Lightbar;
            if (lb.Mode is { } mode)
            {
                _zoneColors[1] = lb.ModeColor;
                _activeMode = mode;
                _selectedZone = 1;
                SetUiMode(true);
                BrightnessSlider.Value = lb.Brightness;
                SetWheelFromRgb(lb.ModeColor.R, lb.ModeColor.G, lb.ModeColor.B, fromUser: false);
            }
            else
            {
                _activeMode = null;
                SetUiMode(false);
                if (lb.ZoneColors is { } zoneColors)
                {
                    foreach ((int zone, RgbColor color) in zoneColors) _zoneColors[zone] = color;
                }
                BrightnessSlider.Value = lb.Brightness;
                UpdateZonePreviews();
                RgbColor current = _selectedZone is int z ? _zoneColors[z] : _zoneColors[1];
                SetWheelFromRgb(current.R, current.G, current.B, fromUser: false);
            }
        }
        finally
        {
            _suppressLiveApply = false;
        }

        // Reactive settings were applied server-side too, if this preset
        // has any -- refresh from the server rather than duplicating that
        // logic here, so it can't drift out of sync with what actually
        // got written (matches app.js's applyLightbarPreset comment).
        if (preset.Reactive is not null)
        {
            _ = LoadReactiveConfigAsync();
        }
    }

    private async void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string name }) return;
        if (MessageBox.Show(this, $"Delete preset \"{name}\"?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        if (_activePresetName == name) _activePresetName = null;
        await _api.DeleteLightbarPresetAsync(name);
        await RefreshPresetsAsync();
        ShowToast($"Deleted \"{name}\"");
    }

    private async void SavePresetBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NamePromptWindow("Save current lightbar as preset") { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await _api.SaveLightbarPresetAsync(dialog.EnteredName);
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
        bool ok = await _api.SetLightbarDefaultPresetAsync(_activePresetName);
        ShowToast(ok ? $"\"{_activePresetName}\" is now the default" : "Failed to set default");
    }
}
