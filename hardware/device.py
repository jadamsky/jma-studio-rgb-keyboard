"""
HID interface to the Acer Predator PH16-71 per-key RGB keyboard.

Hardware facts and wire protocol (confirmed by cross-referencing two
independent reference implementations -- see below):

    - Keyboard lighting controller: Chicony MCU
    - USB VID: 0x04F2   PID: 0x0117
    - Lighting lives on a vendor-specific HID interface (usage page
      0xFF02), separate from the normal keyboard-input interface --
      writing here does not touch typing.
    - Every command is an 8-byte payload {op, p1, p2, p3, p4, p5, p6,
      checksum}, sent as a HID *feature* report (SET_REPORT, report ID
      0). checksum = (0xFF - (sum(op,p1..p6) & 0xFF)) & 0xFF.
    - A full apply is always a short *sequence* of these 8-byte
      commands, not a single one: BEGIN, then a MODE select, then
      color/frame data, then APPLY. This mirrors PredatorSense's own
      captured USB traffic.
    - Full per-key control additionally pushes eight 64-byte
      HID *interrupt-OUT* packets (512 bytes = 128 cells x {0x00, R,
      G, B}) between the MODE and APPLY commands.

Sources (both agree byte-for-byte -- Order52's captured trailing
"checksum" bytes reproduce exactly under Venator's checksum formula,
which is what makes this protocol trustworthy rather than guessed):
    - https://github.com/Exyons/Venator (kernel/venator-main.c,
      kernel/venator.h) -- actively maintained, daily-driven on a real
      PH16-71, derived from a full USBPcap capture of PredatorSense.
    - https://github.com/Order52/ph16-71-rgb (src/rgbkb/controller/
      commands.py, device.py) -- independent Python implementation,
      used here to cross-check Venator's opcodes/checksum.
"""

from __future__ import annotations

import os
import sys

# The `hid` package (pyhidapi) loads its native library with a bare
# ctypes.cdll.LoadLibrary("hidapi.dll") call. On Python 3.8+ Windows,
# ctypes no longer implicitly searches the venv's Scripts/ directory
# (or PATH) for bare-name DLL loads -- only an explicitly registered
# directory. hidapi.dll is expected to sit next to python.exe (i.e. in
# the venv's Scripts/ folder); register that directory before import
# so the load succeeds regardless of how the process was launched.
if sys.platform == "win32":
    _dll_dir = os.path.dirname(sys.executable)
    if os.path.isdir(_dll_dir):
        os.add_dll_directory(_dll_dir)

import hid

VENDOR_ID = 0x04F2
PRODUCT_ID = 0x0117
LIGHTING_USAGE_PAGE = 0xFF02  # vendor-specific interface

NUM_CELLS = 128          # per Venator: 128-cell per-key framebuffer
BYTES_PER_CELL = 4        # {0x00, R, G, B}
FRAME_PACKET_COUNT = 8
FRAME_PACKET_SIZE = 64    # 8 packets * 64 bytes = 512 bytes = 128 * 4

# Used for the APPLY command's BRIGHT byte when send_frame() doesn't
# take a brightness argument (its signature is fixed by the daemon's
# contract). Matches Venator's own default staged brightness.
DEFAULT_BRIGHTNESS = 200

# ---- wire protocol constants (see venator.h) --------------------------

OP_BEGIN = 0x88          # no params; sent before every apply
OP_MODE_ZONE = 0xB1      # select "simple" (zone/static) mode; no params
OP_MODE_PERKEY = 0x12    # select per-key mode; p3 = SCOPE_PERKEY
OP_SET_COLOR = 0x14      # p3,p4,p5 = R,G,B
OP_APPLY = 0x08          # p1=SUB_APPLY p2=EFF p3=MODE_TAG p4=BRIGHT p5=SCOPE p6=FLAG

SUB_APPLY = 0x02
MODE_TAG = 0x05          # unknown semantic, always 0x05 in every capture
FLAG_DEFAULT = 0x01      # probably "save to flash"; always 0x01 in every capture

EFF_STATIC = 0x01
EFF_PERKEY = 0x33

SCOPE_ZONE = 0x01
SCOPE_PERKEY = 0x08


