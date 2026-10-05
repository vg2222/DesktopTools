using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using DesktopTools.Core;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Extras;
using DesktopTools.UI;

/// <summary>Word-level OCR layout, the text selection view and its embedding in the editors (--text-selection-only).</summary>
internal static class TextSelectionChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static OcrWordBox W(string text, double x, double y, double w = 40, double h = 12, int line = 0) => new(text, new Rect(x, y, w, h), line);

    public static async Task RunAsync()
    {
        ToLayoutKeepsOneReadingPerLine();
        await ScanOverlayShowsAndHidesAsync();
        await TextViewAsync();
        await ScreenTextWindowAsync();
        await EditorTextModeAsync();
        await ImageToolsAsync();
    }

    private static void ToLayoutKeepsOneReadingPerLine()
    {
        var left = new OcrEnhancer.Line("hello world", new Rect(0, 0, 100, 12), [W("hello", 0, 0), W("world", 50, 0)]);
        var right = new OcrEnhancer.Line("again", new Rect(200, 2, 40, 12), [W("again", 200, 2)]);
        var below = new OcrEnhancer.Line("below", new Rect(0, 40, 40, 12), [W("below", 0, 40)]);
        var layout = OcrEnhancer.ToLayout([below, right, left], 300, 100);
        string order = string.Join(" ", layout.Words.Select(w => w.Text));
        Check(order == "hello world again below", "Row order wrong: " + order);
        Check(layout.Words.Select(w => w.LineIndex).Distinct().Count() == 3 && layout.Words[0].LineIndex < layout.Words[2].LineIndex, "Line indexes wrong");
        Check(new OcrEnhancer.Line("x", new Rect(0, 0, 1, 1)).Words == null, "Words must stay optional");
        Console.WriteLine("PASS ToLayout keeps one reading per line in row order with word boxes");
    }

    private static byte[] Pixel(FrameworkElement element, int x, int y)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0); return pixel;
    }

    private static async Task ScanOverlayShowsAndHidesAsync()
    {
        var overlay = new ScanOverlay();
        var host = new Grid { Width = 400, Height = 300, Background = new SolidColorBrush(Color.FromRgb(128, 128, 128)) };
        host.Children.Add(overlay);
        var window = new Window { Content = host, SizeToContent = SizeToContent.WidthAndHeight, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        window.Show();
        try
        {
            await Task.Delay(50);
            var before = Pixel(host, 1, 150);
            Check(before[0] == 128 && before[1] == 128 && before[2] == 128 && overlay.Visibility == Visibility.Collapsed, "Overlay should be invisible before Start");
            overlay.Start(400, 300, 8); await Task.Delay(500);
            var during = Pixel(host, 1, 150);
            Check(overlay.Visibility == Visibility.Visible && Math.Max(during[0], Math.Max(during[1], during[2])) - Math.Min(during[0], Math.Min(during[1], during[2])) > 40,
                $"Rainbow rim not visible at the edge while scanning: {during[2]},{during[1]},{during[0]}");
            overlay.Stop(); await Task.Delay(1200);
            var after = Pixel(host, 1, 150);
            Check(overlay.Visibility == Visibility.Collapsed && after[0] == 128 && after[1] == 128 && after[2] == 128, "Overlay did not hide after Stop");
            overlay.Stop();   // stopping twice must be harmless
        }
        finally { window.Close(); }
        Console.WriteLine("PASS ScanOverlay shows a rainbow rim while scanning and hides afterwards");
    }

    private static BitmapSource TextImage(string[] lines, Color background, Color foreground, int width = 900, double size = 22)
    {
        double lineHeight = size * 1.9; int height = (int)(lineHeight * lines.Length + 60);
        var visual = new DrawingVisual();
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Display);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
            for (int i = 0; i < lines.Length; i++)
                dc.DrawText(new FormattedText(lines[i], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, new SolidColorBrush(foreground), 1.0), new Point(30, 30 + i * lineHeight));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    private static void SavePng(BitmapSource bitmap, string name)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(name); encoder.Save(stream);
    }

    private static byte[] Bytes(BitmapSource image) { var data = new byte[image.PixelWidth * image.PixelHeight * 4]; new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0).CopyPixels(data, image.PixelWidth * 4, 0); return data; }

    private static double Brightness(BitmapSource bitmap, int x, int y)
    {
        var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(Math.Clamp(x, 0, bitmap.PixelWidth - 1), Math.Clamp(y, 0, bitmap.PixelHeight - 1), 1, 1), pixel, 4, 0);
        return (pixel[0] + pixel[1] + pixel[2]) / 3.0;
    }

    private static async Task TextViewAsync()
    {
        string[] lines = ["Settings Capture tools Screen recorder", "Region capture Ctrl + Alt + S", "Visit https://example.com/docs today", "Total amount due this month"];
        var light = Color.FromRgb(235, 235, 240);
        var image = TextImage(lines, light, Colors.Black);
        var before = Bytes(image);
        var reported = new List<string>();
        var view = new TextSelectionView(image, reported.Add, text => reported.Add("translate:" + text), null);
        var window = new Window { Content = view, Width = 1100, Height = 760, WindowStyle = WindowStyle.None, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = 40, Top = 40 };
        window.Show();
        try
        {
            await view.RecognizeAsync(); await Task.Delay(900);
            Check(view.Layout != null && view.Layout.Words.Count >= 12, "Too few words recognized: " + view.Layout?.Words.Count);
            Check(view.AllText.Contains("Settings") && view.AllText.Contains("recorder"), "Recognized text missing words: " + view.AllText);
            Check(view.CanCopyAll && view.StatusText.Contains("words"), "Status/copy state wrong after recognition: " + view.StatusText);
            Check(view.Links.Any(l => l.Kind == TextLinkKind.Url && l.Target.StartsWith("https://example.com")), "The link in the picture was not detected");

            // The dim layer: text areas keep the picture's brightness, empty areas are clearly darker.
            var word = view.Model!.Words[0].Bounds;
            var rendered = view.RenderSurface();
            Point backgroundInWord = default; bool found = false;
            for (int y = (int)word.Top + 2; y < word.Bottom - 2 && !found; y++)
                for (int x = (int)word.Left + 2; x < word.Right - 2 && !found; x++)
                    if (Brightness(image, x, y) > 230 && Brightness(image, x + 1, y) > 230 && Brightness(image, x, y + 1) > 230 && Brightness(image, x - 1, y) > 230 && Brightness(image, x, y - 1) > 230) { backgroundInWord = new Point(x + .5, y + .5); found = true; }
            Check(found, "Test picture has no background pixel inside the first word");
            var inside = view.ImageToSurface(backgroundInWord);
            var empty = view.ImageToSurface(new Point(image.PixelWidth - 20, image.PixelHeight - 15));
            double bright = Brightness(rendered, (int)inside.X, (int)inside.Y), dim = Brightness(rendered, (int)empty.X, (int)empty.Y);
            Check(bright > 215, $"Text area was dimmed (brightness {bright:0})");
            Check(dim < 235 * 0.65, $"Empty area is not dimmed (brightness {dim:0})");

            view.Model.Begin(1); view.Model.Extend(5); view.RefreshSelection(); await Task.Delay(50);
            SavePng(view.RenderSurface(), "text-view-selection.png");
            view.Model.SelectAll(); view.RefreshSelection();
            Check(view.SelectedText == view.AllText && view.SelectedText.Length > 20, "Select all did not select everything");
            view.Model.Clear();
            Check(view.SelectedText == "", "Clear left a selection");
            view.Model.SetOverride(0, "Fixed");
            Check(view.AllText.StartsWith("Fixed"), "A fixed word was not used in the copied text");
            Check(Bytes(image).SequenceEqual(before), "The picture changed while selecting/fixing text");
        }
        finally { window.Close(); view.Cancel(); }

        // A picture without text: clear message, nothing to copy, no exception.
        var blank = TextImage([], light, Colors.Black, 300);
        var emptyView = new TextSelectionView(blank, _ => { }, null, null);
        var emptyWindow = new Window { Content = emptyView, Width = 600, Height = 400, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        emptyWindow.Show();
        try
        {
            await emptyView.RecognizeAsync();
            Check(emptyView.Layout!.Words.Count == 0 && !emptyView.CanCopyAll && emptyView.StatusText.Contains("No text found"), "Blank picture state wrong: " + emptyView.StatusText);
        }
        finally { emptyWindow.Close(); emptyView.Cancel(); }

        // Cancelling during a scan must not throw or leave the animation running.
        var busy = new TextSelectionView(TextImage(lines, light, Colors.Black), _ => { }, null, null);
        var busyWindow = new Window { Content = busy, Width = 900, Height = 600, WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        busyWindow.Show();
        try
        {
            var scan = busy.RecognizeAsync(); busy.Cancel(); await scan;
            Check(!busy.IsScanning, "Scan still marked as running after Cancel");
        }
        finally { busyWindow.Close(); }
        Console.WriteLine("PASS Text view recognizes, dims, selects, fixes, handles blank pictures and cancellation");
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Visuals<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static async Task ScreenTextWindowAsync()
    {
        string[] lines = ["Settings Capture tools Screen recorder", "Region capture Ctrl + Alt + S", "Contact support@example.com or +1 415 555 0199", "Total: 1,234.56 USD on 2026-10-05"];
        var image = TextImage(lines, Color.FromRgb(22, 22, 28), Color.FromRgb(225, 228, 235), 820, 20);
        var window = new ScreenTextWindow(image, _ => { }, text => { });
        window.Show();
        try
        {
            var view = Visuals<TextSelectionView>(window).Single();
            for (int i = 0; i < 100 && view.Model == null; i++) await Task.Delay(100);
            await Task.Delay(900);
            Check(view.Model != null && view.Model.Count >= 12, "Window did not recognize the picture on open");
            Check(view.Links.Any(l => l.Kind == TextLinkKind.Email) && view.Links.Any(l => l.Kind == TextLinkKind.Phone), "E-mail/phone in the picture not detected");
            view.Model!.SelectLine(0); view.RefreshSelection(); await Task.Delay(100);
            var shot = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); shot.Render((Visual)window.Content);
            SavePng(shot, "screen-text-window.png");
            Check(window.ActualWidth > 900 && window.ActualHeight > 560, $"Window is too small: {window.ActualWidth:0}x{window.ActualHeight:0}");
        }
        finally { window.Close(); }
        Console.WriteLine("PASS Scan screen text window opens large, recognizes on open and finds e-mail and phone links");
    }

    private static async Task EditorTextModeAsync()
    {
        string[] lines = ["Settings Capture tools Screen recorder", "Region capture Ctrl + Alt + S", "Total amount due this month"];
        var image = TextImage(lines, Color.FromRgb(235, 235, 240), Colors.Black);
        var translated = new List<string>();
        var window = new ScreenshotEditorWindow(image, _ => { }, _ => { }, translate: translated.Add);
        window.Show();
        try
        {
            await Task.Delay(300);
            var view = window.View;
            var document = (ScreenshotEditDocument)typeof(ScreenshotEditorView).GetField("_document", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
            document.Add(new Annotation { Color = Colors.Red, Thickness = 6, Points = [new Point(600, 20), new Point(760, 60)] });
            int items = document.Items.Count;
            Check(!view.InTextMode && !Visuals<TextSelectionView>(window).Any(), "Text view present before Extract text");
            view.ShowTextMode();
            Check(view.InTextMode && view.Backdrop.Visibility == Visibility.Collapsed && view.PropertyCard.Visibility == Visibility.Collapsed && view.ToolCard.Visibility == Visibility.Collapsed, "Editor panels still visible in text mode");
            var text = Visuals<TextSelectionView>(window).Single();
            for (int i = 0; i < 100 && text.Model == null; i++) await Task.Delay(100);
            Check(text.Model != null && text.AllText.Contains("Settings"), "Embedded text view did not recognize the edited picture");
            var back = Visuals<Button>(window).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Back to editing");
            Check(back.IsVisible, "Back to editing is not visible");
            var translate = Visuals<Button>(window).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Translate");
            text.Model!.SelectLine(0); text.RefreshSelection(); translate.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(translated.Count == 1 && translated[0].StartsWith("Settings"), "Translate did not hand the selected text over");
            back.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(!view.InTextMode && view.Backdrop.Visibility == Visibility.Visible && view.PropertyCard.Visibility == Visibility.Visible && view.ToolCard.Visibility == Visibility.Visible, "Back to editing did not restore the editor");
            Check(!Visuals<TextSelectionView>(window).Any() && document.Items.Count == items, "Back to editing left the text view or changed the annotations");
            // Escape in the text view leaves it (selection first) instead of closing the whole editor.
            view.ShowTextMode(); await Task.Delay(100);
            var escape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            view.RaiseEvent(escape);
            Check(window.IsVisible && !view.InTextMode, "Escape in the text view closed the editor or did not leave the text view");
            // Closing the window while the text scan is still running must be harmless.
            view.ShowTextMode(); window.Close(); await Task.Delay(300);
        }
        finally { if (window.IsVisible) window.Close(); }
        Console.WriteLine("PASS Screenshot editor: Extract text opens the embedded text view and Back to editing restores the editor");
    }

    private static void ShotOf(Window window, string name)
    {
        var shot = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); shot.Render((Visual)window.Content); SavePng(shot, name);
    }

    private static Button ByName(DependencyObject root, string name) => Visuals<Button>(root).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == name);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

    private static async Task ImageToolsAsync()
    {
        string[] lines = ["Settings Capture tools Screen recorder", "Region capture Ctrl + Alt + S", "Total amount due this month"];
        var image = TextImage(lines, Color.FromRgb(235, 235, 240), Colors.Black);
        var sourceBytes = Bytes(image);
        var window = new ImageToolsWindow(_ => { }) { WindowState = WindowState.Normal, Width = 1300, Height = 800 };
        window.Show();
        try
        {
            await Task.Delay(300);
            window.LoadImage("sample.png", image); await Task.Delay(200);
            ShotOf(window, "image-tools-size.png");
            foreach (string tab in new[] { "Size", "Crop", "Format", "Background", "Annotate" })
            {
                var button = ByName(window, tab);
                Check(!window.Inspector.IsAncestorOf(button), $"The '{tab}' tab is still inside the right-hand inspector");
            }
            Check(window.AnnotateView == null && !window.HasUnsavedChanges, "Fresh image should not be in Annotate mode or have unsaved changes");

            Click(ByName(window, "Annotate")); await Task.Delay(300);
            var view = window.AnnotateView;
            Check(view != null && view.IsVisible && window.Inspector.Visibility == Visibility.Collapsed, "Annotate did not host the editor in place of the inspector");
            Check(Visuals<RadioButton>(window).Any(r => System.Windows.Automation.AutomationProperties.GetName(r) == "Hide data") && !Visuals<RadioButton>(window).Any(r => System.Windows.Automation.AutomationProperties.GetName(r) == "Style"),
                "Annotate should offer Draw and Hide data but not Style");
            ShotOf(window, "image-tools-annotate.png");
            view!.Document.Add(new Annotation { Color = Colors.Red, Thickness = 12, Points = [new Point(700, 15), new Point(860, 15)] });
            Check(window.HasUnsavedChanges, "Pending annotations are not reported as unsaved changes");
            Click(ByName(window, "Crop")); await Task.Delay(300);
            Check(window.AnnotateView == null && !view.IsVisible, "Leaving Annotate did not discard the editor");
            var baked = Bytes(window.CurrentBitmap!);
            int index = (15 * image.PixelWidth + 780) * 4;
            Check(baked.Length == sourceBytes.Length && baked[index + 2] > 200 && baked[index + 1] < 80, "The annotation was not baked into the image");
            Check(window.CanUndoImage, "Baking should be one undoable history step");

            // Original / Edited: the same icon swap as in the screenshot editor.
            var original = ByName(window, "Show original");
            Check(((System.Windows.Shapes.Path)original.Content).Data.ToString() == ToolIcons.GeometryFor("Image").ToString(), "Original button should start with the picture icon");
            Click(original);
            var edited = ByName(window, "Show edited");
            Check(((System.Windows.Shapes.Path)edited.Content).Data.ToString() == ToolIcons.GeometryFor("Draw").ToString(), "Original button icon did not change while showing the original");
            Click(edited);
            Check(ByName(window, "Show original") != null, "Original button did not return to its first state");

            Click(ByName(window, "Undo")); await Task.Delay(100);
            Check(Bytes(window.CurrentBitmap!).SequenceEqual(sourceBytes), "Undo after baking did not restore the picture");

            // Extract text and back.
            var before = window.CurrentBitmap; var modeBefore = window.ActiveMode;
            Click(ByName(window, "Extract text")); await Task.Delay(200);
            var text = window.TextView;
            Check(text != null && !window.ModeStrip.IsVisible && !window.Inspector.IsVisible, "Extract text did not take over the workspace");
            for (int i = 0; i < 100 && text!.Model == null; i++) await Task.Delay(100);
            Check(text!.AllText.Contains("Settings"), "Image tools text view did not read the picture");
            await Task.Delay(700); ShotOf(window, "image-tools-text.png");
            Check(!ByName(window, "Undo").IsEnabled && !ByName(window, "Redo").IsEnabled && !ByName(window, "Show original").IsEnabled, "Header undo/redo/original must be disabled while the text view is open");
            Click(ByName(window, "Back to editing")); await Task.Delay(200);
            Check(window.TextView == null && window.ModeStrip.IsVisible && window.ActiveMode == modeBefore && ReferenceEquals(window.CurrentBitmap, before), "Back to editing did not restore the previous mode and picture");
            Check(ByName(window, "Show original").IsEnabled && ByName(window, "Extract text").IsEnabled && !ByName(window, "Undo").IsEnabled == !window.CanUndoImage, "Header buttons were not restored after Back to editing");
        }
        finally { window.Close(); }
        Console.WriteLine("PASS Image tools: Annotate tab, one-step baking, Original/Edited icon swap and Extract text with Back to editing");
    }
}
