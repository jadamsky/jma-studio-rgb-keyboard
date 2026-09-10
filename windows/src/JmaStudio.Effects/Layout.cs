// Port of effects/layout.py (main branch). The KEY_POSITIONS table
// below is hand-authored, physically-measured data (built from an
// actual photo of this specific PH16-71) -- transcribed verbatim from
// the Python source, not re-derived. Do not "clean up" or re-round any
// of these values without re-checking against effects/layout.py first.
//
// Row increases downward (0 = function row). Column increases
// left-to-right using standard fractional keyboard-unit stagger.

using System.Text.Json;

namespace JmaStudio.Effects;

public readonly record struct GridPosition(double Row, double Col);

public static class Layout
{
    public static readonly IReadOnlyDictionary<string, GridPosition> KeyPositions = new Dictionary<string, GridPosition>
    {
        // Row 0 -- function row.
        ["esc"] = new(0, 0),
        ["f1"] = new(0, 1.075), ["f2"] = new(0, 1.925), ["f3"] = new(0, 2.775), ["f4"] = new(0, 3.625),
        ["f5"] = new(0, 4.7), ["f6"] = new(0, 5.55), ["f7"] = new(0, 6.4), ["f8"] = new(0, 7.25),
        ["f9"] = new(0, 8.325), ["f10"] = new(0, 9.175), ["f11"] = new(0, 10.025), ["f12"] = new(0, 10.875),
        ["prtsc"] = new(0, 11.95), ["ins"] = new(0, 12.8), ["del"] = new(0, 13.65),
        ["rewind"] = new(0, 14.8), ["play_pause"] = new(0, 15.8), ["fast_forward"] = new(0, 16.8),
        ["power_button"] = new(0, 17.8),

        // Row 1 -- number row
        ["backtick"] = new(1, 0),
        ["1"] = new(1, 1), ["2"] = new(1, 2), ["3"] = new(1, 3), ["4"] = new(1, 4), ["5"] = new(1, 5),
        ["6"] = new(1, 6), ["7"] = new(1, 7), ["8"] = new(1, 8), ["9"] = new(1, 9), ["0"] = new(1, 10),
        ["minus"] = new(1, 11), ["equals"] = new(1, 12), ["backspace"] = new(1, 13),
        ["predator_key"] = new(1, 14.8), ["numlk"] = new(1, 15.8),
        ["num_divide"] = new(1, 16.8), ["num_multiply"] = new(1, 17.8),

        // Row 2 -- QWERTY row
        ["tab"] = new(2, 0),
        ["q"] = new(2, 1.5), ["w"] = new(2, 2.5), ["e"] = new(2, 3.5), ["r"] = new(2, 4.5),
        ["t"] = new(2, 5.5), ["y"] = new(2, 6.5), ["u"] = new(2, 7.5), ["i"] = new(2, 8.5),
        ["o"] = new(2, 9.5), ["p"] = new(2, 10.5),
        ["left_bracket"] = new(2, 11.5), ["right_bracket"] = new(2, 12.5),
        ["backslash"] = new(2, 13.5),
        ["num_7"] = new(2, 14.8), ["num_8"] = new(2, 15.8), ["num_9"] = new(2, 16.8), ["num_minus"] = new(2, 17.8),

        // Row 3 -- ASDF row
        ["caps_lock"] = new(3, 0),
        ["a"] = new(3, 1.75), ["s"] = new(3, 2.75), ["d"] = new(3, 3.75), ["f"] = new(3, 4.75),
        ["g"] = new(3, 5.75), ["h"] = new(3, 6.75), ["j"] = new(3, 7.75), ["k"] = new(3, 8.75),
        ["l"] = new(3, 9.75),
        ["semicolon"] = new(3, 10.75), ["quote"] = new(3, 11.75), ["enter"] = new(3, 12.75),
        ["num_4"] = new(3, 14.8), ["num_5"] = new(3, 15.8), ["num_6"] = new(3, 16.8), ["num_plus"] = new(3, 17.8),

        // Row 4 -- ZXCV row
        ["left_shift"] = new(4, 0),
        ["z"] = new(4, 2.25), ["x"] = new(4, 3.25), ["c"] = new(4, 4.25), ["v"] = new(4, 5.25),
        ["b"] = new(4, 6.25), ["n"] = new(4, 7.25), ["m"] = new(4, 8.25),
        ["comma"] = new(4, 9.25), ["period"] = new(4, 10.25), ["slash"] = new(4, 11.25),
        ["right_shift"] = new(4, 12.25),
        ["up_arrow"] = new(4, 13.55),
        ["num_1"] = new(4, 14.8), ["num_2"] = new(4, 15.8), ["num_3"] = new(4, 16.8), ["num_enter"] = new(4, 17.8),

        // Row 5 -- bottom row
        ["left_ctrl"] = new(5, 0), ["fn"] = new(5, 1.25), ["windows"] = new(5, 2.25),
        ["left_alt"] = new(5, 3.25), ["space"] = new(5, 4.25),
        ["alt_gr"] = new(5, 9.25), ["context_menu"] = new(5, 10.25), ["right_ctrl"] = new(5, 11.25),
        ["left_arrow"] = new(5, 12.55), ["down_arrow"] = new(5, 13.55), ["right_arrow"] = new(5, 14.8),
        ["num_0"] = new(5, 15.8), ["num_decimal"] = new(5, 16.8),
    };

    private static Dictionary<string, string> LoadKeymap(string keymapPath)
    {
        try
        {
            string json = File.ReadAllText(keymapPath);
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return raw ?? new Dictionary<string, string>();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>Returns {cell index: GridPosition} by combining keymap.json's
    /// {index: name} with KeyPositions above. A name with no known position
    /// is silently skipped, matching the Python side.</summary>
    public static IReadOnlyDictionary<int, GridPosition> CellPositions(string keymapPath)
    {
        var positions = new Dictionary<int, GridPosition>();
        foreach ((string idxStr, string name) in LoadKeymap(keymapPath))
        {
            if (KeyPositions.TryGetValue(name, out GridPosition pos))
            {
                positions[int.Parse(idxStr)] = pos;
            }
        }
        return positions;
    }

    /// <summary>Returns {key name: cell index} -- the inverse of keymap.json's
    /// own {index: name} shape.</summary>
    public static IReadOnlyDictionary<string, int> NameToIndex(string keymapPath)
    {
        var result = new Dictionary<string, int>();
        foreach ((string idxStr, string name) in LoadKeymap(keymapPath))
        {
            result[name] = int.Parse(idxStr);
        }
        return result;
    }
}
