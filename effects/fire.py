"""Fire: flickering flame colors across the keyboard, each key an
independent ember -- inspired by WLED's beloved "Fire 2012" effect,
one of the most widely used addressable-LED animations there is.

params:
    speed       flicker updates per second. Default 8.
    intensity   0-1.5ish overall heat/brightness scale. Default 1.0.
"""

from effects.noise import pseudo_random01

NAME = "fire"

DEFAULT_SPEED = 8.0
DEFAULT_INTENSITY = 1.0


def _fire_color(heat):
    """heat in [0,1] -> black -> red -> orange -> yellow-white."""
    heat = max(0.0, min(1.0, heat))
    if heat < 0.4:
        f = heat / 0.4
        return (int(255 * f), 0, 0)
    if heat < 0.8:
        f = (heat - 0.4) / 0.4
        return (255, int(140 * f), 0)
    f = (heat - 0.8) / 0.2
    return (255, int(140 + 115 * f), int(200 * f))


def render(t, num_cells, params):
    speed = params.get("speed", DEFAULT_SPEED)
    intensity = params.get("intensity", DEFAULT_INTENSITY)

    scaled_t = t * speed
    tick = int(scaled_t)
    frac = scaled_t - tick

    colors = []
    for i in range(num_cells):
        # Blend two adjacent random ticks so the flicker looks
        # continuous rather than jumping between discrete random values.
        h0 = pseudo_random01(i, tick)
        h1 = pseudo_random01(i, tick + 1)
        heat = (h0 * (1 - frac) + h1 * frac) * intensity
        colors.append(_fire_color(heat))
    return colors
