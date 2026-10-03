using System.Windows;

namespace DesktopTools.Core;

internal static class OcrGapRegions
{
    internal static IReadOnlyList<Int32Rect> Find(OcrLayout layout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (layout.PixelWidth < 1 || layout.PixelHeight < 1 || layout.Words.Count > 10_000) throw new ArgumentException("OCR layout exceeds gap analysis limits.");
        var regions = new HashSet<Int32Rect>();
        foreach (var line in layout.Words.GroupBy(w => w.LineIndex))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var words = line.Where(w => !w.Bounds.IsEmpty && w.Bounds.Width > 0 && w.Bounds.Height > 0 && double.IsFinite(w.Bounds.Right) && double.IsFinite(w.Bounds.Bottom))
                .OrderBy(w => w.Bounds.X).ToArray();
            if (words.Length < 2 || !words.Any(w => w.Text.Length >= 3 && w.Bounds.Height <= 12)) continue;
            Rect bounds = Rect.Empty; foreach (var word in words) bounds.Union(word.Bounds);
            if (bounds.Height > 20) continue;
            for (int i = 1; i < words.Length; i++)
            {
                var previous = words[i - 1].Bounds; var next = words[i].Bounds;
                double gap = next.Left - previous.Right;
                if (gap < Math.Max(24, bounds.Height * 2) || gap > Math.Min(600, bounds.Height * 30)) continue;
                int left = (int)Math.Clamp(Math.Ceiling(previous.Right), 0, layout.PixelWidth);
                int right = (int)Math.Clamp(Math.Ceiling(next.Left) + 3, 0, layout.PixelWidth);
                int top = (int)Math.Clamp(Math.Floor(bounds.Top) - 4, 0, layout.PixelHeight);
                int bottom = (int)Math.Clamp(Math.Ceiling(bounds.Bottom) + 4, 0, layout.PixelHeight);
                if (right > left && bottom > top) regions.Add(new(left, top, right - left, bottom - top));
                if (regions.Count > 64) throw new ArgumentException("Too many OCR gaps to analyze.");
            }
        }
        return regions.OrderBy(r => r.Y).ThenBy(r => r.X).ToArray();
    }
}
