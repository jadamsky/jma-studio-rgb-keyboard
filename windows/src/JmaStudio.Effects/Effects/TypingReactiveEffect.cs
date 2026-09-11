// Port of effects/typing_reactive.py. The whole board sits at a base
// color (or another effect's live output, via BaseEffectName). Each
// pressed key flashes bright and fades back to base in place, AND
// sends a chasing bolt outward across the physical grid that fades as
// it travels. Reads live keypress timing from EffectContext.KeyState
// (the C# analogue of Python's params['key_state'] render-loop injection).
//
// One deliberate behavior difference from Python, worth flagging: if
// BaseEffectName names a registered effect but BaseParams is either
// null or the wrong concrete EffectParams subtype for that effect,
// this falls back to that effect's own defaults (via Effect<TParams>.
// Render's built-in cast-or-default behavior) rather than Python's
// "any dict works, unrecognized keys are just ignored" duck typing.
// Not expected to matter in practice -- BaseParams should always be
// constructed for the effect BaseEffectName actually names.

using JmaStudio.Hardware;

namespace JmaStudio.Effects.Effects;

public sealed class TypingReactiveEffect : Effect<TypingReactiveParams>
{
    private readonly IReadOnlyDictionary<int, GridPosition> _positions;

    public TypingReactiveEffect(IReadOnlyDictionary<int, GridPosition> positions)
    {
        _positions = positions;
    }

    public override string Name => "typing_reactive";

    private static (double Row, double Col) Unit((int Row, int Col) vec)
    {
        double length = Math.Sqrt(vec.Row * (double)vec.Row + vec.Col * (double)vec.Col);
        return (vec.Row / length, vec.Col / length);
    }

    private static RgbColor[] ResolveBaseColors(double t, int numCells, TypingReactiveParams p, EffectContext context)
    {
        if (string.IsNullOrEmpty(p.BaseEffectName))
        {
            return Enumerable.Repeat(p.BaseColor, numCells).ToArray();
        }
        IEffect? baseEffect = context.Registry?.TryGet(p.BaseEffectName);
        if (baseEffect is null)
        {
            return Enumerable.Repeat(p.BaseColor, numCells).ToArray();
        }
        EffectParams baseParams = p.BaseParams ?? baseEffect.DefaultParams;
        return baseEffect.Render(t, numCells, baseParams, context);
    }

    protected override RgbColor[] RenderTyped(double t, int numCells, TypingReactiveParams p, EffectContext context)
    {
        RgbColor[] baseColors = ResolveBaseColors(t, numCells, p, context);
        IReadOnlyDictionary<int, IReadOnlyList<double>> keyState = context.KeyState;

        var flashIntensity = new double[numCells];
        var boltIntensity = new double[numCells];

        // In-place flash for the pressed key itself. Each press fades
        // independently; if the same key was hit more than once
        // recently, take whichever press is currently brightest --
        // unless BoltReset is on, in which case only the single most
        // recent press (smallest elapsed) counts.
        if (p.DecaySeconds > 0)
        {
            foreach ((int idx, IReadOnlyList<double> elapsedList) in keyState)
            {
                if (idx < 0 || idx >= numCells || elapsedList.Count == 0) continue;
                IEnumerable<double> elapsedValues = p.BoltReset ? new[] { elapsedList.Min() } : elapsedList;
                foreach (double elapsed in elapsedValues)
                {
                    double v = Math.Max(0.0, 1.0 - elapsed / p.DecaySeconds);
                    if (v > flashIntensity[idx]) flashIntensity[idx] = v;
                }
            }
        }

        // Chasing bolts outward from each press across the physical grid.
        if (p.Bolts && p.BoltTail > 0 && _positions.Count > 0)
        {
            var unitDirs = p.BoltShape == BoltShape.Rays
                ? p.BoltDirections.Select(Unit).ToArray()
                : Array.Empty<(double Row, double Col)>();

            foreach ((int originKey, IReadOnlyList<double> elapsedList) in keyState)
            {
                if (!_positions.TryGetValue(originKey, out GridPosition originPos) || elapsedList.Count == 0) continue;
                IEnumerable<double> elapsedValues = p.BoltReset ? new[] { elapsedList.Min() } : elapsedList;
                foreach (double elapsed in elapsedValues)
                {
                    double headDistance = elapsed * p.BoltSpeed;
                    foreach ((int targetIdx, GridPosition targetPos) in _positions)
                    {
                        double dr = targetPos.Row - originPos.Row;
                        double dc = targetPos.Col - originPos.Col;
                        if (dr == 0 && dc == 0) continue; // origin cell already handled above

                        if (p.BoltShape == BoltShape.Radial)
                        {
                            // True 360 degree ring: only straight-line
                            // distance from the origin matters.
                            double distance = Math.Sqrt(dr * dr + dc * dc);
                            if (distance > p.BoltMaxDistance) continue;
                            double tail = headDistance - distance;
                            if (tail < 0 || tail > p.BoltTail) continue;
                            double v = 1.0 - (tail / p.BoltTail);
                            if (v > boltIntensity[targetIdx]) boltIntensity[targetIdx] = v;
                            continue;
                        }

                        // "rays": project the offset onto each requested
                        // direction; a cell "belongs" to that ray if
                        // close enough to the line and within the
                        // fading tail behind the head.
                        foreach ((double ur, double uc) in unitDirs)
                        {
                            double projection = dr * ur + dc * uc;
                            if (projection <= 0 || projection > p.BoltMaxDistance) continue;
                            double perp = Math.Sqrt(Math.Pow(dr - projection * ur, 2) + Math.Pow(dc - projection * uc, 2));
                            if (perp > p.BoltTolerance) continue;
                            double tail = headDistance - projection;
                            if (tail < 0 || tail > p.BoltTail) continue;
                            double v = 1.0 - (tail / p.BoltTail);
                            if (v > boltIntensity[targetIdx]) boltIntensity[targetIdx] = v;
                        }
                    }
                }
            }
        }

        int tick = (int)(t * p.BoltFlickerSpeed);
        var colors = new RgbColor[numCells];
        for (int i = 0; i < numCells; i++)
        {
            RgbColor cellBase = baseColors[i];
            double fv = flashIntensity[i];
            double bv = boltIntensity[i];
            double v;
            RgbColor target;
            if (bv > 0 && bv >= fv)
            {
                v = bv;
                target = p.BoltStyle == BoltStyle.Rainbow
                    ? ColorMath.Hsv(PseudoRandom.Value01(i, tick), 1.0, 1.0)
                    : p.BrightColor;
            }
            else if (fv > 0)
            {
                v = fv;
                target = p.BrightColor;
            }
            else
            {
                colors[i] = cellBase;
                continue;
            }
            colors[i] = ColorMath.Lerp(cellBase, target, v);
        }
        return colors;
    }
}
