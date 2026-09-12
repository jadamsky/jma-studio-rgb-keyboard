// Phase 8 (V2): a hardcoded, always-on plug/unplug indicator -- NOT a
// selectable/configurable option (no config file, no endpoint, no GUI
// toggle), per the user's explicit request. Unplugging the charger
// blinks the whole keyboard red twice; plugging it back in blinks it
// green twice; either way, whatever was displaying before the blink
// (a preset, the idle screensaver's current playlist entry, the
// low-battery override's dim color -- anything DaemonState.GetEffect()
// reports) is restored exactly once the blink finishes. Same
// stash/restore-via-GetEffect()/SetEffect() pattern as every other
// override feature in this file (IdleScreensaverManager,
// LowBatteryOverrideManager, ControllerReactiveManager) -- "whatever
// was live" needs zero special-casing regardless of which feature
// produced it.
//
// Polls PowerStatus every 1s (much tighter than LowBatteryOverride
// Manager's 30s -- that one only cares about slow battery drain, this
// one needs to notice a physical plug/unplug quickly enough to feel
// immediate) and fires purely on ACLineStatus transitions, not on
// percentage. The first successful read after startup just establishes
// a baseline (no flash on service start, even if that "transition" is
// technically unknown -> known).
//
// Each blink fades up from black, holds briefly, and fades back down
// (not an instant on/off toggle -- the user's own live feedback on the
// first, abrupt version) via many small interpolated SetEffect calls,
// same technique a real per-frame effect would use, just driven from
// this manager's own timer instead of the render loop. Two blinks take
// just over 1s total; tuned down from an initial slower pass per the
// user's live feedback ("a little faster").
//
// EffectOverrideCoordinator.FlashInProgress is held for the whole
// sequence, so IdleScreensaverManager/LowBatteryOverrideManager can't
// land a SetEffect call mid-blink and clobber it -- see
// EffectOverrideCoordinator's own header comment.

using JmaStudio.Effects;
using JmaStudio.Hardware;

namespace JmaStudio.Service;

public sealed class PowerStateFlashManager : BackgroundService
{
    private const double TickSeconds = 1.0;
    private const int FlashCount = 2;
    private const int FadeInMs = 220;
    private const int HoldMs = 80;
    private const int FadeOutMs = 220;
    private const int GapMs = 130;
    private const int FadeSteps = 18; // ~20ms/step -- smooth without hammering the HID write path
    private static readonly RgbColor UnplugColor = new(255, 0, 0);
    private static readonly RgbColor PlugColor = new(0, 255, 0);
    private static readonly RgbColor Off = new(0, 0, 0);

    private readonly DaemonState _daemonState;
    private readonly EffectOverrideCoordinator _coordinator;
    private readonly ILogger<PowerStateFlashManager> _logger;

    private bool? _lastOnBattery;

    public PowerStateFlashManager(
        DaemonState daemonState, EffectOverrideCoordinator coordinator, ILogger<PowerStateFlashManager> logger)
    {
        _daemonState = daemonState;
        _coordinator = coordinator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Power-state flash tick failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(TickSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync()
    {
        if (!PowerStatus.TryGet(out bool onBattery, out _)) return;

        if (_lastOnBattery is null)
        {
            _lastOnBattery = onBattery; // establish baseline -- no flash on startup
            return;
        }

        if (onBattery == _lastOnBattery.Value) return;
        _lastOnBattery = onBattery;

        await FlashAsync(onBattery ? UnplugColor : PlugColor);
    }

    private async Task FlashAsync(RgbColor color)
    {
        (string EffectName, EffectParams Params) stashed = _daemonState.GetEffect();
        _coordinator.FlashInProgress = true;
        try
        {
            for (int i = 0; i < FlashCount; i++)
            {
                await FadeAsync(Off, color, FadeInMs);
                await Task.Delay(HoldMs);
                await FadeAsync(color, Off, FadeOutMs);
                if (i < FlashCount - 1) await Task.Delay(GapMs);
            }
            _daemonState.SetEffect(stashed.EffectName, stashed.Params);
        }
        finally
        {
            _coordinator.FlashInProgress = false;
        }
    }

    private async Task FadeAsync(RgbColor from, RgbColor to, int durationMs)
    {
        int stepDelayMs = Math.Max(1, durationMs / FadeSteps);
        for (int i = 1; i <= FadeSteps; i++)
        {
            double t = (double)i / FadeSteps;
            var step = new RgbColor(
                (byte)Math.Round(from.R + (to.R - from.R) * t),
                (byte)Math.Round(from.G + (to.G - from.G) * t),
                (byte)Math.Round(from.B + (to.B - from.B) * t));
            _daemonState.SetEffect("static", new StaticParams { Color = step });
            await Task.Delay(stepDelayMs);
        }
    }
}
