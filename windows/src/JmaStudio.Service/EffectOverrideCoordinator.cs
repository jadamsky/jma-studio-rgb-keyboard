// Phase 8 (V2): tiny shared piece of state so the handful of independent
// BackgroundServices that can each call DaemonState.SetEffect() don't
// fight over the keyboard when their conditions overlap.
//
// BatteryOverrideActive: "battery wins" over the idle screensaver
// (confirmed by the user during design) -- LowBatteryOverrideManager
// sets this true/false on its own Activate()/Deactivate(), and
// IdleScreensaverManager.Tick() checks it first and no-ops entirely
// while it's true.
//
// FlashInProgress: set for the brief (well under 1s) window
// PowerStateFlashManager is blinking the keyboard red/green on a
// plug/unplug transition -- checked by both other managers so a cycle
// boundary or activation landing exactly inside that window can't
// clobber the flash mid-blink. See PowerStateFlashManager's own header
// comment for why this exists and why it's not selectable/configurable.
//
// Plain bools are sufficient for both -- every manager here ticks on
// its own single-threaded timer, and a stale read for at most one tick
// has no visible effect.

namespace JmaStudio.Service;

public sealed class EffectOverrideCoordinator
{
    public bool BatteryOverrideActive { get; set; }
    public bool FlashInProgress { get; set; }
}
