using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Core;
using DesktopTools.Extras;

/// <summary>
/// Face suggestions in Check screenshot (spec section 9): the optional YuNet pack finds faces in a generated video-call frame at two sizes,
/// nothing in a text-only screenshot; the analyzer adds faces to text findings only while the pack is installed; a damaged file adds nothing
/// and marks the pack damaged; a check writes nothing under the models folder. The real-model part needs the developer's file and the
/// public-domain astronaut photo from scikit-image under E:\DesktopTools-ai-spikes; without them it is Not tested.
/// </summary>
internal static class FaceSuggestionChecks
{
    internal const string DevModel = @"E:\DesktopTools-ai-spikes\models\faces\face_detection_yunet_2026may.onnx";
    internal const string Astronaut = @"E:\DesktopTools-ai-spikes\venv\Lib\site-packages\skimage\data\astronaut.png";
    // Where the face is in the 512 × 512 astronaut photo (measured with the reference decoder).
    private static readonly Rect AstronautFace = new(178, 63, 90, 113);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    private static double Overlap(Rect a, Rect b)
    {
        var shared = Rect.Intersect(a, b); if (shared.IsEmpty) return 0;
        double area = shared.Width * shared.Height; return area / (a.Width * a.Height + b.Width * b.Height - area);
    }
    private static Rect ToRect(Int32Rect r) => new(r.X, r.Y, r.Width, r.Height);

