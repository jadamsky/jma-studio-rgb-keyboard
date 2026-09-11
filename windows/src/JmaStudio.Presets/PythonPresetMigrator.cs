// One-time migration from the Python side's on-disk data (presets.json,
// lightbar_presets.json, lightbar_reactive.json, config.json -- all on
// `main`) into this project's own typed shapes (Models.cs). This is
// deliberately NOT a general-purpose Python-JSON-to-C# converter kept
// around forever -- it's a bridge for real, currently-existing user
// data, covering every field every ported effect actually has (see
// EffectParams.cs), so it survives the migration untouched even for
// effects/fields the user's current presets don't happen to use.
//
// Field-name mapping throughout is Python's snake_case dict keys ->
// this project's PascalCase properties -- this is why System.Text.Json's
// own [JsonDerivedType] polymorphism on EffectParams (see EffectParams.cs)
// can't be reused here directly; that scheme expects C#-shaped JSON
// (from a preset THIS project saved), not Python-shaped JSON.

using System.Text.Json;
using JmaStudio.Effects;
using JmaStudio.Hardware;

namespace JmaStudio.Presets;

public static class PythonPresetMigrator
{
    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement;

    // ---- generic JsonElement helpers ---------------------------------

    private static RgbColor ParseColor(JsonElement arr) =>
        new((byte)arr[0].GetInt32(), (byte)arr[1].GetInt32(), (byte)arr[2].GetInt32());

    private static RgbColor ParseColorOr(JsonElement p, string key, RgbColor fallback) =>
        p.TryGetProperty(key, out JsonElement el) ? ParseColor(el) : fallback;

    private static double ParseDoubleOr(JsonElement p, string key, double fallback) =>
        p.TryGetProperty(key, out JsonElement el) ? el.GetDouble() : fallback;

    private static int ParseIntOr(JsonElement p, string key, int fallback) =>
        p.TryGetProperty(key, out JsonElement el) ? el.GetInt32() : fallback;

    private static bool ParseBoolOr(JsonElement p, string key, bool fallback) =>
        p.TryGetProperty(key, out JsonElement el) ? el.GetBoolean() : fallback;

    private static string ParseStringOr(JsonElement p, string key, string fallback) =>
        p.TryGetProperty(key, out JsonElement el) ? (el.GetString() ?? fallback) : fallback;

    private static IReadOnlyList<string> ParseStringListOr(JsonElement p, string key, IReadOnlyList<string> fallback) =>
        p.TryGetProperty(key, out JsonElement el)
            ? el.EnumerateArray().Select(e => e.GetString()!).ToArray()
            : fallback;

    private static IReadOnlyList<double> ParseDoubleListOr(JsonElement p, string key) =>
        p.TryGetProperty(key, out JsonElement el)
            ? el.EnumerateArray().Select(e => e.GetDouble()).ToArray()
            : Array.Empty<double>();

    private static IReadOnlyList<RgbColor>? ParseColorListOrNull(JsonElement p, string key) =>
        p.TryGetProperty(key, out JsonElement el) ? el.EnumerateArray().Select(ParseColor).ToArray() : null;

    private static IReadOnlyDictionary<int, RgbColor> ParseIntColorDictOr(JsonElement p, string key) =>
        p.TryGetProperty(key, out JsonElement el)
            ? el.EnumerateObject().ToDictionary(prop => int.Parse(prop.Name), prop => ParseColor(prop.Value))
            : new Dictionary<int, RgbColor>();

    private static IReadOnlyDictionary<string, RgbColor> ParseStringColorDictOr(JsonElement p, string key) =>
        p.TryGetProperty(key, out JsonElement el)
            ? el.EnumerateObject().ToDictionary(prop => prop.Name, prop => ParseColor(prop.Value))
            : new Dictionary<string, RgbColor>();

    // ---- per-effect params parsers (one per effects/*.py module) -----

