using DesktopTools.Core;
using DesktopTools.Localization;
using DesktopTools.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Text.Json;

namespace DesktopTools.Extras;

public sealed partial class ScreenshotEditorWindow
{
    private AutoRedactOptions _redactOptions = new();
    private RedactionReviewState _reviewState = new();
    private RedactionStyle _redactionStyle;
    private CancellationTokenSource? _analysisCancellation;
    private readonly CancellationTokenSource _exportCancellation = new();
    internal CancellationToken ExportCancellationToken { get; private set; }
    private bool _reviewClosed, _reviewInitialized, _reviewBeforeOutput, _exporting;
    private long _reviewDocumentRevision;
    private int _resizeHandle = -1;
    private ListBox _findings = null!;
    private WrapPanel _selectionActions = null!;
    private TextBlock _scanStatus = null!;
    private Button _findButton = null!, _hideAllButton = null!, _acceptButton = null!, _ignoreButton = null!, _skipButton = null!, _cancelScan = null!;
    private readonly List<Button> _exportButtons = [];
    private Func<BitmapSource, ScreenshotExportAction, Task<bool>>? _exportAsync;
    internal Func<BitmapSource, string, IReadOnlyCollection<string>, CancellationToken, Task<IReadOnlyList<SensitiveFinding>>> AnalyzeSensitiveData { get; set; }
        = (image, language, categories, token) => SensitiveDataAnalyzer.AnalyzeAsync(image, language, categories, token);

