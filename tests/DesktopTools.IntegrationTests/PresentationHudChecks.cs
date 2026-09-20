using System;
using DesktopTools.Extras;

internal static class PresentationHudChecks
{
    public static void RunFormatter()
    {
        // Dropping the shortcut modifier guard must never reveal ordinary text.
        for (uint key = 0x30; key <= 0x5A; key++)
        {
            Check(PresentationShortcutFormatter.Format(key, false, false, false, false) == null, "Ordinary text appeared in the shortcut HUD.");
            Check(PresentationShortcutFormatter.Format(key, false, false, true, false) == null, "Shift-typed text appeared in the shortcut HUD.");
        }
        Check(PresentationShortcutFormatter.Format(0x43, true, false, false, false) == "Ctrl + C", "Ctrl+C shortcut was not displayed.");
        Check(PresentationShortcutFormatter.Format(0x53, true, false, true, false) == "Ctrl + Shift + S", "Shift shortcut modifiers were lost.");
        Check(PresentationShortcutFormatter.Format(0x09, false, true, false, false) == "Alt + Tab", "Alt+Tab was not displayed.");
        Check(PresentationShortcutFormatter.Format(0x31, false, false, false, true) == "Win + 1", "Windows shortcut was not displayed.");
        Check(PresentationShortcutFormatter.Format(0x70, false, false, false, false) == "F1", "Function key was lost.");
        Check(PresentationShortcutFormatter.Format(0x87, false, false, false, false) == "F24", "Last function key was lost.");
        Check(PresentationShortcutFormatter.Format(0x25, false, false, false, false) == "Left", "Navigation key was lost.");
        Check(PresentationShortcutFormatter.Format(0x1B, false, false, false, false) == "Esc", "Escape key was lost.");
        Check(PresentationShortcutFormatter.Format(0x51, true, true, false, false, true) == null, "AltGr character input appeared in the shortcut HUD.");
        foreach (uint key in new uint[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0xE7, 0xBA, 0xFFFF })
            Check(PresentationShortcutFormatter.Format(key, true, false, false, false) == null, "Modifier, IME packet or unknown text key was exposed.");
    }

    private static void Check(bool condition, string error) { if (!condition) throw new Exception(error); }
}
