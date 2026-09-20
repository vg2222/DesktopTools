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
        string output = Path.Combine(repo, "docs/screenshots/gallery");
        string demo = Path.Combine(repo, "artifacts/github-gallery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output); Directory.CreateDirectory(demo);
        Environment.CurrentDirectory = demo;
        var inventory = new List<string> { "Filename,Width,Height" };
        try
        {
            using var controller = new AppController(true);
            controller.UpdateSettings(s =>
            {
                s.Language = "en"; s.Theme = "Dark"; s.Animations = false; s.Transparency = false;
                s.SharingWelcomeSeen = true; s.StartAtLogin = false; s.RecordingMicrophone = false; s.RecordingSystemAudio = false;
                s.RecordingQuality = "High"; s.RecordingFramesPerSecond = 60;
            });
            L.Use("en"); controller.OpenMain();
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
            }

            foreach (var page in new[] { ("Home", "01-home-dark"), ("Capture tools", "02-capture-tools-dark"), ("Presentation tools", "03-presentation-tools-dark"), ("Media tools", "04-media-tools-dark") })
            { main.Height = page.Item1 == "Home" ? 900 : 800; main.Navigate(page.Item1); await Capture(main, page.Item2); }

            BitmapSource sample = PresentationSlide(0);
            string samplePath = Path.Combine(demo, "Presentation.png"); Save(sample, samplePath);
            var editor = new ScreenshotEditorWindow(sample, _ => { }, _ => { }, editorLayout: "A") { Width = 1120, Height = 800 };
            editor.Show();
            var document = Field<ScreenshotEditDocument>(editor, "_document");
            document.Add(new Annotation { Kind = AnnotationKind.Rectangle, Points = [new Point(92, 252), new Point(775, 475)], Color = Color.FromRgb(45, 118, 246), Thickness = 5 });
            document.Add(new Annotation { Kind = AnnotationKind.Arrow, Points = [new Point(1040, 646), new Point(760, 455)], Color = Color.FromRgb(45, 118, 246), Thickness = 7, ArrowHeadSize = 1.2 });
            document.Add(new Annotation { Kind = AnnotationKind.Text, Points = [new Point(1010, 667)], Text = "Keep the message clear", Color = Color.FromRgb(45, 118, 246), FontSize = 31, Bold = true });
            Call(editor, "Update"); await Capture(editor, "05-screenshot-editor-dark"); editor.Close();

            var image = new ImageToolsWindow(_ => { }) { Width = 1120, Height = 790 };
            image.Show(); typeof(ImageToolsWindow).GetField("original", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(image, samplePath);
            Call(image, "SetImage", sample);
            await Capture(image, "06-image-editor-dark");
            Walk(image).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Crop").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Walk(image).OfType<ComboBox>().Single(c => Equals(c.Tag, "crop-ratio")).SelectedItem = "4:3";
            await Capture(image, "07-image-crop-dark"); image.Close();

            var export = new ImageExportDialog(sample, samplePath, "JPEG", choosePath: _ => null);
            export.Show(); await Capture(export, "08-image-export-dark"); export.Close();

            // The recorder gets the preview of this owned demo window, never the user's desktop.
            var scene = new Window { Title = "Presentation demo", Width = 1000, Height = 600, Content = new Image { Source = sample, Stretch = Stretch.Uniform }, Background = Brushes.White, ShowInTaskbar = false };
            scene.Show();
            var source = new RecordingWindowInfo(new WindowInteropHelper(scene).Handle, (uint)Environment.ProcessId, scene.Title);
            var recorder = new ScreenRecorderWindow(controller) { Width = 1000, Height = 660 };
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
            main.Hide();
        }
        finally { Environment.CurrentDirectory = previousDirectory; }
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
