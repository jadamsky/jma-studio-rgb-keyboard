// Lightweight rolling latency counter for the Diagnostics window's perf
// tile (HID write / WMI commit timings). Deliberately just min/avg/max/
// count -- a live "is it healthy" glance, not a profiling tool, so no
// percentiles or retained history.

namespace JmaStudio.Hardware;

public sealed class LatencyStats
{
    private readonly object _lock = new();
    private double _minMs = double.MaxValue;
    private double _maxMs;
    private double _sumMs;
    private long _count;

    public void Record(double ms)
    {
        lock (_lock)
        {
            if (ms < _minMs) _minMs = ms;
            if (ms > _maxMs) _maxMs = ms;
            _sumMs += ms;
            _count++;
        }
    }

    public (double MinMs, double AvgMs, double MaxMs, long Count) Snapshot()
    {
        lock (_lock)
        {
            return _count == 0 ? (0, 0, 0, 0) : (_minMs, _sumMs / _count, _maxMs, _count);
        }
    }
}
