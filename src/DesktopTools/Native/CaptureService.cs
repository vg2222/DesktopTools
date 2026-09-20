using DesktopTools.Localization;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace DesktopTools.Native;

public static class CaptureService
{
    /// <summary>Captures physical desktop pixels once. Caller hides all overlays and flushes DWM first.</summary>
    public static BitmapSource Capture(MonitorInfo monitor)
    {
        int width = checked((int)monitor.Bounds.Width);
        int height = checked((int)monitor.Bounds.Height);
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(monitor), L.T("Monitor dimensions must be positive."));
        IntPtr screen = IntPtr.Zero, memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            screen = NativeMethods.GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) throw Failure(L.T("Could not access the desktop."));
            memory = NativeMethods.CreateCompatibleDC(screen);
            if (memory == IntPtr.Zero) throw Failure(L.T("Could not allocate a capture context."));
            bitmap = NativeMethods.CreateCompatibleBitmap(screen, width, height);
            if (bitmap == IntPtr.Zero) throw Failure(L.T("Could not allocate a screenshot."));
            previous = NativeMethods.SelectObject(memory, bitmap);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1)) throw Failure(L.T("Could not prepare the screenshot."));
            if (!NativeMethods.BitBlt(memory, 0, 0, width, height, screen, checked((int)monitor.Bounds.X), checked((int)monitor.Bounds.Y), 0x00CC0020 | 0x40000000))
                throw Failure(L.T("Windows could not capture this desktop."));
            BitmapSource image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            if (previous != IntPtr.Zero && previous != new IntPtr(-1)) NativeMethods.SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
            if (memory != IntPtr.Zero) NativeMethods.DeleteDC(memory);
            if (screen != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static Win32Exception Failure(string message) => new(Marshal.GetLastWin32Error(), message);
}
