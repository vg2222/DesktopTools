using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DesktopTools.Extras;

/// <summary>Copy buttons of the text view (--text-copy-only): only the pressed button confirms, the right text lands on the clipboard. The user's clipboard text is put back afterwards.</summary>
internal static class TextCopyChecks
{
    private static System.Collections.Generic.IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Visuals<T>(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    /// <summary>Reads the clipboard text; the framework reader competes with clipboard managers too, so ask again for up to a second.</summary>
    private static string ReadText(string expected)
    {
        string text = "";
        for (int i = 0; i < 20; i++)
        {
            try { text = Clipboard.GetText(); } catch (Exception) { text = ""; }
            if (text == expected) break;
            Thread.Sleep(50);
        }
        return text;
    }

    public static async Task RunAsync()
    {
        string? saved = null;
        if (Clipboard.ContainsText()) saved = Clipboard.GetText();
        else if (Clipboard.GetDataObject()?.GetFormats().Length is > 0) { Console.WriteLine("SKIP text copy checks: the clipboard holds data other than text and is preserved"); return; }
        try
        {
            // The buttons of the text view: only the button that was pressed shows the check mark.
            var image = new System.Windows.Media.Imaging.WriteableBitmap(400, 80, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null); image.Freeze();
            var view = new DesktopTools.Extras.TextSelectionView(image, _ => { }, null, null);
            var shown = new Window { Content = view, Width = 700, Height = 400, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
            shown.Show();
            try
            {
                var layout = new DesktopTools.Core.OcrLayout(400, 80, [new("hello", new Rect(10, 10, 60, 20), 0), new("world", new Rect(80, 10, 60, 20), 0)]);
                view.ShowForTest(layout); view.Model!.SelectWord(0); view.RefreshSelection();
                Button Named(string name) => Visuals<Button>(view).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == name);
                Named("Copy selection").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                await Task.Delay(300);
                Check(ReadText("hello") == "hello", "Copy selection did not copy the selected word: " + Clipboard.GetText());
                Check(Named("Copy selection").Content is System.Windows.Shapes.Path, "Copy selection did not show its check mark");
                Check(Named("Copy all").Content is not System.Windows.Shapes.Path, "Copy selection put the check mark on Copy all");
                await Task.Delay(1500);
                Named("Copy all").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                await Task.Delay(300);
                Check(ReadText("hello world") == "hello world" && Named("Copy all").Content is System.Windows.Shapes.Path && Named("Copy selection").Content is not System.Windows.Shapes.Path, "Copy all marks the wrong button or copies the wrong text");
            }
            finally { shown.Close(); }
        }
        finally { if (saved != null) { try { Clipboard.SetText(saved); } catch (Exception) { } } else { try { Clipboard.Clear(); } catch (Exception) { } } }
        Console.WriteLine("PASS Text view copy buttons: the right text and the check mark on the pressed button only");
    }
}
