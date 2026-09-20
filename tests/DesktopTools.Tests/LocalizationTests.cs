using DesktopTools.Core;
using DesktopTools.Localization;
using System.Globalization;
using System.Text.RegularExpressions;

internal static class LocalizationTests
{
    public static void Run()
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        var previous = L.Language;
        try
        {
            var expectedKeys = L.Catalog("ru").Keys.Order().ToArray();
            Check(expectedKeys.Length > 500, "Embedded translation catalog missing or incomplete");
            foreach (var language in L.Languages)
            {
                L.Use(language);
                Check(L.T("DesktopTools") == "DesktopTools", "Brand changed");
                Check(L.T("user-defined-file.txt") == "user-defined-file.txt", "Unknown key fallback lost");
                if (language == "en") continue;
                Check(L.Catalog(language).Keys.Order().SequenceEqual(expectedKeys), "Catalog key mismatch: " + language);
                foreach (var (key, value) in L.Catalog(language))
                {
                    Check(!string.IsNullOrWhiteSpace(value), "Empty translation: " + key);
                    Check(!value.Contains('\uFFFD'), "Broken Unicode: " + key);
                    string[] Tokens(string text) => Regex.Matches(text, @"\{\d+(?:,-?\d+)?(?::[^{}]+)?\}").Select(x => x.Value).Order().ToArray();
                    Check(Tokens(key).SequenceEqual(Tokens(value)), "Format placeholders changed: " + language + ": " + key);
                    if (Tokens(key).Length > 0) _ = string.Format(L.Culture, value, Enumerable.Repeat<object>(1.5, 20).ToArray());
                    if (key.Contains('|')) Check(key.Split('|').Where((_, i) => i % 2 == 1).SequenceEqual(value.Split('|').Where((_, i) => i % 2 == 1)), "File filter changed: " + key);
                }
                Check(L.T("Settings") != "Settings", "Main navigation not translated: " + language);
                Check(L.EnglishHint(L.T("Capture failed: ") + "native detail").Contains("failed"), "Error severity lost");
                Check(L.F($"Slot {3}").Contains('3'), "Formatting lost argument");
                var settings = new AppSettings { Language = language, DefaultTool = "Arrow", DrawShortcut = "Ctrl+Alt+D", TeleprompterText = "Settings — Привет" };
                SettingsStore.Validate(settings);
                Check(settings.Language == language && settings.DefaultTool == "Arrow" && settings.DrawShortcut == "Ctrl+Alt+D" && settings.TeleprompterText == "Settings — Привет", "Settings IDs or user text changed");
            }
            L.Use("unsupported"); Check(L.Language == "en", "Invalid language fallback");
            var invalid = new AppSettings { Language = "unsupported" }; SettingsStore.Validate(invalid); Check(invalid.Language == "en", "Invalid saved language not repaired");
        }
        finally { L.Use(previous); }
    }
}
