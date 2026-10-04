using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace DesktopTools.Core;

public sealed record LibraryEntry(Guid Id, DateTimeOffset CreatedAt, int Width, int Height, string Text);

/// <summary>
/// The list of screenshots kept for text search, stored as one JSON file next to the images. It knows nothing about OCR or images,
/// so it can be tested without a display. Writes are atomic and a damaged file is set aside instead of being overwritten.
/// </summary>
public sealed class ScreenshotLibraryIndex
{
    private readonly object gate = new();
    private readonly string directory;
    private List<LibraryEntry> entries = [];
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
    private string IndexPath => Path.Combine(directory, "index.json");
    public string ImagePath(Guid id) => Path.Combine(directory, "images", id.ToString("N") + ".png");
    public string ThumbnailPath(Guid id) => Path.Combine(directory, "thumbs", id.ToString("N") + ".jpg");
    public string? RecoveryMessage { get; private set; }

    public ScreenshotLibraryIndex(string directory) { this.directory = Path.GetFullPath(directory); Load(); }

    public IReadOnlyList<LibraryEntry> All { get { lock (gate) return entries.ToArray(); } }
    public int Count { get { lock (gate) return entries.Count; } }

    private void Load()
    {
        lock (gate)
        {
            entries = [];
            if (!File.Exists(IndexPath)) return;
            try
            {
                var loaded = JsonSerializer.Deserialize<List<LibraryEntry>>(File.ReadAllText(IndexPath), Options) ?? throw new JsonException("empty");
                entries = loaded.Where(e => e.Id != Guid.Empty && File.Exists(ImagePath(e.Id))).OrderByDescending(e => e.CreatedAt).ToList();
            }
            catch (JsonException)
            {
                RecoveryMessage = "The screenshot library index was damaged and has been set aside. Images are still in the library folder.";
                try { File.Move(IndexPath, Path.Combine(directory, $"index.damaged-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json")); } catch (IOException) { }
            }
        }
    }

    /// <summary>Adds an entry for an image that has already been written to <see cref="ImagePath"/>. Removes the oldest items above <paramref name="limit"/>.</summary>
    public void Add(LibraryEntry entry, int limit)
    {
        lock (gate)
        {
            entries.RemoveAll(e => e.Id == entry.Id);
            entries.Insert(0, entry);
            while (entries.Count > Math.Max(1, limit)) { DeleteFiles(entries[^1].Id); entries.RemoveAt(entries.Count - 1); }
            Save();
        }
    }

    public bool Remove(Guid id)
    {
        lock (gate)
        {
            int removed = entries.RemoveAll(e => e.Id == id);
            if (removed > 0) { DeleteFiles(id); Save(); }
            return removed > 0;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            foreach (var entry in entries) DeleteFiles(entry.Id);
            entries = []; Save();
            // Also remove strays (files from an interrupted write) so "clear" really leaves nothing behind.
            foreach (string folder in new[] { "images", "thumbs" })
            {
                string path = Path.Combine(directory, folder);
                if (Directory.Exists(path)) foreach (var file in Directory.EnumerateFiles(path)) { try { File.Delete(file); } catch (IOException) { } }
            }
        }
    }

    /// <summary>Entries whose text contains every word of <paramref name="query"/>, ignoring case and accents. Newest first. An empty query returns everything.</summary>
    public IReadOnlyList<LibraryEntry> Search(string query)
    {
        var words = Tokenize(query);
        lock (gate)
        {
            if (words.Length == 0) return entries.ToArray();
            return entries.Where(e => { string haystack = Normalize(e.Text); return words.All(haystack.Contains); }).ToArray();
        }
    }

    /// <summary>The first line of the entry's text that contains a query word, for showing why it matched.</summary>
    public static string Snippet(LibraryEntry entry, string query, int maxLength = 90)
    {
        var words = Tokenize(query);
        string line = entry.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => words.Length > 0 && words.Any(Normalize(l).Contains))
            ?? entry.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        return line.Length <= maxLength ? line : line[..(maxLength - 1)] + "…";
    }

    private static string[] Tokenize(string query) => Normalize(query).Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    internal static string Normalize(string text)
    {
        var decomposed = (text ?? "").Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    private void DeleteFiles(Guid id)
    {
        foreach (string path in new[] { ImagePath(id), ThumbnailPath(id) }) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    private void Save()
    {
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, "index-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(entries, Options)); File.Move(temporary, IndexPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
