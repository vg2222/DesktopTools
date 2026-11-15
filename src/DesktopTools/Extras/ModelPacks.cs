using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using DesktopTools.Core;
using DesktopTools.Localization;

namespace DesktopTools.Extras;

internal enum ModelPackState { Missing, Installed, Damaged }

/// <summary>A download or install failure with a message meant for the user.</summary>
internal sealed class ModelPackException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Downloads, verifies, installs and removes optional model packs listed in the embedded catalog. Files come only from the catalog's
/// URLs, are checked for exact size and SHA-256 in a staging folder, and are moved into place only when every file is correct.
/// </summary>
internal static class ModelPacks
{
    internal const string StagingPrefix = ".download-";
    private const string DamagedMarker = "DAMAGED";
    private static readonly Lazy<IReadOnlyList<ModelPack>> catalog = new(() =>
    {
        using var stream = typeof(ModelPacks).Assembly.GetManifestResourceStream("DesktopTools.ModelPacks")!;
        return ModelCatalog.Parse(stream);
    });
    private static readonly Lazy<HttpClient> sharedClient = new(CreateClient);
    /// <summary>Redirects are followed by <see cref="DownloadFilesAsync"/>, which checks each target host.</summary>
    internal static HttpClient CreateClient() => new(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private const int MaximumRedirects = 5;
    /// <summary>One download at a time across model and translation packs.</summary>
    internal static readonly SemaphoreSlim InstallGate = new(1, 1);
    /// <summary>A download that receives nothing for this long is stopped.</summary>
    internal static TimeSpan StallTimeout { get; set; } = TimeSpan.FromSeconds(60);

    // Test seams: a fixture catalog, folder, server and free-space probe used when the caller passes none.
    private static IReadOnlyList<ModelPack>? catalogOverride;
    private static string? rootOverride;
    private static HttpMessageHandler? handlerOverride;
    private static Func<string, long>? freeBytesOverride;

    /// <summary>The folder name created inside a folder the person chooses, so the app only ever moves or deletes its own folder.</summary>
    internal const string CustomFolderName = "DesktopTools Models";
    internal static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTools", "Models");
    private static string? configuredRoot;
    internal static string Root => rootOverride ?? configuredRoot ?? DefaultRoot;
    /// <summary>Follows <see cref="AppSettings.AiModelsFolder"/>; translation packs keep their own folder.</summary>
    internal static void ApplySettings(AppSettings settings) => configuredRoot = string.IsNullOrWhiteSpace(settings.AiModelsFolder) ? null : settings.AiModelsFolder;
    /// <summary>The models folder to use for a folder the person picked: the picked folder itself when it is already ours, otherwise our folder inside it.</summary>
    internal static string TargetFor(string picked)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(picked));
        return string.Equals(Path.GetFileName(full), CustomFolderName, StringComparison.OrdinalIgnoreCase) ? full : Path.Combine(full, CustomFolderName);
    }

    /// <summary>
    /// Moves installed packs and licence acceptances from <paramref name="from"/> to <paramref name="to"/>. Refuses a folder that already holds
    /// files or that sits inside the other one, checks free space, copies (leaving unfinished downloads behind), and only then deletes the old folder.
    /// When copying fails, the new folder is removed and the old one stays as it was.
    /// </summary>
    internal static async Task MoveAsync(string from, string to, IProgress<double>? progress, CancellationToken token)
    {
        from = Path.TrimEndingDirectorySeparator(Path.GetFullPath(from)); to = Path.TrimEndingDirectorySeparator(Path.GetFullPath(to));
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return;
        static bool Inside(string child, string parent) => child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (Inside(to, from) || Inside(from, to)) throw new ModelPackException(L.T("Choose a folder outside the current AI models folder."));
        if (Directory.Exists(to) && Directory.EnumerateFileSystemEntries(to).Any()) throw new ModelPackException(L.F($"{to} already contains files. Choose an empty folder."));
        if (!InstallGate.Wait(0)) throw new ModelPackException(L.T("Wait for the current download to finish, then try again."));
        bool created = false;
        try
        {
            AiRuntime.ReleaseAll();
            var files = Directory.Exists(from)
                ? Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)
                    .Where(file => !Path.GetRelativePath(from, file).Split(Path.DirectorySeparatorChar).Any(part => part.StartsWith(StagingPrefix, StringComparison.Ordinal)))
                    .Select(file => new FileInfo(file)).ToArray()
                : [];
            long total = files.Sum(file => file.Length), done = 0;
            if (!string.Equals(Path.GetPathRoot(from), Path.GetPathRoot(to), StringComparison.OrdinalIgnoreCase) && (freeBytesOverride ?? FreeBytes)(to) < total + 64L * 1024 * 1024)
                throw new ModelPackException(L.F($"Not enough free disk space. Moving the AI models needs {FormatSize(total)} free on the drive that holds {to}."));
            created = !Directory.Exists(to);
            Directory.CreateDirectory(to);
            await Task.Run(async () =>
            {
                var buffer = new byte[1 << 20];
                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();
                    string target = Path.Combine(to, Path.GetRelativePath(from, file.FullName));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, true);
                    await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, true);
                    int read;
                    while ((read = await input.ReadAsync(buffer, token)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                        done += read; progress?.Report(total == 0 ? 1 : (double)done / total);
                    }
                }
            }, token);
            created = false;
            // Everything is in the new place; the old folder is the app's own, so it is removed whole (with any unfinished download).
            if (Directory.Exists(from))
            {
                try { Directory.Delete(from, true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            progress?.Report(1);
        }
        catch
        {
            if (created) { try { Directory.Delete(to, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
            else if (Directory.Exists(to)) { try { foreach (var entry in Directory.EnumerateFileSystemEntries(to)) { if (Directory.Exists(entry)) Directory.Delete(entry, true); else File.Delete(entry); } } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
            throw;
        }
        finally { InstallGate.Release(); }
    }
    internal static IReadOnlyList<ModelPack> Catalog => catalogOverride ?? catalog.Value;
    internal static ModelConsentStore Consents(string? root = null) => new(root ?? Root);

    internal static IDisposable UseForTests(IReadOnlyList<ModelPack> packs, string root, HttpMessageHandler handler, Func<string, long>? freeBytes = null)
    {
        foreach (var pack in packs) ModelCatalog.Validate(pack);
        catalogOverride = packs; rootOverride = root; handlerOverride = handler; freeBytesOverride = freeBytes;
        return new Restore();
    }
    private sealed class Restore : IDisposable
    {
        public void Dispose() { catalogOverride = null; rootOverride = null; handlerOverride = null; freeBytesOverride = null; }
    }

    private static readonly object stopLock = new();
    private static CancellationTokenSource stopAll = new();
    /// <summary>Linked into every model and translation pack download so a reset or import can stop them.</summary>
    internal static CancellationToken StopToken { get { lock (stopLock) return stopAll.Token; } }

    /// <summary>
    /// Cancels running model and translation pack downloads and waits until each has closed and removed its files.
    /// Returns false when a download did not finish within <paramref name="timeout"/>. Later downloads work normally.
    /// </summary>
    internal static async Task<bool> StopDownloadsAsync(TimeSpan timeout)
    {
        CancellationTokenSource old;
        lock (stopLock) { old = stopAll; stopAll = new CancellationTokenSource(); }
        old.Cancel();
        if (!await InstallGate.WaitAsync(timeout)) return false;
        InstallGate.Release();
        return true;
    }


    internal static string LicenseText(ModelPack pack) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "Licenses", pack.LicenseFile));

    internal static string LicenseName(string licenseId) => licenseId switch
    {
        "mit" => "MIT", "apache-2.0" => "Apache-2.0", "bsd-2-clause" => "BSD-2-Clause", "bsd-3-clause" => "BSD-3-Clause", "cc-by-4.0" => "CC BY 4.0",
        _ => licenseId
    };

    /// <summary>Decimal units, as publishers and Windows Explorer's download dialogs show them.</summary>
    internal static string FormatSize(long bytes) => bytes switch
    {
        >= 1_000_000_000 => L.F($"{bytes / 1e9:0.0} GB"),
        >= 100_000_000 => L.F($"{bytes / 1e6:0} MB"),
        >= 1_000_000 => L.F($"{bytes / 1e6:0.0} MB"),
        _ => L.F($"{Math.Max(1, bytes / 1000):0} KB")
    };

    private static string PackDirectory(ModelPack pack, string? root) => Path.Combine(root ?? Root, pack.Id);

    /// <summary>Installed when every catalog file is present at its catalog size and the pack was not marked damaged. Hashes are checked at install and at load.</summary>
    internal static ModelPackState StateOf(ModelPack pack, string? root = null)
    {
        string directory = PackDirectory(pack, root);
        try
        {
            if (!Directory.Exists(directory)) return ModelPackState.Missing;
            if (File.Exists(Path.Combine(directory, DamagedMarker))) return ModelPackState.Damaged;
            foreach (var file in pack.Files)
            {
                var info = new FileInfo(Path.Combine(directory, file.File));
                if (!info.Exists || info.Length != file.Bytes) return ModelPackState.Damaged;
            }
            return ModelPackState.Installed;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ModelPackState.Damaged; }
    }

    internal static string? DirectoryFor(ModelPack pack, string? root = null) =>
        StateOf(pack, root) == ModelPackState.Installed ? PackDirectory(pack, root) : null;

    /// <summary>Records that a file failed its hash check after install, so the feature offers a fresh download instead of running it.</summary>
    internal static void MarkDamaged(ModelPack pack, string? root = null)
    {
        string directory = PackDirectory(pack, root);
        try { if (Directory.Exists(directory)) File.WriteAllText(Path.Combine(directory, DamagedMarker), "A file of this pack failed its checksum. Download it again.\n"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    internal static long SizeOnDisk(ModelPack pack, string? root = null)
    {
        string directory = PackDirectory(pack, root);
        try { return Directory.Exists(directory) ? new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>Removes staging folders left by a download that was interrupted (for example, the app was closed).</summary>
    internal static void CleanStaging(string? root = null)
    {
        root ??= Root;
        if (!Directory.Exists(root)) return;
        foreach (string folder in Directory.EnumerateDirectories(root, StagingPrefix + "*"))
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    internal static async Task InstallAsync(ModelPack pack, ModelConsentStore consents, IProgress<double>? progress, CancellationToken token,
        string? root = null, HttpMessageHandler? handler = null, Func<string, long>? freeBytes = null)
    {
        ModelCatalog.Validate(pack);
        if (!consents.IsCurrent(pack, LicenseText(pack)))
            throw new InvalidOperationException("The licence for this model has not been accepted.");
        bool followsSetting = root == null;
        root ??= Root;
        handler ??= handlerOverride;
        freeBytes ??= freeBytesOverride ?? FreeBytes;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, StopToken);
        token = stop.Token;
        await InstallGate.WaitAsync(token);
        // The models folder can move while a download waits for its turn; the licence acceptance was read from the old folder.
        if (followsSetting && !string.Equals(root, Root, StringComparison.OrdinalIgnoreCase))
        {
            InstallGate.Release();
            throw new ModelPackException(L.T("The AI models folder changed. Start the download again."));
        }
        string stage = Path.Combine(root, StagingPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            CleanStaging(root);
            if (StateOf(pack, root) == ModelPackState.Installed) { progress?.Report(1); return; }
            Directory.CreateDirectory(root);
            // Staging and the old copy can both exist briefly, so ask for twice the download size.
            long needed = pack.TotalBytes * 2;
            if (freeBytes(root) < needed)
                throw new ModelPackException(L.F($"Not enough free disk space. This download needs {FormatSize(needed)} free on the drive that holds {root}."));
            Directory.CreateDirectory(stage);
            using var client = handler == null ? null : new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
            await DownloadFilesAsync(pack.Files, stage, progress, token, client ?? sharedClient.Value);
            await File.WriteAllTextAsync(Path.Combine(stage, "LICENSE.txt"), LicenseText(pack), token);
            await File.WriteAllTextAsync(Path.Combine(stage, "ATTRIBUTION.txt"), Attribution(pack), token);
            await File.WriteAllTextAsync(Path.Combine(stage, "SOURCE.txt"), Source(pack), token);
            token.ThrowIfCancellationRequested();
            string destination = PackDirectory(pack, root);
            if (Directory.Exists(destination))
                Directory.Move(destination, Path.Combine(root, StagingPrefix + "old-" + Guid.NewGuid().ToString("N")));
            Directory.Move(stage, destination);
            CleanStaging(root);
            progress?.Report(1);
        }
        finally
        {
            try { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            finally { InstallGate.Release(); }
        }
    }

    internal static void Remove(ModelPack pack, ModelConsentStore consents, string? root = null)
    {
        root ??= Root;
        string directory = PackDirectory(pack, root);
        if (!InstallGate.Wait(0)) throw new ModelPackException(L.T("Wait for the current download to finish, then try again."));
        try
        {
            if (AiRuntime.LoadedModelPaths.Any(loaded => loaded.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) AiRuntime.ReleaseAll();
            if (Directory.Exists(directory))
            {
                // Rename first so a half-deleted folder never looks like a pack. Once renamed, the pack is gone:
                // its consent record goes too, and a folder that cannot be deleted now is removed by the next CleanStaging.
                string trash = Path.Combine(root, StagingPrefix + "remove-" + Guid.NewGuid().ToString("N"));
                Directory.Move(directory, trash);
                try { Directory.Delete(trash, recursive: true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            consents.Remove(pack.Id);
        }
        finally { InstallGate.Release(); }
    }

    /// <summary>
    /// Downloads each file into <paramref name="stage"/>, refusing any byte past its catalog size, a short file, or a wrong SHA-256.
    /// Shared by model and translation packs; callers hold <see cref="InstallGate"/> and own the staging folder.
    /// </summary>
    internal static async Task DownloadFilesAsync(IReadOnlyList<ModelPackFile> files, string stage, IProgress<double>? progress, CancellationToken token, HttpClient client)
    {
        long total = files.Sum(file => file.Bytes), completed = 0;
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            string target = Path.Combine(stage, file.File);
            var uri = new Uri(file.Url);
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(token);
            stall.CancelAfter(StallTimeout);
            try
            {
                using var response = await GetFollowingRedirectsAsync(client, uri, file.File, stall.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new ModelPackException(L.F($"Could not download {file.File}: the server answered {(int)response.StatusCode} {response.ReasonPhrase}."));
                if (response.Content.Headers.ContentLength is long announced && announced != file.Bytes)
                    throw new ModelPackException(L.T("The downloaded file is not the expected model."));
                await using var input = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
                await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                {
                    byte[] buffer = new byte[128 * 1024]; long received = 0; int read;
                    while ((read = await input.ReadAsync(buffer, stall.Token).ConfigureAwait(false)) > 0)
                    {
                        stall.CancelAfter(StallTimeout);
                        received += read;
                        if (received > file.Bytes) throw new ModelPackException(L.T("The downloaded file is not the expected model."));
                        await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                        progress?.Report((double)(completed + received) / total);
                    }
                    if (received != file.Bytes) throw new ModelPackException(L.T("The downloaded file is not the expected model."));
                }
                await using var check = File.OpenRead(target);
                string hash = Convert.ToHexString(await SHA256.HashDataAsync(check, token).ConfigureAwait(false));
                if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new ModelPackException(L.T("The downloaded file is not the expected model."));
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new ModelPackException(L.F($"The download from {uri.Host} stopped responding. Check the connection and try again."));
            }
            catch (HttpRequestException e)
            {
                throw new ModelPackException(L.F($"Could not connect to {uri.Host}. Check the connection and try again. ({e.Message})"), e);
            }
            catch (IOException e) when (e is not FileNotFoundException && !token.IsCancellationRequested && e.InnerException is System.Net.Sockets.SocketException)
            {
                throw new ModelPackException(L.F($"The connection to {uri.Host} was interrupted. Try again."), e);
            }
            completed += file.Bytes;
        }
    }

    /// <summary>Follows at most five redirects, each to an allowed https download server; anything else is refused before it is requested.</summary>
    private static async Task<HttpResponseMessage> GetFollowingRedirectsAsync(HttpClient client, Uri uri, string fileName, CancellationToken token)
    {
        var current = uri;
        for (int hop = 0; ; hop++)
        {
            var response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            int status = (int)response.StatusCode;
            if (status is not (301 or 302 or 303 or 307 or 308) || response.Headers.Location is not { } location) return response;
            var next = location.IsAbsoluteUri ? location : new Uri(current, location);
            string reason = response.ReasonPhrase ?? "";
            response.Dispose();
            if (hop >= MaximumRedirects || !ModelCatalog.IsAllowedDownload(next))
                throw new ModelPackException(L.F($"Could not download {fileName}: the server answered {status} {reason}."));
            current = next;
        }
    }

    private static long FreeBytes(string root)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace; }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { return long.MaxValue; }
    }

    private static string Attribution(ModelPack pack) => new StringBuilder()
        .AppendLine(pack.Name)
        .AppendLine(pack.Attribution)
        .AppendLine("Publisher: " + pack.Publisher)
        .AppendLine("Authors: " + pack.Authors)
        .AppendLine("Licence: " + LicenseName(pack.LicenseId) + " (see LICENSE.txt)")
        .AppendLine("Training data: " + pack.TrainingData)
        .AppendLine()
        .AppendLine("This model is not part of DesktopTools. It was not created, trained, or owned by the DesktopTools project, and DesktopTools is not affiliated with or endorsed by its publisher.")
        .ToString();

    private static string Source(ModelPack pack)
    {
        var text = new StringBuilder()
            .AppendLine("Upstream: " + pack.Upstream)
            .AppendLine("Revision: " + pack.Revision)
            .AppendLine("Downloaded: " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm zzz", System.Globalization.CultureInfo.InvariantCulture))
            .AppendLine("DesktopTools: " + (typeof(ModelPacks).Assembly.GetName().Version?.ToString(3) ?? "unknown"))
            .AppendLine();
        foreach (var file in pack.Files) text.AppendLine($"{file.File}\t{file.Bytes}\tsha256:{file.Sha256.ToLowerInvariant()}\t{file.Url}");
        return text.ToString();
    }
}
