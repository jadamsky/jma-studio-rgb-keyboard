// Effects that need no spatial key-position data -- either purely a
// function of time (whole-board color) or purely a function of cell
// index (no row/col awareness). Ported from effects/static.py,
// mask.py, probe.py, rainbow.py, puke.py, spectrum_cycle.py,
// breathing.py, pulse.py, custom_keys.py, fire.py, confetti.py,
// starlight.py on `main`.

using JmaStudio.Hardware;

namespace JmaStudio.Effects.Effects;

public sealed class StaticEffect : Effect<StaticParams>
{
    public override string Name => "static";

    protected override RgbColor[] RenderTyped(double t, int numCells, StaticParams p, EffectContext context) =>
        Enumerable.Repeat(p.Color, numCells).ToArray();
}

/// <summary>Diagnostic: lights an arbitrary set of cells -- used to
/// visually verify keymap coverage. Not a "real" effect for end use.</summary>
public sealed class MaskEffect : Effect<MaskParams>
{
    public override string Name => "mask";

    protected override RgbColor[] RenderTyped(double t, int numCells, MaskParams p, EffectContext context)
    {
        var colors = new RgbColor[numCells];
        foreach (int i in p.Indices)
        {
            if (i >= 0 && i < numCells) colors[i] = p.Color;
        }
        return colors;
    }
}

/// <summary>Diagnostic: lights exactly one cell -- used by keymap
/// discovery to find which physical key a cell index corresponds to.</summary>
public sealed class ProbeEffect : Effect<ProbeParams>
{
    public override string Name => "probe";

    protected override RgbColor[] RenderTyped(double t, int numCells, ProbeParams p, EffectContext context)
    {
        var colors = new RgbColor[numCells];
        if (p.Index >= 0 && p.Index < numCells) colors[p.Index] = p.Color;
        return colors;
    }
}

/// <summary>Rainbow wave -- hue rotation across all cells over time,
/// staggered by position index (not physical row/col).</summary>
public sealed class RainbowEffect : Effect<RainbowParams>
{
    public override string Name => "rainbow";

    protected override RgbColor[] RenderTyped(double t, int numCells, RainbowParams p, EffectContext context)
    {
        var colors = new RgbColor[numCells];
        for (int i = 0; i < numCells; i++)
        {
            double hue = (i / (double)numCells + t * p.Speed) % 1.0;
            colors[i] = ColorMath.Hsv(hue, 1.0, 1.0);
        }
        return colors;
    }
}

/// <summary>Chaotic per-key rainbow noise -- every cell independently
/// flickers to a new pseudo-random hue at its own rate.</summary>
public sealed class PukeEffect : Effect<PukeParams>
{
    public override string Name => "puke";

    protected override RgbColor[] RenderTyped(double t, int numCells, PukeParams p, EffectContext context)
    {
        int tick = (int)(t * p.Speed);
        var colors = new RgbColor[numCells];
        for (int i = 0; i < numCells; i++)
        {
            double hue = PseudoRandom.Value01(i, tick);
            colors[i] = ColorMath.Hsv(hue, 1.0, 1.0);
        }
        return colors;
    }
}

/// <summary>Every key shows the exact same color at once, sweeping
/// through the hue wheel together (whole board stays in sync).</summary>
public sealed class SpectrumCycleEffect : Effect<SpectrumCycleParams>
{
    public override string Name => "spectrum_cycle";

    protected override RgbColor[] RenderTyped(double t, int numCells, SpectrumCycleParams p, EffectContext context)
    {
        double hue = (t * p.Speed) % 1.0;
        RgbColor color = ColorMath.Hsv(hue, 1.0, 1.0);
        return Enumerable.Repeat(color, numCells).ToArray();
    }
}

/// <summary>The whole keyboard fades a single color smoothly in and out.</summary>
public sealed class BreathingEffect : Effect<BreathingParams>
{
    public override string Name => "breathing";

    protected override RgbColor[] RenderTyped(double t, int numCells, BreathingParams p, EffectContext context)
    {
        // Cosine easing lingers near fully-on/off rather than spending
        // equal time in the middle -- reads as a more natural "breath".
        double v = (Math.Cos(2 * Math.PI * p.Speed * t) + 1) / 2;
        RgbColor scaled = ColorMath.Scale(p.Color, v);
        return Enumerable.Repeat(scaled, numCells).ToArray();
    }
}

/// <summary>The whole keyboard snaps bright then decays quickly on a beat.</summary>
public sealed class PulseEffect : Effect<PulseParams>
{
    public override string Name => "pulse";

    protected override RgbColor[] RenderTyped(double t, int numCells, PulseParams p, EffectContext context)
    {
        RgbColor color;
        if (p.CycleHue)
        {
            double hue = (t * 0.1) % 1.0;
            color = ColorMath.Hsv(hue, 1.0, 1.0);
        }
        else
        {
            color = p.Color;
        }

        double period = p.Rate > 0 ? 1.0 / p.Rate : 1.0;
        double phase = (t % period) / period; // 0..1, resets every pulse
        double v = Math.Max(0.0, 1.0 - phase * 2.2); // snap bright, fade over ~45%, dark the rest

        RgbColor scaled = ColorMath.Scale(color, v);
        return Enumerable.Repeat(scaled, numCells).ToArray();
    }
}

