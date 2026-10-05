using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace DesktopTools.Extras;

/// <summary>
/// Asks the Windows spelling service (the dictionaries Windows already ships for every installed language) whether a word is a real word.
/// OCR output is judged with it: a reading made of dictionary words is far more likely to be right than one with "Claün" or "Rémoueitern".
/// Nothing is installed, nothing leaves the PC; when Windows has no dictionary for a language every word counts as unknown and callers fall back
/// to their other signals.
/// </summary>
internal static class SpellLexicon
{
    [ComImport, Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")] private class SpellCheckerFactoryClass { }

    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellCheckerFactory
    {
        IEnumString SupportedLanguages();
        [return: MarshalAs(UnmanagedType.Bool)] bool IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
        ISpellChecker CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
    }

    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellChecker
    {
        [return: MarshalAs(UnmanagedType.LPWStr)] string LanguageTag();
        IEnumSpellingError Check([MarshalAs(UnmanagedType.LPWStr)] string text);
        IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);
    }

    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumSpellingError
    {
        [PreserveSig] int Next([MarshalAs(UnmanagedType.Interface)] out object? error);
    }

    private static readonly object Gate = new();
    private static ISpellCheckerFactory? factory;
    private static bool factoryTried;
    private static readonly Dictionary<string, ISpellChecker?> Checkers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string, string), bool> Known = [];

    private static ISpellChecker? CheckerFor(string languageTag)
    {
        if (!factoryTried)
        {
            factoryTried = true;
            try { factory = (ISpellCheckerFactory)new SpellCheckerFactoryClass(); } catch (Exception) { factory = null; }
        }
        if (factory == null) return null;
        if (Checkers.TryGetValue(languageTag, out var cached)) return cached;
        ISpellChecker? checker = null;
        try
        {
            string tag = languageTag;
            if (!factory.IsSupported(tag))
            {
                // "ru" -> "ru-RU": take the first supported language with the same primary subtag.
                string primary = languageTag.Split('-')[0];
                tag = "";
                var languages = factory.SupportedLanguages(); var buffer = new string[1];
                while (languages.Next(1, buffer, IntPtr.Zero) == 0) if (buffer[0].Split('-')[0].Equals(primary, StringComparison.OrdinalIgnoreCase)) { tag = buffer[0]; break; }
            }
            if (tag.Length > 0) checker = factory.CreateSpellChecker(tag);
        }
        catch (Exception) { checker = null; }
        Checkers[languageTag] = checker;
        return checker;
    }

    public static bool Supports(string languageTag) { lock (Gate) return CheckerFor(languageTag) != null; }

    /// <summary>True when Windows' dictionary for the language accepts the word (letters only; case does not matter).</summary>
    public static bool IsWord(string languageTag, string word)
    {
        if (word.Length == 0) return false;
        lock (Gate)
        {
            var key = (languageTag.ToLowerInvariant(), word.ToLowerInvariant());
            if (Known.TryGetValue(key, out bool known)) return known;
            var checker = CheckerFor(languageTag);
            if (checker == null) return false;
            bool valid;
            try
            {
                var errors = checker.Check(word.ToLowerInvariant());
                valid = errors.Next(out var first) != 0 || first == null;   // S_FALSE with no error object: nothing is misspelt
                if (first != null) Marshal.ReleaseComObject(first);
            }
            catch (Exception) { valid = false; }
            if (Known.Count > 50_000) Known.Clear();
            Known[key] = valid; return valid;
        }
    }

    /// <summary>Windows' suggestions for a misspelt word, best first (empty when there is no dictionary).</summary>
    public static IReadOnlyList<string> Suggest(string languageTag, string word)
    {
        lock (Gate)
        {
            var checker = CheckerFor(languageTag); var result = new List<string>();
            if (checker == null) return result;
            try
            {
                var suggestions = checker.Suggest(word); var buffer = new string[1];
                while (result.Count < 8 && suggestions.Next(1, buffer, IntPtr.Zero) == 0) result.Add(buffer[0]);
            }
            catch (Exception) { }
            return result;
        }
    }
}
