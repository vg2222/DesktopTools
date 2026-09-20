using DesktopTools.Localization;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DesktopTools.Native;

/// <summary>Bounds and WorkingArea are physical desktop pixels, including negative origins.</summary>
public sealed record MonitorInfo(string Id, Rect Bounds, Rect WorkingArea, double ScaleX, double ScaleY);

public static class MonitorService
{
    public static MonitorInfo GetVirtualDesktop()
    {
        var monitors = GetAll();
        if (monitors.Count == 0) throw new InvalidOperationException(L.T("No displays are connected."));
        var bounds = monitors[0].Bounds; var work = monitors[0].WorkingArea;
        foreach (var monitor in monitors.Skip(1)) { bounds.Union(monitor.Bounds); work.Union(monitor.WorkingArea); }
        return new MonitorInfo("VirtualDesktop", bounds, work, 1, 1);
    }

    public static MonitorInfo GetCurrent(bool primary = false)
    {
        NativeMethods.Point point = default;
        if (!primary && !NativeMethods.GetCursorPos(out point)) throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("Could not locate the pointer."));
        return Read(NativeMethods.MonitorFromPoint(point, primary ? 1u : 2u));
    }

    internal static MonitorInfo GetForWindow(Window window)
    {
        window.Dispatcher.VerifyAccess();
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) throw new InvalidOperationException("The window has no native handle.");
        return Read(NativeMethods.MonitorFromWindow(handle, 2));
    }

    public static IReadOnlyList<MonitorInfo> GetAll()
    {
        var monitors = new List<MonitorInfo>();
        Exception? failure = null;
        bool Enumerate(IntPtr monitor, IntPtr dc, ref NativeMethods.Rect bounds, IntPtr data)
        {
            try { monitors.Add(Read(monitor)); return true; }
            catch (Exception ex) { failure = ex; return false; }
        }
        bool success = NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Enumerate, IntPtr.Zero);
        if (failure != null) throw new InvalidOperationException(L.T("Could not read connected monitors."), failure);
        if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("Could not enumerate monitors."));
        return monitors;
    }

    private static MonitorInfo Read(IntPtr monitor)
    {
        var info = new NativeMethods.MonitorInfoEx { Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>(), Device = string.Empty };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        int result = NativeMethods.GetDpiForMonitor(monitor, 0, out uint x, out uint y);
        if (result < 0) Marshal.ThrowExceptionForHR(result);
        return new(info.Device, Convert(info.Monitor), Convert(info.Work), x / 96d, y / 96d);
    }

    private static Rect Convert(NativeMethods.Rect value) => new(value.Left, value.Top, value.Right - value.Left, value.Bottom - value.Top);
    public static Point PhysicalToLocal(Point point, MonitorInfo monitor) => new((point.X - monitor.Bounds.X) / monitor.ScaleX, (point.Y - monitor.Bounds.Y) / monitor.ScaleY);
    public static Point LocalToPhysical(Point point, MonitorInfo monitor) => new(monitor.Bounds.X + point.X * monitor.ScaleX, monitor.Bounds.Y + point.Y * monitor.ScaleY);

    public static void PlaceWindow(Window window, MonitorInfo monitor)
    {
        window.Dispatcher.VerifyAccess();
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException(L.T("Show the window before placing it on a monitor."));
        Rect bounds = monitor.Bounds;
        if (!NativeMethods.SetWindowPos(hwnd, new IntPtr(-1), checked((int)bounds.X), checked((int)bounds.Y), checked((int)bounds.Width), checked((int)bounds.Height), 0x0010))
            throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("Could not position the overlay."));
    }
}
