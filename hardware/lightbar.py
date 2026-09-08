"""
Rear lightbar control via Windows ACPI-WMI (AcerGamingFunction) on the
Predator PH16-71.

Separate transport from hardware/device.py's USB HID -- the per-key
keyboard and the rear lightbar are two completely different hardware
paths on this machine. The keyboard is driven directly over HID; the
lightbar has no HID (or documented) interface at all and is only
reachable through Acer's ACPI-WMI provider -- the same one PredatorSense
itself uses internally via a bundled, unpublished fork of OpenRGB.

Every byte value below was captured directly from that real software
(by instrumenting its actual WMI calls with Frida), not guessed. Acer
does not document this protocol anywhere, and every publicly available
reference for sibling Acer/Nitro models uses a different -- and, for
this exact chassis, incorrect -- byte layout. See HANDOFF.md, "SOLVED:
the rear lightbar", for the full reverse-engineering history.

Requires an elevated (Administrator) process -- every Set* method on
this WMI class rejects calls from a non-elevated caller.
"""

from __future__ import annotations

import threading
import time

import pythoncom
import win32com.client

_WMI_NAMESPACE = r"winmgmts:\\.\root\wmi"
_WMI_CLASS = "AcerGamingFunction"

# Fixed priming/"arm" trigger Acer's own software sends immediately
# before every color commit -- identical every single time regardless
# of color, zone, or brightness. Do not try to vary this; guessing at a
# "meaningful" content for it is exactly what didn't work for months.
_LED_PAYLOAD = [0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x15, 0x00, 0x00]

# Zone number (as shown to callers -- matches the physical left-to-right
# layout viewed from the front of the laptop with the lid up) -> the
# mask byte SetGamingRgbKb actually expects.
_ZONE_MASKS = {1: 1, 2: 2, 3: 4}
NUM_ZONES = 3

# How many times to repeat the full commit sequence, and the delay
# between repeats -- matches Acer's own real software's cadence
# exactly (captured live), presumably for reliability against this
# chassis's documented real-world lighting flakiness.
_COMMIT_ROUNDS = 3
_COMMIT_ROUND_DELAY = 0.065  # seconds


def find_lightbar_instance():
    """Returns the live AcerGamingFunction WMI instance, or None if this
    machine doesn't expose it (wrong laptop model, or the WMI class
    genuinely isn't present). Does NOT require elevation -- only the
    later Set* calls do."""
    try:
        wmi = win32com.client.GetObject(_WMI_NAMESPACE)
        instances = list(wmi.InstancesOf(_WMI_CLASS))
        return instances[0] if instances else None
    except Exception:
        return None


class Lightbar:
    """Owns the last-known color for each zone (every commit re-asserts
    all three zones at once, matching the real protocol, so this cache
    is what lets changing one zone leave the other two untouched).

    Does NOT cache a single WMI/COM instance across calls -- COM objects
    are thread-affine, but this is meant to be called from a daemon
    whose HTTP endpoints run in a worker threadpool (any given call can
    land on a different OS thread each time). Each thread that actually
    talks to WMI gets its own `pythoncom.CoInitialize()` call and its
    own freshly-resolved instance, cached per-thread via `threading.
    local` -- resolving the instance is cheap (a local WMI provider
    lookup), so there's no real cost to doing this instead of sharing
    one instance unsafely across threads."""

    def __init__(self):
        # Startup-time sanity check only (runs once, on whichever thread
        # calls this) -- confirms the class exists on this machine at
        # all before the daemon reports itself as lightbar-capable.
        # Actual per-call instances are resolved fresh per-thread below.
        if find_lightbar_instance() is None:
            raise RuntimeError(
                "AcerGamingFunction WMI class not found. This laptop may "
                "not be a PH16-71 with a rear lightbar, or the WMI "
                "provider isn't available in this process."
            )
        self._colors = {zone: (0, 0, 0) for zone in _ZONE_MASKS}
        self._local = threading.local()

    # ---- low-level wire helpers ----------------------------------------

    def _instance(self):
        if not hasattr(self._local, "instance"):
            pythoncom.CoInitialize()
            instance = find_lightbar_instance()
            if instance is None:
                raise RuntimeError("AcerGamingFunction WMI class not found on this thread.")
            self._local.instance = instance
        return self._local.instance

    def _call_array(self, method: str, arr):
        instance = self._instance()
        p = instance.Methods_(method).InParameters.SpawnInstance_()
        p.gmInput = list(arr)
        return instance.ExecMethod_(method, p).Properties_("gmOutput").Value

    def _call_u64(self, method: str, value: int):
        instance = self._instance()
        p = instance.Methods_(method).InParameters.SpawnInstance_()
        p.gmInput = value
        return instance.ExecMethod_(method, p).Properties_("gmOutput").Value

    def _send_led(self):
        return self._call_array("SetGamingLED", _LED_PAYLOAD)

    def _send_kb_commit(self, brightness: int = 100):
        return self._call_array(
            "SetGamingKBBacklight",
            [0, 0, brightness, 0, 0, 0, 0, 0, 3, 2, 0, 0, 0, 0, 0, 0],
        )

    def _send_rgbkb(self, mask: int, r: int, g: int, b: int):
        value = (r << 8) | (g << 16) | (b << 24) | (0x08 << 32) | (mask << 40)
        return self._call_u64("SetGamingRgbKb", value)

    def _commit(self, brightness: int = 100):
        for _ in range(_COMMIT_ROUNDS):
            self._send_led()
            self._send_kb_commit(brightness)
            for zone, mask in _ZONE_MASKS.items():
                r, g, b = self._colors[zone]
                self._send_rgbkb(mask, r, g, b)
            time.sleep(_COMMIT_ROUND_DELAY)

    # ---- public API ------------------------------------------------------

    def set_zone(self, zone: int, r: int, g: int, b: int):
        if zone not in _ZONE_MASKS:
            raise ValueError(f"zone must be one of {sorted(_ZONE_MASKS)}, got {zone}")
        self._colors[zone] = (r, g, b)
        self._commit()

    def set_all(self, r: int, g: int, b: int):
        for zone in _ZONE_MASKS:
            self._colors[zone] = (r, g, b)
        self._commit()

    def off(self):
        self.set_all(0, 0, 0)
