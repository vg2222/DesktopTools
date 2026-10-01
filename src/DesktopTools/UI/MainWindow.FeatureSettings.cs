using DesktopTools.Extras;
using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace DesktopTools.UI;

internal sealed partial class MainWindow
{
    private static string FeatureSettingsSection(string id) => id switch
    {
        "capture" => "Capture behavior", "pin" => "Pin screenshot", "guide" => "Step-by-step guide", "draw" => "Drawing defaults",
        "laser" => "Laser pointer", "spotlight" => "Cursor spotlight", "freeze" => "Freeze frame",
        "record" => "Screen recorder", "prompter" => "Teleprompter", "images" => "Image tools",
        "video" => "Video editor", "color" => "Screen eyedropper", "ocr" => "Scan screen text",
        "translate" => "Local translation", "qr" => "QR codes", "notes" => "Floating notes",
        "files" => "File shelf", "audio" => "Audio controls", "wheel" => "Quick actions wheel",
        "window" => "Pin active window", _ => id
    };

    private void FeatureSettings(string id)
    {
        var tool = DashboardTools().FirstOrDefault(item => item.Id == id);
        if (tool is null) { Navigate("Home"); return; }
        var back = new TextBlock { Margin = new Thickness(0, 0, 0, 14) };
        var category = new Hyperlink { TextDecorations = null, Cursor = Cursors.Hand };
        category.SetResourceReference(TextElement.ForegroundProperty, "Muted");
        category.Inlines.Add(new InlineUIContainer(Ui.Icon("Chevron", 14)) { BaselineAlignment = BaselineAlignment.Center });
        category.Inlines.Add(new Run("  " + L.T(tool.Group)));
        category.Click += (_, _) => Navigate(tool.Group);
        System.Windows.Automation.AutomationProperties.SetName(category, L.T(tool.Group));
        back.Inlines.Add(category); page.Children.Add(back);
        PageBanner(tool.Title, tool.Detail, tool.Icon);

        if (id.StartsWith("aid-", StringComparison.Ordinal))
        {
            AidSettings(id[4..]);
            return;
        }
        if (id == "automation") { Group(L.T("Desktop Automation"), Ui.Row(L.T("Build and run local desktop actions."), L.T("Schedules run while DesktopTools is open. Window triggers use part of a title."), Ui.Button(L.T("Open"), controller.OpenAutomation))); return; }
        if (id == "pin")
        {
            Group(L.T("Pin screenshot"),
                Ui.Row(L.T("Pin above applications"), L.T("Resize it or adjust its opacity."), Ui.Button(L.T("Pin screenshot"), controller.PinLast)));
            return;
        }
        if (id == "guide")
        {
            Group(L.T("Step-by-step guide"),
                Ui.Row(L.T("Build a guide"), L.T("Arrange screenshots, write captions, and export one PNG."),
                    Ui.Button(L.T("Open guide builder"), controller.OpenStepGuide)));
            return;
        }
        if (id == "capture")
        {
            visibleFeatureSections = [L.T("Capture behavior"), L.T("Sensitive data"), L.T("Capture again")];
            Capture();
            return;
        }
        if (id == "draw") { Draw(); return; }
        if (id is "laser" or "spotlight" or "freeze")
        {
            presentationSection = L.T(FeatureSettingsSection(id));
            Present();
            return;
        }
        if (id == "translate")
        {
            var settings = controller.Settings;
            Group(L.T("Local translation"),
                Ui.Row(L.T("Translation direction"), null,
                    new TranslationLanguagePicker(settings.TranslationDirection, value => Change(state => state.TranslationDirection = value))),
                Ui.Row(L.T("Open text tools"), L.T("Review, copy or translate recognized text."),
                    Ui.Button(L.T("Open"), controller.OpenTextToolsWindow)));
            return;
        }
        if (id == "ocr")
        {
            var languages = LocalOcr.Languages;
            var rows = new List<UIElement>();
            if (languages.Count > 0)
            {
                var choice = new ComboBox { ItemsSource = languages,
                    SelectedItem = LocalOcr.SelectLanguage(languages, controller.Settings.ScreenTextLanguage),
                    MinWidth = 180 };
                choice.SelectionChanged += (_, _) =>
                {
                    if (choice.SelectedItem is OcrLanguage selected)
                        Change(state => state.ScreenTextLanguage = selected.Tag);
                };
                rows.Add(Ui.Row(L.T("Screen text language"), L.T("Uses installed Windows OCR language packs."), choice));
            }
            else rows.Add(Ui.Text(L.T("No Windows OCR languages are installed. Add a language pack in Windows Settings → Time & language → Language & region, then reopen this window."), 13));
            rows.Add(Ui.Row(L.T("Scan screen text"), L.T("Recognize text (OCR)"),
                Ui.Button(L.T("Scan screen text"), () => { HideImmediatelyForCapture(); _ = controller.CaptureAsync(textOnly: true); })));
            Group(L.T("Scan screen text"), rows.ToArray());
            return;
        }
        visibleFeatureSections = [L.T(FeatureSettingsSection(id))];
        Utilities();
    }
}
