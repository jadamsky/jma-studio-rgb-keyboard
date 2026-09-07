"""Shared cheap deterministic pseudo-random helper. Several effects
(puke, typing_reactive, starlight, fire, rain, confetti) need an
independent "random-looking" value per (cell, time-tick) pair without
any external RNG state, so each render() call stays a pure function of
its arguments -- this used to be six near-identical copies of the same
six-line hash, one pasted into each of those files.

Not an effect itself (no NAME/render) -- the daemon's effects loader
just imports and skips it, same as layout.py.
"""


def pseudo_random01(i, tick, salt=0):
    """Deterministic hash of (i, tick, salt) -> a float in [0, 1).
    Passing only (i, tick) reproduces the original two-term formula
    exactly, since salt=0 contributes nothing to the sum."""
    x = (i * 2654435761 + tick * 40503 + salt * 97) & 0xFFFFFFFF
    x ^= x >> 15
    x = (x * 2246822519) & 0xFFFFFFFF
    x ^= x >> 13
    return (x & 0xFFFF) / 65536.0
