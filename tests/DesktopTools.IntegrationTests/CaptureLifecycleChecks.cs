using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Native;

internal static class CaptureLifecycleChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Settle()
    {
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(60);
    }
    private static async Task Collect()
    {
        await Settle(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await Settle();
    }
    public static async Task RunAsync(Action<string> result)
    {
        // Capture only a small interior rectangle of this owned, opaque helper.
        // No clipboard, user documents, whole-desktop snapshots or injected input.
        var source = new Window { Title = "DesktopTools capture lifecycle target", Width = 300, Height = 240,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false,
            ShowInTaskbar = false, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        using var process = Process.GetCurrentProcess();
        var closedEditors = new List<WeakReference>();
        try
        {
            source.Show(); await Settle();
            for (int i = 0; i < 10; i++) await Cycle(source, i);
            await Collect(); process.Refresh();
            int windows = Application.Current.Windows.Count, handles = process.HandleCount;
            uint gdi = GetGuiResources(process.Handle, 0), user = GetGuiResources(process.Handle, 1);
            long memory = process.PrivateMemorySize64;
            for (int i = 0; i < 100; i++) closedEditors.Add(await Cycle(source, i));
            await Collect(); process.Refresh();
            int retained = closedEditors.Count(reference => reference.IsAlive);
            result($"MEASURE 100 capture/edit/export/close: windows {windows}->{Application.Current.Windows.Count}; handles {handles}->{process.HandleCount}; GDI {gdi}->{GetGuiResources(process.Handle, 0)}; USER {user}->{GetGuiResources(process.Handle, 1)}; private MiB {memory / 1048576d:F1}->{process.PrivateMemorySize64 / 1048576d:F1}; retained editors {retained}");
            Check(Application.Current.Windows.Count == windows, "Closed editors retained windows");
            Check(retained == 0, "Closed editors remain rooted after dispatcher cleanup");
            Check(process.HandleCount <= handles + 20, "Capture/editor native handles grew by more than 20");
            Check(GetGuiResources(process.Handle, 0) <= gdi + 4 && GetGuiResources(process.Handle, 1) <= user + 4, "Capture/editor GDI or USER resources accumulated");
            Check(process.PrivateMemorySize64 <= memory + 64 * 1048576L, "Capture/editor private memory grew by more than 64 MiB after warmup");
        }
        finally { source.Close(); }
    }
    private static async Task<WeakReference> Cycle(Window source, int index)
    {
        Color color = index % 2 == 0 ? Colors.CornflowerBlue : Colors.Coral;
        source.Background = new SolidColorBrush(color); source.UpdateLayout(); await Settle(); DwmFlush();
        Point origin = source.PointToScreen(new Point(24, 24));
        var bounds = new Rect(Math.Ceiling(origin.X), Math.Ceiling(origin.Y), 160, 100);
        var capture = CaptureService.Capture(new MonitorInfo("Owned helper interior", bounds, bounds, 1, 1));
        var pixel = new byte[4]; capture.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        Check(pixel[0] == color.B && pixel[1] == color.G && pixel[2] == color.R, "Native capture did not contain the current helper frame");
        BitmapSource? exported = null; string? error = null;
        var editor = new ScreenshotEditorWindow(capture, image => exported = image, message => error = message,
            applyToImage: true, editorLayout: index % 2 == 0 ? "A" : "B") { Width = 860, Height = 500, ShowActivated = false };
        var reference = new WeakReference(editor);
        try
        {
            editor.Show(); await Settle();
            var document = (ScreenshotEditDocument)typeof(ScreenshotEditorWindow).GetField("_document", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            document.Add(new Annotation { Color = Colors.White, Thickness = 8, Points = new[] { new Point(20, 20), new Point(80, 60) } });
            document.SetCrop(new Int32Rect(10, 10, 100, 70));
            typeof(ScreenshotEditorWindow).GetMethod("Export", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, new object[] { "Apply" });
            Check(error == null && exported is { PixelWidth: 100, PixelHeight: 70 } && !editor.IsVisible, "Apply did not export and close the editor: " + error);
            // Exercise encoding/decoding while keeping the generated data in memory.
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(exported!));
            using var stream = new System.IO.MemoryStream(); encoder.Save(stream); stream.Position = 0;
            var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            Check(decoded.PixelWidth == 100 && decoded.PixelHeight == 70, "Exported PNG did not round-trip");
            capture.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
            Check(pixel[0] == color.B && pixel[1] == color.G && pixel[2] == color.R, "Editing mutated captured source pixels");
        }
        finally { if (editor.IsVisible) editor.Close(); }
        return reference;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern uint GetGuiResources(nint process, uint flags);
}
