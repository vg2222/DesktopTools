using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopTools.Extras;

public static class ScreenshotPixel
{
    public static Color Read(BitmapSource image, int x, int y)
    {
        if (x < 0 || y < 0 || x >= image.PixelWidth || y >= image.PixelHeight) throw new ArgumentOutOfRangeException(nameof(x));
        BitmapSource converted = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4]; converted.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
}
