using System.Windows.Automation;

namespace DesktopTools.Native;

internal static class SelectedTextService
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    internal static async Task<string?> ReadAsync(nint foreground)
    {
        if (foreground == 0 || !await gate.WaitAsync(0)) return null;
        var work = Task.Run(() =>
        {
            try
            {
                if (NativeWindowService.GetForegroundWindowHandle() != foreground) return null;
                var focused = AutomationElement.FocusedElement;
                return ReadElementSelection(focused, foreground);
            }
            catch { return null; } // Unsupported/stale/elevated providers fall back to explicit paste.
            finally { gate.Release(); }
        });
        try { return await work.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (TimeoutException) { return null; } // The gate stays held until the provider actually returns.
    }
    internal static string? ReadElementSelection(AutomationElement? focused, nint foreground)
    {
        if (focused == null || focused.Current.IsPassword) return null;
        var owner = focused;
        while (owner != null && owner.Current.NativeWindowHandle != foreground) owner = TreeWalker.ControlViewWalker.GetParent(owner);
        if (owner == null || !focused.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;
        var ranges = ((TextPattern)pattern).GetSelection();
        string text = string.Join("\n", ranges.Take(8).Select(r => r.GetText(4001)));
        return text.Length <= 4000 ? text : null;
    }
}
