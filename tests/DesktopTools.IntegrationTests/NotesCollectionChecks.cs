using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Extras;

internal static class NotesCollectionChecks
{
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj)!;
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        controller.UpdateSettings(s => { s.Theme = "Dark"; s.Animations = false; });
        DesktopTools.Localization.L.Use("en");
        using (var editing = new NoteEditorView())
        {
            editing.SetNote(new FloatingNote());
            var probe = new Window { Content = editing, Width = 500, Height = 400 }; probe.Show(); await Task.Delay(30); probe.UpdateLayout();
            try
            {
                foreach (var input in new[] { editing.TitleEditor, editing.BodyEditor })
                {
                    input.Focus(); probe.UpdateLayout();
                    var hint = Children(editing).OfType<TextBlock>().Single(x => x.Text == (input.AcceptsReturn ? "Write a note…" : "Untitled note"));
                    var hintOrigin = hint.TransformToAncestor(editing).Transform(new Point());
                    var caret = input.TransformToAncestor(editing).TransformBounds(input.GetRectFromCharacterIndex(0));
                    Console.WriteLine($"Note text origin {input.Name}: caret={caret}; placeholder={hintOrigin}");
                    Check(Math.Abs(caret.X - hintOrigin.X) <= 2.5 && Math.Abs(caret.Y - hintOrigin.Y) <= 2.5, "Note caret starts away from its placeholder");
                    input.Text = "Sample text"; probe.UpdateLayout();
                    var point = input.TransformToAncestor(editing).Inverse!.Transform(new Point(hintOrigin.X + 1, hintOrigin.Y + caret.Height / 2));
                    Check(input.GetCharacterIndexFromPoint(point, true) == 0, "Clicking placeholder origin did not target the beginning of text");
                }
            }
            finally { probe.Close(); }
        }
        string directory = Path.Combine(Environment.CurrentDirectory, "notes-collection-" + Guid.NewGuid().ToString("N"));
        var store = new NotesStore(directory); store.Load();
        store.Save(new List<FloatingNote> { new FloatingNote { Title = "План встречи", Body = "Обсудить макеты\nПроверить запись" }, new FloatingNote { Title = "Ideas", Body = "Release notes" } });
        using (var service = new FloatingNotesService(directory, _ => { }))
        {
            service.Show(); await Task.Delay(80);
            var window = Field<NotesWindow>(service, "manager");
            var controls = Children(window).OfType<TextBox>().ToArray();
            var search = controls.Single(x => x.Name == "NotesSearch"); var title = controls.Single(x => x.Name == "NoteTitle"); var body = controls.Single(x => x.Name == "NoteBody");
            search.Text = "release";
            Check(Children(window).OfType<Button>().Count(x => x.Tag is Guid) == 1, "Body search did not filter notes");
            search.Text = "nothing-matches"; Check(!Children(window).OfType<Button>().Any(x => x.Tag is Guid), "Empty search result retained rows");
            search.Text = "";
            var model = Field<List<FloatingNote>>(service, "notes")[0];
            var originalRow = Children(window).OfType<Button>().Single(x => Equals(x.Tag, model.Id));
            body.Focus(); body.CaretIndex = body.Text.Length;
            System.Windows.Input.TextCompositionManager.StartComposition(new System.Windows.Input.TextComposition(System.Windows.Input.InputManager.Current, body, " typed"));
            Check(ReferenceEquals(originalRow, Children(window).OfType<Button>().Single(x => Equals(x.Tag, model.Id))), "Typing rebuilt the selected note row");
            Check(body.CaretIndex == body.Text.Length && body.CanUndo, $"Typing lost caret or native undo: caret={body.CaretIndex}, length={body.Text.Length}, undo={body.CanUndo}");
            var second = Field<List<FloatingNote>>(service, "notes")[1]; window.Select(second);
            Check(!body.CanUndo && !title.CanUndo, "Switching notes retained another note's undo history");
            body.Undo(); Check(second.Body == "Release notes", "Undo crossed between notes"); window.Select(model);
            typeof(FloatingNotesService).GetMethod("Open", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, new object[] { model });
            var floating = Field<Dictionary<Guid, Window>>(service, "windows")[model.Id];
            title.Text = "Совещание • café"; body.Text = "One\nДва\n三"; await Task.Delay(30);
            var floatingEditors = Children(floating).OfType<TextBox>().ToArray();
            Check(floatingEditors.Any(x => x.Text == body.Text), "Collection edit did not reach floating editor");
            floatingEditors.Single(x => x.AcceptsReturn).Text = "Из плавающего окна"; await Task.Delay(30);
            Check(body.Text == "Из плавающего окна", "Floating edit did not reach collection");
            Check(!body.CanUndo, "External edit retained stale undo history");
            floating.UpdateLayout();
            var floatRender = new RenderTargetBitmap((int)floating.ActualWidth, (int)floating.ActualHeight, 96, 96, PixelFormats.Pbgra32); floatRender.Render(floating);
            var floatEncoder = new PngBitmapEncoder(); floatEncoder.Frames.Add(BitmapFrame.Create(floatRender));
            using (var output = File.Create("notes-floating-Dark.png")) floatEncoder.Save(output);
            Check(service.TryFlush(), "Initial note changes did not save");
            using (var locked = new FileStream(Path.Combine(directory, "notes.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                body.Text = "Unsaved while file is locked";
                Check(!service.TryFlush(), "Locked notes unexpectedly saved");
                floating.Close(); Check(floating.IsVisible, "Floating note closed despite failed persistence");
                Check(Children(floating).OfType<Button>().Any(x => x.IsVisible && System.Windows.Automation.AutomationProperties.GetName(x) == "Retry saving"), "Save failure did not expose retry");
            }
            Check(service.TryFlush() && new NotesStore(directory).Load()[0].Body == body.Text, "Retry after save failure lost text");
            floating.Close();
            foreach (string theme in new[] { "Dark", "Light" })
            {
                controller.UpdateSettings(s => { s.Theme = theme; s.Animations = false; }); await Task.Delay(60); window.UpdateLayout();
                var render = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); render.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(render)); using var output = File.Create(Path.Combine(Environment.CurrentDirectory, "notes-collection-" + theme + ".png")); encoder.Save(output);
            }
            body.Text = "Saved immediately on close"; window.Close();
            Check(new NotesStore(directory).Load()[0].Body == "Saved immediately on close", "Close lost debounced text");
            service.Show(); Check(Field<NotesWindow>(service, "manager").IsVisible, "Collection did not reopen");
        }
        using (var service = new FloatingNotesService(directory, _ => { }))
        {
            service.Show();
            Check(Field<List<FloatingNote>>(service, "notes")[0].Title == "Совещание • café", "Restart lost Unicode title");
        }
        bool canClose = false;
        var blocked = new NotesWindow(new List<FloatingNote>(), () => { }, _ => { }, _ => { }, () => canClose, false);
        blocked.Show(); blocked.Close(); Check(blocked.IsVisible, "Failed persistence allowed close");
        canClose = true; blocked.Close();
    }
}
