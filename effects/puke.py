"""Chaotic per-key rainbow noise. Unlike rainbow.py's smooth hue wave,
every cell independently flickers to a new pseudo-random hue at its
own rate -- garish and glitchy on purpose. 'Puke' is accurate."""

import colorsys

from effects.noise import pseudo_random01

NAME = "puke"
DEFAULT_SPEED = 6.0  # flickers per second


def render(t, num_cells, params):
    speed = params.get("speed", DEFAULT_SPEED)
    tick = int(t * speed)
    colors = []
    for i in range(num_cells):
        hue = pseudo_random01(i, tick)
        r, g, b = colorsys.hsv_to_rgb(hue, 1.0, 1.0)
        colors.append((int(r * 255), int(g * 255), int(b * 255)))
    return colors
