using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using DesktopTools.Core;
using DesktopTools.Extras;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

internal static class VideoEditingChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static MediaEncodingProfile Profile(uint width = 320, uint height = 240)
    {
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga);
        profile.Video.Width = width; profile.Video.Height = height; profile.Video.FrameRate.Numerator = 30; profile.Video.FrameRate.Denominator = 1; profile.Audio = null;
        return profile;
    }
    internal static async Task<string> FixtureAsync(string directory, uint width = 320, uint height = 240)
    {
        Directory.CreateDirectory(directory);
        var file = await (await StorageFolder.GetFolderFromPathAsync(directory)).CreateFileAsync("source.mp4", CreationCollisionOption.ReplaceExisting);
        var composition = new MediaComposition();
        foreach (var color in new[] { Windows.UI.Color.FromArgb(255, 255, 0, 0), Windows.UI.Color.FromArgb(255, 0, 255, 0), Windows.UI.Color.FromArgb(255, 0, 0, 255), Windows.UI.Color.FromArgb(255, 255, 255, 255) })
            composition.Clips.Add(MediaClip.CreateFromColor(color, TimeSpan.FromSeconds(1)));
        var result = await composition.RenderToFileAsync(file, MediaTrimmingPreference.Precise, Profile(width, height));
        composition.Clips.Clear(); Check(result == TranscodeFailureReason.None, "Fixture encoding failed: " + result);
        return file.Path;
    }
    internal static async Task<byte[]> PixelAsync(string path, double time, int x = 32, int y = 24)
    {
        var composition = new MediaComposition(); composition.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path)));
        using var thumbnail = await composition.GetThumbnailAsync(TimeSpan.FromSeconds(time), 64, 48, VideoFramePrecision.NearestFrame);
        using var stream = thumbnail.AsStreamForRead();
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(decoder.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var pixels = new byte[64 * 48 * 4]; converted.CopyPixels(pixels, 64 * 4, 0); composition.Clips.Clear();
        return pixels.Skip((y * 64 + x) * 4).Take(4).ToArray();
    }
    public static async Task RunAsync()
    {
        string directory = Path.GetFullPath("video-checks-" + Guid.NewGuid().ToString("N"));
        string path = await FixtureAsync(directory); var original = SHA256.HashData(File.ReadAllBytes(path));
        var info = await VideoEditorService.ProbeAsync(path); Check(info.Width == 320 && info.Height == 240 && Math.Abs(info.Duration - 4) < .1, "Source metadata");
        var edited = Path.Combine(directory, "cut.mp4");
        await VideoEditorService.ExportAsync(info, new VideoEdit(.5, 3.5, true, 1, 3), edited);
        var result = await VideoEditorService.ProbeAsync(edited); Check(Math.Abs(result.Duration - 1) < .12, "Precise cut duration: " + result.Duration);
        var red = await PixelAsync(edited, .2); var white = await PixelAsync(edited, .7);
        Check(red[2] > 210 && red[0] < 40 && red[1] < 40, "First kept segment not red");
        Check(white.Take(3).All(x => x > 210), "Second kept segment not white");
        var rotated = Path.Combine(directory, "rotated.mp4");
        await VideoEditorService.ExportAsync(info, new VideoEdit(0, 1, CropX: 20, CropY: 20, CropWidth: 160, CropHeight: 100, Rotation: 90, Mute: true), rotated);
        var rotateInfo = await VideoEditorService.ProbeAsync(rotated); Check(rotateInfo.Width == 100 && rotateInfo.Height == 160 && !rotateInfo.HasAudio, "Crop/rotation/mute output");
        try { await VideoEditorService.ExportAsync(info, new VideoEdit(0, 1), path); throw new Exception("Original overwrite accepted"); } catch (IOException) { }
        var alias = Path.Combine(directory, "alias.mp4");
        Check(CreateHardLink(alias, path, IntPtr.Zero), "Could not create isolated fixture hard link");
        try { await VideoEditorService.ExportAsync(info, new VideoEdit(0, 1), alias); throw new Exception("Hardlink overwrite accepted"); } catch (IOException) { }
        var protectedOutput = Path.Combine(directory, "cancelled.mp4"); File.WriteAllText(protectedOutput, "keep destination");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await VideoEditorService.ExportAsync(info, new VideoEdit(0, 1), protectedOutput, cancellationToken: cancelled.Token); throw new Exception("Cancelled export completed"); } catch (OperationCanceledException) { }
        Check(File.ReadAllText(protectedOutput) == "keep destination", "Cancellation changed destination");
        Check(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Original changed");
        // Add a generated PCM tone as audio, without playing it on the user's device.
        var wavePath = Path.Combine(directory, "tone.wav");
        using (var writer = new BinaryWriter(File.Create(wavePath)))
        {
            const int samples = 48000 * 4;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(48000); writer.Write(96000); writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
            for (int i = 0; i < samples; i++) writer.Write((short)(Math.Sin(i * 2 * Math.PI * 440 / 48000) * 3000));
        }
        var audioComposition = new MediaComposition(); audioComposition.Clips.Add(await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path)));
        audioComposition.BackgroundAudioTracks.Add(await BackgroundAudioTrack.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(wavePath)));
        var audioFile = await (await StorageFolder.GetFolderFromPathAsync(directory)).CreateFileAsync("with-audio.mp4", CreationCollisionOption.ReplaceExisting);
        var audioProfile = Profile(); audioProfile.Audio = AudioEncodingProperties.CreateAac(48000, 1, 128000);
        Check(await audioComposition.RenderToFileAsync(audioFile, MediaTrimmingPreference.Precise, audioProfile) == TranscodeFailureReason.None, "Audio fixture encoding");
        audioComposition.Clips.Clear(); audioComposition.BackgroundAudioTracks.Clear();
        var audioInfo = await VideoEditorService.ProbeAsync(audioFile.Path); Check(audioInfo.HasAudio, "Fixture has no audio");
        var audioCut = Path.Combine(directory, "audio-cut.mp4"); await VideoEditorService.ExportAsync(audioInfo, new VideoEdit(.5, 3.5, true, 1, 3), audioCut);
        var audioResult = await VideoEditorService.ProbeAsync(audioCut); Check(audioResult.HasAudio && Math.Abs(audioResult.Duration - 1) < .12, "Audio lost or cut duration changed");
        var muted = Path.Combine(directory, "muted.mp4"); await VideoEditorService.ExportAsync(audioInfo, new VideoEdit(0, 1, Mute: true), muted);
        Check(!(await VideoEditorService.ProbeAsync(muted)).HasAudio, "Mute retained audio stream");
        // Asymmetric pattern proves crop content and clockwise orientation, not only output dimensions.
        byte[] pattern = new byte[320 * 240 * 4];
        for (int y = 0; y < 240; y++) for (int x = 0; x < 320; x++)
        {
            int offset = (y * 320 + x) * 4; pattern[offset + 3] = 255;
            if (x < 160) pattern[offset + 2] = 255; else pattern[offset] = 255;
        }
        var pngPath = Path.Combine(directory, "pattern.png");
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(320, 240, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pattern, 1280)));
        using (var stream = File.Create(pngPath)) png.Save(stream);
        var patternComposition = new MediaComposition(); patternComposition.Clips.Add(await MediaClip.CreateFromImageFileAsync(await StorageFile.GetFileFromPathAsync(pngPath), TimeSpan.FromSeconds(1)));
        var patternFile = await (await StorageFolder.GetFolderFromPathAsync(directory)).CreateFileAsync("pattern.mp4", CreationCollisionOption.ReplaceExisting);
        Check(await patternComposition.RenderToFileAsync(patternFile, MediaTrimmingPreference.Precise, Profile()) == TranscodeFailureReason.None, "Pattern fixture"); patternComposition.Clips.Clear();
        var patternInfo = await VideoEditorService.ProbeAsync(patternFile.Path); var patternOutput = Path.Combine(directory, "pattern-rotated.mp4");
        await VideoEditorService.ExportAsync(patternInfo, new VideoEdit(0, 1, CropX: 80, CropY: 40, CropWidth: 160, CropHeight: 160, Rotation: 90), patternOutput);
        var top = await PixelAsync(patternOutput, .2, 32, 8); var bottom = await PixelAsync(patternOutput, .2, 32, 40);
        Check(top[2] > 210 && top[0] < 40 && bottom[0] > 210 && bottom[2] < 40, "Crop/clockwise rotation changed pixel orientation");
        using var cancelDuring = new CancellationTokenSource();
        try { await VideoEditorService.ExportAsync(info, new VideoEdit(0, 4), protectedOutput, new CancelProgress(cancelDuring), cancelDuring.Token); throw new Exception("Mid-render cancellation completed"); }
        catch (OperationCanceledException) { }
        Check(File.ReadAllText(protectedOutput) == "keep destination", "Mid-render cancellation replaced destination");
        await VideoEditorService.ExportAsync(info, new VideoEdit(0, 1), protectedOutput);
        Check(Math.Abs((await VideoEditorService.ProbeAsync(protectedOutput)).Duration - 1) < .12, "Successful export did not replace destination");
        string invalid = Path.Combine(directory, "invalid.mp4"); File.WriteAllText(invalid, "not a video");
        bool invalidRejected = false;
        try { await VideoEditorService.ProbeAsync(invalid); } catch (Exception) { invalidRejected = true; }
        Check(invalidRejected, "Invalid video accepted");
        Check(!Directory.EnumerateFiles(directory, ".desktoptools-video-*").Any(), "Staging files retained");
        Console.WriteLine("VIDEO fixtures: " + directory);
    }
    private sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<double>
    { public void Report(double value) { if (value < 100) cancellation.Cancel(); } }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLink(string newName, string existingName, IntPtr security);
}
