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

**Phases 1-5 — DONE. Phase 6 (WPF GUI) — functionally complete except
3 remaining windows.** Read "Phase 6 continued" and, more importantly,
**"Phase 6 continued: tuning panels + full theming pass + window
behavior (2026-09-10, third session)"** (the newest section, right
before "Key technical decisions") before doing any more GUI work — it
covers the Gradient/Reactive Typing/Custom Key Colors panels (the last
missing pieces of parity with the Python GUI), a full visual theming
pass (the user's own words: "nothing feels polished" → after the pass,
"looks good now" / confirmed the sliders, presets, checkboxes, and
combos all fixed one by one), top-centered window placement, single-
instance enforcement, and a touchpad-scroll-speed fix. Real hardware
control confirmed live for keyboard, lightbar, AND controller. The
whole thing runs as a real ASP.NET Core service process (console-
testable today, real OS service registration deferred to Phase 7)
fronting an HTTP API. **What's left in Phase 6, per the user's own
explicit next-up list**: the Lightbar window, the Controller Reactive
window, and the Diagnostics window — "we are going to tackle [them]
one at a time" starting next session. Not started at all: the
installer (Phase 7) — explicitly deferred until Phase 6 (including
these 3 windows) is fully done, not scheduled ahead of it. **Check
"Immediate live state" at the very end of this file for exactly what's
running right now** — don't rely on any of the older Phase 5/6
"immediate live state" language earlier in this file, only the
bottommost section is current.

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
- [x] Controller-reactive support (`Controller.cs`, the reactive
      effect, enable/disable + settings) — see "Phase 5" below
