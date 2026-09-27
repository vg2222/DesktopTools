using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using DesktopTools.Native;

internal static class HotkeyMessageChecks
{
    internal static Task RunAsync()
    {
        using var hotkeys = new HotkeyService();
        var unavailable = hotkeys.RegisterAvailable(new Dictionary<string, string> { ["PinWindow"] = "Ctrl+Alt+Shift+F24" });
        if (unavailable.Count != 0) throw new InvalidOperationException("Isolated pin test shortcut was unavailable.");
        var registrations = (Dictionary<HotkeyGesture, int>)typeof(HotkeyService)
            .GetField("registrations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(hotkeys)!;
        HotkeyGesture.TryParse("Ctrl+Alt+Shift+F24", out var gesture, out _);
        int id = registrations[gesture];
        int pressed = 0;
        hotkeys.Pressed += action => { if (action == "PinWindow") pressed++; };
        var callback = typeof(HotkeyService).GetMethod("WindowProc", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // The ID can be recycled after settings change while an old WM_HOTKEY remains queued.
        HotkeyGesture.TryParse("Ctrl+Alt+Shift+F23", out var oldGesture, out _);
        Dispatch(oldGesture);
        if (pressed != 0) throw new InvalidOperationException("An obsolete hotkey message triggered PinWindow.");
        Dispatch(gesture);
        if (pressed != 1) throw new InvalidOperationException("The registered pin shortcut was ignored.");
        return Task.CompletedTask;

        void Dispatch(HotkeyGesture sent)
        {
            nint parameter = (nint)(((long)sent.VirtualKey << 16) | sent.Modifiers);
            object?[] args = [nint.Zero, 0x0312, (nint)id, parameter, false];
            callback.Invoke(hotkeys, args);
        }
    }
}