    private void InitializeRedaction(StackPanel properties, WrapPanel tools, StackPanel actions, AutoRedactOptions? options,
        bool automaticReview, Func<BitmapSource, ScreenshotExportAction, Task<bool>>? exportAsync)
    {
        _redactOptions = options == null ? new() : JsonSerializer.Deserialize<AutoRedactOptions>(JsonSerializer.Serialize(options))!;
        _redactOptions.Normalize(); _redactionStyle = Enum.Parse<RedactionStyle>(_redactOptions.Style);
        _reviewState = new(automaticReview); _reviewBeforeOutput = automaticReview; _exportAsync = exportAsync;
        ExportCancellationToken = _exportCancellation.Token;
        _exportButtons.AddRange(actions.Children.OfType<Button>());
        _findButton = Ui.Button(L.T("Check screenshot"), async () => await FindSensitiveDataAsync());
        _findButton.Name = "FindSensitiveData"; _findButton.Content = Ui.IconLabel("ScanText", L.T("Check screenshot")); tools.Children.Add(_findButton);
        var pane = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        pane.Children.Add(Ui.Text(L.T("Hide private data"), 16, true));
        pane.Children.Add(Ui.Text(L.T("Check the screenshot, then hide all or choose areas."), 12, muted: true));
        _scanStatus = Ui.Text(L.T("Nothing hidden yet."), 12, muted: true); _scanStatus.Margin = new Thickness(0, 10, 0, 10); pane.Children.Add(_scanStatus);
        _hideAllButton = Ui.Button(L.T("Hide all found"), () => HideSensitiveData(_reviewState.Findings.ToArray()), true);
        _hideAllButton.Name = "HideAllSensitiveData"; _hideAllButton.Content = Ui.IconLabel("Redaction", L.T("Hide all found"), primary: true);
        _hideAllButton.Margin = new Thickness(0, 0, 0, 8); pane.Children.Add(_hideAllButton);
        _acceptButton = Ui.Button(L.T("Hide selected"), AcceptSensitiveData); _acceptButton.Name = "AcceptSensitiveData";
        _ignoreButton = Ui.Button(L.T("Keep selected visible"), IgnoreSensitiveData); _ignoreButton.Name = "IgnoreSensitiveData";
        var choices = new WrapPanel(); _selectionActions = choices;
        choices.Children.Add(_acceptButton); choices.Children.Add(_ignoreButton); pane.Children.Add(choices);
        _skipButton = Ui.Button(L.T("Keep remaining visible"), () => { _analysisCancellation?.Cancel(); _reviewState.SkipRemaining(); _scanStatus.Text = L.T("Remaining areas stay visible. Check the image before sharing."); RefreshFindings(); });
        _skipButton.Margin = new Thickness(0, 8, 0, 8); pane.Children.Add(_skipButton);
        _cancelScan = Ui.Button(L.T("Cancel analysis"), () => { _analysisCancellation?.Cancel(); _reviewState.Invalidate(); _scanStatus.Text = L.T("Check cancelled. Check again or continue without checking."); RefreshFindings(); }); pane.Children.Add(_cancelScan);
        var manual = Ui.Button(L.T("Add cover manually"), () => { if (_showingOriginal) ToggleOriginal(); CommitText(); CancelGesture(); _tool = "Redaction"; Update(); });
        manual.SetResourceReference(BorderBrushProperty, "Divider");
        manual.Content = Ui.IconLabel("Redaction", L.T("Add cover manually")); manual.Margin = new Thickness(0, 0, 0, 8); pane.Children.Add(manual);
        var optionsPane = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        optionsPane.Children.Add(Ui.Text(L.T("Text language"), 12, true));
        var languageChoice = new ComboBox { ItemsSource = LocalOcr.Languages, MinWidth = 140, Margin = new Thickness(0, 0, 0, 8) };
        languageChoice.SelectedItem = LocalOcr.SelectLanguage(LocalOcr.Languages, _redactOptions.Language);
        System.Windows.Automation.AutomationProperties.SetName(languageChoice, L.T("OCR language"));
        languageChoice.SelectionChanged += (_, _) => { if (languageChoice.SelectedItem is OcrLanguage language) { _redactOptions.Language = language.Tag; _analysisCancellation?.Cancel(); _reviewState.Invalidate(); RefreshReviewControls(); } };
        optionsPane.Children.Add(languageChoice);
        optionsPane.Children.Add(Ui.Text(L.T("Mixed text is also checked with installed English and app-language OCR."), 11, muted: true));
        optionsPane.Children.Add(Ui.Text(L.T("Redaction style"), 12, true));
        var style = Ui.Choice(new[] { "Solid", "Pixelate", "Blur" }, _redactOptions.Style, _ => { });
        style.Name = "RedactionStyleChoice"; style.Margin = new Thickness(0, 4, 0, 8);
        var template = new DataTemplate(); var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding { Converter = new StyleLabelConverter() }); template.VisualTree = text; style.ItemTemplate = template;
        System.Windows.Automation.AutomationProperties.SetName(style, L.T("Redaction style"));
        style.SelectionChanged += (_, _) =>
        {
            _redactionStyle = Enum.Parse<RedactionStyle>((string)style.SelectedItem);
            var selected = _document.Items.FirstOrDefault(a => a.Id == _drawing.SelectedId);
            if (selected?.Kind == AnnotationKind.Redaction) { _document.Replace(selected with { RedactionStyle = _redactionStyle }); Update(); }
        };
        optionsPane.Children.Add(style);
        optionsPane.Children.Add(Ui.Text(L.T("Solid cover removes visible text. Blur and pixelation offer weaker hiding."), 11, muted: true));
        _findings = new ListBox { Name = "SensitiveFindings", SelectionMode = SelectionMode.Extended, MaxHeight = 190, Margin = new Thickness(0, 10, 0, 8), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        _findings.SetResourceReference(BackgroundProperty, "Card"); _findings.SetResourceReference(ForegroundProperty, "Text"); _findings.BorderThickness = new Thickness(0);
        ScrollViewer.SetHorizontalScrollBarVisibility(_findings, ScrollBarVisibility.Disabled);
        var frame = new FrameworkElementFactory(typeof(Border)); frame.Name = "FindingFrame";
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(9)); frame.SetValue(Border.PaddingProperty, new Thickness(10)); frame.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        frame.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent); frame.SetValue(Border.BorderBrushProperty, System.Windows.Media.Brushes.Transparent);
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding("Content") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent }); frame.AppendChild(content);
        var itemTemplate = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = frame };
        foreach (var property in new[] { ListBoxItem.IsSelectedProperty, ListBoxItem.IsKeyboardFocusWithinProperty })
        {
            var trigger = new Trigger { Property = property, Value = true };
            trigger.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Accent"), "FindingFrame"));
            if (property == ListBoxItem.IsSelectedProperty) trigger.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Selected"), "FindingFrame"));
            itemTemplate.Triggers.Add(trigger);
        }
        var itemStyle = new Style(typeof(ListBoxItem)); itemStyle.Setters.Add(new Setter(Control.TemplateProperty, itemTemplate)); _findings.ItemContainerStyle = itemStyle;
        _findings.SelectionChanged += (_, _) => { _drawing.SelectedFinding = (_findings.SelectedItem as ListBoxItem)?.Tag is SensitiveFinding f ? f.Id : null; RefreshReviewControls(); };
        pane.Children.Add(_findings);
        // Keep the result list directly below the primary action. Secondary
        // actions must not push all findings outside a short window's viewport.
        foreach (var action in new UIElement[] { choices, _skipButton, manual }) { pane.Children.Remove(action); pane.Children.Add(action); }
        var optionsButton = Ui.Button(L.T("Detection options"), () => optionsPane.Visibility = optionsPane.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible);
        optionsButton.SetResourceReference(BorderBrushProperty, "Divider");
        optionsButton.Content = Ui.IconLabel("Settings", L.T("Detection options")); optionsButton.Margin = new Thickness(0, 8, 0, 0);
        pane.Children.Add(optionsButton); pane.Children.Add(optionsPane);
        // Two clearly separate jobs share the side panel: styling what you draw, and hiding private data.
        var drawPane = new StackPanel();
        foreach (var child in properties.Children.Cast<UIElement>().ToArray()) { properties.Children.Remove(child); drawPane.Children.Add(child); }
        var stylePane = BuildStylePane();
        var tabs = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        for (int i = 0; i < 3; i++) tabs.ColumnDefinitions.Add(new ColumnDefinition());
        var drawTab = new RadioButton { Content = L.T("Draw"), GroupName = "EditorPanel", Margin = new Thickness(0, 0, 3, 0) };
        var styleTab = new RadioButton { Content = L.T("Style"), GroupName = "EditorPanel", Margin = new Thickness(3, 0, 3, 0) };
        var privacyTab = new RadioButton { Content = L.T("Hide data"), GroupName = "EditorPanel", Margin = new Thickness(3, 0, 0, 0) };
        var tabList = new[] { drawTab, styleTab, privacyTab };
        for (int i = 0; i < tabList.Length; i++)
        {
            tabList[i].SetResourceReference(StyleProperty, "ModeTab"); tabList[i].FontSize = 12; tabList[i].Padding = new Thickness(4, 8, 4, 8); Grid.SetColumn(tabList[i], i); tabs.Children.Add(tabList[i]);
            System.Windows.Automation.AutomationProperties.SetName(tabList[i], (string)tabList[i].Content);
        }
        void ShowPanel(int index)
        {
            drawPane.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            stylePane.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            pane.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (tabList[index].IsChecked != true) tabList[index].IsChecked = true;
            if (index == 1) RefreshStylePreview();
        }
        void ShowPrivacy(bool privacy) { if (privacy) ShowPanel(2); else if (pane.Visibility == Visibility.Visible) ShowPanel(0); }
        drawTab.Checked += (_, _) => { if (_tool == "Redaction") { _tool = "Pen"; Update(); } ShowPanel(0); };
        styleTab.Checked += (_, _) => ShowPanel(1);
        privacyTab.Checked += (_, _) => ShowPanel(2);
        _showPrivacyTab = ShowPrivacy;
        properties.Children.Add(tabs); properties.Children.Add(drawPane); properties.Children.Add(stylePane); properties.Children.Add(pane);
        ShowPanel(automaticReview ? 2 : 0);
        _reviewDocumentRevision = _document.Revision; _reviewInitialized = true;
        if (automaticReview) Loaded += async (_, _) => await FindSensitiveDataAsync();
        RefreshFindings();
    }

    internal async Task FindSensitiveDataAsync()
    {
        if (_reviewClosed || _reviewState.IsScanning || _exporting) return;
        _showPrivacyTab?.Invoke(true);
        if (_showingOriginal) ToggleOriginal(); CommitText(); CancelGesture(); UpdateRedactionReview();
        _analysisCancellation?.Cancel(); _analysisCancellation?.Dispose();
        var cancellation = new CancellationTokenSource(); _analysisCancellation = cancellation;
        long revision = _reviewState.BeginScan(), imageRevision = _document.Revision;
        _scanStatus.Text = L.T("Analyzing this image locally…"); RefreshFindings();
        try
        {
            var languages = LocalOcr.Languages; var language = LocalOcr.SelectLanguage(languages, _redactOptions.Language);
            if (language == null) { _reviewState.FailScan(revision); _scanStatus.Text = L.T("No Windows OCR languages are installed. Add a language pack in Windows Settings → Time & language → Language & region, then reopen this window."); return; }
            _scanStatus.Text = L.F($"Checking text ({language.Name})…");
            var image = _document.Export(); var crop = _document.Crop; var analyze = AnalyzeSensitiveData;
            var result = await Task.Run(() => analyze(image, language.Tag, _redactOptions.Categories, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_reviewClosed || imageRevision != _document.Revision) return;
            var full = result.Select(f => f with { Bounds = new(f.Bounds.X + crop.X, f.Bounds.Y + crop.Y, f.Bounds.Width, f.Bounds.Height) }).ToArray();
            if (!_reviewState.CompleteScan(revision, full)) return;
            _scanStatus.Text = full.Length == 0 ? L.T("Nothing found. Check the image or add a cover manually.") : L.F($"Found areas: {full.Length}");
        }
        catch (OperationCanceledException) { if (!_reviewClosed) _reviewState.FailScan(revision); }
        catch (SensitiveDataAnalysisException ex) { if (!_reviewClosed) { _reviewState.FailScan(revision); _scanStatus.Text = ex.Message; } }
        catch (Exception) { if (!_reviewClosed) { _reviewState.FailScan(revision); _scanStatus.Text = L.T("Could not analyze this image. Try again, add covers manually, or ignore this check."); } }
        finally { if (!_reviewClosed) RefreshFindings(); }
    }

    private const int CoverPadding = 3;
    private void AcceptSensitiveData()
    {
        HideSensitiveData(SelectedFindings());
    }
    private void HideSensitiveData(IReadOnlyList<SensitiveFinding> selected)
    {
        if (selected.Count == 0 || _reviewState.IsScanning || _exporting) return;
        CommitText(); CancelGesture();
        _document.AddRange(selected.Select(f => new Annotation { Kind = AnnotationKind.Redaction, RedactionStyle = _redactionStyle, Points = [new(Math.Max(0, f.Bounds.X - CoverPadding), Math.Max(0, f.Bounds.Y - CoverPadding)), new(Math.Min(_image.PixelWidth, f.Bounds.X + f.Bounds.Width + CoverPadding), Math.Min(_image.PixelHeight, f.Bounds.Y + f.Bounds.Height + CoverPadding))] }).ToArray());
        _reviewDocumentRevision = _document.Revision; _reviewState.Resolve(selected.Select(f => f.Id).ToArray());
        _tool = "Select"; _drawing.SelectedId = null; // finished covers read as plain black bars, not as one selected box with handles
        _scanStatus.Text = L.F($"Hidden areas: {selected.Count}. Undo restores them."); RefreshFindings(); Update();
    }
    private SensitiveFinding[] SelectedFindings() => _findings.SelectedItems.OfType<ListBoxItem>().Select(i => (SensitiveFinding)i.Tag).ToArray();
    private void IgnoreSensitiveData() { _reviewState.Resolve(SelectedFindings().Select(f => f.Id).ToArray()); RefreshFindings(); }
    internal static string CategoryLabel(string category) => category switch
    { "email" => "Email addresses", "phone" => "Phone numbers", "ip" => "IP addresses", "path" => "Local file paths", "credential" => "Keys, tokens and passwords", "username" => "Usernames", "account" => "Account IDs", "serial" => "Serial numbers", _ => "Sensitive data" };
    private void RefreshFindings()
    {
        _findings.Items.Clear();
        int number = 0;
        foreach (var finding in _reviewState.Findings)
        {
            var row = new StackPanel(); row.Children.Add(Ui.Text(L.F($"Area {++number}"), 12, true));
            row.Children.Add(Ui.Text(string.Join(", ", finding.Categories.Select(c => L.T(CategoryLabel(c)))), 11, muted: true));
            _findings.Items.Add(new ListBoxItem { Tag = finding, Content = row });
        }
        _drawing.Suggestions = _reviewState.Findings; _drawing.SelectedFinding = null;
        RefreshReviewControls();
    }
    private void RefreshReviewControls()
    {
        if (!_reviewInitialized || _reviewClosed) return;
        _findButton.IsEnabled = !_reviewState.IsScanning && !_exporting;
        _hideAllButton.IsEnabled = _reviewState.HasUnresolved && !_reviewState.IsScanning && !_exporting;
        _findings.Visibility = _reviewState.HasUnresolved ? Visibility.Visible : Visibility.Collapsed;
        _selectionActions.Visibility = _findings.SelectedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _acceptButton.IsEnabled = _ignoreButton.IsEnabled = _findings.SelectedItems.Count > 0 && !_reviewState.IsScanning && !_exporting;
        _skipButton.IsEnabled = !_reviewState.IsScanning && !_exporting && (_reviewState.NeedsScan || _reviewState.HasUnresolved);
        _skipButton.Visibility = _reviewState.NeedsScan || _reviewState.HasUnresolved ? Visibility.Visible : Visibility.Collapsed;
        _skipButton.Content = L.T(_reviewState.NeedsScan ? "Continue without checking" : "Keep remaining visible");
        _cancelScan.Visibility = _reviewState.IsScanning ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in _exportButtons) button.IsEnabled = _reviewState.CanExport && !_exporting;
        _drawing.InvalidateVisual();
    }
    private void UpdateRedactionReview()
    {
        if (!_reviewInitialized || _reviewClosed || _reviewDocumentRevision == _document.Revision) return;
        _reviewDocumentRevision = _document.Revision; _analysisCancellation?.Cancel(); _reviewState.Invalidate();
        if (_reviewState.NeedsScan) _scanStatus.Text = L.T("Image changed. Scan again or explicitly ignore this check.");
        RefreshFindings();
    }
    private void CloseRedactionReview()
    {
        _reviewClosed = true; _reviewState.Close();
        _exportCancellation.Cancel(); _exportCancellation.Dispose();
        _analysisCancellation?.Cancel(); _analysisCancellation?.Dispose(); _analysisCancellation = null;
    }

    private static Point[] RedactionHandles(Rect r) => [r.TopLeft, new(r.X + r.Width / 2, r.Y), r.TopRight, new(r.Right, r.Y + r.Height / 2), r.BottomRight, new(r.X + r.Width / 2, r.Bottom), r.BottomLeft, new(r.X, r.Y + r.Height / 2)];
    private bool TryBeginRedactionResize(Point point)
    {
        if (_tool != "Select") return false;
        var selected = _document.Items.FirstOrDefault(a => a.Id == _drawing.SelectedId);
        if (selected?.Kind != AnnotationKind.Redaction || selected.Points.Count < 2) return false;
        var handles = RedactionHandles(new Rect(selected.Points[0], selected.Points[^1]));
        _resizeHandle = Array.FindIndex(handles, p => (p - point).Length <= _drawing.HandleRadius * 1.5);
        if (_resizeHandle < 0) return false;
        _movingOriginal = selected; _points.Add(point); _gesture = true; _drawing.Pending = selected; _surface.CaptureMouse(); return true;
    }
    private bool ResizeRedaction(Point point)
    {
        if (_resizeHandle < 0 || _movingOriginal == null) return false;
        var r = new Rect(_movingOriginal.Points[0], _movingOriginal.Points[^1]);
        double left = r.Left, right = r.Right, top = r.Top, bottom = r.Bottom;
        if (_resizeHandle is 0 or 6 or 7) left = Math.Min(point.X, right - 1);
        if (_resizeHandle is 2 or 3 or 4) right = Math.Max(point.X, left + 1);
        if (_resizeHandle is 0 or 1 or 2) top = Math.Min(point.Y, bottom - 1);
        if (_resizeHandle is 4 or 5 or 6) bottom = Math.Max(point.Y, top + 1);
        _drawing.Pending = _movingOriginal with { Points = [new(left, top), new(right, bottom)] }; _drawing.InvalidateVisual(); return true;
    }
    private sealed class StyleLabelConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => L.T(value?.ToString() switch { "Solid" => "Solid cover", "Pixelate" => "Pixelate", _ => "Blur" });
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
    }
}
