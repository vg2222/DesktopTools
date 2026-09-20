using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DesktopTools.Native;

// DWM deliberately suppresses corner rounding in VMs. Clip the actual HWND, not
// just its WPF content: an opaque composition background otherwise fills the corners.
internal sealed class RoundedWindowRegion
{
    private static readonly ConditionalWeakTable<Window, RoundedWindowRegion> attached = new();
    private readonly Window window;
    private readonly HwndSource source;
    private bool queued, closed, applying;

    internal static void Attach(Window window)
    {
        if (window.AllowsTransparency || window.WindowStyle != WindowStyle.None) return;
        if (attached.TryGetValue(window, out var existing)) { existing.Queue(); return; }
        var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        if (source != null) attached.Add(window, new RoundedWindowRegion(window, source));
    }

    private RoundedWindowRegion(Window window, HwndSource source)
    {
        this.window = window; this.source = source;
        source.AddHook(Hook);
        window.Loaded += OnLoaded;
        window.SizeChanged += OnSizeChanged;
        window.StateChanged += OnStateChanged;
        window.Closed += OnClosed;
        Queue();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => Queue();
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Queue();
    private void OnStateChanged(object? sender, EventArgs e) => Queue();
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Run after WindowChrome has finished its own region/frame updates.
        if (!applying && message is 0x0005 or 0x02E0 or 0x031E) Queue(); // SIZE, DPICHANGED, DWMCOMPOSITIONCHANGED
        return 0;
    }

    private void Queue()
    {
        if (queued || closed) return;
        queued = true;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            queued = false;
            if (closed || source.IsDisposed || window.WindowState == WindowState.Minimized) return;
            applying = true;
            try
            {
                if (window.WindowState == WindowState.Maximized)
                {
                    SetWindowRgn(source.Handle, 0, true);
                    return;
                }
                if (!GetWindowRect(source.Handle, out var bounds)) return;
                int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
                if (width <= 0 || height <= 0) return;
                int diameter = Math.Max(2, (int)Math.Round(16d * GetDpiForWindow(source.Handle) / 96d));
                nint region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
                if (region == 0) return;
                // Ownership transfers to Windows only on success.
                if (SetWindowRgn(source.Handle, region, true) == 0) DeleteObject(region);
            }
            finally { applying = false; }
        }));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        closed = true;
        source.RemoveHook(Hook);
        window.Loaded -= OnLoaded; window.SizeChanged -= OnSizeChanged;
        window.StateChanged -= OnStateChanged; window.Closed -= OnClosed;
        attached.Remove(window);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Bounds { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Bounds rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
}
