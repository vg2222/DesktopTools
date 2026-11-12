using System.IO;
using DesktopTools.Core;

internal static class CaptureAutoSaveTests
{
    public static void Run()
    {
        void Check(bool value, string what) { if (!value) throw new Exception("Capture auto-save assertion failed: " + what); }
        var time = new DateTime(2026, 11, 10, 9, 5, 7);
        string folder = Path.Combine("C:\\shots", "work");
        Check(CaptureAutoSave.ResolveDirectory("", "C:\\Pictures") == Path.Combine("C:\\Pictures", "DesktopTools"), "no chosen folder falls back to Pictures\\DesktopTools");
        Check(CaptureAutoSave.ResolveDirectory("  ", "C:\\Pictures") == Path.Combine("C:\\Pictures", "DesktopTools"), "blank folder falls back too");
        Check(CaptureAutoSave.ResolveDirectory(folder, "C:\\Pictures") == folder, "chosen folder is used as is");

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string first = CaptureAutoSave.UniquePath(folder, time, taken.Contains);
        Check(first == Path.Combine(folder, "DesktopTools-20261110-090507.png"), "timestamped name: " + first);
        taken.Add(first);
        string second = CaptureAutoSave.UniquePath(folder, time, taken.Contains);
        Check(second == Path.Combine(folder, "DesktopTools-20261110-090507-2.png"), "same second gets -2: " + second);
        taken.Add(second);
        Check(CaptureAutoSave.UniquePath(folder, time, taken.Contains).EndsWith("-3.png", StringComparison.Ordinal), "then -3");
        try { CaptureAutoSave.UniquePath(folder, time, _ => true); Check(false, "an always-taken folder must fail instead of looping"); }
        catch (IOException) { }

        // The mode survives a settings round trip and unknown values still recover to the default.
        string root = Path.Combine(Path.GetTempPath(), "DesktopTools-autosave-" + Guid.NewGuid());
        try
        {
            var store = new SettingsStore(root);
            var settings = store.Load(); settings.CaptureOutput = CaptureAutoSave.Mode; store.Save(settings);
            Check(new SettingsStore(root).Load().CaptureOutput == CaptureAutoSave.Mode, "mode persists");
            settings.CaptureOutput = "Somewhere else"; store.Save(settings);
            Check(new SettingsStore(root).Load().CaptureOutput == "Clipboard", "unknown output recovers to Clipboard");
            settings = store.Load(); Check(settings.CaptureFileNames == "Date and time", "date-and-time names are the default");
            settings.CaptureFileNames = "Smart"; store.Save(settings);
            Check(new SettingsStore(root).Load().CaptureFileNames == "Smart", "smart names persist");
            settings.CaptureFileNames = "Clever"; store.Save(settings);
            Check(new SettingsStore(root).Load().CaptureFileNames == "Date and time", "unknown file names recover to date and time");
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}
