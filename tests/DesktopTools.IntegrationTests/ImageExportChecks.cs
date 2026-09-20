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

internal static class ImageExportChecks
{
    private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static byte[] Pixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0); return pixels;
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        var directory = Path.GetFullPath("image-export-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var pixels = new byte[320 * 200 * 4]; for (int y = 0; y < 200; y++) for (int x = 0; x < 320; x++) { int i = (y * 320 + x) * 4; pixels[i] = (byte)(x * .7); pixels[i + 1] = (byte)y; pixels[i + 2] = 220; pixels[i + 3] = x < 160 ? (byte)255 : (byte)0; }
        var bitmap = BitmapSource.Create(320, 200, 96, 96, PixelFormats.Bgra32, null, pixels, 1280); bitmap.Freeze();
        string original = Path.Combine(directory, "original.png"); File.WriteAllBytes(original, ImageTransforms.Encode(bitmap, "PNG")); var bytes = File.ReadAllBytes(original);
        foreach (var theme in new[] { "Dark", "Light" }) foreach (var language in new[] { "ru", "en", "de", "fr", "es" })
        {
            controller.Settings.Theme = theme; controller.ApplyTheme(); L.Use(language);
            var window = new ImageExportDialog(bitmap, original, "JPEG", choosePath: _ => null); window.Show(); await Task.Delay(40); window.UpdateLayout();
            var rendered = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); rendered.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(rendered)); using (var stream = File.Create($"image-export-{language}-{theme}.png")) encoder.Save(stream);
            Field<Button>(window, "save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(window.IsVisible && window.SavedPath == null, "Cancelled destination closed export or saved"); window.Close();
        }
        L.Use("en");
        foreach (var format in new[] { "PNG", "JPEG", "BMP" })
        {
            string output = Path.Combine(directory, "half" + (format == "JPEG" ? ".jpg" : "." + format.ToLowerInvariant()));
            var window = new ImageExportDialog(bitmap, original, format, choosePath: request => { Check(request.Width == 160 && request.Height == 100 && request.Format == format, "Export request parameters differ from controls"); return output; });
            window.Show(); await Task.Delay(40); Children(window).OfType<ComboBox>().Single(c => c.Items.Contains("50%")).SelectedItem = "50%";
            Check(Field<FrameworkElement>(window, "qualityRow").IsVisible == (format == "JPEG"), "Quality shown for an inapplicable format");
            if (format == "JPEG")
            {
                var quality = Field<Slider>(window, "quality"); quality.Value = 10; var lowQuality = Pixels((BitmapSource)Field<Image>(window, "preview").Source);
                quality.Value = 100; var highQuality = Pixels((BitmapSource)Field<Image>(window, "preview").Source);
                Check(!lowQuality.SequenceEqual(highQuality), "JPEG quality did not update the decoded export preview");
            }
            var closed = new TaskCompletionSource(); window.Closed += (_, _) => closed.TrySetResult(); Field<Button>(window, "save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Check(window.SavedPath == output && File.Exists(output), "Export did not save selected destination");
            var decoded = ImageTransforms.Load(output); Check(decoded.PixelWidth == 160 && decoded.PixelHeight == 100, "Export dimensions not applied");
            if (format == "PNG") { var converted = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0); var sample = new byte[4]; converted.CopyPixels(new Int32Rect(140, 50, 1, 1), sample, 4, 0); Check(sample[3] == 0, "PNG lost transparency"); }
            if (format == "JPEG") { var converted = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0); var sample = new byte[4]; converted.CopyPixels(new Int32Rect(140, 50, 1, 1), sample, 4, 0); Check(sample[0] > 245 && sample[1] > 245 && sample[2] > 245, "JPEG did not flatten transparency onto white"); }
        }
        foreach (var destination in new[] { original, Path.Combine(directory, "wrong.jpg") })
        {
            var rejected = new ImageExportDialog(bitmap, original, choosePath: _ => destination); rejected.Show(); await Task.Delay(40); Field<Button>(rejected, "save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(250);
            Check(rejected.IsVisible && rejected.SavedPath == null && Field<TextBlock>(rejected, "status").Text.Length > 0, "Unsafe or mismatched destination accepted"); rejected.Close();
        }
        Check(File.ReadAllBytes(original).SequenceEqual(bytes) && !File.Exists(Path.Combine(directory, "wrong.jpg")), "Original or wrong-extension output changed");
        var custom = new ImageExportDialog(bitmap, original); custom.Show(); await Task.Delay(40); Children(custom).OfType<ComboBox>().Single(c => c.Items.Contains("Custom size")).SelectedItem = "Custom size";
        Field<TextBox>(custom, "customWidth").Text = "12001"; Check(!Field<Button>(custom, "save").IsEnabled, "Oversized custom export accepted");
        Field<TextBox>(custom, "customWidth").Text = "100"; Field<TextBox>(custom, "customHeight").Text = "200"; var customPreview = (BitmapSource)Field<Image>(custom, "preview").Source; Check(customPreview.PixelWidth == 100 && customPreview.PixelHeight == 200, "Custom aspect is not reflected in preview"); custom.Close();
    }
}