- [x] Windows Service wrapper (as an ASP.NET Core app, console-run so
      far — real OS service registration is Phase 7's job) — see
      "Phase 5" below
- [~] WPF GUI — **main window is functionally and visually complete**:
      live preview, presets (whole-card click-to-apply + delete),
      quick effects, and all 3 tuning panels (Gradient/Reactive Typing/
      Custom Key Colors), all live-tested and theme-polished to match
      the Python GUI. NOT done: the Lightbar/Controller Reactive/
      Diagnostics windows (all 3 currently `MessageBox` "coming soon"
      placeholders) — see the newest "Phase 6 continued" section below
      before starting any of them.
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
group demo, but not literally clicked through one by one.

**Update from Phase 5**: the "apply the default preset at startup"
behavior mentioned as untested above IS now verified -- see "Phase 5"
below (`GET /status` showed the real "Red Chase" default applied on
the service's very first boot, before any live state had ever been
persisted). The lightbar reactive config's "flash on keypress" loop is
still not built at all (not just untested) -- see Phase 5's "NOT
built" list.

## Phase 5: Windows Service + controller-reactive — DONE (2026-09-09)

New `JmaStudio.Service` project: an ASP.NET Core minimal-API app that
owns the keyboard/lightbar/controller connections and the 30fps render
loop, replacing `daemon/server.py`'s role. Confirmed running end to end
against real hardware, including a from-cold-boot test that correctly
applied the user's real migrated default preset with no live state on
disk yet.

**Architecture**:
- **Hosting**: `Microsoft.Extensions.Hosting.WindowsServices`'
  `UseWindowsService()` -- the same binary runs as a real Windows
  Service when launched by the Service Control Manager, or as a plain
  console app otherwise (that's how this was tested all through this
  phase; actual OS service *registration* -- `sc.exe create`/similar --
  is Phase 7's job, not built here). Binds to `http://127.0.0.1:8420`
  explicitly (same port and loopback-only convention `daemon/server.py`
  used) rather than ASP.NET's default dev-cert ports.
- **`DaemonState`**: the C# analogue of Python's `_current_effect`/
  `_current_params` module globals, but with an explicit `lock`
  (settled decision: no GIL to lean on) and disk persistence on every
  `SetEffect()` call (settled decision #9) via a new `LiveKeyboardState`
  `JsonStore` in `JmaStudio.Presets`. On first-ever boot (no live state
  file yet), falls back to `AppConfig.DefaultPreset` looked up in the
  migrated `KeyboardPresets` -- confirmed live: a fresh service start
  applied "Red Chase" correctly, not a black screen.
- **`LightbarController`**: wraps `Lightbar` so every mutating call
  (`SetZone`/`SetAll`/`SetMode`/`SetBrightness`/`Off`/`ApplyState`) also
  persists `GetState()` to a `LiveLightbarState` `JsonStore`, and a
  `RestorePersistedState()` called once at startup re-applies it. Kept
  as a wrapper rather than adding persistence into `Lightbar` itself,
  so `JmaStudio.Hardware` stays protocol-only (no persistence
  awareness), matching its existing scope.
- **`RenderLoopService`** (a `BackgroundService`): the ~30fps loop,
  injecting live `KeyState` (from `InputListener`) and `ControllerState`
  (from `Controller.GetState()`) into `EffectContext` each frame, same
  shape as `daemon/server.py`'s `_render_loop()`. Skips the actual HID
  write when the frame didn't change (via `DaemonState.RecordFrame`'s
  bool return), same optimization Python has.
- **`InputListener`** + `GlobalKeyboardHook`/`WindowsKeyMap`: the C#
  analogue of `daemon/input_listener.py`. The hook/keymap classes are
  the same implementation already proven in `JmaStudio.HardwareTest`
  during Phase 3/4 interactive testing (see `effect-live`), copied
  (not shared via project reference) into `JmaStudio.Service` as the
  real, permanent version -- narrow-scope by design, same principle as
  the Python original: only ever produces a key name on keydown,
  nothing persisted.
- **`Controller.cs`** (new, in `JmaStudio.Hardware`): port of
  `hardware/controller.py`, including the sleep/wake auto-reconnect fix
  from `main`'s own HANDOFF.md (detects a >2s gap in the ~1000Hz report
  stream and reopens the HID handle) -- reproduced from the start here
  rather than waiting to hit the same bug again.
- **`ControllerReactiveEffect`** (new, in `JmaStudio.Effects`): port of
  `effects/controller_reactive.py`, reading `EffectContext.ControllerState`
  the same way `TypingReactiveEffect` reads `KeyState`.
  `ControllerReactiveParams`/`StickColors` added to `EffectParams.cs`
  with the same `[JsonDerivedType]` polymorphism as every other effect.
- **`ControllerReactiveManager`** (new, in `JmaStudio.Service`): the
  "full override" enable/disable stash-and-restore logic (Python's
  `_pre_controller_reactive_state`), deliberately kept out of
  `JmaStudio.Effects` per the earlier placement decision. **Known gap,
  documented in the file's own header comment**: because `DaemonState`
  now persists whatever effect is live to disk on every change (a real
  C# addition, settled decision #9, that Python doesn't have), a
  service restart while controller-reactive is enabled boots directly
  back into controller-reactive rather than restoring the pre-enable
  effect -- the stash is in-memory only. This is a genuinely new edge
  case, not a regression, and wasn't fixed now; whoever revisits it
  should read that comment first.
- **`AcerLightingServiceManager`**: stops `AcerLightingService` and
  sets its startup type to `Disabled` via `Win32_Service.ChangeStartMode`
  (WMI, `System.Management`) -- called unconditionally at every service
  startup, matching settled decision #10's "cheap insurance" requirement
  (same spirit as `start_all.ps1`'s own retry loop on the Python side).
  Confirmed running without error on real hardware every startup this
  session; a fresh from-scratch verification that it actually prevents
  PredatorSense from regaining control was NOT repeated here (already
  established once on the Python side, and this C# code path calls the
  same underlying Win32 service, not a new mechanism).
- **HTTP API**: modernized per settled decision #7 -- real status codes
  (`404`, `422`, `503`, `400`) and JSON error bodies instead of Python's
  always-`200` `{"ok":...,"error":...}` convention. Route paths mostly
  mirror `daemon/server.py`'s own (`/status`, `/effect`, `/presets`,
  `/lightbar/*`) since there's no reason to churn those; the newer
  controller-reactive group uses kebab-case (`/controller-reactive/*`)
  since it has no existing Python path convention to match. **Real gap,
  not silently dropped**: `ConfigureHttpJsonOptions` needed the exact
  same `IncludeFields`/`JsonStringEnumConverter` settings
  `PresetJsonOptions.Default` already has, or ASP.NET Core's own default
  JSON options apply instead -- hit this live (bolt directions came back
  as empty `{}` objects, enums as raw ints) and fixed it before it
  shipped further.

**Verified on real hardware, this session** (after stopping the Python
daemon first, to avoid the exact two-processes-fighting-over-the-
keyboard problem hit and fixed earlier in this same session):
- Cold service start: `AcerLightingService` handled, keyboard/lightbar/
  controller all initialized, `GET /status` showed the real migrated
  "Red Chase" default preset applied with zero live state on disk --
  the whole boot-fallback chain (`AppConfig` → `KeyboardPresets` →
  `DaemonState`) confirmed working, not just unit-testable.
  `GET /effects` listed all 22 registered effects (21 + controller_reactive).
- `POST /effect` (switch to `rainbow`): user confirmed live.
- `POST /presets/ZONES/apply` (the multi-zone gradient preset, via the
  real HTTP API this time, not the `HardwareTest` console tool): user
  confirmed live.
- `POST /lightbar/all` (solid green, all 3 zones): user confirmed live
  (also noted the middle zone reads very slightly dimmer than the
  outer two -- a hardware/diffuser characteristic, not a software bug,
  since the identical RGB value is sent to all 3 zones in one call).
- Controller-reactive full lifecycle: `POST /controller-reactive/enable`
  took over the keyboard; user confirmed the DualSense drove it live
  (initially with default dark-green colors, since the settings file
  hadn't been migrated yet -- caught this, extended
  `PythonPresetMigrator`/`PresetStore` to cover
  `controller_reactive.json`, re-migrated, restarted the service,
  re-enabled to force a refresh, and the user then confirmed the REAL
  saved colors -- background off, custom per-button colors -- rendering
  correctly); `POST /controller-reactive/disable` correctly restored
  whatever had been stashed (the stash content was itself an artifact
  of the restart-timing during this test, not a bug in the restore
  mechanism -- see the file's own header comment).
- Ended the session by applying "Red Chase" and "Rainbow" (the user's
  real keyboard/lightbar defaults) via the real API, at the user's
  request, leaving the C# service as the one actually driving the
  user's hardware afterward (the Python daemon was stopped for this
  whole testing session and, as of this writing, has not been
  restarted -- see "Immediate live state" if this file gains that
  section again before the next session).

**NOT built in Phase 5** (explicitly out of scope for this pass, not
forgotten):
- The lightbar's keypress-driven reactive flash loop (Python's
  `_lightbar_reactive_loop` / `/lightbar/reactive` config apply) --
  `LightbarReactiveConfig` migration exists (Phase 4) but nothing
  reads/acts on it yet.
- `/keypress` (the GUI's focus-independent forwarded-keystroke path,
  needed because a focused WebView2 page swallows real OS keystrokes
  before the global hook sees them -- see `daemon/input_listener.py`'s
  `register_named_press()` docstring on `main` for why this exists).
  Deferred until the WPF GUI (Phase 6) exists to actually need it --
  WPF doesn't necessarily have the same WebView2-swallows-keystrokes
  problem, so this should be re-evaluated then, not assumed necessary.
- Real OS Windows Service *registration* (`sc.exe create` or
  equivalent) -- explicitly Phase 7's job per the original phasing.
- A dedicated `/controller-reactive/button-groups` /
  `/controller-reactive/defaults` introspection endpoint (Python has
  these for its GUI to build the color-picker grid dynamically) -- not
  needed until Phase 6 has a GUI that needs them.

## Phase 6: WPF GUI — IN PROGRESS (2026-09-10)

New `JmaStudio.Gui` project (WPF, net8.0-windows). **First slice only**
— read "NOT done" below before assuming more exists than does.

**What exists**:
- `ApiClient.cs`: thin `HttpClient` wrapper over the Service's API,
  reusing the exact same strongly-typed models (`EffectParams`,
  `KeyboardPreset`, `LightbarState`, ...) via project references, and
  `PresetJsonOptions.Default` (`JmaStudio.Presets`) for deserialization
  — deliberately the SAME options object the Service uses for file
  persistence, so there's one JSON convention to reason about, not two.
- `MainWindow.xaml`/`.xaml.cs`: dark-themed shell replicating
  `gui/index.html`'s top-level structure — top bar (hardware status
  dot/label, Lightbar/Controller Reactive/Diagnostics/Off buttons),
  live keyboard preview (a `Canvas` of one `Rectangle` per cell,
  positioned via the new `GET /layout` endpoint — see below), a
  Presets panel (apply/delete, each preset as a card), a Quick Effects
  panel (one button per registered effect, applies with that effect's
  own `DefaultParams`), and a footer (Save current as preset / Set as
  startup default), all wired to the real API.
