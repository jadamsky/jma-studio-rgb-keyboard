"""Lights exactly one cell -- used by the keymap discovery process to
find out which physical key each cell index corresponds to. Not a
"real" effect for end use, just a diagnostic building block."""

NAME = "probe"


def render(t, num_cells, params):
    index = params.get("index", 0)
    color = tuple(params.get("color", (255, 255, 255)))
    colors = [(0, 0, 0)] * num_cells
    if 0 <= index < num_cells:
        colors[index] = color
    return colors
