using DesktopTools.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DesktopTools.Extras;

public sealed class FloatingNote : System.ComponentModel.INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    private string title = "";
    private string body = "";
    public string Title { get => title; set { if (title == value) return; title = value; PropertyChanged?.Invoke(this, new(nameof(Title))); } }
    public string Body { get => body; set { if (body == value) return; body = value; PropertyChanged?.Invoke(this, new(nameof(Body))); } }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Bounded local notes storage. A failed read never permits overwriting unread data.</summary>
public sealed class NotesStore
{
    public const int MaximumNotes = 100;
    public const int MaximumTitleLength = 120;
    public const int MaximumBodyLength = 50000;
    private readonly string directory;
    private readonly string path;
    private bool writable;
    public string? RecoveryMessage { get; private set; }
    private sealed class Document
    {
        public int Version { get; set; } = 1;
        public List<FloatingNote> Notes { get; set; } = new();
    }
    public NotesStore(string directory)
    {
        this.directory = directory;
        path = Path.Combine(directory, "notes.json");
    }
    public List<FloatingNote> Load()
    {
        writable = false;
        RecoveryMessage = null;
        if (!File.Exists(path)) { writable = true; return new(); }
        try
        {
            if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new JsonException(L.T("Notes file exceeds the supported size."));
            var json = File.ReadAllText(path);
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind == JsonValueKind.Object && parsed.RootElement.TryGetProperty("Version", out var version) && version.ValueKind == JsonValueKind.Number && version.TryGetInt64(out var number) && number > 1)
            { RecoveryMessage = L.T("Notes were saved by a newer version. The original file is unchanged; saving is disabled."); return new(); }
            if (parsed.RootElement.ValueKind != JsonValueKind.Object || !parsed.RootElement.TryGetProperty("Version", out _) || !parsed.RootElement.TryGetProperty("Notes", out _))
                throw new JsonException(L.T("Missing notes document fields."));
            var document = JsonSerializer.Deserialize<Document>(json) ?? throw new JsonException(L.T("Empty notes document."));
            if (document.Version != 1 || document.Notes == null) throw new JsonException(L.T("Invalid notes document."));
            try { Validate(document.Notes); } catch (ArgumentException ex) { throw new JsonException(ex.Message, ex); }
            writable = true;
            return document.Notes;
        }
        catch (JsonException)
        {
            string backup = Path.Combine(directory, $"notes.damaged-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            try { File.Move(path, backup); writable = true; RecoveryMessage = L.F($"Unreadable notes were preserved at {backup}"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { RecoveryMessage = L.F($"Notes could not be backed up; saving is disabled. {ex.Message}"); }
            return new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { RecoveryMessage = L.F($"Notes could not be read; saving is disabled. {ex.Message}"); return new(); }
    }
    public void Save(IReadOnlyCollection<FloatingNote> notes)
    {
        if (!writable) throw new InvalidOperationException(L.T("Notes saving is unavailable until the existing file can be loaded safely."));
        Validate(notes);
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $"notes-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, new Document { Notes = notes.ToList() }); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void Validate(IReadOnlyCollection<FloatingNote> notes)
    {
        if (notes.Count > MaximumNotes) throw new ArgumentException(L.T("At most 100 notes can be saved."));
        var ids = new HashSet<Guid>();
        foreach (var note in notes)
            if (note == null || note.Id == Guid.Empty || !ids.Add(note.Id) || note.Title == null || note.Body == null || note.Title.Length > MaximumTitleLength || note.Body.Length > MaximumBodyLength)
                throw new ArgumentException(L.T("Notes contain invalid, duplicate, or oversized entries."));
    }
}
