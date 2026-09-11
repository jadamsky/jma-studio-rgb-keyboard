using JmaStudio.Hardware;

namespace JmaStudio.Effects;

public static class ColorMath
{
    /// <summary>Port of Python's colorsys.hsv_to_rgb (h, s, v all in
    /// [0,1]) -- every call site in the ported effects uses s=v=1.0, but
    /// this stays a faithful general port rather than a special-cased
    /// shortcut, so it stays correct if a future effect varies s/v.</summary>
    public static RgbColor Hsv(double h, double s, double v)
    {
        double r, g, b;
        if (s == 0.0)
        {
            r = g = b = v;
        }
        else
        {
            double h6 = h * 6.0;
            int i = (int)h6;
            double f = h6 - i;
            double p = v * (1.0 - s);
            double q = v * (1.0 - s * f);
            double t = v * (1.0 - s * (1.0 - f));
            switch (i % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break; // case 5
            }
        }
        return new RgbColor((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    /// <summary>Channel-wise int(a + (b-a)*frac), matching the truncating
    /// (not rounding) int() casts used throughout the Python effects.</summary>
    public static RgbColor Lerp(RgbColor a, RgbColor b, double frac) => new(
        (byte)(a.R + (b.R - a.R) * frac),
        (byte)(a.G + (b.G - a.G) * frac),
        (byte)(a.B + (b.B - a.B) * frac));

    /// <summary>Channel-wise int(c * factor), matching Python's `int(c * v)`.</summary>
    public static RgbColor Scale(RgbColor c, double factor) => new(
        (byte)(c.R * factor),
        (byte)(c.G * factor),
        (byte)(c.B * factor));

    /// <summary>True mathematical modulo (always non-negative for a
    /// positive divisor), matching Python's `%` -- C#'s `%` is a
    /// remainder operator that keeps the dividend's sign instead.</summary>
    public static double PositiveMod(double a, double m) => ((a % m) + m) % m;
}
