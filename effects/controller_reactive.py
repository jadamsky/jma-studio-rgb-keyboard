"""Visualizes the DualSense's two sticks as a "ripple outward from a
home key" effect (S for the left stick, L for the right -- nearby keys
light up progressively as that stick tilts in each direction), plus a
fixed set of individual buttons/triggers/D-pad/paddles/Fn buttons that
each light their own separate group of keys.

Each stick has 3 independently-colorable magnitude bands (no per-
direction color -- up/down/left/right all share the same band colors):
    idle   0% up to the deadzone threshold -- the anchor key (S/L)
           always shows this color.
    tier1  from the deadzone threshold up to 50% deflection -- lights
           the near ring of keys around the anchor.
    tier2  50%-100% deflection -- lights the near ring AND the far ring,
           both recolored to tier2 (this band doesn't layer tier1's
           color underneath; the whole active set just uses whichever
           band's color matches the current magnitude).

params:
    background_enabled  bool -- when False, the background is forced to
                        black regardless of background_color (a way to
                        keep a background color picked without it
                        actually being applied).
    background_color    [r,g,b] -- base fill for the whole board when
                        background_enabled is True.
    left_stick          {"idle": [r,g,b], "tier1": [...], "tier2": [...]}
    right_stick         same shape as left_stick, independent colors.
    button_colors       {group name: [r,g,b]} -- one independent color
                        per button group (see BUTTON_GROUPS for the
                        full list of group names and which keys each
                        one lights). Falls back to DEFAULT_GROUP_COLOR
                        for any group missing from this dict.
    deadzone            0.0-1.0 -- fraction of full stick deflection
                        below which the stick counts as idle. Falls
                        back to DEFAULT_DEADZONE.
    controller_state    injected by the daemon's render loop each
                        frame -- see hardware/controller.py's
                        Controller.get_state() for the exact shape. Not
                        something a caller sets directly.
"""

import os

from effects.layout import name_to_index

NAME = "controller_reactive"

DEFAULT_BACKGROUND = (10, 10, 10)
DEFAULT_GROUP_COLOR = (0, 100, 0)  # "dark green"
DEFAULT_DEADZONE = 0.15

# Fraction of full deflection at which a stick's lit keys switch from
# tier1's color to tier2's (see module docstring -- bands don't layer).
TIER2_THRESHOLD = 0.5
# L2/R2 are analog but deliberately treated as all-or-nothing, per the
# user's own request -- only a full (100%) pull lights anything, no
# progressive tiers like the sticks have. A little tolerance for
# calibration noise, so "fully pulled" doesn't require hitting exactly
# 255 on the raw byte.
TRIGGER_FULL_THRESHOLD = 0.95

_KEYMAP_PATH = os.path.join(os.path.dirname(os.path.dirname(__file__)), "keymap.json")
_INDEX_BY_NAME = name_to_index(_KEYMAP_PATH)

_LEFT_CENTER_KEY = "s"
_LEFT_ZONES = {
    "up": {"tier1": ["w", "e"], "tier2": ["2", "3", "4"]},
    "down": {"tier1": ["z", "x"], "tier2": ["windows", "left_alt"]},
    "left": {"tier1": ["a"], "tier2": ["caps_lock"]},
    "right": {"tier1": ["d"], "tier2": ["f"]},
}

_RIGHT_CENTER_KEY = "l"
_RIGHT_ZONES = {
    "up": {"tier1": ["o", "p"], "tier2": ["9", "0", "minus"]},
    "down": {"tier1": ["comma", "period"], "tier2": ["alt_gr", "context_menu"]},
    "left": {"tier1": ["k"], "tier2": ["j"]},
    "right": {"tier1": ["semicolon"], "tier2": ["quote"]},
}

# Every independently-colorable button group and the keys it lights.
# Group names double as the GUI's per-group color-picker ids and (for
# everything except l2/r2, handled specially below) the exact boolean
# field name in Controller.get_state().
BUTTON_GROUPS = {
    "l1": ["f1", "f2", "f3", "f4"],
    "r1": ["prtsc", "ins", "del"],
    "l2": ["f5", "f6", "f7", "f8"],
    "r2": ["f9", "f10", "f11", "f12"],
    "cross": ["num_2"],
    "square": ["num_4"],
    "circle": ["num_6"],
    "triangle": ["num_8"],
    "dpad_up": ["y"],
    "dpad_left": ["g"],
    "dpad_right": ["h"],
    "dpad_down": ["b"],
    "left_paddle": ["left_shift"],
    "left_fn": ["left_ctrl"],
    "right_paddle": ["right_shift"],
    "right_fn": ["right_ctrl"],
}

DEFAULT_STICK_COLORS = {
    "idle": DEFAULT_GROUP_COLOR,
    "tier1": DEFAULT_GROUP_COLOR,
    "tier2": DEFAULT_GROUP_COLOR,
}


def _button_active(name: str, state: dict) -> bool:
    if name == "l2":
        return state.get("left_trigger", 0.0) >= TRIGGER_FULL_THRESHOLD
    if name == "r2":
        return state.get("right_trigger", 0.0) >= TRIGGER_FULL_THRESHOLD
    return bool(state.get(name))


def _light_stick_zones(colors, stick_colors, deadzone, center_key, zones, x, y):
    idle_color = tuple(stick_colors.get("idle", DEFAULT_GROUP_COLOR))
    tier1_color = tuple(stick_colors.get("tier1", DEFAULT_GROUP_COLOR))
    tier2_color = tuple(stick_colors.get("tier2", DEFAULT_GROUP_COLOR))

    center_idx = _INDEX_BY_NAME.get(center_key)
    if center_idx is not None:
        colors[center_idx] = idle_color

    active = []
    if -y > deadzone:
        active.append(("up", -y))
    if y > deadzone:
        active.append(("down", y))
    if -x > deadzone:
        active.append(("left", -x))
    if x > deadzone:
        active.append(("right", x))

    for direction, magnitude in active:
        zone = zones[direction]
        in_tier2 = magnitude >= TIER2_THRESHOLD
        keys = list(zone["tier1"]) + (list(zone["tier2"]) if in_tier2 else [])
        color = tier2_color if in_tier2 else tier1_color
        for name in keys:
            idx = _INDEX_BY_NAME.get(name)
            if idx is not None:
                colors[idx] = color


def render(t, num_cells, params):
    bg = tuple(params.get("background_color", DEFAULT_BACKGROUND)) if params.get("background_enabled", True) else (0, 0, 0)
    left_stick = params.get("left_stick") or DEFAULT_STICK_COLORS
    right_stick = params.get("right_stick") or DEFAULT_STICK_COLORS
    button_colors = params.get("button_colors") or {}
    deadzone = params.get("deadzone", DEFAULT_DEADZONE)
    colors = [bg] * num_cells

    state = params.get("controller_state") or {}
    _light_stick_zones(
        colors, left_stick, deadzone, _LEFT_CENTER_KEY, _LEFT_ZONES,
        state.get("left_x", 0.0), state.get("left_y", 0.0),
    )
    _light_stick_zones(
        colors, right_stick, deadzone, _RIGHT_CENTER_KEY, _RIGHT_ZONES,
        state.get("right_x", 0.0), state.get("right_y", 0.0),
    )

    for name, keys in BUTTON_GROUPS.items():
        if _button_active(name, state):
            color = tuple(button_colors.get(name, DEFAULT_GROUP_COLOR))
            for key in keys:
                idx = _INDEX_BY_NAME.get(key)
                if idx is not None:
                    colors[idx] = color

    return colors
