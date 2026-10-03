using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
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
    private static readonly HttpClient client = new() { Timeout = TimeSpan.FromMinutes(15) };
    private static readonly SemaphoreSlim installGate = new(1, 1);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> damagedDirectories = new(StringComparer.OrdinalIgnoreCase);
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTools", "Translation");
    internal static IReadOnlyList<TranslationPack> Catalog => catalog.Value;

    internal static IReadOnlyList<TranslationLanguage> Languages
    {
        get
        {
            var preferred = GlobalizationPreferences.Languages.Select(tag => tag.Split('-')[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return Catalog.SelectMany(pack => pack.Direction.Split('-')).Distinct().Select(code =>
            {
                string name;
                try { name = CultureInfo.GetCultureInfo(code).DisplayName; }
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
    internal static IReadOnlyList<TranslationPack> Missing(string direction) => Route(direction)
        .Where(pair => DirectoryFor(pair) == null).Select(Find).ToArray();

    internal static async Task InstallAsync(string direction, IProgress<double>? progress, CancellationToken token, string? root = null)
    {
        var pack = Find(direction);
        root ??= Root;
        await installGate.WaitAsync(token);
        string stage = Path.Combine(root, ".download-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (DirectoryFor(direction, root) != null) { progress?.Report(1); return; }
            Directory.CreateDirectory(stage);
            long total = pack.Files.Sum(file => file.Bytes), completed = 0;
            foreach (var file in pack.Files)
            {
                token.ThrowIfCancellationRequested();
                using var response = await client.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(token);
                await using (var output = File.Create(Path.Combine(stage, file.File)))
                {
                    byte[] buffer = new byte[128 * 1024]; long received = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, token)) > 0)
                    {
                        received += read;
                        if (received > file.Bytes) throw new InvalidDataException("Translation pack download exceeds its expected size.");
                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                        progress?.Report((double)(completed + received) / total);
                    }
                    if (received != file.Bytes) throw new InvalidDataException("Translation pack download is incomplete.");
                }
                await using var check = File.OpenRead(Path.Combine(stage, file.File));
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(check, token));
                if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Translation pack checksum mismatch.");
                completed += file.Bytes;
            }
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
            finally { installGate.Release(); }
        }
    }
}
