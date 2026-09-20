using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopTools.Updates;

public sealed record GitHubRelease(string Version, Uri NotesUrl, Uri InstallerUrl, string InstallerName, long InstallerSize, Uri ChecksumsUrl);

/// <summary>Public stable releases only. No credentials, telemetry or arbitrary download URLs.</summary>
public sealed class GitHubReleaseClient : IDisposable
{
    public static string RepositoryUrl => "https://github.com/vg2222/DesktopTools";
    private const string ApiUrl = "https://api.github.com/repos/vg2222/DesktopTools/releases/latest";
    private const long MaximumInstallerSize = 1024L * 1024 * 1024;
    private readonly HttpClient http;
    private readonly bool ownsClient;

    public GitHubReleaseClient(HttpClient? client = null)
    {
        ownsClient = client == null;
        http = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    }
    public static bool IsNewer(string candidate, string current) => ParseVersion(candidate) > ParseVersion(current);
    private static Version ParseVersion(string value)
    {
        value = value.TrimStart('v', 'V');
        if (!Regex.IsMatch(value, @"^\d+\.\d+\.\d+$") || !Version.TryParse(value, out var version))
            throw new InvalidDataException("The release version is invalid.");
        return version;
    }
    public async Task<GitHubRelease?> GetLatestAsync(CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await GetAsync(new Uri(ApiUrl), timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await ReadBoundedAsync(response, 1024 * 1024, timeout.Token).ConfigureAwait(false));
        return ParseRelease(json.RootElement);
    }
    public static GitHubRelease? ParseRelease(JsonElement json)
    {
        if (json.GetProperty("draft").GetBoolean() || json.GetProperty("prerelease").GetBoolean()) return null;
        string tag = json.GetProperty("tag_name").GetString() ?? "";
        string version = ParseVersion(tag).ToString(3);
        string name = $"DesktopTools-{version}-win-x64-setup.exe";
        string prefix = RepositoryUrl + "/releases/download/" + Uri.EscapeDataString(tag) + "/";
        Uri Asset(string file)
        {
            var asset = json.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("name").GetString() == file);
            string expected = prefix + file;
            if (asset.GetProperty("browser_download_url").GetString() != expected)
                throw new InvalidDataException("The release asset URL is not from the DesktopTools repository.");
            return new Uri(expected);
        }
        var installer = json.GetProperty("assets").EnumerateArray().Single(a => a.GetProperty("name").GetString() == name);
        long size = installer.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaximumInstallerSize) throw new InvalidDataException("The installer size is invalid.");
        return new GitHubRelease(version, new Uri(RepositoryUrl + "/releases/tag/" + Uri.EscapeDataString(tag)), Asset(name), name, size, Asset("SHA256SUMS.txt"));
    }
    public async Task<string> DownloadInstallerAsync(GitHubRelease release, string directory, IProgress<double>? progress = null, CancellationToken token = default)
    {
        // Validate public callers too, before any network or filesystem operation.
        if (release.InstallerName != $"DesktopTools-{ParseVersion(release.Version).ToString(3)}-win-x64-setup.exe" ||
            release.InstallerSize <= 0 || release.InstallerSize > MaximumInstallerSize ||
            !release.InstallerUrl.AbsoluteUri.StartsWith(RepositoryUrl + "/releases/download/", StringComparison.Ordinal) ||
            !release.InstallerUrl.AbsoluteUri.EndsWith("/" + release.InstallerName, StringComparison.Ordinal) ||
            release.ChecksumsUrl != new Uri(release.InstallerUrl, "SHA256SUMS.txt"))
            throw new InvalidDataException("Invalid DesktopTools release assets.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(30));
        using var checksumResponse = await GetAsync(release.ChecksumsUrl, timeout.Token).ConfigureAwait(false); checksumResponse.EnsureSuccessStatusCode();
        string manifest = System.Text.Encoding.UTF8.GetString(await ReadBoundedAsync(checksumResponse, 65536, timeout.Token).ConfigureAwait(false));
        var matches = Regex.Matches(manifest, @"(?m)^([a-fA-F0-9]{64})[ \t]+\*?" + Regex.Escape(release.InstallerName) + @"\r?$");
        if (matches.Count != 1) throw new InvalidDataException("The release is missing a unique installer SHA-256 checksum.");
        string expectedHash = matches[0].Groups[1].Value;
        string root = Path.GetFullPath(directory); Directory.CreateDirectory(root);
        string destination = Path.Combine(root, release.InstallerName);
        string partial = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using var response = await GetAsync(release.InstallerUrl, timeout.Token).ConfigureAwait(false); response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != release.InstallerSize)
                throw new InvalidDataException("The installer length does not match its release.");
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[81920]; long total = 0; int count, lastPercent = -1;
                while ((count = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    total += count;
                    if (total > release.InstallerSize) throw new InvalidDataException("The installer exceeds its expected size.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                    int percent = (int)(total * 100 / release.InstallerSize);
                    if (percent != lastPercent) { lastPercent = percent; progress?.Report((double)total / release.InstallerSize); }
                }
                if (total != release.InstallerSize || !Convert.ToHexString(hash.GetHashAndReset()).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Installer verification failed. Please try downloading again.");
            }
            token.ThrowIfCancellationRequested(); File.Move(partial, destination, overwrite: true); return destination;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token)
    {
        for (int redirects = 0; redirects < 6; redirects++)
        {
            if (uri.Scheme != "https" || !(uri.Host is "github.com" or "api.github.com" || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Unexpected update download location.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("DesktopTools-Updater/1.0");
            if (uri.Host == "api.github.com") { request.Headers.Accept.ParseAdd("application/vnd.github+json"); request.Headers.Add("X-GitHub-Api-Version", "2026-03-10"); }
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location; response.Dispose();
            if (location == null) throw new InvalidDataException("Missing update redirect location.");
            uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
        }
        throw new InvalidDataException("Too many update redirects.");
    }
    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int maximum, CancellationToken token)
    {
        using var output = new MemoryStream(); using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        byte[] buffer = new byte[8192]; int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        { if (output.Length + count > maximum) throw new InvalidDataException("Update metadata is too large."); await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false); }
        return output.ToArray();
    }
    public void Dispose() { if (ownsClient) http.Dispose(); }
}
