"""Scanner: a single bright band sweeps back and forth across the
keyboard, like KITT's scanner light from Knight Rider. WLED calls this
"Scan"/"Dual Scan" -- one of the most iconic addressable-LED effects.

params:
    color       RGB of the band. Default red.
    base_color  RGB for cells the band isn't over. Default off.
    speed       grid units/second. Default 10.
    width       band width in grid units. Default 3.
"""

import os

from effects.layout import cell_positions

NAME = "scanner"

DEFAULT_COLOR = (255, 30, 30)
DEFAULT_BASE = (0, 0, 0)
DEFAULT_SPEED = 10.0
DEFAULT_WIDTH = 3.0

_KEYMAP_PATH = os.path.join(os.path.dirname(os.path.dirname(__file__)), "keymap.json")
_POSITIONS = cell_positions(_KEYMAP_PATH)
if _POSITIONS:
    _MIN_COL = min(p[1] for p in _POSITIONS.values())
    _MAX_COL = max(p[1] for p in _POSITIONS.values())
else:
    _MIN_COL, _MAX_COL = 0.0, 20.0


def render(t, num_cells, params):
    color = tuple(params.get("color", DEFAULT_COLOR))
    base = tuple(params.get("base_color", DEFAULT_BASE))
    speed = params.get("speed", DEFAULT_SPEED)
    width = params.get("width", DEFAULT_WIDTH)

    span = _MAX_COL - _MIN_COL
    if span <= 0 or not _POSITIONS:
        return [base] * num_cells

    cycle = span * 2
    local_t = (t * speed) % cycle
    pos_col = (local_t if local_t <= span else cycle - local_t) + _MIN_COL  # ping-pong

    colors = []
    for idx in range(num_cells):
        pos = _POSITIONS.get(idx)
        if pos is None:
            colors.append(base)
            continue
        dist = abs(pos[1] - pos_col)
        if dist < width:
            v = 1.0 - (dist / width)
            colors.append(tuple(int(base[c] + (color[c] - base[c]) * v) for c in range(3)))
        else:
            colors.append(base)
    return colors
