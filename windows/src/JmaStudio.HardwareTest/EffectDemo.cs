// Phase 3 visual-verification helper: renders one effect continuously
// for a given duration and pushes each frame to the real keyboard, so
// a human can watch it and confirm it looks right -- the same spirit
// as Phase 2's hardware proof, just for the effects layer instead of
// the raw protocol.

using System.Diagnostics;
using JmaStudio.Effects;
using JmaStudio.Hardware;

namespace JmaStudio.HardwareTest;

public static class EffectDemo
{
    private const double Fps = 30.0;

    /// <summary>Runs `effect` for `seconds`, sending frames at ~30fps.
    /// If `syntheticKeyIndices` is given, simulates a "keypress" that
    /// rotates through those cells one at a time (re-firing at the next
    /// cell in the list every `syntheticPressIntervalSeconds`) so
    /// key_state-driven effects (currently just typing_reactive) have
    /// something more typing-like to react to than one fixed spot --
    /// there's no real input listener in this console app.</summary>
    public static void Run(
        Keyboard keyboard,
        IEffect effect,
        EffectParams parameters,
        double seconds,
        EffectRegistry registry,
        int? syntheticKeyIndex = null,
        double syntheticPressIntervalSeconds = 1.2)
    {
        Run(keyboard, effect, parameters, seconds, registry,
            syntheticKeyIndex is int idx ? new[] { idx } : null,
            syntheticPressIntervalSeconds);
    }

    public static void Run(
        Keyboard keyboard,
        IEffect effect,
        EffectParams parameters,
        double seconds,
        EffectRegistry registry,
        IReadOnlyList<int>? syntheticKeyIndices,
        double syntheticPressIntervalSeconds = 1.2)
    {
        var stopwatch = Stopwatch.StartNew();
        double lastPressAt = double.NegativeInfinity;
        int rotationIndex = 0;
        int currentKey = syntheticKeyIndices is { Count: > 0 } ? syntheticKeyIndices[0] : -1;
        TimeSpan frameInterval = TimeSpan.FromSeconds(1.0 / Fps);

        while (stopwatch.Elapsed.TotalSeconds < seconds)
        {
            double t = stopwatch.Elapsed.TotalSeconds;

            IReadOnlyDictionary<int, IReadOnlyList<double>> keyState =
                new Dictionary<int, IReadOnlyList<double>>();
            if (syntheticKeyIndices is { Count: > 0 } keys)
            {
                if (t - lastPressAt >= syntheticPressIntervalSeconds)
                {
                    lastPressAt = t;
                    currentKey = keys[rotationIndex % keys.Count];
                    rotationIndex++;
                }
                keyState = new Dictionary<int, IReadOnlyList<double>>
                {
                    [currentKey] = new[] { t - lastPressAt },
                };
            }

            var context = new EffectContext { KeyState = keyState, Registry = registry };
            RgbColor[] colors = effect.Render(t, KeyboardConstants.NumCells, parameters, context);
            keyboard.SendFrame(colors);

            double workSeconds = stopwatch.Elapsed.TotalSeconds - t;
            TimeSpan sleep = frameInterval - TimeSpan.FromSeconds(workSeconds);
            if (sleep > TimeSpan.Zero) Thread.Sleep(sleep);
        }
    }
}
