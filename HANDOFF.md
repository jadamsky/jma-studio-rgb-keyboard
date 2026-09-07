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

## Custom Key Colors editor (new panel)

A fully manual per-key painter, for cases the other effects don't
cover -- pick any individual key (or several at once) and give it its
own exact color, independent of gradient zones or reactive typing.

- `effects/custom_keys.py`: the simplest possible layered effect --
  `colors` (dict of `{cell index (str): [r,g,b]}`) overrides,
  everything else falls back to `default_color` (default off). No
  daemon changes needed at all; it auto-registers via the existing
  `NAME`+`render()` convention, same as every other effect.
- `gui/app.js`/`index.html`/`style.css`: new "Custom Key Colors" panel
  with its own keyboard grid (`buildKeyboardGrid()` was factored out of
  `buildKeyboard()` so both the live-preview board and this editor grid
  share the same layout math, just with different per-cell behavior).
  Click a key to select it, shift/ctrl-click to multi-select, then use
  the color picker to paint every selected key at once -- this is the
  "replicate the same color over multiple keys" workflow. "Select all"
  /"Deselect"/"Reset selected"/"Clear all" round out the editing
  actions. A "Default" color picker sets the fallback for un-painted
  keys.
- **Recently-used colors**: every applied color (via the picker or a
  recent swatch) gets pushed to a `.ck-swatch` row (dedup, cap 16),
  persisted in `localStorage` (`jma_studio_recent_colors`) -- clicking
  a swatch instantly reapplies that color to the current selection,
  which is the fast-reuse path the user specifically asked for. Lives
  in the browser profile, not synced anywhere -- redundant with actual
  saved presets, and fine to lose (it's just a shortcut, not data).
- `custom_keys` is excluded from the quick-effect chips
  (`HIDDEN_FROM_CHIPS`) like the other panel-driven effects, and
  `syncTuningPanelsFromPreset()`/`presetSwatch()` both got a
  `custom_keys` branch so saving/reapplying a preset built with this
  editor round-trips correctly (verified live below).
- Follows the same `#ck-live` "Apply live" checkbox convention as the
  other tuning panels, debounced the same way.

**Verified live** via Playwright (`test_custom_keys.py` and
`test_custom_keys_preset.py`, scratchpad-only): single-key paint,
shift-click multi-select paint (confirms additive selection, not
replace -- matches normal OS multi-select conventions), recent-swatch
reapply, reset-selected, clear-all (each checked against the daemon's
real `/status`, not just DOM state), and a full save-preset ->
switch-away -> reapply round trip confirming both the daemon's params
and the editor grid's visual re-render came back correctly. No console
errors in any run.

**"Pull current colors" button** (added right after): snapshots
whatever's actually lit right now -- any effect, not just gradient --
via `GET /frame`, into per-key overrides for every mapped cell, then
switches to `custom_keys` with that snapshot. This is the "set up a
gradient, then pull it into custom and edit it" workflow. Verified via
Playwright (`test_pull_current.py`): applied `gradient_only`, captured
`/frame`'s actual per-cell colors (post-brightness-scaling), clicked
the button, and confirmed both the daemon's `params.colors` and the
editor grid's rendered background matched the live frame exactly for
sampled keys.

## Custom Key Colors editor follow-ups (added right after)

Two refinements requested once the panel was in use:

1. **The "Paint" picker (`#ck-picker`) now tracks the selected key's
   actual current color** instead of holding onto whatever was last
   applied. `currentColorForIndex(idxStr)` (override, or
   `customKeyDefault` if none) is read in `updateSelectionVisual()`
   and written into the picker's value every time selection changes --
   so selecting an already-painted key immediately shows its real
   color, ready to nudge from there, rather than starting blind. With
   a multi-selection, it shows the first-selected key's color (a
   reasonable simplification -- no "mixed colors" indicator).

