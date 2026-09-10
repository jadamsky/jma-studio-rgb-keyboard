// Port of daemon/server.py's _lightbar_reactive_loop(): flashes lightbar
// zones in response to keyboard presses, mapped by which of the
// keyboard's gradient zones (captured once via CaptureZones/
// POST /lightbar/reactive/capture_zones) the pressed key falls in -- zones
// 1-3 flash the matching physical lightbar zone, zone 4 (beyond the last
// boundary) flashes all three. Ticks much slower than RenderLoopService's
// 30fps since the lightbar's WMI protocol can't sustain that rate (same
// _REACTIVE_TICK rationale as the Python original).

using System.Diagnostics;
using JmaStudio.Effects;
using JmaStudio.Hardware;
using JmaStudio.Presets;

namespace JmaStudio.Service;

public sealed class LightbarReactiveManager : BackgroundService
{
    // Matches daemon/server.py's _REACTIVE_TICK/_REACTIVE_PRESS_MAX_AGE/
    // _KEY_STATE_MAX_AGE exactly -- these aren't arbitrary, see that
    // file's own comments for why (WMI can't sustain 30Hz; press-age
    // window controls how "sticky" a flash feels).
    private const double ReactiveTickSeconds = 0.08;
    private const double ReactivePressMaxAgeSeconds = 0.15;
    private const double KeyStateMaxAgeSeconds = 5.0;

    private readonly LightbarController _lightbar;
    private readonly InputListener _inputListener;
    private readonly PresetStore _store;
    private readonly IReadOnlyDictionary<int, GridPosition> _positions;
    private readonly ILogger<LightbarReactiveManager> _logger;
    private IReadOnlyDictionary<int, RgbColor>? _lastTargets;

    public LightbarReactiveManager(
        LightbarController lightbar, InputListener inputListener, PresetStore store,
        string keymapPath, ILogger<LightbarReactiveManager> logger)
    {
        _lightbar = lightbar;
        _inputListener = inputListener;
        _store = store;
        _positions = Layout.CellPositions(keymapPath);
        _logger = logger;
    }

    /// <summary>Forces the next tick to re-apply regardless of whether the
    /// active zone set actually changed -- call after any config edit, same
    /// as Python's `_last_reactive_targets = None` after a settings POST.</summary>
    public void ResetDedup() => _lastTargets = null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Lightbar reactive tick failed");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(ReactiveTickSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void Tick()
    {
        LightbarReactiveConfig config = _store.LightbarReactiveConfig.Load();
        if (!_lightbar.Available || !config.Enabled || config.ZoneBoundaries.Count == 0) return;

        IReadOnlyDictionary<int, IReadOnlyList<double>> snapshot = _inputListener.Snapshot(KeyStateMaxAgeSeconds);
        var activeZones = new HashSet<int>();
        foreach ((int index, IReadOnlyList<double> ages) in snapshot)
        {
            if (ages.Count == 0 || ages.Min() > ReactivePressMaxAgeSeconds) continue;
            if (!_positions.TryGetValue(index, out GridPosition pos)) continue;
            activeZones.Add(ZoneForColumn(pos.Col, config.ZoneBoundaries) + 1);
        }

        RgbColor background = config.BackgroundColor;
        var targets = new Dictionary<int, RgbColor>();
        if (activeZones.Contains(4))
        {
            RgbColor all = config.AllFlashColor;
            targets[1] = all;
            targets[2] = all;
            targets[3] = all;
        }
        else
        {
            for (int zone = 1; zone <= 3; zone++)
            {
                targets[zone] = activeZones.Contains(zone) && config.ZoneFlashColors.TryGetValue(zone, out RgbColor flashColor)
                    ? flashColor
                    : background;
            }
        }

        if (_lastTargets is not null && TargetsEqual(_lastTargets, targets)) return;

        // Instrumentation added 2026-09-10 after the reactive flash felt
        // glitchier/slower than Python live: measured 83-102ms per call
        // (over the 80ms tick budget, so the loop could never keep up)
        // before caching the WMI ManagementObject instance + per-method
        // parameter templates in Lightbar.cs -- now consistently 60-69ms.
        // Left in place (threshold raised to match the new normal) as an
        // early warning if a future change regresses this back toward
        // (or past) the tick interval.
        var sw = Stopwatch.StartNew();
        _lightbar.FlashZones(targets);
        sw.Stop();
        if (sw.ElapsedMilliseconds > 75)
        {
            _logger.LogWarning("Lightbar reactive FlashZones took {Ms}ms (tick interval is {Interval}ms)",
                sw.ElapsedMilliseconds, (int)(ReactiveTickSeconds * 1000));
        }
        _lastTargets = targets;
    }

    // Same logic as effects/gradient.py's _zone_index()/daemon/server.py's
    // _zone_for_column(): 0-based zone for a column position given sorted
    // boundary thresholds.
    private static int ZoneForColumn(double col, IReadOnlyList<double> boundaries)
    {
        int zone = 0;
        foreach (double boundary in boundaries.OrderBy(b => b))
        {
            if (col >= boundary) zone++;
            else break;
        }
        return zone;
    }

    private static bool TargetsEqual(IReadOnlyDictionary<int, RgbColor> a, IReadOnlyDictionary<int, RgbColor> b)
    {
        if (a.Count != b.Count) return false;
        foreach ((int key, RgbColor value) in a)
        {
            if (!b.TryGetValue(key, out RgbColor other) || !value.Equals(other)) return false;
        }
        return true;
    }
}
