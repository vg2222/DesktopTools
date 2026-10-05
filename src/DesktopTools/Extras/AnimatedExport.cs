using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Core;
using DesktopTools.Localization;
using Windows.Media.Editing;
using Windows.Storage;

namespace DesktopTools.Extras;

public enum AnimatedFormat { Gif, WebP }

/// <param name="MaxWidth">Output is scaled down to at most this width; 0 keeps the edited video's width.</param>
public sealed record AnimatedOptions(AnimatedFormat Format, int FramesPerSecond = 15, int MaxWidth = 640, int WebpQuality = 75);

/// <summary>Turns the edited video (trim, cuts, crop, rotation, size) into an animated GIF or WebP file. The source video is never modified.</summary>
public static class AnimatedExport
{
    private const long MaximumFrameBytes = 640L * 1024 * 1024;

    public static string Extension(AnimatedFormat format) => format == AnimatedFormat.Gif ? ".gif" : ".webp";

    /// <summary>Frame size after the edit and the width limit, always even so encoders are happy.</summary>
    public static (int Width, int Height) FrameSize(VideoInfo source, VideoEdit edit, AnimatedOptions options)
    {
        var (width, height) = edit.OutputSize(source);
        if (options.MaxWidth > 0 && width > options.MaxWidth) { height = (int)Math.Round(height * (double)options.MaxWidth / width); width = options.MaxWidth; }
        return (Math.Max(2, width & ~1), Math.Max(2, height & ~1));
    }
    public static int FrameCount(VideoInfo source, VideoEdit edit, AnimatedOptions options) => Math.Max(1, (int)Math.Floor(edit.OutputDuration(source) * options.FramesPerSecond));

    public static async Task ExportAsync(VideoInfo source, VideoEdit edit, string output, AnimatedOptions options, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        edit.Validate(source);
        output = Path.GetFullPath(output);
        if (!string.Equals(Path.GetExtension(output), Extension(options.Format), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(L.F($"Save the animation as a {Extension(options.Format)} file."));
        if (File.Exists(output)) throw new IOException(L.T("Choose a new filename. Existing files are not overwritten."));
        var (width, height) = FrameSize(source, edit, options);
        int count = FrameCount(source, edit, options);
        if ((long)count * width * height * 4 > MaximumFrameBytes)
            throw new InvalidOperationException(L.T("This animation would be too large to build. Shorten the clip, lower the frame rate or choose a smaller width."));

        string intermediate = Path.Combine(Path.GetTempPath(), "DesktopTools-animation-" + Guid.NewGuid().ToString("N") + ".mp4");
        string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            // Stage 1: reuse the normal edit pipeline so trim, cuts, crop and rotation behave exactly as in the MP4 export.
            await VideoEditorService.ExportAsync(source, edit with { Mute = true }, intermediate, new Progress<double>(p => progress?.Report(p * .4)), cancellationToken);
            var edited = await VideoEditorService.ProbeAsync(intermediate, cancellationToken);
            // Stage 2: sample frames.
            var frames = new List<byte[]>(count);
            var composition = new MediaComposition();
            try
            {
                composition.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(intermediate).AsTask(cancellationToken)).AsTask(cancellationToken));
                for (int i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double at = Math.Min(i / (double)options.FramesPerSecond, Math.Max(0, edited.Duration - .001));
                    frames.Add(await ReadFrameAsync(composition, at, width, height, cancellationToken));
                    progress?.Report(40 + 50.0 * (i + 1) / count);
                }
            }
            finally { composition.Clips.Clear(); }
            // Stage 3: encode.
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                using var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                Encode(file, frames, width, height, options);
            }, cancellationToken);
            File.Move(temporary, output, false);
            progress?.Report(100);
        }
        finally
        {
            foreach (string path in new[] { intermediate, temporary }) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
        }
    }

    internal static void Encode(Stream output, IReadOnlyList<byte[]> frames, int width, int height, AnimatedOptions options)
    {
        if (options.Format == AnimatedFormat.Gif) GifEncoder.Write(output, frames, width, height, options.FramesPerSecond);
        else AnimatedWebp.Write(output, frames, width, height, options.FramesPerSecond, options.WebpQuality);
    }

    private static async Task<byte[]> ReadFrameAsync(MediaComposition composition, double seconds, int width, int height, CancellationToken cancellationToken)
    {
        using var thumbnail = await composition.GetThumbnailAsync(TimeSpan.FromSeconds(seconds), width, height, VideoFramePrecision.NearestFrame).AsTask(cancellationToken);
        using var stream = thumbnail.AsStreamForRead();
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames[0];
        if (frame.PixelWidth != width || frame.PixelHeight != height) frame = new TransformedBitmap(frame, new ScaleTransform((double)width / frame.PixelWidth, (double)height / frame.PixelHeight));
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    /// <summary>
    /// Rough size of the finished file: a few frames are encoded on their own and scaled up to the full length. GIF stores only what changes
    /// between frames, so for screen recordings the real file is usually smaller than this; treat it as an upper estimate.
    /// </summary>
    public static async Task<long> EstimateBytesAsync(VideoInfo source, VideoEdit edit, AnimatedOptions options, CancellationToken cancellationToken = default)
    {
        var (width, height) = FrameSize(source, edit, options);
        int count = FrameCount(source, edit, options);
        var composition = new MediaComposition();
        long total = 0; int samples = 0;
        try
        {
            composition.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(source.Path)).AsTask(cancellationToken)).AsTask(cancellationToken));
            var range = edit.Segments(source).First();
            for (int i = 0; i < 4; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double at = Math.Clamp(range.Start + (range.End - range.Start) * (i + .5) / 4, 0, Math.Max(0, source.Duration - .001));
                var frame = await ReadFrameAsync(composition, at, width, height, cancellationToken);
                using var stream = new MemoryStream();
                await Task.Run(() => Encode(stream, [frame], width, height, options with { FramesPerSecond = 10 }), cancellationToken);
                total += stream.Length; samples++;
            }
        }
        finally { composition.Clips.Clear(); }
        double perFrame = total / (double)Math.Max(1, samples);
        // Typical savings from storing only changes between frames: GIF keeps roughly a third for screen content, WebP frames are stored whole.
        return (long)(perFrame * count * (options.Format == AnimatedFormat.Gif ? .4 : 1));
    }
}