    /// <summary>Dispatches by Python effect NAME to the matching parser.
    /// Public so it can also be used to parse a nested base_params blob
    /// for any base_effect, not just the ones seen in real presets today.</summary>
    public static EffectParams Parse(string effectName, JsonElement p) => effectName switch
    {
        "static" => ParseStatic(p),
        "mask" => ParseMask(p),
        "probe" => ParseProbe(p),
        "rainbow" => ParseRainbow(p),
        "puke" => ParsePuke(p),
        "spectrum_cycle" => ParseSpectrumCycle(p),
        "breathing" => ParseBreathing(p),
        "pulse" => ParsePulse(p),
        "custom_keys" => ParseCustomKeys(p),
        "gaming_zone" => ParseGamingZone(p),
        "starlight" => ParseStarlight(p),
        "confetti" => ParseConfetti(p),
        "fire" => ParseFire(p),
        "color_wipe" => ParseColorWipe(p),
        "comet" => ParseComet(p),
        "scanner" => ParseScanner(p),
        "aurora" => ParseAurora(p),
        "ripple" => ParseRipple(p),
        "rain" => ParseRain(p),
        "gradient" => ParseGradient(p),
        "typing_reactive" => ParseTypingReactive(p),
        "controller_reactive" => ParseControllerReactive(p),
        _ => throw new NotSupportedException($"Unknown Python effect name '{effectName}' encountered during migration."),
    };

    private static StaticParams ParseStatic(JsonElement p) => new()
    {
        Color = ParseColorOr(p, "color", new RgbColor(0, 0, 0)),
    };

    private static MaskParams ParseMask(JsonElement p) => new()
    {
        Indices = p.TryGetProperty("indices", out JsonElement el)
            ? el.EnumerateArray().Select(e => e.GetInt32()).ToArray() : Array.Empty<int>(),
        Color = ParseColorOr(p, "color", new RgbColor(255, 255, 255)),
    };

    private static ProbeParams ParseProbe(JsonElement p) => new()
    {
        Index = ParseIntOr(p, "index", 0),
        Color = ParseColorOr(p, "color", new RgbColor(255, 255, 255)),
    };

    private static RainbowParams ParseRainbow(JsonElement p) => new() { Speed = ParseDoubleOr(p, "speed", 0.15) };

    private static PukeParams ParsePuke(JsonElement p) => new() { Speed = ParseDoubleOr(p, "speed", 6.0) };

    private static SpectrumCycleParams ParseSpectrumCycle(JsonElement p) => new() { Speed = ParseDoubleOr(p, "speed", 0.1) };

    private static BreathingParams ParseBreathing(JsonElement p) => new()
    {
        Color = ParseColorOr(p, "color", new RgbColor(79, 123, 255)),
        Speed = ParseDoubleOr(p, "speed", 0.5),
    };

    private static PulseParams ParsePulse(JsonElement p) => new()
    {
        Color = ParseColorOr(p, "color", new RgbColor(255, 60, 60)),
        Rate = ParseDoubleOr(p, "rate", 1.2),
        CycleHue = ParseBoolOr(p, "cycle_hue", false),
    };

    private static CustomKeysParams ParseCustomKeys(JsonElement p) => new()
    {
        Colors = ParseIntColorDictOr(p, "colors"),
        DefaultColor = ParseColorOr(p, "default_color", new RgbColor(0, 0, 0)),
    };

    private static GamingZoneParams ParseGamingZone(JsonElement p) => new()
    {
        Keys = ParseStringListOr(p, "keys", GamingZoneParams.DefaultKeys),
        BrightColor = ParseColorOr(p, "bright_color", new RgbColor(0, 255, 0)),
        DimColor = ParseColorOr(p, "dim_color", new RgbColor(0, 0, 0)),
    };

    private static StarlightParams ParseStarlight(JsonElement p) => new()
    {
        Color = ParseColorOr(p, "color", new RgbColor(255, 255, 255)),
        BaseColor = ParseColorOr(p, "base_color", new RgbColor(5, 5, 10)),
        Density = ParseDoubleOr(p, "density", 0.25),
        Speed = ParseDoubleOr(p, "speed", 0.6),
    };

    private static ConfettiParams ParseConfetti(JsonElement p) => new()
    {
        BaseColor = ParseColorOr(p, "base_color", new RgbColor(0, 0, 0)),
        SpawnRate = ParseDoubleOr(p, "spawn_rate", 6.0),
        DecaySeconds = ParseDoubleOr(p, "decay_seconds", 0.5),
    };

    private static FireParams ParseFire(JsonElement p) => new()
    {
        Speed = ParseDoubleOr(p, "speed", 8.0),
        Intensity = ParseDoubleOr(p, "intensity", 1.0),
    };

    private static ColorWipeParams ParseColorWipe(JsonElement p) => new()
    {
        Colors = ParseColorListOrNull(p, "colors") ?? ColorWipeParams.DefaultColors,
        Speed = ParseDoubleOr(p, "speed", 10.0),
    };

