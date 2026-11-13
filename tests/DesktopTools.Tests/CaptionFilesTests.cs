using DesktopTools.Core;

/// <summary>Caption files: SRT, WebVTT and plain text formatting, SRT parsing, and retiming to a trimmed or cut video.</summary>
internal static class CaptionFilesTests
{
    public static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);
        var lines = new[] { new TranscriptLine(S(.5), S(2.25), "Hello"), new TranscriptLine(S(3), S(3604.005), "World") };

        string srt = CaptionFiles.Format(lines, CaptionFormat.Srt);
        Check(srt == "1\r\n00:00:00,500 --> 00:00:02,250\r\nHello\r\n\r\n2\r\n00:00:03,000 --> 01:00:04,005\r\nWorld\r\n", "SRT: " + srt);
        string vtt = CaptionFiles.Format(lines, CaptionFormat.Vtt);
        Check(vtt == "WEBVTT\n\n00:00:00.500 --> 00:00:02.250\nHello\n\n00:00:03.000 --> 01:00:04.005\nWorld\n", "VTT: " + vtt);
        string text = CaptionFiles.Format(lines, CaptionFormat.Text);
        Check(text == "[0:00] Hello\n[0:03] World\n", "TXT: " + text);
        string markup = CaptionFiles.Format([new TranscriptLine(S(0), S(1), "a < b & c > d")], CaptionFormat.Vtt);
        Check(markup.EndsWith("\na &lt; b &amp; c &gt; d\n", StringComparison.Ordinal), "VTT escapes markup: " + markup);
        var late = CaptionFiles.Format([new TranscriptLine(S(3725), S(3726), "Late")], CaptionFormat.Text);
        Check(late == "[1:02:05] Late\n", "TXT hours: " + late);

        // Blank lines and arrows would break the cue structure; empty lines are left out.
        var awkward = new[] { new TranscriptLine(S(0), S(1), "a\n\n\nb"), new TranscriptLine(S(1), S(2), "x --> y"), new TranscriptLine(S(2), S(3), "  \n ") };
        string awkwardSrt = CaptionFiles.Format(awkward, CaptionFormat.Srt);
        Check(awkwardSrt == "1\r\n00:00:00,000 --> 00:00:01,000\r\na\r\nb\r\n\r\n2\r\n00:00:01,000 --> 00:00:02,000\r\nx → y\r\n", "Awkward SRT: " + awkwardSrt);

        var parsed = CaptionFiles.ParseSrt(srt);
        Check(parsed.SequenceEqual(lines), "SRT round trip");
        Check(CaptionFiles.ParseSrt("﻿1\n00:00:01,000 --> 00:00:02,000\nA\nB\n\n").Single() == new TranscriptLine(S(1), S(2), "A\nB"), "SRT with BOM and LF");
        Check(CaptionFiles.ParseSrt("garbage").Count == 0, "Not SRT");
        Check(CaptionFiles.Extension(CaptionFormat.Srt) == ".srt" && CaptionFiles.Extension(CaptionFormat.Vtt) == ".vtt" && CaptionFiles.Extension(CaptionFormat.Text) == ".txt", "Extensions");

        // Keep 10-50 s, remove 20-30 s: output is 10-20 -> 0-10 and 30-50 -> 10-30.
        var source = new VideoInfo("v.mp4", 60, 640, 360, true);
        var edit = new VideoEdit(10, 50, true, 20, 30);
        var timed = new[]
        {
            new TranscriptLine(S(5), S(8), "before"), new TranscriptLine(S(12), S(15), "kept"), new TranscriptLine(S(18), S(22), "into cut"),
            new TranscriptLine(S(25), S(28), "removed"), new TranscriptLine(S(29), S(33), "out of cut"), new TranscriptLine(S(48), S(55), "past end"),
            new TranscriptLine(S(19.9), S(20.05), "too short"),
        };
        var retimed = CaptionFiles.Retime(timed, edit, source);
        var expected = new[]
        {
            new TranscriptLine(S(2), S(5), "kept"), new TranscriptLine(S(8), S(10), "into cut"),
            new TranscriptLine(S(10), S(13), "out of cut"), new TranscriptLine(S(28), S(30), "past end"),
        };
        Check(retimed.SequenceEqual(expected), "Retimed: " + string.Join(" | ", retimed));
        var shifted = CaptionFiles.Retime([new TranscriptLine(S(12), S(15), "x")], new VideoEdit(10, 60), source);
        Check(shifted.Single() == new TranscriptLine(S(2), S(5), "x"), "Trim only shifts");
        var brief = CaptionFiles.Retime([new TranscriptLine(S(12), S(12.125), "Hi")], new VideoEdit(10, 60), source);
        Check(brief.Single() == new TranscriptLine(S(2), S(2.125), "Hi"), "A short line inside the kept part stays: " + string.Join(" | ", brief));
        // A line spanning the removed section continues on both sides of it.
        var spanning = CaptionFiles.Retime([new TranscriptLine(S(18), S(33), "long")], edit, source);
        Check(spanning.SequenceEqual([new TranscriptLine(S(8), S(10), "long"), new TranscriptLine(S(10), S(13), "long")]), "Spanning line");
    }
}
