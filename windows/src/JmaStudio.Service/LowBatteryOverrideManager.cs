// Phase 8 (V2): low-battery lighting override. When the laptop is
// running on battery and the charge drops to or below
// LowBatteryOverrideConfig.ThresholdPercent, overrides both the
// keyboard and lightbar to a single flat dim color (a "go find a
// charger" signal, not an effect) until either the charger is plugged
// back in or the percentage climbs back above the threshold. Restores
// whatever was live the instant either condition clears.
//
// Simpler than the idle screensaver -- no session-isolation problem at
// all. Battery/AC state is plain system hardware state, readable
// directly from this Service process via the plain Win32
// GetSystemPowerStatus API, with no need to route anything through the
// GUI. Polling every 30s is more than sufficient; battery percentage
// doesn't change fast enough to need anything tighter.
//
// "Battery wins" over the idle screensaver (confirmed by the user) --
// enforced via EffectOverrideCoordinator.BatteryOverrideActive, which
// this manager sets on its own Activate()/Deactivate() and which
// IdleScreensaverManager.Tick() checks first, before its own idle logic
// runs. Stash/restore composes correctly with the screensaver
// automatically: DaemonState.GetEffect() at the moment this activates
// naturally captures whatever's live then -- including a
// screensaver-applied effect, if the screensaver was already showing
// and got paused by the coordinator flag -- so restoring here puts
// back exactly that frozen state with no special-casing needed.
//
// Also skips its own Tick logic while EffectOverrideCoordinator.
// FlashInProgress is set (PowerStateFlashManager's brief plug/unplug
// blink) -- purely a belt-and-suspenders guard against the vanishingly
// rare case of this manager's 30s tick landing exactly inside a <1s
// flash window; see PowerStateFlashManager's own header comment.

using JmaStudio.Effects;
using JmaStudio.Hardware;
using JmaStudio.Presets;

namespace JmaStudio.Service;

public sealed class LowBatteryOverrideManager : BackgroundService
{
    private const double TickSeconds = 30.0;

    private readonly DaemonState _daemonState;
    private readonly LightbarController _lightbar;
    private readonly PresetStore _store;
    private readonly EffectOverrideCoordinator _coordinator;
    private readonly ILogger<LowBatteryOverrideManager> _logger;

    private bool _active;
    private (string EffectName, EffectParams Params)? _stashedKeyboard;
    private LightbarState? _stashedLightbar;
    private bool _lightbarTouched;

    public LowBatteryOverrideManager(
        DaemonState daemonState, LightbarController lightbar, PresetStore store,
        EffectOverrideCoordinator coordinator, ILogger<LowBatteryOverrideManager> logger)
    {
        _daemonState = daemonState;
        _lightbar = lightbar;
        _store = store;
        _coordinator = coordinator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { Tick(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Low-battery override tick failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(TickSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>For the GET /battery/status diagnostic endpoint -- cheap
    /// and useful independent of this feature (e.g. a future
    /// Diagnostics hardware-status tile).</summary>
    public static (bool HasBattery, bool OnBattery, int Percent) GetBatteryStatus()
    {
        bool hasBattery = PowerStatus.TryGet(out bool onBattery, out int percent);
        return (hasBattery, onBattery, percent);
    }

    private void Tick()
    {
        if (_coordinator.FlashInProgress) return;

        LowBatteryOverrideConfig config = _store.LowBatteryOverrideConfig.Load();

        if (!config.Enabled)
        {
            if (_active) Deactivate();
            return;
        }

        if (!PowerStatus.TryGet(out bool onBattery, out int percent))
        {
            if (_active) Deactivate();
            return;
        }

        bool shouldBeActive = onBattery && percent <= config.ThresholdPercent;

        if (shouldBeActive && !_active) Activate(config);
        else if (!shouldBeActive && _active) Deactivate();
    }

    private void Activate(LowBatteryOverrideConfig config)
    {
        _stashedKeyboard = _daemonState.GetEffect();
        _lightbarTouched = _lightbar.Available;
        _stashedLightbar = _lightbarTouched ? _lightbar.GetState() : null;

        _active = true;
        _coordinator.BatteryOverrideActive = true;

        var scaledKeyboard = new RgbColor(
            (byte)Math.Clamp(config.KeyboardColor.R * config.KeyboardBrightness, 0, 255),
            (byte)Math.Clamp(config.KeyboardColor.G * config.KeyboardBrightness, 0, 255),
            (byte)Math.Clamp(config.KeyboardColor.B * config.KeyboardBrightness, 0, 255));
        _daemonState.SetEffect("static", new StaticParams { Color = scaledKeyboard });

        if (_lightbarTouched)
        {
            _lightbar.SetBrightness((int)Math.Clamp(config.LightbarBrightness * 100, 0, 100));
            _lightbar.SetAll(config.LightbarColor);
        }

        _logger.LogInformation("Low-battery override activated.");
    }

    private void Deactivate()
    {
        _active = false;
        _coordinator.BatteryOverrideActive = false;

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

        _logger.LogInformation("Low-battery override deactivated.");
    }
}
