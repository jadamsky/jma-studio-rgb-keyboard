"""Aurora: slow flowing waves of green, blue, and purple across the
keyboard, resembling the northern lights -- a popular ambient mode in
modern RGB software.

params:
    speed   how fast the waves drift over time. Default 0.15.
    scale   spatial frequency of the waves across the grid. Default 0.15.
"""

import math
import os

from effects.layout import cell_positions

NAME = "aurora"

DEFAULT_SPEED = 0.15
DEFAULT_SCALE = 0.15

_KEYMAP_PATH = os.path.join(os.path.dirname(os.path.dirname(__file__)), "keymap.json")
_POSITIONS = cell_positions(_KEYMAP_PATH)

_PALETTE = [
    (10, 20, 40),      # deep night blue
    (20, 200, 140),    # aurora green
    (60, 90, 220),     # blue
    (150, 60, 220),    # purple
]


def _palette_color(f):
    """f in [0,1) -> smooth blend walking around the palette ring."""
    n = len(_PALETTE)
    scaled = (f % 1.0) * n
    i0 = int(scaled) % n
    i1 = (i0 + 1) % n
    frac = scaled - int(scaled)
    c0, c1 = _PALETTE[i0], _PALETTE[i1]
    return tuple(int(c0[c] + (c1[c] - c0[c]) * frac) for c in range(3))


def render(t, num_cells, params):
    speed = params.get("speed", DEFAULT_SPEED)
    scale = params.get("scale", DEFAULT_SCALE)

    colors = []
    for i in range(num_cells):
        pos = _POSITIONS.get(i)
        if pos is None:
            colors.append(_PALETTE[0])
            continue
        row, col = pos
        wave = (
            math.sin(col * scale + t * speed * 4) * 0.5
            + math.sin(row * scale * 2 - t * speed * 2.5) * 0.5
        )
        f = (wave + 2) / 4  # roughly normalize to [0,1]
        colors.append(_palette_color(f))
    return colors
