"""Global keyboard hook -> per-cell "last pressed" timestamps, so
effects can react to real typing. This is the ONLY file that touches
the OS-level keyboard hook; it deliberately keeps nothing more than
{cell_index: last_press_time} in memory -- no key names, no content,
nothing persisted to disk. A global hook is mechanically the same
thing a keylogger uses, so this file should never grow beyond that
narrow purpose.

Translation problem: the `keyboard` library reports its own key
names, which don't match the names typed into keymap.json during
discovery. The tables below were built empirically -- pressing every
key on this specific PH16-71 and comparing `keyboard`'s reported
(name, scan_code) pairs against keymap.json -- not guessed.
"""

import json
import threading
import time

import keyboard

# Numpad cluster: `keyboard` reports the same bare digit/symbol name
# for a numpad key as for its main-row counterpart (e.g. name='7' for
# both '7' and numpad-7), but the numpad cluster always uses this
# distinct scan-code range regardless of NumLock state -- this matches
# the standard PC Set-1 scan code table exactly, and was confirmed
# against a real keypress of each one.
_NUMPAD_SCAN_CODES = {
    71: "num_7", 72: "num_8", 73: "num_9", 74: "num_minus",
    75: "num_4", 76: "num_5", 77: "num_6", 78: "num_plus",
    79: "num_1", 80: "num_2", 81: "num_3", 82: "num_0",
    83: "num_decimal",
}

# Everything else: `keyboard`'s .name string (lowercased) -> the name
# typed into keymap.json. Letters and digits already match keymap.json
# directly and don't need an entry here.
#
# IMPORTANT: only names that are unambiguous on their own belong here.
# Several of these (insert/delete, the arrow names, the punctuation
# words) happen to share a raw scan_code with a numpad key -- e.g.
# dedicated Insert and numpad-0 are both scan_code 82 -- but the name
# string itself is never ambiguous, so this table must be checked
# BEFORE the numpad scan-code fallback below, not after. Getting that
# order backwards was the original bug: it made every one of these
# resolve to the numpad cell instead.
_NAME_TRANSLATION = {
    "print screen": "prtsc",
    "insert": "ins",
    "delete": "del",
    "previous track": "rewind",
    "play/pause media": "play_pause",
    "next track": "fast_forward",
    "num lock": "numlk",
    "caps lock": "caps_lock",
    "shift": "left_shift",
    "right shift": "right_shift",
    "ctrl": "left_ctrl",
    "right ctrl": "right_ctrl",
    "alt": "left_alt",
    "right alt": "alt_gr",
    "left windows": "windows",
    "right windows": "windows",
    "menu": "context_menu",
    "*": "num_multiply",
    "up": "up_arrow",
    "down": "down_arrow",
    "left": "left_arrow",
    "right": "right_arrow",
    "=": "equals",
    "[": "left_bracket",
    "]": "right_bracket",
    "\\": "backslash",
    ";": "semicolon",
    "'": "quote",
    ",": "comma",
    ".": "period",
    "`": "backtick",
    # deliberately NOT here: "-" (see _names_for_event) -- unlike the
    # rest of this table, main '-' and numpad '-' report the IDENTICAL
    # name string, so name alone can't disambiguate it; scan_code must
    # be checked first for this one case.
}

# `keyboard` reports the exact same (name, scan_code) pair for two
# distinct physical keys in these cases -- genuinely indistinguishable
# with this library. Light both cells rather than leaving either dead.
_SLASH_SCAN_CODE = 53
_SLASH_NAMES = ("slash", "num_divide")
_ENTER_NAMES = ("enter", "num_enter")

# Never observable via a generic OS keyboard hook / not worth the risk
# to test: Fn is handled entirely inside the keyboard controller and
# never reaches the OS on virtually all laptops; predator_key and
# power_button are Acer vendor hotkeys (and pressing power deliberately
# wasn't tested). These cells simply won't react -- that's fine.
#
# Known limitation: if NumLock is ever turned OFF, the numpad's 8/2/4/6
# keys may start reporting the same 'up'/'down'/'left'/'right' names as
# the dedicated arrow cluster, which would misattribute them to the
# arrow cells instead of num_8/num_2/num_4/num_6. Not fixed here --
# would require also tracking NumLock's live state, and everyday use
# keeps it on.


