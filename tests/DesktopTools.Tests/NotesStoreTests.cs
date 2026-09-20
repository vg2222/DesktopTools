using System.IO;
using DesktopTools.Extras;

internal static class NotesStoreTests
{
    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "DesktopTools-notes-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "notes.json");
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        void Refused(Action action) { bool refused = false; try { action(); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { refused = true; } Check(refused, "Unsafe write should be refused"); }
        try
        {
            var store = new NotesStore(directory);
            Check(store.Load().Count == 0, "Empty initial notes");
            var note = new FloatingNote { Title = "Unicode: привет", Body = "one\ntwo" };
            store.Save(new[] { note });
            var loaded = new NotesStore(directory).Load();
            Check(loaded.Count == 1 && loaded[0].Id == note.Id && loaded[0].Body == note.Body && loaded[0].Title == note.Title, "Round trip preserves content and identity");
            note.Body = "updated"; store.Save(new[] { note });
            Check(new NotesStore(directory).Load()[0].Body == "updated", "Atomic replacement saves edits");
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "Temporary files cleaned");
            string before = File.ReadAllText(path);
            Refused(() => store.Save(new[] { note, note }));
            Refused(() => store.Save(new[] { new FloatingNote { Body = new string('a', NotesStore.MaximumBodyLength + 1) } }));
            Refused(() => store.Save(Enumerable.Range(0, 101).Select(_ => new FloatingNote()).ToArray()));
            Check(File.ReadAllText(path) == before, "Bounds errors preserve existing data");
            File.WriteAllText(path, "{broken");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Check(store.Load().Count == 0 && store.RecoveryMessage != null, "Locked damaged file reports recovery issue");
                Refused(() => store.Save(new[] { note }));
            }
            Check(File.ReadAllText(path) == "{broken", "Locked damaged file preserved");
            store.Load();
            Check(Directory.GetFiles(directory, "notes.damaged-*.json").Any(file => File.ReadAllText(file) == "{broken"), "Malformed contents preserved in backup");
            store.Save(new[] { note });
            const string future = "{\"Version\":999,\"Notes\":{\"future\":true}}";
            File.WriteAllText(path, future);
            Check(store.Load().Count == 0, "Future schema handled without deserializing new shape");
            Refused(() => store.Save(new[] { note }));
            Check(File.ReadAllText(path) == future, "Future file untouched");
        }
        finally { Directory.Delete(directory, true); }
    }
}
