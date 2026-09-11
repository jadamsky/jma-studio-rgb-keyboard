// Effects that need real physical (row, col) key positions -- ported
// from effects/color_wipe.py, comet.py, scanner.py, aurora.py,
// ripple.py, rain.py on `main`. Each takes the {cellIndex: GridPosition}
// map via constructor injection (computed once by EffectRegistry from
// keymap.json, mirroring the Python modules' own "load once at import
// time" pattern -- re-create the registry after editing keymap.json,
// same operational characteristic as restarting the Python daemon).

using JmaStudio.Hardware;

namespace JmaStudio.Effects.Effects;

/// <summary>A solid color progressively fills the keyboard from one side
/// to the other, then the next color wipes over it.</summary>
public sealed class ColorWipeEffect : Effect<ColorWipeParams>
{
    private readonly IReadOnlyDictionary<int, GridPosition> _positions;
    private readonly double _minCol, _maxCol;

    public ColorWipeEffect(IReadOnlyDictionary<int, GridPosition> positions)
    {
        _positions = positions;
        if (positions.Count > 0)
        {
            _minCol = positions.Values.Min(p => p.Col);
            _maxCol = positions.Values.Max(p => p.Col);
        }
        else
        {
            _minCol = 0.0; _maxCol = 20.0;
        }
    }

    public override string Name => "color_wipe";

    protected override RgbColor[] RenderTyped(double t, int numCells, ColorWipeParams p, EffectContext context)
    {
        double span = (_maxCol - _minCol) is 0.0 ? 1.0 : _maxCol - _minCol;
        if (_positions.Count == 0 || p.Colors.Count == 0)
        {
            return new RgbColor[numCells];
        }

        double progress = (t * p.Speed) / span;
        int paletteLen = p.Colors.Count;
        int step = ((int)progress % paletteLen + paletteLen) % paletteLen;
        double frac = progress - Math.Floor(progress);
        double frontCol = _minCol + frac * span;
        RgbColor current = p.Colors[step];
        RgbColor previous = p.Colors[((step - 1) % paletteLen + paletteLen) % paletteLen];

        var colors = new RgbColor[numCells];
        for (int idx = 0; idx < numCells; idx++)
        {
            if (!_positions.TryGetValue(idx, out GridPosition pos))
            {
                colors[idx] = current;
                continue;
            }
            colors[idx] = pos.Col <= frontCol ? current : previous;
        }
        return colors;
    }
}

/// <summary>A bright head with a fading tail sweeps continuously across
/// the keyboard left-to-right and wraps around.</summary>
public sealed class CometEffect : Effect<CometParams>
{
    private readonly IReadOnlyDictionary<int, GridPosition> _positions;
    private readonly double _minCol, _maxCol;

    public CometEffect(IReadOnlyDictionary<int, GridPosition> positions)
    {
        _positions = positions;
        if (positions.Count > 0)
        {
            _minCol = positions.Values.Min(p => p.Col);
            _maxCol = positions.Values.Max(p => p.Col);
        }
        else
        {
            _minCol = 0.0; _maxCol = 20.0;
        }
    }

    public override string Name => "comet";

    protected override RgbColor[] RenderTyped(double t, int numCells, CometParams p, EffectContext context)
    {
        double span = (_maxCol - _minCol) + p.Tail;
        if (span <= 0 || _positions.Count == 0)
        {
            return Enumerable.Repeat(p.BaseColor, numCells).ToArray();
        }

        double headCol = _minCol + ColorMath.PositiveMod(t * p.Speed, span) - p.Tail;

        var colors = new RgbColor[numCells];
        for (int idx = 0; idx < numCells; idx++)
        {
            if (!_positions.TryGetValue(idx, out GridPosition pos))
            {
                colors[idx] = p.BaseColor;
                continue;
            }
            double behind = headCol - pos.Col;
            if (behind >= 0 && behind <= p.Tail)
            {
                double v = 1.0 - (behind / p.Tail);
                colors[idx] = ColorMath.Lerp(p.BaseColor, p.Color, v);
            }
            else
            {
                colors[idx] = p.BaseColor;
            }
        }
        return colors;
    }
}

