// Strongly-typed params for every ported effect, one record per effect
// kind (see windows/HANDOFF.md's settled decision #3). Default values
// throughout are transcribed from each effect's own effects/*.py DEFAULT_*
// constants on `main` -- not re-derived, kept numerically identical.

using System.Text.Json.Serialization;
using JmaStudio.Hardware;

namespace JmaStudio.Effects;

/// <summary>Base type for every effect's params. Concrete effects each
/// declare their own strongly-typed subtype below rather than sharing
/// one untyped dict the way Python's effects/*.py do -- see
/// windows/HANDOFF.md's settled decision #3.
///
/// The [JsonDerivedType] attributes below make this polymorphic under
/// System.Text.Json (a "$effect" string discriminator picks the right
/// concrete record on deserialize) -- needed so JmaStudio.Presets can
/// save/load a preset's params without knowing its concrete type ahead
/// of time. Discriminator strings match each Python module's NAME
/// constant, for the same reason EffectRegistry's dictionary keys do
/// (see EffectRegistry.cs) -- consistency, not a hard requirement.
/// This is for NEW C#-native preset files; migrating OLD Python JSON
/// (different, snake_case field names) is a separate, explicit parser
/// in JmaStudio.Presets, not something this attribute-based scheme
/// handles automatically.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$effect")]
[JsonDerivedType(typeof(StaticParams), "static")]
[JsonDerivedType(typeof(MaskParams), "mask")]
[JsonDerivedType(typeof(ProbeParams), "probe")]
[JsonDerivedType(typeof(RainbowParams), "rainbow")]
[JsonDerivedType(typeof(PukeParams), "puke")]
[JsonDerivedType(typeof(SpectrumCycleParams), "spectrum_cycle")]
[JsonDerivedType(typeof(BreathingParams), "breathing")]
[JsonDerivedType(typeof(PulseParams), "pulse")]
[JsonDerivedType(typeof(CustomKeysParams), "custom_keys")]
[JsonDerivedType(typeof(GamingZoneParams), "gaming_zone")]
[JsonDerivedType(typeof(StarlightParams), "starlight")]
[JsonDerivedType(typeof(ConfettiParams), "confetti")]
[JsonDerivedType(typeof(FireParams), "fire")]
[JsonDerivedType(typeof(ColorWipeParams), "color_wipe")]
[JsonDerivedType(typeof(CometParams), "comet")]
[JsonDerivedType(typeof(ScannerParams), "scanner")]
[JsonDerivedType(typeof(AuroraParams), "aurora")]
[JsonDerivedType(typeof(RippleParams), "ripple")]
[JsonDerivedType(typeof(RainParams), "rain")]
[JsonDerivedType(typeof(GradientParams), "gradient")]
[JsonDerivedType(typeof(TypingReactiveParams), "typing_reactive")]
[JsonDerivedType(typeof(ControllerReactiveParams), "controller_reactive")]
public abstract record EffectParams;

public sealed record StaticParams : EffectParams
{
    public RgbColor Color { get; init; } = new(0, 0, 0);
}

/// <summary>Diagnostic effect (keymap-coverage check) -- see effects/mask.py.</summary>
public sealed record MaskParams : EffectParams
{
    public IReadOnlyList<int> Indices { get; init; } = Array.Empty<int>();
    public RgbColor Color { get; init; } = new(255, 255, 255);
}

/// <summary>Diagnostic effect (single-cell keymap discovery) -- see effects/probe.py.</summary>
public sealed record ProbeParams : EffectParams
{
    public int Index { get; init; }
    public RgbColor Color { get; init; } = new(255, 255, 255);
}

public sealed record RainbowParams : EffectParams
{
    public double Speed { get; init; } = 0.15;
}

public sealed record PukeParams : EffectParams
{
    public double Speed { get; init; } = 6.0;
}

public sealed record SpectrumCycleParams : EffectParams
{
    public double Speed { get; init; } = 0.1;
}

public sealed record BreathingParams : EffectParams
{
    public RgbColor Color { get; init; } = new(79, 123, 255);
    public double Speed { get; init; } = 0.5;
}

public sealed record PulseParams : EffectParams
{
    public RgbColor Color { get; init; } = new(255, 60, 60);
    public double Rate { get; init; } = 1.2;
    public bool CycleHue { get; init; } = false;
}

public sealed record CustomKeysParams : EffectParams
{
    public IReadOnlyDictionary<int, RgbColor> Colors { get; init; } = new Dictionary<int, RgbColor>();
    public RgbColor DefaultColor { get; init; } = new(0, 0, 0);
}

public sealed record GamingZoneParams : EffectParams
{
    public static readonly IReadOnlyList<string> DefaultKeys =
        new[] { "w", "a", "s", "d", "up_arrow", "down_arrow", "left_arrow", "right_arrow" };

    public IReadOnlyList<string> Keys { get; init; } = DefaultKeys;
    public RgbColor BrightColor { get; init; } = new(0, 255, 0);
    public RgbColor DimColor { get; init; } = new(0, 0, 0);
}

public sealed record StarlightParams : EffectParams
{
    public RgbColor Color { get; init; } = new(255, 255, 255);
    public RgbColor BaseColor { get; init; } = new(5, 5, 10);
    public double Density { get; init; } = 0.25;
    public double Speed { get; init; } = 0.6;
}

public sealed record ConfettiParams : EffectParams
{
    public RgbColor BaseColor { get; init; } = new(0, 0, 0);
    public double SpawnRate { get; init; } = 6.0;
    public double DecaySeconds { get; init; } = 0.5;
}

