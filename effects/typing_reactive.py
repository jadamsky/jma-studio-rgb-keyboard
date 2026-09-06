"""Typing-reactive lighting: the whole board sits at a base color.
Each pressed key flashes bright and fades back to base in place, AND
sends a chasing bolt outward across the physical grid (see layout.py)
that fades as it travels. Reads live keypress timing from
params['key_state'] (injected by the daemon's render loop each frame),
which maps cell_index -> [seconds_since_press, ...] -- a list, since a
key mashed repeatedly fires one independent animation per press rather
than the newest press resetting the previous one.

params:
    base_color      RGB for the resting state. Default 15% white.
                     Ignored if base_effect is set.
    base_effect     name of another effect module to use as the
                     resting background instead of a flat base_color
                     -- e.g. "gradient" to chase on top of the
                     PredatorSense-replica zone split. That module's
                     render(t, num_cells, base_params) is called each
                     frame to produce the per-cell background.
    base_params     params dict passed through to base_effect.
    bright_color    RGB for both the in-place flash and the bolts.
                    Default full white.
    decay_seconds   time for the in-place flash to fade. Default 0.6.
    bolts           enable/disable the chasing bolts. Default True.
    bolt_shape      "rays" (default) travels along a fixed set of
                    straight-line directions (bolt_directions), leaving
                    gaps between them -- a star shape. "radial" ignores
                    bolt_directions and expands as a true 360 degree
                    ring using plain distance from the origin.
    bolt_directions list of (row, col) unit steps, e.g. [(0,1)] for
                    "right only". Only used when bolt_shape="rays".
                    Default: all 8 (cardinal + diagonal).
    bolt_speed      grid units/second the bolt head travels. Default 12.
    bolt_tail       length in grid units of the fading tail behind the
                    bolt's head. Default 3.
    bolt_tolerance  how far off the exact ray line (in grid units) a
                    cell can be and still count as "on" the bolt. Only
                    used when bolt_shape="rays". Default 0.75 --
                    roughly "within the same key lane".
    bolt_style      "solid" (bright_color, default) or "rainbow" (a
                    chaotic per-cell hue flicker, same noise formula as
                    puke.py, visible only where a bolt currently is).
    bolt_flicker_speed  hue flickers/second for "rainbow" style. Default 6.

The in-place flash always uses bright_color regardless of bolt_style
-- only the traveling bolts change appearance.
"""

import colorsys
import importlib
import math
import os

from effects.layout import cell_positions

NAME = "typing_reactive"

DEFAULT_BASE = (38, 38, 38)  # 255 * 0.15 ~= 38
DEFAULT_BRIGHT = (255, 255, 255)
DEFAULT_DECAY = 0.6

DEFAULT_BOLT_DIRECTIONS = [
    (-1, 0), (1, 0), (0, -1), (0, 1),
    (-1, -1), (-1, 1), (1, -1), (1, 1),
]
DEFAULT_BOLT_SPEED = 12.0
DEFAULT_BOLT_TAIL = 3.0
DEFAULT_BOLT_TOLERANCE = 0.75
DEFAULT_BOLT_SHAPE = "rays"
DEFAULT_BOLT_STYLE = "solid"
DEFAULT_BOLT_FLICKER_SPEED = 6.0

_KEYMAP_PATH = os.path.join(os.path.dirname(os.path.dirname(__file__)), "keymap.json")

# Loaded once at import time, same as gaming_zone.py -- restart the
# daemon after editing keymap.json.
_POSITIONS = cell_positions(_KEYMAP_PATH)


def _unit(vec):
    length = math.hypot(vec[0], vec[1])
    return (vec[0] / length, vec[1] / length)


def _pseudo_random(i, tick):
    """Same cheap deterministic hash as puke.py -- each (cell, tick)
    pair gets its own stable "random" hue, no external RNG needed."""
    x = (i * 2654435761 + tick * 40503) & 0xFFFFFFFF
    x ^= x >> 15
    x = (x * 2246822519) & 0xFFFFFFFF
    x ^= x >> 13
    return (x & 0xFFFF) / 65536.0


def _resolve_base_colors(t, num_cells, params):
    """Returns a per-cell list of base colors: either a flat
    base_color repeated, or another effect's own render() output when
    base_effect is given."""
    base_effect_name = params.get("base_effect")
    if not base_effect_name:
        base = tuple(params.get("base_color", DEFAULT_BASE))
        return [base] * num_cells
    try:
        base_module = importlib.import_module(f"effects.{base_effect_name}")
        return base_module.render(t, num_cells, params.get("base_params", {}))
    except Exception as e:
        print(f"[typing_reactive] base_effect {base_effect_name!r} failed: {e}")
        base = tuple(params.get("base_color", DEFAULT_BASE))
        return [base] * num_cells


