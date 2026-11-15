using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopTools.Core;

namespace DesktopTools.Extras;

/// <summary>A video's working transcript: its lines, the language and model that made it, and the hash of the caption file DesktopTools last wrote.</summary>
internal sealed record TranscriptDocument(IReadOnlyList<TranscriptLine> Lines, string Language, string Model, string? CaptionSha256);

/// <summary>
/// App-managed transcripts, one per video, kept like recording markers: keyed by the video's full path and dropped when the video's size or
/// last-write time changes. The video itself is never touched. <see cref="WriteCaptionFile"/> replaces a caption file next to the video without
/// asking only when it is byte-for-byte the file this store wrote last.
/// </summary>
internal sealed class TranscriptStore(string directory)
{
    internal const long MaxBytes = 16 * 1024 * 1024;
    private static readonly UTF8Encoding CaptionEncoding = new(encoderShouldEmitUTF8Identifier: true);

    private sealed class Line
    {
        public long Start { get; set; }
        public long End { get; set; }
        public string Text { get; set; } = "";
    }
    private sealed class Document
    {
        public int Version { get; set; } = 1;
        public long FileLength { get; set; }
        public long LastWriteUtcTicks { get; set; }
        public string Language { get; set; } = "";
        public string Model { get; set; } = "";
        public string? CaptionSha256 { get; set; }
        public List<Line> Lines { get; set; } = new();
    }

    internal string Directory => directory;

    private string MetadataPath(string videoPath)
    {
        string full = Path.GetFullPath(videoPath).ToUpperInvariant();
        return Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full))) + ".json");
    }

    /// <summary>The caption file transcription writes next to a video: same folder and name, .srt extension.</summary>
    internal static string CaptionPath(string videoPath) => Path.ChangeExtension(Path.GetFullPath(videoPath), ".srt");

    internal void Save(string videoPath, TranscriptDocument transcript)
    {
        var info = new FileInfo(videoPath);
        if (!info.Exists) throw new FileNotFoundException("The video could not be found for its transcript.", videoPath);
        var document = new Document
        {
            FileLength = info.Length, LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
            Language = transcript.Language ?? "", Model = transcript.Model ?? "", CaptionSha256 = transcript.CaptionSha256,
            Lines = transcript.Lines.Select(line => new Line { Start = line.Start.Ticks, End = line.End.Ticks, Text = line.Text }).ToList()
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(document);
        if (json.Length > MaxBytes) throw new InvalidDataException("The transcript is too large to keep.");
        System.IO.Directory.CreateDirectory(directory);
        string path = MetadataPath(videoPath), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, json); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>The stored transcript, or null when there is none, the video changed since, or the file is unreadable or too large.</summary>
    internal TranscriptDocument? Load(string videoPath)
    {
        try
        {
            var info = new FileInfo(videoPath);
            var metadata = new FileInfo(MetadataPath(videoPath));
            if (!info.Exists || !metadata.Exists || metadata.Length > MaxBytes) return null;
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllBytes(metadata.FullName));
            if (document?.Version != 1 || document.FileLength != info.Length || document.LastWriteUtcTicks != info.LastWriteTimeUtc.Ticks || document.Lines == null) return null;
            var lines = document.Lines
                .Where(line => line != null && line.Start >= 0 && line.End > line.Start && line.Text != null)
                .Select(line => new TranscriptLine(TimeSpan.FromTicks(line.Start), TimeSpan.FromTicks(line.End), line.Text))
                .OrderBy(line => line.Start).ToArray();
            return new TranscriptDocument(lines, document.Language ?? "", document.Model ?? "", document.CaptionSha256);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException) { return null; }
    }

    /// <summary>
    /// Writes <paramref name="lines"/> as SubRip next to the video. An existing file this store did not write last is replaced only when
    /// <paramref name="confirmReplace"/> (given the caption path) returns true. Returns the path written, or null when the user declined.
    /// The hash of the written file is recorded with the video's transcript.
    /// </summary>
    internal string? WriteCaptionFile(string videoPath, IReadOnlyList<TranscriptLine> lines, Func<string, bool> confirmReplace)
    {
        string target = CaptionPath(videoPath);
        var stored = Load(videoPath);
        if (File.Exists(target))
        {
            string existing = Hash(File.ReadAllBytes(target));
            bool ours = stored?.CaptionSha256 is { } known && known.Equals(existing, StringComparison.OrdinalIgnoreCase);
            if (!ours && !confirmReplace(target)) return null;
        }
        byte[] bytes = CaptionEncoding.GetPreamble().Concat(CaptionEncoding.GetBytes(CaptionFiles.Format(lines, CaptionFormat.Srt))).ToArray();
        string temporary = Path.Combine(Path.GetDirectoryName(target)!, "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, target, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        Save(videoPath, (stored ?? new TranscriptDocument(lines, "", "", null)) with { CaptionSha256 = Hash(bytes) });
        return target;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
