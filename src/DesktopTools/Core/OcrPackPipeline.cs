using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;

namespace DesktopTools.Core;

/// <summary>The script groups the optional OCR pack has a recognizer for.</summary>
public enum OcrPackScript { Latin, Cyrillic, ChineseJapanese }

/// <summary>A text line found by the detector, in source pixels.</summary>
public sealed record OcrPackBox(int X, int Y, int Width, int Height, float Score);

/// <summary>A horizontal band of the picture the detector reads at once. Boxes are kept by the strip whose core holds their centre.</summary>
public sealed record OcrPackStrip(int Top, int Height, int CoreTop, int CoreBottom);

/// <summary>A recognized line: its text, mean character confidence, and each word's first and last CTC time step.</summary>
public sealed record OcrPackDecoded(string Text, float Confidence, IReadOnlyList<(int FirstStep, int LastStep, string Word)> Words);

/// <summary>A detected box with its reading; <see cref="InputWidth"/> and <see cref="Steps"/> map time steps back to pixels.</summary>
public sealed record OcrPackLine(OcrPackBox Box, OcrPackDecoded Decoded, int InputWidth, int Steps);

/// <summary>
/// Pure pre- and post-processing for the PP-OCRv5 pack (spec section 8, contracts in docs/ai-models.md "Task 4"): character lists,
/// detection strips, DB box finding, recognition batches, greedy CTC decoding and the word layout the text view uses. No I/O, no model.
/// </summary>
public static class OcrPackPipeline
{
    public const string TagPrefix = "pack:";
    public const string DetectorPackId = "ocr-text-detector";
    /// <summary>The recognizer's input height in pixels.</summary>
    public const int LineHeight = 48;
    public const float BinaryThreshold = .3f, BoxThreshold = .6f, UnclipRatio = 1.5f;
    public const int MaxBoxes = 10_000;

    public static string PackId(OcrPackScript script) => script switch
    {
        OcrPackScript.Cyrillic => "ocr-cyrillic", OcrPackScript.ChineseJapanese => "ocr-chinese-japanese", _ => "ocr-latin"
    };

    /// <summary>The value saved in settings and used as the reading tag's suffix.</summary>
    public static string Key(OcrPackScript script) => script switch
    {
        OcrPackScript.Cyrillic => "cyrillic", OcrPackScript.ChineseJapanese => "chinese-japanese", _ => "latin"
    };

    public static bool TryParseKey(string? key, out OcrPackScript script)
    {
        foreach (var candidate in Enum.GetValues<OcrPackScript>())
            if (string.Equals(Key(candidate), key, StringComparison.Ordinal)) { script = candidate; return true; }
        script = OcrPackScript.Latin; return false;
    }

    public static string Tag(OcrPackScript script) => TagPrefix + Key(script);

    public static bool TryParseTag(string? tag, out OcrPackScript script)
    {
        script = OcrPackScript.Latin;
        return tag != null && tag.StartsWith(TagPrefix, StringComparison.Ordinal) && TryParseKey(tag[TagPrefix.Length..], out script);
    }

    /// <summary>The recognizer that fits a Windows language: Russian, Ukrainian and Belarusian use East Slavic; Chinese and Japanese theirs; all else Latin.</summary>
    public static OcrPackScript ScriptForLanguage(string? windowsTag)
    {
        string primary = (windowsTag ?? "").Split('-')[0].ToLowerInvariant();
        return primary switch { "ru" or "uk" or "be" => OcrPackScript.Cyrillic, "zh" or "ja" => OcrPackScript.ChineseJapanese, _ => OcrPackScript.Latin };
    }

