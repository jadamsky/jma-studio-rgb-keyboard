// C# analogue of Python's daemon/server.py module-level globals
// (_current_effect, _current_params, _last_frame, frame counters) --
// but with an explicit lock (settled decision: no GIL to lean on) and
// disk persistence on every change (settled decision #9: boot-time
// state via disk persistence, not an empty in-memory default).

using JmaStudio.Effects;
using JmaStudio.Hardware;
using JmaStudio.Presets;

namespace JmaStudio.Service;

public sealed class DaemonState
{
    private readonly object _lock = new();
    private readonly JsonStore<KeyboardPreset?> _liveStateStore;

    private string _effectName;
    private EffectParams _params;
    private RgbColor[] _lastFrame = Array.Empty<RgbColor>();
    private RgbColor[]? _lastSentFrame;
    private long _framesRendered;
    private long _framesWritten;

    /// <summary>`fallbackEffectName`/`fallbackParams` are used only the
    /// very first time this ever runs (no live state persisted yet) --
    /// the caller should resolve these from AppConfig.DefaultPreset when
    /// possible, so a fresh install boots into the user's actual
    /// migrated default preset rather than a bare black screen.</summary>
    public DaemonState(JsonStore<KeyboardPreset?> liveStateStore, string fallbackEffectName, EffectParams fallbackParams)
    {
        _liveStateStore = liveStateStore;
        KeyboardPreset? saved = liveStateStore.Load();
        _effectName = saved?.Effect ?? fallbackEffectName;
        _params = saved?.Params ?? fallbackParams;
    }

    public void SetEffect(string effectName, EffectParams parameters)
    {
        lock (_lock)
        {
            _effectName = effectName;
            _params = parameters;
        }
        _liveStateStore.Save(new KeyboardPreset { Effect = effectName, Params = parameters });
    }

    public (string EffectName, EffectParams Params) GetEffect()
    {
        lock (_lock) return (_effectName, _params);
    }

    public RgbColor[] LastFrame
    {
        get { lock (_lock) return _lastFrame; }
    }

    public (long Rendered, long Written) Stats
    {
        get { lock (_lock) return (_framesRendered, _framesWritten); }
    }

    /// <summary>Records a rendered frame; returns true if it differs from
    /// the last one actually WRITTEN (the caller should push it to
    /// hardware), matching the Python render loop's "skip the USB write
    /// when the frame is visually identical" optimization.</summary>
    public bool RecordFrame(RgbColor[] colors)
    {
        lock (_lock)
        {
            _lastFrame = colors;
            _framesRendered++;
            bool changed = _lastSentFrame is null || !colors.AsSpan().SequenceEqual(_lastSentFrame);
            if (changed)
            {
                _lastSentFrame = colors;
                _framesWritten++;
            }
            return changed;
        }
    }
}
