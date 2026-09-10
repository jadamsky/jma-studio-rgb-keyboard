// C# analogue of daemon/server.py's controller-reactive enable/disable
// logic (_pre_controller_reactive_state stash-and-restore). This is
// the "full override" plumbing the user explicitly wanted to live in
// the Service, not in JmaStudio.Effects itself -- see
// windows/HANDOFF.md's placement decision.
//
// Known gap, documented rather than silently accepted: unlike a
// regular preset, enabling controller-reactive does NOT survive a
// service restart back to "whatever was running before" -- since
// DaemonState persists whatever effect is live to disk on every
// change, a restart while controller-reactive is enabled will boot
// back into controller-reactive directly, not restore the pre-enable
// effect (the stash below is in-memory only). This mirrors a real
// limitation Python doesn't have to think about (it never persists
// live effect state to disk AT ALL), so it's a genuinely new edge
// case introduced by settled decision #9, not a regression -- flagged
// here for whoever eventually revisits it, not fixed now.

using JmaStudio.Effects;

namespace JmaStudio.Service;

public sealed class ControllerReactiveManager
{
    private readonly object _lock = new();
    private readonly DaemonState _daemonState;
    private readonly Presets.JsonStore<ControllerReactiveParams> _settingsStore;
    private ControllerReactiveParams _liveParams;
    private (string EffectName, EffectParams Params)? _preEnableState;

    public ControllerReactiveManager(DaemonState daemonState, Presets.JsonStore<ControllerReactiveParams> settingsStore)
    {
        _daemonState = daemonState;
        _settingsStore = settingsStore;
        _liveParams = settingsStore.Load();
    }

    public bool Enabled
    {
        get { lock (_lock) return _preEnableState is not null; }
    }

    public ControllerReactiveParams GetLiveSettings()
    {
        lock (_lock) return _liveParams;
    }

    /// <summary>Updates the live (in-memory) settings. Only actually
    /// affects the keyboard right now if controller-reactive is
    /// currently enabled -- matches Python's POST /controller_reactive/
    /// settings, which is a no-op on the real effect unless it's active.</summary>
    public void UpdateLiveSettings(ControllerReactiveParams settings)
    {
        lock (_lock)
        {
            _liveParams = settings;
            if (_preEnableState is not null)
            {
                _daemonState.SetEffect("controller_reactive", _liveParams);
            }
        }
    }

    public void SaveSettings()
    {
        ControllerReactiveParams toSave;
        lock (_lock) toSave = _liveParams;
        _settingsStore.Save(toSave);
    }

    /// <summary>Stashes whatever effect is currently running so Disable()
    /// can restore it exactly, then takes over the keyboard.</summary>
    public void Enable()
    {
        lock (_lock)
        {
            if (_preEnableState is not null) return; // already enabled
            _preEnableState = _daemonState.GetEffect();
            _daemonState.SetEffect("controller_reactive", _liveParams);
        }
    }

    public void Disable()
    {
        lock (_lock)
        {
            if (_preEnableState is not { } previous) return; // already disabled
            _preEnableState = null;
            _daemonState.SetEffect(previous.EffectName, previous.Params);
        }
    }
}
