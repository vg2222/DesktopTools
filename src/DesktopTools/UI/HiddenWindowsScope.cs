using System.Windows;

namespace DesktopTools.UI;

/// <summary>Temporarily hide visible controls, preserving their lifetime and activation preferences.</summary>
internal sealed class HiddenWindowsScope : IDisposable
{
    private readonly Dictionary<Window, bool> windows;
    private readonly HashSet<Window> closed = [];
    public HiddenWindowsScope()
    {
        windows = Application.Current.Windows.Cast<Window>().Where(w => w.IsVisible).ToDictionary(w => w, w => w.ShowActivated);
        using (DesktopTools.Presentation.WindowDismissal.Suppress())
        {
            foreach (var w in windows.Keys) { w.Closed += OnClosed; DesktopTools.Presentation.WindowDismissal.Hide(w, () => true); }
        }
    }
    private void OnClosed(object? sender, EventArgs e) { if (sender is Window window) closed.Add(window); }
    public void Dispose()
    {
        foreach (var (w, showActivated) in windows)
        {
            w.Closed -= OnClosed;
            if (closed.Contains(w) || Application.Current.Dispatcher.HasShutdownStarted) continue;
            w.ShowActivated = false; w.Show(); w.ShowActivated = showActivated;
        }
    }
}
