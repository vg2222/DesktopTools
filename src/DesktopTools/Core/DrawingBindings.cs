using DesktopTools.Localization;
using System.Windows.Input;

namespace DesktopTools.Core;

public static class DrawingBindings
{
    public static Dictionary<string, string> Defaults => new()
    {
        ["Select"] = "V", ["Pen"] = "P", ["Highlighter"] = "H", ["Arrow"] = "A", ["Line"] = "L",
        ["Rectangle"] = "R", ["Ellipse"] = "O", ["Text"] = "T", ["Number"] = "N", ["Eraser"] = "E",
        ["Undo"] = "Ctrl+Z", ["Redo"] = "Ctrl+Y", ["Duplicate"] = "Ctrl+D", ["Delete"] = "Delete",
        ["Interact"] = "Space", ["FinishText"] = "Ctrl+Enter"
    };

    public static bool TryParse(string? text, out Key key, out ModifierKeys modifiers, out string? error)
    {
        key = Key.None; modifiers = ModifierKeys.None; error = null;
        if (string.IsNullOrWhiteSpace(text)) { error = L.T("Press a shortcut key."); return false; }
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim().ToUpperInvariant();
            var mod = part switch { "CTRL" or "CONTROL" => ModifierKeys.Control, "ALT" => ModifierKeys.Alt, "SHIFT" => ModifierKeys.Shift, "WIN" or "WINDOWS" => ModifierKeys.Windows, _ => ModifierKeys.None };
            if (mod != ModifierKeys.None)
            {
                if ((modifiers & mod) != 0) { error = L.T("A modifier is listed twice."); return false; }
                modifiers |= mod; continue;
            }
            if (key != Key.None) { error = L.T("Use one key with optional modifiers."); return false; }
            var name = part switch { "ESC" => "Escape", "RETURN" => "Enter", "DEL" => "Delete", "INS" => "Insert", _ => part };
            if (name.Length == 1 && char.IsAsciiDigit(name[0])) name = "D" + name;
            if (!Enum.TryParse(name, true, out key) || !Enum.IsDefined(key) || key is Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            { error = L.T("This key cannot be used as a shortcut."); return false; }
        }
        if (key == Key.None) { error = L.T("Press a key together with the modifiers."); return false; }
        if (key == Key.Escape && modifiers == ModifierKeys.None) { error = L.T("Escape is reserved for cancel and hide."); return false; }
        return true;
    }

    public static bool Validate(IReadOnlyDictionary<string, string>? bindings, out string? error)
    {
        error = null;
        if (bindings == null || bindings.Count != Defaults.Count || Defaults.Keys.Any(a => !bindings.ContainsKey(a))) { error = L.T("The drawing shortcut list is incomplete."); return false; }
        var used = new HashSet<(Key, ModifierKeys)>();
        foreach (var (action, text) in bindings)
        {
            if (!TryParse(text, out var key, out var modifiers, out error)) { error = L.T(action == "FinishText" ? "Finish text" : action) + ": " + error; return false; }
            if (action == "FinishText" && (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0) { error = L.T("Finish text needs Ctrl, Alt or Win so it does not interrupt typing."); return false; }
            if (!used.Add((key, modifiers))) { error = L.T("Two drawing actions use the same shortcut."); return false; }
        }
        return true;
    }

    public static string? Find(IReadOnlyDictionary<string, string> bindings, Key key, ModifierKeys modifiers) => bindings.FirstOrDefault(p => TryParse(p.Value, out var parsed, out var mods, out _) && parsed == key && mods == modifiers).Key;
}
