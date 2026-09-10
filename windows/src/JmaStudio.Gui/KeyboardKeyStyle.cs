// Per-key sizing/labeling data, ported verbatim from gui/app.js's
// KEY_WIDTH/KEY_HEIGHT/FRIENDLY_LABEL tables (the Python GUI's keyboard
// preview reference) -- MainWindow's canvas was drawing every cell as a
// uniform 20x20 square with no stagger/wide-key handling or labels,
// which the user flagged as looking noticeably worse than the Python
// GUI. Grid units here are the same "1u = one standard key" convention
// effects/layout.py's KEY_POSITIONS table already uses.

namespace JmaStudio.Gui;

public static class KeyboardKeyStyle
{
    public static readonly Dictionary<string, double> KeyWidth = new()
    {
        ["tab"] = 1.5, ["caps_lock"] = 1.75, ["left_shift"] = 2.25, ["right_shift"] = 1.3,
        ["backspace"] = 1.5, ["enter"] = 1.75, ["left_ctrl"] = 1.25, ["fn"] = 1, ["windows"] = 1, ["left_alt"] = 1,
        ["space"] = 5, ["alt_gr"] = 1, ["context_menu"] = 1, ["right_ctrl"] = 1.3, ["num_0"] = 1, ["num_plus"] = 1, ["num_enter"] = 1,
        // Function row keys are all slightly narrower than 1u so Del's
        // right edge lines up with Backspace's right edge one row down.
        ["esc"] = 0.85, ["f1"] = 0.85, ["f2"] = 0.85, ["f3"] = 0.85, ["f4"] = 0.85, ["f5"] = 0.85, ["f6"] = 0.85,
        ["f7"] = 0.85, ["f8"] = 0.85, ["f9"] = 0.85, ["f10"] = 0.85, ["f11"] = 0.85, ["f12"] = 0.85,
        ["prtsc"] = 0.85, ["ins"] = 0.85, ["del"] = 0.85,
    };

    // Numpad Enter is a real tall key spanning two grid rows, not a wide one.
    public static readonly Dictionary<string, double> KeyHeight = new()
    {
        ["num_enter"] = 2,
    };

    private static readonly Dictionary<string, string> FriendlyLabelOverrides = new()
    {
        ["left_ctrl"] = "Ctrl", ["right_ctrl"] = "Ctrl",
        ["left_shift"] = "Shift", ["right_shift"] = "Shift",
        ["left_alt"] = "Alt", ["alt_gr"] = "AltGr",
        ["caps_lock"] = "Caps", ["context_menu"] = "Menu",
        ["backspace"] = "⌫", ["enter"] = "⏎", ["num_enter"] = "⏎", ["tab"] = "Tab",
        ["space"] = "", ["windows"] = "⊞", ["fn"] = "Fn",
        ["up_arrow"] = "↑", ["down_arrow"] = "↓", ["left_arrow"] = "←", ["right_arrow"] = "→",
        ["prtsc"] = "PrSc", ["numlk"] = "Num", ["ins"] = "Ins", ["del"] = "Del",
        ["backtick"] = "`", ["backslash"] = "\\", ["left_bracket"] = "[", ["right_bracket"] = "]",
        ["semicolon"] = ";", ["quote"] = "'", ["comma"] = ",", ["period"] = ".", ["slash"] = "/",
        ["minus"] = "-", ["equals"] = "=", ["esc"] = "Esc",
        ["predator_key"] = "\U0001F43E", ["power_button"] = "⏻",
        ["play_pause"] = "⏯", ["fast_forward"] = "⏭", ["rewind"] = "⏮",
        ["num_decimal"] = ".", ["num_divide"] = "/", ["num_multiply"] = "*", ["num_plus"] = "+", ["num_minus"] = "-",
    };

    public static string FriendlyLabel(string name)
    {
        if (FriendlyLabelOverrides.TryGetValue(name, out string? label)) return label;
        if (name.StartsWith("num_")) return name[4..];
        return name.Length <= 3 ? name.ToUpperInvariant() : name;
    }
}
