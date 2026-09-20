using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DesktopTools.Localization;

/// <summary>Local, embedded translations. Stored setting values and user content are never translated.</summary>
public static class L
{
    public static readonly string[] Languages = ["en", "ru", "de", "fr", "es"];
    public static readonly string[] LanguageNames = ["English", "Русский", "Deutsch", "Français", "Español"];
    private static readonly Dictionary<string, Dictionary<string, string>> catalogs = Load();
    public static string Language { get; private set; } = "en";
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language);
    public static string Normalize(string? language) => Languages.Contains(language) ? language! : "en";
    public static void Use(string? language) => Language = Normalize(language);
    public static string T(string? english)
    {
        if (english == null) return "";
        return catalogs.TryGetValue(Language, out var catalog) && catalog.TryGetValue(english, out var translated) ? translated : english;
    }
    public static string F(FormattableString message)
    {
        try { return string.Format(Culture, T(message.Format), message.GetArguments()); }
        catch (FormatException) { return message.ToString(Culture); }
    }
    // Notification severity is derived from source text, not the selected language.
    internal static string EnglishHint(string message)
    {
        if (Language == "en" || !catalogs.TryGetValue(Language, out var catalog)) return message;
        foreach (var (source, translated) in catalog.OrderByDescending(x => x.Value.Length))
        {
            if (message == translated) return source;
            var prefix = translated.Split('{')[0];
            if (prefix.Length >= 8 && message.StartsWith(prefix, StringComparison.Ordinal)) return source;
        }
        // Some warnings begin with the user's shortcut or filename, not a translated prefix.
        foreach (var (source, pattern) in messagePatterns[Language])
            if (pattern.IsMatch(message)) return source;
        return message;
    }
    private static readonly Dictionary<string, List<(string Source, Regex Pattern)>> messagePatterns = BuildPatterns();
    private static Dictionary<string, List<(string, Regex)>> BuildPatterns()
    {
        var result = new Dictionary<string, List<(string, Regex)>>();
        foreach (var (language, catalog) in catalogs)
        {
            var patterns = new List<(string, Regex)>();
            foreach (var (source, translated) in catalog.Where(x => x.Value.StartsWith('{')))
            {
                var parts = Regex.Split(translated, @"(\{\d+(?:,-?\d+)?(?::[^{}]+)?\})");
                string pattern = "^" + string.Concat(parts.Select((part, index) => index % 2 == 1 ? ".*" : Regex.Escape(part))) + "$";
                patterns.Add((source, new Regex(pattern, RegexOptions.Singleline | RegexOptions.NonBacktracking)));
            }
            result.Add(language, patterns.OrderByDescending(x => x.Item2.ToString().Length).ToList());
        }
        return result;
    }
    public static IReadOnlyDictionary<string, string> Catalog(string language) => catalogs.TryGetValue(language, out var result) ? result : new Dictionary<string, string>();
    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var assembly = typeof(L).Assembly;
        foreach (string language in Languages.Where(x => x != "en"))
        {
            var catalog = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var resource in assembly.GetManifestResourceNames().Where(x => x.EndsWith("." + language + ".json", StringComparison.Ordinal)).OrderBy(x => x, StringComparer.Ordinal))
            {
                using var stream = assembly.GetManifestResourceStream(resource)!;
                foreach (var (key, value) in JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!) catalog.TryAdd(key, value);
            }
            result.Add(language, catalog);
        }
        return result;
    }
}
