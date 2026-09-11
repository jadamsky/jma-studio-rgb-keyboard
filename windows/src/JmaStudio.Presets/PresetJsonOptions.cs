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
    /// Python JSON files are hand-readable/editable.
    ///
    /// PropertyNameCaseInsensitive=true because this same options object
    /// is reused by the GUI (JmaStudio.Gui.ApiClient) to deserialize the
    /// Service's HTTP responses, and not every endpoint emits the same
    /// casing: named record types (KeyboardPreset, LightbarState, ...)
    /// come back PascalCase (matching the file-persistence format these
    /// options were designed for), but a few endpoints return anonymous
    /// C#-object literals with camelCase property names hardcoded right
    /// in the source (see Endpoints.cs's /status, /frame, /lightbar/status).
    /// Without this, a casing mismatch doesn't throw -- it just silently
    /// leaves every non-matching property at its type's default (often
    /// null), which then surfaces far downstream as a confusing
    /// NullReferenceException instead of a clear deserialization error.
    /// Hit exactly that live before adding this.</summary>
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
