using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Interop;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.UI;
using Forms = System.Windows.Forms;

internal static class NativeInputChecks
{
    public sealed record TargetState(long Form, long Button, long TextBox, long Panel, int Clicks, string Text, int Scroll, int WheelEvents);
    private sealed class ScrollTarget : Forms.Panel
    {
        public int WheelEvents { get; private set; }
        public ScrollTarget() { SetStyle(Forms.ControlStyles.Selectable, true); TabStop = true; }
        protected override void OnMouseDown(Forms.MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
        protected override void OnMouseWheel(Forms.MouseEventArgs e)
        {
            WheelEvents++;
            // Keep this test application's scroll distance independent of the user's wheel-lines preference.
            AutoScrollPosition = new System.Drawing.Point(0, Math.Clamp(-AutoScrollPosition.Y - e.Delta, 0, 1200));
            if (e is Forms.HandledMouseEventArgs handled) handled.Handled = true;
        }
    }

    // This entry point must run before creating a WPF Application in the child process.
    public static int RunTarget(string statePath)
    {
        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
        Forms.Application.EnableVisualStyles();
        using var form = new Forms.Form { Text = "DesktopTools native input test target", Width = 620, Height = 560,
            StartPosition = Forms.FormStartPosition.Manual, AutoScaleMode = Forms.AutoScaleMode.None };
        var work = Forms.Screen.PrimaryScreen!.WorkingArea;
        form.Location = new System.Drawing.Point(work.Left + (work.Width - form.Width) / 2, work.Top + Math.Max(150, (work.Height - form.Height) / 2));
        var button = new Forms.Button { Text = "Test click counter", Left = 30, Top = 80, Width = 500, Height = 70 };
        var input = new Forms.TextBox { Left = 30, Top = 170, Width = 500 };
        var panel = new ScrollTarget { Left = 30, Top = 220, Width = 500, Height = 220, AutoScroll = true, AutoScrollMinSize = new System.Drawing.Size(0, 1500), BackColor = System.Drawing.Color.AliceBlue };
        form.Controls.AddRange([button, input, panel]);
        int clicks = 0;
        button.Click += (_, _) => clicks++;
        using var timer = new Forms.Timer { Interval = 40 };
        timer.Tick += (_, _) =>
        {
            var state = new TargetState(form.Handle.ToInt64(), button.Handle.ToInt64(), input.Handle.ToInt64(), panel.Handle.ToInt64(), clicks, input.Text, -panel.AutoScrollPosition.Y, panel.WheelEvents);
            try { File.WriteAllText(statePath + ".tmp", JsonSerializer.Serialize(state)); File.Move(statePath + ".tmp", statePath, true); }
            catch (IOException) { /* The next timer tick retries a reader/file-system collision. */ }
        };
        form.Shown += (_, _) => { timer.Start(); form.Activate(); };
        Forms.Application.Run(form);
        return 0;
    }

