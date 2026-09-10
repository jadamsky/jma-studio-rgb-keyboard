// Ad hoc, one-off loader for testing a SPECIFIC live preset pulled
// straight from the Python daemon's own GET /status (or presets.json)
// JSON shape, ahead of Phase 4's real preset persistence/migration
// system. Deliberately narrow: only understands the two effect kinds
// needed for the "typing_reactive wrapping custom_keys" shape (matching
// the real, currently-active "Red Chase"-style preset) -- not a general
// preset deserializer. Delete or replace once Phase 4's real migration
// code exists.

using System.Text.Json;
using JmaStudio.Effects;
using JmaStudio.Hardware;

namespace JmaStudio.HardwareTest;

public static class AdHocPresetLoader
{
    private static RgbColor ParseColor(JsonElement arr) =>
        new((byte)arr[0].GetInt32(), (byte)arr[1].GetInt32(), (byte)arr[2].GetInt32());

    public static CustomKeysParams ParseCustomKeysParams(JsonElement p)
    {
        var colors = new Dictionary<int, RgbColor>();
        if (p.TryGetProperty("colors", out JsonElement colorsElement))
        {
            foreach (JsonProperty prop in colorsElement.EnumerateObject())
            {
                colors[int.Parse(prop.Name)] = ParseColor(prop.Value);
            }
        }
        RgbColor defaultColor = p.TryGetProperty("default_color", out JsonElement dc)
            ? ParseColor(dc) : new RgbColor(0, 0, 0);
        return new CustomKeysParams { Colors = colors, DefaultColor = defaultColor };
    }

    public static TypingReactiveParams ParseTypingReactiveParams(JsonElement p)
    {
        EffectParams? baseParams = null;
        string? baseEffectName = p.TryGetProperty("base_effect", out JsonElement be) ? be.GetString() : null;
        if (baseEffectName == "custom_keys" && p.TryGetProperty("base_params", out JsonElement bp))
        {
            baseParams = ParseCustomKeysParams(bp);
        }

        return new TypingReactiveParams
        {
            BaseEffectName = baseEffectName,
            BaseParams = baseParams,
            BaseColor = p.TryGetProperty("base_color", out JsonElement bc) ? ParseColor(bc) : new RgbColor(38, 38, 38),
            BrightColor = p.TryGetProperty("bright_color", out JsonElement brc) ? ParseColor(brc) : new RgbColor(255, 255, 255),
            DecaySeconds = p.TryGetProperty("decay_seconds", out JsonElement ds) ? ds.GetDouble() : 0.6,
            Bolts = p.TryGetProperty("bolts", out JsonElement bl) ? bl.GetBoolean() : true,
            BoltShape = p.TryGetProperty("bolt_shape", out JsonElement bsh) && bsh.GetString() == "radial" ? BoltShape.Radial : BoltShape.Rays,
            BoltSpeed = p.TryGetProperty("bolt_speed", out JsonElement bsp) ? bsp.GetDouble() : 12.0,
            BoltTail = p.TryGetProperty("bolt_tail", out JsonElement bt) ? bt.GetDouble() : 3.0,
            BoltMaxDistance = p.TryGetProperty("bolt_max_distance", out JsonElement bmd) ? bmd.GetDouble() : 18.5,
            BoltTolerance = p.TryGetProperty("bolt_tolerance", out JsonElement btol) ? btol.GetDouble() : 0.75,
            BoltStyle = p.TryGetProperty("bolt_style", out JsonElement bst) && bst.GetString() == "rainbow" ? BoltStyle.Rainbow : BoltStyle.Solid,
            BoltFlickerSpeed = p.TryGetProperty("bolt_flicker_speed", out JsonElement bfs) ? bfs.GetDouble() : 6.0,
            BoltReset = p.TryGetProperty("bolt_reset", out JsonElement br) ? br.GetBoolean() : false,
        };
    }

    /// <summary>Loads {"current_effect": "...", "params": {...}} (the
    /// shape of the Python daemon's own GET /status) and returns
    /// (effectName, parsedParams). Only "typing_reactive" (optionally
    /// wrapping "custom_keys") and "custom_keys" alone are understood.</summary>
    public static (string EffectName, EffectParams Params) LoadFromStatusJson(string jsonPath)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        JsonElement root = doc.RootElement;
        string effectName = root.GetProperty("current_effect").GetString()!;
        JsonElement paramsElement = root.GetProperty("params");

        EffectParams parsed = effectName switch
        {
            "typing_reactive" => ParseTypingReactiveParams(paramsElement),
            "custom_keys" => ParseCustomKeysParams(paramsElement),
            _ => throw new NotSupportedException(
                $"AdHocPresetLoader only understands typing_reactive/custom_keys right now, got '{effectName}'."),
        };
        return (effectName, parsed);
    }
}
