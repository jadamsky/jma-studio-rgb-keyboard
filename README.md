# PH16-71 Custom RGB Keyboard Controller

A personal DIY replacement for PredatorSense's per-key lighting, built
because PredatorSense (and the OpenRGB-via-PredatorSense bridge) only
exposes a handful of canned firmware effects, while the actual keyboard
hardware is fully per-key addressable.

This is a **starter scaffold**, not a finished project — structured so
Claude Code can pick it up on your machine and fill in the real hardware
protocol against your actual laptop.

## Architecture

```
hardware/device.py   -- talks to the keyboard over HID (the only file
                         that needs real hardware to test)
daemon/server.py     -- background process, owns the HID connection +
                         the currently running effect, exposes a small
                         local HTTP API
effects/*.py         -- each file is one effect: a render(t, num_cells,
                         params) -> list[(r,g,b)] function
cli.py               -- talks to the daemon over HTTP, for testing
keymap.json          -- fill in via a "press each key" discovery step
```

Nothing above `hardware/device.py` ever touches USB directly. That's
intentional — effects, the CLI, and any future UI can all be built and
tested without needing to think about HID at all.

## Setup

```
pip install -r requirements.txt
```

## What's real vs. placeholder right now

- `effects/rainbow.py` and `effects/static.py` are fully working — the
  color math needs no real hardware to test.
- `hardware/device.py` has the right *shape* (VID/PID, 8-byte feature
  reports for simple commands, 8x64-byte per-key frames) based on what
  the Exyons/Venator project documents for this exact keyboard, but the
  exact opcode bytes are marked `TODO`. **Don't trust the placeholder
  byte values** — confirm them against:
    - https://github.com/Exyons/Venator (see `kernel/` — actively
      maintained, daily-driven on a real PH16-71)
    - https://github.com/Order52/ph16-71-rgb (Python implementation of
      the same protocol)

## First real milestones

1. Clone both reference repos and have Claude Code read their HID
   write calls, then fill in the `TODO`s in `hardware/device.py`.
2. Stop `AcerLightingService` (Windows Services) so nothing else holds
   the device open while you're testing.
3. Run the daemon: `uvicorn daemon.server:app --port 8420`
4. `python cli.py static ff0000` — confirm the whole board goes red.
5. Once that works, build the per-key keymap (press each key while a
   probe frame runs, log which cell lit up) and save it to
   `keymap.json`.
6. `python cli.py effect rainbow` — confirm the wave runs across the
   board.

## Adding your own effects

Drop a new file into `effects/`, following the same contract as
`rainbow.py`:

```python
NAME = "my_effect"

def render(t, num_cells, params):
    # return a list of (r, g, b) tuples, one per cell
    ...
```

It's picked up automatically on daemon restart — no other code needs to
change. This is also the shape to use when porting a design or
animation from Venator's own `designs/`/`animations/` folders, or
adapting a script originally written against `openrgb-python`.

## Not built yet (by design)

- A real UI — the daemon's HTTP API is meant to stay UI-agnostic, so a
  Tkinter/PySide6 desktop app or a small local web UI can be added
  later without touching anything below it.
- Reactive typing / audio-reactive effects — both are just new
  `render()`-style logic feeding into the same `send_frame()`, once the
  foundation above is solid.

## Note

This is a private, personal project — not intended for distribution.
Feel free to freely borrow structure and effect math from the reference
projects above for your own use.
