// Port of effects/gaming_zone.py.

using JmaStudio.Hardware;

namespace JmaStudio.Effects.Effects;

/// <summary>Highlights the WASD + arrow-key movement cluster; everything
/// else dims down. Looks up cell indices by key name, so needs the
/// {name: index} map loaded once from keymap.json (same "restart/
/// recreate after editing keymap.json" characteristic the Python module
/// has, now via EffectRegistry re-construction instead of a daemon
/// restart).</summary>
public sealed class GamingZoneEffect : Effect<GamingZoneParams>
{
    private readonly IReadOnlyDictionary<string, int> _indexByName;

    public GamingZoneEffect(IReadOnlyDictionary<string, int> indexByName)
    {
        _indexByName = indexByName;
    }

    public override string Name => "gaming_zone";

    protected override RgbColor[] RenderTyped(double t, int numCells, GamingZoneParams p, EffectContext context)
    {
        var colors = Enumerable.Repeat(p.DimColor, numCells).ToArray();
        foreach (string name in p.Keys)
        {
            if (_indexByName.TryGetValue(name, out int idx) && idx >= 0 && idx < numCells)
            {
                colors[idx] = p.BrightColor;
            }
        }
        return colors;
    }
}
