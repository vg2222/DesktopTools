using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Native;

internal static class ChromeWindowOrderChecks
{
    private sealed record Observation(string Phase, string Display, uint Dpi, bool Topmost, long Owner, int X, int Y, int Width, int Height);
    internal static Task RunAsync() => RunAsync(false);
    internal static Task RunPointerAsync() => RunAsync(true);
    private static async Task RunAsync(bool pointer)
    {
        var monitors = MonitorService.GetAll();
        Check(monitors.Count >= 2, "The Chrome round trip requires two connected displays.");
        string chrome = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        }.FirstOrDefault(File.Exists) ?? throw new InvalidOperationException("Chrome is not installed.");
        string directory = Path.GetFullPath("chrome-window-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string page = Path.Combine(directory, "fixture.html");
        File.WriteAllText(page, "<!doctype html><meta charset=utf-8><title>DesktopTools window check</title><style>body{background:#18232f;color:#edf3ff;font:22px Segoe UI;padding:48px}p{font-size:16px}</style><h1>DesktopTools window check</h1><p>This temporary profile is used only for a monitor-movement check.</p>");
        var start = new ProcessStartInfo(chrome) { UseShellExecute = false, WorkingDirectory = directory };
        foreach (string arg in new[] { "--user-data-dir=" + Path.Combine(directory, "profile"), "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--disable-sync", "--new-window", "--window-size=900,650", new Uri(page).AbsoluteUri })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated Chrome fixture.");
        var observations = new List<Observation>();
        bool tracing = WindowOrderTrace.Enabled; WindowOrderTrace.Enabled = true;
        AppController? controller = null;
        nint originalForeground = GetForegroundWindow(); GetCursorPos(out var originalCursor);
        bool held = false;
        bool pointerMoved = false;
        nint handle = 0;
        try
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(12))
            {
                Check(!process.HasExited, "Isolated Chrome exited before its window appeared.");
                process.Refresh(); handle = process.MainWindowHandle;
                if (handle != 0 && IsWindowVisible(handle)) break;
                await Task.Delay(100);
            }
            void Guard()
            {
                GetWindowThreadProcessId(handle, out uint pid);
                Check(NativeMethods.IsWindow(handle) && pid == (uint)process.Id, "Chrome fixture HWND no longer belongs to the launched process.");
            }
            void Observe(string phase, MonitorInfo monitor)
            {
                Guard(); Check(GetWindowRect(handle, out var bounds), "Could not read the owned Chrome window bounds.");
                bool topmost = WindowOrderTrace.Topmost(handle);
                observations.Add(new(phase, monitor.Id, GetDpiForWindow(handle), topmost, GetWindow(handle, 4).ToInt64(),
                    bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
                Check(!topmost, "Chrome became topmost during " + phase);
            }
            async Task RoundTrip(string phase)
            {
                foreach (var monitor in monitors.Concat(new[] { monitors[0] }))
                {
                    Guard();
                    if (pointer && phase != "drawing-controls")
                    {
                        Check(GetWindowRect(handle, out var rect), "Missing Chrome bounds.");
                        var startPoint = new NativePoint { X = rect.Right - 180, Y = rect.Top + 18 };
                        // Chrome exposes blank title-bar space as HTCAPTION. Never click a tab or page.
                        var packed = new nint((startPoint.Y & 0xffff) << 16 | (startPoint.X & 0xffff));
                        Check(SendMessage(handle, 0x84, 0, packed) == 2, "Chrome title target is not HTCAPTION; no input sent.");
                        if (!SetForegroundWindow(handle) || GetForegroundWindow() != handle)
                        {
                            Check(NativeMethods.SetWindowPos(handle, 0, 0, 0, 0, 0, 0x01 | 0x02 | 0x10),
                                "Cannot expose the isolated Chrome window for guarded activation.");
                            await Task.Delay(80); Guard();
                            MovePointer(startPoint);
                            pointerMoved = true;
                            await Task.Delay(40); Guard(); GetCursorPos(out var activationPoint);
                            nint activationHit = GetAncestor(WindowFromPoint(activationPoint), 2);
                            GetWindowThreadProcessId(activationHit, out uint activationPid);
                            nint activationForeground = GetForegroundWindow();
                            GetWindowThreadProcessId(activationForeground, out uint foregroundPid);
                            Check(Math.Abs(activationPoint.X - startPoint.X) <= 1 && Math.Abs(activationPoint.Y - startPoint.Y) <= 1 &&
                                  activationHit == handle,
                                $"Chrome activation target is obstructed; no input sent. Target={handle}/pid={process.Id}, hit={activationHit}/pid={activationPid}, foreground={activationForeground}/pid={foregroundPid}, point={activationPoint.X},{activationPoint.Y}.");
                            SendMouseClick();
                            await Task.Delay(150);
                        }
                        Check(GetForegroundWindow() == handle, "Pointer test could not activate its isolated Chrome window.");
                        MovePointer(startPoint);
                        pointerMoved = true;
                        await Task.Delay(40); Guard(); GetCursorPos(out var dragStart);
                        Check(Math.Abs(dragStart.X - startPoint.X) <= 1 && Math.Abs(dragStart.Y - startPoint.Y) <= 1 &&
                              GetForegroundWindow() == handle && GetAncestor(WindowFromPoint(dragStart), 2) == handle,
                            "Title target is obstructed; no input sent.");
                        startPoint = dragStart;
                        SendMouse(0x02); held = true;
                        int dx = (int)monitor.WorkingArea.X + 60 - rect.Left, dy = (int)monitor.WorkingArea.Y + 60 - rect.Top;
                        var expected = startPoint;
                        try
                        {
                            for (int step = 1; step <= 20; step++)
                            {
                                Guard(); GetCursorPos(out var actual);
                                Check(GetForegroundWindow() == handle && actual.X == expected.X && actual.Y == expected.Y,
                                    "Pointer/foreground changed outside the fixture; drag aborted.");
                                expected = new NativePoint { X = startPoint.X + dx * step / 20, Y = startPoint.Y + dy * step / 20 };
                                MovePointer(expected);
                                await Task.Delay(12); GetCursorPos(out expected);
                            }
                        }
                        finally { if (held) { SendMouse(0x04); held = false; } }
                        await Task.Delay(150);
                        Check(GetWindowRect(handle, out var moved) && monitor.Bounds.Contains(moved.Left + 100, moved.Top + 100), "Pointer drag did not reach the expected display.");
                    }
                    else Check(NativeMethods.SetWindowPos(handle, 0, (int)monitor.WorkingArea.X + 60, (int)monitor.WorkingArea.Y + 60,
                        0, 0, 0x01 | 0x04 | 0x10 | 0x0200), "Could not move the owned Chrome window.");
                    await Task.Delay(180); Observe(phase, monitor);
                }
            }
            Guard(); Observe("initial", monitors[0]);
            if (pointer)
            {
                Check(NativeMethods.SetWindowPos(handle, 0, (int)monitors[0].WorkingArea.X + 60, (int)monitors[0].WorkingArea.Y + 60,
                    0, 0, 0x01 | 0x04 | 0x10 | 0x0200), "Could not place the isolated Chrome fixture before pointer checks.");
                await Task.Delay(180);
            }
            await RoundTrip("before-controller");
            controller = new AppController(true);
            controller.Settings.DrawingEnabled = true; controller.Settings.MonitorMode = "Primary";
            controller.ToggleDraw();
            Check(controller.State == OverlayState.Draw, "Drawing controls did not start: " + controller.Status);
            await RoundTrip("drawing-controls");
            controller.Interact(); await RoundTrip("interact");
            controller.HideAnnotations(); await RoundTrip("hidden-controls");
            using var pins = new WindowPinService();
            Guard(); Check(pins.ToggleWindow(handle) && WindowOrderTrace.Topmost(handle), "Explicit Chrome pin failed.");
            Guard(); Check(!pins.ToggleWindow(handle) && !WindowOrderTrace.Topmost(handle), "Explicit Chrome unpin failed.");
        }
        finally
        {
            if (held) SendMouse(0x04);
            if (pointer && pointerMoved)
            {
                MovePointer(originalCursor);
                if (GetForegroundWindow() == handle && NativeMethods.IsWindow(originalForeground)) SetForegroundWindow(originalForeground);
            }
            controller?.Dispose();
            File.WriteAllText(pointer ? "chrome-pointer-order.json" : "chrome-window-order.json", JsonSerializer.Serialize(new
            {
                ChromeVersion = FileVersionInfo.GetVersionInfo(chrome).FileVersion,
                Windows = Environment.OSVersion.VersionString,
                Movement = pointer ? "Guarded title-bar pointer drags before controller, in Interact and Hidden; programmatic movement under Draw. Fresh isolated profile." : "Programmatic SetWindowPos round trips; no pointer drag or user profile.",
                Monitors = monitors, Observations = observations, Calls = WindowOrderTrace.Snapshot()
            }, new JsonSerializerOptions { WriteIndented = true }));
            WindowOrderTrace.Enabled = tracing;
            if (!process.HasExited)
            {
                process.CloseMainWindow();
                await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(3000));
                if (!process.HasExited)
                { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            }
        }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint handle, out NativeMethods.Rect bounds);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint handle, uint command);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint handle);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { internal int X, Y; internal uint Data, Flags, Time; internal nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { internal uint Type; internal MouseInput Mouse; }
    private static void SendMouse(uint flags)
    {
        var input = new[] { new Input { Mouse = new MouseInput { Flags = flags } } };
        Check(SendInput(1, input, Marshal.SizeOf<Input>()) == 1, "Native mouse input failed.");
    }
    private static void SendMouseClick()
    {
        var input = new[]
        {
            new Input { Mouse = new MouseInput { Flags = 0x02 } },
            new Input { Mouse = new MouseInput { Flags = 0x04 } }
        };
        Check(SendInput((uint)input.Length, input, Marshal.SizeOf<Input>()) == input.Length, "Native mouse activation click failed.");
    }
    private static void MovePointer(NativePoint point)
    {
        int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
        int width = GetSystemMetrics(78), height = GetSystemMetrics(79);
        Check(width > 1 && height > 1, "Virtual desktop geometry is unavailable.");
        int x = (int)Math.Clamp((long)(point.X - left) * 65535 / (width - 1), 0, 65535);
        int y = (int)Math.Clamp((long)(point.Y - top) * 65535 / (height - 1), 0, 65535);
        var input = new[] { new Input { Mouse = new MouseInput { X = x, Y = y, Flags = 0x0001 | 0x4000 | 0x8000 } } };
        Check(SendInput(1, input, Marshal.SizeOf<Input>()) == 1, "Native absolute pointer movement failed.");
    }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, uint message, nint wparam, nint lparam);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
}
