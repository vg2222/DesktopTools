using System.Globalization;
using System.Text;

namespace DesktopTools.Core;

/// <summary>One timed line of a transcript, in the original video's time.</summary>
public sealed record TranscriptLine(TimeSpan Start, TimeSpan End, string Text);

public enum CaptionFormat { Srt, Vtt, Text }

/// <summary>Pure caption formatting (SRT, WebVTT, plain text), SRT parsing and retiming to an edited copy. No I/O.</summary>
public static class CaptionFiles
{
    /// <summary>Lines shorter than this after retiming are left out; a caption that flashes for a few frames cannot be read.</summary>
    public static readonly TimeSpan MinimumRetimedLength = TimeSpan.FromSeconds(.2);

    public static string Extension(CaptionFormat format) => format switch { CaptionFormat.Vtt => ".vtt", CaptionFormat.Text => ".txt", _ => ".srt" };

    public static string Format(IReadOnlyList<TranscriptLine> lines, CaptionFormat format)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var output = new StringBuilder();
        if (format == CaptionFormat.Vtt) output.Append("WEBVTT\n");
        int number = 0;
        foreach (var line in lines)
        {
            var parts = CleanParts(line.Text);
            if (parts.Count == 0) continue;
            number++;
            switch (format)
            {
                case CaptionFormat.Srt:
                    if (number > 1) output.Append("\r\n");
                    output.Append(number.ToString(CultureInfo.InvariantCulture)).Append("\r\n")
                        .Append(Stamp(line.Start, ',')).Append(" --> ").Append(Stamp(line.End, ',')).Append("\r\n");
                    foreach (var part in parts) output.Append(part).Append("\r\n");
                    break;
                case CaptionFormat.Vtt:
                    output.Append('\n').Append(Stamp(line.Start, '.')).Append(" --> ").Append(Stamp(line.End, '.')).Append('\n');
                    // WebVTT cue text is markup: &, < and > are written as character references.
                    foreach (var part in parts)
                        output.Append(part.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal)).Append('\n');
                    break;
                default:
                    output.Append('[').Append(ShortStamp(line.Start)).Append("] ").Append(string.Join(' ', parts)).Append('\n');
                    break;
            }
        }
        return output.ToString();
    }

    /// <summary>Reads the cues of an SRT file. Malformed blocks are skipped; never throws on content.</summary>
    public static IReadOnlyList<TranscriptLine> ParseSrt(string text)
    {
        var result = new List<TranscriptLine>();
        if (string.IsNullOrEmpty(text)) return result;
        var rows = text.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < rows.Length; i++)
        {
            int arrow = rows[i].IndexOf("-->", StringComparison.Ordinal);
            if (arrow < 0 || !TryParseStamp(rows[i][..arrow], out var start) || !TryParseStamp(rows[i][(arrow + 3)..], out var end)) continue;
            var body = new List<string>();
            while (i + 1 < rows.Length && rows[i + 1].Trim().Length > 0) body.Add(rows[++i].Trim());
            if (body.Count > 0) result.Add(new TranscriptLine(start, end, string.Join("\n", body)));
        }
        return result;
    }

    /// <summary>
    /// Maps lines from the original video's time to the edited copy's time: lines outside the kept segments are dropped, lines across a
    /// boundary are clipped (one output line per segment they touch), and later segments shift left by what was removed before them.
    /// </summary>
    public static IReadOnlyList<TranscriptLine> Retime(IReadOnlyList<TranscriptLine> lines, VideoEdit edit, VideoInfo source)
    {
        ArgumentNullException.ThrowIfNull(lines); ArgumentNullException.ThrowIfNull(edit);
        var segments = edit.Segments(source);
        var result = new List<TranscriptLine>();
        foreach (var line in lines.OrderBy(l => l.Start))
        {
            var offset = TimeSpan.Zero;
            foreach (var segment in segments)
            {
                var segmentStart = TimeSpan.FromSeconds(segment.Start);
                var segmentEnd = TimeSpan.FromSeconds(segment.End);
                var start = line.Start > segmentStart ? line.Start : segmentStart;
                var end = line.End < segmentEnd ? line.End : segmentEnd;
                // A line cut by a boundary keeps a piece only if it is long enough to read; a short line wholly inside a segment stays.
                bool clipped = start != line.Start || end != line.End;
                if (end > start && (!clipped || end - start >= MinimumRetimedLength)) result.Add(new TranscriptLine(start - segmentStart + offset, end - segmentStart + offset, line.Text));
                offset += segmentEnd - segmentStart;
            }
        }
        return result;
    }

    private static List<string> CleanParts(string? text) =>
        (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select(part => part.Replace("-->", "→", StringComparison.Ordinal).Trim()).Where(part => part.Length > 0).ToList();

    private static long Milliseconds(TimeSpan time) => Math.Max(0, (long)Math.Round(time.TotalMilliseconds, MidpointRounding.AwayFromZero));

    private static string Stamp(TimeSpan time, char separator)
    {
        long ms = Milliseconds(time);
        return string.Create(CultureInfo.InvariantCulture, $"{ms / 3_600_000:00}:{ms / 60_000 % 60:00}:{ms / 1000 % 60:00}{separator}{ms % 1000:000}");
    }

    private static string ShortStamp(TimeSpan time)
    {
        long seconds = Milliseconds(time) / 1000;
        return seconds >= 3600
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }

    private static bool TryParseStamp(string value, out TimeSpan time)
    {
        time = default;
        var parts = value.Trim().Replace('.', ',').Split(':');
        if (parts.Length != 3) return false;
        var secondParts = parts[2].Split(',');
        if (secondParts.Length != 2) return false;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int hours) || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
            || !int.TryParse(secondParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) || !int.TryParse(secondParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int milliseconds)
            || minutes > 59 || seconds > 59 || milliseconds > 999) return false;
        time = new TimeSpan(0, hours, minutes, seconds, milliseconds);
        return true;
    }
}