2. **`typing_reactive` can now use Custom Key Colors as its background**,
   not just Gradient or a flat color. New `#tr-use-custom-keys`
   checkbox next to the existing `#tr-use-gradient`, and the two are
   **mutually exclusive by explicit wiring** (checking one force-
   unchecks the other in `wireTypingReactivePanel()`) since
   `base_effect` is a single string on the daemon side -- there's no
   ambiguous state to represent. `readTypingReactiveParams()`,
   `applyCurrentLive()` (both the reactive-on and reactive-off/flat-
   background branches), `syncTuningPanelsFromPreset()`, and
   `presetSwatch()` all got a third `base_effect === "custom_keys"`
   branch alongside the existing `"gradient"` one. Both unchecked still
   falls back to the flat `#tr-base` color, unchanged from before.

**Verified live** via Playwright (`test_ckpicker_and_trbg.py`,
scratchpad-only): picker-tracks-selection for both an unpainted key
(shows default) and a painted one (shows its real color); checking
`#tr-use-custom-keys` unchecks `#tr-use-gradient` and immediately
switches the daemon's live `base_effect` to `custom_keys` (confirmed
via `/status`, including the actual painted color coming through in
`base_params.colors`), and checking `#tr-use-gradient` back correctly
un-checks the other and flips `base_effect` back; a full save-preset ->
switch-away -> reapply round trip confirmed both checkboxes and the
daemon's params come back correctly. No console errors.

## Bloom max distance slider (typing_reactive)

New `bolt_max_distance` param on `effects/typing_reactive.py`: caps how
far a bolt is allowed to travel from its origin key, independent of
`bolt_speed`/`bolt_tail` (which govern travel rate and fade length, not
reach). Cells farther than this -- straight-line distance for
`bolt_shape="radial"`, projected distance along the ray for
`"rays"` -- never light up regardless of elapsed time. Default 35,
comfortably beyond this keyboard's ~20-unit diagonal, so existing
presets that don't set it are visually unaffected.

GUI: new "Bloom max distance" slider in the Reactive Typing panel,
`min=0 max=35 step=0.5`, wired through `readTypingReactiveParams()` /
`updateTypingReactiveLabels()` / `wireTypingReactivePanel()`'s ids
array / `syncTuningPanelsFromPreset()`, same pattern as the other
bolt-tuning sliders.

**Finalized at max=18.5** after the user tested it (originally shipped
with a provisional max=35). Updated three places to match: `#tr-maxdist`'s
`min`/`max`/`value` in `gui/index.html`, `syncTuningPanelsFromPreset()`'s
fallback (`p.bolt_max_distance ?? 18.5`) in `gui/app.js`, and
`DEFAULT_BOLT_MAX_DISTANCE` (now 18.5) in `effects/typing_reactive.py`.
18.5 sits just past this keyboard's real ~18.24-unit corner-to-corner
diagonal (esc to num_enter, computed from `effects/layout.py`'s actual
positions) -- so the slider's max is "full board reach," not an
arbitrary round number, and the default stays visually unconstrained
for old presets that don't set this param.

**Verified**: a direct Python test (not the GUI) confirmed the cap
transitions correctly right at the real distance boundary for both
`bolt_shape` values, using actual `effects/layout.py` positions (e.g.
esc -> num_enter is really ~18.24 units; capped at 10 it doesn't light,
capped at 19 it does). Also verified end-to-end through the GUI
(scratchpad `test_bloom_slider.py`): slider default 35, live-apply
correctly sets `bolt_max_distance` in the daemon's params, and a save/
reapply preset round trip restores the slider's value correctly.

## Reactive Typing panel: slider reorder + Bolt Reset

- Reordered the four sliders so `tr-decay`, `tr-speed`, `tr-tail`,
  `tr-maxdist` sit as one contiguous run (moved Flash decay to right
  before Bolt speed, after the shape/style dropdowns, in
  `gui/index.html`) -- previously the shape/style selects sat between
  decay and the other three, splitting them up.
