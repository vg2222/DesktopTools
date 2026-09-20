using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Extras;

internal static class Program
{
    [STAThread]
    static int Main()
    {
        QrChecks.Run();
        var pixels = new byte[] { 1, 2, 3, 255, 44, 55, 66, 255 };
        var image = BitmapSource.Create(2, 1, 144, 144, PixelFormats.Bgra32, null, pixels, 8);
        var color = ScreenshotPixel.Read(image, 1, 0);
        if (color != Color.FromRgb(66, 55, 44)) throw new Exception("Pixel picker must preserve exact image coordinates and channels.");
        try { ScreenshotPixel.Read(image, 2, 0); throw new Exception("Expected out-of-image rejection."); } catch (ArgumentOutOfRangeException) { }
        Console.WriteLine("PASS exact pixel sampling and bounds");
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 600, 120));
            dc.DrawText(new FormattedText("DESKTOP TOOLS 123", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Arial"), 40, Brushes.Black, 1), new Point(20, 30));
        }
        var sample = new RenderTargetBitmap(600, 120, 96, 96, PixelFormats.Pbgra32); sample.Render(visual); sample.Freeze();
        var language = LocalOcr.Languages.FirstOrDefault(l => l.Tag.StartsWith("en"));
        if (language == null) { Console.WriteLine("SKIP English OCR: no installed English recognizer"); return 0; }
        var result = Task.Run(() => LocalOcr.RecognizeAsync(sample, language.Tag)).GetAwaiter().GetResult();
        if (!result.Contains("DESKTOP") || !result.Contains("123")) throw new Exception("OCR missed generated sample: " + result);
        Console.WriteLine("PASS local Windows OCR recognizes generated image: " + result);
        return 0;
    }
}
