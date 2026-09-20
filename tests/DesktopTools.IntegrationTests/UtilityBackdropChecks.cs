using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Native;

internal static class UtilityBackdropChecks
{
    internal static Task RunAsync() => RunAsync(false);
    internal static Task RunMediaAsync() => RunAsync(true);
    private static async Task RunAsync(bool mediaOnly)
    {
        using var controller = new AppController(true);
        controller.UpdateSettings(s => { s.Theme = "Dark"; s.Transparency = true; s.Animations = false; s.HideControlsFromCapture = false; });
        var background = new Window { Title = "DesktopTools backdrop helper", Width = 1080, Height = 800,
            WindowStyle = WindowStyle.None, ShowActivated = false, ShowInTaskbar = false, Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new LinearGradientBrush(Colors.CornflowerBlue, Colors.DarkOliveGreen, 35) };
        background.Show(); await Task.Delay(50);
        try
        {
            string? source = mediaOnly ? await VideoEditingChecks.FixtureAsync(Path.GetFullPath("media-backdrop-" + Guid.NewGuid().ToString("N"))) : null;
            var sample = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[] { 255, 100, 20, 255, 60, 180, 40, 255, 60, 180, 40, 255, 255, 100, 20, 255 }, 8); sample.Freeze();
            var windows = mediaOnly
                ? new Window[] { new VideoEditorWindow(_ => { }), new TextToolsWindow(controller), new OcrTextWindow(sample, _ => { }) }
                : new Window[] { new ImageToolsWindow(_ => { }), new AudioControlsWindow(_ => { }), new TeleprompterWindow(new AppSettings(), _ => true) };
            foreach (var window in windows)
            {
                try
                {
                    window.Width = Math.Min(900, window.Width); window.Height = Math.Min(620, window.Height);
                    window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = background.Left + 50; window.Top = background.Top + 50;
                    window.Topmost = true; window.Show();
                    if (window is VideoEditorWindow video) { await video.LoadAsync(source!); await Task.Delay(350); }
                    window.UpdateLayout(); await Task.Delay(220);
                    var layout = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); layout.Render(window);
                    var layoutPng = new PngBitmapEncoder(); layoutPng.Frames.Add(BitmapFrame.Create(layout));
                    using (var file = File.Create("utility-layout-" + window.GetType().Name + ".png")) layoutPng.Save(file);
                    if (window.AllowsTransparency || window.WindowStyle != WindowStyle.None) throw new Exception("Utility did not use backdrop-capable chrome");
                    var handle = new WindowInteropHelper(window).Handle;
                    int result = DwmGetWindowAttribute(handle, 38, out int backdrop, sizeof(int));
                    if (result >= 0 && backdrop is not 1 and not 3) throw new Exception("Unexpected DWM backdrop type: " + backdrop);
                    _ = DwmGetWindowAttribute(handle, 14, out int cloaked, sizeof(int));
                    File.AppendAllText("utility-backdrop-state.txt", window.GetType().Name + ": backdrop=" + backdrop + ", cloaked=" + cloaked + Environment.NewLine);
                    if (!NativeWindowService.TryExcludeHandle(handle, false, out var error) ||
                        !NativeWindowService.TryExcludeHandle(new WindowInteropHelper(background).Handle, false, out error))
                        throw new Exception("Could not expose owned visual fixtures: " + error);
                    await Task.Delay(180);
                    window.Activate(); await Task.Delay(350);
                    NativeWindowService.SynchronizeDesktop();
                    Point origin = window.PointToScreen(new Point()); var dpi = VisualTreeHelper.GetDpi(window);
                    var bounds = new Rect(Math.Ceiling(origin.X), Math.Ceiling(origin.Y), Math.Floor(window.ActualWidth * dpi.DpiScaleX), Math.Floor(window.ActualHeight * dpi.DpiScaleY));
                    _ = GetWindowRect(handle, out var nativeBounds);
                    _ = GetWindowDisplayAffinity(handle, out uint affinity);
                    var hit = WindowFromPoint(new NativePoint { X = (int)bounds.X + 40, Y = (int)bounds.Y + 40 });
                    File.AppendAllText("utility-backdrop-state.txt", $"capture={bounds}; native={nativeBounds.Left},{nativeBounds.Top},{nativeBounds.Right},{nativeBounds.Bottom}; affinity={affinity}; own={handle}; hit={hit}; foreground={GetForegroundWindow()}\n");
                    var capture = CaptureService.Capture(new MonitorInfo("Owned utility", bounds, bounds, 1, 1));
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(capture));
                    using (var file = File.Create("utility-backdrop-" + window.GetType().Name + ".png")) png.Save(file);
                    NativeWindowService.ApplyBackdrop(window, true, false);
                    if (window.Background is not SolidColorBrush solid || solid.Color.A != 255) throw new Exception("Opaque fallback missing");
                    if (DwmGetWindowAttribute(handle, 38, out int disabled, sizeof(int)) >= 0 && disabled != 1) throw new Exception("Disabling transparency left acrylic active");
                }
                finally { window.Close(); }
            }
        }
        finally { background.Close(); }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int X, Y; }
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeMethods.Rect bounds);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);
}
