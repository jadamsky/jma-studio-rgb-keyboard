"""Spectrum Cycle: every key shows the exact same color at once,
sweeping through the full hue wheel together over time. Razer Chroma
calls this "Spectrum Cycling" -- one of its most-used built-in effects.
Distinct from effects/rainbow.py, which staggers hue by position for a
traveling-wave look; this one keeps the whole board in perfect sync.

params:
    speed   cycles per second through the hue wheel. Default 0.1 (a
            full cycle every 10 seconds -- slow and ambient).
"""

import colorsys

NAME = "spectrum_cycle"

DEFAULT_SPEED = 0.1


def render(t, num_cells, params):
    speed = params.get("speed", DEFAULT_SPEED)
    hue = (t * speed) % 1.0
    r, g, b = colorsys.hsv_to_rgb(hue, 1.0, 1.0)
    color = (int(r * 255), int(g * 255), int(b * 255))
    return [color] * num_cells
