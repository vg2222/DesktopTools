using DesktopTools.Core;
using DesktopTools.Localization;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Media.Editing;
using Windows.Media.Effects;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace DesktopTools.Extras;

public static class VideoEditorService
{
    internal static async Task<IReadOnlyList<System.Windows.Media.Imaging.BitmapSource>> ThumbnailsAsync(VideoInfo source, CancellationToken cancellationToken)
    {
        var composition = new MediaComposition();
        var images = new List<System.Windows.Media.Imaging.BitmapSource>();
        double scale = Math.Min(128d / source.Width, 96d / source.Height);
        int width = Math.Clamp((int)Math.Round(source.Width * scale), 1, 128);
        int height = Math.Clamp((int)Math.Round(source.Height * scale), 1, 96);
        try
        {
            composition.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(source.Path).AsTask(cancellationToken)).AsTask(cancellationToken));
            for (int i = 0; i < 10; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var thumbnail = await composition.GetThumbnailAsync(TimeSpan.FromSeconds(source.Duration * (i + .5) / 10), width, height, VideoFramePrecision.NearestFrame).AsTask(cancellationToken);
                using var stream = thumbnail.AsStreamForRead();
                var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0]; frame.Freeze(); images.Add(frame);
            }
            return images;
        }
        finally { composition.Clips.Clear(); }
    }
    public static async Task<VideoInfo> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        var file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellationToken);
        var clip = await MediaClip.CreateFromFileAsync(file).AsTask(cancellationToken);
        var properties = clip.GetVideoEncodingProperties();
        var info = new VideoInfo(path, clip.OriginalDuration.TotalSeconds, checked((int)properties.Width), checked((int)properties.Height), clip.EmbeddedAudioTracks.Count > 0);
        new VideoEdit(0, info.Duration).Validate(info);
        return info;
    }
    public static async Task ExportAsync(VideoInfo source, VideoEdit edit, string output, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        edit.Validate(source); cancellationToken.ThrowIfCancellationRequested();
        output = Path.GetFullPath(output);
        if (!string.Equals(Path.GetExtension(output), ".mp4", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(L.T("Save the edited video as an MP4 file."));
        ProtectOriginal(source.Path, output);
        // Keep the source stable while Windows reads it. Encoding never writes to this handle.
        using var sourceLock = File.OpenHandle(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var composition = new MediaComposition();
        string temporary = Path.Combine(Path.GetDirectoryName(output)!, ".desktoptools-video-" + Guid.NewGuid().ToString("N") + ".mp4");
        string intermediate = Path.Combine(Path.GetDirectoryName(output)!, ".desktoptools-video-" + Guid.NewGuid().ToString("N") + ".mp4");
        try
        {
            var input = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(source.Path)).AsTask(cancellationToken);
            VideoEncodingProperties? sourceProperties = null;
            foreach (var segment in edit.Segments(source))
            {
                var clip = await MediaClip.CreateFromFileAsync(input).AsTask(cancellationToken);
                sourceProperties = clip.GetVideoEncodingProperties();
                if (Math.Abs(clip.OriginalDuration.TotalSeconds - source.Duration) > .05 || sourceProperties.Width != source.Width || sourceProperties.Height != source.Height)
                    throw new InvalidOperationException(L.T("The source video changed. Open it again before exporting."));
                clip.TrimTimeFromStart = TimeSpan.FromSeconds(segment.Start);
                clip.TrimTimeFromEnd = clip.OriginalDuration - TimeSpan.FromSeconds(Math.Min(segment.End, source.Duration));
                clip.Volume = edit.Mute ? 0 : 1;
                composition.Clips.Add(clip);
            }
            var size = edit.OutputSize(source);
            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            profile.Video.Width = (uint)size.Width; profile.Video.Height = (uint)size.Height;
            if (sourceProperties!.FrameRate.Numerator > 0 && sourceProperties.FrameRate.Denominator > 0)
            {
                profile.Video.FrameRate.Numerator = sourceProperties.FrameRate.Numerator;
                profile.Video.FrameRate.Denominator = sourceProperties.FrameRate.Denominator;
            }
            profile.Video.PixelAspectRatio.Numerator = 1; profile.Video.PixelAspectRatio.Denominator = 1;
            double fps = profile.Video.FrameRate.Numerator / (double)profile.Video.FrameRate.Denominator;
            profile.Video.Bitrate = (uint)Math.Clamp(size.Width * (double)size.Height * fps * .15, 1_000_000, 40_000_000);
            if (edit.Mute || !source.HasAudio) profile.Audio = null;
            using (File.Open(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            var destination = await StorageFile.GetFileFromPathAsync(temporary).AsTask(cancellationToken);
            bool transformed = edit.CropX != 0 || edit.CropY != 0 || (edit.CropWidth != 0 && edit.CropWidth != source.Width) || (edit.CropHeight != 0 && edit.CropHeight != source.Height) || edit.Rotation != 0;
            TranscodeFailureReason result;
            if (!transformed)
                result = await composition.RenderToFileAsync(destination, MediaTrimmingPreference.Precise, profile).AsTask(cancellationToken, progress);
            else
            {
                using (File.Open(intermediate, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                var intermediateFile = await StorageFile.GetFileFromPathAsync(intermediate).AsTask(cancellationToken);
                var firstProfile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
                var intermediateSize = new VideoEdit(0, source.Duration).OutputSize(source);
                firstProfile.Video.Width = (uint)intermediateSize.Width; firstProfile.Video.Height = (uint)intermediateSize.Height;
                firstProfile.Video.Bitrate = (uint)Math.Clamp(source.Width * (double)source.Height * fps * .25, 4_000_000, 60_000_000);
                firstProfile.Video.FrameRate.Numerator = profile.Video.FrameRate.Numerator; firstProfile.Video.FrameRate.Denominator = profile.Video.FrameRate.Denominator;
                if (edit.Mute || !source.HasAudio) firstProfile.Audio = null;
                result = await composition.RenderToFileAsync(intermediateFile, MediaTrimmingPreference.Precise, firstProfile).AsTask(cancellationToken, new ScaledProgress(progress, 0, .5));
                if (result == TranscodeFailureReason.None)
                {
                    var transform = new VideoTransformEffectDefinition
                    {
                        CropRectangle = new Windows.Foundation.Rect(edit.CropX * intermediateSize.Width / (double)source.Width, edit.CropY * intermediateSize.Height / (double)source.Height,
                            (edit.CropWidth == 0 ? source.Width : edit.CropWidth) * intermediateSize.Width / (double)source.Width, (edit.CropHeight == 0 ? source.Height : edit.CropHeight) * intermediateSize.Height / (double)source.Height),
                        Rotation = (MediaRotation)(edit.Rotation / 90), OutputSize = new Windows.Foundation.Size(size.Width, size.Height), PaddingColor = Windows.UI.Color.FromArgb(255, 0, 0, 0)
                    };
                    var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = false };
                    transcoder.AddVideoEffect(transform.ActivatableClassId, true, transform.Properties);
                    var prepared = await transcoder.PrepareFileTranscodeAsync(intermediateFile, destination, profile).AsTask(cancellationToken);
                    result = prepared.FailureReason;
                    if (prepared.CanTranscode) await prepared.TranscodeAsync().AsTask(cancellationToken, new ScaledProgress(progress, 50, .5));
                    else if (result == TranscodeFailureReason.None) result = TranscodeFailureReason.Unknown;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (result != TranscodeFailureReason.None) throw new InvalidOperationException(L.F($"Windows could not encode the video ({result}). Try an MP4/H.264 source supported by Windows."));
            if (new FileInfo(temporary).Length == 0) throw new IOException(L.T("Windows produced an empty video. The destination was not changed."));
            ProtectOriginal(source.Path, output);
            File.Move(temporary, output, true);
            progress?.Report(100);
        }
        finally
        {
            composition.Clips.Clear();
            foreach (var scratch in new[] { temporary, intermediate })
            if (File.Exists(scratch))
            {
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try { File.Delete(scratch); break; }
                    catch (IOException) when (attempt < 4) { await Task.Delay(100); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(ex); }
                }
            }
        }
    }
    private sealed class ScaledProgress(IProgress<double>? target, double offset, double scale) : IProgress<double>
    { public void Report(double value) => target?.Report(offset + value * scale); }
    internal static void ProtectOriginal(string source, string output)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase) || (File.Exists(output) && SameFile(source, output)))
            throw new IOException(L.T("Choose a different output file. The original video is protected."));
    }
    private static bool SameFile(string source, string output)
    {
        using var a = File.OpenHandle(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var b = File.OpenHandle(output, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(a, out var first) || !GetFileInformationByHandle(b, out var second)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return first.VolumeSerialNumber == second.VolumeSerialNumber && first.FileIndexHigh == second.FileIndexHigh && first.FileIndexLow == second.FileIndexLow;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint FileAttributes; public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
}
