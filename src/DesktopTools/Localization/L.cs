using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DesktopTools.Localization;

/// <summary>Local, embedded translations. Stored setting values and user content are never translated.</summary>
public static class L
{
    public static readonly string[] Languages = ["en", "ru", "de", "fr", "es", "uk", "zh", "pt", "ja", "pl", "tr", "it"];
    public static readonly string[] LanguageNames = ["English", "Русский", "Deutsch", "Français", "Español", "Українська", "中文（简体）", "Português", "日本語", "Polski", "Türkçe", "Italiano"];
    // Catalogs and message patterns are built only for languages that are actually used, not all eleven at startup.
    private static readonly Dictionary<string, Dictionary<string, string>> catalogs = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<(string Source, Regex Pattern)>> messagePatterns = new(StringComparer.Ordinal);
    private static Dictionary<string, string>? CatalogFor(string language)
    {
        if (language == "en" || !Languages.Contains(language)) return null;
        lock (catalogs)
        {
            if (!catalogs.TryGetValue(language, out var catalog)) catalogs.Add(language, catalog = Load(language));
            return catalog;
        }
    }
    public static string Language { get; private set; } = "en";
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language);
    public static string Normalize(string? language) => Languages.Contains(language) ? language! : "en";
    public static string SystemLanguage(CultureInfo? culture = null)
    {
        string language = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName.ToLowerInvariant();
        return Normalize(language);
    }
    public static void Use(string? language) => Language = Normalize(language);
    /// <summary>Called with every text asked for in a non-English language that has no translation. Null in the app; the test harness lists them.</summary>
    public static Action<string>? Missing { get; set; }
    public static string T(string? english)
    {
        if (english == null) return "";
        if (CatalogFor(Language) is { } catalog)
        {
            if (catalog.TryGetValue(english, out var translated)) return translated;
            Missing?.Invoke(english);
        }
        return english;
    }
    public static string F(FormattableString message)
    {
        try { return string.Format(Culture, T(message.Format), message.GetArguments()); }
        catch (FormatException) { return message.ToString(Culture); }
    }
    private static readonly Dictionary<string, Dictionary<string, string>> sources = new(StringComparer.Ordinal);
    /// <summary>The English source of an exactly translated label, or the text itself. Cached per language; used to pick icons, never shown.</summary>
    public static string Source(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (CatalogFor(Language) is not { } catalog) return text;
        Dictionary<string, string>? reverse;
        lock (sources)
        {
            if (!sources.TryGetValue(Language, out reverse))
            {
                reverse = new Dictionary<string, string>(catalog.Count, StringComparer.Ordinal);
                foreach (var (source, translated) in catalog) reverse.TryAdd(translated, source);
                sources.Add(Language, reverse);
            }
        }
        return reverse.TryGetValue(text, out var english) ? english : text;
    }
    // Notification severity is derived from source text, not the selected language.
    internal static string EnglishHint(string message)
    {
        if (CatalogFor(Language) is not { } catalog) return message;
        int minimumPrefix = Language is "zh" or "ja" ? 4 : 8;   // CJK sentences are much shorter than the same text in other languages
        foreach (var (source, translated) in catalog.OrderByDescending(x => x.Value.Length))
        {
            if (message == translated) return source;
            var prefix = translated.Split('{')[0];
            if (prefix.Length >= minimumPrefix && message.StartsWith(prefix, StringComparison.Ordinal)) return source;
        }
        // Some warnings begin with the user's shortcut or filename, not a translated prefix.
        foreach (var (source, pattern) in PatternsFor(Language, catalog))
        {
            try { if (pattern.IsMatch(message)) return source; }
            catch (RegexMatchTimeoutException) { }
        }
        return message;
    }
    private static List<(string Source, Regex Pattern)> PatternsFor(string language, Dictionary<string, string> catalog)
    {
        lock (messagePatterns)
        {
            if (messagePatterns.TryGetValue(language, out var cached)) return cached;
            var patterns = new List<(string, Regex)>();
            foreach (var (source, translated) in catalog.Where(x => x.Value.StartsWith('{')))
            {
                var parts = Regex.Split(translated, @"(\{\d+(?:,-?\d+)?(?::[^{}]+)?\})");
                string pattern = "^" + string.Concat(parts.Select((part, index) => index % 2 == 1 ? ".*?" : Regex.Escape(part))) + "$";
                // Interpreted regexes are small; NonBacktracking built large automata for every catalog entry.
                patterns.Add((source, new Regex(pattern, RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))));
            }
            var result = patterns.OrderByDescending(x => x.Item2.ToString().Length).ToList();
            messagePatterns.Add(language, result);
            return result;
        }
    }
    public static IReadOnlyDictionary<string, string> Catalog(string language) => CatalogFor(language) ?? new Dictionary<string, string>();
    private static Dictionary<string, string> Load(string language)
    {
        var assembly = typeof(L).Assembly;
        var catalog = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var resource in assembly.GetManifestResourceNames().Where(x => x.EndsWith("." + language + ".json", StringComparison.Ordinal)).OrderBy(x => x, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            foreach (var (key, value) in JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!) catalog.TryAdd(key, value);
        }
        catalog.TrimExcess();
        return catalog;
    }
}
