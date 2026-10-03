using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopTools.Extras;

internal static class OfflineLanguageChecks
{
    internal static async Task RunAsync()
    {
        if (TranslationPacks.Languages.Count < 20) throw new Exception("Offline language catalog is incomplete");
        if (!TranslationPacks.Route("de-fr").SequenceEqual(new[] { "de-en", "en-fr" })) throw new Exception("Non-English translation route is incorrect");
        foreach (var pack in TranslationPacks.Catalog)
        {
            if (pack.License is not "cc-by-4.0" and not "apache-2.0") throw new Exception("Missing pack license");
            foreach (var asset in pack.Files)
            {
                var uri = new Uri(asset.Url);
                if (asset.Sha256.Length != 64 || !asset.Sha256.All(Uri.IsHexDigit) || asset.Bytes < 1 ||
                    Path.GetFileName(asset.File) != asset.File || uri.Scheme != "https" || uri.Host != "huggingface.co" ||
                    !uri.AbsolutePath.Contains("/resolve/" + pack.Revision + "/")) throw new Exception("Unpinned or unsafe pack asset");
            }
        }
        var ocr = LocalOcr.SelectLanguage(new[] { new OcrLanguage("en-US", "English"), new OcrLanguage("ru-RU", "Russian") }, "ru");
        if (ocr?.Tag != "ru-RU") throw new Exception("Saved OCR language did not match its Windows locale");
        string root = Path.GetFullPath("offline-language-fixture-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { await TranslationPacks.InstallAsync("en-de", null, canceled.Token, root); throw new Exception("Canceled download was started"); }
            catch (OperationCanceledException) { }
            await TranslationPacks.InstallAsync("en-de", null, CancellationToken.None, root);
            await TranslationPacks.InstallAsync("de-en", null, CancellationToken.None, root);
            string german = await LocalTranslation.TranslateAsync("Open the window.", "en-de", packRoot: root);
            string english = await LocalTranslation.TranslateAsync("Öffnen Sie das Fenster.", "de-en", packRoot: root);
            string russian = await LocalTranslation.TranslateAsync("Öffnen Sie das Fenster.", "de-ru", packRoot: root);
            File.WriteAllText("offline-language-results.txt", german + "\n" + english + "\n" + russian);
            if (!german.Contains("Fenster", StringComparison.OrdinalIgnoreCase) || !english.Contains("window", StringComparison.OrdinalIgnoreCase) ||
                !russian.Contains("окно", StringComparison.OrdinalIgnoreCase)) throw new Exception("Downloaded or pivot translation failed: " + german + " / " + english + " / " + russian);
            await File.AppendAllTextAsync(Path.Combine(root, "en-de", "config.json"), " ");
            try { await LocalTranslation.TranslateAsync("Hello", "en-de", packRoot: root); throw new Exception("Damaged pack was accepted"); }
            catch (InvalidDataException) { }
            if (TranslationPacks.DirectoryFor("en-de", root) != null) throw new Exception("Damaged downloaded pack cannot be re-downloaded");
            if (Directory.GetDirectories(root, ".download-*").Length != 0) throw new Exception("Download staging was retained");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
