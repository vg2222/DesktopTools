using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;

// Reproducible publication captures of real app controls, isolated from the user's profile.
internal static class GitHubGallery
{
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static object? Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, args);
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    internal static async Task RunAsync()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string repo = Path.GetFullPath(Path.Combine(previousDirectory, "../.."));
        // GALLERY_LANGUAGE renders every screen in another language into GALLERY_OUT and lists the text that stayed English; the published gallery is untouched.
        string language = Environment.GetEnvironmentVariable("GALLERY_LANGUAGE") is { Length: > 0 } chosen ? chosen : "en";
        string output = Environment.GetEnvironmentVariable("GALLERY_OUT") is { Length: > 0 } folder ? Path.GetFullPath(folder) : Path.Combine(repo, "docs/screenshots/gallery");
        if (language != "en" && output == Path.Combine(repo, "docs/screenshots/gallery")) throw new InvalidOperationException("Set GALLERY_OUT when GALLERY_LANGUAGE is not en.");
        var missingKeys = new SortedSet<string>(StringComparer.Ordinal); var english = new SortedDictionary<string, string>(StringComparer.Ordinal);
        L.Missing = key => { lock (missingKeys) missingKeys.Add(key); };
        string demo = Path.Combine(repo, "artifacts/github-gallery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output); Directory.CreateDirectory(demo);
        Environment.CurrentDirectory = demo;
        var inventory = new List<string> { "Filename,Width,Height" };
        try
        {
            using var controller = new AppController(true);
            controller.UpdateSettings(s =>
            {
                s.Language = language; s.Theme = "Dark"; s.Animations = false; s.Transparency = false;
                s.SharingWelcomeSeen = true; s.StartAtLogin = false; s.RecordingMicrophone = false; s.RecordingSystemAudio = false;
                s.RecordingQuality = "High"; s.RecordingFramesPerSecond = 60;
            });
            L.Use(language); controller.OpenMain();
            var main = Field<MainWindow>(controller, "main"); main.Width = 1120; main.Height = 800;

            async Task Capture(Window window, string name)
            {
                await Task.Delay(160); window.UpdateLayout();
                UtilityWindowChrome.ClipContent(window);
                // Render vector controls directly at 192 DPI; do not upscale a lower-resolution screenshot.
                int width = (int)Math.Ceiling(window.ActualWidth * 2), height = (int)Math.Ceiling(window.ActualHeight * 2);
                if (width < 200 || height < 100) throw new InvalidOperationException("Window did not lay out: " + name);
                var bitmap = new RenderTargetBitmap(width, height, 192, 192, PixelFormats.Pbgra32);
                bitmap.Render(window); Save(bitmap, Path.Combine(output, name + ".png"));
                inventory.Add($"{name}.png,{width},{height}");
                Console.WriteLine($"CAPTURE {name}: {width} x {height}");
                if (language != "en") foreach (var text in VisibleTexts(window)) if (LooksEnglish(text)) english.TryAdd(text, name);
            }

            foreach (var page in new[] { ("Home", "01-home-dark"), ("Capture tools", "02-capture-tools-dark"), ("Presentation tools", "03-presentation-tools-dark"), ("Media tools", "04-media-tools-dark") })
            { main.Height = page.Item1 == "Home" ? 900 : 800; main.Navigate(page.Item1); await Capture(main, page.Item2); }

            BitmapSource sample = PresentationSlide(0);
            string samplePath = Path.Combine(demo, "Presentation.png"); Save(sample, samplePath);
            var editor = new ScreenshotEditorWindow(sample, _ => { }, _ => { }, editorLayout: "A") { Width = 1120, Height = 800 };
            editor.Show();
            var document = editor.View.Document; // the editor body lives in its view since it became embeddable
            document.Add(new Annotation { Kind = AnnotationKind.Rectangle, Points = [new Point(92, 252), new Point(775, 475)], Color = Color.FromRgb(45, 118, 246), Thickness = 5 });
            document.Add(new Annotation { Kind = AnnotationKind.Arrow, Points = [new Point(1040, 646), new Point(760, 455)], Color = Color.FromRgb(45, 118, 246), Thickness = 7, ArrowHeadSize = 1.2 });
            document.Add(new Annotation { Kind = AnnotationKind.Text, Points = [new Point(1010, 667)], Text = "Keep the message clear", Color = Color.FromRgb(45, 118, 246), FontSize = 31, Bold = true });
            Call(editor.View, "Update"); await Capture(editor, "05-screenshot-editor-dark"); editor.Close();

            var image = new ImageToolsWindow(_ => { }) { Width = 1120, Height = 790 };
            image.Show(); typeof(ImageToolsWindow).GetField("original", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(image, samplePath);
            Call(image, "SetImage", sample);
            await Capture(image, "06-image-editor-dark");
            Walk(image).OfType<Button>().Single(b => AutomationProperties.GetName(b) == L.T("Crop")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Walk(image).OfType<ComboBox>().Single(c => Equals(c.Tag, "crop-ratio")).SelectedItem = "4:3";
            await Capture(image, "07-image-crop-dark"); image.Close();

            var export = new ImageExportDialog(sample, samplePath, "JPEG", choosePath: _ => null);
            export.Show(); await Capture(export, "08-image-export-dark"); export.Close();

            // The recorder gets the preview of this owned demo window, never the user's desktop.
            var scene = new Window { Title = "Presentation demo", Width = 1000, Height = 600, Content = new Image { Source = sample, Stretch = Stretch.Uniform }, Background = Brushes.White, ShowInTaskbar = false };
            scene.Show();
            var source = new RecordingWindowInfo(new WindowInteropHelper(scene).Handle, (uint)Environment.ProcessId, scene.Title);
            var recorder = new ScreenRecorderWindow(controller) { Width = 1120, Height = 800 };
            recorder.SetSource(new RecordingSelection("Presentation demo · application window", Window: source), sample);
            recorder.Show(); await Capture(recorder, "09-screen-recorder-dark"); recorder.Close(); scene.Close();
            var quality = new RecordingQualityWindow(controller.Settings, (_, _, _) => true);
            quality.Show(); await Capture(quality, "10-recording-quality-dark"); quality.Close();

            await GitHubGalleryExtras.RunAsync(controller, Capture, demo);

            string videoPath = Path.Combine(repo, "assets", "desktop-tools-hero.mp4");
            var video = new VideoEditorWindow(message => Console.WriteLine("Video: " + message)) { Width = 1120, Height = 800 };
            video.Show(); await video.LoadAsync(videoPath);
            await Field<Task>(video, "thumbnailTask").WaitAsync(TimeSpan.FromSeconds(30));
            for (int attempt = 0; attempt < 60 && !Field<bool>(video, "mediaOpened"); attempt++) await Task.Delay(100);
            if (!Field<bool>(video, "mediaOpened")) throw new InvalidOperationException("Demo video playback did not open.");
            Field<Slider>(video, "seek").Value = 1;
            await Task.Delay(450); await Capture(video, "19-video-editor-dark"); video.Close();

            main.Navigate("Shortcuts"); await Capture(main, "20-shortcuts-dark");
            foreach (string tab in new[] { "Appearance", "Privacy", "Updates" })
            {
                typeof(MainWindow).GetField("settingsTab", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, tab);
                main.Navigate("Settings"); await Capture(main, tab switch { "Appearance" => "21-appearance-dark", "Privacy" => "22-privacy-dark", _ => "23-updates-dark" });
            }
            main.Navigate("Help"); await Capture(main, "25-help-dark");

            controller.UpdateSettings(s => s.Theme = "Light"); main.Height = 900; main.Navigate("Home"); await Capture(main, "26-home-light");
            var lightEditor = new ScreenshotEditorWindow(sample, _ => { }, _ => { }, editorLayout: "A") { Width = 1120, Height = 800 };
            lightEditor.Show(); await Capture(lightEditor, "27-screenshot-editor-light"); lightEditor.Close();
            var lightImage = new ImageToolsWindow(_ => { }) { Width = 1120, Height = 790 };
            lightImage.Show(); typeof(ImageToolsWindow).GetField("original", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(lightImage, samplePath);
            Call(lightImage, "SetImage", sample); await Capture(lightImage, "28-image-editor-light"); lightImage.Close();

            controller.UpdateSettings(s => s.Theme = "Dark"); main.Height = 800; main.Navigate("Home");
            controller.OpenSetup(restart: true); await Capture(main, "24-first-run-setup-dark");
            Application.Current.Windows.OfType<SetupWindow>().Single().Close();
            File.WriteAllLines(Path.Combine(output, "dimensions.csv"), inventory);
            if (language != "en")
            {
                // The remaining pages and settings tabs, so the report covers the whole main window.
                int extra = 0;
                foreach (var page in new[] { "Home", "News", "Text tools", "Desktop utilities", "Profiles", "Diagnostics", "About", "Draw", "Capture", "Utilities", "Present", "Click indicators", "Shortcut display", "Stopwatch", "Countdown", "Screen ruler", "Screen blackout" })
                { main.Navigate(page); await Capture(main, $"x{++extra:00}-{page.Replace(' ', '-').ToLowerInvariant()}"); }
                foreach (string tab in new[] { "Behavior", "Notifications" })
                {
                    typeof(MainWindow).GetField("settingsTab", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, tab);
                    main.Navigate("Settings"); await Capture(main, $"x{++extra:00}-settings-{tab.ToLowerInvariant()}");
                }
                var report = new List<string> { $"Language: {language}", "", $"== Text asked for but not in the catalog ({missingKeys.Count}):" };
                report.AddRange(missingKeys.Select(key => "  " + key.Replace("\n", "\\n")));
                report.Add(""); report.Add($"== Visible text that stayed English ({english.Count}), with the first screen it appeared on:");
                report.AddRange(english.Select(pair => $"  [{pair.Value}] {pair.Key.Replace("\n", "\\n")}"));
                File.WriteAllLines(Path.Combine(output, "untranslated.txt"), report);
                Console.WriteLine($"UNTRANSLATED {missingKeys.Count} missing keys, {english.Count} English texts -> {Path.Combine(output, "untranslated.txt")}");
            }
            main.Hide();
        }
        finally { Environment.CurrentDirectory = previousDirectory; L.Missing = null; L.Use("en"); }
    }

    /// <summary>Every piece of text a person can read in the window: labels, button captions, placeholders, tooltips and accessible names.</summary>
    private static IEnumerable<string> VisibleTexts(Window window)
    {
        foreach (var node in Walk(window))
        {
            if (node is UIElement { IsVisible: false }) continue;
            if (node is TextBlock block && !string.IsNullOrWhiteSpace(block.Text)) yield return block.Text.Trim();
            if (node is ContentControl { Content: string content } && !string.IsNullOrWhiteSpace(content)) yield return content.Trim();
            if (node is FrameworkElement { ToolTip: ToolTip { Content: string tip } }) yield return tip.Trim();
            if (node is FrameworkElement element && AutomationProperties.GetName(element) is { Length: > 0 } name) yield return name.Trim();
        }
    }

    /// <summary>Two or more ordinary Latin words and nothing in the target script: an English sentence or label, not a name, unit or path.</summary>
    private static bool LooksEnglish(string text)
    {
        if (text.Any(c => c > 0x024F && char.IsLetter(c))) return false;                       // has Cyrillic, CJK, etc.
        if (text.Contains('\\') || text.Contains("://") || text.Contains('@')) return false;    // paths, links, addresses
        var words = System.Text.RegularExpressions.Regex.Matches(text, "[A-Za-z]{3,}").Select(m => m.Value).Where(w => w is not ("DesktopTools" or "GitHub" or "Windows" or "Microsoft" or "Ctrl" or "Alt" or "Shift" or "Esc" or "Enter" or "PNG" or "JPEG" or "JPG" or "WebP" or "GIF" or "MP4" or "FPS" or "OCR" or "HEX" or "RGB" or "HSL" or "MIT" or "Inter" or "Presentation" or "edited" or "demo")).ToArray();
        return words.Length >= 2 || words.Length == 1 && text.Length <= 24 && words[0].Length >= 4;
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }

    private static BitmapSource PresentationSlide(int variation)
    {
        var visual = new DrawingVisual();
        using (var draw = visual.RenderOpen())
        {
            Brush ink = new SolidColorBrush(Color.FromRgb(24, 42, 47));
            Brush muted = new SolidColorBrush(Color.FromRgb(95, 114, 111));
            Brush canvas = new SolidColorBrush(Color.FromRgb(245, 246, 239));
            Brush mint = new SolidColorBrush(Color.FromRgb(185, 218, 199));
            draw.DrawRectangle(canvas, null, new Rect(0, 0, 1600, 900));
            void Text(string text, double size, double x, double y, Brush brush, bool bold = false)
            {
                draw.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush, 1), new Point(x, y));
            }
            Text("STUDIO NOTES", 23, 100, 70, ink, true); Text("A little less noise. A lot more focus.", 21, 1090, 73, muted);
            draw.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(211, 219, 208)), 1), new Point(100, 126), new Point(1500, 126));
            Text("IDEAS WORTH SHARING", 20, 104, 210, muted, true);
            string[] titles = ["A calmer way\nto present.", "Bring your\nideas to life.", "Keep the focus\non what matters."];
            Text(titles[variation], 78, 98, 270, ink, true);
            Text("Capture the detail. Tell the story.\nGive your ideas room to breathe.", 28, 104, 520, muted);
            draw.DrawRoundedRectangle(ink, null, new Rect(104, 657, 234, 64), 12, 12);
            Text("Explore the idea", 23, 132, 673, Brushes.White, true);
            draw.DrawRoundedRectangle(mint, null, new Rect(920, 196, 580, 556), 30, 30);
            draw.PushClip(new RectangleGeometry(new Rect(920, 196, 580, 556), 30, 30));
            for (int i = 0; i < 7; i++)
                draw.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb((byte)(78 + i * 12), (byte)(126 + i * 9), (byte)(108 + i * 9))), 30), new Point(1250, 550), 50 + i * 49, 50 + i * 49);
            draw.Pop();
            Text("01  /  PRESENT WITH PURPOSE", 18, 104, 815, muted, true); Text("DESKTOPTOOLS DEMO", 18, 1270, 815, muted);
        }
        var bitmap = new RenderTargetBitmap(1600, 900, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

}
