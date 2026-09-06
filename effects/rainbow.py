"""Rainbow wave -- a hue rotation across all 128 cells over time.

This is the kind of effect PredatorSense's firmware never gave you:
the math runs here in Python, not on the keyboard, so it's freely
editable and not limited to whatever Acer chose to expose."""

import colorsys

NAME = "rainbow"
DEFAULT_SPEED = 0.15  # cycles per second


def render(t, num_cells, params):
    speed = params.get("speed", DEFAULT_SPEED)
    colors = []
    for i in range(num_cells):
        hue = ((i / num_cells) + t * speed) % 1.0
        r, g, b = colorsys.hsv_to_rgb(hue, 1.0, 1.0)
        colors.append((int(r * 255), int(g * 255), int(b * 255)))
    return colors
