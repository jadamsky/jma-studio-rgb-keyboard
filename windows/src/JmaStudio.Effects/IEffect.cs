using JmaStudio.Hardware;

namespace JmaStudio.Effects;

/// <summary>Live, per-frame state injected by the daemon's render loop --
/// the C# analogue of Python's params["key_state"] injection in
/// daemon/server.py's _render_loop(). Cell index -> seconds-since-press
/// for each still-relevant recent press (a list per cell since mashing
/// a key fires one independent animation per press, not one that resets).
/// Effects that don't care simply ignore it.</summary>
public sealed class EffectContext
{
    public IReadOnlyDictionary<int, IReadOnlyList<double>> KeyState { get; init; } =
        new Dictionary<int, IReadOnlyList<double>>();

    /// <summary>Needed only by effects that delegate to another effect by
    /// name (currently just TypingReactive's base_effect) -- the Python
    /// side does this with importlib.import_module + a name lookup;
    /// here it's an explicit registry reference instead, since C# has no
    /// equivalent of "any module with the right two names is pluggable"
    /// (see windows/HANDOFF.md's settled decision on strongly-typed
    /// effects, not reflection-based discovery).</summary>
    public EffectRegistry? Registry { get; init; }

    public static readonly EffectContext Empty = new();
}

/// <summary>Common, non-generic interface so effects can be stored and
/// dispatched by name in one registry (EffectRegistry) despite each
/// expecting its own strongly-typed params. Implement via the Effect&lt;
/// TParams&gt; base class below rather than directly, in real effect
/// classes -- it centralizes the one unavoidable cast to the concrete
/// params type in a single place instead of repeating it everywhere.</summary>
public interface IEffect
{
    /// <summary>Matches the Python module's NAME constant exactly -- this
    /// is also the string presets.json uses to select an effect, so it
    /// must stay stable for the Phase 4 preset migration to work.</summary>
    string Name { get; }

    /// <summary>This effect's own default params instance (every property
    /// at its Python-side DEFAULT_* value) -- lets a caller that only
    /// holds the non-generic IEffect (e.g. TypingReactiveEffect
    /// delegating to a base_effect it doesn't know the concrete type of)
    /// get sensible defaults without reflection.</summary>
    EffectParams DefaultParams { get; }

    RgbColor[] Render(double t, int numCells, EffectParams parameters, EffectContext context);
}

public abstract class Effect<TParams> : IEffect where TParams : EffectParams, new()
{
    public abstract string Name { get; }

    /// <summary>Effects are commonly invoked with no explicit params
    /// (e.g. a fresh preset, or TypingReactive's base_params defaulting
    /// to {}) -- mirrors Python's params.get(key, DEFAULT) pattern where
    /// an absent dict just means "use every default."</summary>
    public static TParams TypedDefaultParams { get; } = new();

    EffectParams IEffect.DefaultParams => TypedDefaultParams;

    public RgbColor[] Render(double t, int numCells, EffectParams parameters, EffectContext context)
    {
        TParams typed = parameters as TParams ?? TypedDefaultParams;
        return RenderTyped(t, numCells, typed, context);
    }

    protected abstract RgbColor[] RenderTyped(double t, int numCells, TParams parameters, EffectContext context);
}
