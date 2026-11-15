using System.Globalization;
using System.Text;

namespace DesktopTools.Core;

/// <summary>
/// Builds descriptive screenshot file names ("App – Title – date") by fixed rules. Pure: no window, file or network access.
/// </summary>
public static class SmartFileNames
{
    public const int MaxStemLength = 120;
    private const string Separator = " – ";
    private const int MaxAppLength = 40;
    private static readonly char[] Forbidden = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    private static readonly string[] GenericTitles = ["Program Manager"];

    /// <summary>The date-and-time name used when Smart names are off or nothing usable is known.</summary>
    public static string Fallback(DateTime time) => "DesktopTools-" + time.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    /// <summary>
    /// File name without extension. Never returns an invalid Windows file name. A part that <paramref name="isSensitive"/> flags is dropped whole;
    /// the headline is used only when the title is empty or generic.
    /// </summary>
    public static string Compose(string? app, string? title, string? headline, DateTime time, Func<string, bool> isSensitive)
    {
        ArgumentNullException.ThrowIfNull(isSensitive);
        var flagged = isSensitive;
        isSensitive = text => LooksLikeSecret(text) || flagged(text);
        string date = time.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);

        string? appPart = Usable(app, isSensitive);
        string? text = null;
        string? cleanTitle = Clean(title);
        if (cleanTitle is not null && appPart is not null) cleanTitle = WithoutAppSuffix(cleanTitle, appPart);
        bool titleDropped = cleanTitle is null || IsGeneric(cleanTitle, appPart);
        // A sensitive title is dropped without falling back to the headline: the screen most likely shows the same text.
        if (!titleDropped) text = isSensitive(title!) || isSensitive(cleanTitle!) ? null : cleanTitle;
        else text = Usable(headline, isSensitive);
        if (text is not null && appPart is not null && string.Equals(text, appPart, StringComparison.CurrentCultureIgnoreCase)) text = null;

        if (appPart is null && text is null) return Fallback(time);

        int budget = MaxStemLength - date.Length;
        var parts = new List<string>(2);
        if (appPart is not null)
        {
            appPart = Truncate(appPart, Math.Min(MaxAppLength, budget - Separator.Length));
            if (appPart.Length > 0) { parts.Add(appPart); budget -= appPart.Length + Separator.Length; }
        }
        if (text is not null)
        {
            text = Truncate(text, budget - Separator.Length);
            if (text.Length > 0) parts.Add(text);
        }
        if (parts.Count == 0) return Fallback(time);
        parts.Add(date);
        return string.Join(Separator, parts);
    }

    /// <summary>False when the title is empty or generic, so a headline from the screenshot would be used instead.</summary>
    public static bool TitleIsUsable(string? app, string? title)
    {
        string? appPart = Clean(app), cleanTitle = Clean(title);
        if (cleanTitle is not null && appPart is not null) cleanTitle = WithoutAppSuffix(cleanTitle, appPart);
        return cleanTitle is not null && !IsGeneric(cleanTitle, appPart);
    }

    private static readonly System.Text.RegularExpressions.Regex IbanShape = new(@"\b[A-Z]{2}\d{2}(?: ?[A-Z0-9]{4}){3,7}(?: ?[A-Z0-9]{1,3})?\b",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly System.Text.RegularExpressions.Regex CardShape = new(@"(?<!\d)\d(?:[ -]?\d){12,18}(?!\d)",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static bool PassesLuhn(IEnumerable<char> digits)
    {
        int sum = 0, position = 0;
        foreach (char c in digits.Reverse())
        {
            int d = c - '0';
            if (position++ % 2 == 1) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
        }
        return sum % 10 == 0;
    }

    /// <summary>
    /// True when the text holds an IBAN-shaped account number, a card number that passes the Luhn check, or a word that looks like a key, token or password hash:
    /// 16 or more characters with no spaces, mixing at least two letters and two digits. The sensitive-data detector is
    /// built for screen layouts and can miss these in a bare one-line title.
    /// </summary>
    internal static bool LooksLikeSecret(string text)
    {
        try
        {
            if (IbanShape.IsMatch(text)) return true;
            foreach (System.Text.RegularExpressions.Match match in CardShape.Matches(text))
                if (PassesLuhn(match.Value.Where(char.IsDigit))) return true;
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return true; }
        foreach (string word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            string token = word.Trim('"', '\'', '(', ')', '[', ']', '.', ',', ';', ':');
            int eq = token.IndexOf('='); if (eq >= 0 && eq < token.Length - 1) token = token[(eq + 1)..]; // NAME=value
            if (token.Length < 16) continue;
            int letters = 0, digits = 0;
            foreach (char c in token) { if (char.IsLetter(c)) letters++; else if (char.IsDigit(c)) digits++; }
            if (letters >= 2 && digits >= 2) return true;
        }
        return false;
    }

    private static string? Usable(string? value, Func<string, bool> isSensitive)
    {
        string? clean = Clean(value);
        if (clean is null) return null;
        // Check both the original and the cleaned text: cleaning can hide a pattern the detector knows, and the name holds the cleaned form.
        if ((value is not null && isSensitive(value)) || isSensitive(clean)) return null;
        return clean;
    }

    private static bool IsGeneric(string title, string? app) =>
        title.Length == 0
        || (app is not null && string.Equals(title, app, StringComparison.CurrentCultureIgnoreCase))
        || GenericTitles.Any(g => string.Equals(title, g, StringComparison.OrdinalIgnoreCase));

    private static string WithoutAppSuffix(string title, string app)
    {
        foreach (string dash in new[] { " - ", " — ", " – " })
        {
            string suffix = dash.TrimEnd() + " " + app;
            if (title.Length > suffix.Length && title.EndsWith(suffix, StringComparison.CurrentCultureIgnoreCase))
                return TrimName(title[..^suffix.Length]);
        }
        return title;
    }

    /// <summary>Control and forbidden characters become spaces, whitespace runs collapse, and trailing dots and spaces go. Null when nothing is left.</summary>
    private static string? Clean(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var sb = new StringBuilder(value.Length);
        bool space = false;
        foreach (char c in value)
        {
            bool blank = char.IsControl(c) || char.IsWhiteSpace(c) || Array.IndexOf(Forbidden, c) >= 0
                || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
            if (blank) { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
        }
        string result = TrimName(sb.ToString());
        return result.Length == 0 ? null : result;
    }

    private static string TrimName(string value) => value.Trim().TrimEnd('.', ' ').TrimStart(' ');

    /// <summary>Shortens to at most <paramref name="max"/> UTF-16 units on a text-element boundary so no surrogate pair or emoji sequence is split.</summary>
    private static string Truncate(string value, int max)
    {
        if (max <= 0) return "";
        if (value.Length <= max) return value;
        int cut = 0;
        var elements = StringInfo.GetTextElementEnumerator(value);
        while (elements.MoveNext())
        {
            int end = elements.ElementIndex + elements.GetTextElement().Length;
            if (end > max) break;
            cut = end;
        }
        return TrimName(value[..cut]);
    }
}
