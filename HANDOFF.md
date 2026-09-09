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

## SOLVED + BUILT: the rear lightbar (fully working, cold-boot verified)

User asked: "can we access the back LED bar too" -- the PH16-71 has a
separate, physical, replaceable rear lightbar module (Acer part
58.QJQN7.001) driven through a **completely different mechanism** than
the per-key keyboard: Windows ACPI-WMI, not USB HID. This spanned
multiple sessions and a LOT of dead ends (see "History of failed
approaches" below, kept for the record) before finally being cracked by
directly instrumenting Acer's own lighting binary with Frida. **Static/
solid per-zone color is now fully working and reproducible.** What's
NOT done yet: no real feature (`hardware/lightbar.py`, daemon endpoints,
GUI/tray exposure) has been built around this -- next session should
start there, using the confirmed protocol below.

### THE WORKING PROTOCOL (verified live, reproduced on demand)

Three WMI calls, in this exact order, repeated 3 times (~65ms apart) --
matches Acer's own real software's cadence exactly (captured live, see
"How this was found" below):

```python
import win32com.client
wmi = win32com.client.GetObject(r"winmgmts:\\.\root\wmi")
instance = list(wmi.InstancesOf("AcerGamingFunction"))[0]

def call_array(method, arr):
    p = instance.Methods_(method).InParameters.SpawnInstance_()
    p.gmInput = list(arr)
    return instance.ExecMethod_(method, p).Properties_("gmOutput").Value

def call_u64(method, value):
    p = instance.Methods_(method).InParameters.SpawnInstance_()
    p.gmInput = value
    return instance.ExecMethod_(method, p).Properties_("gmOutput").Value

# 1. SetGamingLED -- fixed priming/enable trigger. IDENTICAL every time
#    regardless of color/zone/brightness -- do not try to vary this.
LED_PAYLOAD = [0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x15, 0x00, 0x00]
call_array("SetGamingLED", LED_PAYLOAD)

# 2. SetGamingKBBacklight -- mode=0, brightness, tail=(3,2). Carries NO
#    color -- color lives entirely in step 3.
def kb_commit(brightness=100):
    return call_array("SetGamingKBBacklight",
        [0, 0, brightness, 0, 0, 0, 0, 0, 3, 2, 0, 0, 0, 0, 0, 0])
kb_commit()

# 3. SetGamingRgbKb -- one call per zone. mask: 1=zone1, 2=zone2, 4=zone3.
#    THE PACKING FORMULA WE HAD WRONG FOR MONTHS:
#    (R<<8) | (G<<16) | (B<<24) | (0x08<<32) | (mask<<40)
#    NOT (mask | R<<8 | G<<16 | B<<24) as every prior guess assumed --
#    mask lives in byte 5 (bit 40), not byte 0, and there's a constant
#    0x08 in byte 4 that every previous attempt was missing entirely.
def rgbkb(mask, r, g, b):
    return call_u64("SetGamingRgbKb", (r << 8) | (g << 16) | (b << 24) | (0x08 << 32) | (mask << 40))

for _ in range(3):                    # repeat whole group 3x
    call_array("SetGamingLED", LED_PAYLOAD)
    kb_commit(100)
    rgbkb(1, 255, 0, 0)               # zone 1
    rgbkb(2, 255, 0, 0)               # zone 2
    rgbkb(4, 255, 0, 0)               # zone 3
    time.sleep(0.065)
```

Live in `lightbar_experiments/test_real_bytes.py` -- **verified working
end to end, lightbar went solid red on the real hardware.** This is the
reference implementation to build `hardware/lightbar.py` from.

**Confirmed independent of Acer's own software**: re-ran the exact same
script (as `test_real_bytes_blue.py`, red->blue so a no-op couldn't be
mistaken for success) with `AcerLightingService` fully stopped and
`OpenRGB.exe` not running at all -- still worked, lightbar went solid
blue. So this protocol needs nothing from Acer running in the
background; it's a pure standalone WMI call, consistent with this
whole project's "replace PredatorSense entirely" design.

Requires an elevated (Administrator) process, same as every other
Set* method on this class -- see the elevation-architecture question
still open in "Not done yet" below.

### How this was found: instrumenting Acer's own binary with Frida

Every byte-level guess (hundreds of variants, across many sessions) had
failed. The breakthrough came from watching what Acer's own real
software actually sends, using `pip install frida frida-tools` (note:
the current frida release needs Python 3.11+; this project's venv is
3.10, so pin `frida==16.7.19 frida-tools==13.7.1` -- newer frida's
`__init__.py` does `from typing import NotRequired` which doesn't exist
before 3.11 and throws `ImportError` immediately).

1. **Identified the real actor via the WMI-Activity ETW trace**
   (`wevtutil sl Microsoft-Windows-WMI-Activity/Trace /e:true`, then
   `Get-WinEvent` filtered for `ClientProcessId`) -- it is **NOT**
   `AcerLightingService.exe`. It's **`OpenRGB.exe`**, specifically a
   private, never-upstreamed Acer fork ("AcerOpenRGB" per its own
   unstripped PDB debug paths, e.g. `D:\project\AcerOpenRGB\OpenRGB\
   Controllers\...`) bundled inside the `predatorservice` driver
   package (`C:\WINDOWS\System32\DriverStore\FileRepository\
   predatorservice.inf_amd64_*\OpenRGB.exe`), with its own dedicated
   `AcerLightBarController`/`AcerGlobalController`/`AcerUSBController`
   classes (found via `strings`-style extraction of the binary --
   Acer left full debug paths in for the community-derived parts of
   OpenRGB, though not for their own proprietary controller additions).
   `AcerLightingService` only does read-only `GetGamingSysInfo`
   polling; `OpenRGB.exe` is what actually calls every Set* method.
2. **`OpenRGB.exe` only makes its WMI connection once, at its own
   process startup** (not per lighting request) -- so a Frida hook
   attached to an already-running instance sees nothing. Fix: kill it,
   then `Restart-Service -Name AcerLightingService -Force` (this is
   what spawns it) while tight-polling (`psutil`, 5ms interval) for the
   new PID and attaching Frida to it immediately -- this reliably wins
   the race in practice (attach typically lands well before the
   process's own COM initialization completes).
3. **Bootstrapped the actual COM interception** from two well-known,
   PUBLIC, stable vtable layouts (`wbemcli.h`) rather than guessing at
   WMI's higher-level scripting wrappers:
   - Hook `ole32.dll!CoCreateInstance` (exported, hookable by name),
     filter for `rclsid == CLSID_WbemLocator`
     (`{4590F811-1D3A-11D0-891F-00AA004B2E24}`), read the returned
     `IWbemLocator*` from the out-param.
   - Read ITS vtable, hook vtable slot 3 (`ConnectServer`) via
     `Interceptor.attach` on the raw function address (works on ANY
     instance of that COM class since the vtable is shared/static per
     implementing class -- you only need to bootstrap-discover it
     once).
   - In `ConnectServer`'s `onLeave`, read the returned `IWbemServices*`
     out-param, read ITS vtable, hook slot 24 (`ExecMethod`) the same
     way.
   - In `ExecMethod`'s `onEnter`, `args[2]` (`strMethodName`) is
     declared `BSTR` in the IDL but **is NOT a real length-prefixed
     BSTR in practice** -- reading it with proper BSTR semantics
     (`ptr.sub(4).readU32()` length prefix) always returned an empty
     string. It's actually just a plain null-terminated `LPCWSTR`;
     `args[2].readUtf16String()` (no length arg) works perfectly. Cost
     a lot of debugging time -- **don't trust the IDL-declared type
     over an empirical raw-pointer dump when a read comes back
     suspiciously empty.**
   - Filter `strMethodName` for anything starting with `"SetGaming"`,
     then in `onLeave` call the in-params object's own
     `IWbemClassObject::Get(L"gmInput", 0, &variant, NULL, NULL)`
     (vtable slot 4) to cleanly extract the real value -- far more
     reliable than trying to parse raw SAFEARRAY/VARIANT memory layout
     by hand. For byte-array results (`vt=8209` = `VT_ARRAY|VT_UI1`),
     use the real `SafeArrayAccessData`/`SafeArrayGetUBound`/
     `SafeArrayGetLBound`/`SafeArrayUnaccessData` exports from
     `oleaut32.dll` to read it correctly regardless of the SAFEARRAY
     struct's exact internal layout. **`SetGamingRgbKb`'s gmInput came
     back as `vt=8` (`VT_BSTR`, a decimal-string like `"1137832953344"`)**,
     not the `VT_UI8` its own MOF declares -- another IDL-vs-reality
     mismatch; had to add a `vt===8` branch reading a `BSTR*` at the
     usual offset+8 to get this.
   - Working reference script: `lightbar_experiments/frida_hook_v2.py`.
4. **Decoded the captured bytes by hand** (`python -c "...".to_bytes(8,
   'little')"` on the captured decimal strings) to get the real
   `SetGamingRgbKb` packing formula (see above) and confirm
   `SetGamingLED`'s fixed payload and `SetGamingKBBacklight`'s
   color-free commit shape.

### History of failed approaches (kept for the record -- don't redo these)

All of the below were tried, extensively, across multiple sessions,
before the Frida approach cracked it. Not needed for future work, but
kept so nobody re-derives them from scratch or wastes time re-reading
sibling projects that turned out to be red herrings for this exact
chassis:

- **STATIC mode (`0xFF`) in `SetGamingKBBacklight` never worked**,
  despite being Venator's own documented approach for PH16-71 and
  despite hundreds of byte-level variations (tail bytes, reserved byte,
  direction, speed, brightness scale, color channel order, double-
  sends). Turns out this chassis's real software doesn't use STATIC
  mode at all for solid color -- it uses mode=0 (nominally "OFF") as a
  neutral "commit" carrying no color, with the actual color coming
  entirely from `SetGamingRgbKb`. Sending mode=0 with real color also
  never worked on its own (tried per a sibling PHN18 project's finding)
  -- `SetGamingLED`'s priming call turned out to be the missing
  ingredient, not the KBBacklight payload shape.
- **`SetGamingLED` calls always threw "Invalid parameter"** across many
  dozens of content variants, because every attempt used a 16-byte (or
  9-byte) array -- this exact machine's `SetGamingLED` requires
  **exactly 12 bytes** (`MAX=12`), overriding the generic 16-byte value
  a sibling model's decompiled MOF showed. This was invisible in
  `Get-CimClass`'s default summary view; only visible by drilling into
  each parameter's `.Qualifiers` collection directly. Confirmed via
  Scheduled-Task-as-SYSTEM testing that this had nothing to do with
  privilege level either.
- **`SetGamingLEDColor`/`SetGamingLEDBehavior`/plain `SetGamingRgbKb`
  guesses (mask-only packing) never worked** and were readback-
  confirmed to be either non-functional on this chassis or using a
  completely different (and, it turns out, wrong) packing scheme --
  independently corroborated by `cellux-git/nitro-tray`'s own
  extensive, rigorous research on a different Acer model reaching the
  same "investigated, not figured out" conclusion for their equivalent
  secondary LED surface.
- **A generic `SafeArrayAccessData`/`VariantCopy` hook (no COM
  bootstrapping)** did successfully observe some real traffic (this is
  how `SetGamingKBBacklight`'s color-free commit shape was first
  confirmed) but produced way too much unrelated noise from other COM
  activity in the process to reliably isolate `SetGamingLED`/
  `SetGamingRgbKb`'s actual values -- superseded by the precise
  `ExecMethod`-boundary hook described above.
- Multiple sibling reverse-engineering projects for OTHER Acer/Nitro
  models were mined for clues (`Exyons/Venator`, `fredac100/nekro-sense`,
  `RedStiff/Acer_Predator_Tool-PHN18-71`, `jlucaso1/acer-predator-re`,
  `cellux-git/nitro-tray`, `daeora/apge-control`) -- useful for general
  orientation (confirmed the WMI class/GUID, the method-ID numbering
  scheme, the general shape of the problem) but **none had the actual
  correct bytes for THIS chassis** -- every model's firmware build
  differs in real, undocumented ways (array lengths, packing schemes,
  even which methods are wired to real hardware at all). Lesson
  confirmed repeatedly this session: verify everything live against
  THIS machine; treat every external reference as a hypothesis
  generator, never a source of truth.

### The real feature -- built and warm-tested this session

- **`hardware/lightbar.py`** -- new module implementing the working
  protocol above, mirroring `hardware/device.py`'s style (module
  docstring citing sources, `RuntimeError` with a helpful message if
  the WMI class isn't found, clean public API: `Lightbar.set_zone(zone,
  r,g,b)`, `.set_all(r,g,b)`, `.off()`).
  - **Real bug found and fixed**: COM objects are thread-affine, but
    FastAPI/Starlette runs sync `def` endpoints in a worker threadpool
    -- caching one WMI instance at daemon startup and reusing it from a
    later request handler threw `CO_E_NOTINITIALIZED` (0x800401F0,
    "Exception occurred." with a null description -- distinct from the
    earlier "Invalid parameter" COM errors, easy to mistake for a new
    protocol bug if you don't recognize the code). Fixed by resolving a
    fresh WMI instance **per-thread** (`threading.local()` +
    `pythoncom.CoInitialize()` the first time each thread touches it) --
    cheap to do since resolving the instance is a fast local lookup.
    **If this class is ever touched from a different concurrency model,
    re-check this.**
- **`daemon/server.py`**: `Lightbar()` constructed at startup (same
  try/except-and-degrade-gracefully pattern as `Keyboard()`, but
  catching broad `Exception` since COM can throw things beyond
  `RuntimeError`); new endpoints `POST /lightbar/zone` (`{zone, hex}`),
  `POST /lightbar/all` (`{hex}`), `POST /lightbar/off`; `/status` now
  also reports `lightbar_connected`. Refactored the existing `/color`
  endpoint's inline hex-parsing into a shared `_hex_to_rgb()` helper
  used by both.
- **GUI**: a "Lightbar" button in the main window's topbar opens a
  *second* native pywebview window (`gui/lightbar.html`) via a new
  `Api.open_lightbar()` method exposed as `js_api` on the main window
  (`window.pywebview.api.open_lightbar()` from `gui/app.js`) -- the
  established pattern for spawning additional native windows from a
  page's own JS in this pywebview setup, worth reusing for any future
  secondary window. The lightbar page itself is self-contained (own
  inline `<style>`/`<script>`, just links the shared `style.css` for
  the dark theme/accent variables): four **custom canvas-drawn HSV
  color wheels** (hue = angle, saturation = radius, value fixed at 1.0
  and scaled separately by a brightness slider under each wheel) --
  built from scratch rather than `<input type="color">` specifically
  because the user wanted "click a color and it changes immediately,
  no OK button." Pointer events fire live during drag, throttled to
  ~120ms between actual network calls (the swatch preview updates
  instantly regardless) so fast dragging doesn't flood the daemon with
  a request per pixel of movement. One wheel per zone (labeled Left/
  Center/Right, matching the confirmed physical mapping) plus one for
  "All Zones", and a "Turn Off" button.
- **Also fixed in passing**: a long-standing, unrelated latent CSS bug
  the user finally mentioned -- `.hw-label { width: 0 }` (the
  "Connected"/"No hardware" status pill text, injected via `::after`)
  forced that pseudo-element's text to line-wrap inside a zero-width
  box, visually dropping it below the status dot instead of beside it.
  Fixed by removing `width: 0` and adding `white-space: nowrap` to both
  `.hw-label` and `.hw-label::after`.
- **Daemon logging added for the pending cold-boot test** (see below):
  `start_all.ps1` now wraps itself in `Start-Transcript`/
  `Stop-Transcript` to `start_all.log` (captures its own `Write-Host`
  output -- the `AcerLightingService` stop/retry outcome, daemon/tray
  launch decisions -- which otherwise vanishes since the Scheduled Task
  and every child process run `-WindowStyle Hidden`), and redirects the
  daemon's own stdout/stderr to `daemon.log`/`daemon-error.log` via
  `Start-Process -RedirectStandardOutput/-RedirectStandardError`. All
  three are overwritten fresh on every `start_all.ps1` run, so they
  always reflect the most recent boot. **Caveat found while testing
  this**: Python fully-buffers stdout when redirected to a file (unlike
  a real console), so `daemon.log` can appear empty for a while even
  though the process is healthy -- the live `/status` endpoint
  (`lightbar_connected`/`hardware_connected`) is the faster, more
  reliable way to check daemon health than reading the log while it's
  still running; the log is more useful post-mortem (after the process
  exits/crashes, or once enough output has accumulated to flush).
- **Verified warm** (Scheduled Task triggered manually mid-session,
  i.e. the exact real production startup path, not a dev shortcut):
  `/status` reported both `hardware_connected` and `lightbar_connected`
  true; direct calls to all three new endpoints (`/lightbar/zone` green
  on zone 1, `/lightbar/all` magenta, `/lightbar/off`) each visually
  confirmed on the real hardware; the actual GUI window's "Lightbar"
  button and all four color wheels tested live and confirmed working
  by the user ("it worked").
- **NOT yet verified**: a real cold boot (full restart, not just
  re-triggering the Scheduled Task from an already-logged-in session).
  This is the next thing to happen -- see "Immediately pending" below.

### Cold-boot verification: CONFIRMED

User did a genuine full restart (not just re-triggering the Scheduled
Task from an already-logged-in session). Result: **fully working, zero
manual steps, zero UAC prompts.** Confirmed via `/status` showing both
`hardware_connected` and `lightbar_connected` true, with real usage
stats already accumulated (thousands of rendered frames, real input-
listener key events) -- genuine organic post-boot state, not a warm
test artifact. The elevation architecture (Scheduled Task ->
`start_all.ps1` -> daemon inherits elevation) and the lightbar protocol
are both solid end to end. Nothing left to verify on this front.

Two write-ups were produced from this whole investigation afterward,
for sharing with others solving the same problem on their own units:
`LIGHTBAR_REVERSE_ENGINEERING.md` (full detailed account) and
`LIGHTBAR_SUMMARY.md` (short, forum-comment-ready version pointing to
the detailed one). Both live in the project root.

`lightbar_experiments/` was cleaned out after this (test/diagnostic
scripts and downloaded reference material removed -- their extracted
value is fully captured in this file and the two documents above;
captured log/trace output was kept, moved to
`lightbar_experiments/logs/`).

### Not done yet (not blocking, no immediate plan)

1. ~~Elevation architecture, still unresolved~~ **RESOLVED -- already
   solved by the existing architecture, nothing new needed**: verified
   empirically (via `check_elevation.ps1`, reading each process's real
   `TokenElevationType` through `OpenProcessToken`/`GetTokenInformation`)
   that when the daemon is started the normal production way -- via the
   "JMA Studio Autostart" Scheduled Task (`RunLevel HighestAvailable`,
   triggers at login, no interactive UAC prompt) -- the resulting
   `daemon/server.py` process (and `tray.py`, spawned the same way) is
   **already running fully elevated**, because it's a child process of
   the (already pre-elevated-by-the-task) `start_all.ps1`, and child
   processes inherit their parent's integrity level. So: just call the
   WMI code **directly inside `daemon/server.py`'s own process**
   (`import` `hardware/lightbar.py` and call it in-process, same
   pattern as `hardware/device.py`) -- do NOT spawn it as a separate
   script or shell out to a new elevated process the way every test
   script this session did (those needed their own `-Verb RunAs`
   only because they were run standalone, outside the daemon). Once
   in-process, every lightbar HTTP request the daemon serves will run
   with zero additional UAC prompts, for as long as that daemon keeps
   running (i.e. until next reboot/re-login, when the task
   re-elevates automatically again). No separate helper process,
   service, or `schtasks /run` trick needed after all.
2. **Formalize `pywin32` as a real dependency** -- add to
   `requirements.txt` (currently only installed ad hoc in `.venv`).
   Also decide whether `frida`/`frida-tools`/`psutil` (all installed
   this session, `frida` pinned to `16.7.19`/`13.7.1` for Python 3.10
   compatibility) should be documented as dev-only tools (like
   Playwright already is) since they were purely for this
   investigation, not needed by the shipped app.
3. ~~Only 2 of 3 zones' exact real RGB values were ever independently
   cross-checked~~ **RESOLVED**: confirmed live via
   `lightbar_experiments/color_wheel_tester.py` (interactive per-zone
   color picker). Viewed from the front of the laptop with the lid up:
   **mask=1 = left zone, mask=2 = center zone, mask=4 = right zone.**
4. `AcerLightingService` should be left in whatever state matches this
   project's existing convention (`start_all.ps1` stops it on startup,
   same as it always has for the keyboard) once lightbar work resumes
   -- it was started/stopped/restarted many times this session purely
   for testing and its current state at any given moment shouldn't be
   assumed; check `Get-Service -Name AcerLightingService` fresh.

### `lightbar_experiments/` cleaned up after the feature was built and verified

Once the protocol was confirmed working (cold-boot verified) and fully
written up in `LIGHTBAR_REVERSE_ENGINEERING.md` (see project root),
every test/diagnostic script (`.py`/`.ps1`) and downloaded reference
file (`.c`/`.cs`/`.rs`/`.mof`/`.md` copies of sibling projects) was
deleted -- their value is fully captured in that document and this
file, and all are public/re-downloadable from the cited repos if ever
needed again. **What's left**: `lightbar_experiments/logs/` only,
holding the raw captured evidence (ETW trace dumps, Frida capture
output, extracted binary strings) as a permanent record, in case any
conclusion here is ever questioned or needs re-deriving in more detail
than the write-up covers. Nothing in `logs/` is needed to run the app
or to reproduce the working protocol -- it's an audit trail, not a
dependency.

If the protocol ever needs re-verifying (firmware update, different
chassis), `LIGHTBAR_REVERSE_ENGINEERING.md`'s "Getting the real bytes"
section documents the Frida technique in enough detail to rebuild the
capture script from scratch -- it was short-lived tooling, not
something worth keeping on disk indefinitely.

**Critical operational note (still applies to ALL elevated work on this
project)**: a UAC prompt triggered via `Start-Process ... -Verb RunAs
-Wait` can silently report "the operation was canceled by the user" if
it appears without warning and times out unanswered. **Always tell the
user "a UAC prompt is coming, please click Yes within ~15 seconds" in a
separate message BEFORE** issuing the elevated command, never after.
Also: when a script needs the user to act at a specific moment (e.g.
"change a color now"), make sure its `print()` output is NOT redirected
to a file (`*> file.txt`) if the user needs to see a live on-screen cue
-- redirecting silently blanks the visible console window, which
caused real confusion/wasted attempts this session before being caught.

## Real lightbar UI, round 1 (superseded -- see later sections for what it looks like now)

User asked to redesign the lightbar window's UI to replicate a
PredatorSense screenshot they shared (Static/Dynamic mode toggle,
brightness slider with sun icons, a "Select Zone" dropdown, a visual
illustration of the actual hardware with numbered zone markers, and a
color panel: wheel + vertical brightness slider + swatches + RGB
number inputs) -- in this project's own dark theme/fonts so it "feels
the same" as the rest of the app, not copying PredatorSense's colors.
The illustration built in this round (custom SVG trapezoid) was later
completely replaced by an AI-generated image -- see "Lightbar
illustration, take 2" below. Kept here for the single-instance/close-
cascade/brightness work, which is still exactly as built.

### What's built and working

- **`gui/lightbar.html`** completely rewritten (the old 4-separate-
  wheels test layout is gone). New interaction model: ONE zone
  dropdown (Zone 1/2/3/All Zones) + ONE color wheel that edits
  whichever zone is currently selected -- matches the reference image,
  and is a real UX improvement over "4 wheels visible at once."
  - Custom-drawn SVG illustration: a trapezoid shape with a repeating
    vertical-line "vent" pattern (an SVG `<pattern>`), divided into 3
    zone polygons whose `fill` is updated live to match each zone's
    actual current color, plus a thin gradient bar above summarizing
    all 3 zones' colors, plus 3 numbered "pin" buttons below (also
    clickable to select that zone, alongside the dropdown).
  - HSV color wheel (canvas, hue=angle/saturation=radius/value=1,
    reused from the earlier version) + a **vertical** brightness/value
    slider next to it (CSS `-webkit-appearance: slider-vertical` --
    works fine in WebView2/Chromium) -- matches the reference's layout
    (previous version had a horizontal slider under each wheel).
  - Swatch row: an "off" swatch (turns the selected zone/all zones to
    black), a few default preset colors, and a "+" button that saves
    the CURRENTLY shown color as a new persistent swatch via
    `localStorage` -- same pattern as the existing Custom Key Colors
    editor's "recently used colors" feature in `gui/app.js`, kept
    consistent on purpose.
  - R/G/B numeric inputs, kept in sync with the wheel both ways.
  - Static/Dynamic mode toggle: Static is fully wired; Dynamic is
    present but `disabled` with a "Coming soon" tooltip -- animated
    lightbar effects (breathing/wave/etc., confirmed working back when
    the protocol was first reverse-engineered) were deliberately NOT
    wired up this session; this is an honest placeholder, not a fake
    control.
- **Real brightness control wired end-to-end** (previously hardcoded
  to 100 in `hardware/lightbar.py`): `Lightbar` now stores
  `self._brightness`, `set_brightness(value)` updates it and re-
  commits with the CURRENT colors; new daemon endpoint
  `POST /lightbar/brightness {value}`; the top-bar slider in
  `lightbar.html` posts to it (throttled ~150ms during drag).
- **Single-instance enforcement** (`gui.py`): a named Win32 mutex
  (`JMAStudioGUI_SingleInstance`, per-user not `Global\\`) acquired at
  startup via `win32event.CreateMutex` + checking
  `win32api.GetLastError() == 183` (`ERROR_ALREADY_EXISTS`). If another
  instance already holds it, this one finds the existing main window
  via `win32gui.FindWindow(None, "JMA Studio")`, restores it if
  minimized, calls `SetForegroundWindow`, and exits immediately without
  creating any window. **Verified working**: launching a second
  instance's processes exit cleanly within ~2s of detecting the mutex.
  Caveat noted to the user but not re-verified: Windows can sometimes
  block a background process from stealing foreground focus (a built-
  in anti-annoyance restriction) -- `SetForegroundWindow` might
  silently no-op in some contexts; not confirmed either way whether
  this actually happens here.
- **Closing the main window closes everything** (`gui.py`):
  `main_window.events.closed += lambda: os._exit(0)` -- pywebview
  otherwise keeps the process alive until every open window is closed
  individually, which orphaned the lightbar window before this.
  **Verified working** by the user (closed main, lightbar window
  disappeared too).
- **`pywin32` added to `requirements.txt`** (was previously only
  installed ad hoc in `.venv`, flagged as a "not done yet" item earlier
  -- now formalized, since `gui.py` needs it too now for the mutex/
  window-finding calls, on top of `hardware/lightbar.py` already
  needing it).

### RESOLVED: lightbar window position bug

Went through 7 attempts across a session boundary before landing on the
fix (full blow-by-blow no longer needed here -- the short version, in
case a similar DPI issue ever resurfaces elsewhere in this app):

- Root cause was a **DPI-virtualization mismatch**: this process's
  `GetSystemMetrics` reported a scaled-down screen size (1463x914) while
  `GetWindowRect` returned true physical pixels (real screen: 2560x1600,
  a 1.75x mismatch) -- mixing the two in the same calculation (e.g.
  clamping a physical-pixel position against virtualized screen bounds)
  produced wrong results.
- A "more correct" fix (`SetProcessDpiAwareness(2)` to make the whole
  process DPI-aware) was tried and **explicitly rejected by the user**
  because it changed the MAIN window's position, which had always been
  visually correct despite being computed from the "wrong" virtualized
  metrics. Reverted entirely. **Lesson: a mathematically-more-correct
  fix that changes previously-correct real-world behavior is not an
  improvement -- revert it, don't defend the math.**
- Final fix: compute the main window's position ONCE on the main thread
  in `main()` (unchanged from before), cache it in a module-level
  `_main_window_pos` global, and have `Api.open_lightbar()` (which runs
  on pywebview's JS-bridge callback thread, a different thread with
  apparently different DPI-awareness behavior) just READ that cached
  value with a fixed offset -- no recomputation, no cross-thread calls.
  Confirmed working by the user, then fine-tuned the offset down by
  ~27px (0.25in on this 16in/2560x1600/~189-real-PPI panel) per explicit
  follow-up feedback ("bring it up another .25 inch").
- **Lesson reconfirmed for future diagnostics**: this bug went through
  several rounds specifically because intermediate "looks right in my
  own coordinate reading" checks (via PowerShell `GetWindowRect`
  snippets) turned out to be measuring a different coordinate space than
  what the user visually sees, more than once. Always verify by
  restarting and asking the user to look, not by reading coordinates
  programmatically.

## Not done / possible next steps

See `BACKLOG.md` (project root) for the current list of open/parked
items -- kept there instead of here so there's one place to check,
rather than this file and a separate list drifting out of sync.
Long-standing ones not yet moved into that file:

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

## FIXED: two real gui.py bugs found while building the lightbar UI

Both confirmed via direct reproduction before fixing, not just reasoned
about -- see each section for the repro method.

### Single-instance check was silently defeated by Python's own GC

**Symptom**: user closed the app, then was able to open a second full
instance immediately after -- the single-instance mutex (see the
"real lightbar UI, round 1" section above) stopped working sometime
after it was first verified.

**Root cause**: `_acquire_single_instance_lock()` stored the mutex
handle in a **local variable**. The instant the function returned, its
refcount hit zero and pywin32's `PyHANDLE.__del__` closed the underlying
handle -- destroying the named mutex within microseconds of every
launch, despite the code's own comment claiming it was "deliberately
never closed." Confirmed with an isolated repro (a tiny script mirroring
just this pattern) before touching the real code: two processes 5+
seconds apart both reported "not already running" when the handle was a
local var, and correctly detected each other when it was a module-level
global.

**Fix applied**: added a module-level `_instance_mutex` global that the
handle is assigned to, keeping a live reference for the whole process
lifetime -- matching what the code always intended.

**Verified**: relaunched twice with a 6s gap; the second launch's
processes (launcher stub + real interpreter) started, hit the mutex
check, and exited cleanly without creating a window, leaving only the
first instance's "JMA Studio" window running.

### Closing the app took 2+ seconds instead of feeling instant

**Symptom**: user noticed closing the main window (X button) took
"more than three seconds," and correctly guessed it started around the
close-cascade feature.

**Root cause, found by direct timing instrumentation** (not guessed):
added a timestamped log line inside the `closed` event's callback and
compared it against an external stopwatch that sent `WM_CLOSE` --
the callback itself fired in ~7ms (essentially instant), but the
process took ~2s MORE to actually disappear after calling `os._exit(0)`.
Reproduced with a bare minimal pywebview script (no custom code at all)
calling `os._exit(0)` after `webview.start()` returns: also ~2.3s. A
vanilla script with NO `os._exit()` anywhere, just letting the
interpreter exit normally: ~0.5s. So `os._exit()` itself -- not
anything about the close-cascade logic -- was the slow part. Likely
cause: WebView2's Chromium child (GPU/renderer) processes are tied to
the parent via a Windows Job Object, and Windows waits through that
teardown on an abrupt kill, but not on a normal cooperative exit.

**Fix applied** (`gui.py`): the main window's `closed` handler now
calls `.destroy()` on any other open pywebview window (the lightbar)
instead of `os._exit(0)` -- this routes through pywebview's own normal
per-window close path. Once every window is gone, `webview.start()`
returns on its own and the script just falls off the end, letting the
interpreter exit normally. No `os._exit()` anywhere in the file anymore.

**Verified**: same WM_CLOSE-to-process-gone timing method as above, on
the real `gui.py`: ~0.6s, both with only the main window open and with
the lightbar window open too (confirming the cascade-close still works,
just through the fast path).

## Lightbar illustration, take 2: AI-generated image + dynamic recoloring

The custom SVG illustration from "real lightbar UI, round 1" above (a
hand-drawn trapezoid with a repeating vent pattern) never satisfied the
user -- several rounds of SVG refinement (metallic gradients, per-cell
radial "backlit" glow, circuit-trace patterns, ambient bloom filters)
each looked better in isolation but still read as "obviously not real
hardware" next to the actual PredatorSense reference screenshot.

**Approach that finally worked**: stopped trying to hand-draw a
convincing metal/circuit-board texture in SVG, and instead had the user
generate a real image with an AI tool (Gemini), using a prompt this
session wrote after one bad first attempt (the first prompt asked for
"vector" style, which fought against the reference's actual look --
a realistic 3D hardware render, not a flat icon; the corrected prompt
asked for "realistic 3D render / product visualization style" instead
and got a genuinely convincing result on the first try).

- **`gui/lightbar_bar.png`**: the AI-generated image, background-removed
  via a custom script (not a naive white-threshold) -- computes alpha
  from each pixel's distance from pure white using the MIN channel, then
  un-mixes the white contribution out of the remaining color so
  semi-transparent soft-glow edges don't carry a white tint into the
  final composite. A naive threshold would have left a visible white
  halo/fringe around the glow's blur falloff; this doesn't.
- **Dynamic per-zone recoloring**: since the image is a fixed
  red/green/blue render, each zone is displayed as an independently
  cropped copy of the same image with a CSS `hue-rotate`/`saturate`/
  `brightness` filter computed live from the zone's actual RGB color.
  The rotation math uses the REAL baseline hue sampled directly out of
  the image per zone (354.3°/109.5°/215.4° for zone 1/2/3 -- see
  `ZONE_BASE_HUE` in `gui/lightbar.html`), not assumed pure 0°/120°/240°,
  since the AI-rendered colors weren't perfectly pure.
- **Seam artifact found and fixed**: slicing one continuous image into
  3 independently-rotated crops left a visible off-hue sliver right at
  each zone boundary (rotating an already-mixed-hue pixel from the
  original image's blend transition doesn't produce a sensible color).
  Fixed the top glow strip by covering the image's own strip entirely
  and drawing a fresh CSS gradient from the real target colors directly
  (no rotation, no seam possible); softened the much smaller diamond-
  glow-bleed seam with a masked crossfade between adjacent zone crops.

**User's verdict after this whole pass**: "I think it is still off, but
I want to move forward." Not blocking -- the actual color control works
correctly, this is purely cosmetic. See `BACKLOG.md` for what's still
open here (the user's specific objection was never pinned down before
moving on; don't guess at another fix without asking what's wrong
first).

## Lightbar Presets (separate from the keyboard's)

Mirrors the main window's Presets UX (save/apply/delete/set-default,
`.card-grid`/`.preset-card`/`.modal`/`.actionbar` -- all reusing the
main window's existing CSS) but for the lightbar, in its own store
(`lightbar_presets.json`, `config.json`'s `lightbar_default_preset` key)
completely independent of the keyboard's `presets.json`/`default_preset`
-- this was a deliberate architecture choice (asked and confirmed with
the user) rather than trying to make one preset capture both keyboard
and lightbar state together.

`hardware/lightbar.py`'s `Lightbar` class tracks its own last-commanded
state (`get_state()`/`apply_state()`) so a preset captures whichever
state is actually live right now -- originally just static per-zone
colors + brightness, later extended (see "Lightbar Quick Effects" below)
to also capture an active firmware animated mode, and extended again
(see "Keyboard-reactive lightbar flashing" below) to also capture the
reactive feature's on/off state and colors. Each extension kept older
saved presets working via a `preset.get("lightbar", preset)`-style
fallback on both the Python and JS sides, rather than requiring a
migration.

## Lightbar Quick Effects: real firmware-native animated modes

User wanted a Quick Effects section like the keyboard's, populated with
"the known built-in effects." Research found real firmware-native
animated modes (not a software loop) via Venator
(github.com/Exyons/Venator, already cited in
`LIGHTBAR_REVERSE_ENGINEERING.md`) -- their kernel driver
(`kernel/venator.h`) documents `SetGamingKBBacklight` mode byte values
`0x01`-`0x07` for breathing/neon/rainbow/wave/ripple/scanner/strobe,
with color embedded directly in that same 16-byte buffer.

**This carried a real, flagged risk before testing**: that exact buffer
shape (color at bytes 5-7) is ALSO what Venator documents for `mode=
0xFF` ("static"), which this project's own reverse-engineering
confirmed does NOT work on this chassis -- the real static breakthrough
needed a completely separate `SetGamingRgbKb` call instead (see "SOLVED:
the rear lightbar" earlier in this file). So the animated-mode byte
values were a reasonable hypothesis (this file's own account of the
original reverse-engineering separately says animated effects were
"comparatively easy" via "a documented byte layout," almost certainly
this one) but explicitly NOT assumed correct without live testing.

**Tested live before building anything**: cycled modes 0x01-0x07 on the
real hardware via a temporary endpoint, asked the user to watch and
report back precisely (a first vague "seemed to be working" answer was
followed up with a forced-choice question -- real animation vs. just
static color changes -- to get an unambiguous answer). User confirmed
genuine firmware-driven animation.

**Built after confirming**: `Lightbar.set_mode()` + `MODES` dict,
`POST /lightbar/mode`, and a "Quick Effects" chip grid in
`gui/lightbar.html` that appears when the top bar's "Dynamic" toggle is
selected (previously a disabled "Coming soon" placeholder, now live).
**Important hardware constraint carried into the UI**: per Venator's own
README, these firmware modes only support ONE color for the whole bar,
not per-zone like Static -- so Dynamic mode hides the zone dropdown/pins
and repurposes the existing color wheel as "the one color for the
effect," pinned internally to `zoneColors[1]` so wheel drags and
brightness changes correctly re-send `/lightbar/mode` with the live
color instead of silently falling back to the static commit path (which
would cancel the animation).

**Verified**: via Playwright for the UI toggle/chip behavior, and via
the daemon's real endpoints for the actual hardware calls. `off`/
`static` modes were deliberately excluded from `MODES` since the
existing `off()`/`set_zone()` path already covers those.

## Keyboard-reactive lightbar flashing

User's keyboard is split into 4 gradient zones (a `colors`+`boundaries`
gradient preset, left to right). Asked for pressing a key in zones 1-3
to flash the matching lightbar zone, zone 4 to flash all three --
fully configurable colors (independent background color for all 3 zones
at rest, an independent flash color per zone, a separate flash color for
the zone-4/all case), explicitly NOT tied to the keyboard's own zone
colors (those just describe the physical layout to map against).

- **`Lightbar.flash_zones()`**: a fast, single-round zone write (no
  3x-repeat/sleep cadence) since this runs on every tick of a
  continuous background loop rather than as a one-shot "set and forget"
  command -- a dropped write self-corrects on the next tick ~80-100ms
  later, unlike a one-shot static command with no next tick to retry.
- **`_lightbar_reactive_loop()`** in `daemon/server.py`: an async task
  ticking at ~12.5Hz (`_REACTIVE_TICK`, matched to what the lightbar's
  WMI protocol can actually sustain -- nowhere near the keyboard's
  30fps), reading recent keypresses and mapping each to one of 4 zones
  by column position against a set of boundaries captured once via
  `POST /lightbar/reactive/capture_zones` (reads whatever gradient
  boundaries are live on the keyboard right then -- a one-time snapshot,
  not a live link; needs re-capturing if the keyboard's zone layout
  changes later).
- **Real gotcha hit and fixed while building this**: `InputListener.
  snapshot(max_age)` prunes its internal press history to whatever
  `max_age` is passed, as a side effect of being called. The reactive
  loop originally would have called it with a short window (~0.15s),
  which would have silently cut short the keyboard's OWN
  `typing_reactive` decay/bolt-travel window (which needs several
  seconds of history) every time the reactive loop ticked -- a subtle
  cross-feature bug that would have been easy to ship without noticing
  immediately. Fixed by having the reactive loop call `snapshot()` with
  the SAME `_KEY_STATE_MAX_AGE` the keyboard's own render loop already
  uses, then filtering the returned copy down to a shorter window
  locally, rather than pruning the shared store at a different rate.
- **Also hit**: restarting the daemon (needed to load this code) resets
  the keyboard back to its saved default preset, which silently wiped
  the user's live (unsaved) 4-zone gradient setup once. Recovered by
  re-applying the exact gradient params recorded earlier in the
  conversation before restarting. The user has since saved that exact
  4-zone layout as a real keyboard preset (`"ZONES"` in `presets.json`),
  so this specific risk shouldn't recur for this particular layout --
  but the general risk (any daemon restart reverts unsaved live keyboard
  state to the saved default) still applies for anything not saved.
- **New "Reactive" panel** on the lightbar window: enable toggle + 5
  color pickers, persisted to `lightbar_reactive.json`. Bundled into the
  Presets system afterward (user pointed out the enable checkbox and
  colors acted as a global setting with no way to save as part of a
  preset) -- `POST /lightbar/presets/save` now captures both
  `Lightbar.get_state()` AND the current reactive settings (background/
  flash colors/enabled, but deliberately NOT `zone_boundaries`, which
  describes the keyboard's own zone geometry rather than anything that
  should vary preset to preset) into one `{"lightbar": ..., "reactive":
  ...}` object; applying a preset restores both together. Presets saved
  before this change are a flat dict with no `"lightbar"`/`"reactive"`
  keys -- both the Python and JS sides fall back to treating the whole
  object as the lightbar part for those, so nothing broke.

**Verified precisely**: simulated presses in each of the 4 zones via the
daemon's own `/keypress` endpoint (bypassing physical typing) and
inspected the real resulting lightbar zone colors after each, confirming
exact matches to the configured mapping and that it settles back to the
background color once presses stop. User then confirmed it works on the
real keyboard/lightbar.

## Git: `stable` branch caught up to `main` (2026-09-08)

Per the branching convention established earlier in this file (tag/fast-
forward `stable` to match `main` at good checkpoints, rather than
maintaining two diverging branches): committed this whole session's work
(everything from "real lightbar UI, round 1" through "keyboard-reactive
lightbar flashing" above) as a single commit on `main`, then fast-
forwarded `stable` to match (`git branch -f stable main`). Both branches
now point at the same commit locally. Not yet pushed to `origin` at time
of writing -- see the live git status below/ask the user before pushing,
since that's a shared-state action worth confirming each time rather
than assuming.

## Immediate live state as of writing this file (current, 2026-09-08)

Verified directly against the running daemon just now:

- Daemon running, `hardware_connected` and `lightbar_connected` both
  true. Keyboard's live effect: `typing_reactive` wrapping the "Red
  Chase" preset (`custom_keys` base + reactive chase on top) -- this is
  `config.json`'s actual `default_preset`, so a normal reboot/relaunch
  reproduces this exactly. The user's 4-zone red/green/blue/yellow
  gradient (used to capture the reactive lightbar feature's zone
  boundaries) is saved separately as the `"ZONES"` preset in
  `presets.json` -- apply that one to bring it back live if needed.
- Lightbar: currently showing whatever the `"BLUE"` preset last set (a
  static 3-zone blue/cyan look). The keyboard-reactive lightbar feature
  is **enabled** right now with the user's own configured colors
  (background `#0008ff`, all three zone-flash colors red, zone-4/all
  flash `#fb00ff`) -- typing on the real keyboard will flash the
  lightbar per the "Keyboard-reactive lightbar flashing" section above.
- Git: `main` and `stable` both at commit `af3bf95` ("Add lightbar UI,
  presets, firmware animation modes, and keyboard-reactive flashing"),
  2 commits ahead of `origin/main`/`origin/stable`. Not yet pushed --
  ask before pushing, per the note in the git section above.
- User's own words at this point: "everything is stable time to commit
  this to stable" (done, see above), then asked for this file to be
  fully brought current (this whole set of sections, done), and
  mentioned "another big project after this" -- nothing further
  specified yet as of this writing.

## Controller Reactive: PS5 DualSense keyboard-override effect (new, this session)

The "another big project" from above. User plays most games on this PC
via Steam using a PS5 DualSense Edge controller, and wanted a keyboard
effect driven by it -- different keys light up for different controller
inputs. Explicit constraints given up front: wired (USB) first,
Bluetooth later; needs its own page; enabling it fully overrides
whatever else the keyboard is doing; it's NOT a regular preset, but it
does need its own separate "save current settings"; always enabled
manually, so no startup-default logic.

### Architecture

- **`hardware/controller.py`** (new) -- `Controller` class, background
  thread continuously reading raw USB HID reports from the DualSense,
  exposing the latest parsed state via a thread-safe `get_state()`
  (same shape as `Lightbar`'s per-thread-COM pattern conceptually, but
  simpler since HID reads don't have COM's thread-affinity issue --
  just a plain lock-protected dict updated by one background thread and
  read by the render loop). Graceful-degrades to `None` if no
  controller is connected, same pattern as `Keyboard`/`Lightbar`.
  **Real gotcha hit**: forgot this file needs the exact same
  `os.add_dll_directory()` fix `hardware/device.py` has before
  `import hid` -- without it, and because Python imports
  `hardware.controller` before `hardware.device` alphabetically in
  `daemon/server.py`'s import list, the bare `import hid` failed to
  find `hidapi.dll` and **crashed the entire daemon on startup** (not
  just "controller unavailable," everything was down). Fixed by adding
  the identical DLL-directory-registration block to this file too --
  don't assume only the first hardware/*.py file to `import hid` needs
  this; any of them independently do, depending on import order.
- **`effects/controller_reactive.py`** (new) -- pure `render()`
  function, reads `controller_state` from `params` (injected by the
  daemon each frame, same pattern as `typing_reactive`'s `key_state`).
  See "Final design" below for exactly what it does.
- **`daemon/server.py`**: `_controller` global (initialized at startup,
  try/except-degrade like keyboard/lightbar), `controller_state`
  injected into every `_render_loop()` frame unconditionally (cheap,
  effects that don't care just ignore it -- same pattern as
  `key_state`). New endpoints: `POST /controller_reactive/enable`
  (saves whatever was running as `_pre_controller_reactive_state` so
  `disable` can restore it exactly, then takes over `_current_effect`),
  `POST /controller_reactive/disable`, `GET/POST /controller_reactive
  /settings` (live-update, does NOT persist), `POST /controller_reactive
  /settings/save` (persists to `controller_reactive.json`, separate
  from `presets.json`), `GET /controller_reactive/button_groups` (group
  name -> keys it lights, so the GUI doesn't hardcode a second copy),
  `GET /controller_reactive/defaults` (single source of truth for the
  "Default" button's target values), `GET /controller_reactive/status`.
- **`gui/controller_reactive.html`** (new) -- its own standalone window,
  opened via a new `Api.open_controller_reactive()` in `gui.py`
  (identical cascade-from-cached-main-position pattern as
  `open_lightbar()`) and a new "Controller Reactive" button on the main
  window (`gui/index.html`/`app.js`).

### The HID protocol (DualSense Edge, confirmed empirically)

Same technique as the lightbar's original reverse-engineering (ask the
user to hold a specific input, snapshot the raw report, diff against an
idle baseline) but far faster here since Sony's general DualSense
report layout is well-documented publicly -- only needed a couple of
spot-checks to confirm the well-known layout actually matched this
unit, then genuinely fresh captures only for the completely-
undocumented DualSense-Edge-only extensions (rear paddles, Fn buttons).
**Lesson already learned from the lightbar work paid off directly here
too**: don't just trust the public docs -- Cross (bit5 of byte 8) and
D-pad-Up (hat value 0) and L1 (bit0 of byte 9) were each independently
confirmed live before trusting the rest of that same byte's documented
bit layout for the untested buttons (Square/Circle/Triangle, R1).

USB report: report ID `0x01`, 64 bytes total. Bluetooth uses a
different report ID/length and is NOT implemented at all yet.

| Bytes | Meaning |
|---|---|
| 1-2 | Left stick X, Y (raw 0-255, center ~128) |
| 3-4 | Right stick X, Y (same scale) |
| 5 | L2 analog trigger (0-255) |
| 6 | R2 analog trigger (0-255) |
| 8 low nibble | D-pad hat switch: 0=up, 2=right, 4=down, 6=left, 8=neutral (0=up confirmed live) |
| 8 high nibble | bit4=Square, bit5=Cross, bit6=Circle, bit7=Triangle (Cross confirmed live) |
| 9 | bit0=L1, bit1=R1 (both confirmed live) |
| 10 | **DualSense Edge only, not in general public docs -- all 4 confirmed live**: bit4=left Fn, bit5=right Fn, bit6=left paddle (L4), bit7=right paddle (R4) |

Stick Y axis: confirmed live that pushing up DECREASES the raw byte
toward 0 (screen-coordinate convention) -- pushing down increases it
toward 255.

### Final design (went through one full redesign after the first build)

The FIRST version shared one color across both sticks and used fixed
per-direction key lists with a single deadzone. After live testing, the
user asked for a full color-model redesign:

- **Background** can be toggled on/off independently of its stored
  color (a checkbox next to the color picker) -- off forces pure black
  regardless of the picked color.
- **Each stick has its own 3 colors**, not shared: `idle` (0 to the
  deadzone threshold -- always shown on that stick's anchor key, S for
  left / L for right), `tier1` (deadzone to 50% deflection), `tier2`
  (51-100%). Explicitly **no per-direction colors** -- up/down/left/
  right all use the same band colors, per the user's own "I do not
  need for individual directions."
  **Design choice made but not yet re-confirmed with the user**: when
  a push crosses into tier2, the WHOLE active key set (both the near
  and far rings) switches to tier2's color together -- the near ring
  does not stay tier1-colored underneath. This was my interpretation of
  "a color select for 2-50%, then for 51-100%"; worth double-checking
  next time it comes up, since the other reasonable reading (keep tier1
  keys tier1-colored, only newly-added tier2 keys get tier2's color)
  wasn't explicitly ruled out.
- **Deadzone**: one slider (0-50%) shared by both sticks, doubles as
  the idle/tier1 color boundary. Went through its own back-and-forth
  this session: started at 8%, user said it felt "touchy" (too
  sensitive), I initially LOWERED it to 3% (misread which direction
  "touchy" implied), user corrected me ("I needed the deadzone raised
  not lowered"), settled at 15%.
- **16 individually-colorable button groups** (unchanged since first
  built): `l1`,`r1`,`l2`,`r2`,`cross`,`square`,`circle`,`triangle`,
  `dpad_up`,`dpad_down`,`dpad_left`,`dpad_right`,`left_paddle`,
  `right_paddle`,`left_fn`,`right_fn` -- each its own color, each
  independently on/off based on that specific input's own state (L2/R2
  are deliberately all-or-nothing at 100% pull, not tiered like the
  sticks, per explicit request).
- **"Default" button**: resets every color to dark background
  `(10,10,10)` + dark green `(0,100,0)` everywhere, live-preview only
  (doesn't persist until Save is clicked) -- exact values exposed via
  `GET /controller_reactive/defaults` as a single source of truth
  shared between the effect module's own fallback constants and the
  GUI, rather than hardcoding the numbers twice.
- **"Save current settings"** persists everything to
  `controller_reactive.json` -- deliberately separate from the
  keyboard's `presets.json`, since this was explicitly asked NOT to be
  a regular preset.
- **No startup-default logic anywhere** for this feature -- always has
  to be manually enabled via the window's checkbox, per explicit
  request. Confirmed `_load_startup_default()`/the keyboard's own
  startup-default path is completely untouched by any of this.

### The actual key mapping (iterated live, several rounds of adjustment)

- **Left stick** (anchor **S**): up -> W,E then (at 51-100%) also 2,3,4.
  down -> Z,X then Windows,LeftAlt. left -> A then CapsLock. right -> D
  then F.
- **Right stick** (anchor **L**): up -> O,P then 9,0,Minus. down ->
  Comma,Period then AltGr,ContextMenu. left -> K then J. right ->
  Semicolon then Quote.
- **L1** -> F1-F4. **L2** (100% pull only) -> F5-F8. **R1** -> PrtSc,
  Ins, Del. **R2** (100% pull only) -> F9-F12. (This L1/L2/R1/R2 <->
  key-group assignment was swapped once from an earlier arrangement per
  explicit request -- the GROUPS of keys didn't change, just which
  physical input triggers which group.)
- **Face buttons -> numpad**: Cross -> Num2 (changed from an initial
  Num0 per explicit request), Square -> Num4, Circle -> Num6, Triangle
  -> Num8.
- **D-pad**: Up -> Y, Left -> G, Right -> H, Down -> B.
- **Paddles/Fn** (DualSense Edge only): Left paddle -> Left Shift, Left
  Fn -> Left Ctrl, Right paddle -> Right Shift, Right Fn -> Right Ctrl.

### Verification performed

Isolated Python tests of `render()` directly for every band/button
combination (idle/tier1/tier2 per stick, background on/off, multiple
simultaneous buttons) before ever touching the daemon. Playwright tests
of the full GUI page: confirmed all 23 color pickers render, a live
color change actually reaches the running effect (checked via
`/status`'s real `params`, NOT via `/controller_reactive/settings` --
that endpoint only ever reflects the persisted file, a live-vs-
persisted distinction that tripped up one of my own test scripts before
I caught it), Save actually persists to the file, Default resets every
input, and enable/disable correctly toggles the daemon's active effect
both ways. Every key mapping round was also confirmed by the user
directly on the real controller/keyboard after each change.

### Not done / known caveats

- **Bluetooth**: not implemented at all -- `find_controller_path()`
  only matches `bus_type == hid.BusType.USB`. Deliberately deferred;
  the report format differs (report ID `0x31`, ~78 bytes) and hasn't
  been looked at.
- **Hot-reconnect untested**: `Controller` is constructed once at
  daemon startup. Unplugging/replugging the controller while the daemon
  is running has not been tested -- likely needs a daemon restart to
  pick the controller back up, same as any other hardware object in
  this codebase.
- **The tier2-replaces-tier1 color behavior** (see "Final design"
  above) hasn't been explicitly re-confirmed as correct -- ask if it
  ever comes up as feeling wrong.
- **Uncommitted**: everything in this section (`hardware/controller.py`,
  `effects/controller_reactive.py`, `gui/controller_reactive.html`, the
  `gui.py`/`daemon/server.py`/`gui/index.html`/`gui/app.js` edits, and
  `controller_reactive.json`) is NOT committed to git yet -- ask before
  committing/pushing, per established project convention. `git status`
  at time of writing shows exactly those files modified/untracked, main
  otherwise clean and 2 commits ahead of origin (from the previous
  session's lightbar work, already pushed).

## Immediate live state as of writing this file (current, most recent)

- Daemon running, `hardware_connected`/`lightbar_connected`/
  `controller_connected` all true. `current_effect` is
  `controller_reactive` **right now** (the user was actively testing it
  when context ran low) -- this will NOT survive a daemon restart or
  reboot (no startup-default logic for this feature, by design), so
  don't be surprised if a fresh session finds the keyboard back on its
  normal default preset instead.
  `controller_reactive.json` itself, however, IS saved with clean
  defaults (dark background, dark green everywhere, 15% deadzone) as of
  the last explicit Save during testing -- the user's own real
  preferred colors haven't been saved yet, this is just the neutral
  starting point.
- A real GUI window (`gui.py`) is open on the user's actual desktop
  right now -- confirmed via a live process/window check, not assumed.
  Do not kill python/pythonw processes indiscriminately in a future
  session without checking first; this exact mistake very nearly
  happened once already this session (see the single-instance-mutex
  bug investigation earlier in this file for why a blind
  `Stop-Process -Name pythonw` is risky).
- Git: `main`/`stable` at commit `d4db47d` (pushed to origin last
  session), with the whole Controller Reactive feature above sitting
  uncommitted on top. User has not asked for a commit yet this segment.
- User's own words right before this was written: this chat is running
  low on context, asked for this file to be fully updated and for
  cleanup, in preparation for a manual compact.