/// <summary>A single bright band sweeps back and forth across the
/// keyboard (Knight Rider-style ping-pong scan).</summary>
public sealed class ScannerEffect : Effect<ScannerParams>
{
    private readonly IReadOnlyDictionary<int, GridPosition> _positions;
    private readonly double _minCol, _maxCol;

    public ScannerEffect(IReadOnlyDictionary<int, GridPosition> positions)
    {
        _positions = positions;
        if (positions.Count > 0)
        {
            _minCol = positions.Values.Min(p => p.Col);
            _maxCol = positions.Values.Max(p => p.Col);
        }
        else
        {
            _minCol = 0.0; _maxCol = 20.0;
        }
    }

    public override string Name => "scanner";

    protected override RgbColor[] RenderTyped(double t, int numCells, ScannerParams p, EffectContext context)
    {
        double span = _maxCol - _minCol;
        if (span <= 0 || _positions.Count == 0)
        {
            return Enumerable.Repeat(p.BaseColor, numCells).ToArray();
        }

        double cycle = span * 2;
        double localT = ColorMath.PositiveMod(t * p.Speed, cycle);
        double posCol = (localT <= span ? localT : cycle - localT) + _minCol; // ping-pong

        var colors = new RgbColor[numCells];
        for (int idx = 0; idx < numCells; idx++)
        {
            if (!_positions.TryGetValue(idx, out GridPosition pos))
            {
                colors[idx] = p.BaseColor;
                continue;
            }
            double dist = Math.Abs(pos.Col - posCol);
            if (dist < p.Width)
            {
                double v = 1.0 - (dist / p.Width);
                colors[idx] = ColorMath.Lerp(p.BaseColor, p.Color, v);
            }
            else
            {
                colors[idx] = p.BaseColor;
            }
        }
        return colors;
    }
}

/// <summary>Slow flowing waves of green, blue, and purple across the
/// keyboard, resembling the northern lights.</summary>
public sealed class AuroraEffect : Effect<AuroraParams>
{
    private static readonly RgbColor[] Palette =
    {
        new(10, 20, 40),    // deep night blue
        new(20, 200, 140),  // aurora green
        new(60, 90, 220),   // blue
        new(150, 60, 220),  // purple
    };

    private readonly IReadOnlyDictionary<int, GridPosition> _positions;

    public AuroraEffect(IReadOnlyDictionary<int, GridPosition> positions)
    {
        _positions = positions;
    }

    public override string Name => "aurora";

    /// <summary>f in [0,1) -> smooth blend walking around the palette ring.</summary>
    private static RgbColor PaletteColor(double f)
    {
        int n = Palette.Length;
        double scaled = (((f % 1.0) + 1.0) % 1.0) * n;
        int i0 = (int)scaled % n;
        int i1 = (i0 + 1) % n;
        double frac = scaled - Math.Floor(scaled);
        return ColorMath.Lerp(Palette[i0], Palette[i1], frac);
    }

    protected override RgbColor[] RenderTyped(double t, int numCells, AuroraParams p, EffectContext context)
    {
        var colors = new RgbColor[numCells];
        for (int i = 0; i < numCells; i++)
        {
            if (!_positions.TryGetValue(i, out GridPosition pos))
            {
                colors[i] = Palette[0];
                continue;
            }
            double wave =
                Math.Sin(pos.Col * p.Scale + t * p.Speed * 4) * 0.5
                + Math.Sin(pos.Row * p.Scale * 2 - t * p.Speed * 2.5) * 0.5;
            double f = (wave + 2) / 4; // roughly normalize to [0,1]
            colors[i] = PaletteColor(f);
        }
        return colors;
    }
}

/// <summary>Concentric rings of color continuously emanate outward from
/// the center of the keyboard, fading as they travel.</summary>
public sealed class RippleEffect : Effect<RippleParams>
{
    private readonly IReadOnlyDictionary<int, GridPosition> _positions;
    private readonly double _centerRow, _centerCol;

