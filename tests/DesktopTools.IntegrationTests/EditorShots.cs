using System.Collections.Generic;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Core;
using DesktopTools.Extras;

/// <summary>
/// Renders the screenshot editor in the states a user sees after pressing Edit (opened, privacy check, covers applied) to PNG files,
/// using a fictional account page. EDITOR_OUT picks the folder, EDITOR_LAYOUT the toolbar placement ("A" top, "B" bottom).
/// </summary>
internal static class EditorShots
{
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Save(Window window, string path)
    {
        window.UpdateLayout();
        double scale = 1.5;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * scale), (int)Math.Ceiling(window.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
        Console.WriteLine("EDITOR " + path);
    }
    private static BitmapSource AccountPage()
    {
        var visual = new DrawingVisual();
        using (var draw = visual.RenderOpen())
        {
            Brush ink = new SolidColorBrush(Color.FromRgb(22, 30, 46)), muted = new SolidColorBrush(Color.FromRgb(98, 110, 130));
            Brush page = new SolidColorBrush(Color.FromRgb(240, 243, 249)), blue = new SolidColorBrush(Color.FromRgb(37, 99, 235));
            var line = new Pen(new SolidColorBrush(Color.FromRgb(222, 228, 238)), 2);
            draw.DrawRectangle(page, null, new Rect(0, 0, 1600, 900)); draw.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1600, 96));
            draw.DrawEllipse(blue, null, new Point(88, 48), 22, 22);
            void Text(string text, double size, double x, double y, Brush brush, bool bold = false, string font = "Segoe UI") =>
                draw.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily(font), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush, 1), new Point(x, y));
            Text("Acme Cloud", 32, 124, 26, ink, true); Text("Account settings", 26, 1260, 30, muted);
            draw.DrawRoundedRectangle(Brushes.White, null, new Rect(90, 150, 690, 660), 26, 26); draw.DrawRoundedRectangle(Brushes.White, null, new Rect(820, 150, 690, 660), 26, 26);
            Text("Profile", 40, 130, 190, ink, true); draw.DrawLine(line, new Point(130, 262), new Point(740, 262));
            Text("Name: Alex Morgan", 32, 130, 300, ink); Text("Email: alex.morgan@example.com", 32, 130, 380, ink);
            Text("Phone: +1 (415) 555-0142", 32, 130, 460, ink); Text("Location: San Francisco", 32, 130, 540, ink);
            Text("Developer access", 40, 860, 190, ink, true); draw.DrawLine(line, new Point(860, 262), new Point(1470, 262));
            Text("API key: sk-live-4f9a8c2e71b3d05a", 30, 860, 302, ink, false, "Consolas"); Text("Server IP: 203.0.113.42", 32, 860, 382, ink);
            Text("Config: C:\\Users\\alex\\acme\\.env", 30, 860, 462, ink, false, "Consolas"); Text("Plan: Team", 32, 860, 542, ink);
        }
        var bitmap = new RenderTargetBitmap(1600, 900, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    internal static async Task RunAsync()
    {
        string folder = Path.GetFullPath(Environment.GetEnvironmentVariable("EDITOR_OUT") is { Length: > 0 } v ? v : "editor-shots"); Directory.CreateDirectory(folder);
        string layout = Environment.GetEnvironmentVariable("EDITOR_LAYOUT") is { Length: > 0 } l ? l : null!;
        string tag = Environment.GetEnvironmentVariable("EDITOR_TAG") ?? "";
        using var controller = new DesktopTools.AppController(true);
        if (Environment.GetEnvironmentVariable("EDITOR_THEME") is { Length: > 0 } theme) { controller.Settings.Theme = theme; controller.ApplyTheme(); }
        var window = new ScreenshotEditorWindow(AccountPage(), _ => { }, _ => { }, editorLayout: layout, autoRedact: new AutoRedactOptions()) { Width = 1280, Height = 800 };
        window.Show(); await Task.Delay(500);
        Save(window, Path.Combine(folder, tag + "1-opened.png"));
        await window.FindSensitiveDataAsync(); await Task.Delay(300);
        Save(window, Path.Combine(folder, tag + "2-checked.png"));
        Walk(window).OfType<Button>().Single(b => b.Name == "HideAllSensitiveData").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(300);
        Save(window, Path.Combine(folder, tag + "3-covered.png"));
        Walk(window).OfType<RadioButton>().First(r => System.Windows.Automation.AutomationProperties.GetName(r) == "Style").IsChecked = true; await Task.Delay(200);
        var field = typeof(ScreenshotEditorView).GetField("_beautify", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(window.View, ((BeautifyOptions)field.GetValue(window.View)!) with { Enabled = true, WindowBar = true });
        typeof(ScreenshotEditorView).GetMethod("RefreshStylePreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window.View, null);
        typeof(ScreenshotEditorView).GetMethod("ApplyLiveStyle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window.View, null); await Task.Delay(300);
        Save(window, Path.Combine(folder, tag + "4-style.png"));
        // The check animation, caught while the analysis is still running.
        var scan = window.FindSensitiveDataAsync(); await Task.Delay(450);
        Save(window, Path.Combine(folder, tag + "5-scanning.png"));
        await scan; await Task.Delay(900);
        window.Close();
        await DrawingSpeedAsync();
    }

    /// <summary>Cost of one mouse move while drawing on a large screenshot: the old way re-composited the whole image, the new way draws the stroke over a cached picture.</summary>
    private static async Task DrawingSpeedAsync()
    {
        var big = new WriteableBitmap(2560, 1392, 96, 96, PixelFormats.Bgra32, null); big.Freeze();
        var window = new ScreenshotEditorWindow(big, _ => { }, _ => { }) { Width = 1400, Height = 800 };
        window.Show(); await Task.Delay(400);
        var drawingField = typeof(ScreenshotEditorView).GetField("_drawing", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var canvas = (FrameworkElement)drawingField.GetValue(window.View)!;
        var pending = canvas.GetType().GetProperty("Pending")!;
        var points = Enumerable.Range(0, 400).Select(i => new Point(100 + i * 5, 400 + Math.Sin(i / 12.0) * 200)).ToArray();
        var documentField = typeof(ScreenshotEditorView).GetField("_document", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var document = (ScreenshotEditDocument)documentField.GetValue(window.View)!;
        var stroke = new Annotation { Kind = AnnotationKind.Pen, Points = points, Color = Colors.Red, Thickness = 4, Opacity = 1 };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 15; i++) document.RenderPreview(stroke with { Id = Guid.NewGuid() }, null);
        double oldPath = watch.Elapsed.TotalMilliseconds / 15;
        var backdrop = document.RenderWithout(null, null);   // done once per stroke now
        watch.Restart();
        for (int i = 0; i < 200; i++)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(backdrop, new Rect(0, 0, 2560, 1392));
                DesktopTools.UI.AnnotationRenderer.Draw(dc, [stroke with { Points = points.Take(200 + i % 200).ToArray() }]);
            }
        }
        double newPath = watch.Elapsed.TotalMilliseconds / 200;
        Console.WriteLine($"DRAWING per mouse move on 2560x1392: before, re-compositing the whole image {oldPath:0.0} ms; now, recording the cached picture plus the stroke {newPath:0.00} ms");
        window.Close();
    }
}
