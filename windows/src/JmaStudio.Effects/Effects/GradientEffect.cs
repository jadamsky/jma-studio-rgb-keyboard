// Port of effects/gradient.py -- see that file's docstring on `main`
// for the full field-by-field legacy-vs-multi-zone explanation. Ported
// verbatim in spirit: `Colors`/`Boundaries` given -> multi-zone mode;
// otherwise -> legacy 2-zone mode using LeftColor/RightColor/Boundary
// plus the override/custom-color patches (kept so presets built before
// multi-zone support keep rendering identically).

using JmaStudio.Hardware;

namespace JmaStudio.Effects.Effects;

public sealed class GradientEffect : Effect<GradientParams>
{
    private readonly IReadOnlyDictionary<int, GridPosition> _positions;
    private readonly IReadOnlyDictionary<string, int> _indexByName;
    private readonly double _minCol, _maxCol;

    public GradientEffect(IReadOnlyDictionary<int, GridPosition> positions, IReadOnlyDictionary<string, int> indexByName)
    {
        _positions = positions;
        _indexByName = indexByName;
        if (positions.Count > 0)
        {
            _minCol = positions.Values.Min(p => p.Col);
            _maxCol = positions.Values.Max(p => p.Col);
        }
        else
        {
            _minCol = 0.0; _maxCol = 1.0;
        }
    }

    public override string Name => "gradient";

    private static int ZoneIndex(double col, IReadOnlyList<double> boundaries, int numZones)
    {
        int zone = 0;
        foreach (double b in boundaries)
        {
            if (col >= b) zone++;
            else break;
        }
        return Math.Min(zone, numZones - 1);
    }

    private RgbColor SmoothColor(double col, IReadOnlyList<RgbColor> zoneColors, IReadOnlyList<double> boundaries)
    {
        var stops = new List<(double Pos, RgbColor Color)> { (_minCol, zoneColors[0]) };
        for (int i = 0; i < boundaries.Count; i++)
        {
            stops.Add((boundaries[i], zoneColors[i + 1]));
        }
        stops.Add((_maxCol, zoneColors[^1]));

        for (int i = 0; i < stops.Count - 1; i++)
        {
            (double c0Pos, RgbColor c0) = stops[i];
            (double c1Pos, RgbColor c1) = stops[i + 1];
            if (col <= c1Pos || i == stops.Count - 2)
            {
                double span = (c1Pos - c0Pos) is 0.0 ? 1.0 : c1Pos - c0Pos;
                double frac = Math.Clamp((col - c0Pos) / span, 0.0, 1.0);
                return ColorMath.Lerp(c0, c1, frac);
            }
        }
        return zoneColors[^1];
    }

    protected override RgbColor[] RenderTyped(double t, int numCells, GradientParams p, EffectContext context)
    {
        IReadOnlyList<RgbColor> zoneColors;
        IReadOnlyList<double> boundaries;
        bool legacyMode;

        if (p.Colors is { Count: > 0 })
        {
            zoneColors = p.Colors;
            boundaries = (p.Boundaries ?? Array.Empty<double>()).OrderBy(b => b).ToArray();
            legacyMode = false;
        }
        else
        {
            zoneColors = new[] { p.LeftColor, p.RightColor };
            boundaries = new[] { p.Boundary ?? (_minCol + _maxCol) / 2 };
            legacyMode = true;
        }

        var colors = Enumerable.Repeat(zoneColors[0], numCells).ToArray();
        for (int idx = 0; idx < numCells; idx++)
        {
            if (!_positions.TryGetValue(idx, out GridPosition pos)) continue;
            colors[idx] = p.Hard
                ? zoneColors[ZoneIndex(pos.Col, boundaries, zoneColors.Count)]
                : SmoothColor(pos.Col, zoneColors, boundaries);
        }

        if (legacyMode)
        {
            foreach (string name in p.LeftOverrides)
            {
                if (_indexByName.TryGetValue(name, out int idx)) colors[idx] = zoneColors[0];
            }
            foreach (string name in p.RightOverrides)
            {
                if (_indexByName.TryGetValue(name, out int idx)) colors[idx] = zoneColors[^1];
            }
            foreach ((string name, RgbColor color) in p.CustomColors)
            {
                if (_indexByName.TryGetValue(name, out int idx)) colors[idx] = color;
            }
        }

        if (p.Brightness != 1.0)
        {
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = ColorMath.Scale(colors[i], p.Brightness);
            }
        }

        return colors;
    }
}
