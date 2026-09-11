// Wraps Lightbar so every mutating call also persists the resulting
// state to disk (settled decision #9 -- boot-time state via disk
// persistence). The Lightbar class itself stays a pure hardware/
// protocol class with no persistence awareness, matching
// JmaStudio.Hardware's "protocol only" scope.

using JmaStudio.Hardware;
using JmaStudio.Presets;

namespace JmaStudio.Service;

public sealed class LightbarController
{
    private readonly Lightbar? _lightbar;
    private readonly JsonStore<LightbarState?> _liveStateStore;

    public LightbarController(Lightbar? lightbar, JsonStore<LightbarState?> liveStateStore)
    {
        _lightbar = lightbar;
        _liveStateStore = liveStateStore;
    }

    public bool Available => _lightbar is not null;

    public LightbarState? GetState() => _lightbar?.GetState();

    public void SetZone(int zone, RgbColor color)
    {
        Require().SetZone(zone, color.R, color.G, color.B);
        Persist();
    }

    public void SetAll(RgbColor color)
    {
        Require().SetAll(color.R, color.G, color.B);
        Persist();
    }

    public void SetMode(LightbarMode mode, RgbColor color, int speed, int brightness)
    {
        Require().SetMode(mode, color.R, color.G, color.B, speed, brightness);
        Persist();
    }

    public void SetBrightness(int value)
    {
        Require().SetBrightness(value);
        Persist();
    }

    public void Off()
    {
        Require().Off();
        Persist();
    }

    /// <summary>Deliberately NOT persisted, unlike every other mutating
    /// method here -- this is the reactive keypress-flash loop's transient
    /// per-frame write, not a "last commanded state" the user actually
    /// asked for. Matches daemon/server.py's _lightbar_reactive_loop,
    /// which calls Lightbar.flash_zones() directly with no persistence of
    /// its own; persisting these would mean a service restart boots back
    /// into whatever flash happened to be live at shutdown instead of the
    /// real last-commanded color/mode.</summary>
    public void FlashZones(IReadOnlyDictionary<int, RgbColor> targets) => Require().FlashZones(targets);

    public void ApplyState(LightbarState state)
    {
        Require().ApplyState(state);
        Persist();
    }

    /// <summary>Reloads and re-applies whatever state was last persisted
    /// -- call once at service startup. No-op if nothing was ever saved
    /// (fresh install) or no lightbar is present.</summary>
    public void RestorePersistedState()
    {
        if (_lightbar is null) return;
        LightbarState? saved = _liveStateStore.Load();
        if (saved is not null)
        {
            _lightbar.ApplyState(saved);
        }
    }

    private Lightbar Require() => _lightbar ?? throw new InvalidOperationException("Lightbar not available.");

    private void Persist() => _liveStateStore.Save(_lightbar!.GetState());
}
