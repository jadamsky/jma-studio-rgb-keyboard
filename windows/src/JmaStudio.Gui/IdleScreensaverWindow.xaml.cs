// Phase 8 (V2) idle screensaver settings window. Config lives on the
// Service (IdleScreensaverConfig via GET/POST /idle-screensaver/config);
// this window just edits it and posts the whole thing on Save, rather
// than live-applying on every change like the tuning panels do --
// IdleScreensaverManager only reads the config once per its own 1s
// tick anyway, so there's no responsiveness benefit to live-apply here,
// and a single Save is simpler to reason about for a background timer's
// settings than the render-loop-adjacent tuning panels' debounced-apply
// pattern.
//
// _uiReady guard needed here for the same reason MainWindow's tuning
// panels need it (see that file's own header comment): ThresholdSlider/
// IntervalSlider have Minimum/Maximum set in XAML but no Value attribute,
// so WPF's property-coercion logic snaps Value into range during BAML
// parsing -- firing ValueChanged before later-declared named elements
// (the Label TextBlocks) are assigned yet.

using System.Windows;
using System.Windows.Input;
using JmaStudio.Presets;

namespace JmaStudio.Gui;

public partial class IdleScreensaverWindow : Window
{
    private readonly ApiClient _api = new();
    private readonly bool _uiReady;
    private readonly List<string> _playlist = new();
    private const string NoLightbarOption = "(None -- leave lightbar untouched)";

    public IdleScreensaverWindow()
    {
        InitializeComponent();
        _uiReady = true;
        WindowPlacement.PlaceTopCentered(this);
        WindowPlacement.ReactivateOwnerOnClose(this);
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Dictionary<string, KeyboardPreset> keyboardPresets = await _api.GetPresetsAsync();
        Dictionary<string, LightbarPreset> lightbarPresets = await _api.GetLightbarPresetsAsync();

        var keyboardOptions = new List<string> { IdleScreensaverSentinels.LightsOut };
        keyboardOptions.AddRange(keyboardPresets.Keys.OrderBy(n => n));
        AddPresetCombo.ItemsSource = keyboardOptions;

        var lightbarOptions = new List<string> { NoLightbarOption, IdleScreensaverSentinels.LightsOut };
        lightbarOptions.AddRange(lightbarPresets.Keys.OrderBy(n => n));
        LightbarPresetCombo.ItemsSource = lightbarOptions;

        IdleScreensaverConfig? config = await _api.GetIdleScreensaverConfigAsync();
        if (config is null)
        {
            LightbarPresetCombo.SelectedItem = NoLightbarOption;
            return;
        }

        EnabledCheck.IsChecked = config.Enabled;

        // Setting .Value here does NOT reliably trigger ValueChanged --
        // found live: ThresholdSlider has Minimum="1" and no Value in
        // XAML, so WPF's property coercion already snaps its default
        // Value (0) up to 1 during BAML parsing, before this method ever
        // runs. If the saved config value is ALSO exactly 1 (i.e. equals
        // whatever the slider already coerced itself to), assigning
        // Value = 1 here is a no-op as far as WPF's dependency-property
        // system is concerned (no actual change -> no ValueChanged
        // event), so the label never gets updated and stays stuck on its
        // hardcoded XAML placeholder text ("5.0 min") -- exactly the bug
        // reported live ("I set it to one... it will say five minutes").
        // Fix: update both labels explicitly here, not just via the
        // ValueChanged handler, so they're correct regardless of whether
        // the assignment above happened to be a real change or not.
        ThresholdSlider.Value = Math.Clamp(config.IdleThresholdMinutes, ThresholdSlider.Minimum, ThresholdSlider.Maximum);
        ThresholdLabel.Text = $"{ThresholdSlider.Value:0.0} min";
        IntervalSlider.Value = Math.Clamp(config.CycleIntervalSeconds, IntervalSlider.Minimum, IntervalSlider.Maximum);
        IntervalLabel.Text = $"{(int)IntervalSlider.Value} s";
        RandomOrderCheck.IsChecked = config.RandomOrder;

        _playlist.Clear();
        _playlist.AddRange(config.KeyboardPresetNames);
        RefreshPlaylistList();

        LightbarPresetCombo.SelectedItem = !string.IsNullOrEmpty(config.LightbarPresetName) && lightbarOptions.Contains(config.LightbarPresetName)
            ? config.LightbarPresetName
            : NoLightbarOption;
    }

    private void RefreshPlaylistList()
    {
        PlaylistList.ItemsSource = null;
        PlaylistList.ItemsSource = _playlist.ToList();
    }

    private void AddPresetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (AddPresetCombo.SelectedItem is string name && !_playlist.Contains(name))
        {
            _playlist.Add(name);
            RefreshPlaylistList();
        }
    }

    private void RemovePresetBtn_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistList.SelectedItem is string name)
        {
            _playlist.Remove(name);
            RefreshPlaylistList();
        }
    }

    private void MoveUpBtn_Click(object sender, RoutedEventArgs e)
    {
        int i = PlaylistList.SelectedIndex;
        if (i <= 0) return;
        (_playlist[i - 1], _playlist[i]) = (_playlist[i], _playlist[i - 1]);
        RefreshPlaylistList();
        PlaylistList.SelectedIndex = i - 1;
    }

    private void MoveDownBtn_Click(object sender, RoutedEventArgs e)
    {
        int i = PlaylistList.SelectedIndex;
        if (i < 0 || i >= _playlist.Count - 1) return;
        (_playlist[i + 1], _playlist[i]) = (_playlist[i], _playlist[i + 1]);
        RefreshPlaylistList();
        PlaylistList.SelectedIndex = i + 1;
    }

    private void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady) return;
        ThresholdLabel.Text = $"{ThresholdSlider.Value:0.0} min";
    }

    private void IntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady) return;
        IntervalLabel.Text = $"{(int)IntervalSlider.Value} s";
    }

    private async void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        string? lightbarSelection = LightbarPresetCombo.SelectedItem as string;
        string? lightbarPresetName = (lightbarSelection is null || lightbarSelection == NoLightbarOption)
            ? null
            : lightbarSelection;

        var config = new IdleScreensaverConfig
        {
            Enabled = EnabledCheck.IsChecked == true,
            IdleThresholdMinutes = ThresholdSlider.Value,
            KeyboardPresetNames = _playlist.ToList(),
            CycleIntervalSeconds = (int)IntervalSlider.Value,
            RandomOrder = RandomOrderCheck.IsChecked == true,
            LightbarPresetName = lightbarPresetName,
        };
        bool ok = await _api.SetIdleScreensaverConfigAsync(config);
        ToastText.Text = ok ? "Saved" : "Failed to save";
    }

    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        ScrollBehavior.HandleMouseWheel(MainScrollViewer, e);
}