/// <summary>Fully custom per-key static colors -- paint each key individually.</summary>
public sealed class CustomKeysEffect : Effect<CustomKeysParams>
{
    public override string Name => "custom_keys";

    protected override RgbColor[] RenderTyped(double t, int numCells, CustomKeysParams p, EffectContext context)
    {
        var colors = Enumerable.Repeat(p.DefaultColor, numCells).ToArray();
        foreach ((int idx, RgbColor color) in p.Colors)
        {
            if (idx >= 0 && idx < numCells) colors[idx] = color;
        }
        return colors;
    }
}

/// <summary>Flickering flame colors across the keyboard, each key an
/// independent ember.</summary>
public sealed class FireEffect : Effect<FireParams>
{
    public override string Name => "fire";

    /// <summary>heat in [0,1] -> black -> red -> orange -> yellow-white.</summary>
    private static RgbColor FireColor(double heat)
    {
        heat = Math.Clamp(heat, 0.0, 1.0);
        if (heat < 0.4)
        {
            double f = heat / 0.4;
            return new RgbColor((byte)(255 * f), 0, 0);
        }
        if (heat < 0.8)
        {
            double f = (heat - 0.4) / 0.4;
            return new RgbColor(255, (byte)(140 * f), 0);
        }
        double f2 = (heat - 0.8) / 0.2;
        return new RgbColor(255, (byte)(140 + 115 * f2), (byte)(200 * f2));
    }

    protected override RgbColor[] RenderTyped(double t, int numCells, FireParams p, EffectContext context)
    {
        double scaledT = t * p.Speed;
        int tick = (int)scaledT;
        double frac = scaledT - tick;

        var colors = new RgbColor[numCells];
        for (int i = 0; i < numCells; i++)
        {
            // Blend two adjacent random ticks so the flicker looks
            // continuous rather than jumping between discrete values.
            double h0 = PseudoRandom.Value01(i, tick);
            double h1 = PseudoRandom.Value01(i, tick + 1);
            double heat = (h0 * (1 - frac) + h1 * frac) * p.Intensity;
            colors[i] = FireColor(heat);
        }
        return colors;
    }
}

/// <summary>Random keys spark to a bright random color and quickly fade,
/// against a dim background.</summary>
public sealed class ConfettiEffect : Effect<ConfettiParams>
{
    public override string Name => "confetti";

    protected override RgbColor[] RenderTyped(double t, int numCells, ConfettiParams p, EffectContext context)
    {
        var colors = Enumerable.Repeat(p.BaseColor, numCells).ToArray();
        if (p.SpawnRate <= 0 || p.DecaySeconds <= 0 || numCells == 0) return colors;

        // Each cell has its own "spark schedule": a repeating period
        // sized so the board averages SpawnRate sparks/sec overall, with
        // its own random phase offset so cells don't spark in lockstep.
        double period = numCells / p.SpawnRate;
        for (int i = 0; i < numCells; i++)
        {
            double offset = PseudoRandom.Value01(i, 0, 5) * period;
            int cycleIndex = (int)((t + offset) / period);
            double localT = (t + offset) % period;
            if (localT > p.DecaySeconds) continue;
            double v = 1.0 - (localT / p.DecaySeconds);
            double hue = PseudoRandom.Value01(i, cycleIndex, 7);
            RgbColor spark = ColorMath.Hsv(hue, 1.0, 1.0);
            colors[i] = ColorMath.Lerp(p.BaseColor, spark, v);
        }
        return colors;
    }
}

/// <summary>Random keys softly fade in and out independently against a
/// dim background, like stars twinkling.</summary>
public sealed class StarlightEffect : Effect<StarlightParams>
{
    public override string Name => "starlight";

    protected override RgbColor[] RenderTyped(double t, int numCells, StarlightParams p, EffectContext context)
    {
        double speed = Math.Max(0.01, p.Speed);
        var colors = new RgbColor[numCells];
        for (int i = 0; i < numCells; i++)
        {
            if (PseudoRandom.Value01(i, 1) > p.Density)
            {
                colors[i] = p.BaseColor;
                continue;
            }
            // Each selected key gets its own cycle length and phase
            // offset so twinkles don't all pulse in lockstep.
            double period = (1.5 + PseudoRandom.Value01(i, 2) * 2.5) / speed;
            double offset = PseudoRandom.Value01(i, 3) * period;
            double phase = ((t + offset) % period) / period; // 0..1
            double v = Math.Pow(Math.Max(0.0, 1.0 - Math.Abs(phase * 2 - 1)), 1.5); // triangular fade
            colors[i] = ColorMath.Lerp(p.BaseColor, p.Color, v);
        }
        return colors;
    }
}
