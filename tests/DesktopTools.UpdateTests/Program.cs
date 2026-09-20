using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopTools.Core;
using DesktopTools.Updates;

int failed = 0;
async Task Test(string name, Func<Task> test)
{ try { await test(); Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); } }
void Check(bool value, string message) { if (!value) throw new Exception(message); }
async Task Reject(Func<Task> action)
{ try { await action(); } catch (Exception ex) when (ex is InvalidDataException or HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException) { return; } throw new Exception("Invalid input was accepted"); }
string repo = GitHubReleaseClient.RepositoryUrl;
byte[] installer = Encoding.UTF8.GetBytes("fixture, never executable");
string name = "DesktopTools-2.0.0-win-x64-setup.exe";
string root = Path.GetFullPath(Path.Combine("artifacts", "update-tests-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
string Json(bool draft = false, bool prerelease = false, string tag = "v2.0.0") => JsonSerializer.Serialize(new
{
    draft, prerelease, tag_name = tag,
    assets = new[] { new { name, size = installer.Length, browser_download_url = repo + "/releases/download/" + tag + "/" + name },
        new { name = "SHA256SUMS.txt", size = 200, browser_download_url = repo + "/releases/download/" + tag + "/SHA256SUMS.txt" } }
});
GitHubRelease Release() { using var json = JsonDocument.Parse(Json()); return GitHubReleaseClient.ParseRelease(json.RootElement)!; }
await Test("Stable version ordering and release asset boundaries", async () =>
{
    Check(GitHubReleaseClient.IsNewer("v1.10.0", "1.9.9"), "Compared versions as text");
    Check(!GitHubReleaseClient.IsNewer("1.0.0", "1.0.0"), "Equal version updates");
    using var draft = JsonDocument.Parse(Json(draft: true)); using var preview = JsonDocument.Parse(Json(prerelease: true));
    Check(GitHubReleaseClient.ParseRelease(draft.RootElement) == null && GitHubReleaseClient.ParseRelease(preview.RootElement) == null, "Unstable release accepted");
    await Reject(() => { using var bad = JsonDocument.Parse(Json().Replace("https://github.com/vg2222/DesktopTools", "https://example.com/evil")); GitHubReleaseClient.ParseRelease(bad.RootElement); return Task.CompletedTask; });
    await Reject(() => { GitHubReleaseClient.IsNewer("2.0.0-beta", "1.0.0"); return Task.CompletedTask; });
});
await Test("Public GitHub check, 404 and rate limit", async () =>
{
    using var http = new HttpClient(new Handler(request =>
    {
        Check(request.Headers.UserAgent.ToString().Contains("DesktopTools"), "Missing User-Agent");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json()) };
    }));
    using var client = new GitHubReleaseClient(http);
    Check((await client.GetLatestAsync())!.Version == "2.0.0", "Latest release not parsed");
    using var notFoundHttp = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
    using var absent = new GitHubReleaseClient(notFoundHttp); Check(await absent.GetLatestAsync() == null, "No-release state not handled");
    using var limitedHttp = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
    using var limited = new GitHubReleaseClient(limitedHttp); await Reject(async () => await limited.GetLatestAsync());
});
await Test("Verified download, corruption, cancellation and original preservation", async () =>
{
    var release = Release(); string expectedHash = Convert.ToHexString(SHA256.HashData(installer));
    bool corrupt = false;
    using var http = new HttpClient(new Handler(request => new HttpResponseMessage(HttpStatusCode.OK)
    { Content = request.RequestUri!.AbsoluteUri.EndsWith("SHA256SUMS.txt") ? new StringContent(expectedHash + "  " + name + "\n") : new ByteArrayContent(corrupt ? new byte[installer.Length] : installer) }));
    using var client = new GitHubReleaseClient(http);
    string downloaded = await client.DownloadInstallerAsync(release, root);
    Check(File.ReadAllBytes(downloaded).SequenceEqual(installer), "Downloaded data changed");
    corrupt = true; await Reject(async () => await client.DownloadInstallerAsync(release, root));
    Check(File.ReadAllBytes(downloaded).SequenceEqual(installer), "Failed download replaced previously verified installer");
    Check(!Directory.EnumerateFiles(root, "*.partial").Any(), "Partial installer retained");
    using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Reject(async () => await client.DownloadInstallerAsync(release, root, token: cancel.Token));
    await Reject(async () => await client.DownloadInstallerAsync(release with { InstallerName = "../evil.exe" }, root));
    await Reject(async () => await client.DownloadInstallerAsync(release with { InstallerSize = installer.Length - 1 }, root));
});
await Test("Redirect and metadata size limits", async () =>
{
    using var http = new HttpClient(new Handler(_ => { var r = new HttpResponseMessage(HttpStatusCode.Redirect); r.Headers.Location = new Uri("https://example.com/download.exe"); return r; }));
    using var client = new GitHubReleaseClient(http); await Reject(async () => await client.GetLatestAsync());
    using var hugeHttp = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 1024 * 1024 + 1)) }));
    using var huge = new GitHubReleaseClient(hugeHttp); await Reject(async () => await huge.GetLatestAsync());
});
await Test("Notifications wait for activity, pause and extend", () =>
{
    var duration = TimeSpan.FromSeconds(6); var lifetime = new NotificationLifetime(duration, 20);
    Check(!lifetime.Advance(TimeSpan.FromHours(4), 20, false) && lifetime.Remaining == duration, "AFK notification expired");
    Check(!lifetime.Advance(TimeSpan.FromHours(4), 21, false) && lifetime.Remaining == duration, "Returning activity consumed AFK time");
    Check(!lifetime.Advance(TimeSpan.FromHours(4), 21, true), "Hovered notification expired");
    Check(!lifetime.Advance(TimeSpan.FromSeconds(5), 21, false), "Notice expired early");
    Check(lifetime.Advance(TimeSpan.FromSeconds(1), 21, false), "Active notice never expires");
    lifetime.Reset(TimeSpan.FromSeconds(30), 21);
    Check(!lifetime.Advance(TimeSpan.FromMinutes(1), 21, false), "Extended notice did not wait for activity");
    lifetime.Advance(TimeSpan.Zero, 22, false); Check(!lifetime.Advance(TimeSpan.FromSeconds(20), 22, false), "Release-notes duration not extended");
    var unknown = new NotificationLifetime(duration, null); Check(!unknown.Advance(TimeSpan.FromHours(1), null, false), "Unavailable input clock dismissed notice");
    return Task.CompletedTask;
});
Console.WriteLine("Failures: " + failed); return failed == 0 ? 0 : 1;

sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
}
