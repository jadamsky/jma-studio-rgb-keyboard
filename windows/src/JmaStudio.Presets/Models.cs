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

/// <summary>Phase 8 (V2) idle screensaver config -- when no keyboard,
/// mouse, or controller input has been seen for IdleThresholdMinutes,
/// the keyboard cycles through KeyboardPresetNames (a playlist, not a
/// single fixed effect) and, if LightbarPresetName is set, the lightbar
/// switches to that one preset for the duration (never a cycling
/// playlist for the lightbar -- confirmed by the user). Whatever was
/// running before is restored the instant real input resumes. See
/// IdleScreensaverManager (JmaStudio.Service) for the actual logic.</summary>
public sealed record IdleScreensaverConfig
{
    public bool Enabled { get; init; }
    public double IdleThresholdMinutes { get; init; } = 5.0;
    public IReadOnlyList<string> KeyboardPresetNames { get; init; } = Array.Empty<string>();
    public int CycleIntervalSeconds { get; init; } = 30;
    public bool RandomOrder { get; init; }
    /// <summary>Null/empty means "don't touch the lightbar at all while
    /// the screensaver is active" -- one of the three options the user
    /// asked for (a built-in effect, a solid color, or nothing at all)
    /// is satisfied by picking an existing lightbar preset (which can
    /// itself represent any of those), or leaving this unset.</summary>
    public string? LightbarPresetName { get; init; }
}

/// <summary>Reserved sentinel value usable as a KeyboardPresetNames
/// playlist entry OR as LightbarPresetName -- means "turn this off"
/// rather than "apply a named preset." Shared between JmaStudio.Gui
/// (populates the picker lists with it) and JmaStudio.Service
/// (IdleScreensaverManager special-cases it instead of doing a preset
/// lookup) so both sides agree on the exact string. Not a real preset
/// name -- treated as reserved even in the unlikely case a user names an
/// actual preset identically, same as the GUI's own "(None...)" combo
/// option isn't a real preset either.</summary>
public static class IdleScreensaverSentinels
{
    public const string LightsOut = "(Lights Out)";
}

/// <summary>Phase 8 (V2) low-battery lighting override -- when the
/// laptop is unplugged and battery percentage drops to or below
/// ThresholdPercent, the keyboard and lightbar are each overridden to
/// their own flat color scaled by their own brightness (a "get to a
/// charger" signal, not a light show) -- independently configurable per
/// the user's explicit request (2026-09-12), not a single shared color/
/// brightness. "Battery wins" over the idle screensaver (confirmed by
/// the user) -- see EffectOverrideCoordinator (JmaStudio.Service) for
/// how that priority is enforced. See LowBatteryOverrideManager for the
/// actual logic.</summary>
public sealed record LowBatteryOverrideConfig
{
    public bool Enabled { get; init; }
    public int ThresholdPercent { get; init; } = 15;
    public RgbColor KeyboardColor { get; init; } = new(255, 255, 255);
    public double KeyboardBrightness { get; init; } = 0.3;
    public RgbColor LightbarColor { get; init; } = new(255, 255, 255);
    public double LightbarBrightness { get; init; } = 0.3;
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
