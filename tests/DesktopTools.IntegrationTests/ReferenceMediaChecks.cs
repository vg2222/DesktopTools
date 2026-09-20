using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Extras;
using DesktopTools.Localization;

internal static class ReferenceMediaChecks
{
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static System.Collections.Generic.IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
    private static void Render(Window window, string name)
    {
        window.UpdateLayout();
        var buttons = Children(window).OfType<Button>().Where(b => b.IsVisible && b.ActualWidth > 0).ToArray();
        var bounds = buttons.Select(b => b.TransformToAncestor(window).TransformBounds(new Rect(b.RenderSize))).ToArray();
        for (int i = 0; i < bounds.Length; i++)
        {
            if (bounds[i].Right > window.ActualWidth + 1 || bounds[i].Bottom > window.ActualHeight + 1) throw new InvalidOperationException(name + ": button escapes window");
            for (int j = i + 1; j < bounds.Length; j++)
            {
                var overlap = Rect.Intersect(bounds[i], bounds[j]);
                if (!overlap.IsEmpty && overlap.Width > 1 && overlap.Height > 1) throw new InvalidOperationException(name + ": overlapping controls");
            }
        }
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var output = File.Create(name + ".png"); encoder.Save(output);
    }
    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        var pixels = Enumerable.Repeat((byte)245, 640 * 360 * 4).ToArray(); for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var sample = BitmapSource.Create(640, 360, 96, 96, PixelFormats.Bgra32, null, pixels, 640 * 4); sample.Freeze();
        foreach (var theme in new[] { "Dark", "Light" })
        {
            controller.Settings.Theme = theme; controller.Settings.Transparency = true; controller.ApplyTheme();
            foreach (var language in new[] { "ru", "en", "de", "fr", "es" })
            {
                L.Use(language);
                var qr = new QrCodeWindow(_ => { }) { Width = 680 }; qr.Show(); Field<TextBox>(qr, "input").Text = "https://example.com/Пример"; await Task.Delay(350);
                if (Field<Image>(qr, "preview").Source is not BitmapSource) throw new InvalidOperationException("QR did not automatically generate after text change");
                Render(qr, $"reference-qr-{theme}-{language}");
                Field<TextBox>(qr, "input").Clear(); if (Field<Button>(qr, "save").IsEnabled) throw new InvalidOperationException("QR retained stale export after clearing text"); qr.Close();
                foreach (var variant in new[] { "A", "B" })
                {
                    var editor = new ScreenshotEditorWindow(sample, _ => { }, _ => { }, editorLayout: variant) { Width = 860, Height = 580 }; editor.Show(); await Task.Delay(40); Render(editor, $"reference-screenshot-{variant}-{theme}-{language}"); editor.Close();
                }
                var color = new SampledColorWindow(Colors.DodgerBlue, _ => { }, sample, () => { }); color.Show(); await Task.Delay(40); Render(color, $"reference-color-{theme}-{language}"); color.Close();
            }
        }
    }
}
