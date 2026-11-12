using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DesktopTools.Core;

/// <summary>One file of an optional model pack, fetched from the publisher at a pinned revision.</summary>
public sealed record ModelPackFile(string File, string Url, string Sha256, long Bytes);

/// <summary>An optional model the user downloads after accepting its licence. Entries come only from the catalog embedded in the app.</summary>
public sealed record ModelPack(string Id, string Name, string Purpose, string Feature, string Publisher, string Authors,
    string Upstream, string Revision, string LicenseId, string LicenseFile, string Attribution,
    string TrainingData, string[] Limitations, ModelPackFile[] Files)
{
    public long TotalBytes => Files.Sum(f => f.Bytes);
}

/// <summary>Pure rules for the embedded model catalog. No I/O beyond the stream it is given.</summary>
public static class ModelCatalog
{
    /// <summary>Licences that permit private and commercial use with no field-of-use restriction (spec 2.2 rule 2).</summary>
    public static readonly IReadOnlySet<string> AcceptedLicenses =
        new HashSet<string>(["mit", "apache-2.0", "bsd-2-clause", "bsd-3-clause", "cc-by-4.0"], StringComparer.Ordinal);

    /// <summary>Hosts the admitted models are published on: Hugging Face, GitHub (releases, raw and LFS media) and Qualcomm's public S3 bucket.</summary>
    public static readonly IReadOnlySet<string> AllowedHosts = new HashSet<string>(
        ["huggingface.co", "github.com", "raw.githubusercontent.com", "media.githubusercontent.com", "qaihub-public-assets.s3.us-west-2.amazonaws.com"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>File names each installed pack folder writes for itself; a catalog file may not replace them.</summary>
    public static readonly IReadOnlySet<string> ReservedFiles =
        new HashSet<string>(["LICENSE.txt", "ATTRIBUTION.txt", "SOURCE.txt", "DAMAGED"], StringComparer.OrdinalIgnoreCase);

    private static readonly Regex IdPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex HashPattern = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<ModelPack> Parse(Stream json)
    {
        ModelPack?[]? packs;
        try { packs = JsonSerializer.Deserialize<ModelPack?[]>(json, Options); }
        catch (JsonException e) { throw new InvalidDataException("Model catalog is not valid JSON: " + e.Message, e); }
        if (packs == null) throw new InvalidDataException("Model catalog is empty.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pack in packs)
        {
            if (pack == null) throw new InvalidDataException("Model catalog contains an empty entry.");
            Validate(pack);
            if (!ids.Add(pack.Id)) throw new InvalidDataException($"Model pack '{pack.Id}': the id appears more than once.");
        }
        return packs!;
    }

    public static void Validate(ModelPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        string id = pack.Id ?? "";
        void Fail(string rule) => throw new InvalidDataException($"Model pack '{id}': {rule}");
        if (!IdPattern.IsMatch(id)) Fail("the id must use lowercase letters, digits and single hyphens.");
        foreach (var (value, field) in new[] { (pack.Name, "name"), (pack.Purpose, "purpose"), (pack.Feature, "feature"), (pack.Publisher, "publisher"),
            (pack.Authors, "authors"), (pack.Revision, "revision"), (pack.Attribution, "attribution"), (pack.TrainingData, "training data") })
            if (string.IsNullOrWhiteSpace(value)) Fail($"the {field} is missing.");
        if (pack.LicenseId == null || !AcceptedLicenses.Contains(pack.LicenseId)) Fail($"licence '{pack.LicenseId}' is not accepted.");
        if (!SafeFileName(pack.LicenseFile)) Fail("the licence file must be a plain file name.");
        if (!Uri.TryCreate(pack.Upstream, UriKind.Absolute, out var upstream) || upstream.Scheme != Uri.UriSchemeHttps) Fail("the upstream link must be an https address.");
        if (pack.Limitations == null || pack.Limitations.Length == 0 || pack.Limitations.Any(string.IsNullOrWhiteSpace)) Fail("at least one known limitation must be stated.");
        if (pack.Files == null || pack.Files.Length == 0) Fail("it has no files.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in pack.Files!)
        {
            if (file == null) Fail("it contains an empty file entry.");
            if (!SafeFileName(file!.File) || ReservedFiles.Contains(file.File)) Fail($"file name '{file.File}' is not allowed.");
            if (!names.Add(file.File)) Fail($"file '{file.File}' appears more than once.");
            if (file.Sha256 == null || !HashPattern.IsMatch(file.Sha256)) Fail($"file '{file.File}' needs a 64-digit SHA-256.");
            if (file.Bytes <= 0) Fail($"file '{file.File}' needs its exact size.");
            if (!Uri.TryCreate(file.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps) Fail($"file '{file.File}' must download over https.");
            if (!AllowedHosts.Contains(url!.Host) || !string.IsNullOrEmpty(url.UserInfo) || !url.IsDefaultPort) Fail($"file '{file.File}' is not on an allowed host.");
            // The revision pins exactly what was reviewed; it must be a whole path segment of the address.
            if (!url.AbsolutePath.Split('/').Contains(pack.Revision, StringComparer.Ordinal)) Fail($"file '{file.File}' is not pinned to revision {pack.Revision}.");
            // A branch name moves; the revision must be a commit or a release tag.
            if (BranchNames.Contains(pack.Revision)) Fail($"revision '{pack.Revision}' is a branch name, not a fixed revision.");
        }
    }

    /// <summary>
    /// Publishers serve large files from their own download servers: Hugging Face redirects to *.hf.co, GitHub to *.githubusercontent.com.
    /// A redirect is followed only to a catalog host or one of these, over https on the default port.
    /// </summary>
    private static readonly string[] DownloadServerDomains = ["hf.co", "huggingface.co", "githubusercontent.com"];
    private static readonly HashSet<string> BranchNames = new(["main", "master", "head", "latest", "dev", "develop", "trunk"], StringComparer.OrdinalIgnoreCase);

    public static bool IsAllowedDownload(Uri uri) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
        && (AllowedHosts.Contains(uri.Host) || DownloadServerDomains.Any(domain => uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)));

    /// <summary>The domains a download of this pack can contact: its catalog hosts and their publishers' download servers.</summary>
    public static IReadOnlyList<string> ContactedDomains(ModelPack pack)
    {
        var hosts = pack.Files.Select(file => new Uri(file.Url).Host).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (hosts.Any(host => host.Equals("huggingface.co", StringComparison.OrdinalIgnoreCase))) hosts.Add("hf.co");
        if (hosts.Any(host => host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
            hosts.Add("githubusercontent.com");
        return hosts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool SafeFileName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name == Path.GetFileName(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name != "." && name != ".." && !name.EndsWith('.') && !name.EndsWith(' ');
}
