"""Fully custom per-key static colors -- paint each key individually.
Keys without an explicit override fall back to `default_color`. The
GUI provides an editor (keyboard grid + color picker + recently-used
swatches) for building `colors`, but the param shape is deliberately
simple and hand-editable too.

params:
    colors          dict of {cell index (str): [r,g,b]} -- overrides
                     for individual cells. Cell indices match the ones
                     returned by the daemon's /layout endpoint.
    default_color   RGB for every cell without an override. Default
                     off (0,0,0).
"""

NAME = "custom_keys"

DEFAULT_COLOR = (0, 0, 0)


def render(t, num_cells, params):
    default_color = tuple(params.get("default_color", DEFAULT_COLOR))
    overrides = params.get("colors", {})

    colors = [default_color] * num_cells
    for idx_str, color in overrides.items():
        idx = int(idx_str)
        if 0 <= idx < num_cells:
            colors[idx] = tuple(color)
    return colors