    private static CometParams ParseComet(JsonElement p) => new()
    {
        Color = ParseColorOr(p, "color", new RgbColor(255, 200, 60)),
        BaseColor = ParseColorOr(p, "base_color", new RgbColor(0, 0, 0)),
        Speed = ParseDoubleOr(p, "speed", 8.0),
        Tail = ParseDoubleOr(p, "tail", 5.0),
    };

    private static ScannerParams ParseScanner(JsonElement p) => new()
    {
        Color = ParseColorOr(p, "color", new RgbColor(255, 30, 30)),
        BaseColor = ParseColorOr(p, "base_color", new RgbColor(0, 0, 0)),
        Speed = ParseDoubleOr(p, "speed", 10.0),
        Width = ParseDoubleOr(p, "width", 3.0),
    };

    private static AuroraParams ParseAurora(JsonElement p) => new()
    {
        Speed = ParseDoubleOr(p, "speed", 0.15),
        Scale = ParseDoubleOr(p, "scale", 0.15),
    };

    private static RippleParams ParseRipple(JsonElement p) => new()
    {
        Color = ParseColorOr(p, "color", new RgbColor(79, 190, 255)),
        BaseColor = ParseColorOr(p, "base_color", new RgbColor(0, 0, 0)),
        Speed = ParseDoubleOr(p, "speed", 6.0),
        Interval = ParseDoubleOr(p, "interval", 2.0),
        Width = ParseDoubleOr(p, "width", 2.5),
    };

    private static RainParams ParseRain(JsonElement p) => new()
    {
        Color = ParseColorOr(p, "color", new RgbColor(90, 160, 255)),
        BaseColor = ParseColorOr(p, "base_color", new RgbColor(0, 0, 0)),
        Speed = ParseDoubleOr(p, "speed", 10.0),
        SpawnRate = ParseDoubleOr(p, "spawn_rate", 3.0),
        Tail = ParseDoubleOr(p, "tail", 2.5),
    };

    private static GradientParams ParseGradient(JsonElement p) => new()
    {
        Colors = ParseColorListOrNull(p, "colors"),
        Boundaries = p.TryGetProperty("boundaries", out JsonElement be)
            ? be.EnumerateArray().Select(e => e.GetDouble()).ToArray() : null,
        LeftColor = ParseColorOr(p, "left_color", new RgbColor(20, 90, 230)),
        RightColor = ParseColorOr(p, "right_color", new RgbColor(200, 20, 160)),
        Boundary = p.TryGetProperty("boundary", out JsonElement bo) ? bo.GetDouble() : null,
        Hard = ParseBoolOr(p, "hard", true),
        LeftOverrides = ParseStringListOr(p, "left_overrides", Array.Empty<string>()),
        RightOverrides = ParseStringListOr(p, "right_overrides", Array.Empty<string>()),
        CustomColors = ParseStringColorDictOr(p, "custom_colors"),
        Brightness = ParseDoubleOr(p, "brightness", 0.65),
    };

    private static TypingReactiveParams ParseTypingReactive(JsonElement p)
    {
        string? baseEffectName = p.TryGetProperty("base_effect", out JsonElement beEl) ? beEl.GetString() : null;
        EffectParams? baseParams = null;
        if (!string.IsNullOrEmpty(baseEffectName) && p.TryGetProperty("base_params", out JsonElement bpEl))
        {
            baseParams = Parse(baseEffectName, bpEl);
        }

        IReadOnlyList<(int Row, int Col)> boltDirections = TypingReactiveParams.DefaultBoltDirections;
        if (p.TryGetProperty("bolt_directions", out JsonElement bdEl))
        {
            boltDirections = bdEl.EnumerateArray()
                .Select(pair => (pair[0].GetInt32(), pair[1].GetInt32()))
                .ToArray();
        }

        return new TypingReactiveParams
        {
            BaseColor = ParseColorOr(p, "base_color", new RgbColor(38, 38, 38)),
            BaseEffectName = baseEffectName,
            BaseParams = baseParams,
            BrightColor = ParseColorOr(p, "bright_color", new RgbColor(255, 255, 255)),
            DecaySeconds = ParseDoubleOr(p, "decay_seconds", 0.6),
            Bolts = ParseBoolOr(p, "bolts", true),
            BoltShape = ParseStringOr(p, "bolt_shape", "rays") == "radial" ? BoltShape.Radial : BoltShape.Rays,
            BoltDirections = boltDirections,
            BoltSpeed = ParseDoubleOr(p, "bolt_speed", 12.0),
            BoltTail = ParseDoubleOr(p, "bolt_tail", 3.0),
            BoltMaxDistance = ParseDoubleOr(p, "bolt_max_distance", 18.5),
            BoltTolerance = ParseDoubleOr(p, "bolt_tolerance", 0.75),
            BoltStyle = ParseStringOr(p, "bolt_style", "solid") == "rainbow" ? BoltStyle.Rainbow : BoltStyle.Solid,
            BoltFlickerSpeed = ParseDoubleOr(p, "bolt_flicker_speed", 6.0),
            BoltReset = ParseBoolOr(p, "bolt_reset", false),
        };
    }

