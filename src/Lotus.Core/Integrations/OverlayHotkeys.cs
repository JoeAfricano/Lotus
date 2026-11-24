namespace Lotus.Core.Integrations;

// The key that shows and hides the in-game overlay. RivaTuner's HotkeyHandler plugin (and MSI Afterburner) store a key
// as the virtual-key code in the low bits plus modifiers from bit 16: Shift 0x10000, Ctrl 0x20000, Alt 0x40000,
// extended key 0x80000. Lotus offers a short list that games rarely use; "none" gives back what RivaTuner had.
public static class OverlayHotkeys
{
    public const string None = "none", Custom = "custom";
    public static readonly string[] Choices = ["ScrollLock", "Pause", "CtrlF10", "CtrlF11", "CtrlAltF10", "CtrlAltF11", "CtrlShiftO", None];
    private const uint Shift = 0x10000, Ctrl = 0x20000, Alt = 0x40000;

    public static uint? Code(string choice) => choice switch
    {
        "ScrollLock" => 0x91, "Pause" => 0x13,
        "CtrlF10" => Ctrl | 0x79, "CtrlF11" => Ctrl | 0x7A,
        "CtrlAltF10" => Ctrl | Alt | 0x79, "CtrlAltF11" => Ctrl | Alt | 0x7A,
        "CtrlShiftO" => Ctrl | Shift | 0x4F,
        _ => null
    };
    // The choice for a stored code: None for 0, Custom for a key set some other way.
    public static string ChoiceOf(uint code) => code == 0 ? None : Choices.FirstOrDefault(c => Code(c) == code) ?? Custom;

    // "Ctrl + Alt + F10", "Scroll Lock"... null for 0.
    public static string? Describe(uint code)
    {
        if (code == 0) return null;
        var parts = new List<string>();
        if ((code & Ctrl) != 0) parts.Add("Ctrl");
        if ((code & Alt) != 0) parts.Add("Alt");
        if ((code & Shift) != 0) parts.Add("Shift");
        var vk = code & 0xFF;
        parts.Add(vk switch
        {
            0x91 => "Scroll Lock", 0x13 => "Pause", 0x2D => "Insert", 0x2E => "Delete", 0x24 => "Home", 0x23 => "End",
            0x21 => "Page Up", 0x22 => "Page Down", 0x2C => "Print Screen", 0x90 => "Num Lock",
            >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
            >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x60 and <= 0x69 => "Num " + (vk - 0x60),
            _ => "0x" + vk.ToString("X2")
        });
        return string.Join(" + ", parts);
    }
}
