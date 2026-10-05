using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DesktopTools.Core;
using DesktopTools.Extras;

internal static class AnimatedExportChecks
{
    private static void Check(bool value, string what) { if (!value) throw new Exception("Animated export assertion failed: " + what); }

    public static async Task RunAsync()
    {
        // Fixture: 4 s clip, one second each of red, green, blue and white.
        string directory = Path.GetFullPath("animated-export-" + Guid.NewGuid().ToString("N"));
        string path = await VideoEditingChecks.FixtureAsync(directory);
        var original = SHA256.HashData(File.ReadAllBytes(path));
        var info = await VideoEditorService.ProbeAsync(path);
        var edit = new VideoEdit(0, 4);
        foreach (var format in new[] { AnimatedFormat.Gif, AnimatedFormat.WebP })
        {
            var options = new AnimatedOptions(format, 10, 160);
            var (width, height) = AnimatedExport.FrameSize(info, edit, options);
            Check(width == 160 && height == 120 && AnimatedExport.FrameCount(info, edit, options) == 40, "frame size and count for " + format);
            long estimate = await AnimatedExport.EstimateBytesAsync(info, edit, options);
            Check(estimate > 0, "size estimate for " + format);
            string output = Path.Combine(directory, "clip" + AnimatedExport.Extension(format));
            double lastProgress = 0;
            await AnimatedExport.ExportAsync(info, edit, output, options, new Progress<double>(p => lastProgress = Math.Max(lastProgress, p)));
            long size = new FileInfo(output).Length;
            Check(size > 100, "file written for " + format);
            Console.WriteLine($"{format}: estimate {estimate} B, actual {size} B, frames {AnimatedExport.FrameCount(info, edit, options)}");
            try { await AnimatedExport.ExportAsync(info, edit, output, options); throw new Exception("existing file was overwritten"); } catch (IOException) { }
            try { await AnimatedExport.ExportAsync(info, edit, Path.Combine(directory, "wrong.mp4"), options); throw new Exception("wrong extension accepted"); } catch (ArgumentException) { }
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "temporary files removed");
        }
        Check(original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "source video was modified");
        Console.WriteLine("Animated outputs kept in " + directory);
    }
}
