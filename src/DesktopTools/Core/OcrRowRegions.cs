using System.Windows;

namespace DesktopTools.Core;

internal static class OcrRowRegions
{
    internal static IReadOnlyList<Int32Rect> Find(OcrLayout layout, CancellationToken cancellationToken = default, Func<Int32Rect, bool>? containsDetail = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (layout.PixelWidth < 1 || layout.PixelHeight < 1 || layout.Words.Count > 10_000) throw new ArgumentException("OCR layout exceeds row analysis limits.");
        var words = layout.Words.Where(w => !w.Bounds.IsEmpty && w.Bounds.Width > 0 && w.Bounds.Height > 0 &&
            double.IsFinite(w.Bounds.X) && double.IsFinite(w.Bounds.Y) && double.IsFinite(w.Bounds.Right) && double.IsFinite(w.Bounds.Bottom)).ToArray();
        var rows = new List<Rect>();
        foreach (var word in words.Where(w => w.Text.Length >= 3 && w.Bounds.Height <= 9).OrderBy(w => w.Bounds.Y))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var b = word.Bounds;
            if (!rows.Any(r => Math.Abs(r.Y + r.Height / 2 - b.Y - b.Height / 2) < Math.Max(r.Height, b.Height) * .6)) rows.Add(b);
            if (rows.Count > 256) throw new ArgumentException("Too many OCR rows to analyze.");
        }
        var regions = new HashSet<Int32Rect>(); long pixels = 0;
        void Add(int left, int top, int right, int bottom)
        {
            if (right <= left || bottom <= top) return;
            var region = new Int32Rect(left, top, right - left, bottom - top);
            if (regions.Add(region)) pixels += (long)region.Width * region.Height;
            // At 3x, this caps supplemental row work at 54 million pixels.
            if (regions.Count > 256 || pixels > 6_000_000) throw new ArgumentException("OCR rows exceed the analysis budget.");
        }
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int top = (int)Math.Clamp(Math.Floor(row.Y) - 10, 0, layout.PixelHeight);
            int bottom = (int)Math.Min(layout.PixelHeight, top + Math.Ceiling(row.Height) + 32);
            // Full-screen tile readings can round the same glyph row slightly
            // differently. Keep an extra context pixel above their crop.
            if (layout.PixelWidth > 1600) top = Math.Max(0, top - 1);
            // OCR may omit the last value entirely. Inspect image detail, not
            // recognized word edges, before discarding empty horizontal space.
            if (layout.PixelWidth <= 1600 || containsDetail == null) { Add(0, top, layout.PixelWidth, bottom); continue; }
            void AddContent(int left, int right)
            {
                // Refine occupied cell edges using pixels, including words omitted
                // by OCR. A small symmetric margin avoids layout-dependent blank
                // space influencing the recognizer on a full desktop canvas.
                while (left < right && !containsDetail(new(left, top, 1, bottom - top))) { cancellationToken.ThrowIfCancellationRequested(); left++; }
                while (right > left && !containsDetail(new(right - 1, top, 1, bottom - top))) { cancellationToken.ThrowIfCancellationRequested(); right--; }
                if (right > left) Add(Math.Max(0, left - 10), top, Math.Min(layout.PixelWidth, right + 10), bottom);
            }
            int start = -1;
            for (int x = 0; x < layout.PixelWidth; x += 128)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int right = Math.Min(layout.PixelWidth, x + 128);
                bool detail = bottom > top && containsDetail(new(x, top, right - x, bottom - top));
                if (detail && start < 0) start = x;
                if (!detail && start >= 0) { AddContent(start, x); start = -1; }
            }
            if (start >= 0) AddContent(start, layout.PixelWidth);
        }
        return regions.OrderBy(r => r.Y).ThenBy(r => r.X).ToArray();
    }
}
