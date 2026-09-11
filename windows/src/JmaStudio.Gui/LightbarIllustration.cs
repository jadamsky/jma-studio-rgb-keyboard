// Port of gui/lightbar.html's recolored bar illustration -- the same
// gui/lightbar_bar.png reference photo, split into 3 zones and
// hue-rotated live to match each zone's actual color. The CSS version
// crops 3 overlapping copies of the full image with a mask-based
// crossfade at each seam and rotates each via a `filter: hue-rotate()`;
// this uses a plain 3-way non-overlapping crop (no crossfade -- a minor,
// disclosed simplification, barely visible since each zone is an
// otherwise-uniform color block) and does the hue rotation itself as a
// real per-pixel HSV transform on a downscaled copy of the source
// bitmap (WPF has no CSS-filter equivalent). Downscaling during decode
// (DecodePixelWidth) keeps that per-pixel pass cheap enough to run on
// every wheel-drag color change, not just on release.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using JmaStudio.Hardware;

namespace JmaStudio.Gui;

public sealed class LightbarIllustration : Canvas
{
    private const int DisplayWidth = 660;

    // Baseline hue actually baked into the reference photo for each zone
    // (matches gui/lightbar.html's own ZONE_BASE_HUE, sampled directly
    // from the image -- the AI-rendered reds/greens/blues aren't
    // perfectly pure, so this has to match the real asset, not an
    // assumed 0/120/240).
    private static readonly IReadOnlyDictionary<int, double> ZoneBaseHue =
        new Dictionary<int, double> { [1] = 354.3, [2] = 109.5, [3] = 215.4 };

    private readonly int _displayHeight;
    private readonly Dictionary<int, CroppedBitmap> _zoneSourceCrops = new();
    private readonly Dictionary<int, Image> _zoneImages = new();
    private readonly Dictionary<int, RgbColor> _currentColors = new()
    {
        [1] = new RgbColor(255, 0, 0), [2] = new RgbColor(0, 255, 0), [3] = new RgbColor(0, 0, 255),
    };
    private readonly Rectangle _glowWide;
    private readonly Rectangle _glowCore;

    public LightbarIllustration()
    {
        BitmapSource source = LoadSource();
        _displayHeight = source.PixelHeight;
        Width = DisplayWidth;
        Height = _displayHeight;
        ClipToBounds = true;

        int zoneWidth = DisplayWidth / 3;
        for (int zone = 1; zone <= 3; zone++)
        {
            int x0 = (zone - 1) * zoneWidth;
            int width = zone == 3 ? DisplayWidth - x0 : zoneWidth; // last zone absorbs rounding
            var crop = new CroppedBitmap(source, new Int32Rect(x0, 0, width, _displayHeight));
            _zoneSourceCrops[zone] = crop;
            var image = new Image { Source = crop, Width = width, Height = _displayHeight };
            SetLeft(image, x0);
            SetTop(image, 0);
            Children.Add(image);
            _zoneImages[zone] = image;
        }

        // Hides the source image's own baked-in top glow band -- a
        // fresh one drawn from the real target colors goes on top of
        // this instead (see UpdateGlow), avoiding the off-hue seam the
        // CSS version has to specifically work around when hue-rotating
        // that continuous gradient strip in 3 independent pieces.
        var stripCover = new Border
        {
            Width = DisplayWidth, Height = _displayHeight * 0.31,
            Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x12)),
        };
        SetLeft(stripCover, 0);
        SetTop(stripCover, 0);
        Children.Add(stripCover);

        _glowWide = new Rectangle
        {
            Width = DisplayWidth * 0.92, Height = _displayHeight * 0.11,
            RadiusX = 999, RadiusY = 999, Opacity = 0.9,
            Effect = new BlurEffect { Radius = 9 },
        };
        SetLeft(_glowWide, DisplayWidth * 0.04);
        SetTop(_glowWide, _displayHeight * 0.16);
        Children.Add(_glowWide);

        _glowCore = new Rectangle
        {
            Width = DisplayWidth * 0.92, Height = _displayHeight * 0.022,
            RadiusX = 999, RadiusY = 999,
        };
        SetLeft(_glowCore, DisplayWidth * 0.04);
        SetTop(_glowCore, _displayHeight * 0.205);
        Children.Add(_glowCore);

        foreach (int zone in new[] { 1, 2, 3 }) RecolorZone(zone);
        UpdateGlow();
    }

    private static BitmapSource LoadSource()
    {
        var decoded = new BitmapImage();
        decoded.BeginInit();
        decoded.UriSource = new Uri("pack://application:,,,/Assets/lightbar_bar.png", UriKind.Absolute);
        decoded.DecodePixelWidth = DisplayWidth; // downscale during decode, keeps per-pixel recoloring cheap
        decoded.CacheOption = BitmapCacheOption.OnLoad;
        decoded.EndInit();
        decoded.Freeze();
        return decoded;
    }

    public void SetZoneColor(int zone, RgbColor color)
    {
        if (!_currentColors.ContainsKey(zone)) return;
        _currentColors[zone] = color;
        RecolorZone(zone);
        UpdateGlow();
    }

    /// <summary>Sets all three zones to the same color in one pass (the
    /// "All Zones" selection) -- avoids recomputing the glow gradient
    /// three times for one logical change.</summary>
    public void SetAllZonesColor(RgbColor color)
    {
        foreach (int zone in new[] { 1, 2, 3 })
        {
            _currentColors[zone] = color;
            RecolorZone(zone);
        }
        UpdateGlow();
    }

    private void RecolorZone(int zone)
    {
        RgbColor c = _currentColors[zone];
        (double hue, double sat, double val) = RgbToHsv(c.R, c.G, c.B);
        double rotate = hue - ZoneBaseHue[zone];
        _zoneImages[zone].Source = RecolorHueRotate(_zoneSourceCrops[zone], rotate, sat, Math.Max(val, 0.04));
    }

    private void UpdateGlow()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        brush.GradientStops.Add(new GradientStop(_currentColors[1].ToMediaColor(), 0));
        brush.GradientStops.Add(new GradientStop(_currentColors[2].ToMediaColor(), 0.5));
        brush.GradientStops.Add(new GradientStop(_currentColors[3].ToMediaColor(), 1));
        brush.Freeze();
        _glowWide.Fill = brush;
        _glowCore.Fill = brush;
    }

    // Applies a hue/saturation/value transform to every opaque pixel of
    // `source`, matching CSS's `hue-rotate(deg) saturate(s) brightness(v)`
    // filter chain closely enough for this purpose (a real HSV shift, not
    // a color-matrix approximation, but visually equivalent here).
    private static WriteableBitmap RecolorHueRotate(BitmapSource source, double hueDeltaDegrees, double satScale, double valueScale)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = converted.PixelWidth, height = converted.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[height * stride];
        converted.CopyPixels(pixels, stride, 0);

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte alpha = pixels[i + 3];
            if (alpha == 0) continue;
            byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            (double h, double s, double v) = RgbToHsv(r, g, b);
            h = (h + hueDeltaDegrees) % 360;
            if (h < 0) h += 360;
            s = Math.Clamp(s * satScale, 0, 1);
            v = Math.Clamp(v * valueScale, 0, 1);
            (byte nr, byte ng, byte nb) = HsvToRgb(h, s, v);
            pixels[i] = nb;
            pixels[i + 1] = ng;
            pixels[i + 2] = nr;
        }

        var result = new WriteableBitmap(width, height, source.DpiX, source.DpiY, PixelFormats.Bgra32, null);
        result.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        return result;
    }

    private static (byte R, byte G, byte B) HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
        double m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

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
}
