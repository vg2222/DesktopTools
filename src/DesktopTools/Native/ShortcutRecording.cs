using System.Windows.Input;

namespace DesktopTools.Native;

public static class ShortcutRecording
{
    public static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    public static bool TryFormat(Key key, ModifierKeys modifiers, out string text, out string? error, bool allowUnmodified = false)
    {
        text = ""; error = null;
        if (IsModifier(key)) return false;
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        int vk = KeyInterop.VirtualKeyFromKey(key);
        string name = vk switch
        {
            >= 65 and <= 90 or >= 48 and <= 57 => ((char)vk).ToString(),
            >= 112 and <= 135 => "F" + (vk - 111),
            32 => "Space", 9 => "Tab", 13 => "Enter", 27 => "Esc", 8 => "Backspace", 46 => "Delete", 45 => "Insert",
            36 => "Home", 35 => "End", 33 => "PageUp", 34 => "PageDown", 37 => "Left", 38 => "Up", 39 => "Right", 40 => "Down",
            _ => key.ToString()
        };
        parts.Add(name); text = string.Join("+", parts);
        return allowUnmodified ? Core.DrawingBindings.TryParse(text, out _, out _, out error) : HotkeyGesture.TryParse(text, out _, out error);
    }
}
