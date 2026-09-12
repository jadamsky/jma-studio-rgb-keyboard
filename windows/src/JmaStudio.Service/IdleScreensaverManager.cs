// Phase 8 (V2): idle screensaver. When no keyboard, mouse, or controller
// input has been seen for IdleScreensaverConfig.IdleThresholdMinutes,
// stashes whatever's currently live and cycles the keyboard through a
// playlist of existing presets (optionally randomized), plus applies a
// single fixed lightbar preset if configured. Restores the stashed state
// instantly the moment real input resumes from any source.
//
// Composes with ControllerReactiveManager for free: if controller-
// reactive was the active effect when the screensaver kicks in,
// DaemonState.GetEffect() already reports ("controller_reactive", ...)
// as the current effect, so stashing/restoring via GetEffect()/SetEffect()
// alone correctly resumes live controller-reactive rendering afterward --
// no special-casing needed, since ControllerReactiveManager never
// re-reads the daemon's current effect except when Enable() itself is
// called.
//
// Idle detection combines two independent sources, per the user's own
// explicit requirement ("I may be playing a game and only using my
// controller... if I don't input on any device the timer's going"):
//   - Keyboard/mouse: RecordExternalActivity(), called by POST
//     /idle-activity, forwarded from JmaStudio.Gui's own GetLastInputInfo
//     polling (the Service can't see interactive-session input directly
//     -- same Session 0 isolation reason the /keypress fix exists).
//   - Controller: polled directly here every tick and diffed against the
//     previous tick's state (deadzone-aware, so idle analog noise/drift
//     doesn't count) -- no GUI round trip needed for this source.
//
// "Battery wins" (confirmed by the user): Tick() checks
// EffectOverrideCoordinator.BatteryOverrideActive first and returns
// immediately, completely skipping Activate/Deactivate/
// AdvancePlaylistIfDue, while LowBatteryOverrideManager has priority.
// This manager's own idle-clock bookkeeping (RecordExternalActivity,
// controller-activity diffing) keeps running underneath regardless,
// since none of that touches the keyboard/lightbar -- only the
// activation logic itself is paused.

using System.Diagnostics;
using JmaStudio.Effects;
using JmaStudio.Hardware;
using JmaStudio.Presets;

namespace JmaStudio.Service;

public sealed class IdleScreensaverManager : BackgroundService
{
    private const double TickSeconds = 1.0;
    private const double ControllerDeadzone = 0.15; // matches ControllerReactiveParams' own default

    private readonly DaemonState _daemonState;
    private readonly LightbarController _lightbar;
    private readonly PresetStore _store;
    private readonly Controller? _controller;
    private readonly EffectOverrideCoordinator _coordinator;
    private readonly ILogger<IdleScreensaverManager> _logger;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _random = new();

    private double _lastActivityTime;
    private ControllerState? _lastControllerState;

    private bool _active;
    private (string EffectName, EffectParams Params)? _stashedKeyboard;
    private LightbarState? _stashedLightbar;
    private bool _lightbarTouched;
    private double _screensaverEnteredAt;
    private int _lastCycleCount;
    private int _lastPlaylistIndex = -1;

    public IdleScreensaverManager(
        DaemonState daemonState, LightbarController lightbar, PresetStore store, Controller? controller,
        EffectOverrideCoordinator coordinator, ILogger<IdleScreensaverManager> logger)
    {
        _daemonState = daemonState;
        _lightbar = lightbar;
        _store = store;
        _controller = controller;
        _coordinator = coordinator;
        _logger = logger;
        _lastActivityTime = _clock.Elapsed.TotalSeconds;
    }

