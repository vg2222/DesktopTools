using DesktopTools.Localization;
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
}
