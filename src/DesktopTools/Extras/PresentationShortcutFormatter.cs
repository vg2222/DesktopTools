using System.Collections.Generic;

namespace DesktopTools.Extras;

internal static class PresentationShortcutFormatter
{
    // Deliberately uses a small virtual-key allowlist. Never translates keyboard text,
    // reads a focused control, or retains a history of keys.
    public static string? Format(uint virtualKey, bool control, bool alt, bool shift, bool windows, bool altGraph = false)
    {
        if (altGraph) return null;
        bool shortcut = control || alt || windows;
        string? name = virtualKey switch
        {
            0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Enter", 0x1B => "Esc",
            0x20 when shortcut => "Space", 0x21 => "Page Up", 0x22 => "Page Down",
            0x23 => "End", 0x24 => "Home", 0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
            0x2C => "Print Screen", 0x2D => "Insert", 0x2E => "Delete",
            >= 0x70 and <= 0x87 => "F" + (virtualKey - 0x6F),
            >= 0x30 and <= 0x39 when shortcut => ((char)virtualKey).ToString(),
            >= 0x41 and <= 0x5A when shortcut => ((char)virtualKey).ToString(),
            _ => null
        };
        if (name == null) return null;
        var parts = new List<string>(5);
        if (control) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (windows) parts.Add("Win");
        parts.Add(name);
        return string.Join(" + ", parts);
    }
}
