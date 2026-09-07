"""Rain: bright droplets fall down each column of keys continuously,
fading as they go -- a classic ambient keyboard effect. Distinct from
typing_reactive: this runs on its own, not from keypresses.

params:
    color       RGB of a droplet. Default a cool blue.
    base_color  RGB for cells with no droplet. Default off.
    speed       rows/second a drop falls. Default 10.
    spawn_rate  average new drops per second across the whole board.
                Default 3.
    tail        fading tail length behind each drop, in rows. Default 2.5.
"""

import os

from effects.layout import cell_positions
from effects.noise import pseudo_random01

NAME = "rain"

DEFAULT_COLOR = (90, 160, 255)
DEFAULT_BASE = (0, 0, 0)
DEFAULT_SPEED = 10.0
DEFAULT_SPAWN_RATE = 3.0
DEFAULT_TAIL = 2.5

_KEYMAP_PATH = os.path.join(os.path.dirname(os.path.dirname(__file__)), "keymap.json")
_POSITIONS = cell_positions(_KEYMAP_PATH)
if _POSITIONS:
    _MIN_COL = min(p[1] for p in _POSITIONS.values())
    _MAX_COL = max(p[1] for p in _POSITIONS.values())
    _MIN_ROW = min(p[0] for p in _POSITIONS.values())
    _MAX_ROW = max(p[0] for p in _POSITIONS.values())
else:
    _MIN_COL, _MAX_COL, _MIN_ROW, _MAX_ROW = 0.0, 20.0, 0.0, 5.0


def render(t, num_cells, params):
    color = tuple(params.get("color", DEFAULT_COLOR))
    base = tuple(params.get("base_color", DEFAULT_BASE))
    speed = max(0.01, params.get("speed", DEFAULT_SPEED))
    spawn_rate = params.get("spawn_rate", DEFAULT_SPAWN_RATE)
    tail = params.get("tail", DEFAULT_TAIL)

    colors = [base] * num_cells
    if not _POSITIONS or spawn_rate <= 0:
        return colors

    # A fixed pool of "drop slots", each one periodically restarting at
    # its own pseudo-random column and phase -- looks like independent
    # random drops without needing state persisted between frames.
    row_span = (_MAX_ROW - _MIN_ROW) + tail
    num_slots = max(1, int(spawn_rate * row_span / speed))
    for slot in range(num_slots):
        cycle = row_span / speed + pseudo_random01(slot, 9) * 0.5
        col = _MIN_COL + pseudo_random01(slot, 1) * (_MAX_COL - _MIN_COL)
        phase_offset = pseudo_random01(slot, 2) * cycle
        local_t = (t + phase_offset) % cycle
        head_row = _MIN_ROW + local_t * speed
        for idx, pos in _POSITIONS.items():
            if abs(pos[1] - col) > 0.6:
                continue
            behind = head_row - pos[0]
            if behind < 0 or behind > tail:
                continue
            v = 1.0 - (behind / tail)
            cur = colors[idx]
            colors[idx] = tuple(int(cur[c] + (color[c] - cur[c]) * v) for c in range(3))
    return colors
