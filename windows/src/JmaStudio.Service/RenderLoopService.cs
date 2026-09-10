// C# analogue of daemon/server.py's _render_loop(): ~30fps, injects
// live KeyState/ControllerState the same way Python's does, skips the
// actual hardware write when the frame didn't change.

using System.Diagnostics;
using JmaStudio.Effects;
using JmaStudio.Hardware;

namespace JmaStudio.Service;

public sealed class RenderLoopService : BackgroundService
{
    private const double Fps = 30.0;
    // Seconds of press history kept for effects to read -- matches
    // daemon/server.py's _KEY_STATE_MAX_AGE.
    private const double KeyStateMaxAge = 5.0;

    private readonly DaemonState _state;
    private readonly EffectRegistry _registry;
    private readonly Keyboard? _keyboard;
    private readonly InputListener? _inputListener;
    private readonly Controller? _controller;
    private readonly ILogger<RenderLoopService> _logger;

    public RenderLoopService(
        DaemonState state, EffectRegistry registry, Keyboard? keyboard,
        InputListener? inputListener, Controller? controller, ILogger<RenderLoopService> logger)
    {
        _state = state;
        _registry = registry;
        _keyboard = keyboard;
        _inputListener = inputListener;
        _controller = controller;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_keyboard is null)
        {
            _logger.LogWarning("No keyboard connected -- render loop staying idle.");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        TimeSpan interval = TimeSpan.FromSeconds(1.0 / Fps);

        while (!stoppingToken.IsCancellationRequested)
        {
            double t = stopwatch.Elapsed.TotalSeconds;
            try
            {
                (string effectName, EffectParams parameters) = _state.GetEffect();
                IEffect? effect = _registry.TryGet(effectName);
                if (effect is not null)
                {
                    var context = new EffectContext
                    {
                        KeyState = _inputListener?.Snapshot(KeyStateMaxAge) ?? new Dictionary<int, IReadOnlyList<double>>(),
                        Registry = _registry,
                        ControllerState = _controller?.GetState(),
                    };
                    RgbColor[] colors = effect.Render(t, KeyboardConstants.NumCells, parameters, context);
                    if (_state.RecordFrame(colors))
                    {
                        _keyboard.SendFrame(colors);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Render loop iteration failed");
            }

            double workSeconds = stopwatch.Elapsed.TotalSeconds - t;
            TimeSpan sleep = interval - TimeSpan.FromSeconds(workSeconds);
            if (sleep > TimeSpan.Zero)
            {
                try { await Task.Delay(sleep, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
