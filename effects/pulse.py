"""Pulse: the whole keyboard snaps bright then decays quickly on a
beat -- distinct from breathing.py's slow, smooth, symmetric fade,
this has a sharp attack and fast decay. A staple Corsair/Razer effect.

params:
    color       RGB to pulse. Ignored if cycle_hue is True. Default red.
    rate        pulses per second. Default 1.2.
    cycle_hue   if True, each pulse's color slowly cycles through the
                hue wheel instead of staying fixed. Default False.
"""

import colorsys

NAME = "pulse"

DEFAULT_COLOR = (255, 60, 60)
DEFAULT_RATE = 1.2
DEFAULT_CYCLE_HUE = False


def render(t, num_cells, params):
    rate = params.get("rate", DEFAULT_RATE)
    cycle_hue = params.get("cycle_hue", DEFAULT_CYCLE_HUE)

    if cycle_hue:
        hue = (t * 0.1) % 1.0
        r, g, b = colorsys.hsv_to_rgb(hue, 1.0, 1.0)
        color = (int(r * 255), int(g * 255), int(b * 255))
    else:
        color = tuple(params.get("color", DEFAULT_COLOR))

    period = 1.0 / rate if rate > 0 else 1.0
    phase = (t % period) / period  # 0..1, resets every pulse
    v = max(0.0, 1.0 - phase * 2.2)  # snap bright, fade over ~45% of the period, dark the rest

    scaled = tuple(int(c * v) for c in color)
    return [scaled] * num_cells
