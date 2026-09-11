# Credits & Acknowledgments

This project would not exist without the reverse-engineering work of two
independent open-source projects that documented the PH16-71 keyboard's
actual USB lighting protocol. **Neither project's source code is copied
into this repo** — their implementations, structure, and naming are their
own — but the *protocol facts* they documented (opcode byte values, the
checksum formula, packet layout) are the foundation `hardware/device.py`
is built on, and — via a straight port, not a rediscovery — the same
foundation `windows/src/JmaStudio.Hardware/Keyboard.cs` (the C# port's
HID keyboard driver) is built on too. The `csharp-port` branch
reimplements the Python daemon/GUI in C#/.NET, independently written
throughout, but the underlying hardware protocol facts are the same ones
credited below, carried forward rather than re-derived.

## Venator

- https://github.com/Exyons/Venator
- License: GPL-2.0-only
- A Linux kernel driver for this exact keyboard, derived from a full
  USBPcap capture of PredatorSense's own USB traffic. This is the original
  source for the opcode byte values (the handshake, mode-select,
  write-color, and commit commands) and the checksum formula.

## Order52/ph16-71-rgb

- https://github.com/Order52/ph16-71-rgb
- License: GPL-3.0
- An independent Python implementation of the same protocol, used here to
  cross-validate Venator's checksum formula and opcodes against a second,
  unrelated source. Agreement between two independently reverse-engineered
  implementations is what makes this protocol trustworthy rather than
  guessed.

## What was reused, and what wasn't

- **Reused**: the numeric protocol facts above — opcode values, the
  checksum algorithm, packet sizes and layout. These describe how the
  physical hardware actually communicates over USB; they aren't
  copyrightable expression, just facts about a device.
- **Not reused**: neither project's source code, comments, or file
  structure. `hardware/device.py`'s constant *names* and all of its code
  are independently written — deliberately so, to keep this project free
  of any code-level relationship to either GPL-licensed source, regardless
  of what license this repo itself carries. See the module docstring in
  `hardware/device.py` for the full technical detail. The same is true of
  `windows/src/JmaStudio.Hardware/Keyboard.cs` on the `csharp-port`
  branch — a completely independent C# implementation, built from the
  same underlying protocol facts, not a translation of `hardware/device.py`'s
  own code.

## hidapi

- https://github.com/libusb/hidapi
- License: your choice of GPL-3.0, BSD-3-Clause, or the original
  permissive HIDAPI license — see that project's own `LICENSE.txt`.
- `hidapi.dll`, bundled at this repo's root purely for setup convenience
  (see `README.md`), is libusb/hidapi's compiled native library, used
  unmodified. It's what the `hid` Python package (a separate, MIT-licensed
  pip package, itself not modified either) talks to.

## Built agentically with Claude

The overwhelming majority of this project — the protocol implementation,
the background daemon, every lighting effect, the system tray icon, the
standalone desktop GUI, the Windows autostart wiring, the app icon, and
this documentation — was built through an agentic coding session with
[Claude](https://claude.com) (Anthropic), directed by Jake Adamsky, to
solve one specific real limitation: PredatorSense, Acer's own control
software, only exposes a handful of canned hardware lighting effects,
despite the PH16-71's keyboard being genuinely per-key addressable. This
project exists to actually use that hardware capability.

The full C#/.NET port on the `csharp-port` branch (a real Windows
Service + WPF GUI replacing the Python daemon and pywebview GUI
entirely, no Python at runtime) was built the same way, in a separate,
much longer agentic session — see `windows/HANDOFF.md` for the complete
build history, design decisions, and live-hardware verification record.
