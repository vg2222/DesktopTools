using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Localization;
using DesktopTools.UI;

internal static class RegionFreezeChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    internal static async Task RunAsync()
    {
        string directory = Path.Combine(Environment.CurrentDirectory, "region-freeze-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new SettingsStore(directory);
        Check(store.Load().FreezeRegionBeforeSelection, "New settings must freeze region selection by default");
        var settings = store.Load();
        settings.FreezeRegionBeforeSelection = false; store.Save(settings);
        Check(!new SettingsStore(directory).Load().FreezeRegionBeforeSelection, "Live selection choice was not persisted");
        foreach (string language in new[] { "ru", "de", "fr", "es" })
            Check(L.Catalog(language).ContainsKey("Freeze screen before selecting")
                && L.Catalog(language).ContainsKey("Capture a still when you start region capture, then choose from that frame."),
                "Region freeze setting is untranslated for " + language);

        CheckSelectorRendersStill();
        await CheckDashboardCaptureTransitionAsync();

        using var controller = new AppController(true);
        controller.Settings.CaptureEnabled = true;
        controller.Settings.IncludeAnnotations = false;
        controller.Settings.CaptureDelaySeconds = 0;
        controller.Settings.FreezeRegionBeforeSelection = true;
        var frozenTask = controller.CaptureAsync();
        SelectionWindow? selector = await WaitForSelection(controller);
        Check(selector != null, "Frozen selection did not open: " + controller.Status);
        BitmapSource? frame = selector!.FrozenFrame;
        Check(frame is { IsFrozen: true }, "Selector has no immutable preselection frame");
        var monitor = DesktopTools.Native.MonitorService.GetVirtualDesktop();
        var region = new Rect(Math.Max(20, monitor.Bounds.Width / monitor.ScaleX / 2), Math.Max(20, monitor.Bounds.Height / monitor.ScaleY / 2), 24, 24);
        selector.CompleteSelection(region);
        await frozenTask;
        Check(controller.LastCapture != null, "Frozen region produced no screenshot: " + controller.Status);
        var expected = new CroppedBitmap(frame!, new Int32Rect((int)Math.Floor(region.X * monitor.ScaleX), (int)Math.Floor(region.Y * monitor.ScaleY),
            (int)Math.Ceiling(region.Right * monitor.ScaleX) - (int)Math.Floor(region.X * monitor.ScaleX),
            (int)Math.Ceiling(region.Bottom * monitor.ScaleY) - (int)Math.Floor(region.Y * monitor.ScaleY)));
        Check(PixelsEqual(expected, controller.LastCapture!), "Final screenshot differs from the bitmap shown for selection");

        controller.Settings.FreezeRegionBeforeSelection = false;
        var liveTask = controller.CaptureAsync();
        selector = await WaitForSelection(controller);
        Check(selector != null && selector.FrozenFrame == null, "Live setting still uses a frozen selector");
        var completedCapture = controller.LastCapture;
        selector!.Close();
        await liveTask;
        Check(!controller.IsBusy && ReferenceEquals(completedCapture, controller.LastCapture), "Cancel changed capture or left input busy");

        controller.Settings.FreezeRegionBeforeSelection = true;
        controller.Settings.ScreenTextEnabled = true;
        var textTask = controller.CaptureAsync(textOnly: true);
        selector = await WaitForSelection(controller);
        Check(selector != null && selector.FrozenFrame == null, "OCR selection unexpectedly froze the screen");
        selector!.Close(); await textTask;
        Check(!controller.IsBusy && ReferenceEquals(completedCapture, controller.LastCapture), "Cancelled OCR selection changed the screenshot");

        await controller.CaptureAsync(repeat: true);
        Check(controller.LastCapture is { PixelWidth: > 0, PixelHeight: > 0 }
            && controller.LastCapture.PixelWidth == completedCapture!.PixelWidth
            && controller.LastCapture.PixelHeight == completedCapture.PixelHeight,
            "Repeat capture changed the saved region");
    }

    private static async Task CheckDashboardCaptureTransitionAsync()
    {
        using var controller = new AppController(true);
        controller.Settings.CaptureEnabled = true;
        controller.Settings.FreezeRegionBeforeSelection = true;
        controller.Settings.Animations = true;
        controller.OpenMain();
        var main = Application.Current.Windows.OfType<MainWindow>().Single(w => w.IsVisible);
        await Task.Delay(50);
        int before = DesktopTools.Presentation.WindowDismissal.NativeTransitionRevision(main);
        var tools = (Array)typeof(MainWindow).GetMethod("DashboardTools", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null)!;
        object capture = tools.Cast<object>().Single(tool => (string)tool.GetType().GetProperty("Id")!.GetValue(tool)! == "capture");
        var launch = (Action)capture.GetType().GetProperty("Launch")!.GetValue(capture)!;
        launch();
        Check(!main.IsVisible, "Region capture left the dashboard visible.");
        Check(DesktopTools.Presentation.WindowDismissal.NativeTransitionRevision(main) > before,
            "Dashboard capture hid through a native fade, leaving a translucent window in its frozen frame.");
        var selector = await WaitForSelection(controller);
        selector?.Close();
        for (int i = 0; i < 100 && controller.IsBusy; i++) await Task.Delay(20);
        Check(!controller.IsBusy, "Dashboard capture did not finish after cancellation.");
        main.Show();
        await Task.Delay(50);
        int visibleBefore = DesktopTools.Presentation.WindowDismissal.NativeTransitionRevision(main);
        var visibleCapture = controller.CaptureAsync();
        Check(DesktopTools.Presentation.WindowDismissal.NativeTransitionRevision(main) > visibleBefore,
            "Capturing while DesktopTools is visible did not suppress its native hide transition.");
        var visibleSelector = await WaitForSelection(controller);
        visibleSelector?.Close();
        await visibleCapture;
        Check(!controller.IsBusy, "Visible-main capture did not finish after cancellation.");
    }
    private static void CheckSelectorRendersStill()
    {
        var bounds = new Rect(0, 0, 100, 80);
        var monitor = new DesktopTools.Native.MonitorInfo("synthetic", bounds, bounds, 1, 1);
        var pixels = new byte[] { 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255 };
        var source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        source.Freeze();
        var selector = new SelectionWindow(monitor, frozenFrame: source);
        try
        {
            var surface = (FrameworkElement)selector.Content;
            surface.Measure(new Size(100, 80)); surface.Arrange(bounds);
            var render = new RenderTargetBitmap(100, 80, 96, 96, PixelFormats.Pbgra32);
            render.Render(surface);
            var pixel = new byte[4]; render.CopyPixels(new Int32Rect(1, 1, 1, 1), pixel, 4, 0);
            Check(pixel[2] > 100 && pixel[1] < 40 && pixel[0] < 40, "Selector did not draw the provided still behind its dimming layer");
        }
        finally { selector.Close(); }
    }

    private static async Task<SelectionWindow?> WaitForSelection(AppController controller)
    {
        for (int i = 0; i < 100; i++)
        {
            var selector = Application.Current.Windows.OfType<SelectionWindow>().FirstOrDefault(w => w.IsVisible);
            if (selector != null) return selector;
            if (!controller.IsBusy) return null;
            await Task.Delay(20);
        }
        return null;
    }

    private static bool PixelsEqual(BitmapSource a, BitmapSource b)
    {
        if (a.PixelWidth != b.PixelWidth || a.PixelHeight != b.PixelHeight) return false;
        int stride = (a.PixelWidth * a.Format.BitsPerPixel + 7) / 8;
        byte[] left = new byte[stride * a.PixelHeight], right = new byte[stride * b.PixelHeight];
        a.CopyPixels(left, stride, 0); b.CopyPixels(right, stride, 0);
        return left.SequenceEqual(right);
    }
}
