# JMA Studio — Custom Per-Key RGB Control for the PH16-71

A DIY replacement for Acer PredatorSense's per-key keyboard lighting on
the Predator Helios 16 (PH16-71), because PredatorSense — and the
OpenRGB-via-PredatorSense bridge — only expose a handful of canned
firmware effects, despite this keyboard being genuinely per-key
addressable. This project computes lighting entirely in software and
pushes full 128-cell frames straight to the keyboard's own USB HID
protocol, so any effect that can be expressed as `render(t, cell) ->
(r, g, b)` is possible — not just whatever Acer chose to ship.

**This was built agentically**: the protocol implementation, the
background daemon, every lighting effect, the system tray icon, the
standalone desktop GUI, the Windows autostart wiring, and this
documentation were all built through an agentic coding session with
[Claude](https://claude.com) (Anthropic), directed by Jake Adamsky.
See [CREDITS.md](CREDITS.md) for full attribution, including the two
open-source projects whose reverse-engineering of the USB protocol
made this possible.

This repo exists primarily as a **personal backup** — so a wiped or
replaced machine doesn't mean losing this setup. If you own a PH16-71
too, it should work for you as-is, but it isn't a polished general
product.

## What it does

- **Per-key gradient zones** — replicates (and generalizes to 2–5
  zones) PredatorSense's own static zone-based coloring.
- **Custom Key Colors editor** — a full click-to-paint editor in the
  GUI: select any key (or several at once) and give it its own exact
  color, with a recently-used color palette for quickly reusing shades
  across keys, and a "pull current colors" button to snapshot whatever
  effect is currently live into an editable starting point.
- **Reactive typing** — a bright flash plus a chasing "bloom" that
  travels outward across the physical key grid from whatever you just
  pressed, with tunable shape (star-pattern rays or a true radial
  ring), color style (solid or a chaotic rainbow), speed, tail length,
  maximum travel distance, and an optional "restart instead of
  overlap" mode for repeated presses of the same key.
- **Presets** — save/load/set-default any combination of the above.
- **A system tray icon** for one-click preset switching, and a
  standalone native GUI (live keyboard preview mirroring the actual
  hardware, preset gallery, and live-tunable parameters for every
  effect).
- **Autostart on login with no UAC prompt** — a Scheduled Task
  configured to run elevated silently at login, since stopping
  Acer's own `AcerLightingService` (so it doesn't fight over the
  hardware) needs admin rights.

## Hardware requirement

This targets one specific keyboard controller: a Chicony MCU at USB
VID `04F2` / PID `0117`, found in the Acer Predator Helios 16 (PH16-71).
It will not work on other keyboards without re-deriving the protocol
for that hardware. `keymap.json` (which key name maps to which of the
128 addressable cells) is also specific to this exact keyboard layout.

## Installation (fresh machine / after a reinstall)

**Prerequisites**: Windows, [Python 3.10+](https://www.python.org/downloads/)
(check "Add python.exe to PATH" during install), and this repo cloned
locally.

### Quick path

```
git clone <this-repo-url>
cd RGB
.\setup.ps1
```

`setup.ps1` will (one UAC prompt, for the last step):
1. Create a `.venv` virtual environment and install `requirements.txt`.
2. Place the bundled `hidapi.dll` where the `hid` package needs it
   (`.venv\Scripts\hidapi.dll` — pip does **not** install this native
   library itself, only the Python wrapper around it, so this step is
   easy to forget when setting up by hand).
3. Create a `RGB Keyboard.lnk` shortcut on your Desktop (targeting
   `start_all.bat`, using the app's own icon).
4. Register a **Scheduled Task** ("JMA Studio Autostart") that starts
   everything at login, elevated, with no UAC prompt.

Then just log out and back in, or double-click the new Desktop
shortcut / run `start_all.bat` directly to start it immediately.

### Manual path (if you'd rather not run a script, or need to redo one step)

1. `python -m venv .venv`
2. `.venv\Scripts\pip install -r requirements.txt`
3. Copy `hidapi.dll` (bundled at the repo root) into `.venv\Scripts\hidapi.dll`.
4. Test it: `.venv\Scripts\python.exe cli.py static ff0000` — the whole
   keyboard should go solid red. If this fails with a DLL/library load
   error, re-check step 3.
5. Run `start_all.bat` — this stops `AcerLightingService`, starts the
   background daemon (`uvicorn daemon.server:app --port 8420`), and
   starts the tray icon.
6. (Optional) Set up autostart yourself instead of via `setup.ps1`, in
   an elevated PowerShell:
   ```powershell
   $root = "C:\path\to\this\repo"
   $action = New-ScheduledTaskAction -Execute "$root\start_all.bat" -WorkingDirectory $root
   $trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:COMPUTERNAME\$env:USERNAME"
   $principal = New-ScheduledTaskPrincipal -UserId "$env:COMPUTERNAME\$env:USERNAME" -LogonType Interactive -RunLevel Highest
   Register-ScheduledTask -TaskName "JMA Studio Autostart" -Action $action -Trigger $trigger -Principal $principal -Force
   ```

### If you're setting this up on a *different* PH16-71 for the first time

`keymap.json` here is already filled in for this exact laptop and
should just work, since it's the same model. If it doesn't (a
different chassis revision, etc.), rebuild it with the discovery tool:
`python cli.py discover` — it lights one cell at a time and asks you
to type which physical key just lit up.

## Using it

- **Tray icon**: left-click opens the GUI directly; right-click for
  the full menu (presets, quick effects, off, quit).
- **GUI** (`python gui.py`, or via the tray icon): live keyboard
  preview, preset gallery, quick-effect chips, and full tuning panels
  for the gradient, reactive typing, and custom key color effects.
- **CLI** (`cli.py`): scriptable access to the same daemon API — see
  `python cli.py --help`.

## Architecture

```
hardware/device.py   -- the only file that touches USB HID directly
daemon/server.py     -- background process; owns the HID connection,
                         the currently-running effect, a 30fps render
                         loop, and a small local HTTP API (port 8420)
effects/*.py         -- each file is one effect: a pure
                         render(t, num_cells, params) -> [(r,g,b), ...]
                         function, auto-discovered by the daemon
cli.py, tray.py,      -- thin HTTP clients of the daemon; none of them
gui.py + gui/            touch hardware or compute frames themselves
keymap.json           -- cell index -> physical key name, for this
                         exact keyboard
```

Nothing above `hardware/device.py` ever touches USB directly — new
effects are just new `render()` functions dropped into `effects/`, and
the GUI/tray/CLI are all interchangeable thin clients of the same
daemon.

## Adding your own effects

```python
# effects/my_effect.py
NAME = "my_effect"

def render(t, num_cells, params):
    # return a list of (r, g, b) tuples, one per cell index
    ...
```

Picked up automatically on daemon restart — nothing else needs to
change.

## Credits & License

- [CREDITS.md](CREDITS.md) — full attribution for the two open-source
  projects whose reverse-engineering of this keyboard's USB protocol
  made `hardware/device.py` possible, and for `hidapi.dll`.
- [LICENSE](LICENSE) — this project's own code is MIT-licensed. Note
  that the underlying protocol facts (not code) were derived from two
  GPL-licensed reference projects — see CREDITS.md for exactly what
  was and wasn't reused, and why that distinction matters.
