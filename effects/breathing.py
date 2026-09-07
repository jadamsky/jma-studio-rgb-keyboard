"""Breathing: the whole keyboard fades a single color smoothly in and
out. One of the most widely used RGB effects across every major
ecosystem (Corsair, Razer, etc.) -- simple, calm, and rarely disliked.

params:
    color   RGB to breathe. Default a soft blue.
    speed   breaths per second. Default 0.5 (a slow, relaxed breath).
"""

import math

NAME = "breathing"

DEFAULT_COLOR = (79, 123, 255)
DEFAULT_SPEED = 0.5


def render(t, num_cells, params):
    color = tuple(params.get("color", DEFAULT_COLOR))
    speed = params.get("speed", DEFAULT_SPEED)

    # Cosine easing lingers near fully-on/fully-off rather than
    # spending equal time in the middle, which reads as a more natural
    # "breath" than a plain triangle wave.
    v = (math.cos(2 * math.pi * speed * t) + 1) / 2
    scaled = tuple(int(c * v) for c in color)
    return [scaled] * num_cells
