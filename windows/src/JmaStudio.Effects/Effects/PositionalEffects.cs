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
        //
        // Bug fixed here (found live, 2026-09-11): the column used to be
        // PseudoRandom.Value01(slot, 1) with no salt -- a pure function of
        // the slot index alone, so with the default params (spawn_rate=3,
        // speed=10, tail=2.5) numSlots computed to just 2, and those 2
        // slots' columns were fixed at the same ~2 values FOREVER, every
        // session. On an ~18-column-wide keyboard that meant rain only
        // ever fell in a narrow strip near the far-left edge and visually
        // never reached the lower rows at all (row 5's populated columns
        // start around column 9). Fix: fold in which CYCLE ITERATION this
        // slot is currently on (via `salt`) so each time a lane restarts
        // its fall, it re-rolls to a genuinely new column instead of
        // reusing the same one forever -- still a pure function of `t`,
        // no state persisted, same spawn_rate/speed/tail semantics, just
        // no longer permanently stuck at a couple of fixed columns.
        double rowSpan = (_maxRow - _minRow) + p.Tail;

        // Shower intensity: rather than a constant density, SpawnRate is
        // treated as the PEAK, and the effective density slowly drifts
        // down to half that and back -- a real rain shower eases up and
        // down over time rather than falling at one constant rate. Two
        // sine waves with unrelated periods (~78s and ~300s) are summed
        // so the drift never feels mechanically repetitive within a
        // normal session, while staying a pure function of `t` (no state
        // persisted between frames, matching this whole file's design).
        //
        // A raw sine only touches its low point for an instant before
        // curving straight back up -- found live: "never feels like it
        // ramped down, only a quick moment." Reshaped via Math.Pow below
        // to spend real, sustained time near both the low and high ends
        // and transition faster through the middle, instead of a smooth
        // curve that spends most of its time transitioning.
        // Time since THIS effect was activated, not raw Service uptime --
        // `t` alone would put the shower at an arbitrary point in its
        // cycle whenever the effect happens to be switched on. Using -π/2
        // as both terms' phase makes rawWave equal exactly -1 (the lowest
        // possible point) at tSinceActivation=0, per the user's request
        // to "always start at the lower peak so it always builds up at
        // the start."
        double tSinceActivation = t - context.EffectStartTime;
        double highSpawnRate = p.SpawnRate;
        double lowSpawnRate = 2.0;
        double rawWave = Math.Sin(tSinceActivation * 0.08 - Math.PI / 2) * 0.6
            + Math.Sin(tSinceActivation * 0.021 - Math.PI / 2) * 0.4;
        // Asymmetric reshaping, per the user's request: the low end should
        // linger roughly twice as long as the high end. rawWave < 0 maps to
        // the low half (wave < 0.5) -- a smaller exponent flattens/lingers
        // MORE (see the symmetric version's own comment above), so the
        // negative (low) side gets a noticeably smaller exponent than the
        // positive (high) side.
        double shapedWave = rawWave >= 0
            ? Math.Pow(rawWave, 0.6)
            : -Math.Pow(-rawWave, 0.25);
        double wave = Math.Clamp(0.5 + 0.5 * shapedWave, 0.0, 1.0);
        double currentSpawnRate = lowSpawnRate + (highSpawnRate - lowSpawnRate) * wave;

        // numSlots is always sized for the PEAK rate, so the slot pool
        // (and each slot's own column/phase) never changes shape -- the
        // current shower intensity just controls how many of those slots
        // are active right now, counting up from slot 0. Only the ONE
        // slot currently straddling the threshold gets a fractional
        // alpha, so raising/lowering the intensity fades that single
        // slot in/out smoothly instead of every slot popping on or off
        // at once.
        int numSlots = Math.Max(1, (int)(highSpawnRate * rowSpan / speed));
        double activeSlotsF = Math.Clamp(currentSpawnRate * rowSpan / speed, 0.0, numSlots);
        int fullSlots = (int)Math.Floor(activeSlotsF);
        double partialAlpha = activeSlotsF - fullSlots;

        for (int slot = 0; slot < numSlots; slot++)
        {
            double slotAlpha = slot < fullSlots ? 1.0 : slot == fullSlots ? partialAlpha : 0.0;
            if (slotAlpha <= 0.0) continue;

            double cycle = rowSpan / speed + PseudoRandom.Value01(slot, 9) * 0.5;
            double phaseOffset = PseudoRandom.Value01(slot, 2) * cycle;
            double localTRaw = t + phaseOffset;
            int cycleIndex = (int)Math.Floor(localTRaw / cycle);
            double col = _minCol + PseudoRandom.Value01(slot, 1, cycleIndex) * (_maxCol - _minCol);
            double localT = ColorMath.PositiveMod(localTRaw, cycle);
            double headRow = _minRow + localT * speed;
            foreach ((int idx, GridPosition pos) in _positions)
            {
                if (Math.Abs(pos.Col - col) > 0.6) continue;
                double behind = headRow - pos.Row;
                // A cell used to snap straight to full brightness the
                // instant the drop's head reached it (v=1.0 at behind=0),
                // then fade out over the tail -- no ramp-up at all, just a
                // sudden pop. Added a short fade-IN ahead of the head
                // (behind in [-FadeInRows, 0)) so a cell brightens quickly
                // as the drop arrives instead of snapping on, then the
                // existing fade-out behavior continues unchanged.
                const double FadeInRows = 0.5;
                if (behind < -FadeInRows || behind > p.Tail) continue;
                double v = (behind < 0
                    ? 1.0 - (-behind / FadeInRows)
                    : 1.0 - (behind / p.Tail)) * slotAlpha;
                colors[idx] = ColorMath.Lerp(colors[idx], p.Color, v);
            }
        }

        // Accent drop: exactly one extra lane, in AccentColor, appearing
        // once per a randomized gap (uniform between AccentMinGapSeconds
        // and AccentMaxGapSeconds) -- NOT scaled by SpawnRate/density like
        // the main slots above, since this is meant to read as a rare,
        // occasional highlight rather than part of the regular shower.
        // Each gap's length is random (not a fixed cycle), so unlike the
        // main slots there's no closed-form "which cycle am I in" --
        // walk forward from activation summing randomized gaps until
        // passing tSinceActivation. Capped defensively; in practice this
        // is a handful of iterations even after hours of uptime (average
        // gap ~6.5s), negligible next to the per-cell work already done
        // for every main-slot drop above.
        {
            double accentCycleStart = 0.0;
            int accentCycleIndex = 0;
            double accentGapRange = Math.Max(0.01, p.AccentMaxGapSeconds - p.AccentMinGapSeconds);
            while (accentCycleIndex < 100_000)
            {
                double gap = p.AccentMinGapSeconds + PseudoRandom.Value01(9999, 9999, accentCycleIndex) * accentGapRange;
                if (accentCycleStart + gap > tSinceActivation) break;
                accentCycleStart += gap;
                accentCycleIndex++;
            }
            double accentLocalT = tSinceActivation - accentCycleStart;
            double accentCol = _minCol + PseudoRandom.Value01(9999, 1, accentCycleIndex) * (_maxCol - _minCol);
            double accentHeadRow = _minRow + accentLocalT * speed;
            foreach ((int idx, GridPosition pos) in _positions)
            {
                if (Math.Abs(pos.Col - accentCol) > 0.6) continue;
                double behind = accentHeadRow - pos.Row;
                const double FadeInRows = 0.5;
                if (behind < -FadeInRows || behind > p.Tail) continue;
                double v = behind < 0
                    ? 1.0 - (-behind / FadeInRows)
                    : 1.0 - (behind / p.Tail);
                colors[idx] = ColorMath.Lerp(colors[idx], p.AccentColor, v);
            }
        }
        return colors;
    }
}
