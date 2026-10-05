using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Extras;

internal static class BeautifierTests
{
    private static byte[] Pixel(BitmapSource image, int x, int y)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var buffer = new byte[4]; converted.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), buffer, 4, 0); return buffer; // B, G, R, A
    }
    public static void Run()
    {
        void Check(bool value, string what) { if (!value) throw new Exception("Beautifier assertion failed: " + what); }
        var pixels = new byte[200 * 120 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 0; pixels[i + 1] = 0; pixels[i + 2] = 255; pixels[i + 3] = 255; } // solid red
        var source = BitmapSource.Create(200, 120, 96, 96, PixelFormats.Bgra32, null, pixels, 200 * 4); source.Freeze();

        Check(ReferenceEquals(ScreenshotBeautifier.Apply(source, new BeautifyOptions(false)), source), "disabled returns the original untouched");

        var dressed = ScreenshotBeautifier.Apply(source, new BeautifyOptions(true, "Ocean", 0.1, 0.05, true));
        Check(dressed.PixelWidth > 200 && dressed.PixelHeight > 120, "background adds room around the image");
        int margin = (dressed.PixelWidth - 200) / 2;
        var middle = Pixel(dressed, dressed.PixelWidth / 2, dressed.PixelHeight / 2);
        Check(middle[2] > 240 && middle[1] < 15 && middle[0] < 15, "screenshot pixels are copied unchanged");
        var corner = Pixel(dressed, 1, 1);
        Check(corner[3] == 255 && !(corner[2] > 200 && corner[1] < 50), "outer corner shows the background, not the screenshot");
        var cardCorner = Pixel(dressed, margin + 1, margin + 1);
        Check(!(cardCorner[2] > 200 && cardCorner[1] < 50 && cardCorner[0] < 50), "rounded corners cut the screenshot's own corner");
        Check(dressed.PixelWidth - 200 == dressed.PixelHeight - 120, "equal room on every side");

        var bar = ScreenshotBeautifier.Apply(source, new BeautifyOptions(true, "Paper", 0.05, 0, false, true));
        var plain = ScreenshotBeautifier.Apply(source, new BeautifyOptions(true, "Paper", 0.05, 0, false, false));
        Check(bar.PixelHeight > plain.PixelHeight && bar.PixelWidth == plain.PixelWidth, "window bar adds height only");

        var transparent = ScreenshotBeautifier.Apply(source, new BeautifyOptions(true, "None", 0.1, 0, false));
        Check(Pixel(transparent, 1, 1)[3] == 0, "no background stays transparent for PNG");
        var flat = ScreenshotBeautifier.Apply(source, new BeautifyOptions(true, "Ocean", 0, 0, false));
        Check(flat.PixelWidth == 200 && flat.PixelHeight == 120, "zero padding without shadow keeps the size");
        var shadowed = ScreenshotBeautifier.Apply(source, new BeautifyOptions(true, "Paper", 0, 0, true));
        Check(shadowed.PixelWidth > 200, "a shadow always gets room so it is not cut off");
    }
}
