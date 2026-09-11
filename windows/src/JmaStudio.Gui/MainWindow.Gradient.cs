// Gradient tuning panel -- ported from gui/index.html's "Gradient" panel
// and gui/app.js's gradient-tuning section (readGradientParams,
// setGradientZoneCount, wireGradientPanel, etc). Zone count/brightness are
// fixed controls declared in MainWindow.xaml; the per-zone color/boundary
// rows are variable-count (2-5 zones) so they're built here into
// GradientColorsPanel/GradientBoundariesPanel, mirroring app.js's
// renderGradientZoneFields().

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JmaStudio.Effects;
using JmaStudio.Hardware;

namespace JmaStudio.Gui;

public partial class MainWindow
{
    // Legacy 2-zone hardware-quirk overrides (left_overrides/right_overrides/
    // custom_colors) carried over from an applied preset -- kept alive across
    // live-tuning as long as the zone count stays at 2, exactly like
    // state.gradientExtra in app.js. Switching to 3+ zones drops it.
    private sealed record GradientLegacyExtra(
        IReadOnlyList<string> LeftOverrides,
        IReadOnlyList<string> RightOverrides,
        IReadOnlyDictionary<string, RgbColor> CustomColors);

    private static readonly RgbColor GradientEndpointA = new(20, 90, 230); // blue
    private static readonly RgbColor GradientEndpointB = new(200, 20, 160); // pink

    private Debouncer? _gradientDebounce;
    private GradientLegacyExtra? _gradientExtra;

    private void InitializeGradientPanel()
    {
        _gradientDebounce = new Debouncer(120, async () => await OnGradientLiveApplyAsync());
        GradientBrightnessSlider.Value = 0.65;
        SetGradientZoneCount(2, DefaultZoneColors(2), DefaultBoundaries(2));
        GradientZoneCountCombo.SelectedIndex = 0;
        UpdateGradientBrightnessLabel();
    }

    private static IReadOnlyList<RgbColor> DefaultZoneColors(int count)
    {
        if (count == 2) return new[] { GradientEndpointA, GradientEndpointB };
        var colors = new List<RgbColor>();
        for (int i = 0; i < count; i++)
        {
            double f = (double)i / (count - 1);
            colors.Add(new RgbColor(
                (byte)Math.Round(GradientEndpointA.R + (GradientEndpointB.R - GradientEndpointA.R) * f),
                (byte)Math.Round(GradientEndpointA.G + (GradientEndpointB.G - GradientEndpointA.G) * f),
                (byte)Math.Round(GradientEndpointA.B + (GradientEndpointB.B - GradientEndpointA.B) * f)));
        }
        return colors;
    }

    private static IReadOnlyList<double> DefaultBoundaries(int count)
    {
        if (count == 2) return new[] { 13.5 };
        const double min = 0, max = 20;
        var boundaries = new List<double>();
        for (int i = 1; i < count; i++)
        {
            boundaries.Add(Math.Round((min + (max - min) * ((double)i / count)) * 4) / 4);
        }
        return boundaries;
    }

