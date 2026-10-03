using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopTools.Extras;

/// <summary>App-managed marker metadata; the original video remains untouched.</summary>
internal sealed class RecordingMarkerStore(string directory)
{
    private sealed class Document
    {
        public int Version { get; set; } = 1;
        public long FileLength { get; set; }
        public long LastWriteUtcTicks { get; set; }
        public List<RecordingMarker> Markers { get; set; } = new();
    }
    private string MetadataPath(string videoPath)
    {
        string full = Path.GetFullPath(videoPath).ToUpperInvariant();
        return Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full))) + ".json");
    }
    internal void Save(string videoPath, IReadOnlyList<RecordingMarker> markers)
    {
        if (markers.Count == 0) return;
        var info = new FileInfo(videoPath);
        if (!info.Exists) throw new FileNotFoundException("The recorded video could not be found for its markers.", videoPath);
        Directory.CreateDirectory(directory);
        var document = new Document { FileLength = info.Length, LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks, Markers = markers.Take(100).ToList() };
        string path = MetadataPath(videoPath), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(document)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal IReadOnlyList<RecordingMarker> Load(string videoPath)
    {
        try
        {
            var info = new FileInfo(videoPath);
            string metadata = MetadataPath(videoPath);
            if (!info.Exists || !File.Exists(metadata) || new FileInfo(metadata).Length > 64 * 1024) return [];
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(metadata));
            if (document?.Version != 1 || document.FileLength != info.Length || document.LastWriteUtcTicks != info.LastWriteTimeUtc.Ticks || document.Markers == null || document.Markers.Count > 100) return [];
            return document.Markers.Where(m => m.Number > 0 && m.Position >= TimeSpan.Zero).OrderBy(m => m.Position).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return []; }
    }
}
