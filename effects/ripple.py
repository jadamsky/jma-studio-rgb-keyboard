"""Ripple: concentric rings of color continuously emanate outward from
the center of the keyboard, fading as they travel -- a classic ambient
effect (WLED calls this "Ripple"). Unlike typing_reactive's press-driven
bolts, this runs continuously with no input needed.

params:
    color       RGB of the ring. Default a light blue.
    base_color  RGB for cells with no ring passing through. Default off.
    speed       grid units/second the ring expands. Default 6.
    interval    seconds between new ripples being emitted. Default 2.
    width       ring thickness in grid units. Default 2.5.
"""

import math
import os

from effects.layout import cell_positions

NAME = "ripple"

DEFAULT_COLOR = (79, 190, 255)
DEFAULT_BASE = (0, 0, 0)
DEFAULT_SPEED = 6.0
DEFAULT_INTERVAL = 2.0
DEFAULT_WIDTH = 2.5

_KEYMAP_PATH = os.path.join(os.path.dirname(os.path.dirname(__file__)), "keymap.json")
_POSITIONS = cell_positions(_KEYMAP_PATH)
if _POSITIONS:
    _CENTER_ROW = sum(p[0] for p in _POSITIONS.values()) / len(_POSITIONS)
    _CENTER_COL = sum(p[1] for p in _POSITIONS.values()) / len(_POSITIONS)
else:
    _CENTER_ROW, _CENTER_COL = 0.0, 0.0


def render(t, num_cells, params):
    color = tuple(params.get("color", DEFAULT_COLOR))
    base = tuple(params.get("base_color", DEFAULT_BASE))
    speed = params.get("speed", DEFAULT_SPEED)
    interval = params.get("interval", DEFAULT_INTERVAL)
    width = params.get("width", DEFAULT_WIDTH)

    colors = [base] * num_cells
    if not _POSITIONS or interval <= 0:
        return colors

    # However many ripples have "emitted" by time t, each aging
    # independently -- only the last few are still visible, so we skip
    # anything older than that instead of iterating every ripple ever.
    n_emitted = int(t / interval) + 1
    for n in range(max(0, n_emitted - 4), n_emitted):
        age = t - (n * interval)
        if age < 0:
            continue
        radius = age * speed
        for idx, pos in _POSITIONS.items():
            dist = math.hypot(pos[0] - _CENTER_ROW, pos[1] - _CENTER_COL)
            delta = abs(dist - radius)
            if delta >= width:
                continue
            v = 1.0 - (delta / width)
            cur = colors[idx]
            colors[idx] = tuple(int(cur[c] + (color[c] - cur[c]) * v) for c in range(3))
    return colors
