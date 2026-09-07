"""Comet: a bright head with a fading tail sweeps continuously across
the keyboard left-to-right and wraps around -- WLED calls this
"Meteor", a visually striking and popular ambient effect.

params:
    color       RGB of the comet head. Default warm gold.
    base_color  RGB for cells with no comet passing through. Default off.
    speed       grid units/second. Default 8.
    tail        fading tail length in grid units. Default 5.
"""

import os

from effects.layout import cell_positions

NAME = "comet"

DEFAULT_COLOR = (255, 200, 60)
DEFAULT_BASE = (0, 0, 0)
DEFAULT_SPEED = 8.0
DEFAULT_TAIL = 5.0

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
    tail = params.get("tail", DEFAULT_TAIL)

    span = (_MAX_COL - _MIN_COL) + tail
    if span <= 0 or not _POSITIONS:
        return [base] * num_cells

    head_col = _MIN_COL + ((t * speed) % span) - tail

    colors = []
    for idx in range(num_cells):
        pos = _POSITIONS.get(idx)
        if pos is None:
            colors.append(base)
            continue
        behind = head_col - pos[1]
        if 0 <= behind <= tail:
            v = 1.0 - (behind / tail)
            colors.append(tuple(int(base[c] + (color[c] - base[c]) * v) for c in range(3)))
        else:
            colors.append(base)
    return colors