class InputListener:
    """Owns the global keyboard hook and a thread-safe
    {cell_index: [press_monotonic_time, ...]} map. Each press is kept
    independently (not just the most recent) so effects can spawn one
    animation per press rather than one per key -- mashing the same
    key repeatedly fires overlapping animations instead of the newest
    press resetting/replacing the previous one."""

    def __init__(self, keymap_path: str):
        self._index_by_name = self._load_index_by_name(keymap_path)
        self._last_press = {}
        self._lock = threading.Lock()
        # Diagnostic counters only -- not used by any effect, just so
        # /status can report whether the OS hook is actually still
        # firing (vs. silently starved/unhooked), to tell that apart
        # from a bug in what happens after a press is registered.
        self._raw_event_count = 0
        self._matched_event_count = 0
        self._last_raw_event_time = None

    @staticmethod
    def _load_index_by_name(keymap_path: str) -> dict:
        try:
            with open(keymap_path) as f:
                keymap = json.load(f)
        except (FileNotFoundError, json.JSONDecodeError):
            keymap = {}
        return {name: int(idx) for idx, name in keymap.items()}

    def _names_for_event(self, name: str, scan_code: int):
        name = (name or "").lower()

        # Truly identical (name, scan_code) for two distinct physical
        # keys -- can't disambiguate, so light both.
        if name == "enter":
            return _ENTER_NAMES
        if scan_code == _SLASH_SCAN_CODE and name == "/":
            return _SLASH_NAMES

        # Named keys are unambiguous by name alone, even when their
        # scan_code coincides with a numpad cell's -- check this
        # before the numpad fallback below, not after.
        if name in _NAME_TRANSLATION:
            return (_NAME_TRANSLATION[name],)

        # Bare digits/symbols ('7', '.', '+', etc.) are ambiguous
        # between main row and numpad by name alone; the numpad
        # cluster's scan-code range disambiguates them.
        if scan_code in _NUMPAD_SCAN_CODES:
            return (_NUMPAD_SCAN_CODES[scan_code],)

        # Main-row '-' only reaches here once the numpad case above
        # has already ruled out numpad-minus (scan_code 74).
        if name == "-":
            return ("minus",)

        return (name,)

    def _on_event(self, event):
        now = time.monotonic()
        with self._lock:
            self._raw_event_count += 1
            self._last_raw_event_time = now
        if event.event_type != "down":
            return
        for key_name in self._names_for_event(event.name, event.scan_code):
            idx = self._index_by_name.get(key_name)
            if idx is None:
                continue
            with self._lock:
                self._matched_event_count += 1
                self._last_press.setdefault(idx, []).append(now)

    def start(self):
        keyboard.hook(self._on_event)

    def register_named_press(self, name: str) -> bool:
        """Registers a press by keymap.json name directly, bypassing the
        OS-level hook entirely. Used for keystrokes forwarded by the
        GUI's own JS keydown listener -- when the GUI window's page
        content has keyboard focus, WebView2's embedded Chromium
        control appears to consume real keystrokes before Windows'
        global WH_KEYBOARD_LL hook chain ever reaches this process's
        hook (confirmed: works fine when the app is open but unfocused,
        breaks the instant its page content is focused, regardless of
        which top-level window is the OS foreground window -- a plain
        SetForegroundWindow() call didn't reproduce it, only actually
        focusing the page's DOM did). Since a focused page is
        guaranteed to receive normal browser keydown events for
        anything physically typed, the GUI forwards those here as a
        second, focus-independent input path. Returns whether the name
        matched a known cell (for the caller's own diagnostics)."""
        idx = self._index_by_name.get(name)
        if idx is None:
            return False
        now = time.monotonic()
        with self._lock:
            self._matched_event_count += 1
            self._last_press.setdefault(idx, []).append(now)
        return True

    def diagnostics(self) -> dict:
        """Raw hook-callback counters, for telling apart 'the OS hook
        isn't firing at all' from 'it's firing but something downstream
        is wrong' when debugging responsiveness issues."""
        with self._lock:
            last = self._last_raw_event_time
            return {
                "raw_event_count": self._raw_event_count,
                "matched_event_count": self._matched_event_count,
                "seconds_since_last_raw_event": (
                    time.monotonic() - last if last is not None else None
                ),
            }

    def snapshot(self, max_age: float) -> dict:
        """Returns {cell_index: [seconds_since_press, ...]} -- one
        entry per press within the last `max_age` seconds, oldest
        presses beyond that pruned."""
        now = time.monotonic()
        result = {}
        with self._lock:
            empty = []
            for idx, presses in self._last_press.items():
                kept = [p for p in presses if now - p <= max_age]
                if kept:
                    self._last_press[idx] = kept
                    result[idx] = [now - p for p in kept]
                else:
                    empty.append(idx)
            for idx in empty:
                del self._last_press[idx]
        return result
