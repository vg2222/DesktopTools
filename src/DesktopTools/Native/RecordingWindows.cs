using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopTools.Native;

internal sealed record RecordingWindowInfo(nint Handle, uint ProcessId, string Title);

internal static class RecordingWindows
{
    private delegate bool Callback(nint hwnd, nint data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, nint data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder title, int count);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(nint hwnd, nint rectangle, nint region, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    internal static bool Available(RecordingWindowInfo source)
    {
        return IsSameWindow(source) && IsWindowVisible(source.Handle) && !IsIconic(source.Handle);
    }
    internal static bool IsSameWindow(RecordingWindowInfo source)
    {
        if (!NativeMethods.IsWindow(source.Handle)) return false;
        GetWindowThreadProcessId(source.Handle, out uint process); return process == source.ProcessId;
    }
    internal static RecordingWindowInfo Identify(nint handle) { GetWindowThreadProcessId(handle, out uint process); return new(handle, process, ""); }
    internal static bool RequestFrame(RecordingWindowInfo source) => Available(source) && RedrawWindow(source.Handle, 0, 0, 0x85); // INVALIDATE | ERASE | ALLCHILDREN; never synchronously wait for the source to paint.
    internal static IReadOnlyList<RecordingWindowInfo> GetAll(bool includeOwnProcess = false)
    {
        var result = new List<RecordingWindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
            GetWindowThreadProcessId(hwnd, out uint process); if (!includeOwnProcess && process == Environment.ProcessId) return true;
            if (DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            var title = new StringBuilder(1024); GetWindowText(hwnd, title, title.Capacity);
            if (title.Length > 0) result.Add(new(hwnd, process, title.ToString())); return true;
        }, 0);
        return result;
    }
}

internal sealed class WindowThumbnail : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Size { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct Properties
    {
        public uint Flags; public NativeMethods.Rect Destination, Source; public byte Opacity;
        [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        [MarshalAs(UnmanagedType.Bool)] public bool ClientOnly;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out Size size);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref Properties properties);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(nint thumbnail);
    private nint handle;
    internal bool Show(Window owner, FrameworkElement target, nint source)
    {
        Dispose(); if (DwmRegisterThumbnail(new WindowInteropHelper(owner).Handle, source, out handle) < 0) { handle = 0; return false; }
        return Update(owner, target);
    }
    internal bool Update(Window owner, FrameworkElement target)
    {
        if (handle == 0 || target.ActualWidth <= 0 || target.ActualHeight <= 0 || DwmQueryThumbnailSourceSize(handle, out var size) < 0 || size.Width <= 0 || size.Height <= 0) return false;
        var bounds = target.TransformToAncestor(owner).TransformBounds(new Rect(target.RenderSize));
        var dpi = VisualTreeHelper.GetDpi(owner); double scale = Math.Min(bounds.Width * dpi.DpiScaleX / size.Width, bounds.Height * dpi.DpiScaleY / size.Height);
        int width = (int)(size.Width * scale), height = (int)(size.Height * scale);
        int x = (int)(bounds.X * dpi.DpiScaleX + (bounds.Width * dpi.DpiScaleX - width) / 2), y = (int)(bounds.Y * dpi.DpiScaleY + (bounds.Height * dpi.DpiScaleY - height) / 2);
        var properties = new Properties { Flags = 1 | 4 | 8 | 16, Destination = new() { Left = x, Top = y, Right = x + width, Bottom = y + height }, Opacity = 255, Visible = owner.IsVisible, ClientOnly = false };
        return DwmUpdateThumbnailProperties(handle, ref properties) >= 0;
    }
    public void Dispose() { if (handle != 0) { DwmUnregisterThumbnail(handle); handle = 0; } }
}
