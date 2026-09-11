// System.Text.Json's built-in [JsonPolymorphic]/[JsonDerivedType]
// attribute-driven converter has a real, sharp-edged limitation: it
// reads the JSON object as a forward-only stream and needs the
// "$effect" discriminator to appear BEFORE any other property, or
// deserialization fails outright with a confusing "no parameterless
// constructor" error naming the abstract EffectParams type itself.
// Hit this live: a hand-built request whose JSON object writer put
// "colors" before "$effect" failed 500, even though the exact same
// content with "$effect" moved first succeeded.
//
// Fixed here by buffering the whole object (JsonDocument.ParseValue)
// before looking at any property, so field order stops mattering --
// what any real client (a future GUI, curl, PowerShell's ConvertTo-Json
// on an unordered hashtable) should be able to rely on.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace JmaStudio.Effects;

public sealed class EffectParamsJsonConverter : JsonConverter<EffectParams>
{
    private const string DiscriminatorProperty = "$effect";

    private static readonly IReadOnlyDictionary<string, Type> TypesByDiscriminator = new Dictionary<string, Type>
    {
        ["static"] = typeof(StaticParams),
        ["mask"] = typeof(MaskParams),
        ["probe"] = typeof(ProbeParams),
        ["rainbow"] = typeof(RainbowParams),
        ["puke"] = typeof(PukeParams),
        ["spectrum_cycle"] = typeof(SpectrumCycleParams),
        ["breathing"] = typeof(BreathingParams),
        ["pulse"] = typeof(PulseParams),
        ["custom_keys"] = typeof(CustomKeysParams),
        ["gaming_zone"] = typeof(GamingZoneParams),
        ["starlight"] = typeof(StarlightParams),
        ["confetti"] = typeof(ConfettiParams),
        ["fire"] = typeof(FireParams),
        ["color_wipe"] = typeof(ColorWipeParams),
        ["comet"] = typeof(CometParams),
        ["scanner"] = typeof(ScannerParams),
        ["aurora"] = typeof(AuroraParams),
        ["ripple"] = typeof(RippleParams),
        ["rain"] = typeof(RainParams),
        ["gradient"] = typeof(GradientParams),
        ["typing_reactive"] = typeof(TypingReactiveParams),
        ["controller_reactive"] = typeof(ControllerReactiveParams),
    };

    private static readonly IReadOnlyDictionary<Type, string> DiscriminatorsByType =
        TypesByDiscriminator.ToDictionary(kv => kv.Value, kv => kv.Key);

    public override EffectParams? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        if (!doc.RootElement.TryGetProperty(DiscriminatorProperty, out JsonElement discriminatorEl))
        {
            throw new JsonException($"EffectParams JSON is missing the required \"{DiscriminatorProperty}\" discriminator property.");
        }
        string? discriminator = discriminatorEl.GetString();
        if (discriminator is null || !TypesByDiscriminator.TryGetValue(discriminator, out Type? concreteType))
        {
            throw new JsonException($"Unknown \"{DiscriminatorProperty}\" discriminator '{discriminator}'.");
        }
        return (EffectParams?)doc.RootElement.Deserialize(concreteType, options);
    }

    public override void Write(Utf8JsonWriter writer, EffectParams value, JsonSerializerOptions options)
    {
        Type concreteType = value.GetType();
        if (!DiscriminatorsByType.TryGetValue(concreteType, out string? discriminator))
        {
            throw new JsonException($"No registered \"{DiscriminatorProperty}\" discriminator for type {concreteType.Name}.");
        }

        // Serialize the concrete object on its own, then re-emit with
        // "$effect" injected as the first property -- simplest reliable
        // way to guarantee ordering without hand-writing every property
        // for 22 different types. Nested EffectParams properties (e.g.
        // TypingReactiveParams.BaseParams) correctly recurse through
        // this same converter, since `options` still has it registered.
        using MemoryStream buffer = new();
        using (var innerWriter = new Utf8JsonWriter(buffer))
        {
            JsonSerializer.Serialize(innerWriter, value, concreteType, options);
        }
        using JsonDocument doc = JsonDocument.Parse(buffer.ToArray());

        writer.WriteStartObject();
        writer.WriteString(DiscriminatorProperty, discriminator);
        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
        {
            prop.WriteTo(writer);
        }
        writer.WriteEndObject();
    }
}
