using DesktopTools.Localization;
using System.Windows;
using System.Windows.Threading;
using DesktopTools.UI;

namespace DesktopTools.Extras;

public sealed class FloatingNotesService : IDisposable
{
    private readonly NotesStore store;
    private readonly Action<string> report;
    private readonly List<FloatingNote> notes;
    private readonly Dictionary<Guid, Window> windows = new();
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private NotesWindow? manager;
    private bool disposed;
    private bool dirty;
    private bool saveFailed;
    public bool DefaultTopmost { get; set; } = true;

    public FloatingNotesService(string directory, Action<string> report)
    {
        this.report = report;
        store = new NotesStore(directory);
        notes = store.Load();
        foreach (var note in notes) note.PropertyChanged += NoteChanged;
        if (store.RecoveryMessage is string message) report(message);
        saveTimer.Tick += SaveTick;
    }
    public void Show()
    {
        if (disposed) return;
        if (manager != null) { DesktopTools.Native.NativeWindowService.ShowForeground(manager); return; }
        manager = new NotesWindow(notes, Create, Delete, Open, TryFlush, DefaultTopmost);
        manager.Closed += (_, _) => { manager = null; Flush(); };
        manager.SetSaved(!dirty, saveFailed); DesktopTools.Native.NativeWindowService.ShowForeground(manager);
    }
    private void Create()
    {
        if (notes.Count >= NotesStore.MaximumNotes) { report(L.T("You can keep up to 100 notes. Delete a note to add another.")); return; }
        var note = new FloatingNote(); note.PropertyChanged += NoteChanged;
        notes.Add(note); Changed(); Refresh(); manager?.Select(note);
    }
    private void Refresh() => manager?.Refresh();
    private void NoteChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Changed(); Refresh();
    }
    private void Delete(FloatingNote note)
    {
        if (manager == null || !ConfirmationDialog.Ask(manager, L.T("Delete note"), L.T("Delete this note permanently?"), L.T("Delete note"))) return;
        if (windows.TryGetValue(note.Id, out var window)) ((FloatingNoteWindow)window).CloseForRemoval();
        note.PropertyChanged -= NoteChanged;
        notes.Remove(note); Changed(); Flush(); Refresh();
    }
    private void Open(FloatingNote note)
    {
        if (windows.TryGetValue(note.Id, out var existing)) { DesktopTools.Native.NativeWindowService.ShowForeground(existing); return; }
        var window = new FloatingNoteWindow(note, DefaultTopmost, () => { Show(); manager?.Select(note); }, TryFlush);
        window.Closed += (_, _) => { windows.Remove(note.Id); Flush(); Refresh(); };
        windows.Add(note.Id, window); window.SetSaved(!dirty, saveFailed); DesktopTools.Native.NativeWindowService.ShowForeground(window);
    }
    private void SaveState(bool saved, bool failed = false)
    {
        manager?.SetSaved(saved, failed);
        foreach (var window in windows.Values.OfType<FloatingNoteWindow>()) window.SetSaved(saved, failed);
    }
    private void Changed() { dirty = true; saveFailed = false; SaveState(false); saveTimer.Stop(); saveTimer.Start(); }
    private void SaveTick(object? sender, EventArgs e) { Flush(); }
    public bool TryFlush() { Flush(); return !dirty; }
    private void Flush()
    {
        saveTimer.Stop(); if (!dirty) return;
        try { store.Save(notes); dirty = false; saveFailed = false; SaveState(true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { saveFailed = true; SaveState(false, failed: true); report(L.F($"Notes could not be saved: {ex.Message}")); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var window in windows.Values.ToArray()) window.Close();
        manager?.Close(); Flush();
        foreach (var note in notes) note.PropertyChanged -= NoteChanged; saveTimer.Stop(); saveTimer.Tick -= SaveTick;
    }
}
