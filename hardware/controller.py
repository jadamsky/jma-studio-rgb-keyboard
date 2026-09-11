"""PS5 DualSense controller input via USB HID, for the controller-
reactive keyboard effect.

Wired (USB) only for now -- Bluetooth uses a different report ID and
report length (0x31, ~78 bytes, vs. USB's 0x01/64 bytes) and is
deliberately deferred until wired is working end to end.

Byte offsets below were confirmed empirically against a real DualSense
Edge controller (not just taken from public documentation, though they
match it): captured an idle baseline, then had the user hold specific
inputs (Cross, L1 + D-pad Up, left stick full up) and diffed the raw
reports. Left stick Y decreases toward 0 when pushed up, increases
toward 255 when pushed down (screen-coordinate convention, confirmed
live -- not assumed).
"""

from __future__ import annotations

import os
import sys
import threading
import time

# Same fix hardware/device.py needs: ctypes/Windows no longer implicitly
# searches the venv's Scripts/ directory for bare-name DLL loads, so
# hidapi.dll's load fails unless that directory is explicitly registered
# before `import hid`. Needed here independently of device.py's own copy
# of this fix -- whichever hardware/*.py module happens to `import hid`
# first is the one that actually needs to have run this.
if sys.platform == "win32":
    _dll_dir = os.path.dirname(sys.executable)
    if os.path.isdir(_dll_dir):
        os.add_dll_directory(_dll_dir)

import hid

_VENDOR_ID = 0x054C  # Sony Interactive Entertainment
_PRODUCT_IDS = (0x0CE6, 0x0DF2)  # DualSense, DualSense Edge
_USAGE_PAGE = 1  # Generic Desktop
_USAGE = 5  # Game Pad

_STICK_CENTER = 128
_STICK_RANGE = 127  # distance from center to an extreme (0 or 255)

_READ_TIMEOUT_MS = 100
_POLL_ERROR_BACKOFF_S = 0.5
# The pad streams reports continuously at ~1000Hz even at rest, so any
# gap this long with zero reports means the *handle* has gone stale,
# not that the controller stopped talking. This happens reliably after
# the laptop sleeps/wakes -- Windows re-enumerates the USB device on
# resume and the already-open handle never recovers on its own, even
# though the device path string is usually unchanged. Detected here and
# fixed by reopening rather than requiring a daemon restart every time.
_RECONNECT_STALL_S = 2.0


def find_controller_path():
    """Returns the HID device path for the DualSense's game-controller
    interface (it exposes several HID interfaces -- audio, etc. --
    filtering by usage_page/usage picks the actual gamepad one), or
    None if no supported controller is connected over USB right now."""
    for d in hid.enumerate():
        if (
            d["vendor_id"] == _VENDOR_ID
            and d["product_id"] in _PRODUCT_IDS
            and d.get("usage_page") == _USAGE_PAGE
            and d.get("usage") == _USAGE
            and d.get("bus_type") == hid.BusType.USB
        ):
            return d["path"]
    return None


