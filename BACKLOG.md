# Backlog

Things we've deliberately parked to come back to later. Not urgent, not forgotten.

## Keyboard-reactive lightbar flashing -- DONE (2026-09-08)

User's keyboard is split into 4 gradient zones (red/green/blue/yellow, left
to right). Asked for pressing a key in zones 1-3 to flash the matching
lightbar zone, and zone 4 to flash all three -- fully configurable colors
(a background color for all 3 zones at rest, an independent flash color
per zone, a separate flash color for the zone-4/all case), not tied to the
keyboard's actual red/green/blue/yellow colors (those just describe the
physical layout to map against).

Built: `Lightbar.flash_zones()` in `hardware/lightbar.py` -- a fast,
single-round zone write (no 3x-repeat/sleep) since this runs on every
tick of a background loop, not as a one-shot "set and forget" command.
`daemon/server.py` runs `_lightbar_reactive_loop()` at ~12.5Hz (matched to
what the lightbar's WMI protocol can sustain), reading recent keypresses
from the existing `InputListener`, mapping each to one of 4 zones by
column position against a one-time-captured set of boundaries (`POST
/lightbar/reactive/capture_zones` -- reads whatever gradient boundaries
are currently live on the keyboard; not a live link, needs re-capturing
if the keyboard's zone layout changes later), then writing the configured
background/flash colors. New "Reactive" panel on the lightbar window:
enable toggle + 5 color pickers, persisted to `lightbar_reactive.json`.

**Real gotcha hit and fixed while building this**: `InputListener.
snapshot(max_age)` prunes its internal press history to whatever
`max_age` is passed, as a side effect of being called -- the reactive
loop originally would have called it with a short window (~0.15s),
which would have silently cut short the keyboard's OWN `typing_reactive`
decay/bolt-travel window (which needs several seconds of history) every
time the reactive loop ticked. Fixed by having the reactive loop call
`snapshot()` with the SAME `_KEY_STATE_MAX_AGE` the keyboard's own render
loop uses, then filtering the returned copy down to its own shorter
window locally, rather than pruning the shared store at a different rate.

**Also hit**: restarting the daemon (needed to load this code) reset the
keyboard back to its saved default preset, wiping the user's live
(unsaved) 4-zone gradient setup. Recovered by re-applying the exact
gradient params captured earlier in the conversation before restarting --
if this happens again with no such record, the user needs to redo it
live and re-run `POST /lightbar/reactive/capture_zones` afterward, since
capture only works while a multi-zone gradient (plain or wrapped inside
typing_reactive) is actually the live effect.

Verified precisely via the daemon's own `/keypress` endpoint (bypassing
physical typing) for all 4 zones, confirming exact resulting lightbar
colors matched the configured mapping, and that it settles back to the
background color once presses stop. User confirmed it works on the real
keyboard/lightbar afterward.

## Lightbar illustration still doesn't look right

User's call: "I think it is still off, but I want to move forward" (2026-09-08). Not
blocking -- the lightbar's actual color control (wheel, zones, brightness,
on/off) all works correctly; this is purely about how the illustration on
`gui/lightbar.html` looks.

**Where it stands**: `gui/lightbar_bar.png` is an AI-generated (Gemini) image
of a 3-zone RGB lightbar in a realistic hardware-render style, background-
removed via a custom alpha un-mix script (not a naive white-threshold, so
soft glow edges fade to transparent instead of leaving a white halo). Each
zone is displayed as an independently `hue-rotate`/`saturate`/`brightness`-
filtered crop of that same image, driven live from the real baseline hue
sampled out of the image per zone (354.3/109.5/215.4 degrees for zone
1/2/3 -- see `ZONE_BASE_HUE` in `gui/lightbar.html`). A seam artifact where
the 3 independently-rotated crops met was reduced (top strip replaced
entirely with a clean CSS gradient using real target colors; the diamond-
glow bleed at zone boundaries softened with a masked crossfade) but the
user still isn't satisfied with the overall look, even after that fix.

**What hasn't been tried yet**:
- Regenerating the source image itself with a different prompt/tool --
  the current one may just not be the right base asset regardless of how
  well it's recolored.
