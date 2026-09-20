using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DesktopTools.Native;

internal static class WindowPinChecks
{
    public static async Task RunAsync()
    {
        string statePath = Path.Combine(Path.GetTempPath(), "desktoptools-pin-" + Guid.NewGuid().ToString("N") + ".json");
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("No test executable path.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--input-target");
        start.ArgumentList.Add(statePath);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not launch pin test target.");
        using var service = new WindowPinService();
        bool tracing = WindowOrderTrace.Enabled;
        WindowOrderTrace.Enabled = true;
        try
        {
            nint handle = 0;
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(8))
            {
                if (child.HasExited) throw new InvalidOperationException("Pin test target exited before creating its window.");
                child.Refresh();
                handle = child.MainWindowHandle;
                if (handle != 0 && IsWindowVisible(handle) && File.Exists(statePath)) break;
                await Task.Delay(50);
            }
            Check(handle != 0 && IsWindowVisible(handle), "Pin target window did not become visible.");
            GetWindowThreadProcessId(handle, out uint owner);
            Check(owner == (uint)child.Id, "Pin test HWND is not owned by the launched helper.");
            Check(!Topmost(handle), "Helper unexpectedly started topmost.");
            Check(GetWindowRect(handle, out Rect original), "Could not read helper bounds.");
            foreach (var monitor in MonitorService.GetAll())
            {
                Check(SetWindowPos(handle, 0, (int)monitor.WorkingArea.X + 30, (int)monitor.WorkingArea.Y + 30,
                    0, 0, 0x01 | 0x04 | 0x10 | 0x0200), "Could not move isolated helper to monitor");
                await Task.Delay(80);
                Check(!Topmost(handle), "Moving helper changed its topmost state");
            }
            Check(SetWindowPos(handle, 0, original.Left, original.Top, 0, 0, 0x01 | 0x04 | 0x10 | 0x0200), "Could not return helper");
            await Task.Delay(80);
            Check(!Topmost(handle), "Returning helper changed its topmost state");
            nint foreground = GetForegroundWindow();

            Check(service.ToggleWindow(handle) && Topmost(handle), "Pin did not make helper topmost.");
            SameBounds(handle, original);
            var trace = WindowOrderTrace.Snapshot();
            Check(Array.Exists(trace, e => e.Handle == handle.ToInt64() && e.ProcessId == (uint)child.Id &&
                e.Caller == "SetTopmost" && !e.BeforeTopmost && e.AfterTopmost && e.Success), "Explicit pin missing from window order diagnostics");
            File.WriteAllText("window-order-trace.json", System.Text.Json.JsonSerializer.Serialize(trace, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Check(GetForegroundWindow() == foreground, "Pin changed foreground focus.");
            Check(!service.ToggleWindow(handle) && !Topmost(handle), "Second toggle did not unpin helper.");
            SameBounds(handle, original);
            Check(GetForegroundWindow() == foreground, "Unpin changed foreground focus.");
            service.ToggleWindow(handle);
            service.RestoreAll();
            Check(!Topmost(handle), "RestoreAll did not restore original unpinned state.");
            service.ToggleWindow(handle);
            service.Dispose();
            Check(!Topmost(handle), "Dispose did not restore original unpinned state.");
            SameBounds(handle, original);

            // Establish an original topmost state only on this test's helper.
            Check(SetWindowPos(handle, new nint(-1), 0, 0, 0, 0, 0x213), "Could not establish helper topmost baseline.");
            using (var originallyPinned = new WindowPinService())
            {
                Check(!originallyPinned.ToggleWindow(handle) && !Topmost(handle), "Existing topmost window did not unpin.");
                originallyPinned.RestoreAll();
                Check(Topmost(handle), "RestoreAll lost originally topmost state.");
            }
            SameBounds(handle, original);
        }
        finally
        {
            service.Dispose();
            File.WriteAllText("window-order-trace.json", System.Text.Json.JsonSerializer.Serialize(WindowOrderTrace.Snapshot(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            WindowOrderTrace.Enabled = tracing;
            if (!child.HasExited)
            {
                child.CloseMainWindow();
                Task finished = child.WaitForExitAsync();
                if (await Task.WhenAny(finished, Task.Delay(3000)) != finished)
                { child.Kill(); await child.WaitForExitAsync(); }
            }
            File.Delete(statePath);
            File.Delete(statePath + ".tmp");
        }
    }

    private static bool Topmost(nint handle) => (GetWindowLongPtr(handle, -20).ToInt64() & 8) != 0;
    private static void SameBounds(nint handle, Rect expected)
    {
        Check(GetWindowRect(handle, out Rect actual) && actual.Equals(expected), "Pin changed helper bounds.");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint handle, out Rect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint handle, int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint handle, nint after, int x, int y, int width, int height, uint flags);
}
