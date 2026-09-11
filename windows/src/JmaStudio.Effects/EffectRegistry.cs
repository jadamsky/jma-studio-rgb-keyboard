// C# analogue of daemon/server.py's _load_effects(): builds the full
// name -> IEffect map. Unlike Python's reflection-based module scan
// (any effects/*.py with NAME + render() becomes pluggable), this is a
// fixed, explicit list -- see windows/HANDOFF.md's settled decision #3
// for why. Effect names match each ported Python module's NAME
// constant exactly, since that string is also what presets.json uses
// to select an effect (needed for the Phase 4 preset migration to work).
//
// Position-aware effects (color_wipe, comet, scanner, aurora, ripple,
// rain, gradient, gaming_zone, typing_reactive) load keymap.json ONCE
// here, mirroring the Python modules' own "load once at import time"
// behavior -- re-create an EffectRegistry (not just re-render) after
// keymap.json changes, the same operational characteristic as
// restarting the Python daemon.

using JmaStudio.Effects.Effects;

namespace JmaStudio.Effects;

public sealed class EffectRegistry
{
    private readonly Dictionary<string, IEffect> _byName;

    public EffectRegistry(string keymapPath)
    {
        var positions = Layout.CellPositions(keymapPath);
        var indexByName = Layout.NameToIndex(keymapPath);

        IEffect[] effects =
        {
            new StaticEffect(),
            new MaskEffect(),
            new ProbeEffect(),
            new RainbowEffect(),
            new PukeEffect(),
            new SpectrumCycleEffect(),
            new BreathingEffect(),
            new PulseEffect(),
            new CustomKeysEffect(),
            new GamingZoneEffect(indexByName),
            new StarlightEffect(),
            new ConfettiEffect(),
            new FireEffect(),
            new ColorWipeEffect(positions),
            new CometEffect(positions),
            new ScannerEffect(positions),
            new AuroraEffect(positions),
            new RippleEffect(positions),
            new RainEffect(positions),
            new GradientEffect(positions, indexByName),
            new TypingReactiveEffect(positions),
            new ControllerReactiveEffect(indexByName),
        };

        _byName = effects.ToDictionary(e => e.Name);
    }

    public IEffect? TryGet(string name) => _byName.GetValueOrDefault(name);

    public IReadOnlyDictionary<string, IEffect> All => _byName;
}
