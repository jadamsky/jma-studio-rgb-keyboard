"""Static left-to-right color split across the physical keyboard grid
(see layout.py). Originally built to replicate a PredatorSense
zone-based static color, later generalized to support more than 2
zones.

params:
    colors       list of 2-5 RGB colors, one per zone, left-to-right.
                 Takes priority over left_color/right_color below.
    boundaries   list of len(colors)-1 column values marking each
                 cutover (cells with column >= boundaries[i] belong to
                 zone i+1 or later). Takes priority over `boundary`.
    left_color, right_color, boundary
                 legacy 2-zone params, used only when `colors` isn't
                 given -- kept so existing presets built before
                 multi-zone support keep rendering identically.
    hard         True (default) = a sharp cutover at each boundary,
                 matching PredatorSense's own hardware-zone behavior.
                 False = smooth per-key blend between consecutive zone
                 colors instead (no hard edges).
    left_overrides   list of key names (from keymap.json) forced to
                 the first zone's color regardless of the boundary --
                 for patching individual keys whose real hardware zone
                 doesn't follow a clean vertical column split. Only
                 applied in legacy 2-zone mode (colors not given) --
                 these were hardware-quirk patches for PredatorSense's
                 real 2-zone assignment, not meaningful for 3+ zones.
    right_overrides  same, forced to the last zone's color. Legacy
                 2-zone mode only.
    custom_colors    dict of {key name: [r,g,b]} for individual keys
                 that need their own distinct shade rather than any
                 zone color exactly (e.g. spacebar a touch lighter
                 than the rest of the blue zone). Legacy 2-zone mode
                 only. Applied last, so these take priority over
                 everything else.
    brightness   0.0-1.0 multiplier applied to every cell's color as
                 the final step, after all of the above. Default 0.65
                 -- full brightness (1.0) was tried and felt too
                 intense against this keyboard's diffusers.
"""

import os

from effects.layout import cell_positions, name_to_index

NAME = "gradient"

DEFAULT_LEFT = (20, 90, 230)
DEFAULT_RIGHT = (200, 20, 160)
DEFAULT_BRIGHTNESS = 0.65

_KEYMAP_PATH = os.path.join(os.path.dirname(os.path.dirname(__file__)), "keymap.json")

# Loaded once at import time, same as the other layout-aware effects
# -- restart the daemon after editing keymap.json.
_POSITIONS = cell_positions(_KEYMAP_PATH)

if _POSITIONS:
    _MIN_COL = min(col for _, col in _POSITIONS.values())
    _MAX_COL = max(col for _, col in _POSITIONS.values())
else:
    _MIN_COL, _MAX_COL = 0.0, 1.0


_INDEX_BY_NAME = name_to_index(_KEYMAP_PATH)


def _zone_index(col, boundaries, num_zones):
    zone = 0
    for b in boundaries:
        if col >= b:
            zone += 1
        else:
            break
    return min(zone, num_zones - 1)


def _smooth_color(col, zone_colors, boundaries):
    stops = [(_MIN_COL, zone_colors[0])]
    for i, b in enumerate(boundaries):
        stops.append((b, zone_colors[i + 1]))
    stops.append((_MAX_COL, zone_colors[-1]))

    for i in range(len(stops) - 1):
        c0_pos, c0 = stops[i]
        c1_pos, c1 = stops[i + 1]
        if col <= c1_pos or i == len(stops) - 2:
            span = (c1_pos - c0_pos) or 1.0
            frac = max(0.0, min(1.0, (col - c0_pos) / span))
            return tuple(int(c0[ch] + (c1[ch] - c0[ch]) * frac) for ch in range(3))
    return zone_colors[-1]


def render(t, num_cells, params):
    colors_param = params.get("colors")
    if colors_param:
        zone_colors = [tuple(c) for c in colors_param]
        boundaries = sorted(params.get("boundaries", []))
        legacy_mode = False
    else:
        zone_colors = [
            tuple(params.get("left_color", DEFAULT_LEFT)),
            tuple(params.get("right_color", DEFAULT_RIGHT)),
        ]
        boundaries = [params.get("boundary", (_MIN_COL + _MAX_COL) / 2)]
        legacy_mode = True

    hard = params.get("hard", True)
    brightness = params.get("brightness", DEFAULT_BRIGHTNESS)

    colors = [zone_colors[0]] * num_cells
    for idx in range(num_cells):
        pos = _POSITIONS.get(idx)
        if pos is None:
            continue
        col = pos[1]
        if hard:
            colors[idx] = zone_colors[_zone_index(col, boundaries, len(zone_colors))]
        else:
            colors[idx] = _smooth_color(col, zone_colors, boundaries)

    if legacy_mode:
        for name in params.get("left_overrides", []):
            idx = _INDEX_BY_NAME.get(name)
            if idx is not None:
                colors[idx] = zone_colors[0]
        for name in params.get("right_overrides", []):
            idx = _INDEX_BY_NAME.get(name)
            if idx is not None:
                colors[idx] = zone_colors[-1]
        for name, color in params.get("custom_colors", {}).items():
            idx = _INDEX_BY_NAME.get(name)
            if idx is not None:
                colors[idx] = tuple(color)

    if brightness != 1.0:
        colors = [tuple(int(c * brightness) for c in color) for color in colors]

    return colors