    public static async Task RunAsync(AppController controller, Action<string> result)
    {
        string statePath = Path.Combine(Environment.CurrentDirectory, "native-input-" + Guid.NewGuid().ToString("N") + ".json");
        nint originalForeground = GetForegroundWindow(); GetCursorPos(out var originalCursor);
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("No process executable path.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--input-target"); start.ArgumentList.Add(statePath);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not launch native input target.");
        nint target = 0, overlayHandle = 0, paletteHandle = 0;
        bool OwnedForeground() { var hwnd = GetForegroundWindow(); return hwnd != 0 && (hwnd == target || hwnd == overlayHandle || hwnd == paletteHandle); }
        void Guard() { if (!OwnedForeground()) throw new InvalidOperationException("Input aborted: foreground is outside this test's target/overlay/palette."); }
        void Mouse(uint flags, uint data = 0)
        {
            Guard(); Send(new INPUT { Type = 0, Union = new InputUnion { Mouse = new MOUSEINPUT { Flags = flags, MouseData = data } } });
        }
        void Key(ushort key, bool unicode = false)
        {
            Guard(); uint flags = unicode ? 4u : 0u;
            Send(new INPUT { Type = 1, Union = new InputUnion { Keyboard = new KEYBDINPUT { VirtualKey = unicode ? (ushort)0 : key, Scan = unicode ? key : (ushort)0, Flags = flags } } },
                 new INPUT { Type = 1, Union = new InputUnion { Keyboard = new KEYBDINPUT { VirtualKey = unicode ? (ushort)0 : key, Scan = unicode ? key : (ushort)0, Flags = flags | 2 } } });
        }
        async Task Position(nint hwnd, nint expectedTop)
        {
            Guard(); Check(GetWindowRect(hwnd, out var rect), "Target control has no rectangle.");
            SetCursorPos((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2); await Task.Delay(60); Guard();
            GetCursorPos(out var p); var actual = GetAncestor(WindowFromPoint(p), 2); Check(actual == expectedTop, $"Unexpected hit-test window at {p.X},{p.Y}: expected {expectedTop}, got {actual}; target={target}, overlay={overlayHandle}, palette={paletteHandle}. No mouse input sent.");
        }
        async Task Click(nint hwnd, nint expectedTop)
        {
            await Position(hwnd, expectedTop);
            Guard(); Send(new INPUT { Union = new InputUnion { Mouse = new MOUSEINPUT { Flags = 2 } } }, new INPUT { Union = new InputUnion { Mouse = new MOUSEINPUT { Flags = 4 } } });
            await Task.Delay(100);
        }
        try
        {
            var state = await WaitState(statePath, _ => true); target = (nint)state.Form;
            GetWindowThreadProcessId(target, out uint owner); Check(owner == child.Id, "Handshake HWND does not belong to launched target.");
            controller.Interact(false);
            controller.HideAnnotations();
            Check(controller.UpdateSettings(s => s.MonitorMode = "Primary"), "Cannot set drawing monitor.");
            Check(SetForegroundWindow(target), "Windows denied target activation."); await Task.Delay(100); Guard();
            controller.SetTool("Pen"); controller.ToggleDraw(); await Task.Delay(120);
            var overlay = (OverlayWindow)typeof(AppController).GetField("overlay", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
            overlayHandle = new WindowInteropHelper(overlay).Handle; paletteHandle = new WindowInteropHelper(controller.Palette!).Handle;
            // Keep controls away from the input target and focus the actual canvas.
            controller.Palette!.Left = overlay.Left + 8; controller.Palette.Top = overlay.Top + 8;
            overlay.Activate(); await Task.Delay(80);
            int initialCount = controller.Document.Items.Count;
            await Position((nint)state.Button, overlayHandle);
            Guard();
            // Down, movement and up stay in one bounded native batch, so failure cannot strand a held button.
            Send(new INPUT { Union = new InputUnion { Mouse = new MOUSEINPUT { Flags = 2 } } },
                 new INPUT { Union = new InputUnion { Mouse = new MOUSEINPUT { Dx = 35, Dy = 8, Flags = 1 } } },
                 new INPUT { Union = new InputUnion { Mouse = new MOUSEINPUT { Flags = 4 } } });
            await Task.Delay(150); state = await WaitState(statePath, _ => true);
            Check(controller.Document.Items.Count == initialCount + 1 && state.Clicks == 0, "Draw did not intercept the target button stroke.");
            result("PASS native Draw intercepts a stroke over another process's button");

            controller.Interact(); await Task.Delay(100);
            Check(GetForegroundWindow() == target, "Interact did not restore target foreground.");
            await Click((nint)state.Button, target); state = await WaitState(statePath, s => s.Clicks == 1);
            await Click((nint)state.TextBox, target);
            foreach (char c in "native123") Key(c, true);
            state = await WaitState(statePath, s => s.Text == "native123");
            await Click((nint)state.Panel, target); Mouse(0x0800, unchecked((uint)-360));
            state = await WaitState(statePath, s => s.Scroll > 0 && s.WheelEvents > 0);
            result("PASS native Interact forwards clicks, keyboard text and wheel scrolling to another process");

            controller.ToggleDraw(); await Task.Delay(100); overlay.Activate(); await Task.Delay(80);
            int before = controller.Document.Items.Count;
            await Click((nint)state.Button, overlayHandle); state = await WaitState(statePath, _ => true);
            Check(state.Clicks == 1 && controller.Document.Items.Count == before + 1, "Returning to Draw did not restore interception.");
            result("PASS native returning to Draw restores mouse interception");

            // Explicit activation tests focus transfer without sending Alt+Tab to unrelated windows.
            Guard(); Check(SetForegroundWindow(target), "Windows denied explicit target activation.");
            controller.CheckDrawingFocus(); await Task.Delay(160);
            Check(controller.State == OverlayState.Interact && GetForegroundWindow() == target, "External activation did not suspend Draw without stealing focus.");
            result("PASS native external activation suspends Draw without focus stealing");
            controller.ToggleDraw(); await Task.Delay(100); controller.Palette!.Activate(); await Task.Delay(80);
            Check(GetForegroundWindow() == paletteHandle, "Palette could not receive keyboard focus.");
            controller.TogglePalette(); await Task.Delay(100);
            Check(controller.State == OverlayState.Draw && !controller.Palette.IsVisible && GetForegroundWindow() == overlayHandle, "Hiding palette did not preserve drawing focus.");
            Key(0x41); await Task.Delay(100); Check(controller.Tool == "Arrow", "Local tool shortcut unavailable with hidden palette.");
            controller.TogglePalette(); controller.Palette.Activate(); await Task.Delay(80);
            result("PASS native manual palette hiding retains Draw and local shortcuts");
            Key(0x1B); await Task.Delay(120);
            Check(controller.State == OverlayState.Hidden && !overlay.IsVisible && !controller.Palette.IsVisible, "Escape from palette did not hide drawing.");
            result("PASS native Escape with palette focus hides drawing");
        }
        finally
        {
            bool restore = OwnedForeground();
            if (restore) controller.HideAnnotations(); else controller.Interact(false);
            if (!child.HasExited)
            {
                child.CloseMainWindow();
                var finished = child.WaitForExitAsync();
                if (await Task.WhenAny(finished, Task.Delay(3000)) != finished) child.Kill();
            }
            if (restore) { SetCursorPos(originalCursor.X, originalCursor.Y); if (originalForeground != 0) SetForegroundWindow(originalForeground); }
        }
    }

    private static async Task<TargetState> WaitState(string path, Func<TargetState, bool> predicate)
    {
        var watch = Stopwatch.StartNew(); TargetState? latest = null;
        while (watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            try { latest = JsonSerializer.Deserialize<TargetState>(File.ReadAllText(path)); if (latest != null && predicate(latest)) return latest; }
            catch (IOException) { } catch (JsonException) { }
            await Task.Delay(50);
        }
        throw new TimeoutException("Native target handshake timed out; latest state: " + JsonSerializer.Serialize(latest));
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Send(params INPUT[] inputs) { Check(SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length, "SendInput failed: " + Marshal.GetLastWin32Error()); }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint Type; public InputUnion Union; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT Mouse; [FieldOffset(0)] public KEYBDINPUT Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int Dx, Dy; public uint MouseData, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort VirtualKey, Scan; public uint Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] input, int size);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
}
