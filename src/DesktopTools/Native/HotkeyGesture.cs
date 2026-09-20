using DesktopTools.Localization;
using System.Globalization;

namespace DesktopTools.Native;

public readonly record struct HotkeyGesture(uint Modifiers, uint VirtualKey)
{
    public static bool TryParse(string? text, out HotkeyGesture gesture, out string? error)
    {
        gesture = default;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) { error = L.T("Enter a shortcut such as Ctrl+Alt+D."); return false; }
        uint modifiers = 0;
        uint key = 0;
        foreach (string raw in text.Split('+'))
        {
            string part = raw.Trim().ToUpperInvariant();
            uint modifier = part switch { "CTRL" or "CONTROL" => 2, "ALT" => 1, "SHIFT" => 4, "WIN" or "WINDOWS" => 8, _ => 0 };
            if (modifier != 0)
            {
                if ((modifiers & modifier) != 0) { error = L.T("A modifier is listed twice."); return false; }
                modifiers |= modifier;
                continue;
            }
            if (key != 0) { error = L.T("Use one key with Ctrl, Alt, Shift or Win modifiers."); return false; }
            if (part.Length == 1 && ((part[0] >= 'A' && part[0] <= 'Z') || (part[0] >= '0' && part[0] <= '9'))) key = part[0];
            else if (part.StartsWith('F') && int.TryParse(part.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int function) && function >= 1 && function <= 24) key = (uint)(111 + function);
            else key = part switch { "SPACE" => 32, "TAB" => 9, "ENTER" or "RETURN" => 13, "ESC" or "ESCAPE" => 27, "BACKSPACE" => 8, "DELETE" or "DEL" => 46, "INSERT" or "INS" => 45, "HOME" => 36, "END" => 35, "PAGEUP" => 33, "PAGEDOWN" => 34, "LEFT" => 37, "UP" => 38, "RIGHT" => 39, "DOWN" => 40, _ => 0 };
            if (key == 0) { error = L.F($"Unknown shortcut key: {raw.Trim()}."); return false; }
        }
        if (key == 0 || modifiers == 0) { error = L.T("Use a key with at least one modifier (Ctrl, Alt, Shift or Win)."); return false; }
        if (key == 123) { error = L.T("F12 is reserved for the Windows debugger."); return false; }
        gesture = new(modifiers, key);
        return true;
    }
}