def render(t, num_cells, params):
    base_colors = _resolve_base_colors(t, num_cells, params)
    bright = tuple(params.get("bright_color", DEFAULT_BRIGHT))
    decay = params.get("decay_seconds", DEFAULT_DECAY)
    key_state = params.get("key_state", {})

    bolts_enabled = params.get("bolts", True)
    bolt_shape = params.get("bolt_shape", DEFAULT_BOLT_SHAPE)
    bolt_directions = params.get("bolt_directions", DEFAULT_BOLT_DIRECTIONS)
    bolt_speed = params.get("bolt_speed", DEFAULT_BOLT_SPEED)
    bolt_tail = params.get("bolt_tail", DEFAULT_BOLT_TAIL)
    bolt_tolerance = params.get("bolt_tolerance", DEFAULT_BOLT_TOLERANCE)
    bolt_style = params.get("bolt_style", DEFAULT_BOLT_STYLE)
    bolt_flicker_speed = params.get("bolt_flicker_speed", DEFAULT_BOLT_FLICKER_SPEED)

    # Tracked separately since a bolt can use a different color source
    # (rainbow noise) than the in-place flash (always bright_color).
    flash_intensity = [0.0] * num_cells
    bolt_intensity = [0.0] * num_cells

    # In-place flash for the pressed key itself. Each press fades
    # independently; if the same key was hit more than once recently,
    # take whichever press is currently brightest.
    if decay > 0:
        for idx, elapsed_list in key_state.items():
            idx = int(idx)
            if not (0 <= idx < num_cells):
                continue
            for elapsed in elapsed_list:
                v = max(0.0, 1.0 - elapsed / decay)
                if v > flash_intensity[idx]:
                    flash_intensity[idx] = v

    # Chasing bolts outward from each press across the physical grid.
    # Each individual press spawns its own wave (not one per key), so
    # mashing one key sends multiple overlapping bolts.
    if bolts_enabled and bolt_tail > 0 and _POSITIONS:
        unit_dirs = [_unit(d) for d in bolt_directions] if bolt_shape == "rays" else None
        for origin_key, elapsed_list in key_state.items():
            origin_pos = _POSITIONS.get(int(origin_key))
            if origin_pos is None:
                continue
            for elapsed in elapsed_list:
                head_distance = elapsed * bolt_speed
                for target_idx, target_pos in _POSITIONS.items():
                    dr = target_pos[0] - origin_pos[0]
                    dc = target_pos[1] - origin_pos[1]
                    if dr == 0 and dc == 0:
                        continue  # origin cell already handled above

                    if bolt_shape == "radial":
                        # True 360 degree ring: only straight-line
                        # distance from the origin matters, no fixed
                        # direction set at all.
                        tail = head_distance - math.hypot(dr, dc)
                        if tail < 0 or tail > bolt_tail:
                            continue
                        v = 1.0 - (tail / bolt_tail)
                        if v > bolt_intensity[target_idx]:
                            bolt_intensity[target_idx] = v
                        continue

                    # "rays": project the offset onto each requested
                    # direction; a cell "belongs" to that ray if it's
                    # close enough to the line and within the fading
                    # tail behind the head.
                    for ur, uc in unit_dirs:
                        projection = dr * ur + dc * uc
                        if projection <= 0:
                            continue
                        perp = math.hypot(dr - projection * ur, dc - projection * uc)
                        if perp > bolt_tolerance:
                            continue
                        tail = head_distance - projection
                        if tail < 0 or tail > bolt_tail:
                            continue
                        v = 1.0 - (tail / bolt_tail)
                        if v > bolt_intensity[target_idx]:
                            bolt_intensity[target_idx] = v

    tick = int(t * bolt_flicker_speed)
    colors = []
    for i in range(num_cells):
        cell_base = base_colors[i]
        fv = flash_intensity[i]
        bv = bolt_intensity[i]
        if bv > 0 and bv >= fv:
            v = bv
            if bolt_style == "rainbow":
                hue = _pseudo_random(i, tick)
                r, g, b = colorsys.hsv_to_rgb(hue, 1.0, 1.0)
                target = (int(r * 255), int(g * 255), int(b * 255))
            else:
                target = bright
        elif fv > 0:
            v = fv
            target = bright
        else:
            colors.append(cell_base)
            continue
        colors.append(tuple(int(cell_base[c] + (target[c] - cell_base[c]) * v) for c in range(3)))
    return colors