    private void SetGradientZoneCount(int count, IReadOnlyList<RgbColor> colors, IReadOnlyList<double> boundaries)
    {
        GradientColorsPanel.Children.Clear();
        GradientBoundariesPanel.Children.Clear();
        var subText = (Brush)FindResource("SubTextBrush");

        for (int i = 0; i < count; i++)
        {
            var swatch = new ColorSwatchButton { Color = colors[i].ToMediaColor() };
            swatch.ColorPicked += _ => FireGradientLive();
            var stack = new StackPanel { Margin = new Thickness(0, 0, 16, 8) };
            stack.Children.Add(new TextBlock { Text = $"Zone {i + 1}", Foreground = subText, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(swatch);
            GradientColorsPanel.Children.Add(stack);
        }

        for (int i = 0; i < count - 1; i++)
        {
            double value = i < boundaries.Count ? boundaries[i] : 0;
            var label = new TextBlock { Foreground = subText, FontSize = 11, Margin = new Thickness(0, 0, 0, 2) };
            var slider = new Slider { Minimum = 0, Maximum = 25, Value = value, Width = 160 };
            void UpdateLabel() => label.Text = $"Boundary {i + 1}  {Math.Round(slider.Value * 4) / 4:0.##}";
            UpdateLabel();
            slider.ValueChanged += (_, _) =>
            {
                // Snap to 0.25 steps, matching the HTML range input's step="0.25".
                double snapped = Math.Round(slider.Value * 4) / 4;
                if (Math.Abs(snapped - slider.Value) > 0.0001) slider.Value = snapped;
                UpdateLabel();
                FireGradientLive();
            };
            var stack = new StackPanel { Margin = new Thickness(0, 0, 20, 8) };
            stack.Children.Add(label);
            stack.Children.Add(slider);
            GradientBoundariesPanel.Children.Add(stack);
        }
    }

    private List<RgbColor> ReadGradientZoneColors() =>
        GradientColorsPanel.Children.OfType<StackPanel>()
            .Select(sp => sp.Children.OfType<ColorSwatchButton>().First().Color.ToRgbColor())
            .ToList();

    private List<double> ReadGradientBoundaries() =>
        GradientBoundariesPanel.Children.OfType<StackPanel>()
            .Select(sp => sp.Children.OfType<Slider>().First().Value)
            .ToList();

    private GradientParams BuildGradientParams()
    {
        var colors = ReadGradientZoneColors();
        var boundaries = ReadGradientBoundaries();
        var p = new GradientParams
        {
            Colors = colors,
            Boundaries = boundaries,
            Brightness = GradientBrightnessSlider.Value,
            Hard = true,
        };
        if (colors.Count == 2 && _gradientExtra is { } extra)
        {
            p = p with
            {
                LeftOverrides = extra.LeftOverrides,
                RightOverrides = extra.RightOverrides,
                CustomColors = extra.CustomColors,
            };
        }
        return p;
    }

    private void UpdateGradientBrightnessLabel() =>
        GradientBrightnessLabel.Text = $"{Math.Round(GradientBrightnessSlider.Value * 100)}%";

    // Guarded at the trigger, not just inside the eventual apply: the
    // apply is debounced ~120ms out, so checking _suppressLiveApply only
    // once the timer fires is too late -- by then startup has already
    // flipped the flag back off, and the debounced call would silently
    // overwrite whatever's actually live on the keyboard a moment after
    // the window opens. Not starting the timer at all during init avoids
    // that entirely.
    private void FireGradientLive()
    {
        if (_suppressLiveApply) return;
        _gradientDebounce?.Fire();
    }

    private async Task OnGradientLiveApplyAsync()
    {
        if (GradientLiveCheck.IsChecked != true) return;
        await ApplyCurrentLiveAsync();
    }

    private void GradientZoneCountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady) return;
        if (GradientZoneCountCombo.SelectedItem is not ComboBoxItem item || item.Content is not string text) return;
        int count = int.Parse(text);
        _gradientExtra = null; // changing zone count abandons legacy overrides, matching app.js
        SetGradientZoneCount(count, DefaultZoneColors(count), DefaultBoundaries(count));
        FireGradientLive();
    }

    private void GradientBrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_uiReady) return;
        UpdateGradientBrightnessLabel();
        FireGradientLive();
    }

    // Loads a gradient (whether from a standalone "gradient" preset or
    // typing_reactive's base_params) into this panel -- ported from
    // app.js's syncTuningPanelsFromPreset's gradientParams handling.
    private void LoadGradientParams(GradientParams p)
    {
        GradientBrightnessSlider.Value = p.Brightness;
        if (p.Colors is { Count: > 0 } colors)
        {
            _gradientExtra = null;
            SetGradientZoneCount(colors.Count, colors, p.Boundaries ?? DefaultBoundaries(colors.Count));
            GradientZoneCountCombo.SelectedIndex = Math.Clamp(colors.Count - 2, 0, 3);
        }
        else
        {
            // Legacy 2-zone preset -- keep its hardware-quirk overrides alive.
            _gradientExtra = new GradientLegacyExtra(p.LeftOverrides, p.RightOverrides, p.CustomColors);
            SetGradientZoneCount(2, new[] { p.LeftColor, p.RightColor }, new[] { p.Boundary ?? 13.5 });
            GradientZoneCountCombo.SelectedIndex = 0;
        }
        UpdateGradientBrightnessLabel();
    }
}
