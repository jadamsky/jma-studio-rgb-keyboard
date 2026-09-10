// Fixes the Python side's fragile persistence pattern (full-file
// read-modify-write, no atomicity -- a crash mid-json.dump() can
// corrupt or truncate the whole preset file, not just the entry being
// saved). Per windows/HANDOFF.md's settled decision #6: atomic
// temp-file-then-replace, no database.

using System.Text.Json;

namespace JmaStudio.Presets;

public static class AtomicJsonFile
{
    public static T? Read<T>(string path, JsonSerializerOptions options)
    {
        if (!File.Exists(path)) return default;
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, options);
    }

    /// <summary>Writes to a temp file in the same directory, then
    /// File.Move(..., overwrite: true) into place -- atomic on the same
    /// NTFS volume, so a crash mid-write leaves either the old file or
    /// the new one intact, never a truncated/corrupt hybrid.</summary>
    public static void Write<T>(string path, T value, JsonSerializerOptions options)
    {
        string json = JsonSerializer.Serialize(value, options);
        string tempPath = path + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
    }
}
