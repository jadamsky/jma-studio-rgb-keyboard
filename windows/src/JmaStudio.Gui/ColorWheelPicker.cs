// Port of gui/lightbar.html's hue/saturation wheel (drawWheel/
// updateFromWheelPosition) -- WPF has no built-in equivalent. Draws the
// wheel once into a WriteableBitmap (same per-pixel HSV math as the JS
// canvas version), then tracks mouse drags to update hue/saturation; a
// separate Value component (the lightbar page's vertical slider, kept as
// a normal Slider outside this control) is set via the Value property.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace JmaStudio.Gui;

public sealed class ColorWheelPicker : Canvas
{
    public event Action<Color>? ColorChanged;

    private readonly int _size;
    private readonly WriteableBitmap _bitmap;
    private readonly Ellipse _indicator;
    private double _hue; // 0-360
    private double _saturation; // 0-1
    private double _value = 1.0; // 0-1

    public double Value
    {
        get => _value;
        set
        {
            _value = value;
            RaiseColorChanged();
        }
    }

    // A real parameterless constructor is required for XAML instantiation
    // (x:Name) -- a C# optional-parameter constructor still requires an
    // argument at the IL/reflection level, so `ColorWheelPicker(int size =
    // 180)` alone isn't enough; WPF's markup compiler rejected it outright.
    public ColorWheelPicker() : this(180) { }

    public ColorWheelPicker(int size)
    {
        _size = size;
        Width = size;
        Height = size;

        _bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        DrawWheel();

        var image = new Image { Source = _bitmap, Width = size, Height = size, Cursor = Cursors.Cross };
        Children.Add(image);

        _indicator = new Ellipse
        {
            Width = 12, Height = 12, StrokeThickness = 2,
            Stroke = Brushes.White, Fill = Brushes.Transparent, IsHitTestVisible = false,
        };
        Children.Add(_indicator);
        UpdateIndicatorPosition();

        image.MouseLeftButtonDown += (_, e) =>
        {
            image.CaptureMouse();
            UpdateFromPoint(e.GetPosition(image));
        };
        image.MouseMove += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && image.IsMouseCaptured)
            {
                UpdateFromPoint(e.GetPosition(image));
            }
        };
        image.MouseLeftButtonUp += (_, _) => image.ReleaseMouseCapture();
    }

    /// <summary>Sets hue/sat/value from an RGB color without raising
    /// ColorChanged -- for reflecting a color that came from elsewhere
    /// (a preset, an RGB textbox edit), matching app.js's setWheelFromRgb
    /// (..., fromUser=false) pattern that avoids a feedback loop.</summary>
    public void SetFromRgb(byte r, byte g, byte b)
    {
        (_hue, _saturation, _value) = RgbToHsv(r, g, b);
        UpdateIndicatorPosition();
    }

    private void UpdateFromPoint(Point p)
    {
        double radius = _size / 2.0;
        double dx = p.X - radius, dy = p.Y - radius;
        double dist = Math.Min(radius, Math.Sqrt(dx * dx + dy * dy));
        double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (angle < 0) angle += 360;
        _hue = angle;
        _saturation = dist / radius;
        UpdateIndicatorPosition();
        RaiseColorChanged();
    }

    private void UpdateIndicatorPosition()
    {
        double radius = _size / 2.0;
        double rad = _hue * Math.PI / 180.0;
        double dist = _saturation * radius;
        double cx = radius + Math.Cos(rad) * dist;
        double cy = radius + Math.Sin(rad) * dist;
        SetLeft(_indicator, cx - _indicator.Width / 2);
        SetTop(_indicator, cy - _indicator.Height / 2);
    }

    private void RaiseColorChanged()
    {
        (byte r, byte g, byte b) = HsvToRgb(_hue, _saturation, _value);
        ColorChanged?.Invoke(Color.FromRgb(r, g, b));
    }

    private void DrawWheel()
    {
        int size = _size;
        var pixels = new byte[size * size * 4]; // BGRA
        double radius = size / 2.0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double dx = x - radius, dy = y - radius;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                int idx = (y * size + x) * 4;
                if (dist > radius)
                {
                    pixels[idx + 3] = 0;
                    continue;
                }
                double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                if (angle < 0) angle += 360;
                double sat = Math.Min(1, dist / radius);
                (byte r, byte g, byte b) = HsvToRgb(angle, sat, 1.0);
                pixels[idx] = b;
                pixels[idx + 1] = g;
                pixels[idx + 2] = r;
                pixels[idx + 3] = 255;
            }
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
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