- New `#tr-bolt-reset` ("Bolt Reset") checkbox next to `#tr-enabled`,
  both non-wide `.field`s so they share a grid row.
- New `bolt_reset` param on `effects/typing_reactive.py`: default
  False keeps the existing behavior (mashing a key spawns one
  independent overlapping wave per press). True collapses each key's
  presses down to just the single most recent one (`min(elapsed_list)`)
  for both the in-place flash and the bolt -- so a second press before
  the first bloom finishes restarts it from scratch instead of adding
  an overlapping second wave. Applied identically in both the flash
  loop and the bolt loop (same `elapsed_values = [min(elapsed_list)]
  if bolt_reset else elapsed_list` pattern in each).
- GUI wiring follows the same pattern as every other typing_reactive
  param: `readTypingReactiveParams()`, `wireTypingReactivePanel()`'s
  ids array, `syncTuningPanelsFromPreset()`.

**Verified**: a direct Python test fed `typing_reactive.render()` two
presses of the same key (one 0.1s old, one 1.0s old, so their bolts sit
at very different radii) -- with `bolt_reset=False` both rings lit
(near ~0-1 units AND far ~7-9.7 units); with `bolt_reset=True` only the
near ring lit, confirming the older press's wave was fully discarded,
not just dimmed. Also verified end-to-end through the GUI
(`test_layout_and_boltreset.py`, scratchpad): the four sliders render
in the corrected contiguous order, the checkbox live-applies
`bolt_reset` to the daemon, and it survives a save/reapply preset
round trip.

## GUI window starting position

`gui.py` previously let pywebview pick the window's initial position,
which landed too low on screen -- required a manual drag up every
time. `_initial_position()` now computes `x` via
`ctypes.windll.user32.GetSystemMetrics(0)` (primary screen width) to
center the 1100px-wide window horizontally, and pins `y=0` (flush to
the top), passed into `webview.create_window(..., x=x, y=y)`. Windows-
only (`gui.py` is Windows-only anyway per the whole project -- guarded
with `sys.platform != "win32"` returning `(None, None)`, pywebview's
own default, just in case this ever runs elsewhere).

**Verified live**: launched `gui.py` fresh and checked the real window
rect via `GetWindowRect` -- landed at `Left=181, Top=0` against a
1463px-wide screen (expected center ~182, off by one rounding pixel),
width exactly 1100 as configured. Confirmed both flush-top and
centered as intended.

## Real app/window icon (finally solved the old "known hard limitation")

Previously documented as a hard limitation: `pywebview`'s `icon=` param
on `create_window()` only works on GTK/Qt, not Windows, so the app
window had no real icon (generic default) unless compiled into a
standalone `.exe`. Turns out that limitation was about
`create_window()` specifically -- this pywebview version (6.2.1) added
a *separate* `icon=` param on `webview.start()` that the WinForms
backend actually does support on Windows (confirmed by reading
`webview/platforms/winforms.py`: `self.Icon = Icon(_state['icon'])`).
No standalone-`.exe` build needed after all.

- `gui/make_app_icon.py`: new build script (same one-off-tool pattern
  as `gui/make_logo.py`) generating `gui/app_icon.png` (1024px master)
  and `gui/app_icon.ico` (multi-size: 16/32/48/64/128/256). Design: a
  bold "J" monogram in the app's own blue -> purple -> pink accent
  gradient (matching `gui/style.css`, not the wordmark's red/green/
  blue, since this icon represents the whole app rather than a tray
  glyph) with a soft blue glow, on the app's dark rounded-square
  background. Distinct from `gui/logo.png` (still used for the tray
  icon and in-app header) -- this one is specifically the window/
  taskbar/Alt-Tab icon.
