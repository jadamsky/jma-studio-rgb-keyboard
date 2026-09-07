"""Color Wipe: a solid color progressively fills the keyboard from one
side to the other, then the next color wipes over it -- WLED's "Wipe",
a simple, clean effect that's consistently one of the most-used.

params:
    colors  list of RGB colors to cycle through. Default a 4-color set.
    speed   grid units/second the wipe front travels. Default 10.
"""

import os

from effects.layout import cell_positions

NAME = "color_wipe"

DEFAULT_COLORS = [(255, 40, 40), (40, 120, 255), (40, 220, 120), (255, 200, 40)]
DEFAULT_SPEED = 10.0

_KEYMAP_PATH = os.path.join(os.path.dirname(os.path.dirname(__file__)), "keymap.json")
_POSITIONS = cell_positions(_KEYMAP_PATH)
if _POSITIONS:
    _MIN_COL = min(p[1] for p in _POSITIONS.values())
    _MAX_COL = max(p[1] for p in _POSITIONS.values())
else:
    _MIN_COL, _MAX_COL = 0.0, 20.0


def render(t, num_cells, params):
    palette = [tuple(c) for c in params.get("colors", DEFAULT_COLORS)]
    speed = params.get("speed", DEFAULT_SPEED)

    span = (_MAX_COL - _MIN_COL) or 1.0
    if not _POSITIONS or not palette:
        return [(0, 0, 0)] * num_cells

    progress = (t * speed) / span
    step = int(progress) % len(palette)
    frac = progress - int(progress)
    front_col = _MIN_COL + frac * span
    current_color = palette[step]
    previous_color = palette[(step - 1) % len(palette)]

    colors = []
    for idx in range(num_cells):
        pos = _POSITIONS.get(idx)
        if pos is None:
            colors.append(current_color)
            continue
        colors.append(current_color if pos[1] <= front_col else previous_color)
    return colors
