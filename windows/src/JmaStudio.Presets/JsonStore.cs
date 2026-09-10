namespace JmaStudio.Presets;

/// <summary>Thin atomic-read/write wrapper around one JSON file, typed
/// to whatever it holds (a presets dictionary, a single config record,
/// etc). One instance per file -- see PresetStore for the 4 real ones.</summary>
public sealed class JsonStore<T>
{
    private readonly string _path;
    private readonly Func<T> _defaultFactory;

    public JsonStore(string path, Func<T> defaultFactory)
    {
        _path = path;
        _defaultFactory = defaultFactory;
    }

    public T Load() => AtomicJsonFile.Read<T>(_path, PresetJsonOptions.Default) ?? _defaultFactory();

    public void Save(T value) => AtomicJsonFile.Write(_path, value, PresetJsonOptions.Default);
}

/// <summary>Bundles the 4 stores that replace presets.json,
/// lightbar_presets.json, lightbar_reactive.json, and config.json on
/// `main`. File names below are new (not the same names as the Python
/// files) since this is a distinct, C#-native format populated via
/// migration, not a drop-in replacement of the same files.</summary>
public sealed class PresetStore
{
    public JsonStore<Dictionary<string, KeyboardPreset>> KeyboardPresets { get; }
    public JsonStore<Dictionary<string, LightbarPreset>> LightbarPresets { get; }
    public JsonStore<LightbarReactiveConfig> LightbarReactiveConfig { get; }
    public JsonStore<AppConfig> AppConfig { get; }

    public PresetStore(string directory)
    {
        KeyboardPresets = new JsonStore<Dictionary<string, KeyboardPreset>>(
            Path.Combine(directory, "keyboard-presets.json"), () => new());
        LightbarPresets = new JsonStore<Dictionary<string, LightbarPreset>>(
            Path.Combine(directory, "lightbar-presets.json"), () => new());
        LightbarReactiveConfig = new JsonStore<LightbarReactiveConfig>(
            Path.Combine(directory, "lightbar-reactive-config.json"), () => new());
        AppConfig = new JsonStore<AppConfig>(
            Path.Combine(directory, "app-config.json"), () => new());
    }
}
