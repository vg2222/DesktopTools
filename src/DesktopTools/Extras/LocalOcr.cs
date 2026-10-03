using DesktopTools.Localization;
using DesktopTools.Core;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace DesktopTools.Extras;

public sealed record OcrLanguage(string Tag, string Name)
{
    public override string ToString() => Name;
}

public static class LocalOcr
{
    public static IReadOnlyList<OcrLanguage> Languages => OcrEngine.AvailableRecognizerLanguages
        .Select(l => new OcrLanguage(l.LanguageTag, l.DisplayName)).OrderBy(l => l.Name, StringComparer.CurrentCulture).ToArray();

    internal static OcrLanguage? SelectLanguage(IReadOnlyList<OcrLanguage> languages, string? savedTag)
    {
        var exact = languages.FirstOrDefault(language => string.Equals(language.Tag, savedTag, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;
        if (!string.IsNullOrWhiteSpace(savedTag))
        {
            var sameLanguage = languages.FirstOrDefault(language => language.Tag.Split('-')[0].Equals(savedTag.Split('-')[0], StringComparison.OrdinalIgnoreCase));
            if (sameLanguage != null) return sameLanguage;
        }
        foreach (string tag in Windows.System.UserProfile.GlobalizationPreferences.Languages)
        {
            var match = languages.FirstOrDefault(language => language.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase))
                ?? languages.FirstOrDefault(language => language.Tag.Split('-')[0].Equals(tag.Split('-')[0], StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return languages.FirstOrDefault();
    }

    public static async Task<string> RecognizeAsync(BitmapSource image, string languageTag, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var engine = OcrEngine.TryCreateFromLanguage(new Language(languageTag))
            ?? throw new InvalidOperationException(L.T("This OCR language is unavailable. Install its Windows language pack in Settings → Time & language → Language & region."));
        if (image.PixelWidth > OcrEngine.MaxImageDimension || image.PixelHeight > OcrEngine.MaxImageDimension)
            throw new InvalidOperationException(L.F($"Crop the screenshot to at most {OcrEngine.MaxImageDimension} pixels on each side before extracting text."));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var memory = new MemoryStream(); encoder.Save(memory);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(memory.ToArray()); await writer.StoreAsync(); await writer.FlushAsync();
        }
        stream.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
        return string.Join(Environment.NewLine, result.Lines.Select(line => line.Text));
    }

    public static async Task<OcrLayout> RecognizeLayoutAsync(BitmapSource image, string languageTag, CancellationToken cancellationToken = default)
    {
        var primary = await RecognizeLayoutAtScaleAsync(image, languageTag, 2, cancellationToken);
        // Tiny code/UI glyphs can be read differently at another sampling size.
        // Retain independent line variants so detection can use either reading;
        // do not overwrite a correctly read word with a longer OCR mistake.
        if (primary.Words.Count > 0 && !primary.Words.Any(w => w.Text.Length >= 3 && w.Bounds.Height <= 12)) return primary;
        var extra = await RecognizeLayoutAtScaleAsync(image, languageTag, 3, cancellationToken);
        var words = primary.Words.ToList();
        void Append(OcrLayout reading, int y = 0, int x = 0)
        {
            if (words.Count + reading.Words.Count > 10_000)
                throw new SensitiveDataAnalysisException(L.T("Too much text for sensitive-data analysis. Crop the image and try again."));
            int firstLine = words.Count == 0 ? 0 : words.Max(w => w.LineIndex) + 1;
            words.AddRange(reading.Words.Select(w => w with { LineIndex = w.LineIndex + firstLine,
                Bounds = new Rect(w.Bounds.X + x, w.Bounds.Y + y, w.Bounds.Width, w.Bounds.Height) }));
        }
        Append(extra);
        // Windows OCR sometimes discards tiny words within a mixed-size page,
        // but reads them when the surrounding page layout is removed. Re-read
        // narrow physical rows independently; retain their original geometry.
        IReadOnlyList<Int32Rect> tinyRows;
        BitmapSource pixels = image.Format == PixelFormats.Bgra32 || image.Format == PixelFormats.Pbgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        bool ContainsDetail(Int32Rect region)
        {
            var buffer = new byte[region.Width * region.Height * 4];
            pixels.CopyPixels(region, buffer, region.Width * 4, 0);
            for (int i = 4; i < buffer.Length; i += 4)
                if (buffer[i] != buffer[0] || buffer[i+1] != buffer[1] || buffer[i+2] != buffer[2] || buffer[i+3] != buffer[3]) return true;
            return false;
        }
        try { tinyRows = OcrRowRegions.Find(primary, cancellationToken, ContainsDetail); }
        catch (ArgumentException) { throw new SensitiveDataAnalysisException(L.T("Too much text for sensitive-data analysis. Crop the image and try again.")); }
        int tileSize = Math.Min(1600, (int)OcrEngine.MaxImageDimension / 3);
        int overlap = Math.Min(400, tileSize / 3);
        long rowTiles = tinyRows.Sum(r => (long)Math.Max(1, (int)Math.Ceiling((r.Width - (double)overlap) / (tileSize - overlap))));
        if (rowTiles > 256) throw new SensitiveDataAnalysisException(L.T("Too much text for sensitive-data analysis. Crop the image and try again."));
        foreach (var row in tinyRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var strip = new CroppedBitmap(image, row);
            Append(await RecognizeLayoutAtScaleAsync(strip, languageTag, 3, cancellationToken), row.Y, row.X);
        }
        // Mixed-script OCR can omit an inline word while preserving its
        // neighbours. Read only unusually large gaps, independently of the
        // surrounding language. This keeps omitted code/password text visible
        // to the same detector without modifying the screenshot pixels.
        IReadOnlyList<Int32Rect> gaps;
        try { gaps = OcrGapRegions.Find(primary, cancellationToken); }
        catch (ArgumentException) { throw new SensitiveDataAnalysisException(L.T("Too much text for sensitive-data analysis. Crop the image and try again.")); }
        foreach (var gap in gaps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var crop = new CroppedBitmap(image, gap);
            Append(await RecognizeLayoutAtScaleAsync(crop, languageTag, 3, cancellationToken), gap.Y, gap.X);
        }
        return primary with { Words = words };
    }

    private static async Task<OcrLayout> RecognizeLayoutAtScaleAsync(BitmapSource image, string languageTag, int recognitionScale, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var engine = OcrEngine.TryCreateFromLanguage(new Language(languageTag))
            ?? throw new SensitiveDataAnalysisException(L.T("This OCR language is unavailable. Install its Windows language pack in Settings → Time & language → Language & region."));
        // Small UI text often loses dots/punctuation at native resolution.
        // Enlarge each bounded tile, then return source-pixel word coordinates.
        int tileSize = Math.Min(1600, (int)OcrEngine.MaxImageDimension / recognitionScale);
        int overlap = Math.Min(400, tileSize / 3);
        int step = tileSize - overlap;
        int columns = Math.Max(1, (int)Math.Ceiling((image.PixelWidth - (double)overlap) / step));
        int rows = Math.Max(1, (int)Math.Ceiling((image.PixelHeight - (double)overlap) / step));
        if ((long)image.PixelWidth * image.PixelHeight > 48_000_000 || (long)columns * rows > 64)
            throw new SensitiveDataAnalysisException(L.T("This image is too large for sensitive-data analysis. Crop it and try again."));
        var words = new List<OcrWordBox>();
        var lineBounds = new List<Rect>();
        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                int x = column * step, y = row * step;
                cancellationToken.ThrowIfCancellationRequested();
                var tile = new CroppedBitmap(image, new Int32Rect(x, y, Math.Min(tileSize, image.PixelWidth - x), Math.Min(tileSize, image.PixelHeight - y)));
                var enlarged = new TransformedBitmap(tile, new ScaleTransform(recognitionScale, recognitionScale));
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(enlarged));
                using var memory = new MemoryStream(); encoder.Save(memory);
                using var stream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                { writer.WriteBytes(memory.ToArray()); await writer.StoreAsync(); await writer.FlushAsync(); }
                stream.Seek(0);
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
                var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
                foreach (var nativeLine in result.Lines)
                {
                    Rect lineRect = Rect.Empty;
                    foreach (var word in nativeLine.Words)
                    {
                        var b = word.BoundingRect;
                        lineRect.Union(new Rect(x + b.X / recognitionScale, y + b.Y / recognitionScale, b.Width / recognitionScale, b.Height / recognitionScale));
                    }
                    if (lineRect.IsEmpty) continue;
                    // Preserve the engine's line membership: small quote marks,
                    // dots and underscores can sit above/below the letter center.
                    // Only combine native lines where overlapping tiles repeat it.
                    int line = lineBounds.FindIndex(l => Math.Min(l.Bottom, lineRect.Bottom) - Math.Max(l.Top, lineRect.Top) > Math.Min(l.Height, lineRect.Height) * .5 &&
                        Math.Max(l.Left, lineRect.Left) - Math.Min(l.Right, lineRect.Right) <= Math.Max(l.Height, lineRect.Height) * 3);
                    if (line < 0) { line = lineBounds.Count; lineBounds.Add(lineRect); }
                    else { var previousLine = lineBounds[line]; previousLine.Union(lineRect); lineBounds[line] = previousLine; }
                    foreach (var word in nativeLine.Words)
                    {
                        var b = word.BoundingRect; var rect = new Rect(x + b.X / recognitionScale, y + b.Y / recognitionScale, b.Width / recognitionScale, b.Height / recognitionScale);
                        // Overlapping tiles can recognize different fragments of one word.
                        // Retain their union so a detected fragment covers the whole word.
                        var duplicate = words.FindIndex(w => w.LineIndex == line && Math.Abs(w.Bounds.Y + w.Bounds.Height / 2 - rect.Y - rect.Height / 2) < Math.Min(w.Bounds.Height, rect.Height) / 2 &&
                            Math.Min(w.Bounds.Right, rect.Right) - Math.Max(w.Bounds.Left, rect.Left) > Math.Min(w.Bounds.Width, rect.Width) * .2);
                        if (duplicate >= 0)
                        {
                            var previous = words[duplicate]; rect.Union(previous.Bounds);
                            words[duplicate] = previous with { Bounds = rect, Text = previous.Text.Length >= word.Text.Length ? previous.Text : word.Text };
                        }
                        else words.Add(new(word.Text, rect, line));
                    }
                }
                if (words.Count > 10_000) throw new SensitiveDataAnalysisException(L.T("Too much text for sensitive-data analysis. Crop the image and try again."));
                cancellationToken.ThrowIfCancellationRequested();
            }
        return new(image.PixelWidth, image.PixelHeight, words.OrderBy(w => w.LineIndex).ThenBy(w => w.Bounds.X).ToArray());
    }
}
