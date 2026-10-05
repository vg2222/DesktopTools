using System.Windows;

namespace DesktopTools.Core;

/// <summary>Words of a recognized image in reading order, with PDF-style selection and per-word text fixes. Pure logic; all coordinates are image pixels.</summary>
public sealed class TextSelectionModel
{
    private readonly Dictionary<int, string> overrides = [];
    private readonly int[] lineStart, lineEnd;   // first/last word index of each dense line number
    private readonly int[] lineOfWord;
    private int anchor = -1, active = -1;

    public TextSelectionModel(OcrLayout layout)
    {
        Words = layout.Words.OrderBy(w => w.LineIndex).ThenBy(w => w.Bounds.X).ToArray();
        lineOfWord = new int[Words.Count];
        var starts = new List<int>(); var ends = new List<int>(); int previous = int.MinValue;
        for (int i = 0; i < Words.Count; i++)
        {
            if (Words[i].LineIndex != previous) { starts.Add(i); ends.Add(i); previous = Words[i].LineIndex; } else ends[^1] = i;
            lineOfWord[i] = starts.Count - 1;
        }
        lineStart = starts.ToArray(); lineEnd = ends.ToArray();
    }

    public IReadOnlyList<OcrWordBox> Words { get; }
    public int Count => Words.Count;
    public int LineCount => lineStart.Length;
    public int LineOf(int word) => lineOfWord[word];
    public (int First, int Last) WordsOfLine(int line) => (lineStart[line], lineEnd[line]);

    public int? WordAt(Point p)
    {
        for (int i = 0; i < Words.Count; i++) { var b = Words[i].Bounds; b.Inflate(2, 2); if (b.Contains(p)) return i; }
        return null;
    }

    /// <summary>The word a drag at <paramref name="p"/> should extend to: an exact hit, else the closest line vertically, then the closest word in that line.</summary>
    public int? NearestWord(Point p)
    {
        if (Words.Count == 0) return null;
        if (WordAt(p) is int hit) return hit;
        int bestLine = 0; double bestDistance = double.MaxValue;
        for (int line = 0; line < LineCount; line++)
        {
            double top = double.MaxValue, bottom = double.MinValue;
            for (int i = lineStart[line]; i <= lineEnd[line]; i++) { top = Math.Min(top, Words[i].Bounds.Top); bottom = Math.Max(bottom, Words[i].Bounds.Bottom); }
            double distance = p.Y < top ? top - p.Y : p.Y > bottom ? p.Y - bottom : 0;
            if (distance < bestDistance) { bestDistance = distance; bestLine = line; }
        }
        int best = lineStart[bestLine]; double bestX = double.MaxValue;
        for (int i = lineStart[bestLine]; i <= lineEnd[bestLine]; i++)
        {
            var b = Words[i].Bounds;
            double distance = p.X < b.Left ? b.Left - p.X : p.X > b.Right ? p.X - b.Right : 0;
            if (distance < bestX) { bestX = distance; best = i; }
        }
        return best;
    }

    public bool HasSelection => anchor >= 0;
    public (int Start, int End) Range => anchor < 0 ? (-1, -1) : (Math.Min(anchor, active), Math.Max(anchor, active));
    public bool IsSelected(int word) => anchor >= 0 && word >= Range.Start && word <= Range.End;
    public void Begin(int word) { anchor = active = word; }
    public void Extend(int word) { if (anchor >= 0) active = word; }
    public void SelectWord(int word) { anchor = active = word; }
    public void SelectLine(int word) { int line = lineOfWord[word]; anchor = lineStart[line]; active = lineEnd[line]; }
    public void SelectAll() { if (Words.Count > 0) { anchor = 0; active = Words.Count - 1; } }
    public void Clear() { anchor = active = -1; }

    public bool IsEdited(int word) => overrides.ContainsKey(word);
    public string TextOf(int word) => overrides.TryGetValue(word, out var text) ? text : Words[word].Text;
    public void SetOverride(int word, string text)
    {
        text = text.Trim();
        if (text.Length == 0 || text == Words[word].Text) overrides.Remove(word); else overrides[word] = text;
    }

    /// <summary>Words joined by spaces inside a line and by new lines between lines, using fixed words where the user edited them.</summary>
    public string GetText(bool selectionOnly)
    {
        if (selectionOnly && !HasSelection) return "";
        int from = selectionOnly ? Range.Start : 0, to = selectionOnly ? Range.End : Words.Count - 1;
        var text = new System.Text.StringBuilder();
        for (int i = from; i <= to && i < Words.Count; i++)
        {
            if (i > from) text.Append(lineOfWord[i] != lineOfWord[i - 1] ? '\n' : ' ');
            text.Append(TextOf(i));
        }
        return text.ToString();
    }
}
