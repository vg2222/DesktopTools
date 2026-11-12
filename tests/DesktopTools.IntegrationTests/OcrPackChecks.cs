using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Localization;

/// <summary>
/// The optional OCR pack (spec section 8): fallback to Windows OCR when a pack is missing or damaged, the engine's choices, and, when a developer
/// has the checksum-verified model files (OCR_PACK_SOURCE, default E:\DesktopTools-ai-spikes\models\ocr), reading generated pictures with them.
/// No network: downloads are refused by the handler.
/// </summary>
internal static class OcrPackChecks
{
    internal static string Source => Environment.GetEnvironmentVariable("OCR_PACK_SOURCE") ?? @"E:\DesktopTools-ai-spikes\models\ocr";

    private sealed class Refuse : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new InvalidOperationException("Network used in a test");
    }
    internal static HttpMessageHandler NoNetwork() => new Refuse();

    /// <summary>Copies the developer's model files into <paramref name="root"/> as installed packs after checking each against the catalog. False when any file is absent.</summary>
    internal static bool InstallDevPacks(string root)
    {
        var map = new (string Pack, string File, string Source)[]
        {
            (OcrPackPipeline.DetectorPackId, "inference.onnx", "det.onnx"),
            ("ocr-latin", "inference.onnx", "latin.onnx"), ("ocr-latin", "inference.yml", "latin.yml"),
            ("ocr-cyrillic", "inference.onnx", "eslav.onnx"), ("ocr-cyrillic", "inference.yml", "eslav.yml"),
            ("ocr-chinese-japanese", "inference.onnx", "cjk.onnx"), ("ocr-chinese-japanese", "inference.yml", "cjk.yml"),
        };
        if (map.Any(item => !File.Exists(Path.Combine(Source, item.Source)))) return false;
        foreach (var (packId, file, source) in map)
        {
            var entry = ModelPacks.Catalog.Single(pack => pack.Id == packId).Files.Single(f => f.File == file);
            string from = Path.Combine(Source, source);
            using (var stream = File.OpenRead(from))
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"{from} does not match the catalog checksum for {packId}/{file}");
            Directory.CreateDirectory(Path.Combine(root, packId));
            File.Copy(from, Path.Combine(root, packId, file), true);
        }
        return true;
    }

    internal static BitmapSource Render(IReadOnlyList<string> lines, double size = 18, int width = 760, string font = "Segoe UI", double spacing = 2.2)
    {
        int height = (int)(lines.Count * size * spacing + size * 2);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            for (int i = 0; i < lines.Count; i++)
                context.DrawText(new FormattedText(lines[i], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(font), size, Brushes.Black, 1), new Point(18, size + i * size * spacing));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        return bitmap;
    }

    private static string[] OutputLines(string text) => text.Split('\n').Select(line => string.Join(" ", line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))).Where(line => line.Length > 0).ToArray();

    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject item) foreach (var nested in Tree(item)) yield return nested;
    }

    /// <summary>Text tools offers the engine choice; the pack lists its alphabets and offers Download while the chosen one is missing.</summary>
    private static async Task TextToolsAsync(bool installed)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        using var controller = new AppController(true);
        controller.UpdateSettings(s => { s.OcrEngine = "Windows"; s.OcrPackScript = ""; s.ScreenTextLanguage = "en-US"; });
        var tools = new TextToolsWindow(controller);
        tools.Show(); tools.ShowMode(true);
        try
        {
            var engine = Tree(tools).OfType<ComboBox>().Single(c => Equals(c.Tag, "ocr-engine"));
            var download = Tree(tools).OfType<Button>().Single(b => Equals(b.Tag, "ocr-pack-download"));
            var language = (ComboBox)typeof(TextToolsWindow).GetField("ocrLanguage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(tools)!;
            Check(engine.SelectedIndex == 0 && controller.Settings.OcrEngine == "Windows", "Windows OCR is not the default engine");
            Check(download.Visibility != Visibility.Visible, "Download shown for Windows OCR");
            Check(language.Items.Cast<OcrLanguage>().All(l => !l.Tag.StartsWith("pack:", StringComparison.Ordinal)), "Windows engine lists pack groups");
            engine.SelectedIndex = 1;
            await Task.Delay(100);
            Check(controller.Settings.OcrEngine == "Pack", "Choosing the pack did not save the engine");
            Check(language.Items.Cast<OcrLanguage>().Select(l => l.Tag).SequenceEqual(["pack:latin", "pack:cyrillic", "pack:chinese-japanese"]), "Pack groups not listed");
            Check(((OcrLanguage)language.SelectedItem).Tag == "pack:latin", "Latin not preselected for an English language");
            if (installed)
                Check(download.Visibility != Visibility.Visible && tools.OcrPackStatusText == L.T("OCR pack ready. Text is read on this PC."), "Installed pack still offers Download: " + tools.OcrPackStatusText);
            else
                Check(download.Visibility == Visibility.Visible && tools.OcrPackStatusText.Contains("Windows OCR", StringComparison.Ordinal), "Missing pack: no Download offer: " + tools.OcrPackStatusText);
            language.SelectedIndex = 1;
            await Task.Delay(100);
            Check(controller.Settings.OcrPackScript == "cyrillic" && controller.Settings.ScreenTextLanguage == "en-US", "Choosing a group changed the wrong setting");
            // A change made elsewhere (settings import, reset) is shown: the reader and its language list both follow.
            controller.UpdateSettings(s => s.OcrEngine = "Windows");
            await Task.Delay(100);
            Check(engine.SelectedIndex == 0, "Text reader still shows the pack after an outside change");
            Check(language.Items.Cast<OcrLanguage>().All(l => !l.Tag.StartsWith("pack:", StringComparison.Ordinal)), "Language list did not follow an outside change");
        }
        finally
        {
            tools.Close();
            controller.UpdateSettings(s => { s.OcrEngine = "Windows"; s.OcrPackScript = ""; });
        }
    }

    /// <summary>Manual check: the real Text tools window with the shipped catalog, a real download into an empty
    /// folder (OCR_PACK_PREVIEW_ROOT, default under %TEMP%) and isolated smoke settings. Placed at OCR_PACK_PREVIEW_AT ("x,y").
    /// Closes after the window is closed or 20 minutes.</summary>
    internal static async Task PreviewAsync()
    {
        string root = Environment.GetEnvironmentVariable("OCR_PACK_PREVIEW_ROOT") is { Length: > 0 } r ? r : Path.Combine(Path.GetTempPath(), "dt-ocr-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var packs = ModelPacks.Catalog;
        using var handler = new System.Net.Http.HttpClientHandler();
        using var scope = ModelPacks.UseForTests(packs, root, handler);
        using var controller = new AppController(true);
        controller.UpdateSettings(s => { s.OcrEngine = "Windows"; s.OcrPackScript = ""; });
        var tools = new TextToolsWindow(controller) { WindowStartupLocation = WindowStartupLocation.Manual };
        if (Environment.GetEnvironmentVariable("OCR_PACK_PREVIEW_AT")?.Split(',') is [var x, var y]) { tools.Left = double.Parse(x, System.Globalization.CultureInfo.InvariantCulture); tools.Top = double.Parse(y, System.Globalization.CultureInfo.InvariantCulture); }
        var completion = new TaskCompletionSource();
        tools.Closed += (_, _) => completion.TrySetResult();
        tools.Show(); tools.ShowMode(true);
        Console.WriteLine("Preview models folder: " + root);
        try { await completion.Task.WaitAsync(TimeSpan.FromMinutes(20)); }
        finally { controller.UpdateSettings(s => { s.OcrEngine = "Windows"; s.OcrPackScript = ""; }); AiRuntime.ReleaseAll(); }
    }

    internal static async Task RunAsync(Action<string> report)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        string root = Path.GetFullPath("ocr-pack-" + Guid.NewGuid().ToString("N"));
        var previous = LocalOcr.Preference;
        string[] english = ["Account settings", "Download the report", "Version 2.4.1 build 5821"];
        var image = Render(english);
        try
        {
            Directory.CreateDirectory(root);
            using (ModelPacks.UseForTests(ModelPacks.Catalog, root, NoNetwork()))
            {
                // Missing pack: Windows OCR reads the picture and the notice says the pack is not downloaded.
                LocalOcr.Preference = ("Pack", "latin", "en-US");
                var missing = await LocalOcr.ReadWordsAsync(image, OcrPackPipeline.Tag(OcrPackScript.Latin));
                string latinName = LocalOcr.ScriptName(OcrPackScript.Latin);
                Check(missing.Notice == L.F($"The OCR pack for {latinName} is not downloaded. The text was read with Windows OCR."), "Missing-pack notice: " + missing.Notice);
                Check(missing.Layout.Words.Any(w => w.Text.Contains("Account", StringComparison.Ordinal)), "Fallback did not read the picture");
                var missingText = await LocalOcr.ReadTextAsync(image, OcrPackPipeline.Tag(OcrPackScript.Latin));
                Check(missingText.Notice != null && missingText.Text.Contains("Download the report", StringComparison.Ordinal), "Text fallback: " + missingText.Text);
                var windows = await LocalOcr.ReadWordsAsync(image, "en-US");
                Check(windows.Notice == null && windows.Layout.Words.Count > 0, "A Windows tag gave a notice");

                // Choices: the pack lists its three groups, marked when not downloaded; Windows lists Windows languages.
                var choices = LocalOcr.Choices();
                Check(choices.Select(c => c.Tag).SequenceEqual(["pack:latin", "pack:cyrillic", "pack:chinese-japanese"]), "Pack choices: " + string.Join(", ", choices.Select(c => c.Tag)));
                Check(choices.Select(c => c.Name).SequenceEqual(Enum.GetValues<OcrPackScript>().Select(x => L.F($"{LocalOcr.ScriptName(x)} (not downloaded)"))), "Missing groups are not marked: " + string.Join(", ", choices.Select(c => c.Name)));
                Check(LocalOcr.SelectChoice(choices, null)?.Tag == "pack:latin", "Saved script group not preselected");
                LocalOcr.Preference = ("Pack", "", "ru-RU");
                Check(LocalOcr.SelectChoice(choices, null)?.Tag == "pack:cyrillic", "Script group does not follow the Windows language");
                Check(LocalOcr.DefaultTag() == "pack:cyrillic", "Library tag with the pack engine");
                LocalOcr.Preference = ("Windows", "", "en-US");
                Check(LocalOcr.Choices().All(c => !c.Tag.StartsWith("pack:", StringComparison.Ordinal)), "Windows engine lists pack groups");
                Check(LocalOcr.DefaultTag() != null && !LocalOcr.DefaultTag()!.StartsWith("pack:", StringComparison.Ordinal), "Library tag with Windows OCR");
                LocalOcr.Preference = ("Pack", "latin", "en-US");

                // Damaged pack: files of the right size with wrong bytes are caught at load, marked damaged, and Windows OCR reads instead.
                foreach (var id in new[] { OcrPackPipeline.DetectorPackId, "ocr-latin" })
                {
                    var pack = ModelPacks.Catalog.Single(p => p.Id == id);
                    Directory.CreateDirectory(Path.Combine(root, id));
                    foreach (var file in pack.Files) File.WriteAllBytes(Path.Combine(root, id, file.File), new byte[file.Bytes]);
                }
                Check(OcrPackEngine.IsInstalled(OcrPackScript.Latin), "Size-only state should look installed before the hash check");
                var damaged = await LocalOcr.ReadWordsAsync(image, "pack:latin");
                Check(damaged.Notice == L.T("The OCR pack is damaged. Download it again on the AI models page. The text was read with Windows OCR."), "Damaged-pack notice: " + damaged.Notice);
                Check(damaged.Layout.Words.Count > 0, "Damaged fallback read nothing");
                Check(OcrPackEngine.StateOf(OcrPackScript.Latin) == ModelPackState.Damaged, "Damaged pack was not marked");
                foreach (var id in new[] { OcrPackPipeline.DetectorPackId, "ocr-latin" }) Directory.Delete(Path.Combine(root, id), true);
                await TextToolsAsync(installed: false);
            }

            // Real models, when a developer has them.
            string realRoot = Path.Combine(root, "real");
            if (!InstallDevPacks(realRoot)) { report($"NOT TESTED OCR pack with real model files: not found in {Source}"); return; }
            using (ModelPacks.UseForTests(ModelPacks.Catalog, realRoot, NoNetwork()))
            {
                LocalOcr.Preference = ("Pack", "latin", "en-US");
                var reading = await LocalOcr.ReadWordsAsync(image, "pack:latin");
                Check(reading.Notice == null, "Installed pack gave a notice: " + reading.Notice);
                Check(reading.Layout.Words.All(w => w.Bounds.Left >= 0 && w.Bounds.Top >= 0 && w.Bounds.Right <= image.PixelWidth && w.Bounds.Bottom <= image.PixelHeight), "Word box outside the picture");
                var text = OutputLines((await LocalOcr.ReadTextAsync(image, "pack:latin")).Text);
                foreach (var line in english) Check(text.Contains(line), $"'{line}' not read exactly: {string.Join(" | ", text)}");

                string[] russian = ["Сохранить изменения", "Новые сообщения", "Забыли пароль?"];
                var cyrillic = OutputLines((await LocalOcr.ReadTextAsync(Render(russian), "pack:cyrillic")).Text);
                foreach (var line in russian) Check(cyrillic.Contains(line), $"'{line}' not read exactly: {string.Join(" | ", cyrillic)}");

                // A tall picture is read in strips; every line, including those on strip seams, comes back once.
                var tallLines = Enumerable.Range(1, 60).Select(i => $"Row {i} settings").ToArray();
                var tall = Render(tallLines, 20, 1200, spacing: 2.4);
                Check(tall.PixelHeight > 2500, "Tall picture is not tall: " + tall.PixelHeight);
                var tallText = OutputLines((await LocalOcr.ReadTextAsync(tall, "pack:latin")).Text);
                foreach (var line in tallLines) Check(tallText.Count(l => l == line) == 1, $"'{line}' read {tallText.Count(l => l == line)} times");

                // Chinese and Japanese: smoke reading only; accuracy is Not tested.
                var cjk = (await LocalOcr.ReadTextAsync(Render(["设置 下载", "設定を保存"], 22, font: "Microsoft YaHei"), "pack:chinese-japanese")).Text;
                report("MEASURE OCR pack Chinese/Japanese smoke reading: " + string.Join(" | ", OutputLines(cjk)));

                // The text view reads with the pack (its session set is what is loaded afterwards) and the words can be copied.
                AiRuntime.ReleaseAll();
                var view = new TextSelectionView(image, _ => { }, null, null, "pack:latin");
                var window = new Window { Content = view, Width = 900, Height = 600, WindowStyle = WindowStyle.None, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = 40, Top = 40 };
                window.Show();
                try
                {
                    await view.RecognizeAsync();
                    Check(view.Layout != null && english.All(line => view.AllText.Contains(line, StringComparison.Ordinal)), "Text view with the pack: " + view.AllText);
                    Check(!view.StatusText.Contains("Windows OCR", StringComparison.Ordinal), "Text view fell back: " + view.StatusText);
                    Check(AiRuntime.LoadedModelPaths.Any(path => path.Contains(OcrPackPipeline.DetectorPackId, StringComparison.Ordinal)), "Text view did not read with the pack");
                }
                finally { window.Close(); }
                await TextToolsAsync(installed: true);
                AiRuntime.ReleaseAll();

                // Over the pixel limit with both sides under 8192: the message asks to crop, not to fit a side limit already met.
                var huge = BitmapSource.Create(7000, 7000, 96, 96, PixelFormats.Gray8, null, new byte[7000 * 7000], 7000);
                string? hugeError = null;
                try { await OcrPackEngine.RecognizeWordsAsync(huge, OcrPackScript.Latin, CancellationToken.None); }
                catch (InvalidOperationException e) { hugeError = e.Message; }
                Check(hugeError == L.T("Too much text in this picture. Crop it and try again."), "Pixel-limit message: " + hugeError);

                // A pack removed while a reading waits for the model runtime: the reading falls back as "not downloaded",
                // with no raw file error, and the pack that is still there is not marked damaged.
                var detectorPack = ModelPacks.Catalog.Single(p => p.Id == OcrPackPipeline.DetectorPackId);
                var latinPack = ModelPacks.Catalog.Single(p => p.Id == "ocr-latin");
                string blockerModel = Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "u2netp.onnx");
                using (var hold = new ManualResetEventSlim(false))
                using (var holding = new ManualResetEventSlim(false))
                {
                    var blocker = AiRuntime.RunAsync(blockerModel, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(blockerModel))), _ => { holding.Set(); hold.Wait(); return 0; }, CancellationToken.None);
                    Check(holding.Wait(TimeSpan.FromSeconds(30)), "Blocking model did not start");
                    var waiting = LocalOcr.ReadWordsAsync(image, "pack:latin");
                    await Task.Delay(500);
                    Check(!waiting.IsCompleted, "Reading did not wait for the runtime");
                    ModelPacks.Remove(detectorPack, ModelPacks.Consents(realRoot), realRoot);
                    hold.Set(); await blocker;
                    var removed = await waiting;
                    Check(removed.Notice == L.F($"The OCR pack for {LocalOcr.ScriptName(OcrPackScript.Latin)} is not downloaded. The text was read with Windows OCR."), "Removed-while-waiting notice: " + removed.Notice);
                    Check(ModelPacks.StateOf(latinPack, realRoot) == ModelPackState.Installed, "Removing the text finder marked the Latin pack " + ModelPacks.StateOf(latinPack, realRoot));
                }

                // Only the detector has wrong bytes: the detector is marked damaged, the recognizer is not.
                AiRuntime.ReleaseAll();
                Check(InstallDevPacks(realRoot), "Could not reinstall the dev packs");
                var detectorFile = Path.Combine(realRoot, detectorPack.Id, detectorPack.Files.Single().File);
                File.WriteAllBytes(detectorFile, new byte[new FileInfo(detectorFile).Length]);
                var detectorDamaged = await LocalOcr.ReadWordsAsync(image, "pack:latin");
                Check(detectorDamaged.Notice == L.T("The OCR pack is damaged. Download it again on the AI models page. The text was read with Windows OCR."), "Detector-damaged notice: " + detectorDamaged.Notice);
                Check(ModelPacks.StateOf(detectorPack, realRoot) == ModelPackState.Damaged && ModelPacks.StateOf(latinPack, realRoot) == ModelPackState.Installed,
                    $"Damage marked on the wrong pack: detector {ModelPacks.StateOf(detectorPack, realRoot)}, Latin {ModelPacks.StateOf(latinPack, realRoot)}");
            }
        }
        finally
        {
            LocalOcr.Preference = previous;
            AiRuntime.ReleaseAll();
            try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