class Controller:
    """Runs a background thread continuously reading raw HID reports and
    parsing out the left stick position. `get_state()` is thread-safe
    and cheap -- meant to be called once per rendered frame."""

    def __init__(self):
        path = find_controller_path()
        if path is None:
            raise RuntimeError(
                "No DualSense controller found over USB. Bluetooth isn't "
                "supported yet -- connect via USB cable."
            )
        self._dev = hid.Device(path=path)
        self._lock = threading.Lock()
        # -1.0..1.0, 0 = centered. *_y: negative = up, positive = down
        # (confirmed live for the left stick; the right stick uses the
        # same report layout so this is assumed to hold for it too).
        self._state = {
            "left_x": 0.0, "left_y": 0.0, "right_x": 0.0, "right_y": 0.0,
            "left_trigger": 0.0, "right_trigger": 0.0,  # 0.0..1.0
            "l1": False, "r1": False,
            "square": False, "cross": False, "circle": False, "triangle": False,
            "dpad_up": False, "dpad_right": False, "dpad_down": False, "dpad_left": False,
            # DualSense Edge only -- rear paddles and Function buttons.
            "left_fn": False, "right_fn": False, "left_paddle": False, "right_paddle": False,
        }
        self._connected = True
        self._running = True
        self._thread = threading.Thread(target=self._poll_loop, daemon=True)
        self._thread.start()

    def _poll_loop(self):
        last_data_at = time.monotonic()
        last_reconnect_attempt = 0.0
        while self._running:
            try:
                data = self._dev.read(64, timeout=_READ_TIMEOUT_MS)
            except Exception:
                data = None

            if not data or len(data) < 11:
                now = time.monotonic()
                if (
                    now - last_data_at > _RECONNECT_STALL_S
                    and now - last_reconnect_attempt > _RECONNECT_STALL_S
                ):
                    last_reconnect_attempt = now
                    self._try_reconnect()
                else:
                    time.sleep(_POLL_ERROR_BACKOFF_S if data is None else 0)
                continue
            last_data_at = time.monotonic()
            with self._lock:
                self._connected = True
            lx = (data[1] - _STICK_CENTER) / _STICK_RANGE
            ly = (data[2] - _STICK_CENTER) / _STICK_RANGE
            rx = (data[3] - _STICK_CENTER) / _STICK_RANGE
            ry = (data[4] - _STICK_CENTER) / _STICK_RANGE
            l2 = data[5] / 255.0
            r2 = data[6] / 255.0
            # bits 4-7 = Square/Cross/Circle/Triangle (Cross confirmed
            # live; the other three follow the same documented order --
            # see module docstring). Low nibble = D-pad hat switch:
            # 0=up, 1=up-right, 2=right, 3=down-right, 4=down,
            # 5=down-left, 6=left, 7=up-left, 8=neutral (0=up confirmed
            # live). Only the 4 cardinal directions are exposed here.
            buttons1 = data[8]
            hat = buttons1 & 0x0F
            buttons2 = data[9]  # bit0=L1, bit1=R1 (confirmed live -- see module docstring)
            # DualSense Edge only, all confirmed live: bit4=left Fn,
            # bit5=right Fn, bit6=left paddle (L4), bit7=right paddle (R4).
            buttons3 = data[10]
            with self._lock:
                self._state["left_x"] = max(-1.0, min(1.0, lx))
                self._state["left_y"] = max(-1.0, min(1.0, ly))
                self._state["right_x"] = max(-1.0, min(1.0, rx))
                self._state["right_y"] = max(-1.0, min(1.0, ry))
                self._state["square"] = bool(buttons1 & 0x10)
                self._state["cross"] = bool(buttons1 & 0x20)
                self._state["circle"] = bool(buttons1 & 0x40)
                self._state["triangle"] = bool(buttons1 & 0x80)
                self._state["dpad_up"] = hat == 0
                self._state["dpad_right"] = hat == 2
                self._state["dpad_down"] = hat == 4
                self._state["dpad_left"] = hat == 6
                self._state["left_trigger"] = l2
                self._state["right_trigger"] = r2
                self._state["l1"] = bool(buttons2 & 0x01)
                self._state["r1"] = bool(buttons2 & 0x02)
                self._state["left_fn"] = bool(buttons3 & 0x10)
                self._state["right_fn"] = bool(buttons3 & 0x20)
                self._state["left_paddle"] = bool(buttons3 & 0x40)
                self._state["right_paddle"] = bool(buttons3 & 0x80)

    def _try_reconnect(self):
        """Re-finds the controller and opens a fresh handle to replace
        the stale one. If the pad isn't currently enumerable at all
        (unplugged, or still mid-resume), this just marks us
        disconnected for now -- the next stall check will try again."""
        path = find_controller_path()
        if path is None:
            with self._lock:
                self._connected = False
            return
        try:
            new_dev = hid.Device(path=path)
        except Exception:
            with self._lock:
                self._connected = False
            return
        old_dev = self._dev
        self._dev = new_dev
        with self._lock:
            self._connected = True
        try:
            old_dev.close()
        except Exception:
            pass

    def get_state(self) -> dict:
        with self._lock:
            return dict(self._state)

    def is_connected(self) -> bool:
        """True once real reports have been (or are again being)
        received. Unlike "was Controller() constructed successfully",
        this reflects live read health -- see _RECONNECT_STALL_S."""
        with self._lock:
            return self._connected

    def close(self):
        self._running = False
        self._thread.join(timeout=1.0)
        self._dev.close()
