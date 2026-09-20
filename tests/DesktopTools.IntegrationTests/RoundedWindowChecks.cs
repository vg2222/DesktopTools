using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopTools;
using DesktopTools.Extras;
using DesktopTools.Native;
using DesktopTools.UI;

internal static class RoundedWindowChecks
{
    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        controller.UpdateSettings(s => { s.Animations = false; s.Transparency = false; });
        controller.OpenMain();
        var main = (MainWindow)typeof(AppController).GetField("main", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        foreach (var window in new Window[] { main, new ImageToolsWindow(_ => { }), new TextToolsWindow(controller) })
        {
            try
            {
                window.Show(); await Task.Delay(100);
                // Simulate DWM not supplying corners, as in a VM. The native region must suffice.
                int noRounding = 1;
                DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 33, ref noRounding, sizeof(int));
                AssertRegion(window, true);
                window.Width += 37; window.Height += 19; await Task.Delay(100);
                AssertRegion(window, true);
                window.WindowState = WindowState.Maximized; await Task.Delay(100);
                AssertRegion(window, false);
                window.WindowState = WindowState.Normal; await Task.Delay(100);
                AssertRegion(window, true);
                NativeWindowService.ApplyBackdrop(window, false, true); await Task.Delay(100);
                AssertRegion(window, true);
            }
            finally { window.Close(); }
        }
        var overlay = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Width = 100, Height = 100 };
        try
        {
            overlay.Show(); NativeWindowService.ApplyBackdrop(overlay, true, false); await Task.Delay(30);
            AssertRegion(overlay, false);
        }
        finally { overlay.Close(); }
    }

    private static void AssertRegion(Window window, bool rounded)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            int kind = GetWindowRgn(hwnd, region);
            if (!rounded)
            {
                if (kind != 0 && !PtInRegion(region, 0, 0)) throw new Exception("Unexpected rounding on maximized/layered window");
                return;
            }
            GetWindowRect(hwnd, out var bounds);
            int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
            if (kind == 0 || PtInRegion(region, 0, 0) || PtInRegion(region, width - 1, 0) ||
                PtInRegion(region, 0, height - 1) || PtInRegion(region, width - 1, height - 1) ||
                !PtInRegion(region, width / 2, 0) || !PtInRegion(region, width / 2, height - 1) ||
                !PtInRegion(region, width - 1, height / 2))
                throw new Exception("Native rounded bounds incorrect: " + window.GetType().Name);
        }
        finally { DeleteObject(region); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Bounds { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Bounds bounds);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint hwnd, nint region);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
