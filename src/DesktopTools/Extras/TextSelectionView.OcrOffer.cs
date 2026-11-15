using DesktopTools.Core;
using DesktopTools.Localization;
using DesktopTools.UI;
using System.Windows;
using System.Windows.Controls;

namespace DesktopTools.Extras;

/// <summary>
/// Offers the optional DesktopTools OCR pack from the text view: download it when it is missing (licence first, in the consent window), or switch
/// the Text reader to it when it is already on this PC. Nothing downloads without the person's click and licence acceptance.
/// </summary>
public sealed partial class TextSelectionView
{
    private Button? ocrOffer;
    private CancellationTokenSource? offerDownload;
    private bool refreshingChoices;

    internal string? OcrOfferText => ocrOffer?.Visibility == Visibility.Visible ? System.Windows.Automation.AutomationProperties.GetName(ocrOffer) : null;

    private Button CreateOcrOffer()
    {
        ocrOffer = Ui.Button(L.T("Better text detection"), async () => await AcceptOcrOfferAsync());
        ocrOffer.Name = "OcrPackOffer"; ocrOffer.Margin = new Thickness(10, 0, 0, 0);
        RefreshOcrOffer();
        return ocrOffer;
    }

    /// <summary>The OCR pack script group that fits the current reading: the chosen pack group, or the alphabet of the chosen Windows language.</summary>
    private OcrPackScript CurrentScript =>
        languageChoice.SelectedItem is OcrLanguage language
            ? OcrPackPipeline.TryParseTag(language.Tag, out var script) ? script : OcrPackPipeline.ScriptForLanguage(language.Tag)
            : OcrPackPipeline.ScriptForLanguage(LocalOcr.Preference.WindowsTag);

    private void RefreshOcrOffer()
    {
        if (ocrOffer == null) return;
        if (LocalOcr.ChooseEngine == null || offerDownload != null) { ocrOffer.Visibility = offerDownload != null ? Visibility.Visible : Visibility.Collapsed; return; }
        var missing = OcrPackEngine.MissingPacks(CurrentScript);
        bool packAvailable = OcrPackEngine.Pack(OcrPackPipeline.DetectorPackId) != null;
        string? label = !packAvailable ? null
            : missing.Count > 0 ? L.F($"Better text detection ({ModelPacks.FormatSize(missing.Sum(pack => pack.TotalBytes))})")
            : !LocalOcr.PackEngineChosen ? L.T("Use the OCR pack")
            : null;
        ocrOffer.Visibility = label == null ? Visibility.Collapsed : Visibility.Visible;
        if (label == null) return;
        ocrOffer.Content = Ui.IconLabel("Star", label);
        System.Windows.Automation.AutomationProperties.SetName(ocrOffer, label);
        Ui.Tip(ocrOffer, missing.Count > 0
            ? L.T("Download the optional DesktopTools OCR pack. It reads small text more accurately and runs only on this PC.")
            : L.T("The OCR pack is on this PC. Read text with it instead of Windows OCR."));
    }

    private async Task AcceptOcrOfferAsync()
    {
        if (offerDownload != null) { offerDownload.Cancel(); return; }
        var script = CurrentScript;
        var packs = OcrPackEngine.MissingPacks(script);
        if (packs.Count > 0 && !await DownloadOcrPacksAsync(packs)) return;
        // Read with the pack from now on (the same Text reader setting as Text tools), in the script group that fits this picture's language.
        LocalOcr.ChooseEngine?.Invoke("Pack", OcrPackPipeline.Key(script));
        RefreshLanguageChoices(OcrPackPipeline.Tag(script));
        RefreshOcrOffer();
        await RecognizeAsync();
    }

    private async Task<bool> DownloadOcrPacksAsync(IReadOnlyList<ModelPack> packs)
    {
        var owner = Window.GetWindow(this);
        var consents = ModelPacks.Consents();
        try
        {
            foreach (var pack in packs)
                if (!ModelConsentWindow.Ask(owner, pack, consents)) { status.Text = L.T("Download canceled."); return false; }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { status.Text = L.T("Could not download the OCR pack: ") + ex.Message; return false; }
        Cancel();
        var cancellation = offerDownload = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        ocrOffer!.Content = Ui.IconLabel("Close", L.T("Cancel download"));
        System.Windows.Automation.AutomationProperties.SetName(ocrOffer, L.T("Cancel download"));
        languageChoice.IsEnabled = false;
        long total = Math.Max(1, packs.Sum(pack => pack.TotalBytes)), before = 0;
        try
        {
            foreach (var pack in packs)
            {
                long done = before, size = pack.TotalBytes;
                var progress = new Progress<double>(value =>
                {
                    if (offerDownload != cancellation) return;
                    long bytes = done + (long)(Math.Clamp(value, 0, 1) * size);
                    status.Text = L.F($"Downloading the OCR pack… {ModelPacks.FormatSize(bytes)} of {ModelPacks.FormatSize(total)}");
                });
                await ModelPacks.InstallAsync(pack, consents, progress, cancellation.Token);
                before += size;
            }
            status.Text = L.T("OCR pack ready. Text is read on this PC.");
            return true;
        }
        catch (OperationCanceledException) { status.Text = L.T("Download canceled."); return false; }
        catch (Exception ex) { status.Text = L.T("Could not download the OCR pack: ") + ex.Message; return false; }
        finally
        {
            offerDownload = null; cancellation.Dispose();
            languageChoice.IsEnabled = scan == null;
            RefreshOcrOffer();
        }
    }

    /// <summary>Reloads the language list after the reader changed, selecting <paramref name="tag"/> without starting a second scan.</summary>
    private void RefreshLanguageChoices(string tag)
    {
        refreshingChoices = true;
        try
        {
            var languages = LocalOcr.Choices();
            languageChoice.ItemsSource = languages;
            languageChoice.SelectedItem = LocalOcr.SelectChoice(languages, tag);
        }
        finally { refreshingChoices = false; }
    }

    private void CancelOfferDownload()
    {
        try { offerDownload?.Cancel(); } catch (ObjectDisposedException) { }
    }
}
