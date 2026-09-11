// New C#-native preset/config shapes -- deliberately not byte-for-byte
// copies of Python's presets.json/lightbar_presets.json/
// lightbar_reactive.json/config.json shapes (those were untyped dicts;
// these are strongly-typed records). PythonPresetMigrator.cs is what
// bridges the two, once, at migration time -- these types are what the
// C# side actually reads/writes going forward.

using JmaStudio.Effects;
using JmaStudio.Hardware;

namespace JmaStudio.Presets;

/// <summary>A saved keyboard preset -- which effect, and its params.
/// `Effect` is the source of truth for "which registered IEffect to use
/// when applying this preset" (see EffectRegistry.TryGet); `Params`'s
/// own "$effect" JSON discriminator (see EffectParams.cs) is what tells
/// System.Text.Json which concrete params record to materialize -- the
/// two should always agree, by construction, whenever this is created
/// through normal code (not hand-edited).</summary>
public sealed record KeyboardPreset
{
    public required string Effect { get; init; }
    public required EffectParams Params { get; init; }
}

/// <summary>Mirrors Python's _DEFAULT_REACTIVE_CONFIG /
/// _reactive_settings_snapshot() shape (daemon/server.py on `main`) --
/// the keyboard-press-driven lightbar flash-on-keypress feature's live
/// config. Deliberately excludes zone_boundaries, same as the Python
/// side: that's a one-time keyboard-geometry calibration
/// (captured via /lightbar/reactive/capture_zones), not something that
/// should vary preset to preset -- it lives in LightbarReactiveConfig
/// (the live, non-preset config) instead, not in a per-preset snapshot.</summary>
public sealed record LightbarReactiveSettings
{
    public bool Enabled { get; init; }
    public RgbColor BackgroundColor { get; init; }
    public IReadOnlyDictionary<int, RgbColor> ZoneFlashColors { get; init; } = new Dictionary<int, RgbColor>();
    public RgbColor AllFlashColor { get; init; }
}

/// <summary>The live (non-preset) reactive config, INCLUDING
/// zone_boundaries -- this is the one-time keyboard-geometry
/// calibration plus whatever reactive settings are currently live,
/// separate from any saved preset. Mirrors lightbar_reactive.json on
/// `main` in full.</summary>
public sealed record LightbarReactiveConfig
{
    public bool Enabled { get; init; }
    public IReadOnlyList<double> ZoneBoundaries { get; init; } = Array.Empty<double>();
    public RgbColor BackgroundColor { get; init; }
    public IReadOnlyDictionary<int, RgbColor> ZoneFlashColors { get; init; } = new Dictionary<int, RgbColor>();
    public RgbColor AllFlashColor { get; init; }
}

/// <summary>A saved lightbar preset -- last-commanded lightbar state
/// plus (optionally) a bundled reactive-settings snapshot. `Reactive`
/// is nullable because older presets (see "BLUE" in the real
/// lightbar_presets.json on `main`) predate reactive settings being
/// bundled in at all -- applying one of those should leave whatever
/// reactive config is currently live untouched, exactly like Python's
/// `if "reactive" in preset:` check in apply_lightbar_preset().</summary>
public sealed record LightbarPreset
{
    public required LightbarState Lightbar { get; init; }
    public LightbarReactiveSettings? Reactive { get; init; }
}

/// <summary>Mirrors config.json on `main` -- which preset (if any) each
/// surface starts up with.</summary>
public sealed record AppConfig
{
    public string? DefaultPreset { get; init; }
    public string? LightbarDefaultPreset { get; init; }
}