    private static StickColors ParseStickColors(JsonElement el) => new()
    {
        Idle = ParseColorOr(el, "idle", new RgbColor(0, 100, 0)),
        Tier1 = ParseColorOr(el, "tier1", new RgbColor(0, 100, 0)),
        Tier2 = ParseColorOr(el, "tier2", new RgbColor(0, 100, 0)),
    };

    private static ControllerReactiveParams ParseControllerReactive(JsonElement p) => new()
    {
        BackgroundEnabled = ParseBoolOr(p, "background_enabled", true),
        BackgroundColor = ParseColorOr(p, "background_color", new RgbColor(10, 10, 10)),
        LeftStick = p.TryGetProperty("left_stick", out JsonElement ls) ? ParseStickColors(ls) : new StickColors(),
        RightStick = p.TryGetProperty("right_stick", out JsonElement rs) ? ParseStickColors(rs) : new StickColors(),
        ButtonColors = ParseStringColorDictOr(p, "button_colors"),
        Deadzone = ParseDoubleOr(p, "deadzone", 0.15),
    };

    /// <summary>Migrates controller_reactive.json -- unlike presets.json's
    /// entries, this file IS the params object directly (its own separate
    /// settings store on the Python side, deliberately not a regular
    /// preset -- see daemon/server.py on `main`).</summary>
    public static ControllerReactiveParams MigrateControllerReactiveSettings(string path)
    {
        if (!File.Exists(path)) return new ControllerReactiveParams();
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        return ParseControllerReactive(doc.RootElement);
    }

    // ---- top-level migrations (one per Python JSON file) --------------