- `gui.py`: `webview.start(icon=ICON_PATH)` where `ICON_PATH` points
  at `gui/app_icon.ico` (falls back to `None` if the file's missing,
  so a fresh checkout without having run the build script doesn't
  crash, just shows the default icon).

**Design iteration notes** (in case the icon needs revisiting): the
glow went through several rounds before landing right --
1. A gradient-colored glow (matching the letter's own fill) was too
   subtle to read as a glow at all.
2. A dilated, solid-color halo (`ImageFilter.MaxFilter` for a fixed-
   width ring, minimal blur) was clearly visible even at 32px, but
   looked like a hard outline/border rather than actual light.
3. Final version: Gaussian blur, then `alpha * 2.2` clipped to 255
   (saturates most of the blurred area to full opacity), then a small
   second blur to soften the clip's own edge -- real glow softness at
   1024px, still clearly a glow (not a fade to nothing, not a crisp
   ring) at 48px and 32px. This is the balance actually confirmed
   against real downscaled renders, not just the 1024px master --
   worth re-checking at small sizes again if this ever gets tweaked
   further, since the 1024px version alone is a misleading preview
   (a glow that looks great at full res can vanish completely once
   shrunk to a real icon size).

**Verified live**: relaunched `gui.py`, took a real screenshot, and
confirmed the titlebar shows the finished badge icon (not the generic
default) at actual rendered size -- legible as the gradient "J" with
its blue glow even that small.

**Follow-up bug, fixed but NOT yet confirmed by the user**: title bar
icon worked, but the user reported the taskbar button still didn't
show it. This is a well-known separate issue -- the title bar draws
straight from `Form.Icon`, but Windows identifies/groups taskbar
buttons by the host process (`python.exe`) unless the process claims
its own identity, so the taskbar can show `python.exe`'s own icon (or
none) even though the window's own icon is set correctly. Fixed via
`_set_app_identity()` in `gui.py`, called before any window is
created: `ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID
("JMA.Studio.RGBKeyboard")`. Could NOT self-verify this one -- the
taskbar on this machine appears to be auto-hide, and moving the cursor
via `Cursor.Position` (tried, to trigger the reveal) doesn't trigger
Windows' real auto-hide animation the way physical mouse input does,
so a screenshot never captured it. **Next session: confirm with the
user whether the taskbar now shows the icon correctly** -- if not,
next things to try: (a) an explicit `WM_SETICON` via `SendMessage` on
top of the `Form.Icon` assignment (belt-and-suspenders, shouldn't be
necessary per how WinForms is documented to work, but cheap to try),
(b) check whether the AppUserModelID needs to be set even earlier
(before `webview` itself is imported, in case importing it already
triggers some window-system initialization), (c) confirm via
`Get-StartApps`/`explorer.exe` restart whether this is just Windows'
icon cache being stale from earlier test runs under the old (no-AUMID)
code.

## GitHub backup (private repo, license/attribution cleanup)