    // A 1600 × 900 "video call": the astronaut's head and shoulders scaled so the face is facePixels wide, on a dark frame with a name label.
    internal static (BitmapSource Frame, Rect Face) VideoCall(BitmapSource photo, double facePixels)
    {
        var crop = new CroppedBitmap(photo, new Int32Rect(120, 10, 210, 220));
        double scale = facePixels / AstronautFace.Width;
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(28, 30, 36)), null, new Rect(0, 0, 1600, 900));
            drawing.DrawImage(crop, new Rect(600, 120, 210 * scale, 220 * scale));
            drawing.DrawText(new FormattedText("Meeting · 12:04", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 18, Brushes.White, 1), new Point(24, 860));
        }
        var frame = new RenderTargetBitmap(1600, 900, 96, 96, PixelFormats.Pbgra32); frame.Render(visual); frame.Freeze();
        return (frame, new Rect(600 + (AstronautFace.X - 120) * scale, 120 + (AstronautFace.Y - 10) * scale, AstronautFace.Width * scale, AstronautFace.Height * scale));
    }

    internal static BitmapSource TextScreenshot()
    {
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1280, 720));
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(32, 32, 40)), null, new Rect(0, 0, 1280, 44));
            for (int i = 0; i < 14; i++)
            {
                drawing.DrawEllipse(new SolidColorBrush(Color.FromRgb((byte)(60 + i * 9), 120, 180)), null, new Point(40, 80 + i * 44), 16, 16);
                drawing.DrawText(new FormattedText($"user{100 + i}@example.com · Invoice {40000 + i * 17} · {i + 1} Nov 2026", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 16, Brushes.Black, 1), new Point(70, 70 + i * 44));
            }
        }
        var image = new RenderTargetBitmap(1280, 720, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image;
    }

    // One fixed text finding: the analyzer must keep it and add faces after it.
    private static Task<OcrLayout> FakeRead(BitmapSource image, string language, CancellationToken token) =>
        Task.FromResult(new OcrLayout(image.PixelWidth, image.PixelHeight, [new OcrWordBox("someone@example.com", new Rect(20, 20, 220, 22), 0)]));

    private static string[] Snapshot(string root) => Directory.Exists(root)
        ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(f => f + "|" + new FileInfo(f).Length + "|" + File.GetLastWriteTimeUtc(f).Ticks).OrderBy(x => x).ToArray()
        : [];

    public static async Task RunAsync()
    {
        var pack = ModelPacks.Catalog.SingleOrDefault(p => p.Id == "faces") ?? throw new Exception("No faces pack in the catalog");
        Check(pack.Files.Count() == 1 && pack.Files[0].Bytes == 229738 && pack.LicenseFile == "YuNet-MIT.txt", "Faces pack entry");
        Check(ModelPacks.LicenseText(pack).Contains("MIT License"), "YuNet licence text missing");
        Check(ScreenshotEditorView.CategoryLabel("face") == "Faces", "Face category label");

        string root = Path.GetFullPath("faces-" + Guid.NewGuid().ToString("N"));
        var server = new FakeModelServer();
        if (File.Exists(DevModel)) server.Serve(pack.Files[0].Url, File.ReadAllBytes(DevModel));
        var text = TextScreenshot();
        try
        {
            using var _ = ModelPacks.UseForTests([pack], root, server);
            // Missing pack: only the text finding, nothing downloaded, the editor offers the optional face finder.
            Check(FaceDetector.State == ModelPackState.Missing, "Faces pack reported present in an empty folder");
            var withoutPack = await SensitiveDataAnalyzer.AnalyzeAsync(text, "en", ["email"], CancellationToken.None, FakeRead);
            Check(withoutPack.Count == 1 && withoutPack[0].Categories.Contains("email"), "Without the pack: " + string.Join(", ", withoutPack.Select(f => string.Join("+", f.Categories))));
            Check(server.Requests.Count == 0, "A check downloaded something");
            await EditorRowAsync("Also suggest faces with the optional face finder (229 KB).", true, "Download");

            if (!File.Exists(DevModel) || !File.Exists(Astronaut))
            {
                Console.WriteLine("**Not tested**: YuNet detection, analyzer faces and damage (developer model or astronaut photo not found under E:\\DesktopTools-ai-spikes)");
                return;
            }
            ModelPacks.Consents().Accept(pack, ModelPacks.LicenseText(pack), DateTimeOffset.Now, "test");
            await ModelPacks.InstallAsync(pack, ModelPacks.Consents(), null, CancellationToken.None);
            Check(FaceDetector.State == ModelPackState.Installed, "Faces pack did not install");
            await EditorRowAsync("Faces are suggested too.", false, null);

            var photo = new BitmapImage(); photo.BeginInit(); photo.CacheOption = BitmapCacheOption.OnLoad; photo.UriSource = new Uri(Astronaut); photo.EndInit(); photo.Freeze();
            var before = Snapshot(root);
            foreach (double size in new[] { 60d, 200d })
            {
                var (frame, face) = VideoCall(photo, size);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var found = await FaceDetector.DetectAsync(frame, CancellationToken.None);
                Console.WriteLine($"Faces: {size} px face in 1600 × 900 → {found.Count} found in {watch.ElapsedMilliseconds} ms");
                Check(found.Any(r => Overlap(ToRect(r), face) >= .4), $"{size} px face not found: {string.Join(", ", found)} (expected {face})");
                Check(found.All(r => r.X >= 0 && r.Y >= 0 && r.X + r.Width <= 1600 && r.Y + r.Height <= 900), "A face box lies outside the image");
            }
            Check((await FaceDetector.DetectAsync(text, CancellationToken.None)).Count == 0, "Faces found in a text-only screenshot");
            // A large image is scaled down for the model and the boxes are scaled back.
            var (call, callFace) = VideoCall(photo, 200);
            var large = new TransformedBitmap(call, new ScaleTransform(2.4, 2.4)); large.Freeze();
            var largeFound = await FaceDetector.DetectAsync(large, CancellationToken.None);
            var scaledFace = new Rect(callFace.X * 2.4, callFace.Y * 2.4, callFace.Width * 2.4, callFace.Height * 2.4);
            Check(largeFound.Any(r => Overlap(ToRect(r), scaledFace) >= .4), $"Face in a 3840 × 2160 image not found at full-size coordinates: {string.Join(", ", largeFound)}");

            var (analysed, analysedFace) = VideoCall(photo, 200);
            var findings = await SensitiveDataAnalyzer.AnalyzeAsync(analysed, "en", ["email"], CancellationToken.None, FakeRead);
            Check(findings.Count(f => f.Categories.Contains("email")) == 1 && findings.Count(f => f.Categories.SequenceEqual(["face"])) >= 1, "Analyzer findings: " + string.Join(", ", findings.Select(f => string.Join("+", f.Categories))));
            Check(findings.Where(f => f.Categories.Contains("face")).Any(f => Overlap(ToRect(f.Bounds), analysedFace) >= .4) && findings.Where(f => f.Categories.Contains("face")).All(f => f.MaskedExcerpt == ""), "Face finding position or excerpt");
            Check(Snapshot(root).SequenceEqual(before), "A check wrote under the models folder");
            Check(!FaceDetector.LastCheckSkipped, "A finished face check reported itself skipped");

            // Another model busy for long (a long translation): the check does not wait for it; it returns the text findings and says faces were not checked.
            string u2netp = Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "u2netp.onnx");
            string u2Sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(u2netp)));
            using var holding = new ManualResetEventSlim();
            var hold = AiRuntime.RunAsync(u2netp, u2Sha, _ => { holding.Set(); Thread.Sleep(8000); return 0; }, CancellationToken.None);
            holding.Wait(TimeSpan.FromSeconds(20));
            var busyWatch = System.Diagnostics.Stopwatch.StartNew();
            var busy = await SensitiveDataAnalyzer.AnalyzeAsync(analysed, "en", ["email"], CancellationToken.None, FakeRead);
            Console.WriteLine($"Faces: check while another model is busy returned in {busyWatch.ElapsedMilliseconds} ms");
            Check(busyWatch.ElapsedMilliseconds < 6000, $"The face check waited {busyWatch.ElapsedMilliseconds} ms for a busy model");
            Check(busy.Count == 1 && busy[0].Categories.Contains("email"), "Busy findings: " + string.Join(", ", busy.Select(f => string.Join("+", f.Categories))));
            Check(FaceDetector.LastCheckSkipped && FaceDetector.State == ModelPackState.Installed, "A busy runtime marked the pack or was not reported");
            await hold;
            await EditorRowAsync("Faces were not checked this time. Check the screenshot again.", false, null, afterCheck: true);

            // Damaged after install: the check still returns the text finding, adds no faces, and the pack is marked damaged.
            AiRuntime.ReleaseAll();
            string installed = Path.Combine(root, pack.Id, pack.Files[0].File);
            using (var file = new FileStream(installed, FileMode.Open, FileAccess.ReadWrite)) { file.Position = file.Length / 2; int b = file.ReadByte(); file.Position = file.Length / 2; file.WriteByte((byte)(b ^ 0xFF)); }
            var damaged = await SensitiveDataAnalyzer.AnalyzeAsync(analysed, "en", ["email"], CancellationToken.None, FakeRead);
            Check(damaged.Count == 1 && damaged[0].Categories.Contains("email"), "Damaged pack findings: " + string.Join(", ", damaged.Select(f => string.Join("+", f.Categories))));
            Check(FaceDetector.State == ModelPackState.Damaged, "The damaged faces pack was not marked");
            await EditorRowAsync("Faces were not checked: the face finder is damaged. Download it again.", true, "Download again");
        }
        finally
        {
            AiRuntime.ReleaseAll();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        await LoadFailureAsync(pack, text);
    }

    /// <summary>A file that passes its checksum but does not load is not marked damaged; the text findings stay.</summary>
    private static async Task LoadFailureAsync(ModelPack pack, BitmapSource text)
    {
        byte[] junk = System.Text.Encoding.ASCII.GetBytes("not an ONNX model");
        var junkPack = pack with { Files = [pack.Files[0] with { Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(junk)).ToLowerInvariant(), Bytes = junk.Length }] };
        string root = Path.GetFullPath("faces-junk-" + Guid.NewGuid().ToString("N"));
        var server = new FakeModelServer(); server.Serve(junkPack.Files[0].Url, junk);
        try
        {
            using var _ = ModelPacks.UseForTests([junkPack], root, server);
            ModelPacks.Consents().Accept(junkPack, ModelPacks.LicenseText(junkPack), DateTimeOffset.Now, "test");
            await ModelPacks.InstallAsync(junkPack, ModelPacks.Consents(), null, CancellationToken.None);
            var findings = await SensitiveDataAnalyzer.AnalyzeAsync(text, "en", ["email"], CancellationToken.None, FakeRead);
            Check(findings.Count == 1 && findings[0].Categories.Contains("email"), "Load-failure findings: " + string.Join(", ", findings.Select(f => string.Join("+", f.Categories))));
            Check(FaceDetector.State == ModelPackState.Installed, "A load failure marked the faces pack damaged");
            Check(FaceDetector.LastCheckSkipped, "A load failure was not reported as an unchecked face pass");
        }
        finally
        {
            AiRuntime.ReleaseAll();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // The Hide data pane's face row in a fresh editor.
    private static async Task EditorRowAsync(string expected, bool buttonVisible, string? buttonText, bool afterCheck = false)
    {
        var image = TextScreenshot();
        var view = new ScreenshotEditorView(image, _ => { }, _ => { });
        var window = new Window { Content = view, Width = 1200, Height = 800, Left = -4000, ShowActivated = false };
        try
        {
            window.Show(); await Task.Delay(50);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var status = (TextBlock)typeof(ScreenshotEditorView).GetField("_faceStatus", flags)!.GetValue(view)!;
            var button = (Button)typeof(ScreenshotEditorView).GetField("_faceDownload", flags)!.GetValue(view)!;
            if (afterCheck) typeof(ScreenshotEditorView).GetMethod("RefreshFaceRow", flags)!.Invoke(view, [true]);
            Check(status.Text == expected, $"Face row: '{status.Text}', expected '{expected}'");
            Check((button.Visibility == Visibility.Visible) == buttonVisible, "Face download button visibility");
            if (buttonText != null) Check(System.Windows.Automation.AutomationProperties.GetName(button) == buttonText, "Face download button: " + System.Windows.Automation.AutomationProperties.GetName(button));
        }
        finally { window.Close(); view.Dispose(); }
    }
}