    /// <summary>Migrates presets.json's {"name": {"effect":..., "params":...}}
    /// shape into {"name": KeyboardPreset}.</summary>
    public static Dictionary<string, KeyboardPreset> MigrateKeyboardPresets(string presetsJsonPath)
    {
        var result = new Dictionary<string, KeyboardPreset>();
        if (!File.Exists(presetsJsonPath)) return result;

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(presetsJsonPath));
        foreach (JsonProperty entry in doc.RootElement.EnumerateObject())
        {
            string effect = entry.Value.GetProperty("effect").GetString()!;
            JsonElement paramsEl = entry.Value.TryGetProperty("params", out JsonElement pe) ? pe : EmptyObject;
            result[entry.Name] = new KeyboardPreset { Effect = effect, Params = Parse(effect, paramsEl) };
        }
        return result;
    }

    private static LightbarMode ParseLightbarMode(string name) => name switch
    {
        "breathing" => LightbarMode.Breathing,
        "neon" => LightbarMode.Neon,
        "rainbow" => LightbarMode.Rainbow,
        "wave" => LightbarMode.Wave,
        "ripple" => LightbarMode.Ripple,
        "scanner" => LightbarMode.Scanner,
        "strobe" => LightbarMode.Strobe,
        _ => throw new NotSupportedException($"Unknown lightbar mode '{name}' encountered during migration."),
    };

    private static LightbarState ParseLightbarState(JsonElement state)
    {
        // Absence of an explicit "type": "mode" key means static -- real
        // presets (e.g. "BLUE" in the real lightbar_presets.json on
        // `main`) have no "type" key at all, matching the same
        // permissive check Lightbar.apply_state() uses.
        bool isMode = state.TryGetProperty("type", out JsonElement typeEl) && typeEl.GetString() == "mode";
        if (isMode)
        {
            return new LightbarState
            {
                Mode = ParseLightbarMode(state.GetProperty("mode").GetString()!),
                ModeColor = ParseColorOr(state, "color", new RgbColor(255, 0, 0)),
                ModeSpeed = ParseIntOr(state, "speed", 5),
                Brightness = ParseIntOr(state, "brightness", 100),
            };
        }
        return new LightbarState
        {
            ZoneColors = ParseIntColorDictOr(state, "colors"),
            Brightness = ParseIntOr(state, "brightness", 100),
        };
    }

    private static LightbarReactiveSettings ParseLightbarReactiveSettings(JsonElement r) => new()
    {
        Enabled = ParseBoolOr(r, "enabled", false),
        BackgroundColor = ParseColorOr(r, "background_color", new RgbColor(0, 0, 0)),
        ZoneFlashColors = ParseIntColorDictOr(r, "zone_flash_colors"),
        AllFlashColor = ParseColorOr(r, "all_flash_color", new RgbColor(255, 220, 0)),
    };

    /// <summary>Migrates lightbar_presets.json, including the back-compat
    /// flat-dict shape older presets use (see ParseLightbarState).</summary>
    public static Dictionary<string, LightbarPreset> MigrateLightbarPresets(string lightbarPresetsJsonPath)
    {
        var result = new Dictionary<string, LightbarPreset>();
        if (!File.Exists(lightbarPresetsJsonPath)) return result;

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(lightbarPresetsJsonPath));
        foreach (JsonProperty entry in doc.RootElement.EnumerateObject())
        {
            JsonElement raw = entry.Value;
            JsonElement lightbarEl = raw.TryGetProperty("lightbar", out JsonElement le) ? le : raw;
            LightbarReactiveSettings? reactive = raw.TryGetProperty("reactive", out JsonElement re)
                ? ParseLightbarReactiveSettings(re) : null;
            result[entry.Name] = new LightbarPreset { Lightbar = ParseLightbarState(lightbarEl), Reactive = reactive };
        }
        return result;
    }

    /// <summary>Migrates lightbar_reactive.json (the live, non-preset
    /// reactive config, including zone_boundaries).</summary>
    public static LightbarReactiveConfig MigrateLightbarReactiveConfig(string path)
    {
        if (!File.Exists(path)) return new LightbarReactiveConfig();
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement r = doc.RootElement;
        return new LightbarReactiveConfig
        {
            Enabled = ParseBoolOr(r, "enabled", false),
            ZoneBoundaries = ParseDoubleListOr(r, "zone_boundaries"),
            BackgroundColor = ParseColorOr(r, "background_color", new RgbColor(0, 0, 0)),
            ZoneFlashColors = ParseIntColorDictOr(r, "zone_flash_colors"),
            AllFlashColor = ParseColorOr(r, "all_flash_color", new RgbColor(255, 220, 0)),
        };
    }

    /// <summary>Migrates config.json's default_preset/lightbar_default_preset.</summary>
    public static AppConfig MigrateAppConfig(string path)
    {
        if (!File.Exists(path)) return new AppConfig();
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement r = doc.RootElement;
        return new AppConfig
        {
            DefaultPreset = r.TryGetProperty("default_preset", out JsonElement dp) ? dp.GetString() : null,
            LightbarDefaultPreset = r.TryGetProperty("lightbar_default_preset", out JsonElement ldp) ? ldp.GetString() : null,
        };
    }

    /// <summary>Reads all 4 Python-side files from `pythonRepoRoot` and
    /// writes them into `store` via its normal atomic Save() calls.
    /// Safe to re-run (idempotent, always fully overwrites each of the
    /// 4 new files from the current Python-side source data).</summary>
    public static void MigrateAll(string pythonRepoRoot, PresetStore store)
    {
        store.KeyboardPresets.Save(MigrateKeyboardPresets(Path.Combine(pythonRepoRoot, "presets.json")));
        store.LightbarPresets.Save(MigrateLightbarPresets(Path.Combine(pythonRepoRoot, "lightbar_presets.json")));
        store.LightbarReactiveConfig.Save(MigrateLightbarReactiveConfig(Path.Combine(pythonRepoRoot, "lightbar_reactive.json")));
        store.AppConfig.Save(MigrateAppConfig(Path.Combine(pythonRepoRoot, "config.json")));
        store.ControllerReactiveSettings.Save(MigrateControllerReactiveSettings(Path.Combine(pythonRepoRoot, "controller_reactive.json")));
    }
}
