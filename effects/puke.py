"""Chaotic per-key rainbow noise. Unlike rainbow.py's smooth hue wave,
every cell independently flickers to a new pseudo-random hue at its
own rate -- garish and glitchy on purpose. 'Puke' is accurate."""

import colorsys

NAME = "puke"
DEFAULT_SPEED = 6.0  # flickers per second


def render(t, num_cells, params):
    speed = params.get("speed", DEFAULT_SPEED)
    tick = int(t * speed)
    colors = []
    for i in range(num_cells):
        hue = _pseudo_random(i, tick)
        r, g, b = colorsys.hsv_to_rgb(hue, 1.0, 1.0)
        colors.append((int(r * 255), int(g * 255), int(b * 255)))
    return colors


def _pseudo_random(i, tick):
    """Cheap deterministic hash -> [0,1). Each (cell, tick) pair gets
    its own stable "random" hue, no external RNG state needed."""
    x = (i * 2654435761 + tick * 40503) & 0xFFFFFFFF
    x ^= x >> 15
    x = (x * 2246822519) & 0xFFFFFFFF
    x ^= x >> 13
    return (x & 0xFFFF) / 65536.0
