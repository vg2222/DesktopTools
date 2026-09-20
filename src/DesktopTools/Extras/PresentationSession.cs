using DesktopTools.Localization;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopTools.Core;
using DesktopTools.Native;

namespace DesktopTools.Extras;

/// <summary>Owns one presentation launch and releases every display surface on close.</summary>
public sealed class PresentationSession : IDisposable
{
    private readonly List<PresentationEffectWindow> lasers = [];
    private readonly List<SpotlightSurface> spotlights = [];
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(8) };
    private bool closed;
    public event Action<string>? Failed;
    public MonitorInfo ActiveMonitor { get; }
    public PresentationSession(string mode, AppSettings settings, bool allMonitors, MonitorInfo selectedMonitor)
    {
        ActiveMonitor = selectedMonitor;
        try
        {
            IReadOnlyList<MonitorInfo> monitors = allMonitors ? MonitorService.GetAll() : new[] { selectedMonitor };
            foreach (var monitor in monitors)
            {
                if (mode == "Spotlight") spotlights.Add(new SpotlightSurface(monitor, settings.SpotlightRadius, settings.SpotlightDim, allMonitors));
                else if (mode == "Laser") lasers.Add(new PresentationEffectWindow(monitor, mode, settings));
                else throw new ArgumentException(L.T("Unknown presentation effect."), nameof(mode));
            }
            timer.Tick += Tick;
        }
        catch { Dispose(); throw; }
    }
    public void Show()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        try
        {
            // Prepare every native region while hidden so the first visible frame has its opening.
            if (spotlights.Count != 0)
            {
                if (!NativeMethods.GetCursorPos(out var cursor)) throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("Could not locate the pointer."));
                foreach (var spotlight in spotlights) spotlight.Update(new Point(cursor.X, cursor.Y));
            }
            foreach (var laser in lasers) laser.Show();
            foreach (var spotlight in spotlights) spotlight.Show();
            if (spotlights.Count != 0) timer.Start();
        }
        catch { Dispose(); throw; }
    }
    private void Tick(object? sender, EventArgs args)
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) return;
        var point = new Point(cursor.X, cursor.Y);
        try { foreach (var spotlight in spotlights) spotlight.Update(point); }
        catch (Exception ex) { Close(); Failed?.Invoke(L.T("Presentation stopped: ") + ex.Message); }
    }
    public void Close() => Dispose();
    public void Dispose()
    {
        if (closed) return;
        closed = true; timer.Stop(); timer.Tick -= Tick;
        foreach (var laser in lasers) laser.Close();
        foreach (var spotlight in spotlights) spotlight.Dispose();
        lasers.Clear(); spotlights.Clear();
    }

    // Constant-alpha native window: only its region changes during motion. No per-pixel WPF mask.
    private sealed class SpotlightSurface : IDisposable
    {
        private readonly IntPtr handle;
        private readonly MonitorInfo monitor;
        private readonly double radius;
        private readonly bool allMonitors;
        private Point? previous;
        private bool? active;
        private readonly Subclass paintCallback;
        public SpotlightSurface(MonitorInfo monitor, double radius, double dim, bool allMonitors)
        {
            paintCallback = PaintBlack;
            this.monitor = monitor; this.radius = double.IsFinite(radius) ? Math.Clamp(radius, 20, 1000) : 120; this.allMonitors = allMonitors;
            handle = CreateWindowEx(0x00080000 | 0x20 | 0x80 | 0x08000000, "STATIC", L.T("DesktopTools spotlight"), unchecked((int)0x80000000) | 4,
                (int)monitor.Bounds.X, (int)monitor.Bounds.Y, (int)monitor.Bounds.Width, (int)monitor.Bounds.Height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!SetWindowSubclass(handle, paintCallback, 42, 0)) { DestroyWindow(handle); throw new Win32Exception(Marshal.GetLastWin32Error()); }
            if (!SetLayeredWindowAttributes(handle, 0, (byte)(255 * (double.IsFinite(dim) ? Math.Clamp(dim, .1, .95) : .65)), 2))
            { int error = Marshal.GetLastWin32Error(); DestroyWindow(handle); throw new Win32Exception(error); }
        }
        public void Show()
        {
            if (!NativeMethods.SetWindowPos(handle, new IntPtr(-1), (int)monitor.Bounds.X, (int)monitor.Bounds.Y, (int)monitor.Bounds.Width, (int)monitor.Bounds.Height, 0x10 | 0x40))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        public void Update(Point cursor)
        {
            bool contains = monitor.Bounds.Contains(cursor);
            // Draw the part of the opening on both sides of a monitor seam.
            if (allMonitors)
            {
                var near = monitor.Bounds;
                near.Inflate(radius * monitor.ScaleX, radius * monitor.ScaleY);
                contains = near.Contains(cursor);
            }
            if (previous == cursor) return;
            if (!contains && active == false) { previous = cursor; return; }
            int width = (int)monitor.Bounds.Width, height = (int)monitor.Bounds.Height;
            // All-display mode keeps other displays dim. Selected mode clears when pointer leaves it.
            IntPtr region = CreateRectRgn(0, 0, contains || allMonitors ? width : 0, contains || allMonitors ? height : 0);
            if (region == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (contains)
                {
                    int x = (int)(cursor.X - monitor.Bounds.X), y = (int)(cursor.Y - monitor.Bounds.Y);
                    int rx = (int)Math.Round(radius * monitor.ScaleX), ry = (int)Math.Round(radius * monitor.ScaleY);
                    IntPtr hole = CreateEllipticRgn(x - rx, y - ry, x + rx, y + ry);
                    if (hole == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    try { if (CombineRgn(region, region, hole, 4) == 0) throw new Win32Exception(Marshal.GetLastWin32Error()); }
                    finally { NativeMethods.DeleteObject(hole); }
                }
                if (SetWindowRgn(handle, region, true) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                region = IntPtr.Zero; // Successful SetWindowRgn transfers ownership to Windows.
                previous = cursor; active = contains;
            }
            finally { if (region != IntPtr.Zero) NativeMethods.DeleteObject(region); }
        }
        public void Dispose() => DestroyWindow(handle);
        private static nint PaintBlack(nint hwnd, uint message, nint wp, nint lp, nuint id, nuint data)
        {
            if (message == 0x318) { GetClientRect(hwnd, out var bounds); FillRect(wp, ref bounds, GetStockObject(4)); return 0; }
            if (message == 0xF)
            {
                nint dc = BeginPaint(hwnd, out var paint);
                try { GetClientRect(hwnd, out var rect); FillRect(dc, ref rect, GetStockObject(4)); }
                finally { EndPaint(hwnd, ref paint); }
                return 0;
            }
            if (message == 0x14) return 1;
            return DefSubclassProc(hwnd, message, wp, lp);
        }
    }
    private delegate nint Subclass(nint hwnd, uint message, nint wp, nint lp, nuint id, nuint data);
    [StructLayout(LayoutKind.Sequential)] private struct PaintStruct { public nint Dc; public int Erase; public NativeMethods.Rect Rect; public int Restore, IncUpdate; public long Reserved1, Reserved2, Reserved3, Reserved4; }
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint hwnd, Subclass callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nint wp, nint lp);
    [DllImport("user32.dll")] private static extern nint BeginPaint(nint hwnd, out PaintStruct paint);
    [DllImport("user32.dll")] private static extern bool EndPaint(nint hwnd, ref PaintStruct paint);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out NativeMethods.Rect rect);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref NativeMethods.Rect rect, nint brush);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(int extendedStyle, string className, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int CombineRgn(IntPtr destination, IntPtr first, IntPtr second, int mode);
}
