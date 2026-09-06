"""Solid color effect -- the simplest possible render function.
Every effect in this folder follows this same contract."""

NAME = "static"


def render(t, num_cells, params):
    color = tuple(params.get("color", (0, 0, 0)))
    return [color] * num_cells
