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

**Phases 1-6 — DONE. Phase 6.5 (tray icon) — DONE. Phase 7 (installer)
— FUNCTIONALLY COMPLETE as of the tenth session.** Read **"Phase 7:
Installer"** below (right before "Key technical decisions") for the
full story: a real, previously-unknown architecture problem (Session 0
isolation breaks `typing_reactive`/lightbar-reactive when the Service
runs as a real Windows Service), found via live installer testing, then
fixed in an eighth session by moving key capture into `JmaStudio.Gui`
and forwarding over a new `POST /keypress` endpoint, user-confirmed
working live ("I just tested your live update, it works" — see "Fix
built and verified (2026-09-10, eighth session)"). A ninth session
found and fixed 2 real uninstall bugs (not yet re-verified) and hit one
backlogged theming bug (3 fix attempts failed, do not re-attempt
without new evidence). A tenth session tested real sleep/hibernate/
restart resilience: sleep passed clean, hibernate found one WON'T-FIX
cosmetic gap (user declined the fix), and restart passed clean —
closing out the last open question with the user's own unprompted
observation that boot-time lighting now beats PredatorSense's own
timing ("the keyboard and backlight background colors pickup faster
th[a]n the predator sense software ever did... GOOD JOB"). See
"Immediate live state" at the end of this file for the precise
remaining (non-blocking) loose ends. An eleventh session then found and
fixed three more real installer bugs via live testing (Python's
pre-existing autostart task silently reasserting control after an
uninstall, and a leftover-empty-directory bug) — all confirmed working,
committed, and pushed; the C# port is also now merged into `main` and
published as a public GitHub release. **A twelfth session ran a full
live Python-vs-C# resource-overhead benchmark** (see "Phase 7
continued: Python vs. C# resource overhead benchmark" below) and then
designed — but explicitly did NOT build — **Phase 8: an idle
screensaver + low-battery lighting override, with V2-scope multi-effect
cycling and randomization**. Read **"Phase 8"** below before touching
either feature: the full design is final and user-confirmed, but the
user was explicit that this is planning only — **do not write any
implementation code for Phase 8 without an explicit go-ahead**, even if
resuming cold on this file. **A thirteenth session then actually built
and shipped Phase 9**: a full live-tuned rework of the `rain` effect
(bug fix, fade-in, time-varying shower intensity, a new "accent drop"
feature, and a reusable `EffectContext.EffectStartTime` addition) — see
"Phase 9" below. Committed locally but deliberately NOT pushed to
GitHub yet, per the user's explicit instruction to bundle it with
Phase 8's V2 push later — check `git status`/`git log` against `origin`
before assuming what's actually public. **A fourteenth session then
built Phase 8's Feature 1 (idle screensaver)** — the user gave the
explicit go-ahead to start V2, beginning with this feature specifically
("start with the screen saver"). Fully built, redeployed live several
times, and live-tuned with 3 follow-on requests (ListBox theming fix,
green enabled-checkmarks on the top bar, a "Lights Out" sentinel option)
— see "Phase 8"'s "Feature 1 -- BUILT" subsection below. Features 2
(low-battery override) and 3 (controller hot-discovery) remain design-
only; the go-ahead was specifically for the screensaver, not all of V2
at once — don't assume it extends further without being told again.
Before all that, read "Phase 6.5:
Tray icon (2026-09-10, sixth session)" for the tray icon scope
addition, a real crash bug found and fixed live, a startup-behavior
change (the checkmarked default preset now applies on every boot, not
just a fresh install), and one explicitly backlogged gap (keyboard
sleep/hibernate resilience — since directly tested in the tenth session,
see "Immediate live state" for the resolution; do not reopen without
the user raising it).
Every one of Phase 6's 4 windows (main, Lightbar, Controller Reactive,
Diagnostics) plus the Phase 6.5 tray icon are all built and
user-confirmed working. Real hardware control confirmed live for
keyboard, lightbar, AND controller. **Check "Immediate live state" at
the very end of this file for exactly what's running right now** —
don't rely on any of the older "immediate live state" language earlier
in this file, only the bottommost section is current.

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
- [x] WPF GUI — all 4 windows DONE. Main window, **Lightbar window**
      (one backlogged known issue, see "Phase 6 continued: Lightbar
      window" below — do not attempt to fix without new evidence),
      **Controller Reactive window**, and **Diagnostics window** (see
      "Phase 6 continued: Diagnostics window" below for the full-
      dashboard build: emergency-action row, hardware/service health
      tiles, live mini preview, perf stats, self-tests, logs panel).
      User-confirmed working end to end.
- [x] Tray icon (`TrayIconManager.cs`) — a scope addition raised
      mid-session, not in the original settled decisions. See "Phase
      6.5: Tray icon" below for the full design, a real crash bug found
      and fixed live, and a startup-behavior change (default preset now
      applies on every boot). One item explicitly backlogged (keyboard
      sleep/hibernate resilience).
- [x] Installer (Inno Setup, `windows/installer/`) — packaging, service
      registration, data seeding, and uninstall all built and
      live-tested working. The critical bug that was blocking
      completion — Session 0 isolation breaking `typing_reactive`/
      lightbar-reactive when the Service runs as a real Windows Service
      — is **fixed and user-confirmed live** ("I just tested your live
      update, it works"), see "Phase 7" below, "Fix built and verified
      (2026-09-10, eighth session)". Real cold-boot timing confirmed in
      the tenth session (better than PredatorSense's own timing).
      **Functionally complete**; two small non-blocking items remain
      (uninstall fix re-verification, one backlogged theming bug) — see
      "Immediate live state" at the end of this file.

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
  `JsonStore` in `JmaStudio.Presets`. **Updated 2026-09-10 (Phase 6.5)**:
  originally this persisted state was also read back and preferred at
  startup (only falling back to `AppConfig.DefaultPreset` on a true
  first-ever boot); that was changed so the configured default is now
  applied unconditionally on every boot, matching the actual Python
  reference's behavior -- see "Phase 6.5: Tray icon" above for the full
  story. `LiveKeyboardState` is still written on every `SetEffect()`
  call, just no longer consulted at startup.
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

## Phase 6 continued: Lightbar window (2026-09-10, fourth session)

Built the Lightbar window (`LightbarWindow.xaml`/`.xaml.cs` +
`.Presets.cs`/`.Reactive.cs` partials), the first of the 3 remaining
Phase 6 windows the user explicitly asked to tackle one at a time
(Lightbar → Controller Reactive → Diagnostics). Full parity with
`gui/lightbar.html`: Static/Dynamic mode toggle, zone selector + color
wheel (`ColorWheelPicker.cs`, a from-scratch HSV wheel since WPF has no
`<input type="color">` equivalent) + value slider + RGB fields, saved
swatches, presets (reusing `MainWindow`'s whole-card-click pattern),
and the "Reactive (keyboard -> lightbar)" section. One visual
simplification kept from the plan (disclosed to the user, not silently
dropped): the CSS mask crossfade between the 3 illustration zones was
replaced with a plain 3-way crop, no soft blend at the seams --
everything else, including the actual recolored bar photo
(`LightbarIllustration.cs`, live per-pixel HSV hue-rotation of
`Assets/lightbar_bar.png`), is full parity.

**New Service backend, since the reactive flash loop was explicitly
NOT built in Phase 5**: `LightbarReactiveManager.cs` (a
`BackgroundService`, ~12.5Hz tick matching `daemon/server.py`'s own
`_REACTIVE_TICK`), a non-persisted `LightbarController.FlashZones()`
passthrough, and 3 new endpoints (`GET/POST /lightbar/reactive`,
`POST /lightbar/reactive/capture_zones`) ported from
`daemon/server.py`'s equivalents. The migrated `lightbar_reactive.json`
already had `Enabled: true` from the user's real Python-era config, so
this started actively flashing the instant the loop existed --
confirmed working, not a bug.

**Two real WMI performance bugs found and fixed, both in
`Lightbar.cs`, both via live measurement, not guessing**: (1)
`FindInstance()` re-ran a full `ManagementObjectSearcher` WQL query on
every single Set* call (5 of them per `FlashZones`), and
`GetMethodParameters()` re-fetched the method's CIM schema every call
too -- together these made a single `FlashZones()` call take
83-102ms against an 80ms tick budget, so the reactive loop could
never keep up with real typing. Fixed by caching the resolved
`ManagementObject` instance and a per-method parameter template
(cloned per call to stay safe once calls run concurrently -- see next
item), dropping this to a consistent 60-69ms. (2) The 3 per-zone
`SetGamingRgbKb` writes inside `FlashZones()` were sent sequentially,
leaving a real ~25-30ms gap between when zone 1's color landed on the
physical hardware and when zone 3's did -- now fired via
`Parallel.ForEach`, confirmed safe by the same thread-affinity probe
`LightbarDiagnostics` already proved months earlier in this project.

**KNOWN UNRESOLVED ISSUE, explicitly backlogged, not fixed** -- the
user's own words after testing both fixes live: "it still having
issue trying to fire all three zones together... I don't want to be
stuck in debugging hell right now," and explicitly asked to backlog
this and move on rather than keep digging. **Symptom**: triggering an
all-zone flash (keyboard zone 4) still shows zone 1 visibly out of
sync with zones 2/3 on the real hardware, even after both perf fixes
above (which did measurably help the *overall* lag, per the user's
own earlier "little lag... always going to be" comment, but did NOT
fix this specific desync). **Not yet investigated**: whether
`Parallel.ForEach`'s thread-pool scheduling itself introduces enough
jitter to explain a *specific, consistent* zone-1 lag (rather than
random which-zone-lags-most behavior, which is what pure scheduling
jitter would predict) -- if it's consistently zone 1 specifically,
that points at something about zone 1's mask/position in the
protocol rather than generic concurrency timing, and is worth
re-examining with real evidence (e.g. per-zone timestamps around each
`SendRgbKb` call) before trying another fix blind. **Do not re-attempt
a fix without new evidence** -- two targeted fixes already landed
here based on real measurement and neither fully solved it; a third
guess without profiling first would repeat the exact mistake this
project's own standing practice exists to avoid.

### Controller Reactive window — DONE, same session

Second of the 3 remaining windows. Simpler than Lightbar (no canvas
widgets) -- ported from `gui/controller_reactive.html` as
`ControllerReactiveWindow.xaml`/`.xaml.cs`: the master "Enabled"
toggle (calls the existing Phase 5 `ControllerReactiveManager.Enable/
Disable()`), Background enable+color, Left/Right Stick idle/tier1/
tier2 swatches, a shared Deadzone slider, and 4 button-group panels
(Face/D-Pad/Shoulders & Triggers/Paddles & Fn -- 16 individual button
swatches total, built in code from a fixed `(key, label)` table per
group, matching `gui/app.js`'s `buildGroupGrid`). Live-apply is
debounced (150ms, matching the Python throttle) through the existing
`POST /controller-reactive/settings`.

**Two small Service additions, needed by this window and nothing
before it** (per Phase 5's own "NOT built" list, which flagged these
as deferred until a GUI needed them): `GET /controller-reactive/
defaults` (the "Default" button's target values, mirroring
`daemon/server.py`'s own endpoint -- built from `ControllerReactiveParams`'
own record defaults rather than hardcoding a second copy of the
numbers) and a `connected` field added to the existing `GET
/controller-reactive/status` response (`Controller.IsConnected`,
previously only `enabled` was reported). `Endpoints.MapControllerReactive`
and `Program.cs`'s call site both took a new `Controller?` parameter
for this.

**Verified live**: settings load/save/live-apply, the Enable/Disable
toggle taking over the keyboard, and the "Default" button all
exercised against the real DualSense (connected via USB during this
session -- `connected: true` confirmed via the new status field).

### Window placement + owner-focus fixes — DONE, same session

Two behavior bugs reported after opening/closing the new windows a
few times, both fixed with new shared helpers rather than duplicated
per-window:

- **New windows opened partly off-screen**, needing a manual drag-up
  and resize every time. `LightbarWindow`/`ControllerReactiveWindow`
  both declare a fixed `Height="900"` in XAML, which can exceed a
  shorter display's actual usable height -- `MainWindow`'s own
  top-centered placement logic (added in an earlier session) didn't
  clamp height at all, and the two new windows never had it applied.
  Fixed with a new shared `WindowPlacement.PlaceTopCentered(Window)`
  (`WindowPlacement.cs`) that clamps `Height` to
  `SystemParameters.WorkArea.Height` (the real usable area, excluding
  the taskbar -- `MainWindow`'s original version used
  `SystemParameters.PrimaryScreenWidth`/a literal `Top = 0`, neither of
  which account for the taskbar) before positioning. `MainWindow`
  itself was retrofitted to call this too, replacing its own inline
  version, so all 3 windows now share one implementation.
- **Closing Lightbar/Controller Reactive dropped the main window
  behind unrelated apps** (the user's own example: VS Code below Main
  Window below Lightbar -- closing Lightbar left VS Code on top,
  skipping over Main Window entirely). WPF does not automatically
  reactivate a window's `Owner` when an owned window closes; without
  an explicit `Activate()` call, the OS's next-focused window is
  whatever was behind the closing window in the overall Z-order, which
  isn't necessarily the owner. Fixed with
  `WindowPlacement.ReactivateOwnerOnClose(Window)`, which just wires
  `Closed += (_, _) => window.Owner?.Activate();` -- called once from
  each owned window's constructor (`Owner` doesn't need to be set yet
  at that point since the lambda reads it lazily at close-time, well
  after the caller's `{ Owner = this }` object-initializer has run).

### App icon + logo branding — DONE, same session

User asked to incorporate the icons already made for the Python
version (`gui/app_icon.ico`, `gui/logo.png`) rather than leave the WPF
app with no branding. Copied both into
`windows/src/JmaStudio.Gui/Assets/`, registered in the `.csproj` two
ways: `<ApplicationIcon>Assets\app_icon.ico</ApplicationIcon>` (embeds
into the compiled `.exe`'s own Win32 resources -- shows in Explorer/
taskbar/Alt-Tab even before any window opens) AND as a `<Resource>`
item (so it's also loadable via `pack://application:,,,/Assets/
app_icon.ico` for each `Window.Icon` -- a different embedding
mechanism than `ApplicationIcon`, needed separately). All 3 windows'
XAML root elements now set `Icon="pack://application:,,,/Assets/
app_icon.ico"`. `MainWindow`'s topbar, which previously showed a plain
`TextBlock Text="JMA Studio"`, now shows the actual `logo.png` image
instead (matching `gui/index.html`'s own topbar, which has never had a
separate text label next to its logo). Went through a few live
size iterations with the user (28px → a combined logo+"Studio"-text
version the user disliked and asked reverted → logo-only at 36px →
final: **48px**, logo image alone, no adjacent text).

## Phase 6 continued: Diagnostics window (2026-09-10, fifth session)

Built the Diagnostics window (`DiagnosticsWindow.xaml`/`.xaml.cs` +
`.SelfTest.cs` partial) — the last of the 4 Phase 6 windows. User chose
"Full dashboard" (the largest of 3 scope tiers offered) over just the
emergency-action row settled decision #11 requires, so this includes
every item from that decision's "proposed additional dashboard content"
menu: hardware/service status tiles, a live mini preview, perf stats,
self-tests + a live controller viewer, and a logs panel — confirmed
working end to end by the user ("working, let's move on").

**Design decision worth re-reading before touching the emergency-action
row**: the 3 buttons (re-assert dominance / switch to Python / switch
to C#) do **NOT** call the running Service's HTTP API. They launch as
separate one-shot elevated processes instead — new
`reassert-dominance`/`switch-to-python`/`switch-to-csharp` commands
added to `JmaStudio.HardwareTest` (the existing console tool from
Phase 2), invoked from the GUI via `Process.Start` with `Verb="runas"`.
Reasons, in `DiagnosticsManager.cs`'s own header comment:
1. Settled decision #11 says all 3 need the UAC shield icon convention,
   meaning each click must show a REAL elevation prompt. Routing
   through the already-elevated Service would never prompt at all,
   making the shield icon a lie.
2. `switch-to-python` needs to end by killing the very Service process
   that would otherwise be handling the HTTP request for it.
3. `switch-to-csharp` needs to work even when the Service isn't running
   at all (Python is the currently active stack) — there'd be nothing
   to call.

`JmaStudio.HardwareTest` needed a new `System.ServiceProcess.
ServiceController` package reference for this (small intentional
duplication of `AcerLightingServiceManager.StopAndDisable()` rather
than referencing the whole ASP.NET Core `JmaStudio.Service` project
just for ~15 lines). `switch-to-python`/`switch-to-csharp` find/kill
the other stack's processes via a `Win32_Process` `CommandLine` search
(same technique `start_all.ps1` already uses for its own tray-icon
detection), flip the "JMA Studio Autostart" Scheduled Task's
enabled/disabled state in the direction that makes sense, then launch
the target stack. **Known limitation, not fixed**: since Phase 7's
installer doesn't exist yet, `switch-to-csharp` falls back to the same
`dotnet run` dev commands documented under "How to build / run / test"
— once Phase 7 ships a real installed `.exe`, this should launch that
instead (there's a comment marking exactly where). Also: because the
whole command runs elevated (needed for the scheduled-task/process
changes), the C# GUI it launches inherits that elevation too, unlike a
normal unelevated launch — acceptable for now, worth fixing when the
real installer's autostart entry exists.

**Emergency-action gating** happens in the GUI, not the Service, so it
still works when the Service is unreachable (Python might be the
active stack): `DetectActiveStackLocally()` in
`DiagnosticsWindow.xaml.cs` runs its own `Win32_Process` `CommandLine`
scan (needed a new `System.Management` package reference on
`JmaStudio.Gui`, which had none before) to tell which stack is
currently running, independent of the Service. Python install detection
comes from the Service's own `/diagnostics/status` when reachable, with
a local filesystem-based fallback
(`DetectPythonInstalledLocally()`) when it isn't. **Real bug found and
fixed before the user tested this**: the WMI scan was originally called
synchronously on the UI thread on every 2-second poll tick — moved
behind `Task.Run` so a slow `Win32_Process` enumeration on a busy
machine can't cause a visible micro-freeze while the window is open.

**New Service-side pieces this window needed, none of which existed
before**:
- `DiagnosticsManager.cs` (new) — bundles hardware/service health into
  one `GET /diagnostics/status` payload (keyboard/lightbar/controller
  connected+detected state, `AcerLightingService` status/start mode,
  a best-effort "suspicious process" check for `OpenRGB`/
  `PredatorSenseService` — deliberately NOT plain `PredatorSense`, the
  ordinary companion app, since settled decision #10 already confirmed
  live that having it open does nothing while `AcerLightingService`
  stays stopped; flagging it would just be a false alarm every time the
  user has it open for unrelated reasons — this exact mistake was made
  and caught via a live curl check before the user ever saw it), Python
  install/scheduled-task detection via `schtasks.exe`, and the frame/
  latency perf counters. Also implements the two non-destructive self-
  tests (`TestKeyboardAsync`/`TestLightbarAsync`) and the presence-only
  `Rescan()` — explicitly NOT a true hot-reconnect (the
  keyboard/lightbar/controller handles opened once at `Program.cs`
  startup aren't rebuilt live; that would need those locals behind a
  mutable holder, a bigger refactor not justified yet).
- `SelfTestGate.cs` (new) — a shared flag `RenderLoopService` checks
  every tick so `TestKeyboardAsync`'s direct `SetStaticColor`/
  `SendFrame` calls never interleave raw HID writes with the render
  loop's own (`Keyboard`'s own header comment already documents it's
  "not thread-safe by itself, callers serialize access" — this is
  exactly that serialization, applied for the first time since a
  Diagnostics self-test is the first caller that writes to the keyboard
  from outside the render loop).
- `FileLoggerProvider.cs` (new) — the Service had **no persistent log
  output at all** before this; the Logs panel needed something to
  tail. A minimal custom `ILoggerProvider` (no Serilog dependency),
  truncated once per service start (same "always reflects the most
  recent run" convention `start_all.ps1`'s own log files already use on
  the Python side), shared by every `LoggerFactory` in `Program.cs` so
  everything ends up in one file (`data/logs/service.log`).
- `LatencyStats.cs` (new, in `JmaStudio.Hardware`) — a tiny min/avg/max/
  count counter, instrumented into `Keyboard.SendFrame` (HID) and
  `Lightbar`'s `CallMethodOn` (WMI, excluding the `Commit()` rounds' own
  intentional 65ms sleeps so they don't drown out the real overhead).
  Confirmed live: HID averages ~10ms, WMI averages ~19ms per call —
  consistent with the 60-69ms/3-calls `FlashZones()` figure already
  measured earlier this session.
- `Lightbar.IsPresent()`/`Controller.IsPresent()` (new static presence-
  only probes, no elevation needed for either) — power the Rescan
  button and the status tiles' "detected but not open" distinction.

**Verified live**: hardware/service status tiles, perf stats, mini
preview, and logs panel all confirmed against the real running Service
(`GET /diagnostics/status` curl-checked directly before the user ever
opened the window, catching the `PredatorSense` false-positive early).
The emergency-action row's UAC-prompt launch mechanism was built and
reviewed but the 3 commands were **not** live-tested end to end this
session (too disruptive to trigger `switch-to-python`/`switch-to-csharp`
against the user's real active hardware mid-session without a specific
reason to) — the user confirmed the window overall ("working, let's
move on") without singling out those 3 buttons. If a future session
needs to verify them, do it deliberately, with the user watching, not
as a casual check.

## Phase 6.5: Tray icon (2026-09-10, sixth session)

**Not in the original settled decisions** — the user explicitly flagged
this mid-session, after Phase 6 (all 4 windows) was already confirmed
done: "We need to work on the tray icon first before installer, I don't
think that was ever mention in the original port prompts." Correct: it
wasn't. Python's `tray.py` (a system tray icon showing saved presets,
launched automatically by `start_all.ps1` alongside the daemon) had no
C# equivalent at all before this. Important consequence for Phase 7:
Python's `start_all.ps1` only autostarts the daemon + tray icon, NOT the
full `gui.py` window — the window opens on demand from the tray. The
Phase 7 installer's GUI autostart entry should target whatever ends up
launching the tray (this session's `JmaStudio.Gui.exe` itself, per the
architecture decision below), not assume the main window should be
autostarted directly.

**Architecture, per the user's explicit answers**:
1. **Same process as the rest of `JmaStudio.Gui`**, not a separate
   `JmaStudio.Tray` project (unlike Python's tray.py/gui.py split) —
   simpler, and `MainWindow` already exists as a single long-lived
   instance (`StartupUri`-created) the tray can just Show/Hide/Activate
   directly, no second process to coordinate.
2. **Left-click opens the Studio window; no duplicate "Open Control
   Panel" item in the right-click menu.**
3. **No "Quit" item at all.** The user's own words: "if the tray is gone
   the services aren't running." The ONLY way to make the tray icon (and
   the Service) go away is "Close and End Service" (see below).
4. **Closing MainWindow's X button minimizes to tray** (hides the
   window, keeps the process/tray/Service running) rather than exiting.

**New file `TrayIconManager.cs`** (`JmaStudio.Gui`) — owns a
`System.Windows.Forms.NotifyIcon` (WPF has no tray-icon primitive of its
own; same WinForms-interop pattern `ColorSwatchButton.cs` already
established, including its convention of fully-qualifying
`System.Windows.Forms`/`System.Drawing` types inline rather than
aliasing, since both have their global usings removed project-wide).
Context menu, top to bottom: **Keyboard Presets** submenu, **Lightbar
Presets** submenu (both dynamically rebuilt every 5s via a
`DispatcherTimer` — a real improvement over Python's tray.py, which only
ever listed keyboard presets and read them once at startup, requiring a
restart to see new ones), **Controller Reactive** (a checkable toggle
calling the existing Phase 5 enable/disable endpoints), **Off** (kept
from Python, keyboard only), **All White** (new sibling to Off — same
scope, opposite color, `static` effect at RGB 255,255,255), **Close and
End Service**.

**"Close and End Service"** pushes a hardcoded final visual state, then
gracefully shuts the Service down, then exits this GUI process:
- Keyboard: a hardcoded `GradientParams` object, deliberately NOT
  applied by calling the real "gradient_only" preset by name — the
  user's own words: "I want you to hard code this not have it call the
  preset as if it gets deleted that could cause problems." Values
  transcribed verbatim from the real migrated `gradient_only` preset
  (`windows/data/keyboard-presets.json`): `LeftColor` RGB(20,90,230),
  `RightColor` RGB(200,20,160), `Boundary` 13.5, `Hard`=true,
  `LeftOverrides`=[backspace,del,f11,f12,ins,prtsc],
  `RightOverrides`=[backslash,enter,left_arrow,right_ctrl,right_shift],
  `CustomColors`={space: RGB(79,131,236)}, `Brightness` 0.65.
- Lightbar: all 3 zones to the user's own explicit RGB(18,46,255) at
  brightness 100 (NOT the real "BLUE" preset's colors, which turned out
  on inspection to not even be uniform across zones — zone 2 is a
  distinct cyan).
- Then `POST /system/shutdown` (new endpoint, `Endpoints.MapSystem` in
  `JmaStudio.Service`) — the GUI runs unelevated and the Service runs
  elevated, so a graceful HTTP-triggered self-shutdown
  (`IHostApplicationLifetime.StopApplication()`, delayed 300ms so the
  response finishes flushing first) is the only realistic way for the
  GUI to end the Service; Windows won't let a lower-integrity process
  terminate a higher one directly.
- **Bug found and fixed before this shipped**: all 3 steps above
  originally shared one try/catch block, so a failure partway through
  (confirmed live: the Service went down mid-sequence once, see below)
  silently skipped every step after the failure point — the keyboard
  gradient landed but the lightbar calls never even ran. Fixed by giving
  each step its own independent try/catch (`SafeCall`), so every step
  gets a real attempt regardless of whether an earlier one failed.

**Real crash bug found and fixed live, unrelated to the tray icon
itself but massively more consequential once the tray icon existed**:
`Debouncer.cs` (used by every tuning panel's live-apply across the whole
GUI — Gradient, Reactive Typing, Custom Key Colors, Lightbar zone/
brightness/reactive-config, Controller Reactive settings) took an
`Action fire` parameter. Every real call site passes an `async () =>
await ...()` lambda, which compiles to unsafe `async void` against an
`Action` parameter — any exception thrown inside (e.g. the Service going
unreachable mid-session) cannot be caught by the caller and crashes the
entire process via the WPF Dispatcher. Hit live: the user closed the
Service's own console window directly (see "known rough edge" below),
and a still-open Controller Reactive window's live-update timer then hit
the dead Service and took the whole GUI down — tray icon included, which
is what actually prompted the "I closed JMA studio and it killed the
tray icon" report. **Fixed at the root**: changed `Debouncer`'s `fire`
parameter from `Action` to `Func<Task>` — the exact same lambda syntax
at every call site compiles to a properly awaitable `Task` against that
type, so zero call sites needed to change; `Debouncer` now awaits `fire()`
inside its own try/catch. Also added a global
`Application.DispatcherUnhandledException` handler in `App.xaml.cs` as a
backstop for anything this doesn't cover — logged to Debug output only,
since there's no guaranteed window open to show a toast in.

**Known rough edge, not fixed (inherent to this phase, not a bug)**:
until Phase 7 registers a real Windows Service, the Service runs in a
visible elevated console window (`dotnet run --project src\JmaStudio.Service`,
launched by hand or by this session's restart scripts). Closing that
console window directly kills the Service outside the tray's control —
the ONLY foolproof way to end it is the tray's "Close and End Service".
This will stop being an issue once Phase 7 gives the Service a real,
windowless OS service registration.

**Default-preset checkmark (same session, follow-on request)**: the
user asked for a visible marker on whichever keyboard preset card is
currently the startup default, in `MainWindow`'s Presets panel (main UI
only, not `LightbarWindow`'s lightbar presets, confirmed explicitly).
Added `ApiClient.GetDefaultPresetAsync()` (mirrors the existing
`GetLightbarDefaultPresetAsync()`), `PresetRow` gained an `IsDefault`
bool, and a green checkmark (`SuccessBrush`) is shown via a `DataTrigger`
on the card — positioned bottom-right under the delete "x" per a live
follow-up request (initially top-left).

**Startup-default behavior change (same session, follow-on question)**:
building the checkmark surfaced a real gap — asked directly, the user's
mental model was "the default preset is what runs at startup," but the
actual code only used `AppConfig.DefaultPreset` as a fallback for a
brand-new install with no `live-keyboard-state.json` yet; every other
boot resumed whatever was last persisted (settled decision #9), which
could be a completely different effect (e.g. whatever "Close and End
Service" last set). **Changed, per the user's explicit go-ahead on this
recommendation**: the default preset is now applied unconditionally on
every Service startup, restoring parity with the actual Python reference
(which has no "resume last state" concept at all — `daemon/server.py`
always applies `config.json`'s `default_preset` on every boot; the C#
port's "resume last state" was a mid-port addition that had quietly
drifted from that). `DaemonState`'s constructor no longer reads
`liveStateStore` at all (still writes to it on every `SetEffect()` call,
still useful as a last-known-state record, just no longer consulted at
startup); `Program.cs`'s resolver was renamed `ResolveStartupFallback` →
`ResolveStartupEffect` to reflect that it's now authoritative, not a
fallback. Confirmed live: after this change, a Service restart booted
directly into "Red Chase" (the checkmarked default) instead of whatever
had been last active. **Lightbar's equivalent "resume last state"
behavior (`LightbarController.RestorePersistedState()`) was
deliberately NOT touched** — the user's question and this fix were both
scoped to the keyboard only; revisit only if asked.

**Backlogged, explicitly deferred (user's own words: "put the sleep and
hibernate issue in the backlog")**: answering a direct question about
sleep/hibernate resilience surfaced a real, previously-unknown gap —
`Controller.cs` already has auto-reconnect logic for a stale USB handle
after resume (a known fix ported from the Python side), and `Lightbar`'s
cached WMI instance already auto-recovers once on failure (added earlier
this session), but **`Keyboard.cs` has no equivalent at all** — a single
`HidStream` opened once at `Program.cs` startup with no staleness
detection or reopen logic. If sleep/resume invalidates the keyboard's
USB handle (plausible, given the controller is already known to hit
this), the render loop would silently stop updating the keyboard with no
recovery (caught by `RenderLoopService`'s own try/catch, so it wouldn't
crash the Service, just go quietly dark). **Not fixed — do not start
this without the user raising it again.** If it does come up: mirror
`Controller.cs`'s pattern (detect a gap/failure, reopen
`Keyboard.FindLightingDevice()` + `Open()`, swap the stream).

## Phase 7: Installer — IN PROGRESS, one critical bug blocking (2026-09-10, seventh session)

**Read this whole section before touching the installer or the Service's
input-handling code.** The installer itself (packaging, service
registration, data seeding, uninstall) works and is live-tested. But
installer testing surfaced a real, previously-unknown architecture
problem — **typing_reactive-style effects and the lightbar reactive
flash don't work when the Service runs as a real Windows Service** —
that is NOT fixed yet and needs a real code change before Phase 7 can
be considered done. See "Critical finding" below before doing anything
else in this phase.

### Tooling and layout

**Inno Setup 6** (installed via `winget install --id JRSoftware.InnoSetup`,
lands at `%LocalAppData%\Programs\Inno Setup 6\ISCC.exe`), chosen over
WiX/MSI — matches this project's existing lightweight-PowerShell-script
style far better than a heavier enterprise-deployment toolchain would,
and the user explicitly confirmed wanting "a real installer package"
after weighing both. New `windows/installer/` directory:
- `build.ps1` — orchestrates the whole build: `dotnet publish` both
  `JmaStudio.Service` and `JmaStudio.Gui` as self-contained single-file
  `win-x64` (settled decision #12), stages the bundled default data,
  runs `ISCC.exe` on `JmaStudio.iss`. Run this, not `ISCC.exe` directly
  on a stale `publish\` folder.
- `JmaStudio.iss` — the actual installer script. Extensively commented
  inline; read the file itself for exact mechanics, this section covers
  the decisions and bugs, not a line-by-line walkthrough.
- `generate-wizard-images.ps1` — one-time (re-run only if the logo
  changes) generator for the wizard's branded images: a dark (`#0B0B12`)
  164x314/55x58 canvas with `gui/logo.png` centered, via
  `System.Drawing`.
- `StartJmaStudio.bat` — target of the second desktop shortcut (see
  below).
- `assets/` — the generated wizard images.
- `output/` (gitignored-in-spirit but not yet actually gitignored --
  consider adding before committing) — where `JmaStudio-Setup.exe`
  lands.

### Key design decisions, in the order they were settled

1. **Data location: `%ProgramData%\JMA Studio\`** (not the dev-mode
   `windows\data\`) — finalizes settled decision #12's "likely
   ProgramData" call. Set via the installed service's own registry
   `Environment` value (see "Service registration" below), read by
   `Program.cs`'s existing `JMASTUDIO_DATA_DIR`/`JMASTUDIO_KEYMAP_PATH`
   env var overrides — no Program.cs code changes needed, that
   indirection already existed.
2. **No Python migration or detection in the installer at all** — a
   real oversight caught by the user: "when I decided... to go with
   having all the presets and effects all be written in C# instead of
   python, I assumed the migration tool would be removed [from the
   installer]." Instead, **the installer bundles the CURRENT C#
   preset/config data as its factory defaults** (`build.ps1` copies the
   real `windows/data/*.json` + `keymap.json` into
   `installer/publish/defaultdata/`, which becomes `{app}\DefaultData\`
   in the installed product). `PythonPresetMigrator.cs` and
   `JmaStudio.HardwareTest`'s `migrate-presets` command stay in the repo
   untouched, for the user's own manual future use in Visual Studio —
   just never referenced by the installer. Seeding only happens if
   `%ProgramData%\JMA Studio\data` doesn't already exist (never on an
   upgrade, so an existing install's edited presets are never clobbered)
   — a risk the user specifically flagged before agreeing to this
   design (migration overwriting newer C#-side edits).
3. **AcerLightingService consent screen**: a custom `TInputOptionWizardPage`
   (checkbox "I understand, and want to continue.") inserted after the
   welcome page, gated so `NextButtonClick` refuses to advance unless
   checked. Important nuance worked out with the user: the actual
   stop+disable doesn't happen at install time at all — it happens on
   **every Service startup** (settled decision #10's unconditional
   boot-time check) — so this screen is really "informed consent before
   installing at all," not a trigger point itself. Declining cancels
   the whole install.
4. **Two desktop shortcuts** (both unconditional, not opt-in — the user
   was explicit about wanting them, not offering a checkbox): "JMA
   Studio" (just `Gui\JmaStudio.Gui.exe`, no elevation) and "Start JMA
   Studio (Service + Tray)" (`StartJmaStudio.bat`, which self-elevates
   via a UAC prompt using the same `net session`-check-then-relaunch
   pattern `start_all.ps1` uses on the Python side, then `sc start`s the
   service and launches the GUI) — a manual recovery path for when the
   service isn't running (e.g. after "Close and End Service", or before
   the user's first login on a given boot).
5. **Uninstaller**: prompts once, up front (`InitializeUninstall`),
   "Keep your JMA Studio presets and configuration?" — Yes leaves
   `%ProgramData%\JMA Studio` alone, No `DelTree`s it in
   `usPostUninstall`. Separately, **always** (no prompt) re-enables
   `AcerLightingService` (`start= auto` + `sc start`) on uninstall —
   the user only asked about data, but leaving PredatorSense lighting
   permanently broken after a clean uninstall with no obvious cause
   would be a worse default than just restoring it.

### Real bugs found via live installer testing (all fixed except noted)

All of these were found by actually running the compiled installer and
uninstaller on this machine, not by inspection — same standing practice
as the rest of this project.

1. **Dark theming was inconsistent** — named-control theming (setting
   `WizardForm.PageNameLabel.Font.Color` etc. one by one) missed several
   stock pages' actual background panels and a few instructional labels
   that aren't exposed as named `WizardForm` properties at all (e.g.
   Select Destination Location's body was plain white/black). **Fixed**:
   replaced with a recursive `ThemeControl(WizardForm)` walk over every
   control on the form (colors any `TNewNotebookPage`/`TPanel`
   background, any `TNewStaticText`/`TNewCheckListBox`/`TNewEdit`/
   `TRichEditViewer` font/background it finds), called once in
   `InitializeWizard` (after the custom consent page exists, so it's
   included too) and again on every `CurPageChanged` as a defensive
   backstop. Confirmed live: the consent page and most other pages
   became fully dark and consistent.
2. **Wizard image had a visible white margin** — Inno Setup's actual
   "modern" style image control is larger than the classic 164x314 size
   the images were generated at (`WizardImageStretch=no` displays at
   native size, leaving the control's extra area showing its own
   default white). **Fixed**: `WizardImageStretch=yes`. **NOT yet
   re-verified live** — this fix was made right before the session
   moved on to investigate the Session 0 finding below; the last actual
   installer run the user saw was still on the un-stretched version.
   Verify this specifically next time before assuming it's resolved.
3. **The GUI/tray never launched after clicking Finish** — there was no
   `[Run]` section at all, so nothing told Setup to start anything post-
   install. **Fixed**: added a `[Run]` entry with
   `Flags: postinstall nowait skipifsilent runasoriginaluser` (checked-
   by-default "Launch JMA Studio" box on the Finish page).
   `runasoriginaluser` specifically de-elevates the launch back to the
   interactive user even though Setup itself runs elevated — otherwise
   the GUI would inherit the installer's admin token, the same
   elevation-creep already flagged as a known limitation in Phase 6.5's
   `switch-to-csharp` command. Confirmed live: the GUI/tray launched
   correctly after Finish on the second test run.
4. **Uninstall left `JmaStudio.Service.exe` behind** ("Some elements
   could not be removed" warning) — `sc stop` returns once the SCM
   reports the service stopped, but the self-contained .NET process can
   take a moment longer to actually exit and release its own `.exe`
   file lock; Inno's file-delete pass ran while it was still locked.
   **Fixed**: a `Sleep(2000)` settle delay between `sc stop` and
   `sc delete`/the file-delete phase. Confirmed live on the next
   uninstall test that this specific file was the only thing left
   behind (everything else — service unregistration, AcerLightingService
   re-enable, registry cleanup, ProgramData removal — worked correctly
   the first time).
5. Minor: `RegWriteMultiStringValue` (the seemingly-correct Inno Pascal
   Script function for writing the service's `Environment` REG_MULTI_SZ
   value) compiled but threw an unresolved "type mismatch" against the
   `TArrayOfString` parameter — rather than keep guessing at Inno's
   exact scripting API for this narrow case, switched to calling
   `reg.exe add ... /t REG_MULTI_SZ /d "VAR1=val1\0VAR2=val2" /f`
   directly via `Exec()`, which is reg.exe's own well-documented way to
   embed the null separator from a command line. Works correctly,
   confirmed live (the Service's `JMASTUDIO_DATA_DIR`/
   `JMASTUDIO_KEYMAP_PATH` env vars are correctly picked up — the data
   dir seeding under `%ProgramData%\JMA Studio\` and the Service finding
   its data there both confirmed live).

**Also confirmed working live, no issues**: `JmaStudioService` installs
as a real `AUTO_START` service with the correct `binPath`, starts
correctly, and serves `GET /status`/etc. correctly once installed;
`AcerLightingService` correctly transitions Automatic+Running (a real
clean baseline the user had me manually restore before this test,
specifically so the test would prove something) → Stopped+Disabled on
install, and back to Automatic+Running on uninstall; the HKCU autostart
Run key is added and removed correctly (`uninsdeletevalue`).

### Critical finding, FIXED (2026-09-10, eighth session) — Session 0 isolation breaks reactive input

**This is the one thing standing between the installer and Phase 7
actually being done.** Discovered live: after a real install, "Red
Chase" (and by extension every `typing_reactive`-family effect) no
longer reacts to real keystrokes — the background renders correctly,
but the chasing-bolt animation never triggers. Root cause, confirmed via
`Get-Process JmaStudio.Service | Select SessionId` showing `SessionId: 0`
against `explorer.exe`'s `SessionId: 1`: **a real Windows Service
running as `LocalSystem` executes in Session 0, which is isolated from
the interactive desktop by Windows' own security design (Session 0
Isolation, since Vista) — a global low-level keyboard hook
(`WH_KEYBOARD_LL`) installed from Session 0 physically cannot see
keystrokes typed on the Session 1+ interactive desktop.** This is a hard
OS boundary, not a permissions/elevation issue.

This affects BOTH `InputListener`/`GlobalKeyboardHook`-dependent
features: `typing_reactive`-family keyboard effects, AND the keyboard→
lightbar reactive flash (`LightbarReactiveManager`).

**Why this never showed up before now**: every single test this whole
project has done of these features — all of Phase 3's effect demos, all
of this session's live Lightbar/Diagnostics/tray testing — ran the
Service as a plain console app launched from an *interactive* elevated
PowerShell window, which is architecturally in Session 1 (the same
session as the desktop), not Session 0. The Session 0 problem only
exists for a *real, SCM-launched* Windows Service, which nothing had
actually tested until this installer round.

**Why Python's daemon never had this problem, and what that reveals**:
Python's `daemon/server.py` (via `setup.ps1`'s Scheduled Task
registration) was *never actually a Windows Service* — it's a Scheduled
Task with `-LogonType Interactive -RunLevel Highest`, which runs
**in the user's own interactive session**, just with an elevated token,
triggered `-AtLogOn`. That's a fundamentally different execution context
from a true `LocalSystem` service, and it's exactly why
`daemon/input_listener.py`'s global hook (via the `keyboard` library)
has always worked flawlessly. Settled decision #2's original comparison
between "Windows Service" and "the Python version's Scheduled-Task-with-
RunLevel-HighestAvailable workaround" was correct about the Admin-token
angle but never surfaced this input-capture consequence — nobody knew
to look for it until a real installed service actually got tested here.

**Decision, explicitly made by the user, do not re-litigate without new
input from them**: keep the true Windows Service (do NOT switch to a
Python-style Scheduled Task). Reason: the user's real motivation for
wanting a boot-time service in the first place is avoiding the
PH16-71's ugly firmware-default keyboard animation before login —
`Automatic`-start services begin well before the login screen typically
finishes, while a login-triggered Scheduled Task can't act until after
a full login, a much longer ugly-animation window. That goal is real
and the true-service architecture is the right way to get it.

**Fix built and verified (2026-09-10, eighth session).** A fresh session
resumed cold on this file, was told "read the windows handoff doc and
continue," and per the standing authorization above began building this
immediately (confirmed current live state matched this file's notes
first — `sc query JmaStudioService`/`AcerLightingService`, `GET
/status`, tray/GUI process check — all matched exactly). Implementation:
moved key capture out of the Service (Session 0) and into
`JmaStudio.Gui` (which runs in the interactive session, Session 1+, as
already established by the tray icon architecture).

- **New in `JmaStudio.Gui`**: `WindowsKeyMap.cs` (verbatim third copy of
  the vkCode-to-name table already proven in `JmaStudio.HardwareTest`/
  `JmaStudio.Service`). `GlobalKeyboardHook.cs` (a modified third copy —
  see next point for the one real difference). `KeypressForwarder.cs`
  (new class, owns the hook plus a `DispatcherTimer`-driven gate,
  forwards via `ApiClient.PostKeypressAsync`). Wired into `App.xaml.cs`
  alongside the tray icon — constructed and `Start()`-ed in `OnStartup`
  right after `_trayIcon`, disposed in `OnExit`. Runs for the whole GUI
  lifetime, not tied to `MainWindow` being visible, since the point is
  for reactive effects to keep working while the window is hidden in the
  tray.
- **The one real difference from the other two `GlobalKeyboardHook`
  copies**: this one also reports key-UP transitions (a new `KeyUp`
  event, firing on `WM_KEYUP`/`WM_SYSKEYUP`), which neither the
  `HardwareTest` nor `Service` copies needed before. A `WH_KEYBOARD_LL`
  hook's `KBDLLHOOKSTRUCT` carries no "is this a repeat" bit (unlike a
  normal `WM_KEYDOWN` message's lParam bit 30) -- `KeypressForwarder`
  needs real up-transitions to know when a held key was actually
  released, so it can tell "still held, this is a repeat" apart from "a
  genuinely new press of the same key."
- **Requirement 1 (never block the hook thread) satisfied via
  `Task.Run(() => _api.PostKeypressAsync(name))`**, never awaited from
  `OnKeyDown`. `ApiClient.PostKeypressAsync` (new) swallows
  `HttpRequestException`/`TaskCanceledException` itself, same pattern as
  the existing `ShutdownServiceAsync`.
- **Requirement 2 (filter auto-repeat) satisfied via a `HashSet<string>
  _heldKeys`** guarded by a lock: `OnKeyDown` only forwards if
  `_heldKeys.Add(name)` actually added a new entry (the key wasn't
  already held); `OnKeyUp` removes it. Confirmed live via the Service's
  own request log: real typing produced `POST /keypress` entries spaced
  like genuine keystrokes (100-800ms apart), not a tight repeat-flood.
- **Gating (the "worth doing" extra, also built)**: `KeypressForwarder`
  owns a `DispatcherTimer` (3s interval, plus one immediate check on
  `Start()`) that polls `GetStatusAsync()`/
  `GetLightbarReactiveConfigAsync()` and sets a `volatile bool
  _forwardingEnabled` -- true only when `CurrentEffect ==
  "typing_reactive"` or the lightbar reactive config's `Enabled` is
  true. On any polling failure (Service unreachable), defaults to
  `false` -- no point forwarding to a Service that can't even answer a
  status check. `OnKeyDown` checks this flag after the repeat-filter,
  before the `Task.Run` forward.
- **Service side**: `InputListener.OnKeyDown` renamed to `RecordKeyDown`
  and made public -- the single entry point for a keydown regardless of
  source (this Service's own local hook, dev-mode only, OR a forwarded
  `/keypress`). New `Endpoints.MapInput` maps `POST /keypress`
  (`KeypressRequest(string Key)`, new in `RequestModels.cs`) straight to
  `inputListener.RecordKeyDown(req.Key)`. **Real double-counting risk
  found and avoided before it ever shipped**: the Service's own local
  hook (`InputListener.Start()`) previously ran unconditionally in
  `Program.cs`, which would have fired for every real keystroke
  simultaneously alongside the GUI's newly-forwarded events whenever
  both happen to run in the same interactive session (true for ALL prior
  dev-mode testing, where the Service ran as a console app in Session
  1) -- double-registering every press. Fixed by gating
  `inputListener.Start()` behind an opt-in env var
  (`JMASTUDIO_LOCAL_KEY_HOOK=1`, off by default) instead of calling it
  unconditionally; the GUI's forwarded `/keypress` is now the only active
  input source in the normal case (GUI + Service both running), while
  the local hook stays available for the narrow case of testing the
  Service's reactive effects with no GUI open at all.

**Deployed and verified against the REAL installed service, live, this
session** (not dev mode -- this is the exact configuration the bug was
found in): rebuilt both projects (`dotnet build`, 0 warnings/errors),
then republished both as self-contained single-file `win-x64` builds
directly over the live install (`sc stop JmaStudioService` -> `Sleep 2s`
settle delay, same fix already proven during uninstall testing -> kill
the running `JmaStudio.Gui.exe` -> `dotnet publish -o` straight into
`C:\Program Files\JMA Studio\Service` and `...\Gui` -> `sc start
JmaStudioService`), then launched the GUI fresh (unelevated, as the
normal interactive user). Confirmed via the Service's own request log
(`C:\ProgramData\JMA Studio\data\logs\service.log`): real `POST
/keypress` entries arriving while "Red Chase" was the active effect,
correctly gated (a `GET /lightbar/reactive` poll immediately preceded
the first forwarded keypress), and spaced like genuine keystrokes, not a
repeat-flood. Cross-checked `GET /frame` returning different colors
300ms apart (proving the render loop was actively animating, not
static). **Then the user physically confirmed on the real hardware**:
"I just tested your live update, it works." This is the same
real-hardware verification bar every other phase in this file has used
-- not just log inspection.

**Not yet empirically verified, worth doing once Phase 7 is otherwise
wrapped up**: how early, in wall-clock terms relative to the login
screen, the real installed service actually starts on this specific
machine -- confident in the general Windows behavior (`Automatic`
services start well before login), but the exact timing on this
hardware hasn't been watched through an actual cold reboot yet. This is
now the ONLY remaining unverified item blocking Phase 7 from being
called fully done, alongside the still-unverified `WizardImageStretch`
fix (see "Immediate live state" at the end of this file).

### Uninstall live-tested: 2 real bugs found + fixed (NOT yet re-verified); 1 theming bug found, backlogged (2026-09-11, ninth session)

The user explicitly asked to test the uninstall's `Sleep(2000)` fix live
(never actually re-verified after being added in the seventh session --
see the bug list above). Rebuilt via `build.ps1` (bundling this
session's `/keypress` fix), then ran the real installed uninstaller
(`unins000.exe`) while `JmaStudioService` AND `JmaStudio.Gui.exe` were
both genuinely running, and chose "No" (delete data) at the keep-data
prompt when asked directly by the user which button they clicked --
confirmed this was intentional, not a bug (`AppDataRoot()` being fully
gone afterward was expected, not a `KeepUserData` logic failure).

**Two real bugs found, both fixed in code, NEITHER yet re-verified via
an actual uninstall run** (the session moved on to the theming bug
below before a clean re-test happened -- this is the top thing to do
next time uninstall testing resumes):
1. **`JmaStudio.Service.exe` was STILL left behind**, even with the
   `Sleep(2000)` fix in place -- proof a fixed sleep was never a real
   guarantee, just a race that happened to be won during the original
   (seventh-session) test and lost this time. **Fixed**: replaced with
   `WaitForFileUnlocked()`, a real poll-and-retry loop (tries
   `DeleteFile()` every 250ms, up to 40 attempts / 10s) instead of
   blindly sleeping a fixed amount -- resolves as soon as the handle is
   actually released, and degrades gracefully (logs a warning, lets
   Inno's own removal pass try anyway) if something unusual holds the
   file past the full wait.
2. **`JmaStudio.Gui.exe` was ALSO left behind** -- a previously
   undiscovered bug: the uninstall script had never once addressed the
   GUI/tray process at all (no service registration of its own, no
   graceful-shutdown call), so if it's running (the normal case), its
   `.exe` is still locked when Inno tries to delete it. **Fixed**: added
   `Exec('taskkill.exe', '/F /IM JmaStudio.Gui.exe', ...)` as the very
   first step of `CurUninstallStepChanged(usUninstall)`, before the
   Service is even touched.

Both fixes are in `windows/installer/JmaStudio.iss`, compiled cleanly,
but the actual verification uninstall test that would prove them never
happened this session (see "Immediate live state" at the end of this
file for exactly why, and what to do first next time).

**New theming bug found, 3 fix attempts failed, explicitly backlogged
by the user ("add this for fix later... move on")**: the Restart
Manager "Preparing to Install" page (shown automatically by Inno
whenever a running process locks a file Setup needs to overwrite --
surfaced live here because the leftover `JmaStudio.Gui.exe` from bug #2
above was still running during the very next install attempt) has an
unreadable app list and two unreadable "Automatically close/Do not
close the applications" radio options -- this page was never covered by
any previous live test, since nothing had ever been left running across
a reinstall before. **Confirmed via a temporary `Log(Name)` diagnostic
pass** (removed after use) that `ThemeControl`'s recursive walk DOES
reach the real controls (`FPreparingYesRadio`/`FPreparingNoRadio`/
`FPreparingMemo`), ruling out the walk simply missing them. Three
attempts, each compiled cleanly but visually failed live:
1. Guessed plain VCL `TRadioButton`/`TCheckBox`/`TLabel`/`TListBox`/
   `TMemo` -- valid identifiers in Inno's script engine (compiled), but
   never matched the real runtime objects via `is`.
2. Guessed Inno's own `TNewRadioButton`/`TNewMemo` (matching the "New"-
   prefixed convention every other themed control already uses) --
   also compiled, also didn't visually take effect.
3. Bypassed the generic walk entirely for just these three, setting
   them directly via `WizardForm.PreparingYesRadio`/`PreparingNoRadio`/
   `PreparingMemo` (documented public properties, no runtime type check
   needed at all) -- STILL no visible change.
That three independent approaches (two different guessed types, then a
type-check-free direct property path) all failed suggests something
more structural is going on with this specific page -- possibly a
timing issue (these controls might get destroyed/recreated by Inno's
own RestartManager-detection logic AFTER `CurPageChanged`/
`ApplyDarkTheme` already ran for this page, similar in spirit to this
project's own WPF "mid-BAML-parse" timing bugs from Phase 6, but on
Inno's side this time) rather than a simple wrong-class guess. **Do not
attempt a 4th blind guess** -- if this comes up again, get real
evidence first: try re-theming from a distinct, later hook (e.g. a
`WizardForm`-level idle/paint hook if Inno's scripting exposes one, or
re-running `ApplyDarkTheme` on a short delay/timer after landing on this
page) rather than another one-shot class guess at `CurPageChanged` time.
The `Log(Name)` diagnostic pattern used to find the real control names
here is worth reusing directly if this is picked back up.

### Uninstall re-enabling AcerLightingService caused Python to silently take back over (2026-09-11, eleventh session) — fixed, NOT yet live-tested

Real incident, reported directly by the user the morning after the
tenth session's restart test: they ran the uninstaller, rebooted, and
found the Python version running and controlling the hardware instead
of a clean/expected state. **Root cause confirmed via live evidence
before touching any code** (`schtasks //query //tn "JMA Studio
Autostart" //v //fo list`, `sc query AcerLightingService`, `tasklist`):
this has nothing to do with the C# installer/uninstaller directly. A
pre-existing Scheduled Task, **"JMA Studio Autostart"** (Python's own
autostart, `Schedule Type: At logon time`, predates this entire C# port
project, never managed by `JmaStudio.iss` at all), was still `Enabled`
and fired at login exactly as it always would have. Separately,
confirmed `AcerLightingService`'s `START_TYPE` WAS correctly set back to
`AUTO_START` by the uninstaller (working as designed at the time), but
its actual `STATE` was `STOPPED` — Python's own daemon re-stopped it
again at ITS OWN startup (the same "cheap insurance" pattern settled
decision #10 gives the C# Service, mirrored on the Python side). So the
full picture: uninstalling C# re-enabled `AcerLightingService`
unconditionally (original design), then the still-enabled Python task
fired at the next login regardless, and Python's daemon immediately
re-disabled `AcerLightingService` itself — from the user's point of
view this looked like "the uninstall reinstated the Python version,"
but the uninstaller's OWN actions (re-enabling AcerLightingService)
were actually incidental to that, not the direct cause.

**User's explicit read on this, framing why the fix should still be an
ask, not silence**: "anyone other than me will probably not have the
python version" — i.e. this exact incident is dev-machine-specific
(only this machine has the leftover Python install + task from before
the port), but the general principle (ask before silently flipping a
service's state back on) is a good improvement regardless of cause.

**Three related changes made, all compiled cleanly, NONE live-tested
yet** (the user chose "leave it as-is for now" rather than doing another
install/uninstall cycle this session — Python is currently the active
stack on this machine, C# is currently fully uninstalled, and that's
the user's deliberate choice, not an accident to fix):
1. **`AcerLightingService` re-enable is now a prompt, not automatic.**
   `InitializeUninstall()` now checks the service actually exists first
   (`sc query AcerLightingService`, exit code `1060` =
   `ERROR_SERVICE_DOES_NOT_EXIST` means skip the prompt entirely — most
   machines), then asks "Would you like to re-enable it now?" via
   `MsgBox`, storing the answer in a new `ReEnableAcerService: Boolean`
   acted on later in `CurUninstallStepChanged(usUninstall)`. Same
   defer-to-`usUninstall` pattern the existing `KeepUserData` prompt
   already uses. The `ConsentPage` welcome-screen text (which promised
   "uninstalling JMA Studio re-enables AcerLightingService
   automatically") was updated to match ("...will offer to re-enable
   AcerLightingService").
2. **New: the C# installer now disables (never deletes) Python's "JMA
   Studio Autostart" task on install**, via a new
   `DisablePythonAutostartIfPresent()` procedure (`schtasks.exe /Change
   /TN "JMA Studio Autostart" /Disable`), called from
   `CurStepChanged(ssPostInstall)` right after `InstallService()`.
   Silent no-op (schtasks just fails harmlessly) on the vast majority of
   machines that never had the Python version at all. Explicitly
   **disable, not delete** — keeps this reversible via the Diagnostics
   window's existing "Switch to Python" button
   (`DiagnosticsManager.cs`), which already re-enables this exact task
   by name when a user deliberately switches stacks (Phase 6 continued:
   Diagnostics window, above). This directly prevents the OTHER known
   failure mode this project already hit and fixed once before (two
   processes fighting over the same keyboard, flickering/color-bleeding)
   from ever recurring after a fresh C# install on a machine that
   happens to have both versions present.
3. **Symmetric addition, per explicit user confirmation when asked**:
   uninstalling C# now ALSO offers to re-enable the Python autostart
   task (not just AcerLightingService) — same existence-check-then-ask
   pattern (`schtasks //Query //TN "JMA Studio Autostart"`, exit code 0
   means it exists), new `ReEnablePythonAutostart: Boolean`, acted on in
   `CurUninstallStepChanged(usUninstall)` via `schtasks.exe /Change /TN
   "JMA Studio Autostart" /Enable`. Deliberately does NOT check whether
   the task is currently Enabled/Disabled first (would need parsing
   `schtasks`' text output, more complexity than this is worth) —
   re-enabling an already-enabled task is a harmless no-op.

**All four now confirmed live** (same session, after the user manually
stopped Python and asked to actually run the test): installed fresh —
`JMA Studio Autostart` task confirmed `Disabled` immediately after
install (`schtasks //query`); uninstalled with both `JmaStudioService`
and `JmaStudio.Gui.exe` genuinely running — the `AcerLightingService`
prompt appeared, user chose Yes, and manually confirmed PredatorSense
regained real control afterward; the Python-autostart prompt also
appeared (user saw it, chose not to re-enable it this time); `Service/`
and `Gui/` folders both came back completely empty (zero leftover
`.exe` files) confirming the ninth session's `WaitForFileUnlocked`/GUI
`taskkill` fixes ALSO hold up under a second real test.

**One more real bug found and fixed via this same test, live, before
being asked to check for it** — the user's own words: "it is a pet
peeve of mine when an uninstaller leaves empty folders." `{app}\Service`
and `{app}\Gui` (and the `{app}` root itself) were left behind as empty
directories after that uninstall, even though every file inside them
was gone. Root cause: `WaitForFileUnlocked` deletes the `.exe` files
itself via `DeleteFile()`, ahead of Inno's own built-in file-removal
pass — Inno's normal "remove a directory once nothing's left in it"
auto-cleanup apparently only triggers when INNO itself performs the
final deletion, not when a script deletes the file out from under it
first. **Fixed**: added explicit `RemoveDir()` calls for `{app}\Service`,
`{app}\Gui`, and `{app}` itself in `usPostUninstall` (safe to call
unconditionally — `RemoveDir` only succeeds on a genuinely empty
directory, silently fails otherwise). **Re-verified with a full third
install/uninstall cycle**: `C:\Program Files\JMA Studio\` confirmed
completely absent afterward (not even present as an empty directory).
Machine was then reinstalled a final time via the same `Setup.exe`,
confirmed running cleanly (`Red Chase` active, `AcerLightingService`
Stopped/Disabled) — this is the state the machine is in as of this
writing.

### Settled decision #2 — annotation, not a reversal

Settled decision #2 ("Windows Service, not Scheduled Task") stays
final, explicitly reaffirmed by the user after learning about the
Session 0 consequence above (see "Decision" under the critical finding).
Add this to how that decision is understood going forward: a true
Windows Service's Session 0 execution context is NOT equivalent to
Python's actual Scheduled-Task-based "elevated but still interactive"
execution context, specifically for anything depending on global input
capture — this project's own dev-mode testing accidentally matched
Python's context (interactive, elevated console) for its entire
history so far, which is why this never came up until a real installed
service was tested for the first time this session.

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
   reassert button — **all 4 windows DONE** (main, Lightbar, Controller
   Reactive, Diagnostics — see "Phase 6" and all "Phase 6 continued"
   sections above). The "Create Preset" flow described under settled
   decision #11 was never built as its own separate flow — presets are
   still only created via "Save current as preset" from whatever's
   live, not a pick-a-kind-then-configure wizard; revisit only if the
   user asks for it specifically.
6.5. Tray icon (`TrayIconManager.cs`) — **DONE**, see "Phase 6.5" above.
   Not in the original phasing list at all; the user added this
   mid-session, ahead of the installer, since Python's own autostart
   story is built around the tray icon, not the full GUI window.
7. Installer (location prompt, consent notice, service registration,
   GUI autostart, preset data migration) — **FUNCTIONALLY COMPLETE**,
   see "Phase 7" above. Built with Inno Setup (`windows/installer/`),
   not the originally-unspecified mechanism; migration ended up NOT
   being a Python-import feature at all (bundles current C# data as
   defaults instead, per explicit user decision). GUI autostart
   correctly targets `JmaStudio.Gui.exe` itself, matching Python's own
   behavior. The critical bug that was blocking completion — Session 0
   isolation breaking reactive input features when the Service runs as
   a real Windows Service — is **fixed and user-confirmed live**, and
   real cold-boot timing is confirmed (beats PredatorSense's own
   timing, per the user's own tenth-session observation). Two small,
   explicitly non-blocking items remain: re-verify the ninth session's
   two uninstall fixes with an actual uninstall test, and a backlogged
   (do-not-reattempt-without-new-evidence) theming bug on the Restart
   Manager "Preparing to Install" page.
8. Idle screensaver (multi-effect V2 scope) + low-battery lighting
   override — **Feature 1 (idle screensaver) DONE and live-verified;
   Features 2-3 still DESIGN ONLY**, see "Phase 8" below. Explicitly not
   part of the original planning conversation; added at the user's
   request. Do not start building Features 2/3 without the user's
   explicit go-ahead, even on a fresh session — the go-ahead already
   given was specifically for the screensaver.

## Phase 7 continued: Python vs. C# resource overhead benchmark (2026-09-11, twelfth session)

The user asked for a live, controlled comparison of idle/active resource
overhead between the Python and C# stacks, both closed-GUI and open-GUI,
specifically to answer "would I actually feel this in a game." Both
stacks were run on the SAME "Red Chase" preset for a fair comparison,
each stack fully stopped before the other started (never both at once,
per the established two-processes-fighting-over-hardware risk), across
8 measurement phases (2 stacks x 2 GUI states x idle/synthetic-typing-
load) plus a 60s neither-running baseline. Full methodology, results
table, and analysis were given directly to the user in-conversation
(not duplicated here in full) — key numbers for future reference:

| Phase | CPU (core-equiv %) | Avg Mem |
|---|---|---|
| Python closed idle / active | 1.6% / 9.8% | 100 / 100 MB |
| Python open idle / active | 11.2% / 12.4% | 207 / 208 MB |
| C# closed idle / active | 9.4% / 13.2% | 1064 / 1066 MB |
| C# open idle / active | 18.8% / 22.6% | 1070 / 1073 MB |

C# uses ~5-10x more memory than Python at every stage (expected: the
self-contained single-file .NET publish bundles the whole runtime into
the process, plus WPF's own overhead vs. a lightweight webview). C#'s
idle CPU floor is also meaningfully higher than Python's even with the
GUI closed. On this machine (i9-13900HX, 8P+16E hybrid, 32 logical
threads) none of this is expected to be perceptible in a game -- the
worst measured case (22.6% of one logical thread) is under 1% of total
system compute, and Windows' hybrid scheduler specifically steers this
kind of steady background work onto E-cores, away from a game's
P-core-hungry threads. The more relevant gaming-performance axis is
input latency, not background CPU%, and that was already specifically
designed around in the `/keypress` fix (async, non-blocking, repeat-
filtered) -- see Phase 7's "Fix built and verified" section above.

**A real bug was found and fixed mid-benchmark, worth remembering for
any FUTURE automation that needs to interact with a window (not just
launch a process)**: an elevated PowerShell script's `FindWindow`/
`EnumWindows`/`GetWindowText`/`PostMessage(WM_CLOSE)` calls all silently
failed to find or affect the real, visible "JMA Studio" window --
`EnumWindows` enumerated 161 real top-level windows and found zero
matches, even though `Get-Process -Name JmaStudio.Gui | Select
MainWindowTitle` (a plain, non-elevated read) correctly returned "JMA
Studio" every time. This reproduced identically from a NON-elevated
context too, and even .NET's own `Process.CloseMainWindow()` (a higher-
level, more standard API than raw `PostMessage`) had the exact same
silent-failure symptom -- it returned `True` (message "sent"
successfully by .NET's own accounting) but the window never actually
hid. **Root cause not fully identified** (suspected: some kind of
desktop/window-station distinction between this automation
environment's own window-manipulation calls and the real interactive
desktop, since plain PROCESS-level operations like `Get-Process`,
`Start-Process`, and reading `MainWindowHandle`/`MainWindowTitle`
consistently worked fine throughout -- only direct window-message-based
interaction failed). **The one thing that reliably worked, every time,
was the user physically clicking the window's own X button or the tray
icon.** Practical guidance for any future session attempting this kind
of automation: don't trust `FindWindow`/`PostMessage`/`CloseMainWindow`
from this automation context to actually manipulate a real window's
visibility -- verify unhidden/hidden state explicitly before trusting
the result (which is exactly what caught this bug: an added
`IsWindowVisible` check after the "successful" close attempt), and if
it fails, ask the user for one quick manual click rather than guessing
at further Win32-level workarounds. The final, correct benchmark
numbers above were obtained by asking the user to do exactly that.

## Phase 8: Idle screensaver + low-battery lighting override (V2) — Feature 1 BUILT and live-verified; Features 2-3 still DESIGN ONLY (2026-09-11 design, 2026-09-12 Feature 1 build)

**Feature 1 (idle screensaver) is fully built and confirmed working live
-- see "Feature 1 -- BUILT" below for the real implementation, live
testing, and the 3 follow-on fixes/additions made after the user tried
it.** Features 2 (low-battery override) and 3 (controller hot-discovery)
are STILL planning only -- the original "wait to be asked" standing
instruction from the twelfth session still applies to those two. The
user gave the explicit go-ahead for V2 at the start of the thirteenth
session ("Let's start building V2, start with the screen saver"), which
is why Feature 1 and only Feature 1 has been built -- don't assume that
same go-ahead extends to Features 2/3 without the user saying so again.

**Scope, explicitly confirmed by the user: V2 applies to the C# port
ONLY.** Nothing in this phase touches the Python version (`daemon/`,
`effects/`, `gui/`, `tray.py` on `main`/the repo root) at all -- not
because it couldn't apply there too, but because the user said so
directly. Don't port any of this back to Python without being asked.

### Feature 1: Idle screensaver (multi-effect, V2 scope)

**Origin**: the user's real motivation is that the PH16-71's ugly
firmware-default keyboard animation before login was already solved by
the boot-time service (Phase 7) -- this is the same instinct applied to
being AFK at the desktop: if nothing is happening, show something more
interesting/calming than whatever effect happened to be active, then
snap back the instant real input resumes.

**Idle detection -- the hard part, and the user correctly anticipated
the real difficulty**: "the timing can't be based on me just not
typing, I may be playing a game and only using my controller... if I
don't input on any device the timer's going." Windows' own idle-
tracking API, `GetLastInputInfo()`, reliably covers keyboard AND mouse
-- but does NOT see controller input at all, since games read a
DualSense via raw HID/XInput, bypassing the OS input pipeline entirely
(this is the same well-known reason Windows' own screensaver/display-
sleep timer famously ignores controller-only play). The design combines
two independent activity sources rather than relying on one API:
1. **Keyboard + mouse**: `GetLastInputInfo()`, polled every few seconds
   from `JmaStudio.Gui` (must be the GUI, not the Service -- same
   Session 0 isolation reason the `/keypress` fix exists at all; the
   Service cannot see interactive-session input directly). Deliberately
   NOT a new custom low-level mouse hook -- that would add a second
   always-on global hook next to the existing keyboard one, working
   against this whole project's established "stay light on a gaming
   laptop" principle. `GetLastInputInfo` is a single cheap on-demand
   Win32 call, not a hook intercepting every mouse move.
2. **Controller**: the Service's `RenderLoopService` already reads
   `Controller.GetState()` every single frame for the controller-
   reactive effect -- comparing consecutive frames (any button OR any
   stick movement past the existing `ControllerReactiveParams.Deadzone`
   setting, so idle analog noise/drift doesn't count) gives controller
   activity for free, zero new polling overhead.

The Service owns the actual idle clock (it already owns all render-loop
state): `lastActivity = max(last keyboard/mouse ping received from the
GUI, last frame a controller state change was detected)`. A new
background service (same shape as `LightbarReactiveManager`, ticking
every second or so) compares `now - lastActivity` against the
configured threshold.

**V2 scope, per the user's explicit follow-up ask** (this is new,
beyond the original single-effect idea, and is the reason this is
called V2 rather than just "the screensaver feature"): the screensaver
is a PLAYLIST, not one fixed effect --
- A list of keyboard preset names to cycle through (reusing the
  existing named-preset system directly, not a new preset format).
- A configurable cycle interval (how often it switches to the next
  playlist entry while idle).
- A randomize toggle: off = cycle through the list in stored order,
  wrapping around; on = pick randomly each cycle tick (should avoid
  immediately repeating the same entry twice in a row, otherwise a
  short list feels broken, not random).
- A list of ONE entry degenerates cleanly back to the original "one
  effect" idea -- V2 generalizes V1 rather than replacing it.
- **Open question, not yet answered by the user, flag before building**:
  should the lightbar ALSO cycle through its own parallel playlist in
  lockstep with the keyboard list, or just sit on a single fixed
  "screensaver lightbar preset" the whole time regardless of which
  keyboard effect is currently showing? Recommended default if not
  told otherwise: give the lightbar its own optional list too (a list
  of length 1 behaves as "fixed"), for maximum flexibility with no
  added complexity over the fixed-only alternative.
- This needs a SECOND internal timer distinct from the idle-detection
  timer: one decides WHEN to enter/exit screensaver mode at all, the
  other (only running while screensaver mode is active) decides when to
  advance to the next playlist entry.

**Trigger/restore mechanism**: reuses the exact stash-and-restore
pattern `ControllerReactiveManager` already implements for its own
enable/disable (Phase 5) -- on entering screensaver mode, snapshot
whatever's currently live (keyboard effect + params, lightbar state,
AND whether controller-reactive is currently enabled), apply the
current playlist entry; on any real input resuming from either source,
restore the snapshot exactly, no fade -- an instant "snap back," per
the user's own word for it.

**Confirmed by the user, do not re-litigate**: controller-reactive mode
being active does NOT block/override the screensaver from triggering --
"if you are asking if controller-reactive active should override the
screen saver, I would say no." The screensaver triggers uniformly
regardless of what was running before, including controller-reactive.

**New config, new file, mirroring `LightbarReactiveConfig`'s existing
shape/style** (`JmaStudio.Presets`, not yet created):
```
IdleScreensaverConfig {
  Enabled: bool
  IdleThresholdMinutes: double
  KeyboardPresetNames: List<string>   // the playlist
  LightbarPresetNames: List<string>?  // optional parallel playlist -- open question above
  CycleIntervalSeconds: int
  RandomOrder: bool
}
```

**New Service pieces, not yet created**: an `IdleScreensaverManager`
(`BackgroundService`, same shape as `LightbarReactiveManager`) owning
both timers and the stash/restore logic; a new `POST /idle-activity`
endpoint (or similar name) for the GUI's periodic `GetLastInputInfo`
ping; `GET`/`POST /idle-screensaver/config` (mirrors `/lightbar/
reactive`'s existing GET/POST shape).

**New GUI piece, not yet created**: something needs to expose the
playlist/interval/randomize/threshold settings -- not yet decided where
this UI lives. Recommended default: a new dedicated small settings
window (matching the established one-window-per-concern pattern:
Lightbar, Controller Reactive, Diagnostics each got their own), rather
than folding into an existing window, since there are now potentially
TWO new settings groups (this, plus low-battery below) that would
otherwise clutter the Diagnostics window's existing dashboard scope.
Not a final decision -- worth revisiting once actually building this.

### Feature 1 -- BUILT (2026-09-12, fourteenth session)

Built almost exactly per the original design above, with the two open
questions from that design resolved live by the user before coding
started: the lightbar stays fixed (no cycling playlist -- confirmed:
"Keep it to fixed to one of the built in effects or a solid color, or
nothing at all"), and it got its own dedicated window rather than
folding into MainWindow (the recommended default, which the user
deferred to).

**New files, exactly as designed:**
- `JmaStudio.Presets/Models.cs`: `IdleScreensaverConfig` record (matches
  the design's shape exactly) plus a new `IdleScreensaverSentinels`
  static class (added live, not in the original design -- see "Lights
  Out" below).
- `JmaStudio.Presets/JsonStore.cs`: `PresetStore.IdleScreensaverConfig`,
  backed by a new `idle-screensaver-config.json`.
- `JmaStudio.Service/IdleScreensaverManager.cs`: the `BackgroundService`,
  same shape as `LightbarReactiveManager`. Combines the two activity
  sources exactly as designed (GUI-forwarded keyboard/mouse pings via
  `RecordExternalActivity()`, plus its own direct per-tick
  `Controller.GetState()` diff against the previous tick, deadzone-aware
  at 0.15 matching `ControllerReactiveParams`' own default). Confirmed
  live: stashing/restoring via `DaemonState.GetEffect()`/`SetEffect()`
  alone composes correctly with `ControllerReactiveManager` with zero
  special-casing needed, exactly as reasoned through in the original
  design -- this was never actually tested against controller-reactive
  being active during this session, so "confirmed" here means "confirmed
  by re-reading the composition logic carefully," not "confirmed live
  against that specific combination." Worth an explicit live check if it
  ever matters.
- `JmaStudio.Service/Endpoints.cs`: `MapIdleScreensaver` -- `POST
  /idle-activity`, `GET`/`POST /idle-screensaver/config`, exactly the
  shape the design called for.
- `JmaStudio.Gui/IdleActivityMonitor.cs`: `GetLastInputInfo` polling
  every 2s from the GUI, pinging the Service only when idle time is
  observed to have gone DOWN since the last check (i.e. real new input
  happened) -- not a heartbeat, exactly as designed.
- `JmaStudio.Gui/IdleScreensaverWindow.xaml`/`.xaml.cs`: the settings
  window -- enable toggle, idle-threshold slider, a playlist `ListBox`
  with add/remove/reorder (add/remove were in the original design's
  scope; up/down reordering was an obvious addition made while building,
  not separately requested but clearly implied by "how often they
  change" mattering for sequential order), cycle-interval slider,
  random-order checkbox, and a single lightbar-preset combo. Config is
  edited locally and posted as one whole object on Save -- deliberately
  NOT the tuning panels' live-apply-with-debounce pattern, since
  `IdleScreensaverManager` only reads the config once per its own 1s
  tick anyway, so there's no responsiveness benefit to live-apply for a
  background timer's settings.
- `MainWindow.xaml`/`.xaml.cs`: a new "Screensaver" button next to
  "Controller Reactive" in the top bar, opening the new window (same
  `IsLoaded`/`Activate()` pattern as every other child window).

**Verified live, redeployed to the real installed Service+GUI multiple
times over the course of this session** (same standing practice as
every other phase): the playlist correctly cycles, a single-entry
playlist correctly does NOT re-apply itself every interval (confirmed by
re-reading `AdvancePlaylistIfDue`'s own early-return guard when asked
directly, not by a fresh live test -- the guard was already correct from
first-write), and the lightbar preset combo correctly leaves the
lightbar untouched when unset.

**Three real bugs/gaps found live and fixed, all in the same session,
none anticipated by the original design:**
1. **ListBox text was unreadable** -- black-on-dark, the exact same
   class of bug this project already hit and fixed for ComboBox/CheckBox
   in Phase 6's theming pass, just never triggered before now because
   this was the first-ever use of a plain `ListBox` anywhere in the app.
   Fixed the same way: a global implicit `ListBox`/`ListBoxItem` style in
   `App.xaml`, mirroring `ComboBoxItem`'s own hover/selected-state
   template rather than inventing a new pattern.
2. **User asked for a green checkmark on the Controller Reactive and
   Screensaver top-bar buttons when each is enabled** -- not in the
   original design at all, requested live after trying the feature.
   Added a new, deliberately SEPARATE 3-second poll timer
   (`_featureStatusPollTimer` in `MainWindow.xaml.cs`, polling `GET
   /controller-reactive/status` and `GET /idle-screensaver/config`) --
   kept off the existing 750ms status-poll timer on purpose, since these
   two states only change when a user toggles them, not every tick,
   matching this app's established pattern of not polling
   infrequently-changing things on a fast timer (see `KeypressForwarder`'s
   own 3s gating poll for the same principle applied elsewhere).
   **First attempt placed the checkmark as a floating overlay positioned
   outside the button's own bounds (negative margin on a sibling
   TextBlock in a wrapping Grid) -- the user asked for it moved INSIDE
   the button instead**, fixed by restructuring each button's `Content`
   into a `StackPanel` (label + conditionally-visible checkmark
   TextBlock) rather than a plain string, so the checkmark is genuinely
   part of the button's own content flow, not a separately-positioned
   overlay.
3. **"Lights Out" option, requested live, not in the original design**:
   the user wanted an obviously-named way to turn everything off as a
   playlist entry / lightbar selection, rather than requiring a separate
   checkbox-plus-disable-the-list mechanism (offered as the alternative,
   not chosen). Implemented as a reserved sentinel string
   (`IdleScreensaverSentinels.LightsOut = "(Lights Out)"`, in
   `JmaStudio.Presets` so both the GUI and Service agree on the exact
   value) rather than a real preset -- selectable in both the keyboard
   playlist's "Add" combo and the lightbar combo. `IdleScreensaverManager`
   special-cases this string before doing any preset lookup: for the
   keyboard, applies `static` with default (black) params, the same
   mechanism `ApiClient.TurnOffAsync()`/MainWindow's own Off button
   already use; for the lightbar, calls `LightbarController.Off()`
   directly. `Activate()`'s lightbar handling became a real three-way
   branch (untouched / Lights Out / named preset) sharing the same
   stash-and-restore bookkeeping regardless of which of the three
   applies.

**Not yet exercised live, worth knowing**: the "Lights Out" sentinel
colliding with an actual user-named preset called exactly `(Lights Out)`
was never tested (would silently behave as Lights Out instead of the
real preset) -- extremely unlikely in practice given the parentheses,
not worth guarding against unless it ever actually happens.

### Feature 2: Low-battery lighting override

**Simpler than the screensaver -- no session-isolation problem at all.**
Battery charge/plugged-in state is plain system hardware state, not
tied to any interactive session, so it can be read directly in
`JmaStudio.Service` itself via the plain Win32 `GetSystemPowerStatus`
API (no need to route anything through the GUI, unlike the screensaver's
keyboard/mouse detection). Polling once every 30-60 seconds is more
than sufficient -- battery percentage doesn't change fast enough to
need anything more frequent, so this is essentially zero-overhead.

**Behavior, confirmed by the user**: when battery percentage drops to
or below a user-set threshold AND the laptop is not plugged in, override
BOTH the keyboard and lightbar (confirmed: "yes" to "applies to
keyboard + lightbar together") to a dim-white color -- with the exact
color/brightness user-configurable via a real color picker (confirmed:
"yes" to "should the color be configurable," matching this whole app's
existing philosophy of exposing real pickers everywhere rather than a
hardcoded value). The instant either condition becomes false (plugged
in, or percentage climbs back above the threshold), restore whatever
was running before -- same stash-and-restore pattern as the screensaver
and controller-reactive, a third reuse of the same mechanism.

**Priority rule, confirmed by the user, do not re-litigate**: "battery
wins" -- if the low-battery condition and the idle-screensaver condition
are both true at the same time (e.g. battery crosses the threshold
while already AFK and the screensaver is showing), the low-battery
override takes priority over the screensaver. This means the priority
chain, checked continuously by whatever coordinates these features, is:
low-battery override (highest) > idle screensaver > normal/whatever the
user or API last set (lowest). A real architectural implication worth
noting: with the screensaver's own stash/restore AND the battery
override's own stash/restore both potentially active in sequence
(battery triggers while screensaver is already showing), the RESTORE
target when battery clears must be "whatever the screensaver was
showing," not "whatever was running before the screensaver started" --
i.e. these two features' stash/restore need to nest correctly, not each
assume they're the only one ever active. This needs real care when
actually implemented; do not just copy-paste two independent stash/
restore pairs without thinking through the nesting case.

**New config, new file, not yet created**:
```
LowBatteryOverrideConfig {
  Enabled: bool
  ThresholdPercent: int
  Color: RgbColor
  Brightness: double
}
```

**New Service pieces, not yet created**: a `LowBatteryOverrideManager`
(or folded into a shared coordinator with the screensaver, given the
priority-nesting concern above -- worth considering a single combined
"effect override coordinator" that owns BOTH features' priority
resolution in one place, rather than two independent managers each
guessing at the other's state); `GET`/`POST /battery-override/config`;
possibly a `GET /battery/status` diagnostic endpoint (current percentage
+ plugged-in state) useful for the Diagnostics window's existing
hardware-status-tile pattern, even independent of this feature.

### Feature 3: Controller hot-discovery (USB + Bluetooth)

**Origin**: a real bug the user found live -- every controller-reactive
test throughout this whole project (Phase 5 onward) happened to have
the DualSense already connected before the Service started. Connect it
AFTER the Service is already running and it never works. This isn't a
new problem so much as a previously-undiscovered symptom of an already-
documented gap: the Diagnostics window's existing `Rescan()` endpoint is
explicitly commented as "presence-only... NOT a true hot-reconnect,"
since `Keyboard?`/`Controller?` are captured ONCE in `Program.cs` at
startup and handed out directly (as plain closure-captured references,
not through DI, per Phase 5's own note: "Keyboard/Controller are NOT
registered in DI... passed directly to the endpoint mapping methods and
RenderLoopService's factory") to `RenderLoopService`,
`DiagnosticsManager`, `ControllerReactiveManager`'s status reporting, and
several `Endpoints.cs` handlers (`/status`, `/controller-reactive/
status`, `/diagnostics/*`). None of them can ever see a controller that
wasn't there at the moment `Program.cs` ran.

**User's explicit framing, confirmed after discussion**: this is real,
substantial scope -- "this is a major change... none of this a[re]
minor improvement update[s], this is an upgrade." Build USB
re-discovery and Bluetooth discovery/support TOGETHER as one feature,
not phased -- do not split Bluetooth out as a smaller later follow-up.

**UX, confirmed exactly as described, no changes needed**: one
"Discover" button next to the existing "Enabled" toggle at the top of
`ControllerReactiveWindow`. Searches both USB and Bluetooth in one
action, no separate steps or transport picker for the user. If more
than one matching device is somehow found, connects to the first one
enumeration returns -- no smarter tie-breaking, no picker UI. Realistic
assumption stated by the user and worth keeping: only one controller
will ever realistically be paired to this machine.

**The real architecture change: `Controller?` needs to become a
mutable holder, not a fixed reference.** A new small class (name TBD at
build time, e.g. `ControllerHolder`) wrapping `Controller? Current`
behind a lock (read from the 30fps render loop AND from HTTP request
handlers concurrently, so this needs real thread safety, not just a
bare nullable field) with a `Replace(Controller? newController)`
method. Every current consumer of the raw `Controller? controller`
closure-captured reference (`RenderLoopService`, `DiagnosticsManager`,
and the `Endpoints.cs` handlers listed above) needs to instead hold a
reference to the HOLDER and read `.Current` each time, not the
controller instance directly. `Program.cs` constructs the holder once,
seeds it from the existing startup `TryOpen("controller", ...)` call
(so the already-working "connected at startup" case is unchanged
behavior, not a regression risk), then passes the HOLDER everywhere
instead of the raw nullable reference. This is a mechanical but
real refactor touching several existing files -- budget real time for
it, this is not a one-line change.

**Discovery/connect logic, new**: a method (e.g. `Controller.
TryDiscover()`) that enumerates HID devices matching the DualSense's
VID/PID via the same HidSharp mechanism `Controller.Open()` already
uses, opens the first match, and hands the result to the holder's
`Replace()`. A new endpoint (e.g. `POST /controller-reactive/discover`,
living alongside the existing controller-reactive endpoints since
that's where the button lives) triggers this and returns whether it
succeeded, for the GUI to reflect back to the user.

**Bluetooth -- the genuinely uncertain part, needs live hardware
verification, do not assume it works from reasoning alone**:
- **Finding** a Bluetooth-connected DualSense should need no BT-specific
  API at all -- Windows exposes an already-paired, connected Bluetooth
  HID device through the exact same HID device enumeration used for USB
  (this project's existing HidSharp-based `DeviceList.Local.
  GetHidDevices()` approach), so the SAME discovery scan should see
  both transports for free. The user is expected to have already paired
  the controller via Windows' own Bluetooth settings first, same
  prerequisite as any other Bluetooth accessory -- this app doesn't need
  to do any BLE/pairing UI of its own.
- **Distinguishing which transport a found device is using** -- needed
  because the two transports use different report formats -- can likely
  be read off the HID device's own path string (Windows device instance
  paths for Bluetooth-attached HID devices are typically distinguishable
  from USB ones, e.g. containing a BT-specific enumerator marker vs. a
  USB one), but this needs to be CONFIRMED empirically against this
  exact controller/machine, not assumed from general knowledge.
- **Parsing the report once connected is the real unknown.** `Controller.
  cs` was built and tested exclusively against a USB-connected DualSense
  (confirmed by the user's own admission that every prior test had it
  already plugged in). The DualSense's Bluetooth report format is known
  (from general community reverse-engineering, same spirit as this
  project's own credited Venator/Order52 sources) to differ from USB's
  -- a different report ID and extra framing/CRC bytes wrapping the same
  underlying data -- meaning `GetState()`'s report-parsing logic likely
  needs a second, transport-aware code path, not just a re-pointed HID
  handle. The existing sleep/wake reconnect logic's gap-detection
  threshold (tuned around USB's ~1000Hz report rate) will also likely
  need to be transport-aware, since Bluetooth's real polling rate is
  typically lower.
- **Verification plan when this is actually built**: pair a DualSense
  over Bluetooth on this machine, run the discovery flow, and confirm
  live (same standing practice as every other hardware claim in this
  file) that button/stick state actually reads correctly before calling
  Bluetooth support done -- do not ship this claiming Bluetooth support
  works from protocol-format reasoning alone, the way every other
  hardware protocol fact in this project has been treated.

**Nice bonus worth considering while in this code, not required**: once
a real `Controller.TryDiscover()`/holder-replace mechanism exists, the
Diagnostics window's existing `Rescan()` could be upgraded from
presence-only to an actual reconnect for the controller specifically,
removing that already-documented limitation for free. Not requested by
the user, just an obvious opportunistic improvement once the mechanism
exists -- don't over-scope the initial build chasing this, but keep it
in mind.

### Summary for whoever builds this next

Four pieces of new scope make up this V2 phase, all confirmed with the
user and none started in code:
1. Idle screensaver (multi-effect playlist, cycle interval, randomizer).
2. Low-battery lighting override (configurable color, keyboard +
   lightbar, wins over the screensaver when both conditions are true).
3. Controller hot-discovery (USB + Bluetooth, a mutable-holder refactor
   plus real Bluetooth report-format verification).

Features 1 and 2 are both "override the current effect" mechanisms
sharing the exact same stash-and-restore shape as the existing
controller-reactive enable/disable (Phase 5) -- given the priority-
nesting concern called out under Feature 2, seriously consider building
a single shared coordinator/stash mechanism for all three rather than
independent implementations that each have to know about the others'
state to nest correctly. This was not explicitly requested by the user
but is a strong architectural recommendation based on how the design
shook out. Feature 3 is architecturally unrelated to the other two (a
connection-management refactor, not an effect-override mechanism) and
can be built independently of them in any order.

**Do not start any of this without the user's explicit go-ahead in that
session** -- re-read this whole section first if resuming cold, but the
standing instruction is to wait, not proceed.

## Phase 9: "rain" effect rework -- DONE, built and shipped (2026-09-11, thirteenth session)

A collaborative, purely-iterative live-tuning session on the `rain`
effect (`RainEffect` in `windows/src/JmaStudio.Effects/Effects/
PositionalEffects.cs`), explicitly scoped by the user to the C# port
only (matches Phase 8's own "C# only" scope note -- `effects/rain.py`
on the Python side was NOT touched and still has the original bug).
Unlike Phase 8, none of this was planning -- every change below was
built, redeployed to the real installed Service, and confirmed live one
step at a time before moving to the next.

**Real bug found and fixed**: with the ORIGINAL default params
(`spawn_rate=3, speed=10, tail=2.5`), the `numSlots` formula produced
only 2 concurrent "drop slots," and each slot's column was a pure
function of its slot INDEX alone (`PseudoRandom.Value01(slot, 1)`,
never re-rolled) -- meaning exactly 2 columns, forever, every session,
confirmed empirically at columns ~1.29 and ~2.20 out of an ~18-column-
wide keyboard. This is why the user observed "only using the first 5
rows" -- really "only a narrow 2-column strip near the far-left edge,"
which happens to make row 5 (whose populated columns start around
column 9) essentially unreachable. **Fixed**: each slot's column is now
re-rolled every time it restarts its fall, by folding the current CYCLE
ITERATION into the existing salted hash (`PseudoRandom.Value01(slot, 1,
cycleIndex)`) -- confirmed via a live simulation that this raises
distinct-columns-visited from 2 to 169 over 200 simulated seconds, and
reaches row 5. Still a pure function of `t`, no state persisted, same
`SpawnRate`/`Speed`/`Tail` semantics as before.

**Live-tuned parameter changes** (`RainParams`, same file):
`Speed` 10.0 -> 2.5 (iterated live: 7.5, 5.5, 2.5), `SpawnRate` 3.0 ->
6.0. `Color` (the everyday drop color) changed from the original
(90,160,255) to (0,0,200) -- the user asked me to "remember" whatever
color key `d` happened to be showing live at the time (it was
`custom_keys`, not `rain`, active at that moment -- flagged to the user
before recording it, confirmed as intended). The ORIGINAL (90,160,255)
became a new `AccentColor` field instead of being discarded -- see
"accent drop" below.

**New behavior: fade-in.** A cell used to snap straight to full
brightness the instant a drop's head reached it, then fade out over the
tail -- no ramp-up at all. Added a short (0.5-row) fade-in immediately
ahead of the head (`behind` in `[-0.5, 0)`), so a cell brightens
quickly as a drop approaches instead of popping on, then the existing
fade-out continues unchanged. Confirmed live: "O that is so good."

**New behavior: shower intensity varies over time** ("like how a real
rain shower speeds up and slows down"). `SpawnRate` is now the PEAK
density; the effective density drifts down to a separately-configurable
floor (`RainParams` doesn't expose this as a field currently -- it's a
local `lowSpawnRate = 2.0` constant in `RainEffect.RenderTyped`, set via
live iteration: started as `SpawnRate * 0.5`, then fixed at `2.0` on
request) and back, via two summed sine waves (~78.5s and ~299s periods,
deliberately unrelated so the drift never feels mechanically repetitive)
combined and reshaped with `Math.Pow` so the wave spends real, sustained
time near both ends rather than a raw sine's brief instantaneous touch-
and-bounce at its extremes (found live: "never feels like it ramped
down, only a quick moment"). **Asymmetric per the user's explicit
request** ("bias the lower end for twice as long"): the low
(negative-`rawWave`) side uses exponent 0.25 (aggressive flattening,
long dwell), the high side uses 0.6 (shorter dwell) -- these are a
live-tuned starting point, not derived from an exact mathematical 2:1
time ratio; retune by feel if it doesn't feel exactly right after
further use. The number of ACTIVE slots (not their individual timing)
tracks this intensity, counting up from slot 0, with only the one slot
currently straddling the threshold getting a fractional alpha so
raising/lowering intensity fades that single slot smoothly instead of
every slot popping at once.

**New architecture, not scoped to just this effect: "time since this
effect was activated."** `t` (the render loop's parameter) is the
Service's raw global uptime -- it does NOT reset when an effect is
switched on, so a fixed sine phase baked in at "t=0" would only
actually align with Service startup, not with whenever the user
happens to switch to `rain`. Per the user's request ("always start at
the lower peak so it always builds up at the start"), added:
- `DaemonState`: a private `Stopwatch` clock plus `_effectStartTime`,
  stamped every `SetEffect()` call; exposed as a new public
  `EffectStartTime` property.
- `EffectContext` (`JmaStudio.Effects/IEffect.cs`): new `EffectStartTime`
  field.
- `RenderLoopService`: populates it from `_state.EffectStartTime` each
  frame, alongside the existing `KeyState`/`ControllerState` injection.
- `RainEffect` computes `tSinceActivation = t - context.EffectStartTime`
  and uses THAT (not raw `t`) for the shower-intensity sine phases,
  chosen (`-π/2` on both terms) so `rawWave` equals exactly -1 (the true
  low point) at `tSinceActivation = 0`. The individual drops' own fall
  timing was deliberately left on raw `t` -- only the shower-intensity
  cycle needed this fix. **Any future effect can use this same
  `EffectContext.EffectStartTime` field** for "time since I was turned
  on" semantics -- it's not Rain-specific plumbing.

**New behavior: accent drop.** Once every random 10-20 seconds
(`AccentMinGapSeconds`/`AccentMaxGapSeconds`, new `RainParams` fields,
started at 3-10s per the original request then widened live), exactly
ONE extra drop falls in `AccentColor` (the original 90,160,255 blue,
preserved rather than discarded when `Color` changed) -- deliberately
NOT scaled by `SpawnRate`/density like the main shower, since it's meant
to read as a rare highlight, not part of the regular rain. Implemented
as a single independent lane reusing the exact same head/tail/fade-in
math as the main slots. Since each gap's length is itself random (not a
fixed cycle), there's no closed-form "which cycle am I in" the way the
main slots have -- it walks forward from `tSinceActivation = 0`, summing
randomized gap lengths, until passing the current time (capped at
100,000 iterations as a defensive bound; in practice a handful of
iterations even after hours of uptime, given a ~15s average gap).

**Verified live, every step, on the real installed Service** (not dev
mode) -- each change was built, redeployed via the same stop-service /
`dotnet publish -o` / start-service cycle established in Phase 7, and
confirmed by the user directly on the physical keyboard before moving
to the next change. This is the same real-hardware verification bar
every other phase in this file has used.

**Installer rebuilt** (`windows/installer/build.ps1`) to bundle all of
the above -- a fresh install now ships with the reworked `rain` effect,
not just this session's directly-redeployed live instance.

**Explicitly NOT pushed to GitHub yet, per the user's direct
instruction** ("commit this, but don't upload to github. For github
this will be part of V2") -- committed locally on `main` (already
merged/public from the eleventh session), but this commit should stay
local/unpushed until Phase 8's V2 work is ready to go out together with
it. Whoever picks this up next: check `git log`/`git status` before
assuming what's actually been pushed to `origin` matches local `main`.

## Immediate live state as of writing this (2026-09-12, end of fourteenth session)

**This section supersedes every "immediate live state" note above it in
this file — only trust this one.**

- **Phase 8's Feature 1 (idle screensaver) is fully built, redeployed to
  the real installed Service+GUI, and confirmed working live** —
  playlist cycling, single-entry no-op re-apply, ListBox theming, the
  top-bar green checkmarks, and the "Lights Out" sentinel were all
  exercised live by the user over the course of this session. The
  currently-running installed instance reflects all of this session's
  code, since every change was redeployed (stop service → `dotnet
  publish -o` into both `Service\` and `Gui\` → start service → manually
  relaunch `JmaStudio.Gui.exe`, since the redeploy script does NOT
  auto-relaunch the GUI the way it does the Service).
- **Not yet configured with real settings** — the screensaver was
  exercised functionally (playlist behavior, theming, checkmarks,
  Lights Out) but has not been left ENABLED with the user's actual
  desired real-world settings (threshold, playlist, lightbar choice) as
  of session end. Check `GET /idle-screensaver/config` fresh next time
  rather than assuming any particular Enabled state.
- **Committed locally, still deliberately NOT pushed to `origin`** —
  same standing plan as Phase 9 (rain): this bundles into a future V2
  GitHub push once the user decides V2 is ready to go out, not pushed
  phase-by-phase. Check `git log origin/csharp-port..csharp-port` (or
  `main`, whichever branch is checked out) before assuming what's
  actually public.
- **Features 2 (low-battery override) and 3 (controller hot-discovery)
  are still 100% design-only** — nothing built, see "Phase 8" above for
  the complete designs. The user's go-ahead this session was specifically
  "start with the screen saver," not blanket authorization for the rest
  of V2 — confirm before starting either of the remaining two features.
- **Start here next time**: (1) confirm current live state fresh, same
  checks as always, plus `GET /idle-screensaver/config` specifically;
  (2) if continuing V2, ask which of Features 2/3 to build next rather
  than assuming; (3) if wrapping up V2 for a release, that's the point to
  revisit pushing Phase 8 + Phase 9's local commits to `origin` together,
  per the user's own stated plan.

## Immediate live state as of writing this (2026-09-11, end of thirteenth session)

**This section supersedes every "immediate live state" note above it in
this file — only trust this one.**

- **The `rain` effect rework (Phase 9) is fully built, live-verified, and
  running on this machine right now** — the real installed Service was
  redeployed roughly a dozen times over the course of this session, one
  small change at a time, each confirmed by the user directly on the
  physical keyboard before moving on. Current live state: `Speed=2.5`,
  `SpawnRate=6.0` (peak), a fixed `lowSpawnRate=2.0` floor the shower
  intensity drifts toward, `Color=(0,0,200)`, `AccentColor=(90,160,255)`
  appearing once every 10-20s. All of this is also baked into a freshly
  rebuilt `windows/installer/output/JmaStudio-Setup.exe`.
- **Committed locally, deliberately NOT pushed to `origin`** — the user
  was explicit: "commit this, but don't upload to github. For github
  this will be part of V2." Check `git status` and `git log
  origin/main..main` (or equivalent) before assuming local `main`
  matches what's actually public — it won't, until Phase 8 is also done
  and both go out together.
- **New reusable piece, not just a Rain-specific hack**:
  `EffectContext.EffectStartTime` (populated by `RenderLoopService` from
  a new `DaemonState.EffectStartTime`, stamped on every `SetEffect()`
  call) — any future effect that wants "seconds since I was turned on"
  instead of the Service's raw global uptime can read this directly.
- **Explicitly Python-untouched**: `effects/rain.py` on `main`/repo root
  still has the original bug (permanently-fixed 2 columns) and none of
  this session's improvements. Confirmed in scope discussion this
  session ("this V2 only applies to C#") — the same scoping applies
  here even though Phase 9 isn't technically part of Phase 8's V2 design,
  since it was raised and built in the same session under the same
  framing.
- **Start here next time**: (1) confirm current live state fresh, same
  checks as always, plus specifically check the `rain` effect's live
  params via `GET /status` after applying it, to confirm the values
  above are still what's deployed; (2) if picking Phase 8 back up, build
  from that design, and when ready to publish, push BOTH Phase 8's and
  Phase 9's local commits together per the user's stated plan; (3) don't
  push anything to `origin` before that point without the user
  explicitly asking.

## Immediate live state as of writing this (2026-09-11, end of twelfth session)

**This section supersedes every "immediate live state" note above it in
this file — only trust this one.**

- **The machine is in a clean, normal working state**: JMA Studio C#
  installed and running (Service + GUI, window open), "Red Chase"
  active, `AcerLightingService` correctly Stopped/Disabled, Python
  fully stopped. No test artifacts or leftover processes from this
  session's benchmark work.
- **Nothing was changed in code this session** — this session was
  entirely a live benchmark (see "Phase 7 continued" above) plus design
  work (see "Phase 8" above). `git status` should be clean; there is
  nothing to commit from this session unless a future session is told
  otherwise.
- **The repo is now public on GitHub**, merged to `main`
  (`https://github.com/jadamsky/jma-studio-rgb-keyboard`), with a
  published release (`csharp-v1.0.0`) carrying a prebuilt
  `JmaStudio-Setup.exe` as a downloadable asset. `csharp-port` branch
  still exists, now fully merged into `main` (both point to equivalent
  history as of the eleventh session's merge).
- **THE ONE THING TO KNOW BEFORE DOING ANYTHING ELSE**: Phase 8 (idle
  screensaver + low-battery override) is fully designed in this file
  but the user was explicit that this is planning only —
  **do not write any implementation code for it without an explicit
  go-ahead**, even if a fresh session is told to "read the handoff and
  continue." This is the OPPOSITE standing instruction from Phase 7's
  `/keypress` fix (which had standing authorization to just proceed) —
  don't confuse the two. If the user's next message doesn't clearly
  greenlight starting Phase 8, ask rather than assume.
- **Start here next time**: (1) confirm current live state fresh, same
  checks as always; (2) if the user gives the go-ahead for Phase 8,
  build from the design in that section above — it's complete, including
  the one open question (lightbar playlist vs. fixed preset) flagged for
  a quick confirmation before or during the build, and the priority-
  nesting concern between the screensaver's and battery-override's
  stash/restore logic; (3) otherwise, the three small non-blocking
  Phase 7 items noted in the eleventh-session block below are still the
  next real work if the user wants to close those out instead.

**This section supersedes every "immediate live state" note above it in
this file — only trust this one.**

- **JMA Studio C# is installed and running cleanly** — the user
  manually stopped Python, then this session ran a full three-cycle
  install → uninstall → install → uninstall → install test loop against
  this exact machine (which has both `AcerLightingService` and the
  Python autostart task present, a good real test bed). Every fix from
  this session is now confirmed live — see "Uninstall re-enabling
  AcerLightingService..." above for the complete list and evidence:
  the `AcerLightingService` ask-first prompt (user chose Yes, manually
  confirmed PredatorSense regained control), the install-time Python-
  autostart-disable, the uninstall-time Python-autostart-re-enable
  prompt, and a NEW empty-directory-cleanup fix found and fixed
  mid-session (found by the user immediately noticing leftover empty
  `Service`/`Gui`/root folders, fixed with explicit `RemoveDir()` calls,
  then re-verified clean on a subsequent uninstall cycle).
- **Final state confirmed**: `JmaStudioService` RUNNING, `JmaStudio.Gui.exe`
  running (tray icon present), `AcerLightingService` Stopped/Disabled
  (this install's own startup check correctly re-disabled it —
  independent of and after the uninstall-time re-enable the user chose
  in the PRIOR uninstall test), `GET /status` shows "Red Chase"
  (`typing_reactive`) active, keyboard connected. Python is fully
  stopped (no `python.exe` processes). This is a genuinely clean,
  fully-working, fully-tested state — not a leftover test artifact.
- **`windows/installer/JmaStudio.iss` has all this session's fixes
  applied and compiled, but is still UNCOMMITTED** as of this writing —
  see "Start here next time" below.
- **New this session, also uncommitted**: root `CREDITS.md` updated to
  extend its existing Venator/Order52 attribution (previously only
  covering `hardware/device.py`) to also cover
  `windows/src/JmaStudio.Hardware/Keyboard.cs` on this branch, since the
  C# port carries forward the exact same reverse-engineered protocol
  facts (not code) — see CREDITS.md itself for the full wording. Also
  added a short note crediting the C# port's own agentic build session,
  pointing to this file.
- **User's next ask, not yet started**: figure out how to actually get
  the `csharp-port` branch onto GitHub (`origin` = 
  `https://github.com/jadamsky/jma-studio-rgb-keyboard.git`) — confirmed
  via `git branch -a`/`git rev-parse origin/csharp-port` that this
  branch has NEVER been pushed to origin yet (`main` and `stable` both
  exist on origin; `csharp-port` doesn't). This needs a real plan/
  discussion with the user before pushing anything to a public remote —
  see whatever the next part of this session's transcript settled on,
  or ask the user directly if this file is being read cold with no
  further context on that decision.
- **Start here next time**: (1) confirm current live state fresh (same
  checks as always) before assuming the above is still true; (2) commit
  the installer fixes + CREDITS.md update if not already committed
  (check `git status` — the user asked for this explicitly at the end of
  this session); (3) follow up on the GitHub push question above if it
  wasn't resolved.

## Immediate live state as of writing this (2026-09-11, end of ninth session)

**This section supersedes every "immediate live state" note above it in
this file — only trust this one.**

- **A REAL installed `JmaStudioService` + `JmaStudio.Gui.exe` are
  running right now**, from a clean fresh install done this session
  (uninstalled everything, then reinstalled via a freshly-built
  `Setup.exe`) — `sc query JmaStudioService`: `STATE: RUNNING`,
  `AcerLightingService`: `STATE: STOPPED`. "Red Chase" (`typing_reactive`)
  is the active keyboard effect, lightbar all 3 zones blue, controller
  connected with reactive disabled — this is a genuinely clean, fully
  working state, not a leftover test artifact.
- **`C:\ProgramData\JMA Studio\` was fully wiped this session** (the
  user chose "No, delete" during the uninstall test) then fully
  reseeded from the installer's bundled defaults on the subsequent
  reinstall — so current presets/config are back to the factory-bundled
  set (same data `windows/data/*.json` in git holds), not whatever
  state existed before this session's uninstall. If any preset editing
  happened between the eighth and ninth sessions that was never
  reflected in `windows/data/`, it's gone; there's no reason to believe
  that happened, but worth knowing this reset occurred.
- **`windows/installer/JmaStudio.iss` has uncommitted changes from this
  session**: the two uninstall fixes (`WaitForFileUnlocked`, GUI
  `taskkill`) and the (currently unsuccessful) "Preparing to Install"
  theming attempts — see "Uninstall live-tested" above for the full
  story. **Neither uninstall fix has actually been re-verified with a
  real uninstall test yet** — this is the single most important thing
  to do before trusting them.
- **`windows/installer/output/JmaStudio-Setup.exe` on disk right now
  reflects the LATEST compile** (the one with the failed 3rd theming
  attempt still in place) — safe to use for further testing, just know
  the Preparing-to-Install page will still be unreadable if it ever
  shows up again (only appears when a locked file is detected, which
  won't happen on a normal fresh-machine install).
- **New backlog item, this session**: the Restart Manager "Preparing to
  Install" page's contrast bug (3 fix attempts failed — see above),
  explicitly backlogged by the user ("add this for fix later... move
  on"). **Do not attempt a 4th fix without new evidence** — see that
  section's closing paragraph for what "new evidence" should look like
  here.
- **Sleep/hibernate/restart resilience testing — sleep and hibernate
  DONE this session (both passed cleanly), restart NOT YET DONE.**
  Directly motivated by the long-backlogged "`Keyboard.cs` has no
  sleep/hibernate reconnect logic" item (Phase 6.5 above), now tested
  for real instead of left theoretical. Order was sleep → hibernate →
  restart (user's choice).
  - **Sleep (standby), tested twice — clean pass, no reconnect logic
    needed at all.** Same Service/GUI process survived both cycles
    (PID unchanged, uptime counted straight through matching real
    elapsed wall-clock time — never restarted), keyboard/lightbar/
    controller all stayed connected with no gap, HID/WMI latency
    unchanged from the pre-sleep baseline, and the typing_reactive chase
    worked immediately on waking both times. The backlogged "Keyboard.cs
    has no sleep reconnect logic" concern turns out to be moot for sleep
    specifically on this hardware/driver stack (unlike the controller,
    which the Python side found genuinely does need reconnect logic
    after sleep — that fix already exists in `Controller.cs`, ported in
    Phase 5). One unrelated oddity the user noticed (Microsoft Access
    opening on wake, twice) — confirmed this is NOT anything in this
    codebase (zero Office/Access integration anywhere in
    `JmaStudio.Gui`/`JmaStudio.Service`); most likely it was already
    open before sleep and Windows just restored its window, or an
    unrelated scheduled task/Startup entry. Not investigated further,
    not this project's concern.
  - **Hibernate, tested once — process survived (same PID, hibernate on
    this machine is a real suspend-to-disk/restore, not a reboot), but
    found ONE real, understood, WON'T-FIX cosmetic gap.** After waking
    from hibernate, the keyboard briefly showed its BIOS/firmware
    default lighting even with the desktop fully up, then snapped
    correctly into "Red Chase" on the very first keypress. Root cause
    (reasoned through, not yet verified by code inspection or a fix
    attempt): `RenderLoopService`/`DaemonState.RecordFrame` only writes
    a new HID frame when the computed frame differs from the last frame
    actually sent — a real, otherwise-correct optimization. Hibernate
    fully power-cycles USB devices, so the physical keyboard resets to
    its own firmware default on power-up, independent of software. But
    hibernate restores the Service's RAM byte-for-byte, so its cached
    "last frame sent" is unchanged across the cycle — on resume, the
    render loop correctly computes "this matches what I last sent" and
    skips the write, unaware the physical device forgot everything. The
    first genuinely new frame (the keypress-triggered bolt) finally
    differs from that stale cache, forcing a real write that corrects
    it. A real fix would hook Windows' power-resume event
    (`Microsoft.Win32.SystemEvents.PowerModeChanged`, resume case) and
    force one unconditional frame re-push on wake. **The user explicitly
    said "I don't think I want to fix that" — this is a WON'T-FIX, not
    a backlog item.** Do not implement this fix unless the user
    explicitly reopens it.
  - **Restart, tested (tenth session, after a context compaction and
    resume via "read the windows handoff doc and continue") — clean
    pass, and this closes out the LAST open unknown from Phase 7.** New
    PIDs confirmed after resume (Service 5680, GUI 25700 — both
    different from the pre-restart baseline's 38056/26292, proving this
    was a genuine cold boot, not a resume), both services back in their
    correct states (`JmaStudioService` RUNNING, `AcerLightingService`
    STOPPED), "Red Chase" correctly reapplied as the default, keyboard/
    lightbar/controller all connected. **The user's own real-world
    observation, unprompted, answers the long-open boot-timing
    question**: "the keyboard and backlight background colors pickup
    faster th[a]n the predator sense software ever did" — i.e. the
    Automatic-start Windows Service (settled decision #2) achieves
    exactly the pre-login clean-boot goal that originally motivated
    keeping a true service instead of a Scheduled Task (see Phase 7's
    "Critical finding" section above). **One expected, correctly-
    understood non-issue, also the user's own words**: "the reactive
    side didn't pick up until I was fully logged in... I guess to be
    expected as the hook is connected to the app and not the service."
    Exactly right — `KeypressForwarder`'s global hook lives in
    `JmaStudio.Gui` (Session 1+, only starts once a desktop session
    exists), while the background color comes from the Service (Session
    0, starts well before login) — this is the correct, designed
    tradeoff of the `/keypress` fix, not a gap. User's closing verdict:
    "GOOD JOB."
- **Phase 7 is now functionally complete.** All three items that were
  blocking it are resolved: the Session 0 reactive-input fix (user-
  confirmed working), the wizard-image white-margin fix (never
  complained about across ~5 wizard runs this session, reasonably
  confident though never asked about directly), and now real cold-boot
  timing (confirmed better than PredatorSense ever was). **Two small,
  explicitly non-blocking items remain, neither gating Phase 7**:
  1. The two uninstall fixes (`WaitForFileUnlocked`, GUI `taskkill`,
     ninth session) are still NOT re-verified with an actual uninstall
     test — do this before fully trusting the uninstaller, but it
     doesn't block calling Phase 7 done since the installer/upgrade
     path itself is fully proven.
  2. The Restart Manager "Preparing to Install" page's contrast bug —
     explicitly backlogged, 3 fix attempts failed, do not re-attempt
     without new evidence (see "Uninstall live-tested" above).
  Also explicitly WON'T-FIX (user's own words, tenth session): the
  hibernate cosmetic gap (brief BIOS-default flash until first
  keypress) — do not implement the `SystemEvents.PowerModeChanged`
  fix described above unless the user reopens this themselves.
- **Old "eighth session" state below this point is superseded** by
  everything above, but kept for its still-relevant Phase 7 file-change
  list.

## Immediate live state as of writing this (2026-09-10, end of eighth session)

**This section supersedes every "immediate live state" note above it in
this file — only trust this one.**

- **A REAL installed `JmaStudioService` is running right now, with the
  `/keypress` fix's binaries** — registered as an actual `AUTO_START`
  Windows Service, `binPath` at `C:\Program Files\JMA
  Studio\Service\JmaStudio.Service.exe`, running as `LocalSystem`.
  Confirmed via `sc query JmaStudioService`: `STATE: RUNNING`. This
  session republished BOTH `JmaStudio.Service` and `JmaStudio.Gui`
  (self-contained single-file `win-x64`, same as `build.ps1` does)
  directly over the live install (`sc stop` → `Sleep 2s` → `dotnet
  publish -o` straight into the installed folders → `sc start`), so the
  binaries on disk right now are the fixed ones, not the ones Phase 7's
  seventh session left behind. `JmaStudio.Gui.exe` is also running
  (launched fresh, unelevated, after the redeploy), tray icon present,
  confirmed via `tasklist`.
- **The Session 0 reactive-input bug is FIXED and user-confirmed live**
  ("I just tested your live update, it works") — see Phase 7's "Fix
  built and verified (2026-09-10, eighth session)" for the full
  implementation writeup. `typing_reactive`-family effects and the
  lightbar reactive flash now correctly react to real keystrokes against
  this real installed service.
- **`AcerLightingService` is currently Stopped/Disabled** — unaffected by
  this session's redeploy, still handled correctly by the Service's own
  startup check.
- **Data lives at `C:\ProgramData\JMA Studio\`** — more precisely,
  `JMASTUDIO_DATA_DIR` resolves to `C:\ProgramData\JMA Studio\data\`
  (presets, live state, AND `logs\service.log` all live directly under
  that `data\` subfolder — not a sibling of it) and
  `JMASTUDIO_KEYMAP_PATH` to `C:\ProgramData\JMA Studio\keymap.json`
  (one level up from `data\`). Worth knowing exactly since a future
  session will want to tail the log or inspect presets again.
- **The Python stack is fully stopped**, untouched since earlier
  sessions.
- **New this session** (all in `JmaStudio.Gui`, none yet committed):
  `WindowsKeyMap.cs`, `GlobalKeyboardHook.cs` (with the new `KeyUp`
  event), `KeypressForwarder.cs`. Modified: `App.xaml.cs` (wires
  `KeypressForwarder` in alongside the tray icon), `ApiClient.cs` (new
  `PostKeypressAsync`). Service side, also modified: `InputListener.cs`
  (`OnKeyDown` → public `RecordKeyDown`), `Endpoints.cs` (new
  `MapInput`, updated header comment), `RequestModels.cs` (new
  `KeypressRequest`), `Program.cs` (`inputListener.Start()` now gated
  behind `JMASTUDIO_LOCAL_KEY_HOOK=1`, off by default; new
  `Endpoints.MapInput` call site).
- **One thing to re-verify, still not done**: the `WizardImageStretch=yes`
  fix for the installer's wizard-image white-margin bug (made in the
  seventh session) was never re-tested live — the seventh session moved
  straight to the Session 0 investigation, and this eighth session
  worked directly against the live install rather than through the
  installer/uninstaller flow, so it still hasn't been exercised. Quick
  to check next time: run `windows\installer\build.ps1` (to bundle
  THIS session's `/keypress` fix into a fresh `Setup.exe` — the
  `output\JmaStudio-Setup.exe` left over from the seventh session
  predates this fix), then run the result and look at the Welcome/
  Finished pages for the white margin.
- **Three backlogged, NOT-fixed known issues** — do not start on any of
  these without the user raising it again:
  1. See "Phase 6 continued: Lightbar window" above: an all-zone
     lightbar flash (keyboard zone 4) still shows zone 1 slightly out
     of sync with zones 2/3 on real hardware.
  2. See "Phase 6.5: Tray icon" above: `Keyboard.cs` has no
     sleep/hibernate reconnect logic, unlike `Controller.cs`/`Lightbar`.
  3. See "Phase 7" above: the exact real-world boot timing of the
     installed service relative to the login screen hasn't been watched
     through an actual cold reboot yet.
- **Start here next time**: (1) confirm current live state fresh (`sc
  query JmaStudioService`/`AcerLightingService`, tray icon present, `GET
  /status`) rather than trusting this note blindly, since time may have
  passed; (2) run `windows\installer\build.ps1` to produce a fresh
  installer that actually includes this session's `/keypress` fix (the
  existing `output\JmaStudio-Setup.exe` does NOT), then run it and
  re-verify the `WizardImageStretch` fix on the Welcome/Finished pages;
  (3) once that's confirmed, do an actual cold-reboot timing check; (4)
  after both of those, Phase 7 is fully done; (5) don't revisit any of
  the three backlogged items above without the user raising them first.
  Nothing from this session has been committed yet — check `git status`
  before assuming otherwise.
