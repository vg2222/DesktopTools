using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Localization;
using DesktopTools.UI;

/// <summary>The AI models settings page against a fixture catalog and an in-memory server: no network.</summary>
internal static class AiModelsPageChecks
{
    internal static async Task RunAsync()
    {
        string root = Path.GetFullPath("ai-models-page-" + Guid.NewGuid().ToString("N"));
        string modelRoot = Path.Combine(root, "Models"), translationRoot = Path.Combine(root, "Translation");
        var (missing, server) = ModelPackChecks.Fixture("page-missing", 11);
        var (damaged, damagedServer) = ModelPackChecks.Fixture("page-damaged", 21);
        damaged = damaged with { Name = "Second fixture model", Purpose = "Second fixture purpose", LicenseId = "apache-2.0", LicenseFile = "Apache-2.0.txt" };
        ModelCatalog.Validate(damaged);
        Func<string, long> plenty = _ => long.MaxValue;
        var consents = ModelPacks.Consents(modelRoot);
        consents.Accept(damaged, ModelPacks.LicenseText(damaged), DateTimeOffset.Now, "test");
        await ModelPacks.InstallAsync(damaged, consents, null, default, modelRoot, damagedServer, plenty);
        ModelPacks.MarkDamaged(damaged, modelRoot);
        Check(ModelPacks.StateOf(damaged, modelRoot) == ModelPackState.Damaged, "Fixture pack is not damaged");

        // One downloaded translation pack (any catalog direction that is not bundled).
        string direction = TranslationPacks.Catalog.Select(p => p.Direction).First(d => d is not ("en-ru" or "ru-en"));
        Directory.CreateDirectory(Path.Combine(translationRoot, direction));
        foreach (string file in new[] { "encoder_model_quantized.onnx", "decoder_model_merged_quantized.onnx", "source.spm", "vocab.json", "config.json" })
            File.WriteAllText(Path.Combine(translationRoot, direction, file), "x");

        using var controller = new AppController(true);
        controller.UpdateSettings(s => { s.Animations = false; s.UseCustomBackground = false; s.ThemePreset = "Classic"; });
        string previousTheme = controller.Settings.Theme;
        controller.OpenMain(); L.Use("en");
        var main = (MainWindow)typeof(AppController).GetField("main", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        try
        {
            using (ModelPacks.UseForTests([missing, damaged], modelRoot, server, plenty))
            using (TranslationPacks.UseRootForTests(translationRoot))
            {
                Open(main);
                string text = PageText(main);
                foreach (string expected in new[] { L.T("AI models"), L.T("Optional models"), missing.Name, missing.Purpose, L.F($"Licence: {ModelPacks.LicenseName(missing.LicenseId)}"),
                    L.T("Not downloaded"), L.F($"Download size: {ModelPacks.FormatSize(missing.TotalBytes)}"), damaged.Name, L.F($"Licence: {ModelPacks.LicenseName(damaged.LicenseId)}"),
                    L.T("Damaged. Download it again to use it."), L.T("Translation packs") })
                    Check(text.Contains(expected, StringComparison.Ordinal), "Page does not show " + expected);
                Check(Tagged<Button>(main, "ai-model-download-page-missing").IsVisible, "Missing pack has no Download button");
                Check(ButtonText(Tagged<Button>(main, "ai-model-download-page-damaged")) == L.T("Download again"), "Damaged pack does not offer Download again");
                Check(Tagged<Button>(main, "ai-model-licence-page-missing").IsVisible, "No View licence button");
                Check(Tagged<Button>(main, "translation-remove-" + direction).IsVisible, "Downloaded translation pack has no Remove button");

                // Run on: Processor first and selected; Graphics card only where Windows has DirectML 1.15 or later.
                var runOn = Tagged<ComboBox>(main, "ai-run-on");
                Check(runOn.Items.Count == (AiGpu.IsSupported ? 2 : 1) && runOn.SelectedIndex == 0 && ItemText(runOn.Items[0]) == "Processor", "Run on must start with Processor and offer the graphics card only when supported");
                Check(controller.Settings.AiDevice == "Processor", "AiDevice changed");

                // The shortened warning sits under the list.
                var warning = Tagged<TextBlock>(main, "ai-models-warning");
                Check(warning.Text == "These models are not part of DesktopTools. They were not created, trained, or owned by the DesktopTools project, and DesktopTools is not affiliated with or endorsed by their publishers.", "Short warning differs from the plan: " + warning.Text);
                Check(warning.IsVisible && warning.TranslatePoint(new Point(), main).Y > Tagged<FrameworkElement>(main, "ai-model-row-page-damaged").TranslatePoint(new Point(), main).Y, "Warning is not under the list");

                // Cancel the consent window: nothing is requested, the pack stays missing.
                bool? answered = null;
                AnswerConsent(main, accept: false, () => answered = false);
                Click(Tagged<Button>(main, "ai-model-download-page-missing"));
                await Settle();
                Check(answered == false, "Download did not open the consent window");
                Check(server.Requests.Count == 0, "Something was requested before the licence was accepted");
                Check(ModelPacks.StateOf(missing, modelRoot) == ModelPackState.Missing, "Cancel changed the pack state");
                Check(!ModelPacks.Consents(modelRoot).IsCurrent(missing, ModelPacks.LicenseText(missing)), "Cancel recorded a consent");

                // Accept: progress shows, then Remove and the size on disk.
                double seenProgress = -1;
                server.OnRequest = _ => main.Dispatcher.Invoke(() =>
                {
                    var bar = Descendants(main).OfType<ProgressBar>().FirstOrDefault(b => (b.Tag as string) == "ai-model-progress-page-missing");
                    if (bar is { IsVisible: true }) seenProgress = bar.Value;
                });
                answered = null;
                AnswerConsent(main, accept: true, () => answered = true);
                Click(Tagged<Button>(main, "ai-model-download-page-missing"));
                Check(await Until(() => ModelPacks.StateOf(missing, modelRoot) == ModelPackState.Installed && FindTagged<Button>(main, "ai-model-remove-page-missing") != null), "Accepted download did not install");
                Check(answered == true, "Consent window was not shown before the download");
                Check(seenProgress >= 0, "No progress was shown while downloading");
                Check(server.Requests.Count == missing.Files.Length, "Unexpected requests: " + server.Requests.Count);
                Check(ModelPacks.Consents(modelRoot).IsCurrent(missing, ModelPacks.LicenseText(missing)), "Acceptance was not recorded");
                text = PageText(main);
                Check(text.Contains(L.T("Installed"), StringComparison.Ordinal), "Installed state not shown");
                Check(text.Contains(L.F($"Size on disk: {ModelPacks.FormatSize(ModelPacks.SizeOnDisk(missing, modelRoot))}"), StringComparison.Ordinal), "Size on disk not shown");
                Check(text.Contains(L.F($"{missing.Name} is ready."), StringComparison.Ordinal), "No ready message");
                Render(main, "ai-models-page-installed");

                // Remove asks first: declining keeps it, confirming returns the row to Missing.
                AnswerConfirmation(main, accept: false);
                Click(Tagged<Button>(main, "ai-model-remove-page-missing")); await Settle();
                Check(ModelPacks.StateOf(missing, modelRoot) == ModelPackState.Installed, "Remove did not ask before deleting");
                AnswerConfirmation(main, accept: true);
                Click(Tagged<Button>(main, "ai-model-remove-page-missing")); await Settle();
                Check(ModelPacks.StateOf(missing, modelRoot) == ModelPackState.Missing, "Remove did not delete the pack");
                Check(!ModelPacks.Consents(modelRoot).IsCurrent(missing, ModelPacks.LicenseText(missing)), "Remove kept the consent");
                Check(FindTagged<Button>(main, "ai-model-download-page-missing") != null, "Row did not return to Download");

                // Translation pack Remove.
                AnswerConfirmation(main, accept: true);
                Click(Tagged<Button>(main, "translation-remove-" + direction)); await Settle();
                Check(!Directory.Exists(Path.Combine(translationRoot, direction)), "Translation pack was not removed");
                Check(PageText(main).Contains(L.T("No translation packs downloaded. Text tools offers them when you translate."), StringComparison.Ordinal), "Empty translation group text missing");

                // Settings search finds the page.
                typeof(MainWindow).GetField("featureSettingsQuery", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, "licence");
                typeof(MainWindow).GetField("settingsTab", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, "Appearance");
                main.Navigate("Settings"); main.UpdateLayout();
                Click(Tagged<Button>(main, "settings-result-ai-models")); await Settle();
                Check(PageText(main).Contains(missing.Name, StringComparison.Ordinal), "Search result did not open the AI models page");
                typeof(MainWindow).GetField("featureSettingsQuery", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, "");
            }

            // Empty catalog: translation group plus the "no other models" line.
            using (ModelPacks.UseForTests([], modelRoot, server, plenty))
            using (TranslationPacks.UseRootForTests(translationRoot))
            {
                Open(main);
                string text = PageText(main);
                Check(text.Contains(L.T("Translation packs"), StringComparison.Ordinal), "Empty catalog hides the translation group");
                Check(Tagged<TextBlock>(main, "ai-models-none").Text == L.T("No other models are available yet."), "No 'no other models' line");
            }

            // Renders: both themes and every language, with one installed and one damaged pack.
            Directory.CreateDirectory(Path.Combine(translationRoot, direction));
            foreach (string file in new[] { "encoder_model_quantized.onnx", "decoder_model_merged_quantized.onnx", "source.spm", "vocab.json", "config.json" })
                File.WriteAllText(Path.Combine(translationRoot, direction, file), "x");
            consents = ModelPacks.Consents(modelRoot);
            consents.Accept(missing, ModelPacks.LicenseText(missing), DateTimeOffset.Now, "test");
            await ModelPacks.InstallAsync(missing, consents, null, default, modelRoot, server, plenty);
            using (ModelPacks.UseForTests([missing, damaged], modelRoot, server, plenty))
            using (TranslationPacks.UseRootForTests(translationRoot))
                foreach (string language in L.Languages)
                    foreach (string theme in new[] { "Light", "Dark" })
                    {
                        L.Use(language); controller.Settings.Theme = theme; controller.ApplyTheme();
                        Open(main); await Settle();
                        string where = $" ({language}, {theme})";
                        var heading = Descendants(main).OfType<TextBlock>().First(t => t.Text == L.T("Optional models"));
                        var color = ((SolidColorBrush)heading.Foreground).Color;
                        Check((color.R + color.G + color.B < 300) == (theme == "Light"), "Theme not applied" + where);
                        if (language != "en") Check(Tagged<TextBlock>(main, "ai-models-warning").Text != "These models are not part of DesktopTools. They were not created, trained, or owned by the DesktopTools project, and DesktopTools is not affiliated with or endorsed by their publishers.", "Warning not translated" + where);
                        foreach (var button in Descendants(main).OfType<ButtonBase>().Where(b => b.IsVisible && (b.Tag as string)?.StartsWith("ai-model-", StringComparison.Ordinal) == true))
                            Check(button.ActualWidth > 20 && button.ActualHeight > 20, "Button collapsed" + where);
                        Render(main, $"ai-models-page-{language}-{theme.ToLowerInvariant()}");
                        foreach (var scroller in Descendants(main).OfType<ScrollViewer>().Where(v => v.ScrollableHeight > 0)) scroller.ScrollToEnd();
                        await Settle();
                        Render(main, $"ai-models-page-{language}-{theme.ToLowerInvariant()}-bottom");
                    }
        }
        finally
        {
            L.Use("en"); controller.Settings.Theme = previousTheme; controller.ApplyTheme(); main.Hide();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static void Open(MainWindow main)
    {
        typeof(MainWindow).GetField("settingsTab", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, "AI models");
        main.Navigate("Settings"); main.UpdateLayout();
    }

    /// <summary>Answers the next consent window from inside its modal loop.</summary>
    private static void AnswerConsent(MainWindow main, bool accept, Action shown)
    {
        DispatcherTimer timer = new(DispatcherPriority.Background, main.Dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            var window = Application.Current.Windows.OfType<ModelConsentWindow>().FirstOrDefault(w => w.IsVisible);
            if (window == null) return;
            timer.Stop(); shown();
            if (accept)
            {
                ModelConsentUiChecks.Named<CheckBox>(window, "AcceptLicense").IsChecked = true;
                ModelConsentUiChecks.Named<Button>(window, "DownloadModel").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            else window.Close();
        };
        timer.Start();
    }

    private static void AnswerConfirmation(MainWindow main, bool accept)
    {
        DispatcherTimer timer = new(DispatcherPriority.Background, main.Dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            var window = Application.Current.Windows.OfType<ConfirmationDialog>().FirstOrDefault(w => w.IsVisible);
            if (window == null) return;
            timer.Stop();
            ModelConsentUiChecks.Named<Button>(window, accept ? "AcceptConfirmation" : "CancelConfirmation").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        };
        timer.Start();
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static async Task<bool> Until(Func<bool> condition)
    {
        for (int i = 0; i < 200; i++) { if (condition()) return true; await Task.Delay(50); }
        return condition();
    }
    private static string PageText(MainWindow main) { main.UpdateLayout(); return string.Join("\n", Descendants(main).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text)); }
    private static string ButtonText(Button button) => button.Content as string ?? string.Join(" ", Descendants(button).OfType<TextBlock>().Select(t => t.Text));
    private static string ItemText(object item) => item as string ?? (item as ContentControl)?.Content as string ?? item.ToString() ?? "";
    private static T? FindTagged<T>(DependencyObject root, string tag) where T : FrameworkElement { (root as FrameworkElement)?.UpdateLayout(); return Descendants(root).OfType<T>().FirstOrDefault(e => (e.Tag as string) == tag); }
    private static T Tagged<T>(DependencyObject root, string tag) where T : FrameworkElement => FindTagged<T>(root, tag) ?? throw new Exception("Missing element " + tag);
    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root) => ModelConsentUiChecks.Descendants(root);
    private static void Render(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(name + ".png"); png.Save(file);
    }
    private static Task Settle() => Task.Delay(120);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
