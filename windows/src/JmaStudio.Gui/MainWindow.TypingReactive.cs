// Reactive Typing tuning panel -- ported from gui/index.html's "Reactive
// Typing" panel and gui/app.js's readTypingReactiveParams/
// wireTypingReactivePanel/applyCurrentLive. Also hosts ApplyCurrentLiveAsync,
// the central dispatcher both this panel and the Gradient panel's live-apply
// funnel through (mirrors app.js's applyCurrentLive(): decides whether the
// currently-tuned gradient/custom-keys goes out wrapped in typing_reactive
// or on its own, based on tr-enabled).

using System.Windows;
using System.Windows.Controls;
using JmaStudio.Effects;

namespace JmaStudio.Gui;

public partial class MainWindow
{
    private Debouncer? _trDebounce;

    private void InitializeTypingReactivePanel()
    {
        _trDebounce = new Debouncer(120, async () => await OnTrLiveApplyAsync());

        // Set here rather than as a XAML IsChecked="True" attribute: by now
        // InitializeComponent has fully run, so the Checked event these
        // trigger (UpdateTypingReactiveLabels, which reads TrStyleCombo
        // etc.) can safely run -- doing this as a XAML attribute instead
        // fired those handlers mid-parse, before later-declared named
        // elements existed yet, and crashed with a NullReferenceException.
        TrEnabledCheck.IsChecked = true;
        TrUseGradientCheck.IsChecked = true;

        TrBaseSwatch.Color = System.Windows.Media.Color.FromRgb(0x26, 0x26, 0x26);
        TrBrightSwatch.Color = System.Windows.Media.Colors.White;
        TrBaseSwatch.ColorPicked += _ => FireTrLive();
        TrBrightSwatch.ColorPicked += _ => FireTrLive();

        TrShapeCombo.SelectedIndex = 0; // Rays
        TrStyleCombo.SelectedIndex = 0; // Solid
        TrDecaySlider.Value = 0.6;
        TrSpeedSlider.Value = 12;
        TrTailSlider.Value = 3;
        TrMaxDistSlider.Value = 18.5;
        TrFlickerSlider.Value = 6;

        UpdateTypingReactiveLabels();
    }

    private static T SelectedTag<T>(ComboBox combo) where T : struct, Enum =>
        combo.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<T>(tag, out T value) ? value : default;

    private static void SelectByTag<T>(ComboBox combo, T value) where T : struct, Enum
    {
        foreach (ComboBoxItem item in combo.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag && Enum.TryParse<T>(tag, out T itemValue) && itemValue.Equals(value))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private TypingReactiveParams BuildTypingReactiveParams()
    {
        var p = new TypingReactiveParams
        {
            BrightColor = TrBrightSwatch.Color.ToRgbColor(),
            DecaySeconds = TrDecaySlider.Value,
            BoltShape = SelectedTag<BoltShape>(TrShapeCombo),
            BoltStyle = SelectedTag<BoltStyle>(TrStyleCombo),
            BoltSpeed = TrSpeedSlider.Value,
            BoltTail = TrTailSlider.Value,
            BoltMaxDistance = TrMaxDistSlider.Value,
            BoltReset = TrBoltResetCheck.IsChecked == true,
            BoltFlickerSpeed = TrFlickerSlider.Value,
        };
        if (TrUseGradientCheck.IsChecked == true)
        {
            p = p with { BaseEffectName = "gradient", BaseParams = BuildGradientParams() };
        }
        else if (TrUseCustomKeysCheck.IsChecked == true)
        {
            p = p with { BaseEffectName = "custom_keys", BaseParams = BuildCustomKeysParams() };
        }
        else
        {
            p = p with { BaseColor = TrBaseSwatch.Color.ToRgbColor() };
        }
        return p;
    }

    private void UpdateTypingReactiveLabels()
    {
        TrDecayLabel.Text = $"{TrDecaySlider.Value:0.##}s";
        TrSpeedLabel.Text = $"{TrSpeedSlider.Value:0.##}";
        TrTailLabel.Text = $"{TrTailSlider.Value:0.##}";
        TrMaxDistLabel.Text = $"{TrMaxDistSlider.Value:0.##}";
        TrFlickerLabel.Text = $"{TrFlickerSlider.Value:0.##}";
        TrBaseColorRow.Visibility = (TrUseGradientCheck.IsChecked == true || TrUseCustomKeysCheck.IsChecked == true)
            ? Visibility.Collapsed : Visibility.Visible;
        TrFlickerRow.Visibility = SelectedTag<BoltStyle>(TrStyleCombo) == BoltStyle.Rainbow
            ? Visibility.Visible : Visibility.Collapsed;
        TrReactiveFieldsPanel.Visibility = TrEnabledCheck.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // See FireGradientLive's comment (MainWindow.Gradient.cs) for why this
    // is gated here rather than inside OnTrLiveApplyAsync.
    private void FireTrLive()
    {
        if (_suppressLiveApply) return;
        _trDebounce?.Fire();
    }

    private async Task OnTrLiveApplyAsync()
    {
        if (TrLiveCheck.IsChecked != true) return;
        await ApplyCurrentLiveAsync();
    }

    // The single place that decides whether the currently-tuned
    // gradient/custom-keys should go out wrapped in typing_reactive or on
    // its own -- both this panel's and the Gradient panel's live-apply
    // funnel through here, matching app.js's applyCurrentLive() exactly.
    private async Task ApplyCurrentLiveAsync()
    {
        if (TrEnabledCheck.IsChecked == true)
        {
            await _api.SetEffectAsync("typing_reactive", BuildTypingReactiveParams());
        }
        else if (TrUseGradientCheck.IsChecked == true)
        {
            await _api.SetEffectAsync("gradient", BuildGradientParams());
        }
        else if (TrUseCustomKeysCheck.IsChecked == true)
        {
            await _api.SetEffectAsync("custom_keys", BuildCustomKeysParams());
        }
        else
        {
            await _api.SetEffectAsync("static", new StaticParams { Color = TrBaseSwatch.Color.ToRgbColor() });
        }
        _activePresetName = null;
        await RefreshPresetsAsync();
    }

    private void TrOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady) return;
        UpdateTypingReactiveLabels();
        FireTrLive();
    }

