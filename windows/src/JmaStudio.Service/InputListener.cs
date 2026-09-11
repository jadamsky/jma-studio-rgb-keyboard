// C# analogue of Python's daemon/input_listener.py: owns the global
// keyboard hook and a thread-safe {cellIndex: [pressTime, ...]} map.
// Each press is kept independently (not just the most recent) so
// effects can spawn one animation per press rather than one per key --
// mashing the same key repeatedly fires overlapping animations instead
// of the newest press resetting/replacing the previous one.

using System.Diagnostics;

namespace JmaStudio.Service;

public sealed class InputListener : IDisposable
{
    private readonly object _lock = new();
    private readonly IReadOnlyDictionary<string, int> _indexByName;
    private readonly Dictionary<int, List<double>> _lastPress = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly GlobalKeyboardHook _hook;

    private long _rawEventCount;
    private long _matchedEventCount;
    private double? _lastRawEventTime;

    public InputListener(IReadOnlyDictionary<string, int> indexByName)
    {
        _indexByName = indexByName;
        _hook = new GlobalKeyboardHook();
        _hook.KeyDown += RecordKeyDown;
    }

    /// <summary>Starts this Service's OWN local hook -- only meaningful
    /// when the Service happens to be running in the interactive session
    /// (dev-mode console, not an installed Windows Service). See
    /// Program.cs's call site: NOT started by default any more, since a
    /// real installed Service runs in Session 0 and this hook would never
    /// see interactive-desktop keystrokes there anyway (see HANDOFF.md's
    /// Phase 7 "Critical finding") -- production key capture now comes
    /// from JmaStudio.Gui's KeypressForwarder via RecordKeyDown below.</summary>
    public void Start() => _hook.Start();

    /// <summary>The single entry point for a real keydown, regardless of
    /// source: this Service's own local hook (dev-mode only, see Start()
    /// above) OR JmaStudio.Gui's KeypressForwarder via POST /keypress
    /// (Endpoints.MapInput) -- the production path once the Service runs
    /// as a real installed Windows Service in Session 0.</summary>
    public void RecordKeyDown(string name)
    {
        double now = _clock.Elapsed.TotalSeconds;
        lock (_lock)
        {
            _rawEventCount++;
            _lastRawEventTime = now;
        }
        if (!_indexByName.TryGetValue(name, out int idx)) return;
        lock (_lock)
        {
            _matchedEventCount++;
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

    /// <summary>Raw hook-callback counters, for telling apart "the OS
    /// hook isn't firing at all" from "it's firing but something
    /// downstream is wrong" when debugging responsiveness issues --
    /// mirrors Python's InputListener.diagnostics().</summary>
    public InputListenerDiagnostics Diagnostics()
    {
        lock (_lock)
        {
            return new InputListenerDiagnostics(
                _rawEventCount,
                _matchedEventCount,
                _lastRawEventTime is { } last ? _clock.Elapsed.TotalSeconds - last : null);
        }
    }

    public void Dispose() => _hook.Dispose();
}

public readonly record struct InputListenerDiagnostics(
    long RawEventCount, long MatchedEventCount, double? SecondsSinceLastRawEvent);