- Asking the user specifically what's "off" about it now (color accuracy
  of the hue-rotate approximation? the metal/frame tone? the diamond
  shape/proportions vs. the real PredatorSense reference? something else
  entirely?) -- this was not pinned down before moving on.
- A non-recolored approach: e.g. getting a NEUTRAL/white-lit version of
  the same asset from the AI tool (in addition to the red/green/blue one),
  which would let zones be tinted via a cleaner mask+solid-color technique
  instead of hue-rotating an already-colored image (hue-rotate is only an
  approximation -- it won't hit every target color accurately, especially
  very desaturated or very dark/bright ones).

**Next session**: ask the user what specifically still looks wrong before
attempting another fix -- don't guess again.

## Lightbar Quick Effects -- DONE (2026-09-08)

Tested live first, per project convention: cycled `SetGamingKBBacklight`
modes 0x01-0x07 (Venator's documented breathing/neon/rainbow/wave/ripple/
scanner/strobe values) on the real hardware. User confirmed watching it
and seeing **real firmware-driven animation** (not just static color
changes), despite the buffer layout being a mix of "probably right" and
"confirmed wrong for mode=0xFF static" per the research below -- so the
risk called out before testing was real, but the hypothesis held for the
animated mode values specifically.

Built afterward: `Lightbar.set_mode(mode, r, g, b, speed, brightness)` and
`MODES` dict in `hardware/lightbar.py`, `POST /lightbar/mode` daemon
endpoint, and a "Quick Effects" chip grid in `gui/lightbar.html` (reuses
the keyboard's `.chip`/`.chip-grid` CSS) that appears when the top bar's
"Dynamic" toggle is selected (previously a disabled "Coming soon"
placeholder, now live). Per Venator's own README, the real firmware only
supports ONE color for the whole bar in these modes (no per-zone control
like Static has) -- so Dynamic mode hides the zone dropdown/pins and
repurposes the existing color wheel as "the one color for the effect,"
pinned to `zoneColors[1]` internally so wheel drags and brightness changes
correctly re-send `/lightbar/mode` with the live color instead of
accidentally writing to an unrelated zone or falling back to the static
commit path (which would silently cancel the animation). Switching back to
Static re-applies the real per-zone static colors, which resets the
firmware's mode byte back to 0 as a side effect.

Verified via Playwright: mode toggle correctly shows/hides the right
controls, all 7 chips render with correct labels, clicking marks the
active chip, and switching back to Static correctly restores zone
controls -- zero console errors. Cleaned up: turned the lightbar off and
removed a leftover `lightbar_presets.json`/`config.json` test entries this
work incidentally left behind while restarting the daemon repeatedly.

**Not done, still open**: no speed control in the UI yet (defaults to a
fixed `speed=5`, matching Venator's own breathing example -- untested
whether other values noticeably change animation speed). `off` mode
(0x00) and `static` mode (0xFF) were deliberately excluded from
`MODES` since the existing `off()`/`set_zone()` static-color path already
covers those correctly. `SetGamingProfile`/`GetGamingProfile` (WMI method
IDs 1/3) remain untested and unexplored -- the user's half-remembered
"built-in preset" may or may not refer to this; nothing confirms it either
way.

## Lightbar Presets -- DONE (2026-09-08)

Built and verified: a separate preset store just for lightbar state
(`lightbar_presets.json`, independent of the keyboard's `presets.json`),
with its own `lightbar_default_preset` key in `config.json`. New daemon
endpoints (`GET/POST /lightbar/presets`, `POST /lightbar/presets/{name}
/apply`, `DELETE /lightbar/presets/{name}`, `GET/POST /lightbar/default`),
`Lightbar.get_state()`/`.apply_state()` in `hardware/lightbar.py`, and a
Presets panel + "Save current as preset" / "Set as default" actionbar +
save modal in `gui/lightbar.html`, all reusing the main window's existing
`.card-grid`/`.preset-card`/`.modal`/`.actionbar` CSS for visual
consistency. Verified end-to-end via Playwright (save, apply, set-default,
delete, server-state match) with zero console errors.
