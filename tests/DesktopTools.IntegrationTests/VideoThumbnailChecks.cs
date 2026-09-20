using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopTools.Extras;

internal static class VideoThumbnailChecks
{
    internal static async Task RunAsync()
    {
        foreach (var (width, height) in new[] { (240u, 480u), (480u, 240u) })
        {
            string source = await VideoEditingChecks.FixtureAsync(Path.GetFullPath("thumbnail-bounds-" + Guid.NewGuid().ToString("N")), width, height);
            var thumbnails = await VideoEditorService.ThumbnailsAsync(await VideoEditorService.ProbeAsync(source), CancellationToken.None);
            if (thumbnails.Count != 10 || thumbnails.Any(t => !t.IsFrozen || t.PixelWidth > 128 || t.PixelHeight > 96))
                throw new Exception("Thumbnail memory bounds exceeded for " + width + "x" + height);
            var first = ScreenshotPixel.Read(thumbnails[0], thumbnails[0].PixelWidth / 2, thumbnails[0].PixelHeight / 2);
            var last = ScreenshotPixel.Read(thumbnails[^1], thumbnails[^1].PixelWidth / 2, thumbnails[^1].PixelHeight / 2);
            if (first.R < 200 || first.G > 40 || last.R < 200 || last.G < 200 || last.B < 200)
                throw new Exception("Bounded thumbnails lost source frame content");
        }
    }
}
