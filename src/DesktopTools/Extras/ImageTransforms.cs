using DesktopTools.Localization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopTools.Extras;

internal static class ImageTransforms
{
    internal const int MaxDimension = 12000;
    internal const long MaxPixels = 24_000_000;
    internal static void ValidateSize(int width, int height)
    {
        if (width < 1 || height < 1 || width > MaxDimension || height > MaxDimension || (long)width * height > MaxPixels)
            throw new ArgumentException(L.T("Use dimensions from 1 to 12,000 pixels, up to 24 megapixels total."));
    }
    internal static BitmapSource Load(string path)
    {
        if (new FileInfo(path).Length > 100_000_000) throw new ArgumentException(L.T("Choose an image smaller than 100 MB."));
        using var stream = File.OpenRead(path);
        // Inspect dimensions before allocating a decoded pixel buffer.
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        if (decoder is not PngBitmapDecoder and not JpegBitmapDecoder and not BmpBitmapDecoder)
            throw new ArgumentException(L.T("Choose a PNG, JPEG, or BMP image."));
        var frame = decoder.Frames[0]; ValidateSize(frame.PixelWidth, frame.PixelHeight);
        stream.Position = 0;
        var loaded = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        loaded.Freeze(); return loaded;
    }
    internal static BitmapSource Rotate(BitmapSource source) => Transform(source, new RotateTransform(90));
    internal static BitmapSource Mirror(BitmapSource source, bool horizontal) => Transform(source, new ScaleTransform(horizontal ? -1 : 1, horizontal ? 1 : -1));
    private static BitmapSource Transform(BitmapSource source, Transform transform)
    {
        var result = new TransformedBitmap(source, transform); result.Freeze();
        // Materialize to avoid retaining an unlimited chain of edits.
        var copy = new WriteableBitmap(result); copy.Freeze(); return copy;
    }
    internal static BitmapSource Resize(BitmapSource source, int width, int height)
    {
        ValidateSize(width, height);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen()) dc.DrawImage(source, PixelBounds(width, height, source.DpiX, source.DpiY));
        var result = new RenderTargetBitmap(width, height, source.DpiX, source.DpiY, PixelFormats.Pbgra32); result.Render(visual); result.Freeze(); return result;
    }
    internal static BitmapSource Flatten(BitmapSource source)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var bounds = PixelBounds(source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY);
            dc.DrawRectangle(Brushes.White, null, bounds); dc.DrawImage(source, bounds);
        }
        var result = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY, PixelFormats.Pbgra32);
        result.Render(visual); result.Freeze(); return result;
    }
    private static Rect PixelBounds(int width, int height, double dpiX, double dpiY) => new(0, 0, width * 96d / dpiX, height * 96d / dpiY);
    internal static BitmapSource Crop(BitmapSource source, Int32Rect area)
    {
        if (area.X < 0 || area.Y < 0 || area.Width < 1 || area.Height < 1 || (long)area.X + area.Width > source.PixelWidth || (long)area.Y + area.Height > source.PixelHeight) throw new ArgumentException(L.T("Crop must fit inside the image."));
        var image = new WriteableBitmap(new CroppedBitmap(source, area)); image.Freeze(); return image;
    }
    internal static byte[] Encode(BitmapSource source, string format, int quality = 92)
    {
        if (quality < 1 || quality > 100) throw new ArgumentOutOfRangeException(nameof(quality));
        BitmapEncoder encoder = format.ToLowerInvariant().TrimStart('.') switch
        {
            "png" => new PngBitmapEncoder(), "jpg" or "jpeg" => new JpegBitmapEncoder { QualityLevel = quality },
            "bmp" => new BmpBitmapEncoder(), _ => throw new ArgumentException(L.T("Choose PNG, JPEG or BMP."))
        };
        encoder.Frames.Add(BitmapFrame.Create(encoder is JpegBitmapEncoder ? Flatten(source) : source));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    internal static void Export(BitmapSource source, string path, string original, int quality = 92)
    {
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(original), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(L.T("Choose a different filename to keep the original image safe."));
        byte[] bytes = Encode(source, Path.GetExtension(path), quality);
        string temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, ".desktoptools-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
