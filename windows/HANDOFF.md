# JMA Studio — C# Port (Windows Service + WPF)

**Branch**: `csharp-port` (created from `main` at a clean tree, no
uncommitted changes carried over). `main` stays untouched and fully
usable on its own — the Python daemon/GUI live there unmodified. All
C# work lives under `windows/` on this branch only.

**Read this file first** if you're picking this up cold. It's kept
current at every phase boundary: status, what's verified on real
hardware vs. still assumed, key decisions and why, and how to build/
run/test whatever exists so far.

## The goal

Full port of the hardware/daemon side of JMA Studio (per-key keyboard
RGB + rear lightbar) to C#, no Python at runtime. Not a hybrid —
everything the Python `daemon/` + `hardware/` packages do (HID
keyboard protocol, WMI lightbar protocol, effects, presets, reactive
features) gets reimplemented in C#. The existing Python codebase
(`daemon/`, `hardware/`, `effects/`, `gui/`, `gui.py`) is the reference
implementation / UX spec, not something the C# side calls into or
depends on at runtime.

This came out of a planning conversation that already settled the big
architectural questions — see "Settled decisions" below. Don't
re-litigate those; they're final unless the user reopens them.

## Current status

**Phases 1-4 (branch/scaffold + hardware protocol + effects + presets)
— DONE.** Real hardware control confirmed live for both keyboard and
lightbar, all 21 real Python effects ported and confirmed rendering
correctly, and the real on-disk preset/config data migrated into a new
atomic-write C# store with keyboard AND lightbar presets both
re-applied live from the migrated data. Not yet started: controller-
reactive support, service, GUI, installer (see below and "Suggested
phasing").

Phase progress (updated as each completes — see "Suggested phasing"
below for the full list):

- [x] Branch created, `main` untouched
- [x] `windows/` .NET solution + project scaffold (`JmaStudio.Hardware`
      class library + `JmaStudio.HardwareTest` console app, net8.0-windows)
- [x] WMI lightbar port + empirical COM/API-choice verification — all
      3 flagged risks resolved, confirmed on real hardware (twice)
- [x] HID keyboard port + empirical hardware verification — both the
      static-color command sequence AND the full 512-byte per-key
      frame buffer confirmed on real hardware
- [x] Effects ported behind `IEffect` — all 21, confirmed live (see
      "Phase 3: effects" below)
- [x] Preset persistence (atomic writes) + migration of real on-disk
      data — see "Phase 4: presets" below
- [ ] Controller-reactive support (`hardware/controller.py` +
      `effects/controller_reactive.py`) — see placement decision below.
      **Resolved 2026-09-09: build this in Phase 5**, not folded into
      Phase 4 (the user picked this explicitly when asked) — including
      its settings-file (`controller_reactive.json`) migration, which
      moves with it to Phase 5 rather than the now-completed Phase 4,
      since migrating settings for a feature that doesn't exist in C#
      yet wouldn't have been useful.
- [ ] Windows Service wrapper
- [ ] WPF GUI
- [ ] Installer

**Scope reversal, 2026-09-09**: an earlier version of this document
marked `effects/controller_reactive.py` + `hardware/controller.py` (the
PS5 DualSense-driven keyboard effect) out of scope, since the original
port request was "keyboard per-key RGB + rear lightbar." **The user
has since explicitly reversed this — the full controller-reactive
feature set, including its presets, is now in scope**, with placement
left to Claude's judgment. Decided placement, to avoid re-deriving this
later:
- `Controller.cs` (raw DualSense USB HID reading, port of
  `hardware/controller.py`) → **`JmaStudio.Hardware`**, alongside
  `Keyboard.cs`/`Lightbar.cs` — it's the same kind of thing, a third
  hardware protocol class, not fundamentally different in shape.
- The reactive effect itself (port of `effects/controller_reactive.py`)
  → **`JmaStudio.Effects`**, as one more `IEffect`. Needs one addition
  to the shared `EffectContext`: a nullable `ControllerState` field,
  the same pattern `KeyState` already uses for `typing_reactive` — a
  plain per-frame snapshot the render loop injects, not something the
  effect reaches out and fetches itself.
- The "full override" enable/disable semantics and its own separate
  settings store (`controller_reactive.json`, deliberately NOT part of
  the regular preset system on the Python side — see `daemon/server.py`
  on `main`'s `_pre_controller_reactive_state` stash-and-restore logic)
  → belongs in the future **Windows Service** (Phase 5), not in
  `JmaStudio.Effects` itself, since that's exactly where the equivalent
  logic lives in the Python daemon (`daemon/server.py`, not `effects/`).
- **When to build it: Phase 5, confirmed** (see above) — build
  `Controller.cs` + the effect alongside the enable/disable plumbing it
  depends on, not earlier.

**Resolved 2026-09-09**: `Lightbar.SetMode()` (the 7 firmware-native
animated modes) is now confirmed on real hardware too — the migrated
"Rainbow" lightbar preset (a `mode` preset) was applied live via
`Lightbar.ApplyState()` and confirmed by the user to show the real
firmware rainbow animation. No remaining untested `Lightbar` code path.

## Settled decisions (do not re-ask about these)

These were decided in the planning conversation this port is based on.
If you're a fresh session picking this up, treat all of the following
as final:

1. **Full C# port, no hybrid.** Both the WMI lightbar protocol and the
   HID keyboard protocol move to C#. No Python process at runtime.
2. **Windows Service, not Scheduled Task.** The hardware-owning process
   runs as a real Windows Service (Automatic start, `LocalSystem`).
   This satisfies the Administrator-token requirement every WMI `Set*`
   call needs, silently, and is a more natural fit than the Python
   version's Scheduled-Task-with-`RunLevel HighestAvailable` workaround.
3. **Effects: strongly-typed, not reflection-based.** The Python side's
   plugin discovery (any `effects/*.py` module with a `NAME` + `render()`
   becomes pluggable, untyped/sometimes-nested `params`) is intentionally
   NOT replicated. Use a common `IEffect` interface (`Render(t, cellCount,
   params) -> per-cell colors`) with one strongly-typed params class per
   effect kind (discriminated-union style). Rationale: we're not planning
   to hand-author new effect modules as loose files in C#, so the
   dynamism isn't worth losing compile-time safety for.
4. **Full feature parity required**, not just static/pattern presets:
   typing-reactive effects that wrap other effects (e.g. today's "Red
   Chase" preset = `typing_reactive` wrapping `custom_keys`), per-key
   custom painting (`custom_keys`), the lightbar's separate preset
   system (`lightbar` + `reactive` shape), and the keyboard-press-driven
   lightbar flash-on-keypress reactive feature with its own config store.
5. **Real data migration required**, not a fresh start. `presets.json`,
   `lightbar_presets.json`, and `lightbar_reactive.json` as they exist
   today on the user's machine must be imported, preserving the same
   permissive back-compat parsing the Python `apply_lightbar_preset`/
   `Lightbar.apply_state()` already use (e.g. `"BLUE"` in the real
   `lightbar_presets.json` has no `"type"` key at all — treat that as
   `"static"`, don't drop it).
6. **Atomic writes, no database.** Fix the Python side's "crash
   mid-`json.dump()` corrupts the whole file" risk via temp-file +
   replace, for whatever JSON-equivalent store replaces the three
   preset/config files. Explicitly decided against SQLite/LiteDB — not
   worth the complexity for a handful of named presets.
7. **HTTP API: modernize, don't preserve `{"ok": ..., "error": ...}`.**
   The old convention (always-200, ok/error in the body) existed only
   because the old JS frontend was hand-rolled. The WPF client is new,
   so use real HTTP status codes / `ProblemDetails`-style errors
   instead. ASP.NET Core minimal APIs hosted inside the Windows Service
   is the default choice for the local HTTP surface unless testing
   surfaces a reason not to.
8. **COM/WMI API choice must be verified empirically, not assumed** —
   see "Highest-risk area" below.
9. **Boot-time state via disk persistence, not hardware read-back.**
   The Python lightbar's cached in-memory state resets to defaults
   (black, no mode) on every daemon restart, since nothing re-reads
   real hardware state at startup. Since the C# service starts at boot
   (before login), an empty cache means dark lights before login —
   unacceptable. Fix: persist last-commanded state (keyboard AND
   lightbar) to disk on every change, reload and re-apply at service
   startup. Explicitly NOT pursuing real hardware read-back via
   `GetGamingRgbKb`/`GetGamingLED` (documented, never called by the
   Python side either) — disk persistence is simpler and solves the
   same problem.
10. **`AcerLightingService` scope is final — do not investigate
    further and do not ask about it again.** Target `AcerLightingService`
    only. No investigation into the hidden `OpenRGB.exe`/
    `predatorservice` actor (see `LIGHTBAR_REVERSE_ENGINEERING.md` §6 on
    `main` for what that even is) is needed for the port. Already tested
    live: with `AcerLightingService` stopped, opening PredatorSense
    visibly cannot regain control of the lighting. Acer hasn't updated
    PredatorSense in a long time and likely won't again. Installer sets
    `AcerLightingService`'s startup type to **Disabled** (not just
    `Stop-Service`) so it stays off across reboots; the service also
    does a cheap stop-if-somehow-running check at its own startup, same
    insurance `start_all.ps1` already has on the Python side.
11. **GUI: WPF**, replacing `gui.py`/pywebview entirely. Runs as a
    normal per-user app (Startup folder or per-user login task) —
    separate from the Windows Service, since it needs a desktop session
    and the service doesn't. Existing `gui/index.html` + `gui/app.js` +
    `gui/lightbar.html` (on `main`) are the **UX reference** to
    replicate faithfully (save-preset modal flow, toast confirmations,
    keyboard vs. lightbar as separate views) — not a source to port
    code from, and not license to ship a thinner experience just
    because the tech changed. New in the WPF version, beyond parity:
    - **Create Preset** flow: pick a preset kind (Static, Breathing,
      Wave/Rainbow, Ripple to start), adjust parameters, name it, save.
    - A **"Diagnostics" button** on the main window — out of the way,
      not a primary action, just an entry point — that opens a separate
      **Diagnostics window**. Design intent (user, 2026-09-09): make
      this a real, polished diagnostics dashboard, not just a dumping
      ground for the 3 emergency buttons — "add any other cool
      diagnostics things you can think of... try to make it feel fun."
      Full proposed layout below; this is a design proposal to refine
      when Phase 6 actually starts, not locked-in UI spec.

      **Emergency-action row** (the 3 buttons that started this
      conversation) — each needs the small **UAC shield icon overlay**
      (the standard Windows convention for "this button will trigger an
      elevation prompt") since all three genuinely require it:
      - **Re-assert dominance**: re-runs the stop+disable-
        `AcerLightingService` logic and re-pushes current lighting
        state — insurance against a future Windows/driver update
        silently re-enabling the service. (This is the button
        originally requested as a main-window button; moved here per
        user correction 2026-09-09.)
      - **"Switch to Python Version"**: stops+disables the C# Windows
        Service, removes the WPF GUI from autostart, kills any running
        C# GUI process, starts the Python version (daemon + tray + gui,
        i.e. what `start_all.ps1` does on `main`), re-registers ITS
        autostart (the "JMA Studio Autostart" Scheduled Task).
      - **"Switch to C# Version"**: the exact inverse.
      - **The installer ships the C# version ONLY** (confirmed
        2026-09-09) — it does not bundle or install the Python version.
        So "Switch to Python Version" only makes sense, and is only
        useful, on a machine that separately happens to already have
        the Python version present (true for the user's own dev machine
        during this transition). **Both switch buttons must be grayed
        out (disabled, not hidden) whenever the Python version isn't
        detected on the machine** — detection needs a real check at
        window-open time (candidates: does the Python repo/install path
        exist, does the "JMA Studio Autostart" Scheduled Task exist,
        is `presets.json`/`daemon/server.py` findable at a known path —
        pick the most reliable one when this is actually built, don't
        just check one weak signal). Beyond that base gate, only the
        button for the *inactive* stack should be enabled at a time
        (if C# is currently running, "Switch to Python" is the
        actionable one; vice versa) — showing both grayed-out with a
        tooltip explaining why is better than hiding them outright.
      - Implementation note for Phase 5/6: the stop/start/register/
        unregister logic for BOTH stacks needs to be real, callable code
        (not just installer-time script), since these buttons invoke it
        live, on demand, from a running GUI — factor it so the installer
        and these buttons share the same implementation rather than
        duplicating service/task registration logic in two places.

      **Proposed additional dashboard content** (my own suggestions per
      the user's "think of other things too" ask — not yet approved
      one-by-one, treat as a menu to pick from / refine, not a
      commitment):
      - *Live hardware status tiles*, one per subsystem (keyboard,
        lightbar, controller), each with a colored status dot (green/
        yellow/red), connection state, and the relevant identifying
        info (VID/PID + device path for keyboard; WMI class found +
        current per-zone color swatches + brightness + active mode for
        lightbar; connection health + a live stick-position/button
        readout for the controller — directly exercises the same
        "is this actually alive, not just was-it-constructed" honesty
        this project already learned the hard way once on the Python
        side with the DualSense sleep/wake reconnect bug, see `main`'s
        `HANDOFF.md`).
      - *Live mini preview*: a small on-screen replica of the keyboard's
        current per-cell colors and the lightbar's 3 zones, updating in
        real time — lets you confirm what's actually being rendered
        without looking away from the screen at the physical hardware.
      - *Render/perf stats*: frames rendered vs. frames actually written
        (mirrors the Python daemon's existing `/status` render_stats —
        see `daemon/server.py` on `main`), current effect name + a
        read-only view of its live params, HID write latency (min/avg/
        max), WMI commit latency for the lightbar.
      - *Service/process health panel*: C# Windows Service state +
        uptime, `AcerLightingService` state (should always read
        Stopped/Disabled — surface it going green→red if it's ever
        found running, since that's the exact failure mode the
        dominance-reassert button exists to fix), and a check for
        whether the hidden `OpenRGB.exe`/`predatorservice` process
        (see `LIGHTBAR_REVERSE_ENGINEERING.md` §6 on `main`) is running
        — an early warning that something might be fighting for control
        of the lighting again.
      - *Self-test buttons* (actual interactive tests, not just passive
        readouts): "Test keyboard" (brief built-in test pattern), "Test
        lightbar" (cycle each zone R/G/B briefly), "Test controller"
        (a live input viewer — press any button/move a stick and watch
        it register on screen in real time; satisfying as well as
        useful, fits the "more fun" ask directly), "Re-scan hardware"
        (force re-enumeration without restarting the whole service —
        also gives the hot-plug/reconnect story a real UI hook, which
        the Python side never got around to for the controller).
      - *Logs panel*: a live tail of the service's log file right in
        the window, an "Open logs folder" button, and a "Copy
        diagnostics report to clipboard" button that bundles hardware
        status + version info + recent log lines into one paste-able
        block — genuinely useful for any future bug report (to the user
        themself, or to a future Claude session).
12. **Installer**: prompts for install location (default
    `C:\Program Files\JMA Studio` — 64-bit, not `(x86)`), self-contained
    single-file `win-x64` publish (no separate .NET runtime install),
    explicit/non-silent notice before disabling `AcerLightingService`,
    registers the Windows Service (Automatic) + the GUI's per-user
    autostart, migrates existing preset/config JSON into the new system
    during install or first run.

## Highest-risk area: COM/WMI API choice — RESOLVED (2026-09-09)

The Python side's `hardware/lightbar.py` (and the full story in
`LIGHTBAR_REVERSE_ENGINEERING.md` on `main`) surfaced three real
landmines against the `AcerGamingFunction` WMI class. All three were
independently re-verified in .NET rather than assumed to carry over,
using `JmaStudio.HardwareTest` run directly against the user's real
PH16-71 (see "How this was actually verified" below for the exact
commands and what was observed on the physical hardware each time).

1. **Which .NET WMI API actually works — `System.Management` does.**
   Confirmed: `ManagementObjectSearcher` + `ManagementObject.
   InvokeMethod` successfully calls `AcerGamingFunction`'s `Set*`
   methods and produces real light changes. `Microsoft.Management.
   Infrastructure` was never tried — once the first candidate worked,
   there was no reason to test the fallback. One genuine surprise:
   **even read-only enumeration required an elevated process** here —
   a non-elevated run got `ManagementException: "Access denied"` just
   from `ManagementObjectSearcher.Get()`, whereas the Python side's
   `find_lightbar_instance()` docstring claims enumeration does NOT
   need elevation via `win32com.client`. Whether that's a genuine
   Python-vs-.NET WMI-client difference or something specific to this
   environment wasn't investigated further — it doesn't matter in
   practice since the Windows Service will always run elevated anyway,
   but don't be surprised if a non-elevated diagnostic run fails where
   the Python equivalent wouldn't have.
2. **`SetGamingRgbKb`'s packed-`u64` marshaling — a plain native
   `ulong` works, no `VT_BSTR` workaround needed.** Confirmed live:
   zone 1 turned red via `Lightbar.SetZone()` (which sends the packed
   value as a real C# `ulong` through `ManagementBaseObject`'s
   indexer), observed by the user, repeated twice for certainty. The
   `VT_BSTR`-decimal-string shape Frida observed on the wire from
   Acer's own software (`OpenRGB.exe`) turned out to be an artifact of
   *how that specific application's COM automation happened to send
   it*, not a requirement the receiving WMI provider actually enforces.
   `Lightbar.cs` has been simplified back down to the single
   native-`ulong` code path; the decimal-string fallback code
   (`CallU64AsDecimalString`, the `lightbar-marshal-test` diagnostic
   command) was written, never needed, and has been deleted.
3. **Thread affinity — a real non-issue in .NET.** Confirmed via
   `LightbarDiagnostics.ProbeThreadAffinityAsync`: one
   `ManagementObject` instance resolved on the main thread, then
   invoked from 8 concurrent `Task.Run` threadpool threads
   simultaneously — all 8 succeeded, zero exceptions, run twice. No
   .NET equivalent of Python's `threading.local()` +
   `pythoncom.CoInitialize()`-per-thread dance is needed. `Lightbar.cs`
   still resolves a fresh `ManagementObject` per call rather than
   caching one (matching the Python side's stated rationale that
   resolution is cheap) — that's a deliberate simplicity choice, not
   evidence caching would be unsafe; revisit only if profiling in a
   later phase shows the per-call WMI lookup actually costs something.

**Net effect**: `Lightbar.cs` as it exists right now is the *real*
implementation, not a draft — no known open questions remain about its
approach to WMI. The `LightbarDiagnostics` class (raw instance
resolution + the thread-affinity probe) was kept as a standing
diagnostic tool for the `HardwareTest` console app, not deleted, since
it's generically useful if something ever needs re-verifying (e.g.
after a Windows update).

