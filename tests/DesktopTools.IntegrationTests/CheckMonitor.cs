using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using DesktopTools.Native;

/// <summary>
/// Optional: DESKTOPTOOLS_CHECK_MONITOR=DISPLAY1 creates this harness's top-level windows on that monitor,
/// so nothing flashes on the primary one while checks run. A thread-local CBT hook rewrites the position
/// of each window before it exists, and a watchdog counts any that still appear on the primary monitor.
/// </summary>
internal static class CheckMonitor
{
    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct CallWndProcData { public IntPtr LParam; public IntPtr WParam; public uint Message; public IntPtr Hwnd; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect32 { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int hook, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);

    private const int WhCallWndProc = 4, GwlStyle = -16, GwlExStyle = -20, WmShowWindow = 0x18;
    private const int WsChild = 0x40000000;
    private const uint SwpNoSize = 0x1, SwpNoZOrder = 0x4, SwpNoActivate = 0x10;
    private const uint WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;
    private static HookProc? hookProc;
    private static IntPtr hook;
    private static Thread? watchdog;
    private static volatile bool watching;
    private static readonly HashSet<string> flashed = [];
    private static MonitorInfo? target;
    private static int seen, topLevel, moved;

    internal static void Enable(string wanted)
    {
        target = MonitorService.GetAll().FirstOrDefault(m => m.Id.Contains(wanted, StringComparison.OrdinalIgnoreCase));
        Console.WriteLine(target == null ? $"CHECK MONITOR {wanted}: not found, using default placement" : $"CHECK MONITOR {target.Id}: {target.WorkingArea}");
        if (target == null) return;
        hookProc = OnCallWindowProc;
        hook = SetWindowsHookEx(WhCallWndProc, hookProc, IntPtr.Zero, GetCurrentThreadId());
        if (hook == IntPtr.Zero) Console.WriteLine("CHECK MONITOR: could not install the window placement hook");
        watching = true;
        watchdog = new Thread(Watch) { IsBackground = true, Name = "check-monitor-watchdog" };
        watchdog.Start();
    }

    /// <summary>Stops watching and reports how many harness windows were visible on the primary monitor.</summary>
    internal static void Finish()
    {
        if (target == null) return;
        watching = false; watchdog?.Join(500);
        if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        Console.WriteLine($"CHECK MONITOR hook: {seen} windows shown, {topLevel} top-level, {moved} moved");
        Console.WriteLine($"CHECK MONITOR primary-monitor windows seen: {flashed.Count}" + (flashed.Count == 0 ? "" : " (" + string.Join(", ", flashed.Take(8)) + ")"));
    }

    // Runs before the window procedure for every message sent to this thread's windows. WPF recomputes the
    // proposed position of a window itself, so instead of editing that, the window is moved when it is about
    // to be shown (WM_SHOWWINDOW), before it is ever painted on the primary monitor.
    private static IntPtr OnCallWindowProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && target != null)
        {
            try
            {
                var data = Marshal.PtrToStructure<CallWndProcData>(lParam);
                if (data.Message == WmShowWindow && data.WParam != IntPtr.Zero)
                {
                    seen++;
                    if (IsPlainTopLevel(data.Hwnd) && GetWindowRect(data.Hwnd, out var rect))
                    {
                        topLevel++;
                        int cx = rect.Right - rect.Left, cy = rect.Bottom - rect.Top;
                        var primary = MonitorService.GetAll().FirstOrDefault(m => m.Bounds.X == 0 && m.Bounds.Y == 0);
                        double centerX = rect.Left + cx / 2.0, centerY = rect.Top + cy / 2.0;
                        bool onPrimary = primary != null && centerX >= primary.Bounds.X && centerX < primary.Bounds.Right && centerY >= primary.Bounds.Y && centerY < primary.Bounds.Bottom;
                        if (onPrimary && cx >= 200 && cy >= 120)
                        {
                            var area = target.WorkingArea;
                            int x = (int)(area.X + Math.Max(0, (area.Width - cx) / 2)), y = (int)(area.Y + Math.Max(0, (area.Height - cy) / 2));
                            SetWindowPos(data.Hwnd, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
                            moved++;
                        }
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    // Menus, tooltips, combo drops and notices are transient and keep their own placement.
    private static bool IsPlainTopLevel(IntPtr hwnd) =>
        GetParent(hwnd) == IntPtr.Zero && (GetWindowLong(hwnd, GwlStyle) & WsChild) == 0 &&
        (((uint)GetWindowLong(hwnd, GwlExStyle)) & (WsExToolWindow | WsExNoActivate)) == 0;

    private static void Watch()
    {
        uint self = (uint)Environment.ProcessId;
        var primary = MonitorService.GetAll().FirstOrDefault(m => m.Bounds.X == 0 && m.Bounds.Y == 0) ?? MonitorService.GetAll()[0];
        var name = new StringBuilder(128);
        while (watching)
        {
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out uint process);
                if (process != self || !IsWindowVisible(hwnd) || !GetWindowRect(hwnd, out var r)) return true;
                if ((((uint)GetWindowLong(hwnd, GwlExStyle)) & (WsExToolWindow | WsExNoActivate)) != 0) return true;
                double width = r.Right - r.Left, height = r.Bottom - r.Top;
                if (width < 200 || height < 120) return true;
                double centerX = (r.Left + r.Right) / 2.0, centerY = (r.Top + r.Bottom) / 2.0;
                if (centerX >= primary.Bounds.X && centerX < primary.Bounds.Right && centerY >= primary.Bounds.Y && centerY < primary.Bounds.Bottom)
                {
                    name.Clear(); GetClassName(hwnd, name, name.Capacity);
                    lock (flashed) flashed.Add($"{hwnd} {width:0}x{height:0}@{r.Left},{r.Top}");
                }
                return true;
            }, IntPtr.Zero);
            Thread.Sleep(4);
        }
    }
}
