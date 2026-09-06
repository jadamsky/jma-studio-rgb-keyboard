# Project Handoff / Continuity Notes

Written right before a manual context compaction, so a fresh session (or
a post-compaction continuation) can pick this project up without
re-deriving anything or accidentally redoing settled work. Read this
file first if you're picking this project back up cold.

## What this project is

A DIY replacement for Acer PredatorSense's per-key RGB keyboard
lighting on a PH16-71 laptop, because PredatorSense/OpenRGB only expose
a handful of canned hardware effects despite the keyboard being truly
per-key addressable. Everything here computes lighting in software and
pushes full 128-cell frames to the keyboard's own HID protocol.

This is a **finished, working, daily-usable system** as of this
writeup, currently in a "polish/touch-up" phase (branding, layout
fine-tuning, GUI feature requests) rather than core-functionality work.
Nothing described below is speculative or half-built unless explicitly
flagged in "Known limitations."

## Architecture (layering -- preserve this, user cares about it)

```
hardware/device.py   -> daemon/server.py -> effects/*.py -> {cli.py, tray.py, gui.py+gui/}
```

- `hardware/device.py` is the ONLY file that imports `hid`. Two public
  methods: `set_static_color(r,g,b)` and `send_frame(colors)`. Nothing
  above it touches HID directly.
- `daemon/server.py` is a permanent background FastAPI process. It owns
  the HID connection, the currently-running effect, and a 30fps render
  loop. It's the single source of truth; everything else is a thin
  HTTP client.
- `effects/*.py` are pure functions: `render(t, num_cells, params) ->
  list[(r,g,b)]`. The daemon auto-discovers any module in `effects/`
  exposing `NAME` + `render()`.
- `cli.py`, `tray.py`, `gui.py`+`gui/` are all thin clients of the
  daemon's HTTP API. None of them compute frames or touch hardware.

## Confirmed hardware facts (don't re-derive)

- Chicony MCU, USB VID 0x04F2 PID 0x0117, vendor HID interface (usage
  page 0xFF02) is interface 3 on this real machine -- confirmed by
  enumeration, separate from normal keyboard input.
- Every command is an 8-byte feature report `{op, p1..p6, checksum}`,
  `checksum = (0xFF - sum(op,p1..p6)&0xFF) & 0xFF`. Verified byte-for-
  byte by cross-referencing Venator (github.com/Exyons/Venator) and
  Order52/ph16-71-rgb -- their captured checksums reproduce exactly
  under this formula, so it's confirmed, not guessed.
- Static color = 4-command sequence: BEGIN(0x88) -> MODE_ZONE(0xB1) ->
  SET_COLOR(0x14, 0,0,R,G,B,0) -> APPLY(0x08, 0x02, EFF_STATIC=0x01,
  0x05, brightness, SCOPE_ZONE=0x01, 0x01).
- Per-key frame = BEGIN -> MODE_PERKEY(0x12, 0,0,SCOPE_PERKEY=0x08,0,0,0)
  -> eight 64-byte interrupt-OUT packets (128 cells x {0x00,R,G,B}) ->
  APPLY(..., EFF_PERKEY=0x33, ..., SCOPE_PERKEY=0x08, ...).
- hidapi needs a leading `0x00` report-ID byte on every feature report
  AND every `write()` call (stripped before the wire) -- this is baked
  into `hardware/device.py` already.
