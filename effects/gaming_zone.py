"""Highlights the WASD + arrow-key movement cluster; everything else
dims down. Looks up cell indices by key name from keymap.json, so it
only works meaningfully once keymap discovery has been run.

params:
    keys         list of key names (from keymap.json) to highlight.
                 Defaults to WASD + arrows.
    bright_color RGB tuple for the highlighted keys. Default green.
    dim_color    RGB tuple for everything else. Default off.
"""

import os

from effects.layout import name_to_index

NAME = "gaming_zone"

DEFAULT_KEYS = ["w", "a", "s", "d", "up_arrow", "down_arrow", "left_arrow", "right_arrow"]
DEFAULT_BRIGHT = (0, 255, 0)
DEFAULT_DIM = (0, 0, 0)

_KEYMAP_PATH = os.path.join(os.path.dirname(os.path.dirname(__file__)), "keymap.json")

# Loaded once at import time, same as the daemon's own one-time effect
# load at startup -- restart the daemon after editing keymap.json.
_INDEX_BY_NAME = name_to_index(_KEYMAP_PATH)


def render(t, num_cells, params):
    keys = params.get("keys", DEFAULT_KEYS)
    bright = tuple(params.get("bright_color", DEFAULT_BRIGHT))
    dim = tuple(params.get("dim_color", DEFAULT_DIM))

    colors = [dim] * num_cells
    for name in keys:
        idx = _INDEX_BY_NAME.get(name)
        if idx is not None and 0 <= idx < num_cells:
            colors[idx] = bright
    return colors