    /// <summary>The entries under <c>character_dict:</c> of a recognizer's inference.yml, in order. Ordinal comparisons only: a culture-aware one drops entries.</summary>
    public static string[] ParseCharacterList(string yml)
    {
        var list = new List<string>();
        bool inList = false;
        foreach (string raw in yml.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            string trimmed = line.TrimStart(' ');
            if (!inList)
            {
                if (trimmed.StartsWith("character_dict:", StringComparison.Ordinal)) inList = true;
                continue;
            }
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                if (list.Count > 0 || trimmed.Length > 0) break;
                continue;
            }
            string value = trimmed[2..];
            if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'') value = value[1..^1].Replace("''", "'", StringComparison.Ordinal);
            else if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = Unescape(value[1..^1]);
            list.Add(value);
        }
        return list.ToArray();
    }

    private static string Unescape(string text)
    {
        var result = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\\' || i + 1 >= text.Length) { result.Append(c); continue; }
            char next = text[++i];
            int hex = next switch { 'x' => 2, 'u' => 4, 'U' => 8, _ => 0 };
            if (hex > 0 && i + hex < text.Length && int.TryParse(text.AsSpan(i + 1, hex), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
            { result.Append(char.ConvertFromUtf32(code)); i += hex; continue; }
            result.Append(next switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '0' => '\0', _ => next });
        }
        return result.ToString();
    }

    /// <summary>Bands of at most <paramref name="stripHeight"/> rows that overlap by at least <paramref name="overlap"/>, with contiguous cores.</summary>
    public static IReadOnlyList<OcrPackStrip> Strips(int height, int stripHeight = 1024, int overlap = 160)
    {
        if (height <= stripHeight) return [new OcrPackStrip(0, Math.Max(0, height), 0, Math.Max(0, height))];
        var tops = new List<int> { 0 };
        while (tops[^1] + stripHeight < height) tops.Add(Math.Min(tops[^1] + stripHeight - overlap, height - stripHeight));
        var strips = new List<OcrPackStrip>();
        for (int i = 0; i < tops.Count; i++)
        {
            int coreTop = i == 0 ? 0 : (tops[i] + tops[i - 1] + stripHeight) / 2;
            int coreBottom = i == tops.Count - 1 ? height : (tops[i + 1] + tops[i] + stripHeight) / 2;
            strips.Add(new OcrPackStrip(tops[i], stripHeight, coreTop, coreBottom));
        }
        return strips;
    }

    /// <summary>
    /// DB post-processing on one strip's probability map (row-major, <paramref name="width"/> × <paramref name="height"/>, of which the top-left
    /// <paramref name="validWidth"/> × <paramref name="validHeight"/> is picture and the rest padding): 4-connected regions above 0.3, mean probability
    /// at least 0.6, both sides at least 3 pixels, grown by area × 1.5 / perimeter, clamped to the picture, shifted down by <paramref name="offsetY"/>,
    /// and kept only when their centre lies in the strip's core.
    /// </summary>
    public static IReadOnlyList<OcrPackBox> FindBoxes(float[] probability, int width, int height, int validWidth, int validHeight, int offsetY, OcrPackStrip strip)
    {
        if (probability.Length < (long)width * height) throw new ArgumentException("The probability map is smaller than its size.", nameof(probability));
        validWidth = Math.Min(validWidth, width); validHeight = Math.Min(validHeight, height);
        var visited = new bool[width * height];
        var stack = new Stack<int>();
        var boxes = new List<OcrPackBox>();
        for (int y = 0; y < validHeight; y++)
            for (int x = 0; x < validWidth; x++)
            {
                int start = y * width + x;
                if (visited[start] || probability[start] <= BinaryThreshold) continue;
                visited[start] = true; stack.Push(start);
                int x0 = x, x1 = x, y0 = y, y1 = y, count = 0; double sum = 0;
                while (stack.Count > 0)
                {
                    int index = stack.Pop(), px = index % width, py = index / width;
                    sum += probability[index]; count++;
                    if (px < x0) x0 = px; if (px > x1) x1 = px; if (py < y0) y0 = py; if (py > y1) y1 = py;
                    void Visit(int qx, int qy)
                    {
                        if (qx < 0 || qy < 0 || qx >= validWidth || qy >= validHeight) return;
                        int q = qy * width + qx;
                        if (visited[q] || probability[q] <= BinaryThreshold) return;
                        visited[q] = true; stack.Push(q);
                    }
                    Visit(px + 1, py); Visit(px - 1, py); Visit(px, py + 1); Visit(px, py - 1);
                }
                int boxWidth = x1 - x0 + 1, boxHeight = y1 - y0 + 1;
                float score = (float)(sum / count);
                if (score < BoxThreshold || Math.Min(boxWidth, boxHeight) < 3) continue;
                double distance = boxWidth * (double)boxHeight * UnclipRatio / (2.0 * (boxWidth + boxHeight));
                int left = (int)Math.Max(0, Math.Floor(x0 - distance)), top = (int)Math.Max(0, Math.Floor(y0 - distance));
                int right = (int)Math.Min(validWidth, Math.Ceiling(x1 + 1 + distance)), bottom = (int)Math.Min(validHeight, Math.Ceiling(y1 + 1 + distance));
                double centre = offsetY + (top + bottom) / 2.0;
                if (centre < strip.CoreTop || centre >= strip.CoreBottom) continue;
                boxes.Add(new OcrPackBox(left, top + offsetY, right - left, bottom - top, score));
                if (boxes.Count > MaxBoxes) return boxes;   // the caller reports too much text
            }
        return boxes;
    }

    /// <summary>Groups line indexes of similar width (widest at most <paramref name="maxRatio"/> × narrowest, at most <paramref name="maxBatch"/> each) so padding stays small.</summary>
    public static IReadOnlyList<int[]> Batches(IReadOnlyList<int> widths, int maxBatch = 8, double maxRatio = 1.5)
    {
        var order = Enumerable.Range(0, widths.Count).OrderBy(i => widths[i]).ThenBy(i => i).ToArray();
        var batches = new List<int[]>(); var current = new List<int>();
        foreach (int index in order)
        {
            if (current.Count > 0 && (current.Count >= maxBatch || widths[index] > Math.Max(1, widths[current[0]]) * maxRatio)) { batches.Add(current.ToArray()); current.Clear(); }
            current.Add(index);
        }
        if (current.Count > 0) batches.Add(current.ToArray());
        return batches;
    }

    /// <summary>The recognizer's input width for a line resized to <paramref name="contentWidth"/>: at least 320 and a multiple of 8 (one time step per 8 pixels).</summary>
    public static int InputWidth(int contentWidth) => Math.Max(320, (contentWidth + 7) / 8 * 8);

    /// <summary>
    /// Greedy CTC over one line's scores (<paramref name="steps"/> × <paramref name="classes"/>, row-major): index 0 is blank, index i is
    /// <c>characters[i - 1]</c>, the last index is a space. Only the first <paramref name="usableSteps"/> steps are read, so padding adds nothing.
    /// </summary>
    public static OcrPackDecoded DecodeCtc(ReadOnlySpan<float> scores, int steps, int classes, int usableSteps, string[] characters)
    {
        var text = new StringBuilder(); var words = new List<(int, int, string)>(); var word = new StringBuilder();
        int previous = -1, first = -1, last = -1, emitted = 0; double confidence = 0;
        void EndWord() { if (word.Length > 0) words.Add((first, last, word.ToString())); word.Clear(); first = -1; }
        int limit = Math.Min(steps, Math.Max(0, usableSteps));
        for (int t = 0; t < limit; t++)
        {
            var row = scores.Slice(t * classes, classes);
            int best = 0; float value = row[0];
            for (int k = 1; k < classes; k++) if (row[k] > value) { value = row[k]; best = k; }
            if (best != 0 && best == previous) { if (word.Length > 0) last = t; continue; }
            previous = best;
            if (best == 0) continue;
            string symbol = best - 1 < characters.Length ? characters[best - 1] : " ";
            text.Append(symbol); confidence += value; emitted++;
            if (string.IsNullOrWhiteSpace(symbol)) { EndWord(); continue; }
            if (word.Length == 0) first = t;
            last = t; word.Append(symbol);
        }
        EndWord();
        return new OcrPackDecoded(text.ToString().Trim(), emitted == 0 ? 0 : (float)(confidence / emitted), words);
    }

    /// <summary>
    /// The word layout the text view uses: lines below <paramref name="dropScore"/> confidence are dropped; boxes are grouped into rows by vertical
    /// overlap, rows read top to bottom and left to right, and neighbours in a row closer than three line heights share one line index.
    /// </summary>
    public static OcrLayout ToLayout(IReadOnlyList<OcrPackLine> lines, int pixelWidth, int pixelHeight, float dropScore = .5f)
    {
        var kept = lines.Where(line => line.Decoded.Confidence >= dropScore && line.Decoded.Words.Count > 0 && line.Steps > 0)
            .OrderBy(line => line.Box.Y + line.Box.Height / 2.0).ToList();
        var rows = new List<(double Top, double Bottom, List<OcrPackLine> Lines)>();
        foreach (var line in kept)
        {
            double top = line.Box.Y, bottom = line.Box.Y + line.Box.Height;
            int found = rows.FindIndex(row => Math.Min(row.Bottom, bottom) - Math.Max(row.Top, top) > Math.Min(row.Bottom - row.Top, bottom - top) * .5);
            if (found < 0) rows.Add((top, bottom, [line]));
            else { var row = rows[found]; row.Lines.Add(line); rows[found] = (Math.Min(row.Top, top), Math.Max(row.Bottom, bottom), row.Lines); }
        }
        var words = new List<OcrWordBox>();
        int lineIndex = -1;
        foreach (var row in rows.OrderBy(row => row.Top))
        {
            OcrPackBox? previous = null;
            foreach (var line in row.Lines.OrderBy(line => line.Box.X))
            {
                var box = line.Box;
                if (previous == null || box.X - (previous.X + previous.Width) > Math.Max(previous.Height, box.Height) * 3) lineIndex++;
                previous = box;
                double stepPixels = line.InputWidth / (double)line.Steps * box.Height / LineHeight;
                foreach (var (firstStep, lastStep, text) in line.Decoded.Words)
                {
                    double left = Math.Clamp(box.X + (firstStep - .5) * stepPixels, box.X, box.X + box.Width);
                    double right = Math.Min(box.X + box.Width, Math.Max(left + 1, box.X + (lastStep + 1.5) * stepPixels));
                    words.Add(new OcrWordBox(text, new Rect(left, box.Y, Math.Max(1, right - left), box.Height), lineIndex));
                }
            }
        }
        return new OcrLayout(pixelWidth, pixelHeight, words);
    }
}