    /// <summary>Called by POST /idle-activity -- resets the idle clock
    /// for the keyboard/mouse activity source.</summary>
    public void RecordExternalActivity()
    {
        _lastActivityTime = _clock.Elapsed.TotalSeconds;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { Tick(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Idle screensaver tick failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(TickSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void Tick()
    {
        if (_coordinator.BatteryOverrideActive || _coordinator.FlashInProgress) return;

        IdleScreensaverConfig config = _store.IdleScreensaverConfig.Load();

        CheckControllerActivity();

        if (!config.Enabled)
        {
            if (_active) Deactivate();
            return;
        }

        double now = _clock.Elapsed.TotalSeconds;
        double idleSeconds = now - _lastActivityTime;
        bool shouldBeActive = idleSeconds >= config.IdleThresholdMinutes * 60.0;

        if (shouldBeActive && !_active)
        {
            Activate(config);
        }
        else if (!shouldBeActive && _active)
        {
            Deactivate();
        }
        else if (_active)
        {
            AdvancePlaylistIfDue(config, now);
        }
    }

    private void CheckControllerActivity()
    {
        if (_controller is null) return;
        ControllerState current = _controller.GetState();
        if (_lastControllerState is not null && HasSignificantChange(_lastControllerState, current))
        {
            _lastActivityTime = _clock.Elapsed.TotalSeconds;
        }
        _lastControllerState = current;
    }

    private static bool HasSignificantChange(ControllerState a, ControllerState b)
    {
        if (Math.Abs(a.LeftX - b.LeftX) > ControllerDeadzone || Math.Abs(a.LeftY - b.LeftY) > ControllerDeadzone) return true;
        if (Math.Abs(a.RightX - b.RightX) > ControllerDeadzone || Math.Abs(a.RightY - b.RightY) > ControllerDeadzone) return true;
        if (Math.Abs(a.LeftTrigger - b.LeftTrigger) > ControllerDeadzone || Math.Abs(a.RightTrigger - b.RightTrigger) > ControllerDeadzone) return true;
        return a.L1 != b.L1 || a.R1 != b.R1 || a.Square != b.Square || a.Cross != b.Cross
            || a.Circle != b.Circle || a.Triangle != b.Triangle
            || a.DpadUp != b.DpadUp || a.DpadRight != b.DpadRight || a.DpadDown != b.DpadDown || a.DpadLeft != b.DpadLeft
            || a.LeftFn != b.LeftFn || a.RightFn != b.RightFn || a.LeftPaddle != b.LeftPaddle || a.RightPaddle != b.RightPaddle;
    }

    private void Activate(IdleScreensaverConfig config)
    {
        if (config.KeyboardPresetNames.Count == 0)
        {
            _logger.LogInformation("Idle screensaver enabled but has no keyboard presets configured -- skipping activation.");
            return;
        }

        _stashedKeyboard = _daemonState.GetEffect();

        // Three-way branch on LightbarPresetName: null/empty (leave the
        // lightbar alone entirely), the reserved Lights Out sentinel
        // (turn it off), or a real preset name (apply it if it still
        // exists). All three share the same stash/restore bookkeeping --
        // _lightbarTouched just means "did Activate() do ANYTHING to the
        // lightbar," regardless of which of the two active branches did it.
        bool wantsLightsOut = config.LightbarPresetName == IdleScreensaverSentinels.LightsOut;
        LightbarPreset? lbPreset = null;
        bool wantsLightbarPreset = !wantsLightsOut && !string.IsNullOrEmpty(config.LightbarPresetName)
            && _store.LightbarPresets.Load().TryGetValue(config.LightbarPresetName, out lbPreset);
        _lightbarTouched = (wantsLightsOut || wantsLightbarPreset) && _lightbar.Available;
        _stashedLightbar = _lightbarTouched ? _lightbar.GetState() : null;

        _active = true;
        _screensaverEnteredAt = _clock.Elapsed.TotalSeconds;
        _lastCycleCount = 0;
        _lastPlaylistIndex = -1;
        ApplyPlaylistEntry(config, 0);

        if (_lightbarTouched)
        {
            if (wantsLightsOut) _lightbar.Off();
            else if (lbPreset is not null) _lightbar.ApplyState(lbPreset.Lightbar);
        }

        _logger.LogInformation("Idle screensaver activated.");
    }

    private void Deactivate()
    {
        _active = false;

        if (_stashedKeyboard is { } kb)
        {
            _daemonState.SetEffect(kb.EffectName, kb.Params);
        }
        _stashedKeyboard = null;

        if (_lightbarTouched && _stashedLightbar is { } lb && _lightbar.Available)
        {
            _lightbar.ApplyState(lb);
        }
        _stashedLightbar = null;
        _lightbarTouched = false;

        _logger.LogInformation("Idle screensaver deactivated (real input resumed).");
    }

    private void AdvancePlaylistIfDue(IdleScreensaverConfig config, double now)
    {
        if (config.KeyboardPresetNames.Count <= 1) return; // nothing to cycle to
        double elapsed = now - _screensaverEnteredAt;
        int cycleCount = (int)(elapsed / Math.Max(1, config.CycleIntervalSeconds));
        if (cycleCount == _lastCycleCount) return;
        _lastCycleCount = cycleCount;
        ApplyPlaylistEntry(config, cycleCount);
    }

    private void ApplyPlaylistEntry(IdleScreensaverConfig config, int cycleCount)
    {
        IReadOnlyList<string> names = config.KeyboardPresetNames;
        int index;
        if (config.RandomOrder && names.Count > 1)
        {
            do { index = _random.Next(names.Count); } while (index == _lastPlaylistIndex);
        }
        else
        {
            index = cycleCount % names.Count;
        }
        _lastPlaylistIndex = index;

        string name = names[index];
        if (name == IdleScreensaverSentinels.LightsOut)
        {
            // Same "static, all-black" mechanism MainWindow's own Off
            // button and ApiClient.TurnOffAsync() already use -- not a
            // preset lookup, since this is a reserved sentinel, not a
            // real saved preset.
            _daemonState.SetEffect("static", new StaticParams());
            return;
        }

        Dictionary<string, KeyboardPreset> presets = _store.KeyboardPresets.Load();
        if (presets.TryGetValue(name, out KeyboardPreset? preset))
        {
            _daemonState.SetEffect(preset.Effect, preset.Params);
        }
        else
        {
            _logger.LogWarning("Idle screensaver: preset '{Name}' not found, skipping.", name);
        }
    }
}
