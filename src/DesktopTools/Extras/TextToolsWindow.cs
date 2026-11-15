using DesktopTools.Core;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace DesktopTools.Extras;

internal sealed class TextToolsWindow : Window
{
    private readonly AppController controller;
    private readonly Func<IReadOnlyList<string>> readMissingRuntime;
    private readonly TextBox source = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = LocalTranslation.MaxCharacters };
    private readonly TextBox output = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock status = Ui.Text(L.T("Select text in an app, scan an area, or paste text here."), 12, muted: true);
    private readonly Border activity = new() { Height = 4, CornerRadius = new CornerRadius(2), Opacity = 0, Margin = new Thickness(0, 10, 0, 10) };
    private readonly Button translate, cancel, copy;
    private readonly ComboBox ocrLanguage, ocrEngine;
    private readonly Button ocrPackDownload;
    private readonly TextBlock ocrPackStatus = Ui.Text("", 12, muted: true);
    private readonly ProgressBar ocrPackProgress = new() { Minimum = 0, Maximum = 1, Height = 5, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
    private const string WindowsEngine = "Windows OCR", PackEngine = "DesktopTools OCR pack";
    private readonly TranslationLanguagePicker languagePair;
    private readonly Button downloadPack, refreshLanguages;
    private readonly TextBlock packStatus = Ui.Text("", 12, muted: true);
    private readonly ProgressBar packProgress = new() { Minimum = 0, Maximum = 1, Height = 5, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
    // Long passages translate sentence by sentence; this shows how far a translation has got (collapsed when idle).
    private readonly ProgressBar translateProgress = new() { Minimum = 0, Maximum = 1, Height = 5, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
    private static readonly TimeSpan TranslationTimeout = TimeSpan.FromMinutes(30);
    private Button scan = null!, copySource = null!;
    private CancellationTokenSource? operation;
    private bool closed, writingSource;
    private bool synchronizingSettings;
    private readonly Grid fields = new();
    private readonly Image capturedArea = new() { Stretch = Stretch.Uniform, Margin = new Thickness(0, 7, 12, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock sourceHeading = Ui.Text(L.T("Source text"), 13, true);
    private readonly TextBlock resultHeading = Ui.Text(L.T("Translation"), 13, true);
    private FrameworkElement directionRow = null!, languageRow = null!;
    private bool ocrMode;
    private readonly RadioButton translationTab, screenshotTab;
    private readonly TextBlock modeDescription = Ui.Text("", 12, muted: true);
    private readonly TextBlock runtimeWarning = Ui.Text("", 12);
    private readonly Button runtimeHelp;
    private string lastDirection;
    internal bool IsProcessing => operation != null;
    public TextToolsWindow(AppController controller, Func<IReadOnlyList<string>>? readMissingRuntime = null)
    {
        this.controller = controller;
        this.readMissingRuntime = readMissingRuntime ?? VisualCppRuntime.FindMissingFiles;
        lastDirection = controller.Settings.TranslationDirection;
        Title = L.T("Text tools"); Width = 920; Height = 640; MinWidth = 750; MinHeight = 520;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; Background = Brushes.Transparent;
        UtilityWindowChrome.EnableBackdrop(this);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel(); var header = UtilityWindowChrome.Header(this, "DesktopTools — " + L.T("Text tools"), Close, L.T("Close text tools"), 13, allowMinimize: true); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var settings = new StackPanel();
        var modes = new Grid { Margin = new Thickness(0, 12, 0, 8) }; modes.ColumnDefinitions.Add(new ColumnDefinition()); modes.ColumnDefinitions.Add(new ColumnDefinition());
        translationTab = new RadioButton { Content = Ui.IconLabel("Translate", L.T("Translate text"), 20), GroupName = "TextMode", Tag = "text-mode-translate", Margin = new Thickness(0, 0, 4, 0) };
        screenshotTab = new RadioButton { Content = Ui.IconLabel("ScanText", L.T("Text from screenshot"), 20), GroupName = "TextMode", Tag = "text-mode-screenshot", Margin = new Thickness(4, 0, 0, 0) };
        foreach (var tab in new[] { translationTab, screenshotTab }) tab.SetResourceReference(StyleProperty, "ModeTab");
        System.Windows.Automation.AutomationProperties.SetName(translationTab, L.T("Translate text")); System.Windows.Automation.AutomationProperties.SetName(screenshotTab, L.T("Text from screenshot"));
        translationTab.Checked += (_, _) => { if (ocrMode) ShowMode(false); }; screenshotTab.Checked += (_, _) => { if (!ocrMode) ShowMode(true); };
        modes.Children.Add(translationTab); Grid.SetColumn(screenshotTab, 1); modes.Children.Add(screenshotTab); settings.Children.Add(modes);
        modeDescription.Margin = new Thickness(0, 0, 0, 10); settings.Children.Add(modeDescription);
        languagePair = new TranslationLanguagePicker(lastDirection, value => controller.UpdateSettings(s => s.TranslationDirection = value));
        if (languagePair.Direction != lastDirection) lastDirection = languagePair.Direction;
        languagePair.Margin = new Thickness(0, 0, 0, 8);
        var translationSettings = new StackPanel(); translationSettings.Children.Add(languagePair);
        var packRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        downloadPack = Ui.Button(L.T("Download offline packs"), async () => await DownloadPacksAsync());
        downloadPack.Content = Ui.IconLabel("Folder", L.T("Download offline packs"));
        DockPanel.SetDock(downloadPack, Dock.Right); packRow.Children.Add(downloadPack);
        packStatus.TextWrapping = TextWrapping.Wrap; packStatus.VerticalAlignment = VerticalAlignment.Center;
        packStatus.Margin = new Thickness(0, 0, 12, 0); packRow.Children.Add(packStatus);
        translationSettings.Children.Add(packRow); translationSettings.Children.Add(packProgress);
        directionRow = translationSettings; settings.Children.Add(directionRow);
        // Windows OCR stays the default; the optional OCR pack is a second engine (spec section 8). The language list follows the engine.
        ocrEngine = Ui.Choice([WindowsEngine, PackEngine], controller.Settings.OcrEngine == "Pack" ? PackEngine : WindowsEngine,
            value => { if (!synchronizingSettings) controller.UpdateSettings(s => s.OcrEngine = value == PackEngine ? "Pack" : "Windows"); }, aiChoices: [PackEngine]);
        ocrEngine.Tag = "ocr-engine"; ocrEngine.MinWidth = 180;
        System.Windows.Automation.AutomationProperties.SetName(ocrEngine, L.T("Text reader"));
        var languages = LocalOcr.Choices();
        ocrLanguage = new ComboBox { ItemsSource = languages, MinWidth = 180, SelectedItem = LocalOcr.SelectChoice(languages, SavedOcrTag()) };
        ocrLanguage.SelectionChanged += (_, _) => { if (!synchronizingSettings && ocrLanguage.SelectedItem is OcrLanguage selected) SaveOcrChoice(selected); RefreshOcrPackStatus(); };
        var ocrChoices = new StackPanel { Orientation = Orientation.Horizontal }; ocrChoices.Children.Add(ocrLanguage);
        refreshLanguages = Ui.IconButton("Refresh", L.T("Refresh Windows OCR languages"), RefreshOcrLanguages); ocrChoices.Children.Add(refreshLanguages);
        ocrPackDownload = Ui.Button(L.T("Download"), async () => await DownloadOcrPackAsync());
        ocrPackDownload.Content = Ui.IconLabel("Save", L.T("Download")); ocrPackDownload.Tag = "ocr-pack-download"; ocrPackDownload.Margin = new Thickness(8, 0, 0, 0);
        ocrChoices.Children.Add(ocrPackDownload);
        var ocrSettings = new StackPanel();
        ocrSettings.Children.Add(Ui.Row(L.T("Text reader"), L.T("Windows OCR is built in. The DesktopTools OCR pack is an optional download that reads small text more accurately."), ocrEngine));
        ocrSettings.Children.Add(Ui.Row(L.T("Screen text language"), L.T("Uses installed Windows OCR language packs, or the OCR pack's alphabets."), ocrChoices));
        ocrPackStatus.TextWrapping = TextWrapping.Wrap; ocrPackStatus.Tag = "ocr-pack-status"; ocrPackStatus.Margin = new Thickness(0, 0, 0, 8);
        ocrSettings.Children.Add(ocrPackStatus); ocrSettings.Children.Add(ocrPackProgress);
        languageRow = ocrSettings; settings.Children.Add(languageRow);
        RefreshOcrPackStatus();
        DockPanel.SetDock(settings, Dock.Top); root.Children.Add(settings);
        var footer = new StackPanel(); activity.SetResourceReference(Border.BackgroundProperty, "Accent"); footer.Children.Add(activity);
        status.TextWrapping = TextWrapping.Wrap; footer.Children.Add(status);
        System.Windows.Automation.AutomationProperties.SetName(translateProgress, L.T("Translation progress")); footer.Children.Add(translateProgress);
        runtimeWarning.TextWrapping = TextWrapping.Wrap; runtimeWarning.Margin = new Thickness(0, 8, 0, 0);
        footer.Children.Add(runtimeWarning);
        runtimeHelp = Ui.Button(L.T("Open Microsoft Visual C++ Runtime download page"), OpenRuntimeHelp);
        runtimeHelp.HorizontalAlignment = HorizontalAlignment.Left; runtimeHelp.Margin = new Thickness(0, 8, 0, 0);
        footer.Children.Add(runtimeHelp);
        var actions = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        translate = Ui.Button(L.T("Translate"), async () => await TranslateAsync(), true);
        translate.Width = 150; DockPanel.SetDock(translate, Dock.Right); actions.Children.Add(translate);
        cancel = Ui.IconButton("Close", L.T("Cancel"), () => operation?.Cancel()); cancel.Visibility = Visibility.Collapsed; DockPanel.SetDock(cancel, Dock.Right); actions.Children.Add(cancel);
        copy = Ui.IconButton("Copy", L.T("Copy result"), () => { if (Copy(ocrMode ? source.Text : output.Text)) Ui.Flash(copy!); }); copy.IsEnabled = false;
        scan = Ui.Button(L.T("Scan screen text"), async () => await controller.CaptureAsync(textOnly: true)); scan.Content = Ui.IconLabel("ScanText", L.T("Scan screen text")); scan.HorizontalAlignment = HorizontalAlignment.Left; actions.Children.Add(scan);
        copySource = Ui.IconButton("Copy", L.T("Copy source"), () => { if (Copy(source.Text)) Ui.Flash(copySource!); });
        footer.Children.Add(actions); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); fields.RowDefinitions.Add(new RowDefinition());
        fields.ColumnDefinitions.Add(new ColumnDefinition()); fields.ColumnDefinitions.Add(new ColumnDefinition());
        var leftHeading = new DockPanel { Margin = new Thickness(0, 0, 12, 0) }; DockPanel.SetDock(copySource, Dock.Right); leftHeading.Children.Add(copySource); leftHeading.Children.Add(sourceHeading); fields.Children.Add(leftHeading);
        var rightHeading = new DockPanel(); DockPanel.SetDock(copy, Dock.Right); rightHeading.Children.Add(copy); rightHeading.Children.Add(resultHeading); Grid.SetColumn(rightHeading, 1); fields.Children.Add(rightHeading);
        source.Margin = new Thickness(0, 7, 12, 0); Grid.SetRow(source, 1); fields.Children.Add(source);
        output.Margin = new Thickness(0, 7, 0, 0); Grid.SetRow(output, 1); Grid.SetColumn(output, 1); fields.Children.Add(output);
        Grid.SetRow(capturedArea, 1); fields.Children.Add(capturedArea); root.Children.Add(fields);
        System.Windows.Automation.AutomationProperties.SetName(capturedArea, L.T("Selected area"));
        var card = Ui.Card(root, 20); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); card.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); Content = card;
        source.VerticalContentAlignment = VerticalAlignment.Top; output.VerticalContentAlignment = VerticalAlignment.Top;
        source.Padding = output.Padding = new Thickness(12); source.FontSize = output.FontSize = 14;
        System.Windows.Automation.AutomationProperties.SetName(source, L.T("Source text")); System.Windows.Automation.AutomationProperties.SetName(output, L.T("Translation"));
        System.Windows.Automation.AutomationProperties.SetName(languagePair, L.T("Translation direction")); System.Windows.Automation.AutomationProperties.SetName(ocrLanguage, L.T("Screen text language"));
        source.TextChanged += (_, _) => { if (!writingSource) Invalidate(); RefreshButtons(); };
        controller.SettingsChanged += SettingsChanged;
        Closed += (_, _) => { closed = true; controller.SettingsChanged -= SettingsChanged; operation?.Cancel(); StopAnimation(); source.Clear(); output.Clear(); capturedArea.Source = null; };
        IsVisibleChanged += (_, _) => UpdateAnimation();
        Activated += (_, _) => { if (!IsProcessing) RefreshOcrLanguages(); };
        var guide = FeatureTourButton.Create(this, "text-tools", () => new GuidedTour.Step[]
        {
            new(() => source, "Source text", "Paste text here, or scan an area of the screen. You can correct recognized text before translating."),
            new(() => ocrMode ? languageRow : directionRow, "Languages", "Choose the source and result languages, or an installed OCR language."),
            new(() => actions, "Translate", "Run translation or OCR when ready. Copy transfers the result to the clipboard.")
        }, controller.Settings, controller.UpdateSettings); DockPanel.SetDock(guide, Dock.Right); header.Children.Insert(header.Children.Count - 1, guide);
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { if (IsProcessing) operation?.Cancel(); else Close(); e.Handled = true; } };
        Motion.WindowEntrance(this); ShowMode(!controller.Settings.TranslationEnabled); RefreshButtons();
    }
    internal void ShowMode(bool ocr)
    {
        if (ocrMode != ocr) operation?.Cancel();
        ocrMode = ocr;
        translationTab.IsChecked = !ocr; screenshotTab.IsChecked = ocr;
        modeDescription.Text = L.T(ocr ? "Select an area of your screen, then copy the recognized text." : "Type or paste text, choose the languages, then translate.");
        scan.Visibility = ocr ? Visibility.Visible : Visibility.Collapsed; copySource.Visibility = ocr ? Visibility.Collapsed : Visibility.Visible;
        directionRow.Visibility = ocr ? Visibility.Collapsed : Visibility.Visible;
        languageRow.Visibility = ocr ? Visibility.Visible : Visibility.Collapsed;
        capturedArea.Visibility = ocr ? Visibility.Visible : Visibility.Collapsed;
        output.Visibility = ocr ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(source, ocr ? 1 : 0); source.Margin = new Thickness(0, 7, ocr ? 0 : 12, 0);
        sourceHeading.Text = L.T(ocr ? "Selected area" : "Source text"); resultHeading.Text = L.T(ocr ? "Recognized text" : "Translation");
        System.Windows.Automation.AutomationProperties.SetName(source, L.T(ocr ? "Recognized text" : "Source text")); RefreshButtons();
        RefreshRuntimeWarning();
    }
    private bool Copy(string text) { try { if (!string.IsNullOrWhiteSpace(text)) { var copied = DesktopTools.Native.ClipboardService.SetText(text); if (copied.Success) { status.Text = L.T("Text copied."); return true; } controller.Report(L.T("Could not copy text: ") + copied.Error); } } catch (Exception ex) { controller.Report(L.T("Could not copy text: ") + ex.Message); } return false; }
    private void SettingsChanged()
    {
        if (closed) return;
        if (lastDirection != controller.Settings.TranslationDirection)
        {
            lastDirection = controller.Settings.TranslationDirection; Invalidate();
            languagePair.SetDirection(lastDirection);
        }
        string engine = LocalOcr.PackEngineChosen ? PackEngine : WindowsEngine;
        if (!Equals(ocrEngine.SelectedItem, engine))
        {
            synchronizingSettings = true;
            try { ocrEngine.SelectedItem = engine; } finally { synchronizingSettings = false; }
        }
        var shown = (ocrLanguage.ItemsSource as IEnumerable<OcrLanguage>)?.ToArray() ?? [];
        bool packShown = shown.Any(l => OcrPackPipeline.TryParseTag(l.Tag, out _));
        if (packShown != LocalOcr.PackEngineChosen) { operation?.Cancel(); RefreshOcrLanguages(); }
        else
        {
            var language = shown.FirstOrDefault(l => l.Tag == SavedOcrTag());
            if (language != null && !Equals(ocrLanguage.SelectedItem, language))
            {
                operation?.Cancel(); synchronizingSettings = true;
                try { ocrLanguage.SelectedItem = language; } finally { synchronizingSettings = false; }
            }
        }
        RefreshButtons(); UpdateAnimation();
    }
    private void RefreshOcrLanguages()
    {
        if (closed || IsProcessing) return;
        var languages = LocalOcr.Choices();
        synchronizingSettings = true;
        try
        {
            ocrLanguage.ItemsSource = languages;
            ocrLanguage.SelectedItem = LocalOcr.SelectChoice(languages, SavedOcrTag());
        }
        finally { synchronizingSettings = false; }
        if (ocrLanguage.SelectedItem is OcrLanguage selected) SaveOcrChoice(selected);
        RefreshOcrPackStatus();
    }
    /// <summary>The reading tag the settings point at: the pack's saved script group with the pack engine, else the Windows OCR language.</summary>
    private string SavedOcrTag() => controller.Settings.OcrEngine == "Pack" && OcrPackPipeline.TryParseKey(controller.Settings.OcrPackScript, out var script)
        ? OcrPackPipeline.Tag(script) : controller.Settings.ScreenTextLanguage;
    private void SaveOcrChoice(OcrLanguage selected)
    {
        if (OcrPackPipeline.TryParseTag(selected.Tag, out var script))
        {
            string key = OcrPackPipeline.Key(script);
            if (controller.Settings.OcrPackScript != key) controller.UpdateSettings(s => s.OcrPackScript = key);
        }
        else if (controller.Settings.ScreenTextLanguage != selected.Tag) controller.UpdateSettings(s => s.ScreenTextLanguage = selected.Tag);
    }
    private OcrPackScript? SelectedPackScript => ocrLanguage.SelectedItem is OcrLanguage language && OcrPackPipeline.TryParseTag(language.Tag, out var script) ? script : null;
    internal string OcrPackStatusText => ocrPackStatus.Text;
    private void RefreshOcrPackStatus()
    {
        if (closed || ocrPackDownload == null) return;
        var script = SelectedPackScript;
        var missing = script is { } chosen ? OcrPackEngine.MissingPacks(chosen) : [];
        ocrPackDownload.Visibility = missing.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ocrPackDownload.IsEnabled = !IsProcessing;
        refreshLanguages.Visibility = script == null ? Visibility.Visible : Visibility.Collapsed;
        ocrPackStatus.Visibility = script == null ? Visibility.Collapsed : Visibility.Visible;
        ocrPackStatus.Text = script == null ? ""
            : missing.Any(pack => ModelPacks.StateOf(pack) == ModelPackState.Damaged) ? L.T("The OCR pack is damaged. Download it again to use it.")
            : missing.Count > 0 ? L.F($"Not downloaded yet ({ModelPacks.FormatSize(missing.Sum(pack => pack.TotalBytes))}). Until then, text is read with Windows OCR.")
            : L.T("OCR pack ready. Text is read on this PC.");
    }
    /// <summary>Asks for each missing pack's licence in the consent window (text finder first), then downloads the accepted ones.</summary>
    private async Task DownloadOcrPackAsync()
    {
        if (closed || IsProcessing || SelectedPackScript is not { } script) return;
        var packs = OcrPackEngine.MissingPacks(script);
        if (packs.Count == 0) { RefreshOcrPackStatus(); return; }
        var consents = ModelPacks.Consents();
        try
        {
            foreach (var pack in packs)
                if (!ModelConsentWindow.Ask(this, pack, consents)) { status.Text = L.T("Download canceled."); return; }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { status.Text = L.T("Could not download the OCR pack: ") + ex.Message; return; }
        var cancellation = Begin(L.T("Downloading the OCR pack…"), TimeSpan.FromMinutes(30));
        ocrPackProgress.Visibility = Visibility.Visible; ocrPackProgress.Value = 0;
        try
        {
            for (int i = 0; i < packs.Count; i++)
            {
                int index = i;
                var progress = new Progress<double>(value => { if (!closed && operation == cancellation) ocrPackProgress.Value = (index + value) / packs.Count; });
                await ModelPacks.InstallAsync(packs[i], consents, progress, cancellation.Token);
            }
            if (!closed) status.Text = L.T("OCR pack ready. Text is read on this PC.");
        }
        catch (OperationCanceledException) { if (!closed) status.Text = L.T("Download canceled."); }
        catch (Exception ex) { if (!closed) status.Text = L.T("Could not download the OCR pack: ") + ex.Message; }
        finally
        {
            if (!closed) ocrPackProgress.Visibility = Visibility.Collapsed;
            End(cancellation);
            if (!closed) RefreshOcrLanguages();
        }
    }
    private void RefreshPackStatus()
    {
        if (closed) return;
        var missing = TranslationPacks.Missing(languagePair.Direction);
        packStatus.Text = missing.Count > 0
            ? L.F($"Offline packs needed: {missing.Sum(pack => pack.Files.Sum(file => file.Bytes)) / 1048576d:0} MB. Download once; your text stays on this PC.")
            : L.T(TranslationPacks.Route(languagePair.Direction).Length == 2 ? "Offline translation through English. Review the result." : "Offline packs ready. Your text stays on this PC.");
        downloadPack.Visibility = missing.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        downloadPack.IsEnabled = !IsProcessing;
    }
    private async Task DownloadPacksAsync()
    {
        if (closed || IsProcessing) return;
        var packs = TranslationPacks.Missing(languagePair.Direction);
        if (packs.Count == 0) return;
        // Each new pack's licence is accepted in the consent window first (spec 2.4); declining any of them downloads nothing.
        var consents = ModelPacks.Consents();
        try
        {
            foreach (var pack in packs)
                if (!ModelConsentWindow.Ask(this, TranslationPacks.ConsentPack(pack), consents)) { status.Text = L.T("Download canceled."); return; }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { status.Text = L.T("Could not download offline packs: ") + ex.Message; return; }
        var cancellation = Begin(L.T("Downloading offline packs…"), TimeSpan.FromMinutes(30));
        packProgress.Visibility = Visibility.Visible; packProgress.Value = 0;
        try
        {
            for (int i = 0; i < packs.Count; i++)
            {
                int index = i;
                var progress = new Progress<double>(value => { if (!closed && operation == cancellation) packProgress.Value = (index + value) / packs.Count; });
                await TranslationPacks.InstallAsync(packs[i].Direction, consents, progress, cancellation.Token);
            }
            if (!closed) status.Text = L.T("Offline packs ready. Your text stays on this PC.");
        }
        catch (OperationCanceledException) { if (!closed) status.Text = L.T("Processing canceled. You can try again."); }
        catch (Exception ex) { if (!closed) status.Text = L.T("Could not download offline packs: ") + ex.Message; }
        finally { if (!closed) packProgress.Visibility = Visibility.Collapsed; End(cancellation); }
    }
    private void Invalidate() { operation?.Cancel(); output.Clear(); copy.IsEnabled = false; status.Text = L.T("Text changed. Translate when ready."); }
    internal void SetSource(string text)
    {
        ShowMode(false); Invalidate(); writingSource = true; try { source.Text = text; } finally { writingSource = false; }
        status.Text = string.IsNullOrWhiteSpace(text) ? L.T("Selection unavailable. Paste text here or use Scan screen text.") : L.T("Review the source, then choose Translate."); RefreshButtons();
    }
    internal async Task TranslateAsync()
    {
        if (closed || !controller.Settings.TranslationEnabled || string.IsNullOrWhiteSpace(source.Text)) return;
        ShowMode(false);
        if (RefreshRuntimeWarning().Count > 0)
        {
            status.Text = L.T("Local translation is unavailable until Microsoft Visual C++ x64 is installed.");
            controller.Report(status.Text, NotificationKind.Warning);
            return;
        }
        if (TranslationPacks.Missing(languagePair.Direction).Count > 0) { status.Text = L.T("Download the offline language pack first."); return; }
        var input = source.Text; string language = languagePair.Direction;
        var cancellation = Begin(L.T("Translating locally…"), TranslationTimeout);
        translateProgress.Value = 0; translateProgress.Visibility = Visibility.Visible;
        var progress = new Progress<double>(value => { if (!closed && operation == cancellation) translateProgress.Value = value; });
        try
        {
            var result = await LocalTranslation.TranslateAsync(input, language, cancellation.Token, null, progress); cancellation.Token.ThrowIfCancellationRequested();
            if (!closed && operation == cancellation) { output.Text = result; status.Text = L.T("Translation ready. Review it before use."); }
        }
        catch (OperationCanceledException) { if (!closed && operation == cancellation) status.Text = L.T("Processing canceled. You can try again."); }
        catch (Exception ex) { if (!closed && operation == cancellation) status.Text = L.T("Translation failed: ") + ex.Message; }
        finally { if (operation == cancellation) translateProgress.Visibility = Visibility.Collapsed; End(cancellation); }
    }
    internal async Task RecognizeScreenAsync(BitmapSource image)
    {
        if (closed || !controller.Settings.ScreenTextEnabled) return;
        RefreshOcrLanguages();
        ShowMode(true); capturedArea.Source = image;
        if (ocrLanguage.SelectedItem is not OcrLanguage language) { status.Text = L.T("No Windows OCR languages are installed. Add a language pack in Windows Settings → Time & language → Language & region, then reopen this window."); return; }
        var cancellation = Begin(L.T("Recognizing text…"));
        try
        {
            var (recognized, notice) = await LocalOcr.ReadTextAsync(image, language.Tag, cancellation.Token); cancellation.Token.ThrowIfCancellationRequested();
            if (recognized.Length > LocalTranslation.MaxCharacters) throw new InvalidOperationException(L.T("Select a smaller text area (up to 20,000 characters)."));
            if (!closed && operation == cancellation)
            {
                writingSource = true; try { source.Text = recognized; } finally { writingSource = false; }
                status.Text = string.IsNullOrWhiteSpace(recognized) ? L.T("No text found. Try another language or a tighter crop.") : L.T("Text ready. Review and edit it before copying.");
                // A missing or damaged OCR pack was replaced by Windows OCR: say so first.
                if (notice != null) status.Text = notice + " " + status.Text;
            }
        }
        catch (OperationCanceledException) { if (!closed && operation == cancellation) status.Text = L.T("Processing canceled. You can try again."); }
        catch (Exception ex) { if (!closed && operation == cancellation) status.Text = L.T("Text recognition unavailable: ") + ex.Message; }
        finally { End(cancellation); }
    }
    private CancellationTokenSource Begin(string message, TimeSpan? timeout = null)
    {
        operation?.Cancel(); output.Clear(); translateProgress.Visibility = Visibility.Collapsed; var next = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(2)); operation = next; status.Text = message; RefreshButtons(); UpdateAnimation(); return next;
    }
    private void End(CancellationTokenSource completed)
    {
        if (operation == completed)
        {
            operation = null;
            if (!closed)
            {
                // The engine may have changed while this ran (the list is not rebuilt during an operation).
                bool packShown = (ocrLanguage.ItemsSource as IEnumerable<OcrLanguage>)?.Any(l => OcrPackPipeline.TryParseTag(l.Tag, out _)) == true;
                if (packShown != LocalOcr.PackEngineChosen) RefreshOcrLanguages();
                RefreshButtons(); UpdateAnimation();
            }
        }
        completed.Dispose();
    }
    private void RefreshButtons()
    {
        translate.IsEnabled = !closed && !IsProcessing && controller.Settings.TranslationEnabled && !string.IsNullOrWhiteSpace(source.Text);
        if (translate.IsEnabled) translate.IsEnabled = TranslationPacks.Missing(languagePair.Direction).Count == 0;
        languagePair.IsEnabled = refreshLanguages.IsEnabled = !IsProcessing; RefreshPackStatus();
        copy.IsEnabled = !IsProcessing && !string.IsNullOrWhiteSpace(ocrMode ? source.Text : output.Text); cancel.Visibility = IsProcessing ? Visibility.Visible : Visibility.Collapsed; ocrLanguage.IsEnabled = ocrEngine.IsEnabled = !IsProcessing;
        if (ocrPackDownload != null) ocrPackDownload.IsEnabled = !IsProcessing;
    }
    private IReadOnlyList<string> RefreshRuntimeWarning()
    {
        var missing = ocrMode ? Array.Empty<string>() : readMissingRuntime();
        bool unavailable = missing.Count > 0;
        runtimeWarning.Text = unavailable
            ? L.T("Local translation needs Microsoft Visual C++ x64. Install it to use offline translation. Screen text scanning still works.")
                + "\n" + L.T("Missing files: ") + string.Join(", ", missing)
            : "";
        runtimeWarning.Visibility = runtimeHelp.Visibility = unavailable ? Visibility.Visible : Visibility.Collapsed;
        return missing;
    }
    private void OpenRuntimeHelp()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(VisualCppRuntime.HelpUri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { status.Text = L.T("Could not open the Microsoft download page. Use this link: ") + VisualCppRuntime.HelpUri.AbsoluteUri + "\n" + ex.Message; }
    }
    private void StopAnimation() { activity.BeginAnimation(OpacityProperty, null); activity.Opacity = IsProcessing && !closed ? .65 : 0; }
    private void UpdateAnimation()
    {
        StopAnimation();
        if (!closed && IsVisible && IsProcessing && Motion.Enabled) activity.BeginAnimation(OpacityProperty, new DoubleAnimation(.25, 1, TimeSpan.FromMilliseconds(550)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }
}
