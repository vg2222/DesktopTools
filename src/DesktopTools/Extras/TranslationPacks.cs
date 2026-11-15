using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Windows.System.UserProfile;

namespace DesktopTools.Extras;

internal sealed record TranslationLanguage(string Code, string Name, bool Preferred)
{
    public override string ToString() => Preferred ? Name + " · Windows" : Name;
}

internal sealed record TranslationPackFile(string File, string Url, string Sha256, long Bytes);
internal sealed record TranslationPack(string Direction, string Repository, string Revision, string Upstream,
    string License, TranslationPackFile[] Files);

internal static class TranslationPacks
{
    private static readonly Lazy<TranslationPack[]> catalog = new(() =>
    {
        using var stream = typeof(TranslationPacks).Assembly.GetManifestResourceStream("DesktopTools.TranslationPacks")!;
        return JsonSerializer.Deserialize<TranslationPack[]>(stream)!;
    });
    // No overall time limit: ModelPacks.DownloadFilesAsync stops a download that stalls, and the user can cancel.
    private static readonly HttpClient client = ModelPacks.CreateClient();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> damagedDirectories = new(StringComparer.OrdinalIgnoreCase);
    private static string? rootOverride;
    internal static string Root => rootOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTools", "Translation");
    /// <summary>Points <see cref="Root"/> at a test folder until disposed.</summary>
    internal static IDisposable UseRootForTests(string root) { rootOverride = root; return new RootReset(); }
    private sealed class RootReset : IDisposable { public void Dispose() => rootOverride = null; }
    internal static IReadOnlyList<TranslationPack> Catalog => catalog.Value;

