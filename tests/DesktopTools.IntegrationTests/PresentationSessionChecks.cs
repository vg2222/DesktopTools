using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Native;

internal static class PresentationSessionChecks
{
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint message, nint wp, nint lp);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static object[] Surfaces(PresentationSession session) => Field<IEnumerable>(session, "spotlights").Cast<object>().ToArray();
    private static nint Handle(object surface) => Field<nint>(surface, "handle");

    public static Task RunAsync()
    {
        var settings = new AppSettings();
        Check(settings.LaserMonitorMode == "All" && settings.SpotlightMonitorMode == "All", "Presentation defaults must cover all monitors.");
        var selected = MonitorService.GetCurrent();
        foreach (bool all in new[] { false, true })
        {
            using var session = new PresentationSession("Spotlight", settings, all, selected);
            var surfaces = Surfaces(session);
            Check(surfaces.Length == (all ? MonitorService.GetAll().Count : 1), "Wrong number of native display surfaces.");
            bool sawShow = false, uninitializedAtShow = false;
            Subclass callback = (hwnd, msg, wp, lp, id, data) =>
            {
                if (msg == 0x46 && (Marshal.PtrToStructure<WindowPos>(lp).Flags & 0x40) != 0)
                {
                    sawShow = true;
                    nint region = CreateRectRgn(0, 0, 0, 0);
                    try { if (GetWindowRgn(hwnd, region) == 0) uninitializedAtShow = true; }
                    finally { DeleteObject(region); }
                }
                return DefSubclassProc(hwnd, msg, wp, lp);
            };
            foreach (var surface in surfaces) Check(SetWindowSubclass(Handle(surface), callback, 1, 0), "Could not observe native show.");
            try
            {
                session.Show();
                Check(sawShow && !uninitializedAtShow, "Spotlight became visible before its opening was initialized.");
                Check(Field<DispatcherTimer>(session, "timer").IsEnabled, "Spotlight cursor updates did not start.");
                foreach (var surface in surfaces)
                {
                    nint hwnd = Handle(surface);
                    Check(IsWindowVisible(hwnd), "Native spotlight was not shown.");
                    using (var pixels = new System.Drawing.Bitmap(20, 20))
                    using (var graphics = System.Drawing.Graphics.FromImage(pixels))
                    {
                        graphics.Clear(System.Drawing.Color.Magenta);
                        nint dc = graphics.GetHdc();
                        try { SendMessage(hwnd, 0x318, dc, new nint(4)); }
                        finally { graphics.ReleaseHdc(dc); }
                        Check(pixels.GetPixel(10, 10).ToArgb() == System.Drawing.Color.Black.ToArgb(), "Spotlight paint must be explicit black, independent of Windows theme colors.");
                    }
                    var monitor = Field<MonitorInfo>(surface, "monitor");
                    // Exercise desktop coordinates without moving the real pointer or injecting input.
                    var update = surface.GetType().GetMethod("Update")!;
                    update.Invoke(surface, [new Point(monitor.Bounds.Left + 200, monitor.Bounds.Top + 200)]);
                    nint region = CreateRectRgn(0, 0, 0, 0);
                    try
                    {
                        Check(GetWindowRgn(hwnd, region) != 0 && !PtInRegion(region, 200, 200), "Spotlight opening did not exclude the pointer.");
                        update.Invoke(surface, [new Point(monitor.Bounds.Right + 10000, monitor.Bounds.Bottom + 10000)]);
                        Check(GetWindowRgn(hwnd, region) != 0 && PtInRegion(region, 10, 10) == all, "Leaving a display did not apply the selected/all-monitor dim policy.");
                    }
                    finally { DeleteObject(region); }
                }
            }
            finally
            {
                foreach (var surface in surfaces) RemoveWindowSubclass(Handle(surface), callback, 1);
                session.Close();
                GC.KeepAlive(callback);
            }
            Check(!Field<DispatcherTimer>(session, "timer").IsEnabled && surfaces.All(s => !IsWindow(Handle(s))), "Close retained native windows or cursor timer.");
            session.Close();
        }
        using (var broken = new PresentationSession("Spotlight", settings, false, selected))
        {
            DestroyWindow(Handle(Surfaces(broken)[0]));
            bool threw = false;
            try { broken.Show(); } catch (System.ComponentModel.Win32Exception) { threw = true; }
            Check(threw && Field<bool>(broken, "closed") && !Field<DispatcherTimer>(broken, "timer").IsEnabled,
                "Failed initial show must dispose the session and leave its timer stopped.");
        }
        using (var broken = new PresentationSession("Spotlight", settings, false, selected))
        {
            broken.Show();
            var surface = Surfaces(broken)[0];
            DestroyWindow(Handle(surface));
            surface.GetType().GetField("previous", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(surface, null);
            int failures = 0; broken.Failed += _ => failures++;
            typeof(PresentationSession).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(broken, [null, EventArgs.Empty]);
            Check(failures == 1 && Field<bool>(broken, "closed") && !Field<DispatcherTimer>(broken, "timer").IsEnabled,
                "Failed cursor update must report once and release the session.");
        }
        return Task.CompletedTask;
    }

    [StructLayout(LayoutKind.Sequential)] private struct WindowPos { public nint Hwnd, After; public int X, Y, Width, Height; public uint Flags; }
    private delegate nint Subclass(nint hwnd, uint msg, nuint wp, nint lp, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, Subclass callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, Subclass callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint msg, nuint wp, nint lp);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint hwnd, nint region);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint region);
}
