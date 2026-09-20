using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopTools.Extras;

public static class RedactionRenderer
{
    /// <summary>Flatten solid covers into image pixels. Input and its metadata are never edited.</summary>
    public static BitmapSource Apply(BitmapSource image, IReadOnlyList<Int32Rect> covers)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(covers);
        BitmapSource converted = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int stride = checked(converted.PixelWidth * 4);
        var pixels = new byte[checked(stride * converted.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        foreach (var cover in covers)
        {
            int left = Math.Clamp(cover.X, 0, image.PixelWidth), top = Math.Clamp(cover.Y, 0, image.PixelHeight);
            int right = (int)Math.Clamp((long)cover.X + cover.Width, 0, image.PixelWidth);
            int bottom = (int)Math.Clamp((long)cover.Y + cover.Height, 0, image.PixelHeight);
            for (int y = top; y < bottom; y++)
                for (int x = left; x < right; x++)
                {
                    int offset = y * stride + x * 4;
                    pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 0;
                    pixels[offset + 3] = 255;
                }
        }
        var result = BitmapSource.Create(image.PixelWidth, image.PixelHeight, image.DpiX, image.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }
}
