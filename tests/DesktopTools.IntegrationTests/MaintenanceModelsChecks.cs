using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopTools;
using DesktopTools.Extras;
using DesktopTools.Localization;

internal static class MaintenanceModelsChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
    private static byte[] Pixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; converted.CopyPixels(pixels, image.PixelWidth * 4, 0); return pixels;
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    internal static async Task RunUiAsync()
    {
        using var controller = new AppController(true);
        L.Use("en"); controller.UpdateSettings(s => s.Animations = false);
        byte[] pixels = [1, 2, 3, 255, 4, 5, 6, 128, 7, 8, 9, 0, 10, 11, 12, 255, 13, 14, 15, 128, 16, 17, 18, 255];
        var image = BitmapSource.Create(3, 2, 144, 144, PixelFormats.Bgra32, null, pixels, 12); image.Freeze();
        var window = new ImageToolsWindow(_ => { });
        try
        {
            window.LoadImage("maintenance-fixture.png", image); window.Show(); window.UpdateLayout();
            var left = Walk(window).OfType<Button>().Single(button => AutomationProperties.GetName(button) == L.T("Rotate left"));
            left.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var rotated = window.CurrentBitmap!;
            byte[] expected = [7, 8, 9, 0, 16, 17, 18, 255, 4, 5, 6, 128, 13, 14, 15, 128, 1, 2, 3, 255, 10, 11, 12, 255];
            Check(rotated.PixelWidth == 2 && rotated.PixelHeight == 3 && rotated.IsFrozen && rotated.DpiX == 144 && rotated.DpiY == 144,
                "Left rotation changed dimensions, DPI or freeze contract");
            Check(Pixels(rotated).SequenceEqual(expected), "Left rotation changed pixel order or alpha");
            Check(Pixels(image).SequenceEqual(pixels), "Left rotation altered the source image");
            Field<Button>(window, "undoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(ReferenceEquals(window.CurrentBitmap, image), "Left rotation undo did not restore the original");
            Field<Button>(window, "redoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(ReferenceEquals(window.CurrentBitmap, rotated), "Left rotation redo did not restore the edited image");
        }
        finally { window.Close(); }

        var view = new ScreenshotEditorView(image, _ => { }, _ => { });
        try
        {
            Call(view, "SchedulePreview");
            var timer = Field<DispatcherTimer>(view, "_previewTimer");
            Check(timer.IsEnabled, "Style preview was not scheduled");
            view.Dispose();
            Check(!timer.IsEnabled, "Disposed editor still has a pending style render");
            await Task.Delay(180);
            Check(Field<Image>(view, "_stylePreview").Source == null, "Disposed editor rendered or retained a style preview");
            Call(view, "SchedulePreview");
            await Task.Delay(180);
            Check(!timer.IsEnabled && Field<Image>(view, "_stylePreview").Source == null, "Disposed editor scheduled another style render");
            view.Dispose(); // Hosts may dispose again after a manual close.
        }
        finally { if (!Field<bool>(view, "_reviewClosed")) view.Dispose(); }
    }

    internal static async Task RunTranslationAsync()
    {
        string english = "Hello world! Open the window. The computer is on the table.";
        Check(await LocalTranslation.TranslateAsync(english, "en-ru") == "Приветствую мир! Открой окно. Компьютер на столе.",
            "Reusing translation sessions changed the baseline three-sentence output");
        foreach (var (direction, sentences) in new[]
        {
            ("en-ru", new[] { "Hello world!", "Open the window.", "The computer is on the table." }),
            ("ru-en", new[] { "Привет, мир!", "Открой окно.", "Компьютер на столе." })
        })
        {
            var separate = new string[sentences.Length];
            for (int i = 0; i < sentences.Length; i++) separate[i] = await LocalTranslation.TranslateAsync(sentences[i], direction);
            string together = await LocalTranslation.TranslateAsync(string.Join("\n\n", sentences), direction);
            Check(together == string.Join("\n\n", separate), direction + " leaked sentence decoder state or lost paragraph separators");
            Check(await LocalTranslation.TranslateAsync(sentences[0], direction) == separate[0], direction + " leaked state across requests");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            var pending = LocalTranslation.TranslateAsync(english, "en-ru", cancellation.Token);
            await Task.Delay(100); cancellation.Cancel();
            try { await pending; throw new Exception("Translation ignored cancellation"); } catch (OperationCanceledException) { }
        }
        Check(await LocalTranslation.TranslateAsync("Hello world!", "en-ru") == "Приветствую мир!", "Canceled request stranded translation resources or gate");
        try { await LocalTranslation.TranslateAsync(string.Join(" ", Enumerable.Repeat("Hello!", 33)), "en-ru"); throw new Exception("Sentence limit ignored"); }
        catch (ArgumentException) { }
        // The first sentence creates both sessions; rejection of the second must still dispose the stage.
        try { await LocalTranslation.TranslateAsync("Hello world!\n" + string.Join(" ", Enumerable.Repeat("hello", 300)), "en-ru"); throw new Exception("Token limit ignored"); }
        catch (ArgumentException) { }
        Check(await LocalTranslation.TranslateAsync("Hello world!", "en-ru") == "Приветствую мир!", "Rejected passage stranded translation resources or gate");
    }
}
