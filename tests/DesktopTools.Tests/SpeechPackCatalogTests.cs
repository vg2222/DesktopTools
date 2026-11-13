using DesktopTools.Core;
using System.IO;

/// <summary>The embedded catalog lists exactly the reviewed Whisper files for transcripts, pinned to their revisions.</summary>
internal static class SpeechPackCatalogTests
{
    public static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "global.json"))) root = Path.GetDirectoryName(root) ?? throw new Exception("Repository root not found");
        using var stream = File.OpenRead(Path.Combine(root, "src", "DesktopTools", "Assets", "Models", "catalog.json"));
        var packs = ModelCatalog.Parse(stream).ToDictionary(p => p.Id);
        const string vocab = "50d6a919f0a0601d56a04eb583c780d18553aa388254ba3158eb6a00f13e2c1a";
        var expected = new Dictionary<string, (string Repo, string Revision, (string File, string UrlPath, long Bytes, string Sha)[] Files)>
        {
            ["speech-fast"] = ("whisper-base", "64da57285918e20ea79ea5c88eed7197933abaa8",
            [
                ("encoder_model.onnx", "onnx/encoder_model.onnx", 82_474_863, "f0bd7927234639c6e1f293cef18a210cee4e4aea93e200ebbe48e1d7acf6fdb1"),
                ("decoder_model_merged.onnx", "onnx/decoder_model_merged.onnx", 208_560_983, "3fed66b2b8dfa1ae213f768527dff1ef7e3857b8d45a36381850ce4dff8db936"),
                ("vocab.json", "vocab.json", 1_036_584, vocab),
                ("generation_config.json", "generation_config.json", 3_776, "3bba359e33fdd6dc1c10f71846a477d339b0242f462f70ea1dd73274caa38d05"),
            ]),
            ["speech-accurate"] = ("whisper-small", "2d67713f236afa48a18992566e7647f6ca848e13",
            [
                ("encoder_model.onnx", "onnx/encoder_model.onnx", 352_839_389, "31a05a14d514440e43746fdaaa8d4e8102c9543e53c5ae1111910af142041406"),
                ("decoder_model_merged.onnx", "onnx/decoder_model_merged.onnx", 615_405_212, "16c197b4c4702c51c767d1931a5673a10fa528bd1e79963891c97d69e0258584"),
                ("vocab.json", "vocab.json", 1_036_584, vocab),
                ("generation_config.json", "generation_config.json", 3_837, "0b7407a4e53a677f826e03c75d409e6f830663932bf43dda3b08c5efa2223279"),
            ]),
        };
        foreach (var (id, want) in expected)
        {
            Check(packs.TryGetValue(id, out var pack), "Catalog lacks " + id);
            Check(pack!.LicenseId == "mit" && pack.LicenseFile == "Whisper-MIT-Apache-2.0.txt" && pack.Revision == want.Revision, id + " licence or revision");
            Check(pack.Upstream == "https://huggingface.co/Xenova/" + want.Repo, id + " upstream");
            Check(pack.Files.Length == 4, id + " must list exactly four files");
            foreach (var (file, urlPath, bytes, sha) in want.Files)
            {
                var entry = pack.Files.SingleOrDefault(f => f.File == file) ?? throw new Exception(id + " lacks " + file);
                Check(entry.Bytes == bytes && entry.Sha256 == sha, id + " " + file + " size or hash");
                Check(entry.Url == $"https://huggingface.co/Xenova/{want.Repo}/resolve/{want.Revision}/{urlPath}", id + " " + file + " url");
            }
            Check(File.Exists(Path.Combine(root, "src", "DesktopTools", "Assets", "Models", "Licenses", pack.LicenseFile)), "Licence text missing");
        }
        Check(packs.Keys.Count(k => k.StartsWith("speech-", StringComparison.Ordinal)) == 2, "Only the admitted speech packs");
    }

    public static void Settings()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        Check(WhisperDecoding.KnownLanguages.Length == 99 && WhisperDecoding.KnownLanguages.Distinct().Count() == 99, "Whisper language list");
        var settings = new AppSettings();
        Check(settings.TranscriptModel == "Fast" && settings.TranscriptLanguage == "auto" && !settings.TranscriptNoticeAccepted, "Transcript defaults");
        settings.TranscriptModel = "Huge"; settings.TranscriptLanguage = "xx"; SettingsStore.Validate(settings);
        Check(settings.TranscriptModel == "Fast" && settings.TranscriptLanguage == "auto", "Unknown transcript values kept");
        settings.TranscriptModel = null!; settings.TranscriptLanguage = null!; SettingsStore.Validate(settings);
        Check(settings.TranscriptModel == "Fast" && settings.TranscriptLanguage == "auto", "Null transcript values kept");
        settings.TranscriptModel = "accurate"; settings.TranscriptLanguage = "RU"; SettingsStore.Validate(settings);
        Check(settings.TranscriptModel == "Accurate" && settings.TranscriptLanguage == "ru", "Valid transcript values lost");
    }
}
