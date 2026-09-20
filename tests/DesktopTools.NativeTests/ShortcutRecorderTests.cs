using System;
using System.Windows.Input;
using DesktopTools.Native;

internal static class ShortcutRecorderTests
{
    public static void Run(Action<bool, string> check)
    {
        check(ShortcutRecording.TryFormat(Key.D7, ModifierKeys.Control | ModifierKeys.Alt, out var digits, out _) && digits == "Ctrl+Alt+7", "record top row digit");
        check(ShortcutRecording.TryFormat(Key.Escape, ModifierKeys.Windows | ModifierKeys.Shift, out var escape, out _) && escape == "Shift+Win+Esc", "record modified Escape");
        check(ShortcutRecording.IsModifier(Key.LeftCtrl) && ShortcutRecording.IsModifier(Key.RWin), "ignore modifier-only input");
        check(!ShortcutRecording.TryFormat(Key.F12, ModifierKeys.Control, out _, out var error) && error != null, "recording validates reserved F12");
        check(!ShortcutRecording.TryFormat(Key.Enter, ModifierKeys.None, out _, out _), "bare Enter is not a binding");
        check(ShortcutRecording.TryFormat(Key.Enter, ModifierKeys.Control, out var enter, out _) && enter == "Ctrl+Enter", "record modified Enter");
        check(ShortcutRecording.TryFormat(Key.P, ModifierKeys.None, out var local, out _, allowUnmodified: true) && local == "P", "record local bare key");
        check(!ShortcutRecording.TryFormat(Key.Escape, ModifierKeys.None, out _, out _, allowUnmodified: true), "preserve local emergency Escape");
    }
}
