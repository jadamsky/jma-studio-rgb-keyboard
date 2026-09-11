// Thread-safe {cellIndex: [seconds-since-press, ...]} tracker fed by
// GlobalKeyboardHook -- the C# analogue of Python's InputListener
// (daemon/input_listener.py) _last_press/snapshot(), scoped down to
// just what's needed for interactive effect testing (no /status
// diagnostics counters).

using System.Diagnostics;

namespace JmaStudio.HardwareTest;

public sealed class LiveKeyState
{
    private readonly object _lock = new();
    private readonly Dictionary<int, List<double>> _lastPress = new();
    private readonly IReadOnlyDictionary<string, int> _indexByName;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public LiveKeyState(IReadOnlyDictionary<string, int> indexByName)
    {
        _indexByName = indexByName;
    }

    /// <summary>Wire this directly to GlobalKeyboardHook.KeyDown.</summary>
    public void OnKeyDown(string name)
    {
        if (!_indexByName.TryGetValue(name, out int idx)) return;
        double now = _clock.Elapsed.TotalSeconds;
        lock (_lock)
        {
            if (!_lastPress.TryGetValue(idx, out List<double>? list))
            {
                list = new List<double>();
                _lastPress[idx] = list;
            }
            list.Add(now);
        }
    }

    /// <summary>Returns {cellIndex: [secondsSincePress, ...]} for presses
    /// within the last `maxAge` seconds, pruning anything older.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<double>> Snapshot(double maxAge)
    {
        double now = _clock.Elapsed.TotalSeconds;
        var result = new Dictionary<int, IReadOnlyList<double>>();
        lock (_lock)
        {
            var toRemove = new List<int>();
            foreach ((int idx, List<double> presses) in _lastPress)
            {
                var kept = presses.Where(p => now - p <= maxAge).ToList();
                if (kept.Count > 0)
                {
                    _lastPress[idx] = kept;
                    result[idx] = kept.Select(p => now - p).ToArray();
                }
                else
                {
                    toRemove.Add(idx);
                }
            }
            foreach (int idx in toRemove) _lastPress.Remove(idx);
        }
        return result;
    }
}
