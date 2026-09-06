"""Lights an arbitrary set of cells at once -- used to visually verify
keymap coverage (light every mapped cell, see if any real key stays
dark or any lit cell doesn't correspond to a mapped key)."""

NAME = "mask"


def render(t, num_cells, params):
    indices = params.get("indices", [])
    color = tuple(params.get("color", (255, 255, 255)))
    colors = [(0, 0, 0)] * num_cells
    for i in indices:
        if 0 <= i < num_cells:
            colors[i] = color
    return colors