- The `hid` package (pyhidapi) needs `hidapi.dll` next to `python.exe`
  (in `.venv\Scripts\`) AND `os.add_dll_directory()` called before
  `import hid` on Python 3.8+ Windows, or the bare-name DLL load fails.
  This fix is already in `hardware/device.py`'s top -- don't remove it.

## keymap.json

Complete: 103 of 128 cells mapped to real keys, 25 confirmed-dead
cells (this MCU's 128-cell buffer just has more addressable slots than
this SKU has physical keys). Built via `cli.py discover` (lights one
cell, you type the key name). Two intentional quirks baked into the
naming, both confirmed via `daemon/input_listener.py`'s translation
table: main `/` and numpad-divide are the same physical signal to the
`keyboard` library (light both), same for main Enter and numpad Enter.

## effects/layout.py -- physical grid positions

Hand-authored `{key_name: (row, col)}` table, NOT measured to the
millimeter -- built from photos and PredatorSense's own zone-editor
screenshot, then iteratively corrected key-by-key against user
feedback over ~8 rounds (function-row gaps, right-shift width, arrow
cluster alignment, numpad-0/decimal overlap, etc.). **As of this
writeup the user has said "looks good" / "we're good" on every part of
the layout.** Don't second-guess or re-derive these numbers without a
specific new complaint -- if asked to adjust further, ask for exact
key-to-key relationships (e.g. "X's right edge should align with Y's")
rather than re-measuring from a photo, since photo-based guessing took
several wrong rounds before this per-key-relationship approach (used
for the last ~4 fixes) started working on the first try.

`gui/app.js` has a matching `KEY_WIDTH` / `KEY_HEIGHT` map (num_enter
is a real 2-row-tall key) that must stay in sync with any layout.py
position changes.

## effects/gradient.py -- the PredatorSense-replica effect

Started as a 2-zone (blue/purple) hard-cutover replica of
PredatorSense's own static zone coloring, generalized to support 2-5
zones. Key design:

- `colors: [...]` + `boundaries: [...]` (new, general N-zone form) take
  priority if present.
- `left_color`/`right_color`/`boundary` (legacy 2-zone form) used only
  if `colors` is absent -- kept so old presets render byte-identical.
- `left_overrides`/`right_overrides`/`custom_colors` (key-name lists
  patching individual keys to the first/last zone color, or a fully
  custom color) **only apply in legacy 2-zone mode**. These encode
  PredatorSense's *real* zone assignment for this exact keyboard,
  which doesn't follow a clean vertical line (confirmed against
  PredatorSense's own zone-editor screenshot, then hand-corrected twice
  more per user's own eyes on the real keyboard: `backslash` is
  currently in `right_overrides` -- i.e. purple/zone2 -- per the user's
  most recent explicit correction, overriding what the PredatorSense
  screenshot showed).
- `brightness` default is 0.65 (user tested 1.0 and found it too
  intense).

## effects/typing_reactive.py -- the reactive chase effect

Global keyboard hook (`daemon/input_listener.py`) -> per-cell press
timestamps -> this effect renders an in-place flash AND a chasing bolt
(rays or true-radial shape, solid or rainbow-noise style) outward
across the physical grid. `base_effect`/`base_params` lets it use
*any other effect's render() output* as its background layer instead
of a flat color -- this is how "gradient with a white chase on top"
works (`gradient_white_chase` preset). Multiple simultaneous presses of
the same key produce independent overlapping waves (not one that
resets), per explicit user request.

## Presets (presets.json) -- current 5

- `gradient_only` -- the PredatorSense replica, static, default (see
  `config.json`: `default_preset`).
- `gradient_white_chase` -- gradient background + white typing chase.
- `rainbow_puke` -- chaotic rainbow noise (`effects/puke.py`), pure fun.
- `rainbow_radial_chase` -- typing_reactive, radial rainbow bolts.
- `white_on_white` -- the very first working reactive preset (flat 15%
  white base, white flash/chase).

`cli.py preset-save/-load/-list`, `set-default`/`show-default` manage
these; the GUI has its own save/apply/delete/set-default UI hitting the
same daemon endpoints (`/presets`, `/presets/{name}/apply`, `/default`).

## The GUI (gui.py + gui/) -- built this session, "impressive" was the brief

Native `pywebview` window (title "JMA Studio") loading
`http://127.0.0.1:8420/app/`, which the daemon serves as static files
(`app.mount("/app", StaticFiles(directory="gui", html=True))`) --
avoids CORS entirely, keeps the GUI a pure thin HTTP client like
cli.py/tray.py. Dark theme, blue->purple->pink accent gradient
(intentionally the same palette as the keyboard's own gradient effect).

Features: live keyboard preview (polls `/frame` ~20x/sec, shaped from
`effects/layout.py` + `KEY_WIDTH`/`KEY_HEIGHT`, auto-scales to fit the
window), preset gallery (click to apply, hover to delete, default
badge), quick-effect chips for simple effects, full tuning panels for
`gradient` (now multi-zone, 2-5 zones with dynamic color/boundary
fields) and `typing_reactive`, save-as-preset (custom modal, not
`prompt()`), set-as-startup-default.

**Known hard limitation**: `pywebview`'s `icon` param only works on
GTK/Qt, not Windows (confirmed from the library's own source comment)
-- the native taskbar icon can't be set without compiling this into a
standalone `.exe` with an embedded icon (not done, bigger step, flagged
to user twice, not yet requested).

## Branding (this session's last big feature)

Renamed "RGB Studio" -> "JMA Studio" everywhere (HTML title, pywebview
window title, tray tooltip). Logo: `gui/logo.png`, a bold Segoe UI
"JMA" wordmark with a red->green->blue gradient fill and soft drop
shadow, generated by `gui/make_logo.py` (re-run that script if the
design ever needs to change -- it's a build tool, not part of the
running app). Used as both the tray icon and the in-app header logo.
A script/cursive font was tried first and rejected -- it blurred into
an unreadable smudge at real Windows tray-icon sizes (~16px); bold sans
was the deliberate fix.

Tray icon: left-click opens the Studio directly (`pystray.MenuItem`
with `default=True`), right-click still shows the full menu.

## Testing approach used this session

Playwright (installed in `.venv`, but **deliberately NOT added to
requirements.txt** -- it's a dev/verification tool for me, not a
runtime dependency of the shipped app) drives the actual served
`/app/` page in a headless browser: functional checks (click a preset,
verify the daemon's real `/status` changed), visual checks (screenshot
+ look at it), and console-error checks. This caught a real bug once
(a `hidden` attribute not actually hiding elements due to a CSS
specificity issue with `.class{display:flex}` beating the browser's
default `[hidden]{display:none}` -- fixed with a global
`[hidden]{display:none!important}` rule).

Test scripts live in the **session scratchpad**, not the repo:
`C:\Users\jakea\AppData\Local\Temp\claude\c--Users-jakea-RGB\<session-id>\scratchpad\`
-- specifically `test_gui.py`, `test_gui2.py`, `test_multizone.py`,
plus one-off screenshot scripts. These will NOT survive to a new
session/scratchpad. If deep GUI testing is needed again, they'd need
to be rewritten (quick to redo -- the pattern is: launch Playwright,
`page.goto(BASE+"/app/", wait_until="load")`,
`page.wait_for_selector(".key")` (NOT `networkidle` -- the page polls
`/frame` forever, so networkidle never fires), interact, verify via
both DOM state and the daemon's real `/status`/`/presets` endpoints.

## Critical operational gotchas (learned the hard way this session)

1. **Every python.exe run via this venv actually spawns TWO processes**
   (a `.venv\Scripts\python.exe` launcher + a real
   `...\Programs\Python\Python310\python.exe` child). Both must be
   killed to fully stop something.
2. **`Get-CimInstance Win32_Process | Where CommandLine -like ...` is
   UNRELIABLE** -- it intermittently returns blank `CommandLine`
   values, causing filtered kills to silently miss processes. The
   robust pattern that always works: `Get-Process -Name python |
   Stop-Process -Force` (kills all python processes indiscriminately,
   fine since this project doesn't run anything else named python).
3. **After starting the daemon, verify it's ACTUALLY listening** via
   `Get-NetTCPConnection -LocalPort 8420 | Where State -eq 'Listen'`
   -- don't trust the startup log alone. A stale process holding the
   port can cause a *new* daemon to silently fail to bind while a
   *stale* one keeps answering requests with old code, which happened
   at least twice this session and wasted real time before being
   caught.
4. **Daemon restart is required after editing**: `daemon/server.py`,
   any file under `effects/*.py` (all imported once at startup and
   cached, including `effects/layout.py`'s positions). **Daemon
   restart is NOT required for**: anything under `gui/` (served fresh
   per-request via StaticFiles).
5. **Tray icon restart is required after**: editing `tray.py` itself,
   OR changing `presets.json` (the tray builds its menu once at
   `pystray.Icon()` construction, not dynamically).
6. **GUI window restart is required after**: any `gui/` file change,
   since the already-open pywebview window loaded the old page once
   and won't hot-reload (F5/Ctrl+R in the window works too, if that
   shortcut is available in that pywebview build).
7. The user's own convention established mid-session: after any fix,
   restart affected components and re-verify before reporting done --
   they explicitly asked for this workflow ("restart the tray icon
   after each fix") and it's been followed consistently since.

## User preferences / working style observed this session

- Wants to be walked through *why*, not just told *what* -- e.g. when
  correcting layout spacing, showing the actual arithmetic (measured
  gap ratios, solved-for width equations) got fixes right much faster
  than eyeballing photos repeatedly.
- Prefers exact key-to-key relationship instructions ("shorten X so
  its edge lines up with Y's edge") over "look at this photo again" --
  the latter produced several wrong guesses in a row; the former
  produced correct fixes on the first try, every time, once adopted.
- Corrects mistakes tersely ("I misspoke, I meant X") -- take the
  correction at face value and apply it directly, don't relitigate.
- Appreciates humor/personality in the project (the `puke` effect was
  an explicit, enthusiastic ask: "THIS IS AWFUL. I LOVE IT.").
- Wants things tested before being told they're done, ideally without
  needing to look themselves -- the Playwright-based verification loop
  was explicitly requested ("test the app and make correction if
  necessary") and explicitly OK'd as sufficient in place of a special
  debug API hook ("don't do that if you feel it's not necessary" --
  judged not necessary, correctly, per later confirmation).
- Is fine with autonomous multi-step work without checking in
  constantly, as long as the end state is verified and clearly
  reported.

## Not done / possible next steps (nothing promised, just noted)

- Standalone `.exe` build (PyInstaller or similar) for a real Windows
  taskbar icon -- flagged twice, not requested yet.
- Per-key override editing UI for 3+ zone gradients (currently only
  the legacy 2-zone path supports hardware-quirk key overrides).
- No autostart-on-login wiring for `start_all.bat` -- user explicitly
  said "I don't need it to autostart when windows starts right now."
- No test suite committed to the repo (all Playwright scripts are
  session-scratchpad-only, by original design/convenience, not a
  deliberate decision to exclude testing from the repo).

## Immediate live state as of writing this file

Verified directly against `/status` just now (not assumed from memory
-- worth calling out: right before writing this, `/status` actually
showed a stale `typing_reactive`-wrapping-a-4-zone-gradient state left
over from Playwright test scripts, not `gradient_only` as expected from
memory of "the last thing I did." Re-applied `gradient_only` and
re-verified before trusting it. **Lesson: always re-check `/status`
directly rather than trusting your own recollection of the last action
-- test scripts and manual verification steps leave state behind.**

- Daemon running (port 8420 confirmed listening via
  `Get-NetTCPConnection`), active effect confirmed via `/status` to be
  `gradient` with `gradient_only`'s exact params (boundary 13.5, the
  6 left_overrides / 5 right_overrides including `backslash` in
  right_overrides per the user's latest correction). This daemon
  process has the GUI-open-breaks-chase fix (see above) applied --
  it was restarted after that edit.
- Tray icon and GUI window are NOT currently running -- start with
  `python tray.py` and/or `python gui.py` (or `start_all.bat` for the
  full one-click flow, which also stops AcerLightingService first).
- `AcerLightingService`: Stopped, StartType Automatic (will come back
  on reboot; re-stop manually or via `start_all.bat` next session).

## FIXED: GUI-open-breaks-chase bug

**Symptom (was)**: `typing_reactive`'s chase (bolts) stopped responding
to real keystrokes -- both on the physical keyboard AND in the GUI's
live preview -- whenever the GUI window (`gui.py`) was open.

**First hypothesis (WRONG, but harmless improvement kept anyway)**: GIL
/thread contention from the GUI's 50ms `/frame` polling starving the
global keyboard hook thread. Fixed `/frame`/`/status` to be `async def`
and moved `_keyboard.send_frame()` off the event loop thread via
`run_in_executor` in `daemon/server.py`. This is a legitimate
improvement (still in place) but **did not fix the actual bug** --
synthetic keypress tests kept passing with the GUI open no matter how
this was tweaked, which didn't match the user still seeing it broken
live. Don't re-chase this theory if the bug ever seems to partially
resurface; it's not the mechanism.

**Actual root cause, found via user's own precise observation**: it's
not about the GUI being *open* at all -- it's about the GUI window's
**page content having actual keyboard focus**. The user noticed: chase
works fine while the app is open but unfocused (e.g. alt-tabbed to
something else, or just brought to the OS foreground without clicking
into it -- confirmed a plain `SetForegroundWindow()` call does NOT
reproduce the bug), and breaks the instant they click into the app and
type with it focused. WebView2's embedded Chromium control appears to
consume real keystrokes before Windows' global `WH_KEYBOARD_LL` hook
chain ever reaches the daemon process's hook, specifically when the
page's DOM content (not just the top-level window) holds focus.

**Fix applied**: rather than fight this at the OS/WebView2 level, added
a second, focus-independent input path -- since a focused page is
*guaranteed* to receive normal browser `keydown` events for anything
physically typed, the GUI now forwards those to the daemon directly:
- `daemon/input_listener.py`: `InputListener.register_named_press(name)`
  -- registers a press by keymap.json name directly, bypassing the OS
  hook, reusing the same `_last_press`/lock the hook path uses.
- `daemon/server.py`: new `POST /keypress {"name": ...}` endpoint
  calling it.
- `gui/app.js`: `CODE_TO_KEYMAP_NAME` (a `KeyboardEvent.code` ->
  keymap.json name table, built directly from `keymap.json`'s key
  list) + `wireKeypressForwarding()`, a `window` `keydown` listener
  that POSTs to `/keypress` (skipped while focus is on an `<input>`/
  `<textarea>`, e.g. the save-preset modal, so naming a preset doesn't
  spam bolts). Wired into `init()`. This is additive with the OS hook
  path, not a replacement -- both can fire for the same physical press
  with no ill effect, since the effects already support overlapping
  presses by design.

**Verified live**: a Playwright test (`test_focused_keydown.py`,
scratchpad-only) loaded the real `/app/` page, did an actual `page.click
("body")` (real DOM focus, unlike `SetForegroundWindow`), then used
`page.keyboard.press()` -- genuine browser keydown events on the
focused page, deliberately NOT going through the OS hook at all -- and
confirmed `/frame` showed the expected brightness spike (delta 217 vs
the 38,38,38 base) immediately after each simulated press. This
directly validates the new `/keypress` forwarding path independent of
whatever WebView2 is doing to the OS hook.

Confirmed live against the real physical keyboard + real `gui.py`
window: works now while the app is genuinely focused.

## FIXED: gradient zone-count change drops the typing_reactive wrapper

**Symptom**: after the focus fix above, chase worked fine right up
until changing the number of gradient zones in the Tune -- Gradient
panel's dropdown, at which point it stopped again.

**Root cause**: `applyGradientLive()` in `gui/app.js` unconditionally
POSTed a bare `{name: "gradient", ...}` to `/effect` on every gradient
tuning change, with no awareness that the currently-active effect might
be `typing_reactive` wrapping gradient as its `base_effect` (e.g. the
`gradient_white_chase` preset). Any gradient-panel tweak -- zone count,
a color, a boundary slider -- silently replaced the whole effect with
plain gradient, dropping the chase. Same category of bug as the
earlier dropped-`left_overrides`/`right_overrides` issue, different
code path.

**Fix applied** (`gui/app.js`): added a new `#tr-enabled` checkbox
(see feature below) as the single source of truth for "should the
current tuning state go out wrapped in typing_reactive or not," and
routed both `applyGradientLive()` and `applyTypingReactiveLive()`
through one shared `applyCurrentLive()`:
- `tr-enabled` checked -> POST `typing_reactive` with
  `readTypingReactiveParams()` (which itself nests
  `readGradientParams()` as `base_params` when "Use Gradient panel as
  background" is checked) -- so any gradient-panel tweak now correctly
  re-posts through the typing_reactive wrapper instead of replacing it.
- `tr-enabled` unchecked -> POST whatever the plain background should
  be (`gradient` with `readGradientParams()`, or `static` with the flat
  base color).

**New feature (requested alongside this fix)**: `#tr-enabled` checkbox
at the top of the Tune -- Typing Reactive panel ("Enable typing-reactive
chase") toggles the whole reactive layer on/off without needing to
switch presets -- unchecking it collapses to just the background
(gradient or flat color), rechecking restores the chase with whatever
settings were last tuned. `syncTuningPanelsFromPreset()` sets its
checked state from `preset.effect === "typing_reactive"` so applying a
preset reflects reality. The reactive-only fields (bright/bolt color,
decay, shape, style, speed, tail, flicker) are wrapped in a
`#tr-reactive-fields` container (`display: contents` in `style.css` so
it doesn't disturb the parent `.tune-grid` layout) and hidden via the
`hidden` attribute when disabled.

**Verified** via Playwright (`test_zonecount_regression.py`,
scratchpad-only): applied `gradient_white_chase`, changed the zone-count
dropdown from 2 to 3, confirmed `/status` still showed `typing_reactive`
with a 3-color `base_params.colors`, and confirmed a focused keydown
still produced a brightness spike afterward. Also verified unchecking
`#tr-enabled` switches `/status` to plain `gradient`, and rechecking it
switches back to `typing_reactive`.

## FIXED: any Typing Reactive panel control breaks the chase until focus leaves the app

**Symptom**: after both fixes above, changing gradient zone count kept
working, but touching *any* control in the Tune -- Typing Reactive panel
(the decay slider, bright color, bolt shape/style/speed/tail) broke the
chase again -- until focus moved off the app entirely (not just off
that control).

**Root cause**: this was a bug in the focus-independent `/keypress`
forwarding fix itself (`wireKeypressForwarding()` in `gui/app.js`). Its
exclusion check was `tag === "INPUT" || tag === "TEXTAREA"`, meant to
stop forwarding while typing a name into the save-preset modal's text
field -- but range sliders and color pickers are ALSO `<input>`
elements, and a browser leaves one of those focused after a mouse
interaction (drag a slider, pick a color) until something else is
clicked. So the moment the user touched any slider/color-picker in
either tuning panel, `document.activeElement` became that control, and
the forwarding listener silently excluded every subsequent keydown
until focus left the page (not just the control) -- e.g. alt-tabbing
away, which happened to also take the physical keyboard out of "focus
broke it" territory for the unrelated reason (see the very first fix
above). This one had been silently affecting the Gradient panel's own
sliders too; it just hadn't been reported yet because the zone-count
regression test used the `<select>` dropdown, which isn't an `<input>`
and was never excluded.

**Fix applied** (`gui/app.js`): replaced the tag-only check with
`isTypingIntoTextField()`, which only treats `<textarea>`,
`contenteditable` elements, or `<input>` whose `type` is an actual
text-entry type (`text`, `search`, `email`, `url`, `tel`, `password`,
`number`) as "the user is typing into the UI." Range, color, and
checkbox inputs no longer suppress forwarding just by holding focus.

**Verified** via Playwright (`test_focus_exclusion.py`,
scratchpad-only): clicked `#tr-decay` (a range slider) and confirmed it
became `document.activeElement`, then confirmed physical-style keydowns
still produced a brightness spike; repeated for `#tr-bright` (a color
picker); then opened the save-preset modal and confirmed its real text
field still correctly reports `isTypingIntoTextField() === true` and
still suppresses forwarding.
