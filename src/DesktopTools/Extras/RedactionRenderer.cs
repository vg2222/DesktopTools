using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Core;

namespace DesktopTools.Extras;

public static class RedactionRenderer
{
    public static BitmapSource Apply(BitmapSource image, IReadOnlyList<RedactionRegion> regions)
    {
        BitmapSource converted = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int stride = checked(image.PixelWidth * 4); var pixels = new byte[checked(stride * image.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        foreach (var region in regions.OrderBy(r => r.Style == RedactionStyle.Solid ? 1 : 0))
        {
            var b = region.Bounds;
            int left = Math.Clamp(b.X, 0, image.PixelWidth), top = Math.Clamp(b.Y, 0, image.PixelHeight);
            int right = (int)Math.Clamp((long)b.X + b.Width, 0, image.PixelWidth), bottom = (int)Math.Clamp((long)b.Y + b.Height, 0, image.PixelHeight);
            int width = right - left, height = bottom - top; if (width <= 0 || height <= 0) continue;
            if (region.Style == RedactionStyle.Solid)
            {
                for (int y = top; y < bottom; y++) for (int x = left; x < right; x++)
                { int i = y * stride + x * 4; pixels[i] = pixels[i + 1] = pixels[i + 2] = 0; pixels[i + 3] = 255; }
                continue;
            }
            var area = new byte[checked(width * height * 4)];
            for (int y = 0; y < height; y++) Array.Copy(pixels, (top + y) * stride + left * 4, area, y * width * 4, width * 4);
            if (region.Style == RedactionStyle.Pixelate)
            {
                for (int y = 0; y < height; y += 12) for (int x = 0; x < width; x += 12)
                {
                    int endX = Math.Min(x + 12, width), endY = Math.Min(y + 12, height), count = (endX - x) * (endY - y);
                    for (int c = 0; c < 4; c++)
                    {
                        int sum = 0; for (int yy = y; yy < endY; yy++) for (int xx = x; xx < endX; xx++) sum += area[(yy * width + xx) * 4 + c];
                        for (int yy = y; yy < endY; yy++) for (int xx = x; xx < endX; xx++) area[(yy * width + xx) * 4 + c] = (byte)(sum / count);
                    }
                }
            }
            else area = Blur(Blur(area, width, height, true), width, height, false);
            for (int y = 0; y < height; y++) Array.Copy(area, y * width * 4, pixels, (top + y) * stride + left * 4, width * 4);
        }
        var result = BitmapSource.Create(image.PixelWidth, image.PixelHeight, image.DpiX, image.DpiY, PixelFormats.Bgra32, null, pixels, stride); result.Freeze(); return result;
    }

    private static byte[] Blur(byte[] source, int width, int height, bool horizontal)
    {
        var result = new byte[source.Length]; int length = horizontal ? width : height, lines = horizontal ? height : width;
        for (int line = 0; line < lines; line++) for (int channel = 0; channel < 4; channel++)
        {
            int Index(int position) => ((horizontal ? line * width + position : position * width + line) * 4) + channel;
            int count = Math.Min(9, length), sum = 0; for (int i = 0; i < count; i++) sum += source[Index(i)];
            for (int position = 0; position < length; position++)
            {
                result[Index(position)] = (byte)(sum / count);
                int remove = position - 8, add = position + 9;
                if (remove >= 0) { sum -= source[Index(remove)]; count--; }
                if (add < length) { sum += source[Index(add)]; count++; }
            }
        }
        return result;
    }
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
