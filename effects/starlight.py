"""Starlight: random keys softly fade in and out independently against
a dim background, like stars twinkling. Modeled on Razer Chroma's
"Starlight" -- one of its most-loved built-in animations, and visually
distinct from puke.py's chaotic rapid hue-flicker (this is slow, soft,
and monochrome by default).

params:
    color       RGB a twinkling key fades up to. Default white.
    base_color  RGB for keys not currently twinkling. Default near-off.
    density     fraction (0-1) of keys twinkling at any given moment.
                Default 0.25.
    speed       twinkle cycles per second (higher = faster twinkling).
                Default 0.6.
"""

from effects.noise import pseudo_random01

NAME = "starlight"

DEFAULT_COLOR = (255, 255, 255)
DEFAULT_BASE = (5, 5, 10)
DEFAULT_DENSITY = 0.25
DEFAULT_SPEED = 0.6


def render(t, num_cells, params):
    color = tuple(params.get("color", DEFAULT_COLOR))
    base = tuple(params.get("base_color", DEFAULT_BASE))
    density = params.get("density", DEFAULT_DENSITY)
    speed = max(0.01, params.get("speed", DEFAULT_SPEED))

    colors = []
    for i in range(num_cells):
        if pseudo_random01(i, 1) > density:
            colors.append(base)
            continue
        # Each selected key gets its own cycle length and phase offset
        # (both derived from the cell index) so twinkles don't all
        # pulse in lockstep.
        period = (1.5 + pseudo_random01(i, 2) * 2.5) / speed
        offset = pseudo_random01(i, 3) * period
        phase = ((t + offset) % period) / period  # 0..1
        v = max(0.0, 1.0 - abs(phase * 2 - 1)) ** 1.5  # triangular fade in/out
        colors.append(tuple(int(base[c] + (color[c] - base[c]) * v) for c in range(3)))
    return colors
