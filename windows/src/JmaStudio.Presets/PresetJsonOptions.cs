using System.Text.Json;
using System.Text.Json.Serialization;

namespace JmaStudio.Presets;

public static class PresetJsonOptions
{
    /// <summary>IncludeFields=true because TypingReactiveParams.BoltDirections
    /// is IReadOnlyList&lt;(int Row, int Col)&gt; -- ValueTuple exposes its
    /// data as public fields (Item1/Item2), not properties, which
    /// System.Text.Json otherwise ignores by default. Enums serialize as
    /// their string name (e.g. "Radial", not 1) for the same reason the
    /// Python JSON files are hand-readable/editable.</summary>
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
