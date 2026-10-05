using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace DesktopTools.Core;

/// <summary>
/// Moves settings, shortcuts, profiles, automation workflows and notes between computers as one zip file. Nothing leaves the PC:
/// the user saves the archive wherever they like. Import validates everything first and keeps a backup of what it replaces.
/// </summary>
public static class PortableData
{
    public const string Format = "desktoptools-portable-data";
    public const int FormatVersion = 1;
    private const long MaximumTotalBytes = 64L * 1024 * 1024;
    private const int MaximumEntries = 5000;
    private static readonly string[] Files = ["settings.json", "automation.json"];
    private static readonly string[] Folders = ["notes", "profiles"];

    public sealed record Summary(string AppVersion, DateTime CreatedUtc, int Notes, int Profiles, bool HasSettings, bool HasAutomation);

    public static Summary Export(string dataRoot, string zipPath, string appVersion)
    {
        if (File.Exists(zipPath)) throw new IOException("An archive with this name already exists.");
        string temporary = zipPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        int notes = 0, profiles = 0; bool settings = false, automation = false;
        try
        {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (string name in Files)
                {
                    string path = Path.Combine(dataRoot, name);
                    if (!File.Exists(path)) continue;
                    zip.CreateEntryFromFile(path, name, CompressionLevel.Optimal);
                    if (name == "settings.json") settings = true; else automation = true;
                }
                foreach (string folder in Folders)
                {
                    string root = Path.Combine(dataRoot, folder);
                    if (!Directory.Exists(root)) continue;
                    foreach (string path in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                        zip.CreateEntryFromFile(path, folder + "/" + Path.GetFileName(path), CompressionLevel.Optimal);
                        if (folder == "notes") notes++; else profiles++;
                    }
                }
                var manifest = zip.CreateEntry("manifest.json");
                using var writer = new StreamWriter(manifest.Open());
                writer.Write(JsonSerializer.Serialize(new { format = Format, version = FormatVersion, appVersion, createdUtc = DateTime.UtcNow, notes, profiles, settings, automation },
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            File.Move(temporary, zipPath, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return new Summary(appVersion, DateTime.UtcNow, notes, profiles, settings, automation);
    }

    /// <summary>Checks an archive without changing anything. Throws <see cref="InvalidDataException"/> with a readable reason.</summary>
    public static Summary Validate(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            if (zip.Entries.Count > MaximumEntries) throw new InvalidDataException("The archive has too many files.");
            long total = 0; int notes = 0, profiles = 0; bool settings = false, automation = false, manifestFound = false;
            string appVersion = "unknown"; DateTime created = default;
            foreach (var entry in zip.Entries)
            {
                total += entry.Length;
                if (total > MaximumTotalBytes) throw new InvalidDataException("The archive is larger than DesktopTools data can be.");
                string name = entry.FullName;
                if (name.EndsWith('/')) continue;
                if (!IsAllowedName(name)) throw new InvalidDataException("The archive contains a file DesktopTools does not use: " + Shorten(name));
                using var stream = entry.Open();
                using var document = JsonDocument.Parse(stream);
                if (name == "manifest.json")
                {
                    var root = document.RootElement;
                    if (root.TryGetProperty("format", out var format) ? format.GetString() != Format : true) throw new InvalidDataException("This is not a DesktopTools data archive.");
                    if (!root.TryGetProperty("version", out var version) || version.GetInt32() > FormatVersion) throw new InvalidDataException("This archive was made by a newer DesktopTools. Update the app and try again.");
                    manifestFound = true;
                    if (root.TryGetProperty("appVersion", out var app)) appVersion = app.GetString() ?? "unknown";
                    if (root.TryGetProperty("createdUtc", out var date) && date.TryGetDateTime(out var parsed)) created = parsed.ToUniversalTime();
                }
                else if (name == "settings.json") settings = true;
                else if (name == "automation.json") automation = true;
                else if (name.StartsWith("notes/")) notes++;
                else profiles++;
            }
            if (!manifestFound) throw new InvalidDataException("This is not a DesktopTools data archive.");
            if (!settings && !automation && notes == 0 && profiles == 0) throw new InvalidDataException("The archive contains no settings, notes, profiles or workflows.");
            return new Summary(appVersion, created, notes, profiles, settings, automation);
        }
        catch (JsonException) { throw new InvalidDataException("A file in the archive is damaged."); }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new InvalidDataException("The archive could not be read: " + ex.Message); }
    }

    /// <summary>Replaces the data in <paramref name="dataRoot"/>. Run while the app is not using the files. Returns the backup archive path.</summary>
    public static string Import(string zipPath, string dataRoot)
    {
        Validate(zipPath);
        Directory.CreateDirectory(dataRoot);
        string backups = Path.Combine(dataRoot, "backups"); Directory.CreateDirectory(backups);
        string backup = Path.Combine(backups, $"before-import-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        Export(dataRoot, backup, "backup");
        foreach (var old in Directory.EnumerateFiles(backups, "before-import-*.zip").OrderByDescending(File.GetLastWriteTimeUtc).Skip(3)) { try { File.Delete(old); } catch (IOException) { } }

        string staging = Path.Combine(dataRoot, "import-staging-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            using (var zip = ZipFile.OpenRead(zipPath))
                foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/') && e.FullName != "manifest.json"))
                {
                    string target = Path.GetFullPath(Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe path in archive.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, false);
                }
            foreach (string name in Files)
            {
                string source = Path.Combine(staging, name);
                if (File.Exists(source)) File.Move(source, Path.Combine(dataRoot, name), true);
            }
            foreach (string folder in Folders)
            {
                string source = Path.Combine(staging, folder);
                if (!Directory.Exists(source)) continue;
                string target = Path.Combine(dataRoot, folder);
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(source, target);
            }
        }
        finally { try { Directory.Delete(staging, true); } catch (IOException) { } }
        return backup;
    }

    internal static bool IsAllowedName(string name)
    {
        if (name == "manifest.json" || Files.Contains(name)) return true;
        foreach (string folder in Folders)
        {
            if (!name.StartsWith(folder + "/", StringComparison.Ordinal)) continue;
            string leaf = name[(folder.Length + 1)..];
            return leaf.Length > 0 && leaf.Length <= 120 && leaf.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && leaf.IndexOfAny(['/', '\\', ':', '\0']) < 0 && !leaf.Contains("..");
        }
        return false;
    }
    private static string Shorten(string name) => name.Length <= 60 ? name : name[..57] + "...";
}
