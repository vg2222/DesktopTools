using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Native;
using DesktopTools;
using DesktopTools.Extras;
using System.Linq;
using System.Reflection;
using DesktopTools.UI;
using DesktopTools.Localization;

internal static class BlackoutChecks
{
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Bounds bounds);
    [StructLayout(LayoutKind.Sequential)] private struct Bounds { public int Left, Top, Right, Bottom; }
    private static byte[] CapturePixel(Bounds rect)
    {
        var monitor = new MonitorInfo("probe", new Rect(rect.Left + 30, rect.Top + 30, 100, 100), new Rect(), 1, 1);
        var capture = CaptureService.Capture(monitor); var converted = new FormatConvertedBitmap(capture, PixelFormats.Bgra32, null, 0); byte[] data = new byte[40000]; converted.CopyPixels(data, 400, 0); return data;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static async Task RunAsync()
    {
        var target = new Window { Width = 240, Height = 180, Left = 100, Top = 100, WindowStyle = WindowStyle.None, Background = Brushes.Red, Topmost = true, ShowActivated = false };
        try
        {
            target.Show(); GetWindowRect(new WindowInteropHelper(target).Handle, out var rect);
            await Task.Delay(250); NativeWindowService.SynchronizeDesktop();
            Check(CapturePixel(rect)[2] > 240, "Control window was not red before masking");
            foreach (byte alpha in new byte[] { 0, 1, 255 })
            {
                var overlay = new Window { Width = 240, Height = 180, Left = 100, Top = 100, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0)), Topmost = true, ShowActivated = false };
                try
                {
                    overlay.Show(); NativeWindowService.ConfigureOverlay(overlay, true);
                    NativeWindowService.ApplySharingBlackout(overlay); bool affinity = true;
                    await Task.Delay(250); NativeWindowService.SynchronizeDesktop();
                    var monitor = new MonitorInfo("probe", new Rect(rect.Left + 30, rect.Top + 30, 100, 100), new Rect(), 1, 1);
                    var capture = CaptureService.Capture(monitor); var converted = new FormatConvertedBitmap(capture, PixelFormats.Bgra32, null, 0); byte[] data = new byte[40000]; converted.CopyPixels(data, 400, 0);
                    File.AppendAllText("blackout-probe.txt", $"alpha={alpha}; affinity={affinity}; pixel RGB={data[2]},{data[1]},{data[0]}\n");
                    Check(data[0] == 0 && data[1] == 0 && data[2] == 0, "Supported GDI capture was not black");
                }
                finally { overlay.Close(); }
            }
            await Task.Delay(200); NativeWindowService.SynchronizeDesktop(); Check(CapturePixel(rect)[2] > 240, "Closing mask did not restore capture");
            using var controller = new AppController(true);
            controller.UpdateSettings(s => s.BlackoutSharingOnly = true); controller.Hud.ToggleBlackout();
            try
            {
                var windows = Application.Current.Windows.Cast<Window>().Where(w => ReferenceEquals(w.Tag, AppCapturePrivacy.PresentationSurface)).ToArray();
                Check(windows.Length == MonitorService.GetAll().Count, "Missing monitor masks");
                Check(windows.All(w => w.Background == Brushes.Transparent && !w.IsHitTestVisible && !AppCapturePrivacy.IsControlWindow(w)), "Sharing mask affects local input/appearance or gets excluded");
                await Task.Delay(200); NativeWindowService.SynchronizeDesktop(); Check(CapturePixel(rect)[2] == 0, "Real HUD mask did not blank capture");
                controller.CancelActiveTool(); Check(!controller.Hud.IsBlackoutVisible, "Escape retained sharing mask");
                controller.Hud.ToggleBlackout(); controller.UpdateSettings(s => s.BlackoutSharingOnly = false);
                Check(!controller.Hud.IsBlackoutVisible, "Changing mode retained old mask");
                controller.Hud.ToggleBlackout();
                Check(Application.Current.Windows.Cast<Window>().Where(w => ReferenceEquals(w.Tag, AppCapturePrivacy.PresentationSurface)).All(w => w.Background == Brushes.Black), "Local blackout no longer opaque black");
                controller.CancelActiveTool(); Check(!controller.Hud.IsBlackoutVisible, "Escape retained local blackout");
            }
            finally { controller.Hud.StopAll(); }
            target.Hide(); controller.OpenMain();
            var main = (MainWindow)typeof(AppController).GetField("main", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
            try
            {
                foreach (string language in L.Languages)
                {
                    L.Use(language); controller.UpdateSettings(s => { s.Theme = language == "ru" ? "Dark" : "Light"; s.Animations = false; });
                    main.Width = 800; main.Height = 560; main.Navigate("Screen blackout"); main.UpdateLayout();
                    var screenshot = new RenderTargetBitmap(800, 560, 96, 96, PixelFormats.Pbgra32); screenshot.Render(main);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(screenshot)); using var file = File.Create("blackout-ui-" + language + ".png"); encoder.Save(file);
                    Check(AppController.ClassifyNotification(L.T("Warning: your screen may still be visible in screen-sharing apps. Check the viewer's output before using this mode. Press Escape to stop.")) == NotificationKind.Warning, "Warning severity lost in " + language);
                }
            }
            finally { L.Use("en"); }
        }
        finally { target.Close(); }
    }
}
