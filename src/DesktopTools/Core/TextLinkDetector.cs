using System.Text.RegularExpressions;

namespace DesktopTools.Core;

public enum TextLinkKind { Url, Email, Phone }
public sealed record TextLink(TextLinkKind Kind, string Target, int FirstWord, int LastWord);

public static class TextLinkDetector
{
    private static readonly Regex Url = new(@"(?<![\w@])(?:https?://|www\.)[^\s<>""']*[^\s<>""'.,;:!?)\]]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Email = new(@"(?<![\w.])[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly Regex Phone = new(@"(?<![\w.,/-])\+?\d(?:[\d\s().-]{6,}\d)", RegexOptions.Compiled);

    public static IReadOnlyList<TextLink> Find(TextSelectionModel model)
    {
        var links = new List<TextLink>();
        for (int line = 0; line < model.LineCount; line++)
        {
            var (first, last) = model.WordsOfLine(line);
            var text = new System.Text.StringBuilder(); var starts = new int[last - first + 1];
            for (int i = first; i <= last; i++) { if (i > first) text.Append(' '); starts[i - first] = text.Length; text.Append(model.TextOf(i)); }
            string joined = text.ToString(); var taken = new List<(int Start, int End)>();
            int WordAtOffset(int offset) { int w = 0; for (int k = 0; k < starts.Length; k++) if (starts[k] <= offset) w = k; return first + w; }
            void Add(Match m, TextLinkKind kind, string target)
            {
                if (taken.Any(t => m.Index < t.End && m.Index + m.Length > t.Start)) return;
                taken.Add((m.Index, m.Index + m.Length));
                links.Add(new TextLink(kind, target, WordAtOffset(m.Index), WordAtOffset(m.Index + m.Length - 1)));
            }
            foreach (Match m in Url.Matches(joined)) Add(m, TextLinkKind.Url, m.Value.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + m.Value : m.Value);
            foreach (Match m in Email.Matches(joined)) Add(m, TextLinkKind.Email, "mailto:" + m.Value);
            foreach (Match m in Phone.Matches(joined))
            {
                string digits = new(m.Value.Where(char.IsDigit).ToArray());
                if (digits.Length is < 9 or > 15 || Regex.IsMatch(m.Value, @"^\d{4}-\d{2}-\d{2}")) continue;
                Add(m, TextLinkKind.Phone, "tel:" + (m.Value.TrimStart().StartsWith('+') ? "+" : "") + digits);
            }
        }
        return links.OrderBy(l => model.Words[l.FirstWord].LineIndex).ThenBy(l => l.FirstWord).ToArray();
    }
}