The user wants this repo pushed to GitHub purely as a personal
disaster-recovery backup ("if I ever have to wipe this computer") --
private for now, but with proper attribution/licensing in place in
case it's ever made public later. This prompted a real find: `hardware/
device.py`'s wire-protocol constants (`OP_BEGIN`, `OP_MODE_ZONE`,
`SUB_APPLY`, `MODE_TAG`, `EFF_STATIC`, `SCOPE_PERKEY`, etc.) weren't
just using the same byte VALUES as the Venator reference project (a
fact, not copyrightable) -- they used Venator's exact constant NAMES
too, which is a much closer relationship than "independently derived
from the same protocol." Checked: Venator is GPL-2.0-only,
Order52/ph16-71-rgb is GPL-3.0.

**Fix applied**: renamed every wire-protocol constant in
`hardware/device.py` to an independently-chosen name (`CMD_HANDSHAKE`,
`CMD_SELECT_SIMPLE_MODE`, `CMD_SELECT_PERKEY_MODE`, `CMD_WRITE_COLOR`,
`CMD_COMMIT`, `COMMIT_ACTION_APPLY`, `COMMIT_RESERVED_TAG`,
`COMMIT_PERSIST_FLAG`, `EFFECT_SOLID`, `EFFECT_PERKEY_BUFFER`,
`TARGET_ZONE`, `TARGET_PERKEY`) while keeping every numeric byte value
byte-for-byte identical -- confirmed via a live restart + preset apply
with no HID write errors afterward, so this was a pure rename, not a
behavior change. Nothing outside `hardware/device.py` referenced the
old names (checked via grep first). This fully decouples the code from
any naming/structure relationship to the GPL sources; only the
protocol facts (unavoidable and not copyrightable) remain shared.

**New files**:
- `LICENSE` -- MIT, copyright Jake Adamsky, with a short pointer at the
  bottom to CREDITS.md for the third-party/GPL-derivation nuance.
- `CREDITS.md` -- full attribution for Venator, Order52/ph16-71-rgb,
  and hidapi (checked hidapi's actual license via its GitHub repo/
  README: choice of GPL-3.0 / BSD-3-Clause / the original permissive
  HIDAPI license), plus an explicit "what was and wasn't reused"
  section, and an explicit statement that this was built agentically
  with Claude to solve a specific PredatorSense limitation (the user
  explicitly asked for this to be stated clearly).
- `hidapi.dll` bundled at the repo root (copied from `.venv\Scripts\`)
  -- the `hid` pip package is a ctypes wrapper and does NOT install
  this native library itself; bundling it means a disaster-recovery
  setup doesn't depend on some external download link still working
  years from now.
- `setup.ps1` -- one-time fresh-machine setup automation: creates
  `.venv`, installs `requirements.txt`, copies `hidapi.dll` into
  `.venv\Scripts\`, creates the Desktop shortcut (with the app icon),
  and registers the "JMA Studio Autostart" Scheduled Task -- i.e. the
  full manual setup this session did by hand earlier, now scripted.
  Self-elevates once (same pattern as `start_all.ps1`) since task
  registration needs admin.
- `README.md` fully rewritten (the old one was a stale "starter
  scaffold" doc predating almost all of the actual work) -- current
  feature list, hardware requirement, install instructions (both the
  `setup.ps1` quick path and a manual step-by-step), usage, current
  architecture, and pointers to CREDITS.md/LICENSE.

**GitHub CLI**: `gh` wasn't installed; installed via
`winget install --id GitHub.cli`. The user then ran `gh auth login`
themselves (deliberately not asking them to paste a token into chat).

**Repo visibility**: private, per explicit user choice, "but setup a
proper license incase I want to go public later" -- which is exactly
why the constant-renaming and CREDITS.md work above happened before
the push rather than being skipped as unnecessary for a private repo.

## 12 new "quick effect" modules (researched, not invented)

User asked for 10 popular RGB lighting effects "found online," with an
explicit "if you find more than 10 please add them." Researched via
WebSearch (Razer Chroma's effect list, Corsair iCUE, and especially
WLED's wiki -- WLED being the most extensively documented open-source
addressable-LED effect library, so the best source for well-known,
genuinely community-loved effect names) rather than inventing effect
names from scratch. Landed on 12, each a new `effects/*.py` file, all
following the existing pure-`render()` contract -- **zero daemon or GUI
code changes needed**, since new effects auto-register (`NAME` +
`render()` convention) and automatically appear as Quick Effect chips
(anything not in `app.js`'s `HIDDEN_FROM_CHIPS` set shows up, and none
of these 12 needed a dedicated tuning panel):

- `breathing.py` -- smooth single-color fade in/out (Corsair/Razer
  staple).
- `spectrum_cycle.py` -- whole board synced to the same hue, sweeping
  the color wheel together (Razer "Spectrum Cycling"). Distinct from
  the existing `rainbow.py`, which staggers hue by position for a
  traveling wave instead.
- `starlight.py` -- random keys softly twinkle in/out independently
  against a dim background (Razer "Starlight"). Distinct from
  `puke.py`'s fast chaotic hue-flicker -- this is slow, soft, and
  monochrome by default.
- `ripple.py` -- concentric rings continuously emanate from the
  keyboard's center (WLED "Ripple"). Autonomous, not press-driven --
  contrast with `typing_reactive.py`.
- `fire.py` -- per-key flame flicker through a black->red->orange->
  yellow-white palette (WLED's iconic "Fire 2012").
- `rain.py` -- droplets fall down each column continuously, fading as
  they go.
- `comet.py` -- a bright head + fading tail sweeps left-to-right and
  wraps around (WLED "Meteor").
- `scanner.py` -- a single band ping-pongs back and forth (WLED "Scan"
  / Knight Rider's KITT scanner).
- `color_wipe.py` -- a color progressively fills the board, then the
  next color wipes over it, cycling a palette (WLED "Wipe").
- `confetti.py` -- random keys spark to a bright random hue and
  quickly fade against a dim background (WLED "Confetti").
- `aurora.py` -- slow overlapping sine waves through a green/blue/
  purple palette, northern-lights style.
- `pulse.py` -- sharp attack + fast decay brightness pulse on a beat,
  optionally hue-cycling. Deliberately distinct from `breathing.py`'s
  slow symmetric cosine fade.

Effects needing spatial awareness (`ripple`, `rain`, `comet`,
`scanner`, `color_wipe`, `aurora`) load `effects/layout.py`'s
`_POSITIONS` at import time, same pattern as `gradient.py`/
`typing_reactive.py`. Effects needing pseudo-randomness per cell
(`starlight`, `fire`, `confetti`) reuse `puke.py`'s cheap deterministic
hash trick (`hash(cell_index, salt) -> [0,1)`) rather than a stateful
RNG, keeping `render()` a pure function of `(t, num_cells, params)`
with no persisted state between frames -- consistent with every other
effect in this codebase.

**Verified**: (1) a direct Python check imported all 12 and called
`render()` at six different `t` values with empty params, asserting
correct list length and valid `(r,g,b)` tuples throughout; (2) cycled
every one live through the real daemon/HID-write path for 0.5s each,
checked logs for zero errors afterward; (3) confirmed via Playwright
that all 12 render as clickable chips in the GUI's Quick Effects
panel, with no GUI code changes required. Daemon restored to
`gradient_only` afterward.

## Full codebase audit + real gaming-overhead measurement

User asked for a thorough clean/efficiency pass over the entire
codebase, plus actual (not reasoned-about) testing of gaming overhead,
explicitly authorizing temporary tool installs if needed. Read every
`.py` file in the project end to end. Findings and fixes:

**The big one -- an overhead fix that was discussed but never actually
applied**: back in the original gaming-overhead conversation, I
identified that `daemon/server.py`'s render loop unconditionally wrote
every computed frame to the keyboard via USB, 30x/sec, forever --
including for fully static effects (`gradient_only`, off, `gaming_zone`,
...) whose output never changes. I proposed fixing it, the
conversation moved on to other things, and it was **never implemented**
-- confirmed by re-reading the file fresh this session. Fixed now:
`_render_loop()` compares each computed frame to `_last_sent_frame`
(plain list equality -- cheap, and correct regardless of object
identity since effects build fresh lists/tuples every call) and skips
`_keyboard.send_frame()` entirely when nothing changed. Still
recomputes every frame either way (cheap for every effect here); only
the actual hardware write is skipped. Added `_frames_rendered`/
`_frames_written` counters exposed via `/status.render_stats` so this
is now an observable, ongoing fact about the system rather than
something only provable by one-off testing.

**Verified this fix directly** (not just reasoned about): applied
`gradient_only`, sampled `render_stats` before/after 3s -- renders kept
advancing at the normal rate, **zero** additional frames written.
Applied `rainbow` (continuously animating) the same way -- writes
tracked renders almost 1:1 (64 of 65), confirming legitimately
time-varying effects are completely unaffected.

**Duplicated code, consolidated**:
- The same 6-line pseudo-random hash (cell index + time-tick ->
  deterministic float) was pasted into six separate files: `puke.py`,
  `typing_reactive.py`, `starlight.py`, `fire.py`, `rain.py`,
  `confetti.py`. Extracted to a new `effects/noise.py`
  (`pseudo_random01(i, tick, salt=0)` -- the `salt` param is a superset
  of every call site's shape, confirmed `salt=0` reproduces the
  original two-term formula exactly since it contributes 0 to the sum).
  Not an effect itself (no NAME/render), same as `layout.py` -- the
  daemon's loader just imports and skips it.
- `gradient.py` and `gaming_zone.py` each had their own copy of "load
  keymap.json, invert it to {name: index}". Extracted to
  `effects/layout.py`'s new `name_to_index()`, alongside the existing
  `cell_positions()` it's a natural sibling of. (Deliberately did NOT
  touch `daemon/input_listener.py`'s own near-identical copy -- that
  file has an explicit stated design principle of staying maximally
  self-contained since it's the one file touching the OS keyboard hook;
  importing from `effects/` would be a backwards architectural
  dependency for no real benefit.)

**Read and found clean, no changes needed**: `hardware/device.py`
(already refactored this session for the GitHub-backup constant
renaming), `daemon/input_listener.py`, `cli.py`, `tray.py`, `gui.py`,
`effects/layout.py`'s core logic, and every simple effect
(`static`, `mask`, `probe`, `rainbow`, `custom_keys`).

**Regression testing after the refactor**: cycled all 20 non-diagnostic
effects live through the real daemon/HID-write path, zero errors in
logs; full Playwright smoke test confirmed all 15 quick-effect chips,
all 6 presets (including the user's own "Red Chase"), both keyboard
previews (103 keys each), zero console errors.

### Real gaming-overhead measurement (psutil, installed temporarily)

Installed `psutil` into `.venv` for process-level CPU/thread/memory
instrumentation -- **deliberately not added to `requirements.txt`**,
same treatment as Playwright: a dev-only diagnostic tool, not a runtime
dependency. Left installed in case future sessions need it again.

Used `psutil.Process.cpu_percent()` sampling (correctly targeting the
real uvicorn worker process, not the `.venv\Scripts\python.exe`
launcher stub -- the exact two-PID gotcha this file already documents
elsewhere, confirmed by finding a 4MB/1-thread process the first time
and fixing the finder to pick the highest-thread-count match instead).
Machine has 32 logical cores, so "% of one core" numbers below are the
right way to read magnitude, not "% of system."

| Scenario | Daemon avg CPU | Daemon max CPU |
|---|---|---|
| Idle, `gradient_only` (post-fix) | 1.4% | 9.4% |
| `typing_reactive`, no presses (resting) | 0.5% | 9.1% |
| `typing_reactive`, heavy synthetic mashing (~20 keys/sec) | 14.8% | 30.3% |
| `puke` (worst-case: every cell changes every frame) | 1.4% | 6.2% |
| GUI open + **focused**, idle | 6.9% | 24.3% |
| GUI open + focused, heavy mashing | 29.0% | 40.6% |
| GUI open + **unfocused** (minimized), idle | 2.6% | 15.6% |
| GUI open + unfocused, heavy mashing | 19.2% | 45.4% |

The GUI's own WebView2 child processes: **18.3%** combined CPU when
the window is focused, dropping to **0.00%** when minimized/unfocused
-- direct confirmation of Chromium's background-tab throttling kicking
in, consistent with everything learned about focus-dependent behavior
earlier in this project. This is the key honest point for "will this
affect my gaming": **you cannot have both the game and the Studio
window focused at once** -- while actually gaming, the Studio (if open
at all) sits unfocused in the background, which is the ~19-20% daemon
/ ~0% GUI row above, not the ~29-40% focused row. The focused numbers
only apply if you've alt-tabbed away from the game to the Studio
itself, i.e. not actually gaming at that moment.

**Leak check**: 90 seconds of sustained aggressive mashing (~33
keys/sec, faster than the earlier tests) -- daemon thread count stayed
exactly 6 and RSS stayed exactly 50.8MB at every 15s checkpoint. No
thread or memory growth under sustained load.

**Bottom line**: even the single worst real number found (~29% of one
core, GUI open AND focused AND heavy mashing simultaneously -- not a
real gaming scenario) is under 1% of this 32-core machine's total
capacity. The realistic worst case while actually gaming (Studio
closed or open-but-unfocused, occasional heavy input) tops out around
15-19% of one core. Daemon restored to `gradient_only` afterward; GUI
test window closed.

## Not done / possible next steps (nothing promised, just noted)

- Standalone `.exe` build (PyInstaller or similar) for a real Windows
  taskbar icon -- flagged twice, not requested yet.
- Per-key override editing UI for 3+ zone gradients (currently only
  the legacy 2-zone path supports hardware-quirk key overrides).
- No test suite committed to the repo (all Playwright scripts are
  session-scratchpad-only, by original design/convenience, not a
  deliberate decision to exclude testing from the repo).

## Autostart on login (added later, after the "not needed yet" note above)

`start_all.bat`/`.ps1` stops `AcerLightingService` (which requires
admin) before starting the daemon and tray icon. Running that on every
login would mean a UAC prompt every time if autostarted naively (e.g.
a plain Startup-folder shortcut). Fixed via a Scheduled
Task instead of disabling the service outright (user's explicit choice
-- keeps the option to fall back to PredatorSense's own lighting later
by just not running this app; the alternative considered was
permanently setting the service's StartType to Disabled, which would
have avoided needing elevation at all but forecloses that fallback).

- Task name: **"JMA Studio Autostart"**, registered via
  `Register-ScheduledTask` (`Get-ScheduledTask -TaskName "JMA Studio
  Autostart"` to inspect/modify later).
- Trigger: `AtLogOn` for the `jakea` user.
- Principal: `LogonType=Interactive`, `RunLevel=Highest` -- this
  specific combination is what makes Task Scheduler elevate silently
  at logon with no UAC dialog (elevation is pre-authorized in the task
  definition itself, unlike an interactive `-Verb RunAs` request).
  "Run whether user is logged on or not" was deliberately NOT used --
  that mode runs non-interactively with no desktop session, which
  would break the tray icon and any GUI window.
- Action: runs `start_all.bat` directly (working directory set to the
  project root) -- no duplicate logic; the task just triggers the
  exact same script a manual double-click would.
- `start_all.bat` now passes `-WindowStyle Hidden` to the inner
  PowerShell call so no console window flashes at login.
- `start_all.ps1`'s own self-elevation check (`-Verb RunAs` if not
  already admin) is now just a fallback for manually double-clicking
  the batch file outside the scheduled task -- it's a no-op when
  launched by the task, since the task already provides an elevated
  token.

**Verified live**: killed all python processes (confirmed zero
running), ran `Start-ScheduledTask -TaskName "JMA Studio Autostart"`
to simulate the logon trigger, and confirmed within ~8s: 4 new python
processes (daemon + tray, 2 PIDs each per the usual venv-launcher
pattern), port 8420 listening and answering `/status`, and
`AcerLightingService` actually stopped (proving it ran elevated) --
all with no UAC prompt shown at trigger time. Not yet verified across
an actual reboot/real logon (only a manual task trigger, which uses
the identical principal/trigger settings a real logon would use).

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
