"""Hand-authored physical (row, col) grid position for every key name
in keymap.json -- built from an actual photo of this specific PH16-71,
not guessed. Row increases downward (0 = function row). Column
increases left-to-right using standard fractional keyboard-unit
stagger (e.g. Tab is 1.5u wide, so Q starts at column 1.5, matching
real physical key stagger). Precision here is "close enough for a
lighting effect to look right", not measured to the millimeter.

Shared by any effect that wants spatial awareness (e.g. a chasing
bolt) -- pure data plus one loader function, no daemon/hardware
imports, consistent with the rest of effects/*.py.
"""

import json

KEY_POSITIONS = {
    # Row 0 -- function row. Gaps between the F-key groups (esc-f1,
    # f4-f5, f8-f9, f12-prtsc) are 0.225u -- solved precisely so Del's
    # right edge lands exactly on Backspace's right edge one row down.
    # Every key here is also narrower than the standard 1u (see
    # KEY_WIDTH in gui/app.js, 0.85u).
    "esc": (0, 0),
    "f1": (0, 1.075), "f2": (0, 1.925), "f3": (0, 2.775), "f4": (0, 3.625),
    "f5": (0, 4.7), "f6": (0, 5.55), "f7": (0, 6.4), "f8": (0, 7.25),
    "f9": (0, 8.325), "f10": (0, 9.175), "f11": (0, 10.025), "f12": (0, 10.875),
    "prtsc": (0, 11.95), "ins": (0, 12.8), "del": (0, 13.65),
    # Media + power (4 keys) share the numpad's own 4-column grid
    # (14.8/15.8/16.8/17.8), standard 1u width, not row0's narrower one.
    "rewind": (0, 14.8), "play_pause": (0, 15.8), "fast_forward": (0, 16.8),
    "power_button": (0, 17.8),

    # Row 1 -- number row
    "backtick": (1, 0),
    "1": (1, 1), "2": (1, 2), "3": (1, 3), "4": (1, 4), "5": (1, 5),
    "6": (1, 6), "7": (1, 7), "8": (1, 8), "9": (1, 9), "0": (1, 10),
    "minus": (1, 11), "equals": (1, 12), "backspace": (1, 13),
    "predator_key": (1, 14.8), "numlk": (1, 15.8),
    "num_divide": (1, 16.8), "num_multiply": (1, 17.8),

    # Row 2 -- QWERTY row
    "tab": (2, 0),
    "q": (2, 1.5), "w": (2, 2.5), "e": (2, 3.5), "r": (2, 4.5),
    "t": (2, 5.5), "y": (2, 6.5), "u": (2, 7.5), "i": (2, 8.5),
    "o": (2, 9.5), "p": (2, 10.5),
    "left_bracket": (2, 11.5), "right_bracket": (2, 12.5),
    "backslash": (2, 13.5),
    "num_7": (2, 14.8), "num_8": (2, 15.8), "num_9": (2, 16.8), "num_minus": (2, 17.8),

    # Row 3 -- ASDF row
    "caps_lock": (3, 0),
    "a": (3, 1.75), "s": (3, 2.75), "d": (3, 3.75), "f": (3, 4.75),
    "g": (3, 5.75), "h": (3, 6.75), "j": (3, 7.75), "k": (3, 8.75),
    "l": (3, 9.75),
    "semicolon": (3, 10.75), "quote": (3, 11.75), "enter": (3, 12.75),
    "num_4": (3, 14.8), "num_5": (3, 15.8), "num_6": (3, 16.8), "num_plus": (3, 17.8),

    # Row 4 -- ZXCV row. Right shift is narrower than left shift on
    # this board -- was overcorrected wide before.
    "left_shift": (4, 0),
    "z": (4, 2.25), "x": (4, 3.25), "c": (4, 4.25), "v": (4, 5.25),
    "b": (4, 6.25), "n": (4, 7.25), "m": (4, 8.25),
    "comma": (4, 9.25), "period": (4, 10.25), "slash": (4, 11.25),
    "right_shift": (4, 12.25),
    "up_arrow": (4, 13.55),
    "num_1": (4, 14.8), "num_2": (4, 15.8), "num_3": (4, 16.8), "num_enter": (4, 17.8),

    # Row 5 -- bottom row
    "left_ctrl": (5, 0), "fn": (5, 1.25), "windows": (5, 2.25),
    "left_alt": (5, 3.25), "space": (5, 4.25),
    "alt_gr": (5, 9.25), "context_menu": (5, 10.25), "right_ctrl": (5, 11.25),
    "left_arrow": (5, 12.55), "down_arrow": (5, 13.55), "right_arrow": (5, 14.8),
    "num_0": (5, 15.8), "num_decimal": (5, 16.8),
}


def cell_positions(keymap_path: str) -> dict:
    """Returns {cell_index: (row, col)} by combining keymap.json's
    {index: name} with KEY_POSITIONS above. A name with no known
    position (shouldn't happen, but harmless if a future discovery
    adds one) is silently skipped rather than erroring."""
    try:
        with open(keymap_path) as f:
            keymap = json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        keymap = {}
    positions = {}
    for idx, name in keymap.items():
        pos = KEY_POSITIONS.get(name)
        if pos is not None:
            positions[int(idx)] = pos
    return positions
