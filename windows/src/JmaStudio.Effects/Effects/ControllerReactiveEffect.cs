// Port of effects/controller_reactive.py. Visualizes the DualSense's
// two sticks as a "ripple outward from a home key" (S for the left
// stick, L for the right), plus a fixed set of individual buttons/
// triggers/D-pad/paddles/Fn buttons that each light their own group of
// keys. See the Python module's docstring on `main` for the full
// magnitude-band design rationale (idle/tier1/tier2, no per-direction
// color).
//
// Unlike every other ported effect, this one is meant to be a full
// keyboard override when enabled -- that enable/disable "take over
// from whatever's running, restore it on disable" semantics live in
// the Windows Service (Phase 5), not here; this class only renders
// given a ControllerState, the same way TypingReactiveEffect only
// renders given a KeyState.

using JmaStudio.Hardware;

namespace JmaStudio.Effects.Effects;

public sealed class ControllerReactiveEffect : Effect<ControllerReactiveParams>
{
    private const double Tier2Threshold = 0.5;
    // L2/R2 are analog but deliberately treated as all-or-nothing --
    // only a full pull lights anything, no progressive tiers like the
    // sticks have. Some tolerance for calibration noise.
    private const double TriggerFullThreshold = 0.95;
    private static readonly RgbColor DefaultGroupColor = new(0, 100, 0);

    private const string LeftCenterKey = "s";
    private const string RightCenterKey = "l";

    private static readonly IReadOnlyDictionary<string, (string[] Tier1, string[] Tier2)> LeftZones =
        new Dictionary<string, (string[], string[])>
        {
            ["up"] = (new[] { "w", "e" }, new[] { "2", "3", "4" }),
            ["down"] = (new[] { "z", "x" }, new[] { "windows", "left_alt" }),
            ["left"] = (new[] { "a" }, new[] { "caps_lock" }),
            ["right"] = (new[] { "d" }, new[] { "f" }),
        };

    private static readonly IReadOnlyDictionary<string, (string[] Tier1, string[] Tier2)> RightZones =
        new Dictionary<string, (string[], string[])>
        {
            ["up"] = (new[] { "o", "p" }, new[] { "9", "0", "minus" }),
            ["down"] = (new[] { "comma", "period" }, new[] { "alt_gr", "context_menu" }),
            ["left"] = (new[] { "k" }, new[] { "j" }),
            ["right"] = (new[] { "semicolon" }, new[] { "quote" }),
        };

    // Every independently-colorable button group and the keys it lights.
    private static readonly IReadOnlyDictionary<string, string[]> ButtonGroups = new Dictionary<string, string[]>
    {
        ["l1"] = new[] { "f1", "f2", "f3", "f4" },
        ["r1"] = new[] { "prtsc", "ins", "del" },
        ["l2"] = new[] { "f5", "f6", "f7", "f8" },
        ["r2"] = new[] { "f9", "f10", "f11", "f12" },
        ["cross"] = new[] { "num_2" },
        ["square"] = new[] { "num_4" },
        ["circle"] = new[] { "num_6" },
        ["triangle"] = new[] { "num_8" },
        ["dpad_up"] = new[] { "y" },
        ["dpad_left"] = new[] { "g" },
        ["dpad_right"] = new[] { "h" },
        ["dpad_down"] = new[] { "b" },
        ["left_paddle"] = new[] { "left_shift" },
        ["left_fn"] = new[] { "left_ctrl" },
        ["right_paddle"] = new[] { "right_shift" },
        ["right_fn"] = new[] { "right_ctrl" },
        // Stick clicks -- added at the user's explicit request (2026-09-12),
        // previously read from the controller but never wired to anything.
        ["l3"] = new[] { "5", "6" },
        ["r3"] = new[] { "7", "8" },
    };

    private readonly IReadOnlyDictionary<string, int> _indexByName;

    public ControllerReactiveEffect(IReadOnlyDictionary<string, int> indexByName)
    {
        _indexByName = indexByName;
    }

    public override string Name => "controller_reactive";

    private static bool ButtonActive(string name, ControllerState s) => name switch
    {
        "l2" => s.LeftTrigger >= TriggerFullThreshold,
        "r2" => s.RightTrigger >= TriggerFullThreshold,
        "l1" => s.L1,
        "r1" => s.R1,
        "cross" => s.Cross,
        "square" => s.Square,
        "circle" => s.Circle,
        "triangle" => s.Triangle,
        "dpad_up" => s.DpadUp,
        "dpad_down" => s.DpadDown,
        "dpad_left" => s.DpadLeft,
        "dpad_right" => s.DpadRight,
        "left_paddle" => s.LeftPaddle,
        "right_paddle" => s.RightPaddle,
        "left_fn" => s.LeftFn,
        "right_fn" => s.RightFn,
        "l3" => s.L3,
        "r3" => s.R3,
        _ => false,
    };

    private void LightStickZones(
        RgbColor[] colors, StickColors stickColors, double deadzone, string centerKey,
        IReadOnlyDictionary<string, (string[] Tier1, string[] Tier2)> zones, double x, double y)
    {
        if (_indexByName.TryGetValue(centerKey, out int centerIdx))
        {
            colors[centerIdx] = stickColors.Idle;
        }

        var active = new List<(string Direction, double Magnitude)>();
        if (-y > deadzone) active.Add(("up", -y));
        if (y > deadzone) active.Add(("down", y));
        if (-x > deadzone) active.Add(("left", -x));
        if (x > deadzone) active.Add(("right", x));

        foreach ((string direction, double magnitude) in active)
        {
            (string[] tier1, string[] tier2) = zones[direction];
            bool inTier2 = magnitude >= Tier2Threshold;
            RgbColor color = inTier2 ? stickColors.Tier2 : stickColors.Tier1;
            IEnumerable<string> keys = inTier2 ? tier1.Concat(tier2) : tier1;
            foreach (string name in keys)
            {
                if (_indexByName.TryGetValue(name, out int idx)) colors[idx] = color;
            }
        }
    }

    protected override RgbColor[] RenderTyped(double t, int numCells, ControllerReactiveParams p, EffectContext context)
    {
        RgbColor bg = p.BackgroundEnabled ? p.BackgroundColor : new RgbColor(0, 0, 0);
        var colors = Enumerable.Repeat(bg, numCells).ToArray();
        ControllerState state = context.ControllerState ?? ControllerState.Idle;

        LightStickZones(colors, p.LeftStick, p.Deadzone, LeftCenterKey, LeftZones, state.LeftX, state.LeftY);
        LightStickZones(colors, p.RightStick, p.Deadzone, RightCenterKey, RightZones, state.RightX, state.RightY);

        foreach ((string name, string[] keys) in ButtonGroups)
        {
            if (!ButtonActive(name, state)) continue;
            RgbColor color = p.ButtonColors.GetValueOrDefault(name, DefaultGroupColor);
            foreach (string key in keys)
            {
                if (_indexByName.TryGetValue(key, out int idx)) colors[idx] = color;
            }
        }
        return colors;
    }
}
