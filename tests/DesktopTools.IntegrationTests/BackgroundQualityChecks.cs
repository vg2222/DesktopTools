using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopTools.Core;
using DesktopTools.Extras;

/// <summary>
/// The optional high-quality background model (spec section 9): the bundled model is unchanged, the pack is asked for and downloaded
/// through the licence window, runs through AiRuntime, refuses without enough memory, and a damaged file marks the pack damaged.
/// The real-model part needs the developer's checksum-verified file under E:\DesktopTools-ai-spikes; without it that part is Not tested.
/// </summary>
internal static class BackgroundQualityChecks
{
    internal const string DevModel = @"E:\DesktopTools-ai-spikes\models\bg\BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx";
    // SHA-256 of the BGRA pixels the bundled model produced for the fixture before this update.
    private const string StandardFixtureHash = "CB1494E5E059A1CA6F632A9CCD1FB045FBCE2E9CAB23D2C2119E7AF26BC40977";
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static byte[] Pixels(BitmapSource image) { var bytes = new byte[image.PixelWidth * image.PixelHeight * 4]; new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0).CopyPixels(bytes, image.PixelWidth * 4, 0); return bytes; }

    internal static BitmapSource Fixture()
    {
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 400, 300));
            drawing.DrawEllipse(Brushes.ForestGreen, null, new Point(200, 145), 75, 100);
            drawing.DrawRectangle(Brushes.SaddleBrown, null, new Rect(185, 205, 30, 65));
        }
        var fixture = new RenderTargetBitmap(400, 300, 96, 96, PixelFormats.Pbgra32); fixture.Render(visual); fixture.Freeze();
        return fixture;
    }

    // Closes the licence window as soon as it opens, as a user choosing Cancel would.
    private static void DeclineNextConsent()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.GetType().Name == "ModelConsentWindow" && w.IsVisible);
            if (dialog == null) return;
            timer.Stop(); dialog.Close();
        };
        timer.Start();
    }

    /// <summary>The touch-up brush after a Standard removal: a restore stroke brings back removed pixels as one undo step, erase removes,
    /// Compare keeps working, and any other edit turns the brush off.</summary>
    private static async Task TouchUpAsync(BitmapSource fixture, byte[] before)
    {
        string model = "Standard";
        var window = new ImageToolsWindow(_ => { }, getBackgroundModel: () => model, setBackgroundModel: value => model = value);
        try
        {
            window.GetType().GetMethod("SetImage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [fixture]);
            window.Show(); await Task.Delay(50);
            Check(!window.CanTouchUp, "The brush is available before a removal");
            await window.RemoveBackgroundAsync();
            var removed = Field<BitmapSource>(window, "bitmap");
            Check(!ReferenceEquals(removed, fixture) && window.CanTouchUp, "No touch-up after removal: " + Field<TextBlock>(window, "status").Text);
            Check(Field<FrameworkElement>(window, "touchUpPanel").Visibility == Visibility.Visible, "The Touch up controls are hidden after a removal");
            Check(Field<Slider>(window, "brushSize").Value == 24 && Field<Slider>(window, "brushSize").Minimum == 4 && Field<Slider>(window, "brushSize").Maximum == 200, "Brush size range");
            var removedPixels = Pixels(removed);
            int Index(int x, int y) => (y * 400 + x) * 4;
            Check(removedPixels[Index(6, 6) + 3] < 32, "Fixture corner was not removed");

            // Restore along the top-left corner: alpha back to 255 with the original colour; far pixels untouched; one undo step.
            // A stroke keeps the zoom the user set to work on edges.
            var zoom = Field<Slider>(window, "previewZoom"); zoom.Value = 3;
            window.PaintStroke(true, new Point(6, 6), new Point(30, 6));
            Check(Math.Abs(zoom.Value - 3) < .001, $"A brush stroke reset the zoom to {zoom.Value}");
            var restored = Field<BitmapSource>(window, "bitmap"); var restoredPixels = Pixels(restored);
            Check(restoredPixels[Index(6, 6) + 3] == 255 && restoredPixels[Index(30, 6) + 3] == 255, $"Restore did not bring back the corner: {restoredPixels[Index(6, 6) + 3]}");
            Check(restoredPixels.AsSpan(Index(6, 6), 3).SequenceEqual(before.AsSpan(Index(6, 6), 3)), "Restore changed the colour");
            Check(restoredPixels.AsSpan(Index(0, 100)).SequenceEqual(removedPixels.AsSpan(Index(0, 100))), "Restore touched pixels far from the stroke");
            Check(window.CanTouchUp && Field<CheckBox>(window, "compare").IsEnabled, "Brush or Compare unavailable on a touch-up");
            Field<Button>(window, "undoButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(ReferenceEquals(Field<BitmapSource>(window, "bitmap"), removed) && window.CanTouchUp, "Undo did not return the removal result in one step");
            Check(Math.Abs(zoom.Value - 3) < .001, $"Undoing a stroke reset the zoom to {zoom.Value}");
            Field<Button>(window, "redoButton").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(ReferenceEquals(Field<BitmapSource>(window, "bitmap"), restored) && window.CanTouchUp, "Redo did not return the touch-up");

            // Erase in the subject's centre.
            window.PaintStroke(false, new Point(200, 145));
            Check(Pixels(Field<BitmapSource>(window, "bitmap"))[Index(200, 145) + 3] == 0, "Erase did not clear the centre");

            // Any other edit (a resize) turns the brush off; undoing back to the touch-up does not bring back a dropped mask.
            Field<TextBox>(window, "width").Text = "200";
            window.GetType().GetMethod("ResizeImage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Check(Field<BitmapSource>(window, "bitmap").PixelWidth == 200 && !window.CanTouchUp, "A resize kept the brush");
            Check(Field<FrameworkElement>(window, "touchUpPanel").Visibility != Visibility.Visible, "Touch up controls shown after a resize");
            Check(Pixels(fixture).SequenceEqual(before), "The original pixels changed");
        }
        finally { window.Close(); }
    }

    /// <summary>A file that passes its checksum but does not load is not "damaged": downloading it again would not help.</summary>
    private static async Task LoadFailureAsync(BitmapSource fixture)
    {
        var pack = ModelPacks.Catalog.Single(p => p.Id == "background-quality");
        byte[] junk = System.Text.Encoding.ASCII.GetBytes("not an ONNX model");
        var junkPack = pack with { Files = [pack.Files[0] with { Sha256 = Convert.ToHexString(SHA256.HashData(junk)).ToLowerInvariant(), Bytes = junk.Length }] };
        string root = Path.GetFullPath("background-junk-" + Guid.NewGuid().ToString("N"));
        var server = new FakeModelServer(); server.Serve(junkPack.Files[0].Url, junk);
        try
        {
            using var _ = ModelPacks.UseForTests([junkPack], root, server);
            BackgroundRemovalService.AvailableMemory = () => 64UL << 30;
            ModelPacks.Consents().Accept(junkPack, ModelPacks.LicenseText(junkPack), DateTimeOffset.Now, "test");
            await ModelPacks.InstallAsync(junkPack, ModelPacks.Consents(), null, CancellationToken.None);
            try { await BackgroundRemovalService.RemoveWithMaskAsync(fixture, BackgroundModel.Quality, default); throw new Exception("A model that does not load ran"); }
            catch (BackgroundQualityUnavailableException e) { throw new Exception("A load failure was reported as " + (e.Damaged ? "damage" : "missing") + ": " + e.Message); }
            catch (InvalidOperationException e) { Check(e.Message == "The high-quality model could not be loaded. Close other apps and try again, or choose Built-in.", "Load failure message: " + e.Message); }
            Check(BackgroundRemovalService.QualityState == ModelPackState.Installed, "A load failure marked the pack damaged");
        }
        finally
        {
            AiRuntime.ReleaseAll(); BackgroundRemovalService.AvailableMemory = null;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    public static async Task RunAsync()
    {
        var fields = (string[])typeof(DesktopTools.Native.ProfileStore).GetField("Fields", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Check(fields.Contains(nameof(AppSettings.BackgroundModel)), "The background model is not saved in profiles");

        var fixture = Fixture(); byte[] before = Pixels(fixture);
        // The bundled model gives exactly what it gave before this update, and the mask it reports rebuilds that image.
        var standard = await BackgroundRemovalService.RemoveWithMaskAsync(fixture, BackgroundModel.Standard, default);
        Check(Convert.ToHexString(SHA256.HashData(Pixels(standard.Image))) == StandardFixtureHash, "The bundled model's output changed");
        Check(Pixels(BackgroundRemovalService.ApplyMask(fixture, standard.Mask, standard.MaskWidth, standard.MaskHeight)).SequenceEqual(Pixels(standard.Image)), "The reported mask does not rebuild the result");
        Check(Pixels(await BackgroundRemovalService.RemoveAsync(fixture)).SequenceEqual(Pixels(standard.Image)), "RemoveAsync and the Standard model differ");

        await TouchUpAsync(fixture, before);
        await LoadFailureAsync(fixture);

        var pack = ModelPacks.Catalog.Single(p => p.Id == "background-quality");
        string root = Path.GetFullPath("background-quality-" + Guid.NewGuid().ToString("N"));
        var server = new FakeModelServer();
        server.Override = request => request.RequestUri!.ToString() == pack.Files[0].Url && File.Exists(DevModel)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(File.OpenRead(DevModel)) }
            : null;
        string model = "Quality";
        ImageToolsWindow? window = null;
        try
        {
            using var _ = ModelPacks.UseForTests([pack], root, server);
            Check(BackgroundRemovalService.QualityState == ModelPackState.Missing, "Quality pack reported present in an empty folder");
            try { await BackgroundRemovalService.RemoveWithMaskAsync(fixture, BackgroundModel.Quality, default); throw new Exception("Quality ran without its pack"); }
            catch (BackgroundQualityUnavailableException e) { Check(!e.Damaged, "A missing pack was reported damaged"); }

            // Image tools: Quality chosen, not downloaded, licence declined → nothing downloads, the image and the choice stay.
            window = new ImageToolsWindow(_ => { }, getBackgroundModel: () => model, setBackgroundModel: value => model = value);
            window.GetType().GetMethod("SetImage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [fixture]);
            window.Show(); await Task.Delay(50);
            var choice = Field<ComboBox>(window, "backgroundModel");
            Check(choice.SelectedIndex == 1, "The saved Quality choice is not shown");
            Check(Field<TextBlock>(window, "backgroundModelStatus").Text.Contains("224 MB"), "Status does not give the download size: " + Field<TextBlock>(window, "backgroundModelStatus").Text);
            DeclineNextConsent();
            await window.RemoveBackgroundAsync();
            Check(ReferenceEquals(Field<BitmapSource>(window, "bitmap"), fixture), "Declining the licence changed the image");
            Check(Field<TextBlock>(window, "status").Text == "Download canceled.", "Decline status: " + Field<TextBlock>(window, "status").Text);
            Check(model == "Quality" && server.Requests.Count == 0 && BackgroundRemovalService.QualityState == ModelPackState.Missing, "Declining downloaded or changed the choice");

            // Choosing Built-in saves the choice.
            choice.SelectedIndex = 0; Check(model == "Standard", "Built-in choice not saved");
            choice.SelectedIndex = 1; Check(model == "Quality", "Quality choice not saved");

            if (!File.Exists(DevModel))
            {
                Console.WriteLine("**Not tested**: BiRefNet-lite download, removal, reuse, memory refusal and damage (developer model file not found at " + DevModel + ")");
                return;
            }
            // Accepted: the download goes through the consent record and the server, then the removal runs on the new model.
            ModelPacks.Consents().Accept(pack, ModelPacks.LicenseText(pack), DateTimeOffset.Now, "test");
            var watch = Stopwatch.StartNew();
            await window.RemoveBackgroundAsync();
            Console.WriteLine($"Background quality: download from file + first removal {watch.Elapsed.TotalSeconds:F1} s");
            Check(BackgroundRemovalService.QualityState == ModelPackState.Installed, "The pack did not install: " + Field<TextBlock>(window, "status").Text);
            var shown = Field<BitmapSource>(window, "bitmap");
            Check(!ReferenceEquals(shown, fixture), "Quality removal did not change the working copy: " + Field<TextBlock>(window, "status").Text);
            Check(Pixels(fixture).SequenceEqual(before), "The original pixels changed");
            string installed = Path.Combine(root, pack.Id, pack.Files[0].File);
            Check(string.Equals(AiRuntime.LoadedModelPath, installed, StringComparison.OrdinalIgnoreCase), "AiRuntime does not hold the quality model: " + AiRuntime.LoadedModelPath);

            watch.Restart();
            var quality = await BackgroundRemovalService.RemoveWithMaskAsync(fixture, BackgroundModel.Quality, default);
            Console.WriteLine($"Background quality: second removal (session reused) {watch.Elapsed.TotalSeconds:F1} s");
            var alpha = Pixels(quality.Image);
            Check(quality.Image.PixelWidth == 400 && quality.Image.PixelHeight == 300, "Result size differs from the input");
            Check(alpha[3] < 32 && alpha[(145 * 400 + 200) * 4 + 3] > 223, $"Quality model failed the fixture (corner {alpha[3]}, centre {alpha[(145 * 400 + 200) * 4 + 3]})");
            Check(quality.MaskWidth == 1024 && quality.MaskHeight == 1024, "Quality mask size");

            // Not enough free memory: refused with the reason before the model runs; nothing marked.
            BackgroundRemovalService.AvailableMemory = () => 1UL << 30;
            try { await BackgroundRemovalService.RemoveWithMaskAsync(fixture, BackgroundModel.Quality, default); throw new Exception("Ran without enough memory"); }
            catch (InsufficientMemoryException e) { Check(e.Message.Contains("6 GB"), "Memory message: " + e.Message); }
            finally { BackgroundRemovalService.AvailableMemory = null; }
            Check(BackgroundRemovalService.QualityState == ModelPackState.Installed, "A memory refusal changed the pack");

            // A file damaged after install: the next load fails its checksum, the pack is marked damaged, Standard is not used silently.
            AiRuntime.ReleaseAll();
            using (var file = new FileStream(installed, FileMode.Open, FileAccess.ReadWrite)) { file.Position = file.Length / 2; int b = file.ReadByte(); file.Position = file.Length / 2; file.WriteByte((byte)(b ^ 0xFF)); }
            try { await BackgroundRemovalService.RemoveWithMaskAsync(fixture, BackgroundModel.Quality, default); throw new Exception("A damaged model ran"); }
            catch (BackgroundQualityUnavailableException e) { Check(e.Damaged, "Damage not reported as damage"); }
            Check(BackgroundRemovalService.QualityState == ModelPackState.Damaged, "The damaged pack was not marked");
            await Dispatcher.Yield();
            window.GetType().GetMethod("RefreshBackgroundModelStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Check(Field<TextBlock>(window, "backgroundModelStatus").Text == "The high-quality model is damaged. Download it again.", "Damaged status: " + Field<TextBlock>(window, "backgroundModelStatus").Text);
        }
        finally
        {
            window?.Close();
            AiRuntime.ReleaseAll();
            BackgroundRemovalService.AvailableMemory = null;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
