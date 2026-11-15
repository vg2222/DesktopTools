using System.IO;
using DesktopTools.Core;

internal static class SmartFileNamesTests
{
    public static void Run()
    {
        void Check(bool value, string what) { if (!value) throw new Exception("Smart file name assertion failed: " + what); }
        void Eq(string actual, string expected) { if (actual != expected) throw new Exception($"Smart file name: expected \"{expected}\", got \"{actual}\""); }
        var t = new DateTime(2026, 11, 11, 9, 5, 7);
        Func<string, bool> none = _ => false;

        Eq(SmartFileNames.Compose("Google Chrome", "Invoice 1042 - Google Chrome", null, t, none), "Google Chrome – Invoice 1042 – 2026-11-11 09-05-07");
        Eq(SmartFileNames.Compose("Notepad", "", "Quarterly plan", t, none), "Notepad – Quarterly plan – 2026-11-11 09-05-07");
        Eq(SmartFileNames.Compose("Notepad", "", null, t, none), "Notepad – 2026-11-11 09-05-07");
        Eq(SmartFileNames.Compose(null, null, null, t, none), "DesktopTools-20261111-090507");
        Eq(SmartFileNames.Compose("App", "a<b>:c\"/d\\e|f?g*h", null, t, none), "App – a b c d e f g h – 2026-11-11 09-05-07");
        Eq(SmartFileNames.Compose("App", "report.   ", null, t, none), "App – report – 2026-11-11 09-05-07");
        Eq(SmartFileNames.Compose("Mail", "Re: offer for anna@example.com", null, t, s => s.Contains('@')), "Mail – 2026-11-11 09-05-07");
        Eq(SmartFileNames.Compose("Проводник", "Документы", null, t, none), "Проводник – Документы – 2026-11-11 09-05-07");
        // Key- and token-shaped words are dropped by the name rules themselves, even when the detector does not flag them.
        Eq(SmartFileNames.Compose("Vault", "API key sk_" + "live_4f9a8b7c6d5e4f3a2b1c0d9e", null, t, none), "Vault – 2026-11-11 09-05-07");
        Eq(SmartFileNames.Compose("Terminal", "export TOKEN=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOjF9", null, t, none), "Terminal – 2026-11-11 09-05-07");
        Eq(SmartFileNames.Compose("Notepad", "", "ghp_1a2B3c4D5e6F7g8H9i0J", t, none), "Notepad – 2026-11-11 09-05-07");
        Eq(SmartFileNames.Compose("Bank", "Transfer to DE89 3704 0044 0532 0130 00", null, t, none), "Bank – 2026-11-11 09-05-07");
        Eq(SmartFileNames.Compose("Bank", "IBAN GB29NWBK60161331926819", null, t, none), "Bank – 2026-11-11 09-05-07");
        Eq(SmartFileNames.Compose("Shop", "Card 4111 1111 1111 1111 - Checkout", null, t, none), "Shop – 2026-11-11 09-05-07");
        Eq(SmartFileNames.Compose("Shop", "Order 1234 5678 9012 3456", null, t, none), "Shop – Order 1234 5678 9012 3456 – 2026-11-11 09-05-07");
        Eq(SmartFileNames.Compose("Word", "Windows 11 build 26300 notes 2026", null, t, none), "Word – Windows 11 build 26300 notes 2026 – 2026-11-11 09-05-07");
        Check(SmartFileNames.Compose("App", new string('x', 500), null, t, none).Length <= SmartFileNames.MaxStemLength, "length cap");
        Check(SmartFileNames.Compose("App", new string('x', 500), null, t, none).EndsWith("2026-11-11 09-05-07"), "the date survives truncation");
        Eq(SmartFileNames.Compose("App", "Program Manager", null, t, none), "App – 2026-11-11 09-05-07");
        Eq(SmartFileNames.Compose("App", "line1\r\nline2\tx", null, t, none), "App – line1 line2 x – 2026-11-11 09-05-07");

        // Rules beyond the plan's table (Review Focus 1-3).
        Eq(SmartFileNames.Compose("Word", "Plan — Word", null, t, none), "Word – Plan – 2026-11-11 09-05-07");
        Eq(SmartFileNames.Compose("Notepad", "Notepad", "Headline", t, none), "Notepad – Headline – 2026-11-11 09-05-07");
        Eq(SmartFileNames.Compose("Notepad", "Title", "Headline", t, none), "Notepad – Title – 2026-11-11 09-05-07");
        Eq(SmartFileNames.Compose("", "Only a title", null, t, none), "Only a title – 2026-11-11 09-05-07");
        Eq(SmartFileNames.Compose("App", "Secret", "also secret", t, _ => true), "DesktopTools-20261111-090507");
        Eq(SmartFileNames.Compose("CON", null, null, t, none), "CON – 2026-11-11 09-05-07");
        string emoji = string.Concat(Enumerable.Repeat("😀", 300));
        string emojiName = SmartFileNames.Compose("App", emoji, null, t, none);
        Check(emojiName.Length <= SmartFileNames.MaxStemLength && emojiName.EndsWith("2026-11-11 09-05-07"), "emoji title capped: " + emojiName.Length);
        Check(!emojiName.Any(char.IsSurrogate) || IsWellFormed(emojiName), "truncation never splits a surrogate pair");
        string cjk = SmartFileNames.Compose("App", "文档 " + new string('字', 400), null, t, none);
        Check(cjk.Length <= SmartFileNames.MaxStemLength && cjk.Contains("文档"), "CJK kept and capped");
        string rtl = SmartFileNames.Compose("App", "مستند", null, t, none);
        Check(rtl.Contains("مستند"), "right-to-left text kept");
        string longApp = SmartFileNames.Compose(new string('a', 400), "title", null, t, none);
        Check(longApp.Length <= SmartFileNames.MaxStemLength && longApp.EndsWith("2026-11-11 09-05-07"), "a long app name is capped too");
        foreach (string name in new[] { emojiName, cjk, rtl, longApp })
            Check(name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.EndsWith('.') && !name.EndsWith(' '), "valid Windows name: " + name);

        // UniquePath with a stem.
        string folder = Path.Combine("C:\\shots", "work");
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string stem = "Notepad – 2026-11-11 09-05-07";
        string first = CaptureAutoSave.UniquePath(folder, stem, taken.Contains);
        Check(first == Path.Combine(folder, stem + ".png"), "stem.png first: " + first);
        taken.Add(first);
        string second = CaptureAutoSave.UniquePath(folder, stem, taken.Contains);
        Check(second == Path.Combine(folder, stem + "-2.png"), "then -2: " + second);
        taken.Add(second);
        Check(CaptureAutoSave.UniquePath(folder, stem, taken.Contains) == Path.Combine(folder, stem + "-3.png"), "then -3");
        try { CaptureAutoSave.UniquePath(folder, stem, _ => true); Check(false, "always taken must throw"); }
        catch (IOException) { }
    }

    private static bool IsWellFormed(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i])) { if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) return false; i++; }
            else if (char.IsLowSurrogate(s[i])) return false;
        }
        return true;
    }
}
