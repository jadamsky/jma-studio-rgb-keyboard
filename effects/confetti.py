"""Confetti: random keys spark to a bright random color and quickly
fade, against a dim background -- WLED's "Confetti", one of its most
frequently used default effects.

params:
    base_color      RGB for cells with no active spark. Default off.
    spawn_rate      average new sparks per second, board-wide. Default 6.
    decay_seconds   time for a spark to fade out. Default 0.5.
"""

import colorsys

from effects.noise import pseudo_random01

NAME = "confetti"

DEFAULT_BASE = (0, 0, 0)
DEFAULT_SPAWN_RATE = 6.0
DEFAULT_DECAY = 0.5


def render(t, num_cells, params):
    base = tuple(params.get("base_color", DEFAULT_BASE))
    spawn_rate = params.get("spawn_rate", DEFAULT_SPAWN_RATE)
    decay = params.get("decay_seconds", DEFAULT_DECAY)

    colors = [base] * num_cells
    if spawn_rate <= 0 or decay <= 0 or num_cells == 0:
        return colors

    # Each cell has its own "spark schedule": a repeating period sized
    # so the board averages spawn_rate sparks/sec overall, with its own
    # random phase offset so cells don't spark in lockstep.
    period = num_cells / spawn_rate
    for i in range(num_cells):
        offset = pseudo_random01(i, 0, 5) * period
        cycle_index = int((t + offset) / period)
        local_t = (t + offset) % period
        if local_t > decay:
            continue
        v = 1.0 - (local_t / decay)
        hue = pseudo_random01(i, cycle_index, 7)
        r, g, b = colorsys.hsv_to_rgb(hue, 1.0, 1.0)
        spark = (int(r * 255), int(g * 255), int(b * 255))
        colors[i] = tuple(int(base[c] + (spark[c] - base[c]) * v) for c in range(3))
    return colors