public sealed record FireParams : EffectParams
{
    public double Speed { get; init; } = 8.0;
    public double Intensity { get; init; } = 1.0;
}

public sealed record ColorWipeParams : EffectParams
{
    public static readonly IReadOnlyList<RgbColor> DefaultColors = new[]
    {
        new RgbColor(255, 40, 40), new RgbColor(40, 120, 255),
        new RgbColor(40, 220, 120), new RgbColor(255, 200, 40),
    };

    public IReadOnlyList<RgbColor> Colors { get; init; } = DefaultColors;
    public double Speed { get; init; } = 10.0;
}

public sealed record CometParams : EffectParams
{
    public RgbColor Color { get; init; } = new(255, 200, 60);
    public RgbColor BaseColor { get; init; } = new(0, 0, 0);
    public double Speed { get; init; } = 8.0;
    public double Tail { get; init; } = 5.0;
}

public sealed record ScannerParams : EffectParams
{
    public RgbColor Color { get; init; } = new(255, 30, 30);
    public RgbColor BaseColor { get; init; } = new(0, 0, 0);
    public double Speed { get; init; } = 10.0;
    public double Width { get; init; } = 3.0;
}

public sealed record AuroraParams : EffectParams
{
    public double Speed { get; init; } = 0.15;
    public double Scale { get; init; } = 0.15;
}

public sealed record RippleParams : EffectParams
{
    public RgbColor Color { get; init; } = new(79, 190, 255);
    public RgbColor BaseColor { get; init; } = new(0, 0, 0);
    public double Speed { get; init; } = 6.0;
    public double Interval { get; init; } = 2.0;
    public double Width { get; init; } = 2.5;
}

public sealed record RainParams : EffectParams
{
    public RgbColor Color { get; init; } = new(90, 160, 255);
    public RgbColor BaseColor { get; init; } = new(0, 0, 0);
    public double Speed { get; init; } = 10.0;
    public double SpawnRate { get; init; } = 3.0;
    public double Tail { get; init; } = 2.5;
}

/// <summary>See effects/gradient.py's docstring for the full field-by-field
/// legacy-vs-multi-zone explanation -- ported here verbatim in spirit,
/// with `Colors`/`Boundaries` (multi-zone, null = "not given") taking
/// priority over the legacy 2-zone fields, exactly as the Python
/// render() does.</summary>
public sealed record GradientParams : EffectParams
{
    public IReadOnlyList<RgbColor>? Colors { get; init; }
    public IReadOnlyList<double>? Boundaries { get; init; }
    public RgbColor LeftColor { get; init; } = new(20, 90, 230);
    public RgbColor RightColor { get; init; } = new(200, 20, 160);
    public double? Boundary { get; init; }
    public bool Hard { get; init; } = true;
    public IReadOnlyList<string> LeftOverrides { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RightOverrides { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, RgbColor> CustomColors { get; init; } = new Dictionary<string, RgbColor>();
    public double Brightness { get; init; } = 0.65;
}

public enum BoltShape { Rays, Radial }
public enum BoltStyle { Solid, Rainbow }

public sealed record TypingReactiveParams : EffectParams
{
    public static readonly IReadOnlyList<(int Row, int Col)> DefaultBoltDirections = new[]
    {
        (-1, 0), (1, 0), (0, -1), (0, 1),
        (-1, -1), (-1, 1), (1, -1), (1, 1),
    };

    public RgbColor BaseColor { get; init; } = new(38, 38, 38); // 255 * 0.15 ~= 38
    /// <summary>Name of another registered effect to use as the resting
    /// background instead of BaseColor -- e.g. "gradient". Null = use
    /// BaseColor. Mirrors Python's base_effect string param.</summary>
    public string? BaseEffectName { get; init; }
    public EffectParams? BaseParams { get; init; }
    public RgbColor BrightColor { get; init; } = new(255, 255, 255);
    public double DecaySeconds { get; init; } = 0.6;
    public bool Bolts { get; init; } = true;
    public BoltShape BoltShape { get; init; } = BoltShape.Rays;
    public IReadOnlyList<(int Row, int Col)> BoltDirections { get; init; } = DefaultBoltDirections;
    public double BoltSpeed { get; init; } = 12.0;
    public double BoltTail { get; init; } = 3.0;
    /// <summary>Default 18.5 -- just past this keyboard's real ~18.24-unit
    /// corner-to-corner diagonal (esc to num_enter), so it doesn't
    /// constrain anything at the default. See the Python docstring.</summary>
    public double BoltMaxDistance { get; init; } = 18.5;
    public double BoltTolerance { get; init; } = 0.75;
    public BoltStyle BoltStyle { get; init; } = BoltStyle.Solid;
    public double BoltFlickerSpeed { get; init; } = 6.0;
    public bool BoltReset { get; init; } = false;
}

/// <summary>Port of effects/controller_reactive.py's params. "dark green"
/// == (0,100,0), matching the Python DEFAULT_GROUP_COLOR.</summary>
public sealed record StickColors
{
    public RgbColor Idle { get; init; } = new(0, 100, 0);
    public RgbColor Tier1 { get; init; } = new(0, 100, 0);
    public RgbColor Tier2 { get; init; } = new(0, 100, 0);
}

public sealed record ControllerReactiveParams : EffectParams
{
    public bool BackgroundEnabled { get; init; } = true;
    public RgbColor BackgroundColor { get; init; } = new(10, 10, 10);
    public StickColors LeftStick { get; init; } = new();
    public StickColors RightStick { get; init; } = new();
    public IReadOnlyDictionary<string, RgbColor> ButtonColors { get; init; } = new Dictionary<string, RgbColor>();
    public double Deadzone { get; init; } = 0.15;
}