def _checksum(body: bytes) -> int:
    """body is the 7 bytes {op, p1..p6}."""
    return (0xFF - (sum(body) & 0xFF)) & 0xFF


def _build_command(op: int, p1: int = 0, p2: int = 0, p3: int = 0,
                    p4: int = 0, p5: int = 0, p6: int = 0) -> bytes:
    """Build the 8-byte command payload {op,p1..p6,checksum}. Does NOT
    include the leading HID report-ID byte -- callers add that."""
    body = bytes([op, p1, p2, p3, p4, p5, p6])
    return body + bytes([_checksum(body)])


def find_lighting_device_path():
    """Enumerate HID devices and return the path of the FF02 vendor
    (lighting) interface on the PH16-71 keyboard controller, or None
    if it isn't found on this machine."""
    for dev in hid.enumerate(VENDOR_ID, PRODUCT_ID):
        if dev.get("usage_page") == LIGHTING_USAGE_PAGE:
            return dev["path"]
    return None


class Keyboard:
    """Owns the open HID handle to the lighting interface."""

    def __init__(self):
        path = find_lighting_device_path()
        if path is None:
            raise RuntimeError(
                "PH16-71 lighting interface not found. Confirm this "
                "laptop's keyboard controller really is 04F2:0117 by "
                "calling hid.enumerate() with no filter and checking "
                "usage_page values -- some revisions/report other "
                "PIDs (e.g. 011A has also been seen on related models)."
            )
        self._dev = hid.Device(path=path)

    def close(self):
        self._dev.close()

    # ---- low-level wire helpers ----------------------------------------

    def _send_command(self, op: int, p1: int = 0, p2: int = 0, p3: int = 0,
                       p4: int = 0, p5: int = 0, p6: int = 0):
        """Send one 8-byte feature-report command. hidapi requires a
        leading report-ID byte (0x00 -- this device declares no report
        IDs) that never actually reaches the wire; the real 8-byte
        command follows it, giving a 9-byte buffer."""
        report = bytes([0x00]) + _build_command(op, p1, p2, p3, p4, p5, p6)
        self._dev.send_feature_report(report)

    # ---- public API ------------------------------------------------------

    def set_static_color(self, r: int, g: int, b: int, brightness: int = 255):
        """Solid color across the whole keyboard. This is a 4-command
        sequence (BEGIN, select zone mode, set color, apply) -- a
        single feature report is not sufficient; that's how
        PredatorSense itself drives this mode."""
        self._send_command(OP_BEGIN)
        self._send_command(OP_MODE_ZONE)
        self._send_command(OP_SET_COLOR, 0, 0, r, g, b, 0)
        self._send_command(OP_APPLY, SUB_APPLY, EFF_STATIC, MODE_TAG,
                            brightness, SCOPE_ZONE, FLAG_DEFAULT)

    def send_frame(self, colors):
        """Push a full per-key frame. `colors` must be a sequence of
        exactly NUM_CELLS (r, g, b) tuples, one per cell index."""
        colors = list(colors)
        if len(colors) != NUM_CELLS:
            raise ValueError(f"expected {NUM_CELLS} colors, got {len(colors)}")

        buf = bytearray(NUM_CELLS * BYTES_PER_CELL)
        for i, (r, g, b) in enumerate(colors):
            offset = i * BYTES_PER_CELL
            buf[offset:offset + 4] = bytes([0x00, r, g, b])

        self._send_command(OP_BEGIN)
        self._send_command(OP_MODE_PERKEY, 0, 0, SCOPE_PERKEY, 0, 0, 0)

        # Eight raw 64-byte interrupt-OUT packets. Each still needs the
        # same leading 0x00 hidapi report-ID byte as the feature reports
        # above -- it is stripped before transmission, so the packet
        # that actually reaches the wire is exactly 64 bytes of cell
        # data, matching the reference captures.
        for chunk_index in range(FRAME_PACKET_COUNT):
            start = chunk_index * FRAME_PACKET_SIZE
            chunk = bytes(buf[start:start + FRAME_PACKET_SIZE])
            self._dev.write(bytes([0x00]) + chunk)

        self._send_command(OP_APPLY, SUB_APPLY, EFF_PERKEY, MODE_TAG,
                            DEFAULT_BRIGHTNESS, SCOPE_PERKEY, FLAG_DEFAULT)
