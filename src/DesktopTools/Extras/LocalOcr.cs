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

    /// <summary>
    /// Reads the text of an image. The picture is read as it is and again as a polarity-neutral "ink" map (which rescues light-on-dark and coloured
    /// text), at a magnification chosen from the size of the text, in overlapping tiles so large screenshots work too. The better reading wins
    /// and anything only the other one found is added. If the enhanced path fails the plain single-pass reading is used.
    /// </summary>
    public static async Task<string> RecognizeAsync(BitmapSource image, string languageTag, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { return await RecognizeEnhancedAsync(image, languageTag, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (SensitiveDataAnalysisException) when (image.PixelWidth > OcrEngine.MaxImageDimension || image.PixelHeight > OcrEngine.MaxImageDimension)
        { throw new InvalidOperationException(L.F($"Crop the screenshot to at most {OcrEngine.MaxImageDimension} pixels on each side before extracting text.")); }
        catch (Exception) when (image.PixelWidth <= OcrEngine.MaxImageDimension && image.PixelHeight <= OcrEngine.MaxImageDimension)
        { return await RecognizeSinglePassAsync(image, languageTag, cancellationToken); }
    }

    internal static async Task<string> RecognizeEnhancedAsync(BitmapSource image, string languageTag, CancellationToken cancellationToken)
    {
        var readings = new List<IReadOnlyList<OcrEnhancer.Line>>();
        // Pass 1 at 2x also tells how large the text is. Tiny UI text is read again at a higher magnification.
        var plain = await RecognizeLayoutAtScaleAsync(image, languageTag, 2, cancellationToken);
        readings.Add(OcrEnhancer.Lines(plain));
        double height = OcrEnhancer.MedianWordHeight(plain);
        int scale = height > 0 && height < 18 ? Math.Min(4, (int)Math.Ceiling(OcrEnhancer.TargetTextHeight / height)) : 2;
        bool inkable = (long)image.PixelWidth * image.PixelHeight <= 20_000_000;
        var ink = inkable ? OcrEnhancer.InkMap(image) : null;
        async Task ReadAsync(string tag, bool first)
        {
            if (!first || scale > 2) readings.Add(OcrEnhancer.Lines(await RecognizeLayoutAtScaleAsync(image, tag, scale, cancellationToken)));
            if (ink != null) readings.Add(OcrEnhancer.Lines(await RecognizeLayoutAtScaleAsync(ink, tag, scale, cancellationToken)));
        }
        await ReadAsync(languageTag, first: true);
        var lines = OcrEnhancer.Select(readings);

        // A page that mixes alphabets (Cyrillic UI with Latin file names, for example) is misread in whole lines by an engine that does not know
        // the other alphabet. Read it with one installed language of another script too and take over only the lines that clearly belong to it.
        var primary = OcrEnhancer.ScriptOfLanguage(languageTag);
        var other = Languages.FirstOrDefault(l => OcrEnhancer.ScriptOfLanguage(l.Tag) != primary);
        if (other != null)
        {
            var otherScript = OcrEnhancer.ScriptOfLanguage(other.Tag);
            var secondary = new List<IReadOnlyList<OcrEnhancer.Line>>();
            var saved = readings.ToList(); readings.Clear();
            await ReadAsync(other.Tag, first: false);
            secondary.AddRange(readings); readings.Clear(); readings.AddRange(saved);
            // The other engine reads every alphabet it does not know as look-alike garbage, so its lines only replace ours where they are
            // clearly in its own alphabet and clearly more word-like than what we read in the same place.
            var foreign = OcrEnhancer.Select(secondary).Where(l => OcrEnhancer.ScriptShare(l.Text, otherScript) >= .7 && OcrEnhancer.WordLikeness(l.Text) is >= .28).ToList();
            if (foreign.Count > 0)
            {
                bool Overlaps(OcrEnhancer.Line a, OcrEnhancer.Line b)
                {
                    var overlap = Rect.Intersect(a.Bounds, b.Bounds);
                    return !overlap.IsEmpty && overlap.Width * overlap.Height / Math.Max(1, a.Bounds.Width * a.Bounds.Height) > .5;
                }
                var replaced = new HashSet<OcrEnhancer.Line>(); var added = new List<OcrEnhancer.Line>();
                foreach (var f in foreign)
                {
                    var mine = lines.Where(own => Overlaps(own, f) || Overlaps(f, own)).ToArray();
                    double mineLike = mine.Select(m => OcrEnhancer.WordLikeness(m.Text) ?? 0).DefaultIfEmpty(0).Average();
                    if (OcrEnhancer.WordLikeness(f.Text)!.Value < mineLike + .08) continue;
                    foreach (var m in mine) replaced.Add(m);
                    added.Add(f);
                }
                lines = lines.Where(own => !replaced.Contains(own)).Concat(added).ToArray();
            }
        }
        return OcrEnhancer.Compose(lines);
    }

    /// <summary>The original reading: the whole image once, at native size. Kept as the fallback and as the baseline for quality checks.</summary>
    internal static async Task<string> RecognizeSinglePassAsync(BitmapSource image, string languageTag, CancellationToken cancellationToken = default)
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
        // Dark themes: read the polarity-neutral ink map as well and keep both readings, so light-on-dark secrets are not missed.
        OcrLayout? inkReading = null;
        if ((long)image.PixelWidth * image.PixelHeight <= 20_000_000 && OcrEnhancer.IsMostlyDark(image))
            inkReading = await RecognizeLayoutAtScaleAsync(OcrEnhancer.InkMap(image), languageTag, 2, cancellationToken);
        bool plainIsEnough = primary.Words.Count > 0 && !primary.Words.Any(w => w.Text.Length >= 3 && w.Bounds.Height <= 12);
        // Tiny code/UI glyphs can be read differently at another sampling size.
        // Retain independent line variants so detection can use either reading;
        // do not overwrite a correctly read word with a longer OCR mistake.
        if (plainIsEnough && inkReading == null) return primary;
        var words = primary.Words.ToList();
        void Append(OcrLayout reading, int y = 0, int x = 0)
        {
            if (words.Count + reading.Words.Count > 10_000)
                throw new SensitiveDataAnalysisException(L.T("Too much text for sensitive-data analysis. Crop the image and try again."));
            int firstLine = words.Count == 0 ? 0 : words.Max(w => w.LineIndex) + 1;
            words.AddRange(reading.Words.Select(w => w with { LineIndex = w.LineIndex + firstLine,
                Bounds = new Rect(w.Bounds.X + x, w.Bounds.Y + y, w.Bounds.Width, w.Bounds.Height) }));
        }
        if (inkReading != null) Append(inkReading);
        if (plainIsEnough) return primary with { Words = words };
        Append(await RecognizeLayoutAtScaleAsync(image, languageTag, 3, cancellationToken));
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
                using var bitmap = OcrEnhancer.ToSoftwareBitmap(OcrEnhancer.Enlarge(tile, recognitionScale));
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
