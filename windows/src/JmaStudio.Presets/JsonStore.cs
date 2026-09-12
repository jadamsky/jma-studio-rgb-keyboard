using JmaStudio.Effects;
using JmaStudio.Hardware;

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

    /// <summary>The controller-reactive effect's own live settings --
    /// deliberately separate from KeyboardPresets, matching the Python
    /// side's controller_reactive.json being its own file, not a regular
    /// preset (see daemon/server.py on `main`). Added in Phase 5 even
    /// though this file/project was built in Phase 4.</summary>
    public JsonStore<ControllerReactiveParams> ControllerReactiveSettings { get; }

    /// <summary>Last-commanded keyboard/lightbar state, persisted on every
    /// change and reloaded at service startup -- settled decision #9:
    /// the Python daemon's in-memory-only state resets to defaults on
    /// every restart, which is fine for an AtLogOn-triggered process but
    /// NOT for a real boot-time Windows Service (dark keyboard/lightbar
    /// before login otherwise). Null means "never set yet, use the
    /// built-in default" -- distinct from any real saved state.</summary>
    public JsonStore<KeyboardPreset?> LiveKeyboardState { get; }
    public JsonStore<LightbarState?> LiveLightbarState { get; }

    /// <summary>Phase 8 (V2) idle screensaver config -- see
    /// IdleScreensaverConfig's own doc comment.</summary>
    public JsonStore<IdleScreensaverConfig> IdleScreensaverConfig { get; }

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
        ControllerReactiveSettings = new JsonStore<ControllerReactiveParams>(
            Path.Combine(directory, "controller-reactive-settings.json"), () => new());
        LiveKeyboardState = new JsonStore<KeyboardPreset?>(
            Path.Combine(directory, "live-keyboard-state.json"), () => null);
        LiveLightbarState = new JsonStore<LightbarState?>(
            Path.Combine(directory, "live-lightbar-state.json"), () => null);
        IdleScreensaverConfig = new JsonStore<IdleScreensaverConfig>(
            Path.Combine(directory, "idle-screensaver-config.json"), () => new());
    }
}