    internal static IReadOnlyList<TranslationLanguage> Languages
    {
        get
        {
            var preferred = GlobalizationPreferences.Languages.Select(tag => tag.Split('-')[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return Catalog.SelectMany(pack => pack.Direction.Split('-')).Distinct().Select(code =>
            {
                string name;
                try
                {
                    // In the app's language where it has the name; otherwise the language's own name, which its speakers recognise.
                    var culture = CultureInfo.GetCultureInfo(code); string english = culture.EnglishName, shown = DesktopTools.Localization.L.T(english);
                    name = DesktopTools.Localization.L.Language != "en" && shown == english ? culture.NativeName : shown;
                }
                catch (CultureNotFoundException) { name = code; }
                return new TranslationLanguage(code, name, preferred.Contains(code));
            }).OrderByDescending(language => language.Preferred).ThenBy(language => language.Name, StringComparer.CurrentCulture).ToArray();
        }
    }

    internal static string[] Route(string direction)
    {
        var codes = direction.Split('-');
        if (codes.Length != 2 || codes[0] == codes[1] || codes.Any(code => !Languages.Any(language => language.Code == code)))
            throw new ArgumentException("Unsupported translation direction.");
        if (Catalog.Any(pack => pack.Direction == direction)) return [direction];
        return [codes[0] + "-en", "en-" + codes[1]];
    }
    internal static TranslationPack Find(string direction) => Catalog.Single(pack => pack.Direction == direction);
    internal static string? DirectoryFor(string direction, string? root = null)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "Translation", direction);
        if ((direction is "en-ru" or "ru-en") && Complete(bundled)) return bundled;
        var downloaded = Path.Combine(root ?? Root, Find(direction).Direction);
        return Complete(downloaded) && !damagedDirectories.ContainsKey(downloaded) ? downloaded : null;
    }
    internal static void MarkDamaged(string directory) => damagedDirectories.TryAdd(directory, 0);
    private static bool Complete(string directory) => new[] { "encoder_model_quantized.onnx", "decoder_model_merged_quantized.onnx", "source.spm", "vocab.json", "config.json" }
        .All(file => File.Exists(Path.Combine(directory, file)));
    /// <summary>Packs downloaded into <paramref name="root"/>; the bundled English–Russian packs are not listed.</summary>
    internal static IReadOnlyList<TranslationPack> Downloaded(string? root = null) =>
        Catalog.Where(pack => Complete(Path.Combine(root ?? Root, pack.Direction))).ToArray();
    internal static long SizeOnDisk(string direction, string? root = null)
    {
        try
        {
            var directory = new DirectoryInfo(Path.Combine(root ?? Root, Find(direction).Direction));
            return directory.Exists ? directory.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length) : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }
    /// <summary>Deletes a downloaded pack. Waits for any pack download to finish first.</summary>
    internal static void Remove(string direction, string? root = null, DesktopTools.Core.ModelConsentStore? consents = null)
    {
        root ??= Root;
        string directory = Path.Combine(root, Find(direction).Direction);
        if (!ModelPacks.InstallGate.Wait(0)) throw new ModelPackException(DesktopTools.Localization.L.T("Wait for the current download to finish, then try again."));
        try
        {
            if (Directory.Exists(directory))
            {
                // A loaded translation keeps its model files open; release it so the rename below can succeed.
                if (AiRuntime.LoadedModelPaths.Any(loaded => loaded.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) AiRuntime.ReleaseAll();
                // Rename first so a half-deleted folder is never used; a translation in progress keeps its files open and the rename fails cleanly.
                string trash = Path.Combine(root, ".download-remove-" + Guid.NewGuid().ToString("N"));
                Directory.Move(directory, trash);
                damagedDirectories.TryRemove(directory, out _);
                Directory.Delete(trash, recursive: true);
            }
            // Removing a pack removes its licence acceptance too (spec 2.4), as for model packs.
            (consents ?? ModelPacks.Consents()).Remove(ConsentPack(Find(direction)).Id);
        }
        finally { ModelPacks.InstallGate.Release(); }
    }
    /// <summary>
    /// The pack as the licence consent window and the record of consent see it (spec 2.4). The licence text is the one already written into
    /// each translation pack folder; the id is "translation-" plus the direction so it never collides with a model pack.
    /// </summary>
    internal static DesktopTools.Core.ModelPack ConsentPack(TranslationPack pack)
    {
        var codes = pack.Direction.Split('-');
        var names = Languages.ToDictionary(language => language.Code, language => language.Name);
        string Name(string code) => names.TryGetValue(code, out var name) ? name : code;
        return new DesktopTools.Core.ModelPack("translation-" + pack.Direction,
            DesktopTools.Localization.L.F($"Translation pack: {Name(codes[0])} → {Name(codes[1])}"),
            "Translates text between these two languages on this PC.", "Text tools (Translate)",
            "Xenova (Joshua Lochner) on Hugging Face", "Language Technology Research Group, University of Helsinki (OPUS-MT)",
            "https://huggingface.co/" + pack.Repository, pack.Revision, pack.License, pack.License == "apache-2.0" ? "Apache-2.0.txt" : "CC-BY-4.0.txt",
            $"OPUS-MT model {pack.Upstream} by the University of Helsinki Language Technology Research Group. Quantized ONNX conversion published by Xenova on Hugging Face.",
            "OPUS parallel corpora (opus.nlpl.eu), collected from public translated texts with their own terms.",
            ["Translates one sentence at a time, so context across sentences can be lost.", "Names, numbers and technical terms can be translated wrongly.", "Results can be wrong; check them before you rely on them."],
            pack.Files.Select(file => new DesktopTools.Core.ModelPackFile(file.File, file.Url, file.Sha256, file.Bytes)).ToArray());
    }

    internal static IReadOnlyList<TranslationPack> Missing(string direction) => Route(direction)
        .Where(pair => DirectoryFor(pair) == null).Select(Find).ToArray();

    /// <summary>Downloads a pack whose licence was accepted in the consent window (spec 2.4); refuses before any request otherwise.</summary>
    internal static async Task InstallAsync(string direction, DesktopTools.Core.ModelConsentStore consents, IProgress<double>? progress, CancellationToken token,
        string? root = null, HttpMessageHandler? handler = null)
    {
        var pack = Find(direction);
        var consent = ConsentPack(pack);
        if (!consents.IsCurrent(consent, ModelPacks.LicenseText(consent)))
            throw new InvalidOperationException("The licence for this translation pack has not been accepted.");
        root ??= Root;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, ModelPacks.StopToken);
        token = stop.Token;
        await ModelPacks.InstallGate.WaitAsync(token);
        string stage = Path.Combine(root, ".download-" + Guid.NewGuid().ToString("N"));
        try
        {
            ModelPacks.CleanStaging(root);
            if (DirectoryFor(direction, root) != null) { progress?.Report(1); return; }
            Directory.CreateDirectory(stage);
            using var testClient = handler == null ? null : new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
            await ModelPacks.DownloadFilesAsync(consent.Files, stage, progress, token, testClient ?? client);
            await File.WriteAllTextAsync(Path.Combine(stage, "ATTRIBUTION.txt"),
                $"OPUS-MT by the University of Helsinki Language Technology Research Group.\nhttps://huggingface.co/{pack.Upstream}\nONNX conversion: https://huggingface.co/{pack.Repository}/tree/{pack.Revision}\nLicense: {pack.License}\nQuantized ONNX weights. Input text is processed locally.\n", token);
            string license = pack.License == "apache-2.0" ? "EN-RU-APACHE-2.0.txt" : "CC-BY-4.0.txt";
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "Translation", license), Path.Combine(stage, "LICENSE.txt"));
            // A partial old pack is never used. Replace only files named in the trusted catalog.
            string destination = Path.Combine(root, pack.Direction);
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.EnumerateFiles(stage)) File.Move(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
            damagedDirectories.TryRemove(destination, out _);
            progress?.Report(1);
        }
        finally
        {
            try { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
            finally { ModelPacks.InstallGate.Release(); }
        }
    }
}