- `NamePromptWindow.xaml`/`.xaml.cs`: a small modal for the one text
  input WPF has no built-in equivalent for (the save-preset name
  prompt) — matches `gui/index.html`'s own modal in spirit.
- Two new Service endpoints added specifically to support the GUI
  without giving it direct filesystem access: `GET /layout` (cell
  index → name/row/col, mirrors `daemon/server.py`'s own `/layout`)
  and `POST /effects/{name}/apply-default` (apply a registered effect
  using its own `IEffect.DefaultParams`, for the Quick Effects grid,
  which shouldn't need to know each effect's params shape just to
  offer "try this effect").

**Two real bugs found and fixed while wiring this up** (both are
exactly the kind of thing that would have been very confusing to debug
from inside a future GUI feature instead of this first, minimal
connectivity test — this is why building the plumbing first and
proving it end-to-end mattered):
1. **`EffectParams` JSON discriminator had to be the first property or
   deserialization failed outright.** `System.Text.Json`'s built-in
   `[JsonPolymorphic]`/`[JsonDerivedType]` attributes read JSON as a
   forward-only stream and require the `"$effect"` discriminator to
   appear before any other property — a perfectly valid but
   differently-ordered JSON object (e.g. `{"colors":...,"$effect":...}`
   instead of `{"$effect":...,"colors":...}`) 500'd with a confusing
   error naming the abstract `EffectParams` type itself. **Fixed**:
   replaced with a custom `EffectParamsJsonConverter` (in
   `JmaStudio.Effects`) that buffers the whole object
   (`JsonDocument.ParseValue`) before reading any property, so field
   order stops mattering for any client. Already committed
   (`b667ac0`, before this GUI work).
2. **HTTP JSON casing mismatch between the Service and the GUI.**
   `PresetJsonOptions.Default` (designed for file persistence) has no
   naming policy, so it round-trips named record types
   (`KeyboardPreset`, `LightbarState`, ...) as PascalCase, matching
   their real C# property names. But several `Endpoints.cs` handlers
   (`/status`, `/frame`, `/lightbar/status`) return **anonymous C#
   object literals with camelCase property names hardcoded directly in
   the source** (e.g. `new { keyboardConnected = ... }`) — that's not
   a naming-policy artifact, it's literally what those properties are
   named, so setting `PropertyNamingPolicy = null` server-side (tried
   first) did nothing for those endpoints. The real, robust fix: added
   `PropertyNameCaseInsensitive = true` to `PresetJsonOptions.Default`
   itself, since that's shared by both the file-persistence code and
   the GUI's `ApiClient` — makes deserialization tolerant of casing
   differences regardless of which shape a given endpoint happens to
   use, rather than requiring every current and future endpoint to be
   audited for consistent casing by hand. **Symptom before the fix was
   confusing and worth remembering**: no exception, no error — every
   mismatched property just silently stayed at its type's default
   (frequently `null` for reference types), which only surfaced much
   later as an unrelated-looking `NullReferenceException` deep in
   `MainWindow.xaml.cs`. If a future symptom looks like "some API
   response is silently all-default/null," suspect a casing (or other
   silent-mismatch) issue before assuming the deserialized value is
   simply absent.

**Verified working (real hardware, this session)**: the GUI connects
to the real Service, hardware status shows "connected," and the live
keyboard preview canvas shows the user's REAL current colors (Red
Chase's actual per-key blues/purples), confirmed by the user after
both bugs above were found and fixed.

**NOT yet verified** — only the passive display path (status + frame
polling) was actually exercised. None of the interactive controls have
been click-tested yet: Apply/Delete on a preset card, Save current as
preset, Set as startup default, Off, or any Quick Effects button.
Don't assume these work just because they compile and the passive
path does.

