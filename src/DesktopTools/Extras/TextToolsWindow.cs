using DesktopTools.Localization;
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
    private readonly TextBox source = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = LocalTranslation.MaxCharacters };
    private readonly TextBox output = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock status = Ui.Text(L.T("Select text in an app, scan an area, or paste text here."), 12, muted: true);
    private readonly Border activity = new() { Height = 4, CornerRadius = new CornerRadius(2), Opacity = 0, Margin = new Thickness(0, 10, 0, 10) };
    private readonly Button translate, cancel, copy;
    private readonly ComboBox direction, ocrLanguage;
    private ComboBox fromLanguage = null!, toLanguage = null!;
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
    private string lastDirection;
    internal bool IsProcessing => operation != null;
    public TextToolsWindow(AppController controller)
    {
        this.controller = controller;
        lastDirection = controller.Settings.TranslationDirection;
        Title = L.T("Text tools"); Width = 860; Height = 560; MinWidth = 660; MinHeight = 420;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; Background = Brushes.Transparent;
        UtilityWindowChrome.EnableBackdrop(this);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel(); var header = UtilityWindowChrome.Header(this, "DesktopTools — " + L.T("Text tools"), Close, L.T("Close text tools"), 13); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var settings = new StackPanel();
        var modes = new Grid { Margin = new Thickness(0, 12, 0, 8) }; modes.ColumnDefinitions.Add(new ColumnDefinition()); modes.ColumnDefinitions.Add(new ColumnDefinition());
        translationTab = new RadioButton { Content = Ui.IconLabel("Translate", L.T("Translate text"), 20), GroupName = "TextMode", Tag = "text-mode-translate", Margin = new Thickness(0, 0, 4, 0) };
        screenshotTab = new RadioButton { Content = Ui.IconLabel("ScanText", L.T("Text from screenshot"), 20), GroupName = "TextMode", Tag = "text-mode-screenshot", Margin = new Thickness(4, 0, 0, 0) };
        foreach (var tab in new[] { translationTab, screenshotTab }) tab.SetResourceReference(StyleProperty, "ModeTab");
        System.Windows.Automation.AutomationProperties.SetName(translationTab, L.T("Translate text")); System.Windows.Automation.AutomationProperties.SetName(screenshotTab, L.T("Text from screenshot"));
        translationTab.Checked += (_, _) => { if (ocrMode) ShowMode(false); }; screenshotTab.Checked += (_, _) => { if (!ocrMode) ShowMode(true); };
        modes.Children.Add(translationTab); Grid.SetColumn(screenshotTab, 1); modes.Children.Add(screenshotTab); settings.Children.Add(modes);
        modeDescription.Margin = new Thickness(0, 0, 0, 10); settings.Children.Add(modeDescription);
        var directionNames = new[] { "English → Russian", "Russian → English" };
        direction = Ui.Choice(directionNames, controller.Settings.TranslationDirection == "ru-en" ? directionNames[1] : directionNames[0], value =>
        {
            if (synchronizingSettings) return;
            controller.UpdateSettings(s => s.TranslationDirection = value == directionNames[1] ? "ru-en" : "en-ru"); Invalidate();
        });
        var pair = new Grid { Margin = new Thickness(0, 8, 0, 16) }; pair.ColumnDefinitions.Add(new ColumnDefinition()); pair.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) }); pair.ColumnDefinitions.Add(new ColumnDefinition());
        fromLanguage = Ui.Choice(new[] { "English", "Russian" }, controller.Settings.TranslationDirection == "ru-en" ? "Russian" : "English", value => { if (!synchronizingSettings) direction.SelectedItem = value == "Russian" ? directionNames[1] : directionNames[0]; });
        toLanguage = Ui.Choice(new[] { "English", "Russian" }, controller.Settings.TranslationDirection == "ru-en" ? "English" : "Russian", value => { if (!synchronizingSettings) direction.SelectedItem = value == "English" ? directionNames[1] : directionNames[0]; });
        var swap = Ui.IconButton("Swap", L.T("Swap languages"), () => direction.SelectedItem = Equals(direction.SelectedItem, directionNames[0]) ? directionNames[1] : directionNames[0]); pair.Children.Add(fromLanguage); Grid.SetColumn(swap, 1); pair.Children.Add(swap); Grid.SetColumn(toLanguage, 2); pair.Children.Add(toLanguage);
        System.Windows.Automation.AutomationProperties.SetName(fromLanguage, L.T("Source language")); System.Windows.Automation.AutomationProperties.SetName(toLanguage, L.T("Target language"));
        directionRow = pair; settings.Children.Add(directionRow);
        var languages = LocalOcr.Languages;
        ocrLanguage = new ComboBox { ItemsSource = languages, MinWidth = 180, SelectedItem = languages.FirstOrDefault(l => l.Tag == controller.Settings.ScreenTextLanguage) ?? languages.FirstOrDefault() };
        ocrLanguage.SelectionChanged += (_, _) => { if (!synchronizingSettings && ocrLanguage.SelectedItem is OcrLanguage selected) controller.UpdateSettings(s => s.ScreenTextLanguage = selected.Tag); };
        languageRow = Ui.Row(L.T("Screen text language"), L.T("Uses installed Windows OCR language packs."), ocrLanguage); settings.Children.Add(languageRow);
        DockPanel.SetDock(settings, Dock.Top); root.Children.Add(settings);
        var footer = new StackPanel(); activity.SetResourceReference(Border.BackgroundProperty, "Accent"); footer.Children.Add(activity);
        status.TextWrapping = TextWrapping.Wrap; footer.Children.Add(status);
        var actions = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        translate = Ui.Button(L.T("Translate"), async () => await TranslateAsync(), true);
        translate.Width = 150; DockPanel.SetDock(translate, Dock.Right); actions.Children.Add(translate);
        cancel = Ui.IconButton("Close", L.T("Cancel"), () => operation?.Cancel()); cancel.Visibility = Visibility.Collapsed; DockPanel.SetDock(cancel, Dock.Right); actions.Children.Add(cancel);
        copy = Ui.IconButton("Copy", L.T("Copy result"), () => Copy(ocrMode ? source.Text : output.Text)); copy.IsEnabled = false;
        scan = Ui.Button(L.T("Scan screen text"), async () => await controller.CaptureAsync(textOnly: true)); scan.Content = Ui.IconLabel("ScanText", L.T("Scan screen text")); scan.HorizontalAlignment = HorizontalAlignment.Left; actions.Children.Add(scan);
        copySource = Ui.IconButton("Copy", L.T("Copy source"), () => Copy(source.Text));
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
        System.Windows.Automation.AutomationProperties.SetName(direction, L.T("Translation direction")); System.Windows.Automation.AutomationProperties.SetName(ocrLanguage, L.T("Screen text language"));
        source.TextChanged += (_, _) => { if (!writingSource) Invalidate(); RefreshButtons(); };
        controller.SettingsChanged += SettingsChanged;
        Closed += (_, _) => { closed = true; controller.SettingsChanged -= SettingsChanged; operation?.Cancel(); StopAnimation(); source.Clear(); output.Clear(); capturedArea.Source = null; };
        IsVisibleChanged += (_, _) => UpdateAnimation();
        var guide = FeatureTourButton.Create(this, "text-tools", () => new GuidedTour.Step[]
        {
            new(() => source, "Source text", "Paste text here, or scan an area of the screen. You can correct recognized text before translating."),
            new(() => ocrMode ? languageRow : directionRow, "Languages", "Choose the source and result languages, or an installed OCR language."),
            new(() => actions, "Translate", "Run translation or OCR when ready. Copy transfers the result to the clipboard.")
        }, controller.Settings, controller.UpdateSettings); DockPanel.SetDock(guide, Dock.Right); header.Children.Insert(1, guide);
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
    }
    private void Copy(string text) { try { if (!string.IsNullOrWhiteSpace(text)) { Clipboard.SetText(text); status.Text = L.T("Text copied."); } } catch (Exception ex) { controller.Report(L.T("Could not copy text: ") + ex.Message); } }
    private void SettingsChanged()
    {
        if (closed) return;
        if (lastDirection != controller.Settings.TranslationDirection)
        {
            lastDirection = controller.Settings.TranslationDirection; Invalidate();
            synchronizingSettings = true; try { direction.SelectedItem = lastDirection == "ru-en" ? "Russian → English" : "English → Russian"; fromLanguage.SelectedItem = lastDirection == "ru-en" ? "Russian" : "English"; toLanguage.SelectedItem = lastDirection == "ru-en" ? "English" : "Russian"; } finally { synchronizingSettings = false; }
        }
        var language = LocalOcr.Languages.FirstOrDefault(l => l.Tag == controller.Settings.ScreenTextLanguage);
        if (language != null && !Equals(ocrLanguage.SelectedItem, language))
        {
            operation?.Cancel(); synchronizingSettings = true;
            try { ocrLanguage.SelectedItem = language; } finally { synchronizingSettings = false; }
        }
        RefreshButtons(); UpdateAnimation();
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
        var input = source.Text; string language = controller.Settings.TranslationDirection;
        var cancellation = Begin(L.T("Translating locally…"));
        try
        {
            var result = await LocalTranslation.TranslateAsync(input, language, cancellation.Token); cancellation.Token.ThrowIfCancellationRequested();
            if (!closed && operation == cancellation) { output.Text = result; status.Text = L.T("Translation ready. Review it before use."); }
        }
        catch (OperationCanceledException) { if (!closed && operation == cancellation) status.Text = L.T("Processing canceled. You can try again."); }
        catch (Exception ex) { if (!closed && operation == cancellation) status.Text = L.T("Translation failed: ") + ex.Message; }
        finally { End(cancellation); }
    }
    internal async Task RecognizeScreenAsync(BitmapSource image)
    {
        if (closed || !controller.Settings.ScreenTextEnabled) return;
        ShowMode(true); capturedArea.Source = image;
        if (ocrLanguage.SelectedItem is not OcrLanguage language) { status.Text = L.T("No Windows OCR languages are installed. Add a language pack in Windows Settings → Time & language → Language & region, then reopen this window."); return; }
        var cancellation = Begin(L.T("Recognizing text…"));
        try
        {
            string recognized = await LocalOcr.RecognizeAsync(image, language.Tag, cancellation.Token); cancellation.Token.ThrowIfCancellationRequested();
            if (recognized.Length > LocalTranslation.MaxCharacters) throw new InvalidOperationException(L.T("Select a smaller text area (up to 4,000 characters)."));
            if (!closed && operation == cancellation)
            {
                writingSource = true; try { source.Text = recognized; } finally { writingSource = false; }
                status.Text = string.IsNullOrWhiteSpace(recognized) ? L.T("No text found. Try another language or a tighter crop.") : L.T("Text ready. Review and edit it before copying.");
            }
        }
        catch (OperationCanceledException) { if (!closed && operation == cancellation) status.Text = L.T("Processing canceled. You can try again."); }
        catch (Exception ex) { if (!closed && operation == cancellation) status.Text = L.T("Text recognition unavailable: ") + ex.Message; }
        finally { End(cancellation); }
    }
    private CancellationTokenSource Begin(string message)
    {
        operation?.Cancel(); output.Clear(); var next = new CancellationTokenSource(TimeSpan.FromMinutes(2)); operation = next; status.Text = message; RefreshButtons(); UpdateAnimation(); return next;
    }
    private void End(CancellationTokenSource completed)
    {
        if (operation == completed) { operation = null; if (!closed) { RefreshButtons(); UpdateAnimation(); } }
        completed.Dispose();
    }
    private void RefreshButtons()
    {
        translate.IsEnabled = !closed && !IsProcessing && controller.Settings.TranslationEnabled && !string.IsNullOrWhiteSpace(source.Text);
        copy.IsEnabled = !IsProcessing && !string.IsNullOrWhiteSpace(ocrMode ? source.Text : output.Text); cancel.Visibility = IsProcessing ? Visibility.Visible : Visibility.Collapsed; ocrLanguage.IsEnabled = !IsProcessing;
    }
    private void StopAnimation() { activity.BeginAnimation(OpacityProperty, null); activity.Opacity = IsProcessing && !closed ? .65 : 0; }
    private void UpdateAnimation()
    {
        StopAnimation();
        if (!closed && IsVisible && IsProcessing && Motion.Enabled) activity.BeginAnimation(OpacityProperty, new DoubleAnimation(.25, 1, TimeSpan.FromMilliseconds(550)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }
}
