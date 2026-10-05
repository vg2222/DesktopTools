using DesktopTools.Core;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace DesktopTools.Extras;

/// <summary>
/// Image preparation and result assembly that make Windows OCR read the things it is bad at: light text on a dark theme, small user-interface
/// text, text on coloured or gradient backgrounds, and pages that mix several of these. Everything runs locally on pixels the user already has.
/// </summary>
internal static class OcrEnhancer
{
    // Tunables (internal so the quality harness can sweep them).
    internal static int InkRadius = 9;
    internal static double InkGain = 3.4;
    internal static double TargetTextHeight = 28;
    internal static double Sharpen = 0;

    /// <summary>
    /// Turns any text/background combination into dark text on a white page. Each pixel is compared with the average of its neighbourhood and
    /// the difference (in either direction) becomes "ink", so light-on-dark, dark-on-light and coloured regions all look the same to the engine.
    /// </summary>
    public static BitmapSource InkMap(BitmapSource source, int radius = -1, double gain = -1)
    {
        if (radius < 0) radius = InkRadius; if (gain < 0) gain = InkGain;
        int width = source.PixelWidth, height = source.PixelHeight;
        var bgra = ToBgra(source);
        var gray = new byte[width * height];
        for (int i = 0, p = 0; i < gray.Length; i++, p += 4)
            gray[i] = (byte)((bgra[p] * 29 + bgra[p + 1] * 150 + bgra[p + 2] * 77) >> 8);        // B, G, R weights of Rec.601 luma
        var mean = BoxMean(gray, width, height, radius);
        var output = new byte[width * height * 4];
        for (int i = 0, p = 0; i < gray.Length; i++, p += 4)
        {
            double deviation = Math.Abs(gray[i] - mean[i] / 256.0);
            byte value = (byte)(255 - Math.Min(255, deviation * gain));
            output[p] = output[p + 1] = output[p + 2] = value; output[p + 3] = 255;
        }
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, output, width * 4); result.Freeze(); return result;
    }

    /// <summary>Separable box average, returned in 8.8 fixed point.</summary>
    private static int[] BoxMean(byte[] gray, int width, int height, int radius)
    {
        var horizontal = new int[width * height];
        var prefix = new int[Math.Max(width, height) + 1];
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++) prefix[x + 1] = prefix[x] + gray[row + x];
            for (int x = 0; x < width; x++)
            {
                int lo = Math.Max(0, x - radius), hi = Math.Min(width - 1, x + radius);
                horizontal[row + x] = (prefix[hi + 1] - prefix[lo]) * 256 / (hi - lo + 1);
            }
        }
        var result = new int[width * height];
        var column = new long[height + 1];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++) column[y + 1] = column[y] + horizontal[y * width + x];
            for (int y = 0; y < height; y++)
            {
                int lo = Math.Max(0, y - radius), hi = Math.Min(height - 1, y + radius);
                result[y * width + x] = (int)((column[hi + 1] - column[lo]) / (hi - lo + 1));
            }
        }
        return result;
    }

    /// <summary>The picture as plain luma grey, which removes the coloured sub-pixel fringes of ClearType text before enlarging.</summary>
    public static BitmapSource Gray(BitmapSource source)
    {
        var gray = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
        var result = new FormatConvertedBitmap(gray, PixelFormats.Bgra32, null, 0); result.Freeze(); return result;
    }

    /// <summary>True when most of the picture is dark (a dark theme), judged from a coarse sample of its luma.</summary>
    public static bool IsMostlyDark(BitmapSource source)
    {
        var pixels = ToBgra(source); long sum = 0; int count = 0;
        int stepX = Math.Max(1, source.PixelWidth / 96), stepY = Math.Max(1, source.PixelHeight / 96);
        for (int y = 0; y < source.PixelHeight; y += stepY)
            for (int x = 0; x < source.PixelWidth; x += stepX)
            { int p = (y * source.PixelWidth + x) * 4; sum += (pixels[p] * 29 + pixels[p + 1] * 150 + pixels[p + 2] * 77) >> 8; count++; }
        return count > 0 && sum / count < 115;
    }

    public static byte[] ToBgra(BitmapSource source)
    {
        BitmapSource converted = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var buffer = new byte[checked(source.PixelWidth * source.PixelHeight * 4)];
        converted.CopyPixels(buffer, source.PixelWidth * 4, 0);
        return buffer;
    }

    /// <summary>Smooth enlargement; small UI text loses dots and thin strokes if the engine has to read it at native size.</summary>
    public static BitmapSource Enlarge(BitmapSource tile, int scale, BitmapScalingMode mode = BitmapScalingMode.HighQuality)
    {
        if (scale <= 1) return tile;
        int width = tile.PixelWidth * scale, height = tile.PixelHeight * scale;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, mode);
        using (var dc = visual.RenderOpen()) dc.DrawImage(tile, new Rect(0, 0, width, height));
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual); target.Freeze(); return target;
    }

    /// <summary>Hands pixels to the Windows engine directly, skipping a PNG encode/decode round trip for every tile.</summary>
    public static SoftwareBitmap ToSoftwareBitmap(BitmapSource source)
    {
        var pixels = ToBgra(source);
        var writer = new DataWriter();
        writer.WriteBytes(pixels);
        IBuffer buffer = writer.DetachBuffer();
        return SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8, source.PixelWidth, source.PixelHeight, BitmapAlphaMode.Ignore);
    }

    public sealed record Line(string Text, Rect Bounds, IReadOnlyList<OcrWordBox>? Words = null);

    public static IReadOnlyList<Line> Lines(OcrLayout layout) =>
        layout.Words.GroupBy(w => w.LineIndex).Select(group =>
        {
            var words = group.OrderBy(w => w.Bounds.X).ToArray();
            Rect bounds = Rect.Empty; foreach (var word in words) bounds.Union(word.Bounds);
            double height = Math.Max(1, words.Average(w => w.Bounds.Height));
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < words.Length; i++)
            {
                if (i > 0) text.Append(words[i].Bounds.Left - words[i - 1].Bounds.Right > height * 2.5 ? "    " : " ");
                text.Append(words[i].Text);
            }
            return new Line(text.ToString(), bounds, words);
        }).Where(line => !line.Bounds.IsEmpty && line.Text.Trim().Length > 0).ToArray();

    /// <summary>Median word height in source pixels; decides how much the page needs to be enlarged.</summary>
    public static double MedianWordHeight(OcrLayout layout)
    {
        var heights = layout.Words.Where(w => w.Text.Length >= 2).Select(w => w.Bounds.Height).OrderBy(h => h).ToArray();
        return heights.Length == 0 ? 0 : heights[heights.Length / 2];
    }

    /// <summary>
    /// How much of a line looks like real text. The engine returns no confidence, so words made of letters/digits count for the line
    /// and symbol soup or erratic upper/lower-case mixes (typical for misread pixels) count against it.
    /// </summary>
    public static double Plausibility(string text)
    {
        double good = 0, bad = 0;
        foreach (var token in text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            int letters = token.Count(char.IsLetterOrDigit), length = token.Length;
            if (length == 1) { if (letters == 1) good += 1; else bad += .5; continue; }
            if ((double)letters / length < .6) { bad += length; continue; }
            good += letters;
            int switches = 0;
            for (int i = 1; i < token.Length; i++)
                if (char.IsLetter(token[i]) && char.IsLetter(token[i - 1]) && char.IsUpper(token[i]) != char.IsUpper(token[i - 1])) switches++;
            if (switches >= 3) bad += length / 2.0;
        }
        return good - 1.5 * bad;
    }

    public static double Plausibility(IEnumerable<Line> lines) => lines.Sum(line => Plausibility(line.Text));

    private static readonly HashSet<string> LatinPairs = new("th he in er an re on at en nd ti es or te of ed is it al ar st to nt ng se ha as ou io le ve co me de hi ri ro ic ne ea ra ce li ch ll be ma si om ur ei ie ge un ad ac et ta ol ss rs ns ld ui qu ue ez os ci la el lo do ca sa na ni pr zi gl ek eg ob ow ie wi rz ra ko".Split(' '));
    private static readonly HashSet<string> CyrillicPairs = new("ст но то на ен ов ни ра ко ро ре ер ол по ал ог ли ан ет пр ор ос ат ел ил ль ск ти ту те ес ых ка ит ва ле ем ед ий ие ия ть не ны ым ом ам ма ме ми мо му мы от ох ац ак ад аз ач ей ех ят ую ди ча ща ше бо вы ди до за из ла ме ми му ну од ож ри св се ся".Split(' '));

    /// <summary>
    /// Share of adjacent letter pairs that are common in real text of the line's alphabet (about 0.4-0.6 for words, 0.1-0.2 for misread pixels).
    /// Null when the line has too few letters to say anything.
    /// </summary>
    public static double? WordLikeness(string text)
    {
        int latin = 0, cyrillic = 0;
        foreach (char c in text) { if (!char.IsLetter(c)) continue; var script = ScriptOf(c); if (script == Script.Latin) latin++; else if (script == Script.Cyrillic) cyrillic++; }
        if (latin < 6 && cyrillic < 6) return null;
        var table = cyrillic > latin ? CyrillicPairs : LatinPairs;
        int pairs = 0, hits = 0; char previous = ' ';
        foreach (char raw in text)
        {
            if (!char.IsLetter(raw)) { previous = ' '; continue; }
            char c = char.ToLowerInvariant(raw);
            if (previous != ' ') { pairs++; if (table.Contains(new string([previous, c]))) hits++; }
            previous = c;
        }
        return pairs < 5 ? null : (double)hits / pairs;
    }

    /// <summary>
    /// Picks, for every place on the page, the most plausible reading among all candidate lines (different magnifications, plain or ink map,
    /// other languages). Lines are taken best first; a line that mostly sits on top of one already taken is dropped.
    /// </summary>
    public static IReadOnlyList<Line> Select(IEnumerable<IReadOnlyList<Line>> readings)
    {
        var candidates = readings.SelectMany(r => r).Select(line => (Line: line, Score: Plausibility(line.Text) * (WordLikeness(line.Text) is { } like ? .55 + Math.Min(1, like * 1.6) : 1))).Where(c => c.Score > 0).OrderByDescending(c => c.Score).ToList();
        var accepted = new List<Line>();
        foreach (var (line, _) in candidates)
        {
            double area = Math.Max(1, line.Bounds.Width * line.Bounds.Height), covered = 0;
            foreach (var existing in accepted)
            {
                var overlap = Rect.Intersect(line.Bounds, existing.Bounds);
                if (!overlap.IsEmpty) covered += overlap.Width * overlap.Height;
            }
            if (covered / area < .35) accepted.Add(line);
        }
        return accepted;
    }

    public enum Script { Latin, Cyrillic, Greek, Han, Kana, Hangul, Arabic, Hebrew, Other }

    public static Script ScriptOfLanguage(string tag) => tag.Split('-')[0].ToLowerInvariant() switch
    {
        "ru" or "uk" or "bg" or "be" or "sr" or "mk" or "kk" or "mn" => Script.Cyrillic,
        "el" => Script.Greek, "zh" => Script.Han, "ja" => Script.Kana, "ko" => Script.Hangul, "ar" or "fa" or "ur" => Script.Arabic, "he" => Script.Hebrew,
        _ => Script.Latin
    };

    internal static Script ScriptOf(char c) => c switch
    {
        >= 'A' and <= 'ɏ' => Script.Latin,
        >= 'Ͱ' and <= 'Ͽ' => Script.Greek,
        >= 'Ѐ' and <= 'ԯ' => Script.Cyrillic,
        >= '֐' and <= '׿' => Script.Hebrew,
        >= '؀' and <= 'ۿ' => Script.Arabic,
        >= '぀' and <= 'ヿ' => Script.Kana,
        >= '一' and <= '鿿' => Script.Han,
        >= '가' and <= '힯' => Script.Hangul,
        _ => Script.Other
    };

    /// <summary>Share of the letters of a line that belong to <paramref name="script"/>.</summary>
    public static double ScriptShare(string text, Script script)
    {
        int letters = 0, match = 0;
        foreach (char c in text) if (char.IsLetter(c)) { letters++; if (ScriptOf(c) == script) match++; }
        return letters == 0 ? 0 : (double)match / letters;
    }

    /// <summary>The chosen lines as a clean word layout: rows top to bottom, lines left to right inside a row; every line gets its own LineIndex.</summary>
    public static OcrLayout ToLayout(IReadOnlyList<Line> lines, int pixelWidth, int pixelHeight)
    {
        var rows = new List<List<Line>>();
        foreach (var line in lines.OrderBy(l => l.Bounds.Top + l.Bounds.Height / 2))
        {
            double center = line.Bounds.Top + line.Bounds.Height / 2;
            var row = rows.Count == 0 ? null : rows[^1];
            if (row != null)
            {
                Rect bounds = Rect.Empty; foreach (var l in row) bounds.Union(l.Bounds);
                if (Math.Abs(center - (bounds.Top + bounds.Height / 2)) < Math.Min(bounds.Height, line.Bounds.Height) * .5) { row.Add(line); continue; }
            }
            rows.Add([line]);
        }
        var words = new List<OcrWordBox>(); int index = 0;
        foreach (var row in rows)
            foreach (var line in row.OrderBy(l => l.Bounds.Left))
            {
                foreach (var word in line.Words ?? []) words.Add(word with { LineIndex = index });
                index++;
            }
        return new OcrLayout(pixelWidth, pixelHeight, words);
    }

    /// <summary>Lines in reading order: rows top to bottom, left to right inside a row, with a blank line between paragraphs.</summary>
    public static string Compose(IReadOnlyList<Line> lines)
    {
        if (lines.Count == 0) return "";
        var ordered = lines.OrderBy(l => l.Bounds.Top + l.Bounds.Height / 2).ToList();
        var rows = new List<List<Line>>();
        foreach (var line in ordered)
        {
            var row = rows.Count == 0 ? null : rows[^1];
            double center = line.Bounds.Top + line.Bounds.Height / 2;
            if (row != null)
            {
                Rect rowBounds = Rect.Empty; foreach (var l in row) rowBounds.Union(l.Bounds);
                double rowCenter = rowBounds.Top + rowBounds.Height / 2;
                if (Math.Abs(center - rowCenter) < Math.Min(rowBounds.Height, line.Bounds.Height) * .5) { row.Add(line); continue; }
            }
            rows.Add([line]);
        }
        var text = new System.Text.StringBuilder(); double previousBottom = double.NaN, previousHeight = 0;
        foreach (var row in rows)
        {
            Rect bounds = Rect.Empty; foreach (var l in row) bounds.Union(l.Bounds);
            if (!double.IsNaN(previousBottom))
            {
                text.AppendLine();
                if (bounds.Top - previousBottom > Math.Max(previousHeight, bounds.Height) * 1.1) text.AppendLine();
            }
            var parts = row.OrderBy(l => l.Bounds.Left).ToArray();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) text.Append(parts[i].Bounds.Left - parts[i - 1].Bounds.Right > bounds.Height * 2 ? "    " : " ");
                text.Append(parts[i].Text);
            }
            previousBottom = bounds.Bottom; previousHeight = bounds.Height;
        }
        return text.ToString();
    }
}
