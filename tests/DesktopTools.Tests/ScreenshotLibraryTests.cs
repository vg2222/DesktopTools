using System.IO;
using DesktopTools.Core;

internal static class ScreenshotLibraryTests
{
    public static void Run()
    {
        void Check(bool value, string what) { if (!value) throw new Exception("Screenshot library assertion failed: " + what); }
        string root = Path.Combine(Path.GetTempPath(), "DesktopTools-library-" + Guid.NewGuid());
        try
        {
            var index = new ScreenshotLibraryIndex(root);
            LibraryEntry Make(string text, int minutesAgo)
            {
                var entry = new LibraryEntry(Guid.NewGuid(), DateTimeOffset.Now.AddMinutes(-minutesAgo), 800, 600, text);
                Directory.CreateDirectory(Path.GetDirectoryName(index.ImagePath(entry.Id))!); File.WriteAllText(index.ImagePath(entry.Id), "png");
                Directory.CreateDirectory(Path.GetDirectoryName(index.ThumbnailPath(entry.Id))!); File.WriteAllText(index.ThumbnailPath(entry.Id), "jpg");
                return entry;
            }
            var error = Make("Error 0x80070005: Access is denied\nPlease contact your administrator", 120);
            var invoice = Make("Invoice 2026-10\nÜberweisung an Café Müller", 60);
            var plain = Make("Nothing special here", 5);
            index.Add(error, 10); index.Add(invoice, 10); index.Add(plain, 10);
            Check(index.Count == 3 && index.All[0].Id == plain.Id, "newest first");
            Check(index.Search("access DENIED").Single().Id == error.Id, "all words, any case");
            Check(index.Search("cafe muller").Single().Id == invoice.Id, "accents are ignored");
            Check(index.Search("denied invoice").Count == 0, "every word must match one screenshot");
            Check(index.Search("  ").Count == 3, "empty query lists everything");
            Check(ScreenshotLibraryIndex.Snippet(error, "administrator") == "Please contact your administrator", "snippet shows the matching line");
            Check(ScreenshotLibraryIndex.Snippet(plain, "zzz") == "Nothing special here", "snippet falls back to the first line");

            // persistence and pruning
            var reopened = new ScreenshotLibraryIndex(root);
            Check(reopened.Count == 3 && reopened.Search("overweisung").Count == 0 && reopened.Search("uberweisung").Count == 1, "index survives restart");
            var newest = Make("Newest", 0);
            reopened.Add(newest, 3);
            Check(reopened.Count == 3 && !File.Exists(reopened.ImagePath(error.Id)) && !File.Exists(reopened.ThumbnailPath(error.Id)), "oldest is removed with its files over the limit");
            Check(reopened.Remove(newest.Id) && !File.Exists(reopened.ImagePath(newest.Id)) && !reopened.Remove(newest.Id), "remove deletes files once");

            // an entry whose image vanished is dropped on load; a damaged index is set aside
            File.Delete(reopened.ImagePath(plain.Id));
            Check(new ScreenshotLibraryIndex(root).All.All(e => e.Id != plain.Id), "missing image dropped");
            File.WriteAllText(Path.Combine(root, "index.json"), "{ broken");
            var recovered = new ScreenshotLibraryIndex(root);
            Check(recovered.Count == 0 && recovered.RecoveryMessage != null && Directory.EnumerateFiles(root, "index.damaged-*.json").Any(), "damaged index kept aside");

            // clear removes everything, including stray files
            var again = new ScreenshotLibraryIndex(root); var e1 = Make("one", 1); again.Add(e1, 10);
            File.WriteAllText(Path.Combine(root, "images", "stray.png"), "x");
            again.Clear();
            Check(again.Count == 0 && !Directory.EnumerateFiles(Path.Combine(root, "images")).Any() && !Directory.EnumerateFiles(Path.Combine(root, "thumbs")).Any(), "clear leaves no images");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
