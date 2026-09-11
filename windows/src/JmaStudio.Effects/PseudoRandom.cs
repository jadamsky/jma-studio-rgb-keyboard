// Port of effects/noise.py's pseudo_random01 -- a cheap deterministic
// hash of (i, tick, salt) so several effects can get an independent
// "random-looking" value per (cell, time-tick) pair without external
// RNG state, keeping Render() a pure function of its arguments.

namespace JmaStudio.Effects;

public static class PseudoRandom
{
    public static double Value01(int i, int tick, int salt = 0)
    {
        // Computed in `long` first (matching Python's arbitrary-precision
        // arithmetic before its explicit & 0xFFFFFFFF mask) then narrowed
        // to uint, rather than multiplying directly in 32-bit -- avoids a
        // compile-time int/uint operator ambiguity and, more importantly,
        // avoids silently wrapping differently than Python's mask-after-
        // full-precision-sum semantics for any future negative input.
        long sum = (long)i * 2654435761L + (long)tick * 40503L + (long)salt * 97L;
        uint x = unchecked((uint)(sum & 0xFFFFFFFFL));
        x ^= x >> 15;
        x = unchecked(x * 2246822519U);
        x ^= x >> 13;
        return (x & 0xFFFF) / 65536.0;
    }
}