    private void TrOption_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady) return;
        UpdateTypingReactiveLabels();
        FireTrLive();
    }

    private void TrOption_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady) return;
        UpdateTypingReactiveLabels();
        FireTrLive();
    }

    private void TrStyleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady) return;
        UpdateTypingReactiveLabels(); // also toggles the flicker-speed row
        FireTrLive();
    }

    // "Use Gradient" and "Use Custom Key Colors" are mutually exclusive --
    // checking one always unchecks the other, so BaseEffectName (a single
    // string on the daemon side) is never ambiguous. Both unchecked falls
    // back to the flat base color. Matches app.js exactly.
    private void TrUseGradientCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady) return;
        if (TrUseGradientCheck.IsChecked == true) TrUseCustomKeysCheck.IsChecked = false;
        UpdateTypingReactiveLabels();
        FireTrLive();
    }

    private void TrUseCustomKeysCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady) return;
        if (TrUseCustomKeysCheck.IsChecked == true) TrUseGradientCheck.IsChecked = false;
        UpdateTypingReactiveLabels();
        FireTrLive();
    }

    // Loads a typing_reactive preset's params into this panel (and, via
    // LoadGradientParams/LoadCustomKeysParams, the panel it's using as a
    // background) -- ported from app.js's syncTuningPanelsFromPreset.
    private void LoadTypingReactiveParams(TypingReactiveParams p)
    {
        TrBrightSwatch.Color = p.BrightColor.ToMediaColor();
        TrDecaySlider.Value = p.DecaySeconds;
        SelectByTag(TrShapeCombo, p.BoltShape);
        SelectByTag(TrStyleCombo, p.BoltStyle);
        TrSpeedSlider.Value = p.BoltSpeed;
        TrTailSlider.Value = p.BoltTail;
        TrMaxDistSlider.Value = p.BoltMaxDistance;
        TrBoltResetCheck.IsChecked = p.BoltReset;
        TrFlickerSlider.Value = p.BoltFlickerSpeed;

        TrUseGradientCheck.IsChecked = p.BaseEffectName == "gradient";
        TrUseCustomKeysCheck.IsChecked = p.BaseEffectName == "custom_keys";
        if (p.BaseEffectName == "gradient" && p.BaseParams is GradientParams gp)
        {
            LoadGradientParams(gp);
        }
        else if (p.BaseEffectName == "custom_keys" && p.BaseParams is CustomKeysParams ckp)
        {
            LoadCustomKeysParams(ckp);
        }
        else
        {
            TrBaseSwatch.Color = p.BaseColor.ToMediaColor();
        }
        UpdateTypingReactiveLabels();
    }
}