**Known issues from live user feedback, NOT fixed yet** — this is the
most important unresolved item in this whole file as of this session
ending:
1. **Responsiveness/animation smoothness is noticeably worse than the
   Python GUI** — the user's exact words: "the reaction time of the
   keyboard is slow, and the animation is slow for reactive and
   doesn't keep up the way the python version did." Prime suspects,
   not yet investigated in code:
   - `MainWindow.xaml.cs`'s poll loop (`_pollTimer.Interval =
     TimeSpan.FromMilliseconds(150)`) refreshes the preview at ~6-7Hz,
     far below the Service's actual 30fps render loop — for fast
     effects (bolts, chases) this alone would look choppy regardless
     of anything else.
   - `PollAsync()` awaits `GetStatusAsync()` then `GetFrameAsync()`
     **sequentially**, not concurrently (`Task.WhenAll`) — doubles the
     round-trip latency per tick for no reason, since the two calls
     are independent.
   - `GetStatusAsync()` re-fetches and re-deserializes the ENTIRE
     current effect's params on every single poll tick, including
     potentially large ones (`typing_reactive` wrapping `custom_keys`'s
     100+-entry color dictionary, for example) — the preview loop only
     actually needs `/frame`; `/status` (for the current-effect label
     and connection dot) could poll far less often, or the Service
     could expose a lighter-weight "just the essentials" status shape.
   - Not yet profiled which of these actually dominates — don't assume
     the fix is "just raise the polling rate" without checking; a
     naive higher-frequency poll that still does two sequential full
     HTTP+JSON round trips per tick might not actually get close to
     30fps.
2. **Visual polish is well below the Python GUI**: the user's exact
   words: "the keyboard doesn't look nearly as nice as the python
   version." The current preview is a plain black `Canvas` with
   generically-sized (20×20, 3px corner radius) rectangles and a flat
   3px gap — no attempt yet to match `gui/style.css`'s actual keyboard
   styling (key proportions, real stagger fidelity beyond raw
   row/col numbers, any depth/glow/shadow treatment, key labels).
   Compare directly against `gui/style.css` + `gui/app.js`'s keyboard-
   building code on `main` when picking this up again — this was
   built as a functional first pass, explicitly not a design pass.

**NOT built at all yet** (not forgotten, explicitly deferred to keep
this first slice small enough to actually verify end-to-end):
- Lightbar window, Controller Reactive window, and Diagnostics window
  — all three are currently `MessageBox` "coming soon" placeholders in
  `MainWindow.xaml.cs`.
- The Gradient panel, Reactive Typing panel, and Custom Key Colors
  painter from `gui/index.html` — the per-effect tuning UI. Quick
  Effects only applies each effect's bare defaults right now.
- The Diagnostics window design captured earlier in this file (the 3
  emergency buttons with UAC shields + install-detection gating, plus
  the broader dashboard proposal) — none of it is built, only planned.

## Phase 6 continued: performance fix + visual overhaul + 3 bugs found live (2026-09-10, second session)

Picked back up the same day, focused entirely on the two unfixed issues
from the section above (performance, visual polish) plus whatever live
testing surfaced. All of it was verified against the user's real
hardware and real GUI clicks/keypresses, same verification bar as every
other phase in this file.

**1. Responsiveness fix — user-confirmed better.** Root-caused to
exactly hypotheses #1 and #2 from the section above, both fixed at once
in `MainWindow.xaml.cs`:
- Replaced the single 150ms `_pollTimer` with two independent
  `DispatcherTimer`s: `_framePollTimer` at 33ms (matching
  `RenderLoopService`'s real 30fps render rate) calling a new
  `PollFrameAsync()`, and `_statusPollTimer` at 750ms calling a new
  `PollStatusAsync()` — status (current effect name, connection dot,
  frame counters) doesn't need anywhere near frame-rate polling.
- The two are no longer awaited sequentially — they're on separate
  timers entirely now, not just parallelized within one tick, which
  also directly addresses hypothesis #3 (the full `/status` payload,
  including potentially-large `EffectParams`, is now fetched ~14x less
  often than before).
- Added `_frameInFlight`/`_statusInFlight` guards so a slow tick can't
  stack up overlapping HTTP requests against the service.
- **User-confirmed**: "much better" after this fix, before the visual
  work below even started.

**2. Visual overhaul of the keyboard preview — user-confirmed "much
nicer."** The old canvas drew every cell as a uniform 20x20 black
square with a 3px gap and no labels — nothing like the Python GUI's
real keyboard shape. Fixed by porting `gui/app.js`'s layout tables
directly:
- New file `KeyboardKeyStyle.cs`: `KeyWidth`/`KeyHeight` dictionaries
  (wide keys like space/shift/enter, the function row's narrower 0.85u,
  num_enter's 2-row height) and `FriendlyLabel()`, transcribed verbatim
  from `gui/app.js`'s `KEY_WIDTH`/`KEY_HEIGHT`/`FRIENDLY_LABEL`/
  `friendlyLabel()`.
- `BuildKeyboardCanvas()` (`MainWindow.xaml.cs`) rewritten: each cell is
  now a `Border` (5px corner radius, subtle white border, dark base
  fill) with a centered `TextBlock` label, sized per-key from the new
  tables instead of a uniform square, and the whole board scales its
  per-key pixel `unit` to fit the panel's actual width (clamped 16-34px)
  the same way `buildKeyboardGrid()` does in `gui/app.js`, instead of a
  fixed size. `PreviewBoardHost_SizeChanged` (new, wired in
  `MainWindow.xaml`) rebuilds the grid on window resize so it keeps
  fitting. `_cellRectangles` (a `Rectangle` dict) was renamed
  `_cellBorders` (a `Border` dict) throughout, including in
  `PollFrameAsync` where live colors are painted.
- Still NOT done (user, after seeing this): "still some UI stuff that
  could be touched up... let's deal with that when things are more
  complete" — explicitly deferred, not forgotten. Likely candidates for
  a future pass: overall window chrome/panel colors don't match
  `gui/style.css`'s `--bg`/`--panel` palette closely (close but not
  exact), preset cards and quick-effect chips don't have the CSS
  reference's hover/active states or accent gradient, no toast
  animation. Nothing broken, just not pixel-matched.

**3. Bug found live: "custom_keys" Quick Effect = all-off keyboard.**
User reported the keyboard going fully dark after tabbing away from the
GUI. Investigation via `GET /status` showed `currentEffect: "custom_keys"`
with `Colors: {}` and `DefaultColor: {R:0,G:0,B:0}` — i.e. exactly what
you'd expect from clicking a "custom_keys" quick-effect button, since
that effect's bare `DefaultParams` is an empty per-key color map with a
black fallback. **This was never a hardware/timing bug** — the C# GUI's
Quick Effects panel was listing every single registered effect
(including diagnostic-only and blank-canvas-by-default ones) with zero
filtering, unlike the Python GUI, which has always hidden exactly this
set from its own chip grid (`gui/app.js`'s `HIDDEN_FROM_CHIPS`). Fixed
in `MainWindow.xaml.cs` with a matching `HiddenFromQuickEffects` set:
`probe`, `mask`, `gradient`, `typing_reactive`, `static`, `custom_keys`
(same 6 as Python) **plus `controller_reactive`** (a C#-only addition,
excluded because it has its own enable/disable lifecycle via
`ControllerReactiveManager` and must never be poked via a raw one-click
`/effect` apply that bypasses that stash/restore logic).

**4. Bug found live: WPF access-key hazard turned a bare keypress into
a silent effect switch.** After fix #3, the user reported the keyboard
switching from "Red Chase" to a rainbow-cycling effect "after only a
couple seconds" while testing — with no click. Since there was **no
request logging anywhere in the Service** at the time, this was
undiagnosable from evidence, so request logging was added first (see
#5) before it could be confirmed. The very next occurrence, the user
reported it happened "when I pressed c" while randomly mashing keys
(not clicking). Root cause: `MainWindow.xaml`'s Quick Effects
`DataTemplate` bound `Button.Content="{Binding}"` directly to the raw
effect name string. WPF's default `Button` template sets
`ContentPresenter.RecognizesAccessKey = true`, which means a literal
`_` character in bound `Content` text is parsed as an access-key
(mnemonic) marker — the character right after it becomes a hidden
Alt+key shortcut for that button, with the underscore itself hidden
from display. Effect names are snake_case (`spectrum_cycle`,
`color_wipe`, ...), so **every single quick-effect button silently
registered an Alt+key shortcut nobody asked for**, using whatever
letter happened to follow an underscore in its name. Once Alt was
pressed anywhere (even incidentally, e.g. testing the `left_alt` key
while typing), WPF's `AccessKeyManager` entered its classic Win32
"access-key mode," where a **subsequent bare letter keypress alone**
(no Alt held) fires the matching mnemonic — exactly matching "pressed
c" with no visible modifier. Confirmed the exact trigger via the new
request log: `POST /effects/spectrum_cycle/apply-default` fired at the
moment the user pressed `c`. **Fixed**: added an `EffectChip(string
Name, string DisplayName)` record; `DisplayName` replaces every `_`
with a space before binding to `Content` (also just reads better —
"spectrum cycle" instead of "spectrum_cycle"), while `Name` (the real,
raw effect id, no underscores stripped) stays bound to `Tag` for the
click handler's existing `Tag: string name` pattern match, so the API
calls are unaffected. **This class of bug can recur anywhere a raw
snake_case name gets bound straight to a WPF `Button`/`MenuItem`/
`Label`'s `Content` or `Header`** — worth grepping for
`Content="{Binding` / `Header="{Binding` against any future
raw-identifier-string binding, not just effect names (preset names are
user-chosen and typically don't contain underscores, but nothing stops
a user from naming a preset with one).

**5. Added permanent request logging to `JmaStudio.Service`.** There
was no logging of incoming HTTP requests at all before tonight, which
made bug #4 briefly undiagnosable from evidence (had to wait for a
second live occurrence after adding logging). Added a minimal
`app.Use(...)` middleware in `Program.cs`, right after `app.Build()`
and before the `Endpoints.Map*` calls, logging `{Method} {Path}` via
`ILogger<Program>` for every request (not bodies, to keep it cheap on
the 30fps-adjacent `/frame` polling path). This is a permanent addition,
not a temporary debug hack — kept because it's exactly what made bug #4
solvable instead of a repeat guessing game. Requires a service restart
to pick up (done tonight via the elevated restart flow — see "How this
was actually verified" above for the general elevation-wrapper pattern
this reused).

**Verified working, end to end, tonight**: split-timer responsiveness
(user: "much better"), the new keyboard grid layout (user: "much
nicer"), both GUI bugs fixed and reproduced-then-resolved live via
actual clicks/keypresses against real hardware, request logging
confirmed capturing the exact triggering request for bug #4.

**Uncommitted as of end of session — 4 files, listed here so nothing
gets lost or double-guessed tomorrow**:
- `windows/src/JmaStudio.Gui/MainWindow.xaml` (modified — `Canvas`
  height no longer fixed, `PreviewBoardHost` named +
  `SizeChanged` wired, Quick Effects button binds `DisplayName`/`Name`
  instead of the raw string twice)
- `windows/src/JmaStudio.Gui/MainWindow.xaml.cs` (modified — split
  timers, `Border`-based keyboard grid, `HiddenFromQuickEffects`,
  `EffectChip`)
- `windows/src/JmaStudio.Gui/KeyboardKeyStyle.cs` (new — key
  width/height/label tables)
- `windows/src/JmaStudio.Service/Program.cs` (modified — request
  logging middleware)
- `windows/data/live-keyboard-state.json` is ALSO showing as modified
  in `git status` — this is just real-time disk persistence from
  tonight's testing (settled decision #9: every `SetEffect()` call
  writes through to disk), not something anyone edited by hand. Expect
  this file to keep churning normally as testing continues; it's real
  user hardware state, tracked in git same as the other `windows/data/`
  files, not something to "clean up."
- **None of this was committed tonight** — the user asked to update
  this handoff and get to a clean stopping point, not to commit
  (per this project's standing rule: only commit when explicitly
  asked). Nothing is at risk by leaving it uncommitted — it's sitting
  on disk on the `csharp-port` branch same as any in-progress work — but
  don't assume it's committed either; check `git status` before
  building on top of it or before reporting phase completion.
  **Update from the next session**: all of the above was committed as
  `b75cfec` at the start of the next session, at the user's explicit
  request ("commit and switch back over").

## Phase 6 continued: tuning panels + full theming pass + window behavior (2026-09-10, third session)

Picked back up the same day (after the user rebooted/logged back in,
which is why the Python autostart task had already re-fired and needed
switching back to the C# stack at the start of this session). Covers
three things, in the order they happened: (1) porting the three tuning
panels the WPF GUI was still missing, (2) a full visual theming pass
after the user reported the result "feels functional but not
polished," (3) three small window-behavior fixes requested afterward.
**This is the section to read before starting the Lightbar/Controller
Reactive/Diagnostics windows** — the patterns established here
(`ColorSwatchButton`, `Debouncer`, the `_uiReady`/`_suppressLiveApply`
guard pair, the implicit dark-theme control styles) are meant to be
reused by those windows, not reinvented.

### Gradient / Reactive Typing / Custom Key Colors panels — DONE

Full behavioral parity with `gui/index.html`'s three tuning panels and
`gui/app.js`'s corresponding logic (`readGradientParams`,
`readTypingReactiveParams`, `readCustomKeysParams`,
`syncTuningPanelsFromPreset`, the debounced live-apply functions, the
`applyCurrentLive()` dispatcher), confirmed working live end to end
before the theming pass started.

**New files** (`JmaStudio.Gui`):
- `ColorSwatchButton.cs` — WPF has no `<input type="color">` equivalent.
  A small `Border`-derived control that opens
  `System.Windows.Forms.ColorDialog` (WinForms interop, via
  `<UseWindowsForms>true</UseWindowsForms>` in the `.csproj`) on click.
  **Real gotcha hit and fixed**: combining `UseWPF` and
  `UseWindowsForms` with implicit usings enabled makes the SDK
  auto-inject a `global using System.Windows.Forms;` alongside WPF's
  own `System.Windows.*` global usings, which collide on every
  identically-named type both frameworks define (`Color`, `ComboBox`,
  `Application`, `KeyEventArgs`, ...) — this broke the build project-
  wide, not just in the one file that needed WinForms. Fixed with
  `<Using Remove="System.Windows.Forms" />` and
  `<Using Remove="System.Drawing" />` in the `.csproj`, since the only
  WinForms type actually used (`ColorDialog`) is fully-qualified in
  `ColorSwatchButton.cs` anyway.
- `Debouncer.cs` — a tiny reusable wrapper around `DispatcherTimer`
  matching `gui/app.js`'s `debounce(fn, ms)` helper. Each panel gets
  its **own** `Debouncer` instance (not one shared one) so, e.g.,
  dragging a Gradient slider can't cancel a pending Custom Key Colors
  apply — matches app.js's independent per-function debounce closures.
- `MainWindow.Gradient.cs`, `MainWindow.TypingReactive.cs`,
  `MainWindow.CustomKeys.cs` — three `partial class MainWindow` files
  (not separate classes/windows), one per panel, holding that panel's
  state/logic. `MainWindow.TypingReactive.cs` also hosts
  `ApplyCurrentLiveAsync()`, the single dispatcher both the Gradient
  and Reactive Typing panels' live-apply funnel through (mirrors
  app.js's `applyCurrentLive()`: decides whether the currently-tuned
  gradient/custom-keys goes out wrapped in `typing_reactive` or bare,
  based on the "Enable typing-reactive chase" checkbox). **Custom Key
  Colors' own live-apply deliberately does NOT go through this
  dispatcher** — ported faithfully from app.js's `applyCustomKeysLive`,
  which always posts a bare `custom_keys` effect directly even if
  `typing_reactive` is currently using it as a background. Confirmed
  this is app.js's real behavior, not an oversight, before replicating
  it — editing that grid always previews as a static board.

**Shared keyboard-grid refactor**: `MainWindow.xaml.cs`'s
`BuildKeyboardCanvas()` (the live-preview grid) was split into a
reusable `BuildKeyGrid(Canvas, Border host, Dictionary<int,Border>
cellMap, Action<Border,LayoutCell>? decorate)`, mirroring app.js's own
`buildKeyboardGrid()` being shared between the live-preview board and
the Custom Key Colors editor grid. `BuildCustomKeysCanvas()`
(`MainWindow.CustomKeys.cs`) calls it with a `decorate` callback that
wires click-to-select (with shift/ctrl for multi-select, matching
`toggleKeySelection()`); the live-preview board passes `null`.

**Two real WPF-specific crashes hit and fixed while wiring the Reactive
Typing panel's controls up** — both are the same underlying class of
bug, worth knowing about before adding more XAML-wired event handlers
anywhere in this app:
1. A `CheckBox` declared with a non-default `IsChecked="True"`
   attribute in XAML, with `Checked`/`Unchecked` handlers wired in the
   *same* file, fires those handlers **mid-BAML-parse** — before
   later-declared named elements in that same XAML file have been
   assigned to their fields yet. Crashed with a `NullReferenceException`
   from inside a label-update method that referenced a combo box
   declared further down the file. Fixed for the two affected
   checkboxes by setting `IsChecked` in code (after
   `InitializeComponent()` has fully returned) instead of as a XAML
   attribute.
2. The same failure mode, via a completely different trigger: a
   `Slider` with `Minimum`/`Maximum` set in XAML but no `Value`
   attribute has a default `Value` of `0`, which sits below several of
   these sliders' `Minimum` — WPF's property-coercion logic silently
   snaps `Value` into `[Minimum, Maximum]` range as soon as `Minimum`
   is parsed, which **fires `ValueChanged`** as a side effect, again
   mid-parse, again before other named elements exist.
   **General fix, not just a per-control patch**: added a `_uiReady`
   field (`MainWindow.xaml.cs`), false until immediately after
   `InitializeComponent()` returns in the constructor, and added
   `if (!_uiReady) return;` as the first line of every XAML-wired event
   handler in the tuning panels. This is a different, complementary
   guard from `_suppressLiveApply` (also `MainWindow.xaml.cs`, pre-
   existing): `_uiReady` answers "is it even safe to touch other named
   elements right now" (a WPF construction-order question), while
   `_suppressLiveApply` answers "should this cause a network side
   effect right now" (a "don't overwrite the live keyboard state during
   programmatic setup" question) — both are needed, for different
   reasons, and dynamically-created controls (the Gradient panel's
   per-zone color/boundary rows, built entirely in C# well after the
   window exists) can never hit the `_uiReady` problem in the first
   place, only XAML-declared controls with XAML-wired handlers can.

**Preset sync**: `MainWindow.xaml.cs` now caches the last-fetched
`Dictionary<string,KeyboardPreset>` (`_presets` field) so applying a
preset card can look up its real typed params without a second HTTP
round trip, and calls a new `SyncTuningPanelsFromPreset(KeyboardPreset)`
that dispatches by the params' concrete C# type
(`TypingReactiveParams`/`GradientParams`/`CustomKeysParams`) to each
panel's own `Load*Params()` method — the same job as app.js's
`syncTuningPanelsFromPreset()`, but type-dispatched instead of
string-`effect`-field-dispatched, since the C# side already has real
typed params rather than an untyped dict. **Wrapped in
`_suppressLiveApply = true/false`** — each `Load*Params()` call flips
several checkboxes/combos that would otherwise each schedule their own
redundant live-reapply of the preset that was just applied a moment
ago (a real difference from app.js: setting `.checked` programmatically
in JS never fires a change/input event, but a WPF dependency-property
assignment always fires its `RoutedEvent`, checked or not).

**Verified working live** (real hardware, before the theming pass):
all three panels' controls tested via actual clicks/drags/color picks —
gradient zone count/colors/boundaries/brightness, typing-reactive's
bolt shape/style/speed/tail/decay/max-distance/flicker plus both
background-source toggles and their mutual exclusivity, custom key
colors' click-to-select (single and shift-click multi-select),
paint/default color pickers, select-all/deselect/reset/clear/pull-
current, and recent-colors persistence (a small JSON file under
`%LocalAppData%\JmaStudio\gui-recent-colors.json`, the closest WPF
equivalent to `localStorage` — same best-effort try/catch spirit as
app.js's own `loadRecentColors`/`saveRecentColors`).

### Full visual theming pass — DONE, user-confirmed

User's own words kicking this off: "It all seems to be functional but
nothing feels polished. The sliders are blocky looking, the apply and
X on the pre-sets look stupid, you have black text on a grey
background... This is a UI for gamers not IT administrators." All of
the below was in `MainWindow.xaml`'s `Window.Resources` (replacing the
earlier minimal one) unless noted, and confirmed fixed one issue at a
time via live screenshots, not assumed:

- **Palette**: replaced the ad hoc first-slice colors with values
  transcribed from `gui/style.css`'s `:root` variables (`--bg`,
  `--panel`, `--accent-a/b/c`, `--success`, `--danger`, the
  `--accent-grad` gradient), as real `SolidColorBrush`/
  `LinearGradientBrush` resources (`BgBrush`, `PanelBrush`,
  `CardBrush`, `AccentABrush`/`AccentBBrush`/`AccentCBrush`,
  `AccentGradBrush`, `SuccessBrush`, `DangerBrush`, `TrackBrush`,
  `HoverOverlayBrush`).
- **Sliders**: WPF's default `Slider` renders as the plain blocky
  system control the user flagged. Re-templated (implicit style, no
  `x:Key`, so this covers every `Slider` in the window including the
  Gradient panel's dynamically-created boundary sliders for free) as a
  thin flat track (matching `gui/style.css`'s range input, which is a
  single flat color with no filled/unfilled split) with a glowing
  gradient-filled circular thumb (`DropShadowEffect`, a dark ring
  border) — the standard WPF pattern of only re-skinning the `Track`'s
  `DecreaseRepeatButton`/`IncreaseRepeatButton`/`Thumb` rather than
  replacing the whole template, so drag/click-to-set behavior keeps
  working unmodified.
- **ComboBox**: WPF's default `ComboBox` popup renders as a plain
  white/light list with black text **regardless of the app's own
  theme** — this was (half of) the "black text on grey" report. Fully
  re-templated (implicit style): a dark toggle button with a small
  chevron `Path` instead of the default arrow glyph, and a dark
  `Popup`/`Border`/`ComboBoxItem` list with a hover-highlight and a
  selected-item accent color.
- **CheckBox**: the other half of "black text on grey" — WPF's default
  `CheckBox` template doesn't reliably inherit the window's `Foreground`
  for its label text, rendering it near-black regardless of theme.
  Fully re-templated (implicit style): a small rounded square that
  fills with the accent gradient and shows a check `Path` when checked,
  with the label text explicitly bound to the window's text color via
  `TextElement.Foreground="{TemplateBinding Foreground}"` on the
  template's root panel.
- **Buttons** (`BtnGhost`/`BtnAccent` styles): re-templated with real
  rounded corners (`CornerRadius="10"`, matching `gui/style.css`'s
  `.btn`) and a hover state that highlights the border in the accent
  color, replacing the flat unstyled-corner look.
- **Preset cards**: rebuilt to match `gui/app.js`'s actual UX, which
  this port had gotten wrong — Python's preset card is **entirely
  clickable to apply**, with only a small circular "×" in the corner
  as a separate click target for delete; this port instead had two
  separate, unstyled system-default buttons ("Apply" text + "✕") side
  by side inside the card, which is exactly what read as "the apply
  and X... look stupid." Fixed: the card `Border` itself now has
  `MouseLeftButtonUp="PresetCard_Click"` (`MainWindow.xaml.cs`) and a
  hover-accent-border trigger; the delete button is a small styled
  circle (`PresetDeleteButtonStyle`) that turns red on hover. **No
  manual "did they click delete instead" guard was needed** — a real
  WPF `Button`'s `Click` marks the underlying mouse-up `RoutedEvent`
  handled before it bubbles to the card's own `MouseLeftButtonUp`
  handler, so clicking delete never also triggers apply, for free.
  `PresetRow` gained an `Effect` field (shown as a small subtitle on
  the card, matching Python's card layout) — `RefreshPresetsAsync`
  populates it from the already-fetched preset dict.

**User confirmation, one round of feedback at a time, not all at
once**: reported a checkbox-label contrast issue via screenshot AFTER
the first theming pass landed ("looks good now, except you still have
black text on a grey background") — this was the `CheckBox` fix above,
added and confirmed separately from the rest. Final confirmation after
that: "I looked at it and it is now correct."

### Window behavior fixes — DONE, user-confirmed

Three small, unrelated requests handled together since they all touch
`MainWindow`'s startup behavior:

1. **Top-centered window placement**, ported from `gui.py`'s
   `_initial_position()` (its own comment: the OS/toolkit default
   placement left the window too low, needing a manual drag up every
   time it opened). Set in `MainWindow`'s constructor, right after
   `InitializeComponent()`:
   `WindowStartupLocation = WindowStartupLocation.Manual;`
   `Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - Width) / 2);`
   `Top = 0;`. WPF's `SystemParameters` are already DPI-independent, so
   none of `gui.py`'s empirical pixel-nudging (needed there to correct
   for `GetSystemMetrics`' raw/DPI-virtualized coordinate mismatch) was
   necessary here. Verified live via `GetWindowRect` on the real
   window: `Top=0`, `Left` matched the centering formula exactly.
2. **Single-instance enforcement**, ported from `gui.py`'s
   `_acquire_single_instance_lock()`/`_focus_existing_instance()`. New
   `App.xaml.cs` (previously just the empty generated stub):
   `OnStartup` creates a named `Mutex` (`"JmaStudioGui_SingleInstance"`
   — deliberately a different name from Python's own
   `"JMAStudioGUI_SingleInstance"` mutex, since the two apps are
   independent and switching between them is a supported workflow, not
   something that should block on the other's lock); if the mutex
   already existed, finds the existing window by exact title via
   `FindWindow` (P/Invoke), restores it if minimized and calls
   `SetForegroundWindow`, then `Environment.Exit(0)` before any
   `MainWindow` gets created. **Same subtle gotcha `gui.py`'s own
   comment documents, ported deliberately, not rediscovered**: the
   `Mutex` instance is stored in a field on `App`, not a local variable
   inside `OnStartup` — an unreferenced `Mutex` is eligible for GC
   finalization (which releases the underlying OS mutex) almost
   immediately, which would silently defeat the whole check for any
   later launch. Verified live: launched a second copy while the first
   was running — it exited immediately with no duplicate window, and
   the process count stayed at 1.
3. **Touchpad two-finger scroll was much too fast.** Root cause:
   precision-touchpad drivers report a per-event scroll wheel `Delta`
   scaled to swipe speed (often far larger than a physical wheel's
   fixed 120-per-notch), and WPF's default `ScrollViewer` mouse-wheel
   handling scales scroll distance directly by that delta — a real
   mouse wheel notch isn't affected by this at all, only touchpad's
   inflated values are. Fixed with a `PreviewMouseWheel` handler on the
   main content `ScrollViewer` (`MainScrollViewer` in
   `MainWindow.xaml.cs`) that computes the usual line-based scroll
   amount (`delta/120 * SystemParameters.WheelScrollLines` lines) but
   clamps it to a max of 3 lines per event before applying it via
   `ScrollToVerticalOffset` — a real wheel notch's normal 3-line scroll
   passes through unchanged (clamped to the same value it already was),
   only touchpad's oversized per-event deltas get tamed. User
   confirmation: "You got it right on the nose."

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
- Solution currently has 5 projects: `JmaStudio.Hardware` (protocol —
  `Keyboard.cs`, `Lightbar.cs`, `Controller.cs`, `LightbarDiagnostics.cs`),
  `JmaStudio.Effects` (all 22 effects + `IEffect`/`EffectRegistry`),
  `JmaStudio.Presets` (atomic store + Python migration),
  `JmaStudio.Service` (the ASP.NET Core daemon/service), and
  `JmaStudio.HardwareTest` (the console app). No tests project yet —
  "testing" so far means the console app / real HTTP calls against real
  hardware, by design (see Phase 2/3/4/5's stated purpose).
- **Run the actual service** (from `windows/`):
  `dotnet run --project src/JmaStudio.Service` — runs as a plain console
  app (Ctrl+C to stop) since no OS service registration exists yet
  (Phase 7). **Requires Administrator** (stops/disables
  `AcerLightingService` and drives the lightbar). Binds to
  `http://127.0.0.1:8420` — **stop the Python daemon first** if it's
  running (same two-processes-fighting-over-the-keyboard problem noted
  below applies to the real service too, not just `effect-live`).
  Defaults to `windows/data/` and the repo-root `keymap.json`; override
  with the `JMASTUDIO_DATA_DIR`/`JMASTUDIO_KEYMAP_PATH` environment
  variables. See "Phase 5" above for the full endpoint list
  (`/status`, `/effect`, `/presets*`, `/lightbar/*`,
  `/controller-reactive/*`).
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
- **Run the GUI** (from `windows/`, no elevation needed — it never
  touches hardware directly, only the Service's HTTP API):
  `dotnet run --project src/JmaStudio.Gui`. **The Service must already
  be running** (see above) or it'll show "service unreachable."

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
   plumbing this phase provides — **DONE**, see "Phase 5" above
6. WPF GUI: replicate existing UX, add Create Preset + dominance-
   reassert button — **main window DONE** (live preview, presets, quick
   effects, all 3 tuning panels, full theming, window behavior — see
   "Phase 6" and both "Phase 6 continued" sections above). **Remaining**:
   the Lightbar window, Controller Reactive window, and Diagnostics
   window (currently `MessageBox` placeholders) — the user's explicit
   next-up plan, one at a time. The "Create Preset" flow and the
   Diagnostics dashboard's emergency-action row (dominance-reassert +
   switch-to-Python/switch-to-C# buttons) described under settled
   decision #11 are part of this remaining work, not yet built.
7. Installer (location prompt, consent notice, service registration,
   GUI autostart, preset data migration) — **explicitly deferred until
   Phase 6's 3 remaining windows are done**, not scheduled ahead of them
   (the user asked directly whether installer or polish comes next;
   answer given and accepted: finish Phase 6 first so the installer
   ships something complete).

## Immediate live state as of writing this (2026-09-10, end of third Phase 6 session)

**This section supersedes every "immediate live state" note above it in
this file — only trust this one.**

- **The C# `JmaStudio.Service` is running and is the one actually
  driving the user's real hardware right now** — elevated console
  process, PID 10592 at the time of writing (find it fresh via
  `Get-NetTCPConnection -LocalPort 8420`), started earlier this session
  after the user asked to switch back from Python. Confirmed live via
  `GET /status`: `keyboardConnected: true`, `currentEffect:
  "typing_reactive"` with the real "Red Chase" preset's params —
  correct, not a fallback state. `controllerConnected: false` at the
  time of writing (the DualSense simply wasn't connected/powered on
  during this session — not a bug, nothing to investigate).
- **`JmaStudio.Gui` is NOT currently running** — the user closed the
  window at some point after the last round of live-testing in this
  session (touchpad scroll fix). This is expected/fine: the Service
  owns the hardware independently of whether the GUI is open. To
  relaunch it: `dotnet run --project src/JmaStudio.Gui` from `windows/`
  (no elevation needed, single-instance-enforced now — see below).
- **The Python stack is fully stopped** — was running at the start of
  this session (the "JMA Studio Autostart" scheduled task had re-fired
  after a reboot/re-login since the previous session), stopped
  deliberately by identifying and killing its exact 4-process tree
  (parent PowerShell → uvicorn launcher → uvicorn worker bound to 8420,
  and separately tray.py → its own child) before starting the C#
  service, per the same two-processes-fighting-over-hardware caution
  used every other time this project has switched stacks.
- **This session's work (3 tuning panels, full theming pass, window
  behavior fixes — see "Phase 6 continued: tuning panels..." above) is
  being committed at the end of this session**, per the user's explicit
  request ("fully update the handoff... the commit everything"). Check
  `git log` on `csharp-port` to confirm this actually happened rather
  than trusting this note alone — this file could theoretically be read
  before that commit lands. The 5 modified + 5 new files are listed at
  the top of this session's `git status` output; no separate manual
  list is maintained here since committing right after writing this
  section makes one immediately redundant.
