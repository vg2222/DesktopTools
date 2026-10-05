using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Core;

namespace DesktopTools.Extras;

/// <summary>
/// Opt-in, on-device screenshot library: keeps a PNG, a small preview and the text Windows OCR finds in it, so a screenshot can be found by
/// what is written on it. Only screenshots that completed their normal output (after any privacy review) are added.
/// </summary>
internal sealed class ScreenshotLibrary
{
    private readonly SemaphoreSlim queue = new(1, 1);
    internal ScreenshotLibraryIndex Index { get; }
    internal event Action? Changed;

    internal ScreenshotLibrary(string directory) => Index = new ScreenshotLibraryIndex(directory);

    internal Task AddAsync(BitmapSource image, string? ocrLanguageTag, int limit)
    {
        var frozen = image.IsFrozen ? image : image.CloneCurrentValue();
        if (!frozen.IsFrozen) frozen.Freeze();
        return Task.Run(async () =>
        {
            await queue.WaitAsync().ConfigureAwait(false);
            try
            {
                var id = Guid.NewGuid();
                Directory.CreateDirectory(Path.GetDirectoryName(Index.ImagePath(id))!); Directory.CreateDirectory(Path.GetDirectoryName(Index.ThumbnailPath(id))!);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(frozen));
                using (var stream = File.Create(Index.ImagePath(id))) png.Save(stream);
                double scale = Math.Min(1, 360.0 / frozen.PixelWidth);
                BitmapSource small = scale < 1 ? new TransformedBitmap(frozen, new ScaleTransform(scale, scale)) : frozen;
                var jpeg = new JpegBitmapEncoder { QualityLevel = 85 }; jpeg.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(small, PixelFormats.Bgr24, null, 0)));
                using (var stream = File.Create(Index.ThumbnailPath(id))) jpeg.Save(stream);
                string text = "";
                if (!string.IsNullOrEmpty(ocrLanguageTag))
                {
                    // A screenshot without readable text, or a machine without an OCR language, is still kept; it just has nothing to search.
                    try { text = (await LocalOcr.RecognizeAsync(frozen, ocrLanguageTag).ConfigureAwait(false)).Trim(); }
                    catch (Exception ex) when (ex is not OutOfMemoryException) { text = ""; }
                }
                Index.Add(new LibraryEntry(id, DateTimeOffset.Now, frozen.PixelWidth, frozen.PixelHeight, text.Length > 20000 ? text[..20000] : text), limit);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(ex); }
            finally { queue.Release(); }
            Changed?.Invoke();
        });
    }

    internal static BitmapSource LoadImage(string path, int? decodeWidth = null)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.UriSource = new Uri(path); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (decodeWidth is int width) bitmap.DecodePixelWidth = width;
        bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }

    internal void Clear() { Index.Clear(); Changed?.Invoke(); }
    internal void Remove(Guid id) { Index.Remove(id); Changed?.Invoke(); }
}
