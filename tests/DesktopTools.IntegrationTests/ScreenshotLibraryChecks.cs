using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Extras;

/// <summary>Real Windows OCR end to end: a screenshot with words on it is stored, found by those words, previewed and removable.</summary>
internal static class ScreenshotLibraryChecks
{
    private static BitmapSource Page(string title, string line)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1200, 500));
            FormattedText Text(string value, double size) => new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, Brushes.Black, 1);
            dc.DrawText(Text(title, 64), new Point(60, 80)); dc.DrawText(Text(line, 40), new Point(60, 240));
        }
        var bitmap = new RenderTargetBitmap(1200, 500, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    internal static async Task RunAsync()
    {
        void Check(bool value, string what) { if (!value) throw new Exception("Screenshot library check failed: " + what); }
        string directory = Path.GetFullPath("library-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            var library = new ScreenshotLibrary(directory);
            var language = DesktopTools.Extras.LocalOcr.Languages.FirstOrDefault(l => l.Tag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            Check(language != null, "an English Windows OCR language is needed for this check");
            await library.AddAsync(Page("Invoice 4471", "Payment due for Contoso Hardware"), language!.Tag, 10);
            await library.AddAsync(Page("Holiday plans", "Flights to Lisbon in March"), language.Tag, 10);
            Check(library.Index.Count == 2, "both screenshots stored");
            var hit = library.Index.Search("contoso hardware");
            Check(hit.Count == 1 && hit[0].Text.Contains("Invoice", StringComparison.OrdinalIgnoreCase), "found by words printed on it: " + string.Join(" | ", library.Index.All.Select(e => e.Text.Replace('\n', ' '))));
            Check(library.Index.Search("lisbon").Count == 1 && library.Index.Search("lisbon contoso").Count == 0, "search is per screenshot");
            var stored = ScreenshotLibrary.LoadImage(library.Index.ImagePath(hit[0].Id));
            Check(stored.PixelWidth == 1200 && stored.PixelHeight == 500, "full-size PNG kept");
            Check(ScreenshotLibrary.LoadImage(library.Index.ThumbnailPath(hit[0].Id)).PixelWidth <= 360, "small preview kept");
            // the window renders its cards from the stored previews
            using var controller = new AppController(true);
            var window = new ScreenshotLibraryWindow(controller, library); window.Show(); await Task.Delay(500);
            Check(window.IsVisible, "library window opens");
            window.Close();
            library.Clear();
            Check(library.Index.Count == 0 && !Directory.EnumerateFiles(Path.Combine(directory, "images")).Any(), "clear removes the images");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
