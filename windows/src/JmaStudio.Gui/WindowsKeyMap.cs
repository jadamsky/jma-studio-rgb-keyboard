// Maps a raw Win32 virtual-key code (from a WH_KEYBOARD_LL hook) to the
// same key-name strings used in keymap.json. Third copy of the same
// table already proven in JmaStudio.HardwareTest and JmaStudio.Service
// (see those projects' own WindowsKeyMap.cs for the full history/design
// notes) -- this GUI copy exists because production key capture now
// happens here (JmaStudio.Gui runs in the interactive session) and gets
// forwarded to the Service over POST /keypress, since a real installed
// Windows Service runs in Session 0 and can't see these keystrokes
// directly. Kept verbatim/unchanged from the Service's copy so the two
// never drift apart for no reason.

namespace JmaStudio.Gui;

public static class WindowsKeyMap
{
    private const int VkReturn = 0x0D;
    private const uint LlkhfExtended = 0x01;

    private static readonly Dictionary<int, string> ByVirtualKey = BuildTable();

    public static string? Resolve(int vkCode, uint flags)
    {
        if (vkCode == VkReturn)
        {
            return (flags & LlkhfExtended) != 0 ? "num_enter" : "enter";
        }
        return ByVirtualKey.GetValueOrDefault(vkCode);
    }

    private static Dictionary<int, string> BuildTable()
    {
        var map = new Dictionary<int, string>
        {
            [0x1B] = "esc",
            [0x70] = "f1", [0x71] = "f2", [0x72] = "f3", [0x73] = "f4",
            [0x74] = "f5", [0x75] = "f6", [0x76] = "f7", [0x77] = "f8",
            [0x78] = "f9", [0x79] = "f10", [0x7A] = "f11", [0x7B] = "f12",
            [0x2C] = "prtsc", [0x2D] = "ins", [0x2E] = "del",
            [0xB1] = "rewind", [0xB3] = "play_pause", [0xB0] = "fast_forward",

            [0xC0] = "backtick",
            [0x31] = "1", [0x32] = "2", [0x33] = "3", [0x34] = "4", [0x35] = "5",
            [0x36] = "6", [0x37] = "7", [0x38] = "8", [0x39] = "9", [0x30] = "0",
            [0xBD] = "minus", [0xBB] = "equals", [0x08] = "backspace",
            [0x90] = "numlk",
            [0x6F] = "num_divide", [0x6A] = "num_multiply",

            [0x09] = "tab",
            [0x51] = "q", [0x57] = "w", [0x45] = "e", [0x52] = "r", [0x54] = "t",
            [0x59] = "y", [0x55] = "u", [0x49] = "i", [0x4F] = "o", [0x50] = "p",
            [0xDB] = "left_bracket", [0xDD] = "right_bracket", [0xDC] = "backslash",
            [0x67] = "num_7", [0x68] = "num_8", [0x69] = "num_9", [0x6D] = "num_minus",

            [0x14] = "caps_lock",
            [0x41] = "a", [0x53] = "s", [0x44] = "d", [0x46] = "f", [0x47] = "g",
            [0x48] = "h", [0x4A] = "j", [0x4B] = "k", [0x4C] = "l",
            [0xBA] = "semicolon", [0xDE] = "quote",
            [0x64] = "num_4", [0x65] = "num_5", [0x66] = "num_6", [0x6B] = "num_plus",

            [0xA0] = "left_shift", [0xA1] = "right_shift",
            [0x5A] = "z", [0x58] = "x", [0x43] = "c", [0x56] = "v",
            [0x42] = "b", [0x4E] = "n", [0x4D] = "m",
            [0xBC] = "comma", [0xBE] = "period", [0xBF] = "slash",
            [0x26] = "up_arrow",
            [0x61] = "num_1", [0x62] = "num_2", [0x63] = "num_3",

            [0xA2] = "left_ctrl", [0x5B] = "windows", [0x5C] = "windows",
            [0xA4] = "left_alt", [0x20] = "space",
            [0xA5] = "alt_gr", [0x5D] = "context_menu", [0xA3] = "right_ctrl",
            [0x25] = "left_arrow", [0x28] = "down_arrow", [0x27] = "right_arrow",
            [0x60] = "num_0", [0x6E] = "num_decimal",
        };
        return map;
    }
}