- **The user is about to update the Claude Code extension in VS Code**
  before continuing — if a fresh session picks this up, that update
  already happened; no action needed regarding it.
- **Explicit, settled next-up plan directly from the user**: "we are
  going to tackle the lightbar, Reactive controller, and diagnostics
  windows one at a time." Not "whichever seems easiest" or "figure out
  a good order" — the order as stated is Lightbar, then Controller
  Reactive, then Diagnostics, one at a time (build + verify live one
  fully before starting the next, same standard this whole project has
  held to throughout). Don't re-propose a different order without the
  user raising it first.
- **Start here next time, in this order**: (1) read this whole "Phase 6
  continued: tuning panels..." section above before writing any new GUI
  code — the `ColorSwatchButton`/`Debouncer`/`_uiReady`/
  `_suppressLiveApply`/implicit-dark-control-style patterns established
  there are meant to be reused for the 3 new windows, not reinvented;
  (2) confirm current live state fresh (service/GUI/Python process
  status, current effect) rather than trusting this note blindly, since
  time may have passed; (3) start the Lightbar window — it's simplest
  of the 3 (no new hardware-state concepts, the Service's
  `/lightbar/*` endpoints are all already built per "Phase 5" above),
  build it, verify it live against the real lightbar, get explicit user
  confirmation, THEN move to Controller Reactive, THEN Diagnostics —
  don't build more than one window ahead of live verification.