    public RippleEffect(IReadOnlyDictionary<int, GridPosition> positions)
    {
        _positions = positions;
        if (positions.Count > 0)
        {
            _centerRow = positions.Values.Average(p => p.Row);
            _centerCol = positions.Values.Average(p => p.Col);
        }
    }

    public override string Name => "ripple";

    protected override RgbColor[] RenderTyped(double t, int numCells, RippleParams p, EffectContext context)
    {
        var colors = Enumerable.Repeat(p.BaseColor, numCells).ToArray();
        if (_positions.Count == 0 || p.Interval <= 0) return colors;

        // However many ripples have "emitted" by time t, each aging
        // independently -- only the last few are still visible.
        int nEmitted = (int)(t / p.Interval) + 1;
        for (int n = Math.Max(0, nEmitted - 4); n < nEmitted; n++)
        {
            double age = t - (n * p.Interval);
            if (age < 0) continue;
            double radius = age * p.Speed;
            foreach ((int idx, GridPosition pos) in _positions)
            {
                double dist = Math.Sqrt(Math.Pow(pos.Row - _centerRow, 2) + Math.Pow(pos.Col - _centerCol, 2));
                double delta = Math.Abs(dist - radius);
                if (delta >= p.Width) continue;
                double v = 1.0 - (delta / p.Width);
                colors[idx] = ColorMath.Lerp(colors[idx], p.Color, v);
            }
        }
        return colors;
    }
}

/// <summary>Bright droplets fall down each column of keys continuously,
/// fading as they go.</summary>
public sealed class RainEffect : Effect<RainParams>
{
    private readonly IReadOnlyDictionary<int, GridPosition> _positions;
    private readonly double _minCol, _maxCol, _minRow, _maxRow;

    public RainEffect(IReadOnlyDictionary<int, GridPosition> positions)
    {
        _positions = positions;
        if (positions.Count > 0)
        {
            _minCol = positions.Values.Min(p => p.Col);
            _maxCol = positions.Values.Max(p => p.Col);
            _minRow = positions.Values.Min(p => p.Row);
            _maxRow = positions.Values.Max(p => p.Row);
        }
        else
        {
            _minCol = 0.0; _maxCol = 20.0; _minRow = 0.0; _maxRow = 5.0;
        }
    }

    public override string Name => "rain";

    protected override RgbColor[] RenderTyped(double t, int numCells, RainParams p, EffectContext context)
    {
        double speed = Math.Max(0.01, p.Speed);
        var colors = Enumerable.Repeat(p.BaseColor, numCells).ToArray();
        if (_positions.Count == 0 || p.SpawnRate <= 0) return colors;

        // A fixed pool of "drop slots", each periodically restarting at
        // its own pseudo-random column and phase -- looks like
        // independent random drops with no state persisted between frames.
        double rowSpan = (_maxRow - _minRow) + p.Tail;
        int numSlots = Math.Max(1, (int)(p.SpawnRate * rowSpan / speed));
        for (int slot = 0; slot < numSlots; slot++)
        {
            double cycle = rowSpan / speed + PseudoRandom.Value01(slot, 9) * 0.5;
            double col = _minCol + PseudoRandom.Value01(slot, 1) * (_maxCol - _minCol);
            double phaseOffset = PseudoRandom.Value01(slot, 2) * cycle;
            double localT = ColorMath.PositiveMod(t + phaseOffset, cycle);
            double headRow = _minRow + localT * speed;
            foreach ((int idx, GridPosition pos) in _positions)
            {
                if (Math.Abs(pos.Col - col) > 0.6) continue;
                double behind = headRow - pos.Row;
                if (behind < 0 || behind > p.Tail) continue;
                double v = 1.0 - (behind / p.Tail);
                colors[idx] = ColorMath.Lerp(colors[idx], p.Color, v);
            }
        }
        return colors;
    }
}
