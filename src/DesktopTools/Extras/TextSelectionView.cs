using DesktopTools.Core;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopTools.Extras;

/// <summary>
/// Shows a picture with its recognized text on top: everything is dimmed except the words, which are outlined and can be selected and copied straight
/// from the picture. Used by "Scan screen text" and, embedded, by the image and screenshot editors (which pass a Back to editing action).
/// The picture itself is never changed; fixed words only live in this view.
/// </summary>
public enum TextCopyKind { Selection, All, Link }

public sealed partial class TextSelectionView : UserControl
{
    private readonly BitmapSource image;
    private readonly Action<string> report;
    private readonly Action<string>? translate;
    private readonly Surface surface;
    private readonly Canvas surfaceHost = new();
    private readonly ScanOverlay scanOverlay = new();
    private readonly Canvas overlayLayer = new() { IsHitTestVisible = false };
    private readonly ComboBox languageChoice;
    private readonly Button copyAll, copySelection, translateButton, backButton;
    private readonly TextBlock status;
    private CancellationTokenSource? scan;

    public TextSelectionView(BitmapSource image, Action<string> report, Action<string>? translate = null, Action? backToEditing = null, string? languageTag = null)
    {
        this.image = image; this.report = report; this.translate = translate;
        System.Windows.Automation.AutomationProperties.SetName(this, L.T("Text view"));
        surface = new Surface(image) { Host = surfaceHost };
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10), VerticalAlignment = VerticalAlignment.Center };
        backButton = Ui.Button(L.T("Back to editing"), () => { Cancel(); backToEditing?.Invoke(); }, true);
        backButton.Content = Ui.IconLabel("Undo", L.T("Back to editing"), primary: true); backButton.Visibility = backToEditing == null ? Visibility.Collapsed : Visibility.Visible;
        toolbar.Children.Add(backButton);
        var languages = LocalOcr.Languages;
        languageChoice = new ComboBox { ItemsSource = languages, SelectedItem = LocalOcr.SelectLanguage(languages, languageTag), MinWidth = 190, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(languageChoice, L.T("OCR language"));
        languageChoice.SelectionChanged += async (_, _) => { if (IsLoaded || scan != null || Model != null) await RecognizeAsync(); };
        toolbar.Children.Add(languageChoice);
        copyAll = Ui.Button(L.T("Copy all"), () => CopyText(AllText, TextCopyKind.All)); copyAll.Content = Ui.IconLabel("Copy", L.T("Copy all"));
        copySelection = Ui.Button(L.T("Copy selection"), () => CopyText(SelectedText, TextCopyKind.Selection)); copySelection.Content = Ui.IconLabel("Copy", L.T("Copy selection"));
        translateButton = Ui.Button(L.T("Translate"), () => TranslateText()); translateButton.Content = Ui.IconLabel("Translate", L.T("Translate"));
        translateButton.Visibility = translate == null ? Visibility.Collapsed : Visibility.Visible;
        toolbar.Children.Add(copyAll); toolbar.Children.Add(copySelection); toolbar.Children.Add(translateButton);

        overlayLayer.Children.Add(scanOverlay);
        var stage = new Grid { ClipToBounds = true };
        stage.Children.Add(surface); stage.Children.Add(overlayLayer); stage.Children.Add(surfaceHost);
        var stageCard = new Border { Child = stage, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), ClipToBounds = true };
        stageCard.SetResourceReference(Border.BackgroundProperty, "Card"); stageCard.SetResourceReference(Border.BorderBrushProperty, "Stroke");
        status = Ui.Text("", 12, muted: true); status.Margin = new Thickness(2, 10, 0, 0);

        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        root.Children.Add(stageCard);
        Content = root;

        surface.SelectionChanged += RefreshButtons;
        surface.CopyRequested += CopyText;
        surface.Notice += message => report(message);
        if (translate != null) surface.TranslateRequested += TranslateText;
        surface.SizeChanged += (_, _) => UpdateOverlay();
        surface.LayoutUpdated += (_, _) => UpdateOverlay();
        Unloaded += (_, _) => Cancel();
        RefreshButtons();
    }

    // ---- state exposed to the hosts and the tests ---------------------------------------------------------------------------------------------------

    public OcrLayout? Layout { get; private set; }
    public TextSelectionModel? Model { get; private set; }
    public IReadOnlyList<TextLink> Links { get; private set; } = [];
    public string AllText => Model?.GetText(false) ?? "";
    public string SelectedText => Model?.GetText(true) ?? "";
    public bool IsScanning => scan != null;
    internal bool CanCopyAll => copyAll.IsEnabled;
    internal string StatusText => status.Text;
    internal BitmapSource RenderSurface() => surface.Render();
    internal Point ImageToSurface(Point point) => surface.ImageToSurface(point);
    /// <summary>Redraws after the model was changed from code (select all, tests).</summary>
    internal void RefreshSelection() { surface.InvalidateVisual(); RefreshButtons(); }

    // ---- recognition ------------------------------------------------------------------------------------------------------------------------------

    public async Task RecognizeAsync()
    {
        Cancel();
        if (languageChoice.SelectedItem is not OcrLanguage language)
        {
            status.Text = L.T("No Windows OCR languages are installed. Add a language pack in Windows Settings → Time & language → Language & region, then reopen this window.");
            return;
        }
        var cts = scan = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        SetBusy(true); status.Text = L.T("Recognizing text…");
        surface.SetModel(null, []); Model = null; Layout = null; Links = []; UpdateOverlay();
        scanOverlay.Start(image.PixelWidth, image.PixelHeight, Math.Max(8, Math.Min(image.PixelWidth, image.PixelHeight) * 0.015));
        try
        {
            var layout = await LocalOcr.RecognizeWordsAsync(image, language.Tag, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (scan != cts) return;
            var model = new TextSelectionModel(layout); var links = TextLinkDetector.Find(model);
            Layout = layout; Model = model; Links = links; surface.SetModel(model, links);
            status.Text = layout.Words.Count == 0
                ? L.T("No text found. Try another language or a tighter crop.")
                : L.F($"{layout.Words.Count} words found") + "   •   " + L.T("Drag to select text · double-click a word · triple-click a line · Ctrl+A selects all");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (scan == cts) status.Text = L.T("Text recognition unavailable: ") + ex.Message; }
        finally
        {
            if (scan == cts) { scan = null; SetBusy(false); scanOverlay.Stop(); }
            cts.Dispose();
        }
    }

    /// <summary>Stops a running scan (window closed, language changed, Back to editing) without touching the shown result.</summary>
    public void Cancel()
    {
        var running = scan; scan = null;
        try { running?.Cancel(); } catch (ObjectDisposedException) { }
        scanOverlay.Stop(); SetBusy(false);
    }

    private void UpdateOverlay()
    {
        var m = surface.GetMatrix();
        overlayLayer.RenderTransform = new MatrixTransform(m);
        scanOverlay.Width = image.PixelWidth; scanOverlay.Height = image.PixelHeight;
    }

    private void SetBusy(bool busy)
    {
        languageChoice.IsEnabled = !busy; RefreshButtons();
    }

    private void RefreshButtons()
    {
        bool has = Model != null && Model.Count > 0 && scan == null;
        copyAll.IsEnabled = has;
        copySelection.IsEnabled = has && Model!.HasSelection;
        translateButton.IsEnabled = has;
    }

    // ---- actions ------------------------------------------------------------------------------------------------------------------------------------

    private async void CopyText(string text, TextCopyKind kind)
    {
        if (string.IsNullOrEmpty(text)) { status.Text = L.T("Select text first."); return; }
        try
        {
            var result = await ClipboardService.SetTextAsync(text);
            if (!IsLoaded && !result.Success) return;
            if (result.Success)
            {
                status.Text = L.T("Text copied.");
                // Only the button that was pressed confirms; Ctrl+C and the menu confirm the matching button.
                if (kind == TextCopyKind.Selection) Ui.Flash(copySelection); else if (kind == TextCopyKind.All) Ui.Flash(copyAll);
            }
            else { status.Text = result.Error; report(L.T("Could not copy text: ") + result.Error); }
        }
        catch (Exception ex) { report(L.T("Could not copy text: ") + ex.Message); }
    }

    /// <summary>Shows a ready-made layout without running OCR (tests).</summary>
    internal void ShowForTest(OcrLayout layout)
    {
        var model = new TextSelectionModel(layout); Layout = layout; Model = model; Links = TextLinkDetector.Find(model);
        surface.SetModel(model, Links); RefreshButtons();
    }

    private void TranslateText()
    {
        string text = SelectedText.Length > 0 ? SelectedText : AllText;
        if (text.Length == 0) { status.Text = L.T("Select text first."); return; }
        translate?.Invoke(text);
    }
}
