using DesktopTools.Core;
using System.Windows.Media.Imaging;

namespace DesktopTools.Extras;

// Only controlled, localized guidance may pass through the editor's error UI.
public sealed class SensitiveDataAnalysisException(string message) : Exception(message);

public static class SensitiveDataAnalyzer
{
    public static async Task<IReadOnlyList<SensitiveFinding>> AnalyzeAsync(BitmapSource image, string languageTag,
        IReadOnlyCollection<string> categories, CancellationToken cancellationToken,
        Func<BitmapSource, string, CancellationToken, Task<OcrLayout>>? recognize = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var read = recognize ?? LocalOcr.RecognizeLayoutAsync;
        var installed = LocalOcr.Languages;
        var additional = new List<OcrLanguage>();
        // English reads machine values (including phone numbers). The installed
        // app-language recognizer also reads localized field labels in mixed UI.
        string[] supplements = categories.Count == 0 ? [] : categories.Any(c => c is "credential" or "username" or "account" or "serial")
            ? ["en", DesktopTools.Localization.L.Language] : ["en"];
        foreach (string tag in supplements.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var language = installed.FirstOrDefault(l => l.Tag.Split('-')[0].Equals(tag, StringComparison.OrdinalIgnoreCase));
            if (language != null && !language.Tag.Split('-')[0].Equals(languageTag.Split('-')[0], StringComparison.OrdinalIgnoreCase)) additional.Add(language);
        }
        var layout = await read(image, languageTag, cancellationToken);
        var layouts = new List<OcrLayout> { layout };
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var findings = SensitiveDataDetector.Detect(layout, categories);
            foreach (var language in additional)
            {
                var extra = await read(image, language.Tag, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                layouts.Add(extra);
                findings = SensitiveDataDetector.Merge(findings.Concat(SensitiveDataDetector.Detect(extra, categories)));
            }
            if (layouts.Count > 1 && categories.Any(c => c is "credential" or "username" or "account" or "serial"))
            {
                var words = new List<OcrWordBox>(); int offset = 0;
                foreach (var reading in layouts)
                {
                    words.AddRange(reading.Words.Select(w => w with { LineIndex = w.LineIndex + offset }));
                    if (reading.Words.Count > 0) offset += reading.Words.Max(w => w.LineIndex) + 1;
                }
                cancellationToken.ThrowIfCancellationRequested();
                var context = categories.Where(c => c is "credential" or "username" or "account" or "serial").ToArray();
                findings = SensitiveDataDetector.Merge(findings.Concat(SensitiveDataDetector.Detect(layout with { Words = words }, context)));
            }
            return findings;
        }
        catch (ArgumentException) { throw new SensitiveDataAnalysisException(DesktopTools.Localization.L.T("Too much text for sensitive-data analysis. Crop the image and try again.")); }
    }
}