## How this was actually verified (real hardware, this session)

Every claim above was checked live against the user's actual PH16-71,
using the `JmaStudio.HardwareTest` console app built in this phase —
not inferred from documentation or Python behavior. For the exact
sequence: `keyboard-find` (HidSharp locates the real `04F2:0117`/
usage-page-`0xFF02` interface) → `keyboard-static 0 255 0 200` (user
confirmed: whole keyboard turned solid green) → `keyboard-frame` (user
confirmed: roughly half the keys turned red, the other half blue —
proves the full 512-byte per-key interrupt-OUT path, not just the
4-command solid-color path) → `lightbar-find` elevated (confirmed
`AcerGamingFunction` resolves via `System.Management` once elevated) →
a batched elevated run of `lightbar-static 1 255 0 0 100` (zone 1 real
API call) + `lightbar-static 1 0 0 0 100` (off) + `lightbar-thread-test
8`, **run twice**, both times with the user visually confirming zone 1
actually turned red then off on the physical lightbar. See "Verified
vs. unverified" below for the one remaining gap (`SetMode`/animated
modes untested).

**Operational note for next time elevation is needed**: running a
console app elevated from a non-elevated session requires a
`Start-Process -Verb RunAs` wrapper around a `.ps1` script (direct
`-Verb RunAs` on `dotnet run` also works but loses stdout — redirect
inside the elevated script to a log file, then read that file back
after `-Wait` returns). **Getting every path in that wrapper exactly
right matters** — two separate attempts this session silently failed
with the elevated window producing no output at all, because a
temp-file path was missing its session-ID subfolder segment (both in
the outer `Start-Process -ArgumentList` and, separately, inside the
`.ps1` script's own `$log` variable). The failure mode is confusing:
`Start-Process ... -Wait` returns with no thrown error either way, so
"ran without error but the log file doesn't exist" almost always means
a wrong path somewhere in the wrapper, NOT a UAC denial (a real UAC
denial DOES throw "The operation was canceled by the user" from
`Start-Process` itself) — check `Test-Path` on every absolute path in
the wrapper before consuming another one of the user's UAC clicks on a
retry.

## Verified vs. unverified

**Verified on real hardware (C# side, this session):**
- Keyboard: `Keyboard.FindLightingDevice()` (HidSharp locates the
  correct vendor-specific HID interface), `SetStaticColor()` (4-command
  feature-report sequence), `SendFrame()` (full 512-byte per-key
  interrupt-OUT frame, all 8 packets). Both keyboard code paths that
  exist in `Keyboard.cs` have now been exercised live.
- Lightbar: `Lightbar.SetZone()` end to end (the real production code
  path, not just a raw diagnostic) — `SetGamingLED` priming,
  `SetGamingKBBacklight` brightness commit, and `SetGamingRgbKb`'s
  packed-`ulong` zone-color call, all via `System.Management`, all
  requiring and confirmed working under an elevated process. Thread
  safety of sharing one WMI instance across threads also confirmed
  (see above) even though `Lightbar.cs` doesn't currently rely on that.

**NOT yet verified on the C# side:**
- `Lightbar.SetMode()` — the 7 firmware-native animated modes
  (breathing/neon/rainbow/wave/ripple/scanner/strobe). Same
  `CallArray`/`SetGamingKBBacklight` plumbing already proven for the
  brightness-commit call, and the exact byte layout is the same
  known-correct one from the Python side, but this specific code path
  (mode byte ≠ 0, color embedded in the 16-byte buffer instead of via
  `SetGamingRgbKb`) hasn't itself been run against real hardware yet.
  Low-risk given everything else checks out, but test it before Phase
  3's effects code depends on it.
- `Lightbar.GetState()`/`ApplyState()`, `FlashZones()`,
  `SetBrightness()` — straightforward code, not independently
  hardware-tested yet, but built from the same confirmed-working
  primitives (`Commit()`, `SendRgbKb()`) as `SetZone()`/`SetAll()`.
- ~~Everything about the keyboard's `NUM_CELLS`/cell-index-to-physical-key
  mapping~~ — **done in Phase 3**, see below (`Layout.cs`, using the
  user's real `keymap.json`).

**Verified on real hardware (Python side only, for reference — see
`main`):** the underlying byte-level protocol facts themselves
(command opcodes, checksum formula, WMI method IDs, the packed-`u64`
formula, the 3x-repeat commit cadence) — not re-derived in this port,
only re-verified that .NET's transports carry them correctly. See
`LIGHTBAR_REVERSE_ENGINEERING.md` and `LIGHTBAR_SUMMARY.md` on `main`
for that full story.

## Phase 3: effects — DONE (2026-09-09)

All 21 real effects from `effects/*.py` on `main` ported into
`JmaStudio.Effects` and confirmed rendering correctly on the real
keyboard via `JmaStudio.HardwareTest`'s new `effect-demo`/
`effect-demo-all` commands. That's every module with a `NAME`/`render()`
except `controller_reactive.py`, which is now back in scope per the
"Scope reversal" note above but not yet ported as of this writing —
`mask.py` and `probe.py` (keymap-discovery diagnostic effects, not
meant for end use) were included too, since "every existing effect"
was explicit.

**Architecture**: a common non-generic `IEffect` interface (`Name`,
`DefaultParams`, `Render(t, numCells, EffectParams, EffectContext)`)
so effects can be stored and dispatched by name in one registry despite
each expecting its own strongly-typed params record — see
`IEffect.cs`. Concrete effects derive from `Effect<TParams>` (not
`IEffect` directly), which centralizes the one unavoidable
`parameters as TParams ?? TypedDefaultParams` cast in a single base
class rather than repeating it in all 21 effects. Every effect's params
is its own `sealed record : EffectParams` (see `EffectParams.cs`) with
`init` properties defaulting to the exact values transcribed from each
Python module's own `DEFAULT_*` constants — this also sets these up
well for Phase 4's `System.Text.Json` preset deserialization, since
records with `init` properties bind directly by property name.

**Files**:
- `Layout.cs` — port of `effects/layout.py`, including the full
  hand-authored `KEY_POSITIONS` table (transcribed verbatim — physically
  measured data, not something to "clean up" or re-round) and the
  keymap.json loader (`CellPositions`/`NameToIndex`).
- `PseudoRandom.cs` — port of `effects/noise.py`'s `pseudo_random01`.
- `ColorMath.cs` — HSV→RGB (port of Python's `colorsys.hsv_to_rgb`),
  channel-wise lerp/scale (matching Python's truncating, not rounding,
  `int()` casts), and a `PositiveMod` helper (C#'s `%` is a remainder
  operator that keeps the dividend's sign; Python's is a true modulo —
  several effects' wrap-around math needs the true-modulo behavior).
- `EffectParams.cs` — all 21 params records.
- `IEffect.cs` — the interface + `Effect<TParams>` base class + `EffectContext`.
- `EffectRegistry.cs` — the C# analogue of `daemon/server.py`'s
  `_load_effects()`. Unlike Python's reflection-based module scan, this
  is a fixed, explicit list of `new SomeEffect(...)` calls — per settled
  decision #3. Effect names match each Python module's `NAME` constant
  exactly (important: Phase 4's preset migration depends on this, since
  `presets.json` selects an effect by this exact string). Position-aware
  effects load `keymap.json` ONCE here (constructor injection), mirroring
  the Python modules' "load once at import time" behavior — re-create an
  `EffectRegistry` (not just re-render) after `keymap.json` changes.
- `Effects/BasicEffects.cs` — the 12 effects needing no spatial data:
  `static`, `mask`, `probe`, `rainbow`, `puke`, `spectrum_cycle`,
  `breathing`, `pulse`, `custom_keys`, `fire`, `confetti`, `starlight`.
- `Effects/GamingZoneEffect.cs` — needs only the {name: index} map.
- `Effects/PositionalEffects.cs` — the 6 effects needing full (row, col)
  positions: `color_wipe`, `comet`, `scanner`, `aurora`, `ripple`, `rain`.
- `Effects/GradientEffect.cs` — legacy 2-zone + multi-zone modes, ported
  in full including the override/custom-color legacy patches.
- `Effects/TypingReactiveEffect.cs` — the hardest one: reads
  `EffectContext.KeyState` for the in-place flash + chasing bolts, AND
  can recursively delegate to another registered effect by name
  (`BaseEffectName`) for its background instead of a flat color —
  mirrors the real `main`-branch preset "Red Chase" (`typing_reactive`
  wrapping `custom_keys`). One deliberate behavior difference from
  Python, documented in the file's header comment: if `BaseEffectName`
  names a real effect but `BaseParams` is null or the wrong concrete
  type for it, this falls back to that effect's own defaults (via
  `DefaultParams`) rather than Python's "any dict works, unrecognized
  keys ignored" duck typing. Shouldn't matter in practice.

**Verified on real hardware**: all 21 effects run without exceptions
and were watched live via `effect-demo-all 2.5` (cycles every
registered effect for ~2.5s each) — user confirmed the whole cycle
"looked right," including motion effects (rainbow/comet/scanner/rain
sweeping) and ambient ones (breathing/pulse/aurora). Separately,
`typing_reactive`'s recursive `base_effect` dispatch was specifically
verified with `effect-demo-typing-with-base gradient 7` — user
confirmed seeing the real gradient color-split background (not a flat
fallback color) with a bright bolt pulsing outward roughly every 1.2s
from a synthetic repeating "keypress," proving the delegation path
actually renders the named effect's live output rather than silently
falling back.

**NOT independently re-verified per-effect beyond the visual group
demo**: nobody did a byte-for-byte/pixel-for-pixel diff against the
Python renderer's output for any single effect — verification here was
"does it look like it's supposed to, live, on the real keyboard," same
verification bar as Phase 2's hardware protocol proof, not a unit-test-
level numeric comparison. If a future bug report says some specific
effect's math is subtly off from the Python original, don't assume
Phase 3 already ruled that out.

## Phase 4: presets — DONE (2026-09-09)

New `JmaStudio.Presets` project: atomic JSON persistence for keyboard
presets, lightbar presets, the lightbar reactive config, and app config
(default-preset pointers), plus a one-time migrator that reads the real
Python-side files (`presets.json`, `lightbar_presets.json`,
`lightbar_reactive.json`, `config.json` on `main`) and writes them into
the new store. Confirmed live on real hardware for both keyboard and
lightbar presets (see "Verified" below) — this is real user data, not
synthetic test fixtures: 7 keyboard presets, 3 lightbar presets, the
live reactive config, and both default-preset pointers, all migrated
from this machine's actual files.

**Format decision**: the new store is **not** byte-compatible with
Python's JSON shape — it's a distinct, C#-native, strongly-typed format
(PascalCase properties, a `"$effect"` polymorphic discriminator on
`EffectParams`, `RgbColor` as `{"R":..,"G":..,"B":..}` objects rather
than `[r,g,b]` arrays). Migration is a one-time, one-directional
transform (old Python shape → new C# shape), not an ongoing
compatibility layer — once migrated, the C# side never reads the old
files again. This was a deliberate choice: preserving Python's untyped
dict shape byte-for-byte would have meant compromising the strongly-
typed `EffectParams` design (settled decision #3) just to match a
format nothing will read anymore once this port is finished.

**Files** (`JmaStudio.Presets`):
- `AtomicJsonFile.cs` — the fix for the Python side's "crash mid-
  `json.dump()` corrupts the whole file" risk (settled decision #6):
  write to `<path>.tmp`, then `File.Move(..., overwrite: true)` --
  atomic on the same NTFS volume.
- `PresetJsonOptions.cs` — shared `JsonSerializerOptions`:
  `WriteIndented` (stays hand-readable, matching the spirit of the
  Python files), `IncludeFields` (needed because
  `TypingReactiveParams.BoltDirections` is
  `IReadOnlyList<(int Row, int Col)>`, and `ValueTuple` exposes its
  data as public fields, not properties, which System.Text.Json
  otherwise silently ignores), and `JsonStringEnumConverter` (so
  `LightbarMode`/`BoltShape`/`BoltStyle` save as readable strings like
  `"Radial"`, not raw integers).
- `Models.cs` — `KeyboardPreset` (effect name + `EffectParams`),
  `LightbarPreset` (a `Hardware.LightbarState` + nullable
  `LightbarReactiveSettings` -- nullable because real presets like
  "BLUE" predate reactive settings being bundled in at all),
  `LightbarReactiveSettings` (the per-preset snapshot shape, no
  `zone_boundaries` -- that's geometry calibration, not a preset
  concern, matching Python's own `_reactive_settings_snapshot()`),
  `LightbarReactiveConfig` (the live, non-preset config, WITH
  `zone_boundaries`), `AppConfig` (the two default-preset pointers).
- `JsonStore.cs` — a generic `JsonStore<T>` (one JSON file, atomic
  load/save) and `PresetStore` (bundles the 4 real ones: new file names
  `keyboard-presets.json`, `lightbar-presets.json`,
  `lightbar-reactive-config.json`, `app-config.json`, distinct from the
  Python names since this is a new format, not a drop-in replacement).
- `PythonPresetMigrator.cs` — the one-time bridge. A `Parse(effectName,
  JsonElement)` dispatcher covers all 21 effects' Python-side
  snake_case field names (not just the 4 kinds the user's current real
  presets happen to use -- `typing_reactive`, `custom_keys`, `gradient`,
  `puke` -- covering all 21 means a future preset using any other
  effect migrates correctly too, without this file needing revisiting).
  `TypingReactiveParams`' `base_effect`/`base_params` are handled
  recursively through the same dispatcher. Lightbar preset migration
  preserves the exact same back-compat permissiveness
  `Lightbar.apply_state()` already has: no `"type": "mode"` key means
  static (real presets like "BLUE" have no `"type"` key at all).
  `MigrateAll(pythonRepoRoot, store)` does all 4 files in one call and
  is idempotent (safe to re-run; always fully overwrites from current
  Python-side source data).

**Verified on real hardware**: migrated all 4 real files from this
machine (`dotnet run ... -- migrate-presets`) — correct counts (7
keyboard presets, 3 lightbar presets) and correct default-preset
pointers ("Red Chase", "Rainbow"). Applied 3 different migrated
keyboard presets live via `preset-demo` -- `ZONES` (multi-zone
gradient, 4 colors + boundaries: user confirmed) and `gradient_only`
(legacy 2-zone gradient with per-key overrides AND a custom per-key
color: user confirmed) exercise `GradientEffect` paths Phase 3's own
verification never touched; `Red Chase` (via the standalone
`effect-demo-from-status` tool built for the earlier interactive
testing request, not `preset-demo`, but the same underlying migrated
params) re-confirms `typing_reactive` wrapping `custom_keys`. Applied 2
migrated lightbar presets live via `lightbar-preset-demo` and
`Lightbar.ApplyState()` (**requires Administrator**) -- `BLUE` (static
per-zone, no `"type"` key -- the back-compat path) and `Rainbow`
(animated mode) both confirmed by the user on the physical lightbar,
closing out the one remaining untested `Lightbar` code path
(`SetMode()`) from Phase 2.

**NOT independently verified**: `rainbow_puke`, `rainbow_radial_chase`,
`white_on_white`, and `gradient_white_chase` (the other 4 of the 7 real
keyboard presets) were migrated but not individually re-applied live --
low risk, since they exercise the same `puke`/`typing_reactive`/
`gradient` code paths already proven via other presets and Phase 3's
group demo, but not literally clicked through one by one. Also not
tested: the lightbar reactive config and app-config migration are
structurally verified (correct field values land in the new JSON files)
but not exercised through any live "apply the default preset at
startup" or "reactive flash on keypress" behavior, since neither of
those behaviors has a Windows Service to run inside of yet (Phase 5).

## Key technical decisions for the scaffold itself

- **Target framework: `net8.0-windows`** across every project in the
  solution (including the ones that don't need WPF yet) — chosen for
  consistency since the GUI phase will need it anyway, and
  `System.Management`/COM interop are Windows-only regardless.
- **HID library: [HidSharp](https://www.nuget.org/packages/HidSharp)**
  (pure managed, no native DLL to load) rather than trying to P/Invoke
  `hidapi.dll` the way Python's `hid` package does. This sidesteps the
  entire "`ctypes` doesn't search the venv's `Scripts/` dir for bare
  DLL loads on Windows" class of problem the Python side hit — there's
  no native DLL dependency to place/find at all. **Confirmed working**:
  it locates the vendor-specific `0xFF02` usage-page interface (by
  parsing each candidate device's report descriptor, since HidSharp
  doesn't expose usage page as a plain enumerate() field the way
  Python's hidapi wrapper does — see `Keyboard.FindLightingDevice()`),
  and both `SetFeature` (feature reports) and `HidStream.Write` (raw
  interrupt-OUT packets) work correctly against real hardware. No
  fallback library needed.
- **WMI library: `System.Management`** — confirmed working, see
  "Highest-risk area" above. `Microsoft.Management.Infrastructure` was
  never needed.
- **Solution layout** (`windows/`):
  ```
  windows/
    HANDOFF.md
    JmaStudio.sln
    data/                       -- migrated preset/config JSON -- real user data, tracked in git
                                   (not gitignored), same as presets.json etc. are on `main`
    src/
      JmaStudio.Hardware/       -- Keyboard.cs, Lightbar.cs (protocol only, no HTTP/service/UI)
      JmaStudio.Effects/        -- all 21 ported effects, IEffect/EffectRegistry (Phase 3)
      JmaStudio.Presets/        -- atomic preset/config store + Python migration (Phase 4)
      JmaStudio.HardwareTest/   -- bare console app, Phase 2/3/4's real-hardware proof
      (later phases add: JmaStudio.Service, JmaStudio.Gui, JmaStudio.Installer)
  ```

## How to build / run / test

- Prereqs: this machine has .NET 8 SDK installed (`dotnet --version` ->
  `8.0.425` as of this writing). NuGet had NO package sources
  configured at all the first time this was set up on this machine —
  `dotnet restore` failed with `NU1100` until `dotnet nuget add source
  https://api.nuget.org/v3/index.json -n nuget.org` was run once. If a
  fresh machine hits the same `NU1100` error, that's why.
- Build: `dotnet build windows/JmaStudio.sln` (from the repo root, or
  `cd windows && dotnet build`).
- Run the hardware test console app (from `windows/`):
  `dotnet run --project src/JmaStudio.HardwareTest -- <command> [args]`
  — run `dotnet run --project src/JmaStudio.HardwareTest` with no
  args to print the full command list. Keyboard commands need no
  elevation; every `lightbar-*` command requires Administrator (run
  from an elevated terminal, or see the elevation-wrapper-script note
  under "How this was actually verified" above if scripting it from a
  non-elevated automation context).
- Solution currently has 4 projects: `JmaStudio.Hardware` (protocol —
  `Keyboard.cs`, `Lightbar.cs`, `LightbarDiagnostics.cs`),
  `JmaStudio.Effects` (all 21 effects + `IEffect`/`EffectRegistry`),
  `JmaStudio.Presets` (atomic store + Python migration), and
  `JmaStudio.HardwareTest` (the console app). No tests project yet —
  "testing" so far means the console app against real hardware, by
  design (see Phase 2/3/4's stated purpose).
- Effect commands (from `windows/`, no elevation needed):
  `dotnet run --project src/JmaStudio.HardwareTest -- effects-list`,
  `... -- effect-demo <name> [seconds] [--synthetic-key idx]`,
  `... -- effect-demo-all [secondsPerEffect]`,
  `... -- effect-demo-typing-with-base <baseEffectName> [seconds]`,
  `... -- effect-demo-from-status <statusJsonPath> [seconds]` (ad hoc,
  loads typing_reactive/custom_keys from a saved `GET /status` blob —
  see `AdHocPresetLoader.cs`, superseded for anything else by the real
  preset commands below), `... -- effect-live <statusJsonPath>
  [maxSeconds]` (same, but reacts to REAL keystrokes via a
  `GlobalKeyboardHook`/`WindowsKeyMap` global low-level keyboard hook --
  built for interactive testing, no elevation needed; **stop the Python
  daemon first** if it's running, or both processes will fight over the
  same keyboard on every real keystroke and produce exactly the
  flickering/color-bleeding this was hit and fixed once already this
  session). All default to the real repo-root `keymap.json` (override
  with `--keymap <path>` if running from somewhere else).
- Preset commands (from `windows/`):
  `... -- migrate-presets [--python-root path] [--out-dir path]` (no
  elevation; defaults to this repo's root and `windows/data/`),
  `... -- preset-list [--out-dir path]`,
  `... -- preset-demo <name> [seconds] [--out-dir path] [--keymap path]`
  (no elevation), `... -- lightbar-preset-demo <name> [--out-dir path]`
  (**requires Administrator**).

## Suggested phasing (from the planning conversation)

1. Branch + `windows/` scaffold + .NET project setup, initial
   `HANDOFF.md` — **DONE**
2. Port the WMI lightbar + HID keyboard protocol to C#, resolve the
   COM/WMI API-choice risk empirically, prove real hardware control
   from a bare console test — highest-risk part, get this solid before
   building anything on top of it — **DONE**, see "Highest-risk area"
   and "Verified vs. unverified" above
3. Port every existing effect (including typing-reactive and
   custom_keys) behind the `IEffect` abstraction; confirm each looks
   right on real hardware — **DONE**, see "Phase 3: effects" above
4. Preset persistence layer (atomic writes) + migration of existing
   on-disk preset data into the new system — **DONE**, see "Phase 4:
   presets" above
5. Windows Service wrapper (boot-time start, disk-persisted state
   restore, `AcerLightingService` disable-at-startup) **+
   controller-reactive support** (`Controller.cs` in `JmaStudio.Hardware`,
   the reactive effect in `JmaStudio.Effects`, the full-override enable/
   disable logic and `controller_reactive.json` migration in the
   service itself) — the user explicitly chose to build this here
   rather than in Phase 4, since it depends on the enable/disable
   plumbing this phase provides
6. WPF GUI: replicate existing UX, add Create Preset + dominance-
   reassert button
7. Installer (location prompt, consent notice, service registration,
   GUI autostart, preset data migration)
