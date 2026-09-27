using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopTools.Localization;

namespace DesktopTools.Updates;

internal sealed record Announcement(string Id, string Kind, DateTimeOffset PublishedUtc,
    IReadOnlyDictionary<string, string> Title, IReadOnlyDictionary<string, string> Message,
    Uri? Url, string? MinimumSafeVersion)
{
    internal bool IsSecurity => Kind == "security";
    internal string LocalTitle => Title.TryGetValue(L.Language, out var value) ? value : Title["en"];
    internal string LocalMessage => Message.TryGetValue(L.Language, out var value) ? value : Message["en"];
    internal bool Affects(string version) => IsSecurity && MinimumSafeVersion != null &&
        GitHubReleaseClient.IsNewer(MinimumSafeVersion, version);
}

internal sealed record AnnouncementFeed(IReadOnlyList<Announcement> Items)
{
    internal static AnnouncementFeed Parse(byte[] data, DateTimeOffset? clock = null)
    {
        if (data.Length > 65536) throw new InvalidDataException("The announcement feed is too large.");
        using var json = JsonDocument.Parse(data);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported announcement feed.");
        var entries = root.GetProperty("items");
        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 20)
            throw new InvalidDataException("Invalid announcement count.");
        var now = clock ?? DateTimeOffset.UtcNow;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<Announcement>();
        foreach (var entry in entries.EnumerateArray())
        {
            string id = Required(entry, "id", 80);
            if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{0,79}$") || !ids.Add(id))
                throw new InvalidDataException("Invalid or duplicate announcement ID.");
            string kind = Required(entry, "kind", 16);
            if (kind is not ("news" or "security")) throw new InvalidDataException("Invalid announcement kind.");
            var published = DateTimeOffset.Parse(Required(entry, "publishedUtc", 40),
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal);
            if (published > now.AddDays(1)) throw new InvalidDataException("Announcement date is in the future.");
            var title = Localized(entry, "title", 100);
            var message = Localized(entry, "message", 500);
            Uri? url = null;
            if (entry.TryGetProperty("url", out var link) && link.ValueKind != JsonValueKind.Null)
            {
                string value = link.GetString() ?? "";
                if (!Uri.TryCreate(value, UriKind.Absolute, out url) || url.Scheme != "https" ||
                    url.Host != "github.com" || !(url.AbsoluteUri == GitHubReleaseClient.RepositoryUrl ||
                    url.AbsoluteUri.StartsWith(GitHubReleaseClient.RepositoryUrl + "/", StringComparison.Ordinal)))
                    throw new InvalidDataException("Announcement link must stay in the DesktopTools repository.");
            }
            string? safeVersion = null;
            if (kind == "security")
            {
                safeVersion = Required(entry, "minimumSafeVersion", 24);
                if (!Regex.IsMatch(safeVersion, @"^\d+\.\d+\.\d+$") || !Version.TryParse(safeVersion, out _))
                    throw new InvalidDataException("Invalid minimum safe version.");
                if (url == null) throw new InvalidDataException("A security alert needs a details link.");
            }
            else if (entry.TryGetProperty("expiresUtc", out var expiry) && expiry.ValueKind == JsonValueKind.String &&
                     DateTimeOffset.Parse(expiry.GetString()!, System.Globalization.CultureInfo.InvariantCulture,
                         System.Globalization.DateTimeStyles.AssumeUniversal) <= now) continue;
            items.Add(new Announcement(id, kind, published, title, message, url, safeVersion));
        }
        return new(items.OrderByDescending(item => item.PublishedUtc).ToArray());
    }

    private static string Required(JsonElement entry, string name, int maximum)
    {
        string value = entry.GetProperty(name).GetString() ?? "";
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(char.IsControl))
            throw new InvalidDataException("Invalid announcement " + name + ".");
        return value;
    }
    private static IReadOnlyDictionary<string, string> Localized(JsonElement entry, string name, int maximum)
    {
        var source = entry.GetProperty(name);
        if (source.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid localized announcement text.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in source.EnumerateObject())
        {
            if (!L.Languages.Contains(property.Name)) continue;
            string value = property.Value.GetString() ?? "";
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(c => char.IsControl(c) && c is not '\n'))
                throw new InvalidDataException("Invalid localized announcement text.");
            values.Add(property.Name, value);
        }
        if (!values.ContainsKey("en")) throw new InvalidDataException("English announcement text is required.");
        return values;
    }
}

internal sealed class AnnouncementClient : IDisposable
{
    private static readonly Uri FeedUrl = new("https://raw.githubusercontent.com/vg2222/DesktopTools/main/news/announcements.json");
    private readonly HttpClient http;
    private readonly bool ownsClient;
    internal AnnouncementClient(HttpClient? client = null)
    {
        ownsClient = client == null;
        http = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    }
    internal static AnnouncementFeed LoadBundled()
    {
        using var stream = typeof(AnnouncementClient).Assembly.GetManifestResourceStream("DesktopTools.Announcements")
            ?? throw new InvalidDataException("Bundled announcements are missing.");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer);
        return AnnouncementFeed.Parse(buffer.ToArray());
    }
    internal async Task<AnnouncementFeed?> GetLatestAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, FeedUrl);
        request.Headers.UserAgent.ParseAdd("DesktopTools-News/1.0");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length > 65536)
            throw new InvalidDataException("The announcement feed is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192]; int count;
        while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
        {
            if (output.Length + count > 65536) throw new InvalidDataException("The announcement feed is too large.");
            output.Write(buffer, 0, count);
        }
        return AnnouncementFeed.Parse(output.ToArray());
    }
    public void Dispose() { if (ownsClient) http.Dispose(); }
}
