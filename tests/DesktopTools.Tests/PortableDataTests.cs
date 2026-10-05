using System.IO;
using System.IO.Compression;
using DesktopTools.Core;

internal static class PortableDataTests
{
    public static void Run()
    {
        void Check(bool value, string what) { if (!value) throw new Exception("Portable data assertion failed: " + what); }
        void Rejects(string zip, string reason) { try { PortableData.Validate(zip); throw new Exception("Archive accepted: " + reason); } catch (InvalidDataException) { } }
        string root = Path.Combine(Path.GetTempPath(), "DesktopTools-portable-" + Guid.NewGuid());
        try
        {
            string a = Path.Combine(root, "a"), b = Path.Combine(root, "b"); Directory.CreateDirectory(Path.Combine(a, "notes")); Directory.CreateDirectory(Path.Combine(a, "profiles")); Directory.CreateDirectory(b);
            File.WriteAllText(Path.Combine(a, "settings.json"), "{\"Version\":3,\"Language\":\"de\"}");
            File.WriteAllText(Path.Combine(a, "automation.json"), "{\"Version\":2,\"Scripts\":[]}");
            File.WriteAllText(Path.Combine(a, "notes", "n1.json"), "{\"Title\":\"First\"}"); File.WriteAllText(Path.Combine(a, "notes", "n2.json"), "{\"Title\":\"Second\"}");
            File.WriteAllText(Path.Combine(a, "profiles", "Talk.json"), "{}");
            File.WriteAllText(Path.Combine(a, "notes", "ignore.txt"), "not part of the archive");
            string zip = Path.Combine(root, "export.zip");
            var exported = PortableData.Export(a, zip, "1.2.7");
            Check(exported.Notes == 2 && exported.Profiles == 1 && exported.HasSettings && exported.HasAutomation, "export counts");
            var validated = PortableData.Validate(zip);
            Check(validated.Notes == 2 && validated.AppVersion == "1.2.7", "validate summary");
            // import into a machine that already has different data: it is replaced, and the old data is kept as a backup
            File.WriteAllText(Path.Combine(b, "settings.json"), "{\"Version\":3,\"Language\":\"en\"}");
            Directory.CreateDirectory(Path.Combine(b, "notes")); File.WriteAllText(Path.Combine(b, "notes", "old.json"), "{}");
            string backup = PortableData.Import(zip, b);
            Check(File.ReadAllText(Path.Combine(b, "settings.json")).Contains("\"de\""), "settings replaced");
            Check(File.Exists(Path.Combine(b, "notes", "n1.json")) && !File.Exists(Path.Combine(b, "notes", "old.json")), "notes replaced");
            using (var kept = ZipFile.OpenRead(backup)) Check(kept.Entries.Any(e => e.FullName == "notes/old.json"), "backup holds previous notes");
            Check(!Directory.EnumerateDirectories(b, "import-staging-*").Any(), "staging removed");

            // hostile or foreign archives
            string traversal = Path.Combine(root, "traversal.zip");
            using (var z = ZipFile.Open(traversal, ZipArchiveMode.Create)) { z.CreateEntry("manifest.json"); var e = z.CreateEntry("notes/../../evil.json"); using var w = new StreamWriter(e.Open()); w.Write("{}"); }
            Rejects(traversal, "path traversal");
            string foreign = Path.Combine(root, "foreign.zip");
            using (var z = ZipFile.Open(foreign, ZipArchiveMode.Create)) { var e = z.CreateEntry("manifest.json"); using var w = new StreamWriter(e.Open()); w.Write("{\"format\":\"something-else\",\"version\":1}"); }
            Rejects(foreign, "foreign format");
            string newer = Path.Combine(root, "newer.zip");
            using (var z = ZipFile.Open(newer, ZipArchiveMode.Create)) { var e = z.CreateEntry("manifest.json"); using var w = new StreamWriter(e.Open()); w.Write("{\"format\":\"" + PortableData.Format + "\",\"version\":99}"); }
            Rejects(newer, "future format");
            string exe = Path.Combine(root, "exe.zip");
            using (var z = ZipFile.Open(exe, ZipArchiveMode.Create)) { var m = z.CreateEntry("manifest.json"); using (var w = new StreamWriter(m.Open())) w.Write("{\"format\":\"" + PortableData.Format + "\",\"version\":1}"); z.CreateEntry("tool.exe"); }
            Rejects(exe, "unexpected file type");
            string damaged = Path.Combine(root, "damaged.zip");
            using (var z = ZipFile.Open(damaged, ZipArchiveMode.Create)) { var m = z.CreateEntry("manifest.json"); using (var w = new StreamWriter(m.Open())) w.Write("{\"format\":\"" + PortableData.Format + "\",\"version\":1}"); var s = z.CreateEntry("settings.json"); using var w2 = new StreamWriter(s.Open()); w2.Write("{ not json"); }
            Rejects(damaged, "damaged json");
            Check(!File.Exists(Path.Combine(root, "evil.json")) && !File.Exists(Path.Combine(root, "..", "evil.json")), "nothing escaped the data folder");
            try { PortableData.Export(a, zip, "1.2.7"); throw new Exception("Existing archive overwritten"); } catch (IOException) { }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
