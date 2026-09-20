using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Extras;
using DesktopTools.Localization;

internal static class BackgroundRemovalChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static byte[] Pixels(BitmapSource image) { var bytes = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(bytes, image.PixelWidth * 4, 0); return bytes; }
    private static void Save(BitmapSource image, string path) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file); }
    public static async Task RunAsync()
    {
        var source = BitmapSource.Create(2, 1, 144, 144, PixelFormats.Bgra32, null, new byte[] { 10, 20, 30, 128, 50, 60, 70, 255 }, 8); source.Freeze();
        var masked = BackgroundRemovalService.ApplyMask(source, [.5f, 0], 2, 1);
        Check(Pixels(masked).SequenceEqual(new byte[] { 10, 20, 30, 64, 50, 60, 70, 0 }), "Mask changed RGB or replaced existing alpha");
        Check(masked.IsFrozen && masked.DpiX == 144 && masked.PixelWidth == 2, "Output dimensions/DPI/freeze changed");
        Check(BackgroundRemovalService.Prepare(source).Length == 3 * 320 * 320, "Non-square preprocessing failed");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await BackgroundRemovalService.RemoveAsync(source, canceled.Token); throw new Exception("Pre-cancellation ignored"); } catch (OperationCanceledException) { }
        try { BackgroundRemovalService.ApplyMask(source, [float.NaN], 1, 1); throw new Exception("Invalid mask accepted"); } catch (ArgumentException) { }
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 400, 300));
            drawing.DrawEllipse(Brushes.ForestGreen, null, new Point(200, 145), 75, 100);
            drawing.DrawRectangle(Brushes.SaddleBrown, null, new Rect(185, 205, 30, 65));
        }
        var fixture = new RenderTargetBitmap(400, 300, 96, 96, PixelFormats.Pbgra32); fixture.Render(visual); fixture.Freeze();
        Save(fixture, "background-original.png");
        var watch = Stopwatch.StartNew(); var result = await BackgroundRemovalService.RemoveAsync(fixture); watch.Stop();
        Save(result, "background-removed.png"); var pixels = Pixels(result);
        if (File.Exists("background-photo-source.png"))
        {
            var photo = ImageTransforms.Load(Path.GetFullPath("background-photo-source.png"));
            var removed = await BackgroundRemovalService.RemoveAsync(photo);
            Save(removed, "background-photo-removed.png");
            var proof = new DrawingVisual(); using (var context = proof.RenderOpen())
            {
                context.DrawRectangle(Brushes.MediumPurple, null, new Rect(0, 0, removed.PixelWidth, removed.PixelHeight));
                context.DrawImage(removed, new Rect(0, 0, removed.PixelWidth, removed.PixelHeight));
            }
            var composite = new RenderTargetBitmap(removed.PixelWidth, removed.PixelHeight, 96, 96, PixelFormats.Pbgra32); composite.Render(proof); Save(composite, "background-photo-proof.png");
        }
        Check(pixels[3] < 30 && pixels[(145 * 400 + 200) * 4 + 3] > 200, "Model failed simple foreground/background fixture");
        Check(Pixels(ImageTransforms.Load(Path.GetFullPath("background-removed.png"))).SequenceEqual(pixels), "PNG changed RGBA pixels");
        using (var inFlight = new CancellationTokenSource())
        {
            var pending = BackgroundRemovalService.RemoveAsync(fixture, inFlight.Token);
            await Task.Delay(100); inFlight.Cancel();
            try { await pending; throw new Exception("In-flight cancellation ignored"); } catch (OperationCanceledException) { }
        }
        File.WriteAllText("background-performance.txt", $"CPU model initialization + inference + mask: {watch.Elapsed.TotalMilliseconds:0} ms; {Environment.ProcessorCount} logical processors; 400x300 synthetic fixture.");
        using var controller = new AppController(true);
        foreach (string language in L.Languages)
        {
            L.Use(language); controller.UpdateSettings(s => { s.Theme = language == "ru" ? "Dark" : "Light"; s.Animations = false; });
            var window = new ImageToolsWindow(_ => { }); window.Show();
            typeof(ImageToolsWindow).GetMethod("SetImage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { fixture });
            if (language == "en")
            {
                await window.RemoveBackgroundAsync();
                Check(!ReferenceEquals(Field<BitmapSource>(window, "bitmap"), fixture), "Window failed to apply result: " + Field<TextBlock>(window, "status").Text);
                Field<Button>(window, "undoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(ReferenceEquals(Field<BitmapSource>(window, "bitmap"), fixture), "Undo did not restore source");
                Field<Button>(window, "redoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!ReferenceEquals(Field<BitmapSource>(window, "bitmap"), fixture), "Redo did not restore removed background");
                Field<Button>(window, "undoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var pending = window.RemoveBackgroundAsync(); Field<Button>(window, "cancelRemoval").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await pending;
                Check(ReferenceEquals(Field<BitmapSource>(window, "bitmap"), fixture) && Field<Button>(window, "removeBackground").IsEnabled, "Cancel changed image or stranded controls");
            }
            window.UpdateLayout(); var screenshot = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); screenshot.Render(window); Save(screenshot, "background-ui-" + language + ".png");
            var closing = window.RemoveBackgroundAsync(); window.Close(); await closing;
            Check(Field<BitmapSource?>(window, "bitmap") == null, "Close retained image");
        }
        L.Use("en");
        // A completed cancellation must release the global gate for subsequent requests.
        await BackgroundRemovalService.RemoveAsync(fixture);
    }
}
