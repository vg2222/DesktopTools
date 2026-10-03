using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;

namespace DesktopTools.UI;

internal sealed partial class MainWindow
{
    private void AutoRedactSettings()
    {
        var options = controller.Settings.AutoRedact;
        var toggle = Ui.Toggle(options.Enabled, value => Change(settings => settings.AutoRedact.Enabled = value)); toggle.Name = "AutoRedactEnabled";
        var rows = new List<UIElement>
        {
            Ui.Row(L.T("Check screenshots before copying or saving"), L.T("Review possible sensitive data in the screenshot editor before output."), toggle),
            Ui.Row(L.T("Default redaction style"), L.T("Solid cover is the strongest option."), Ui.Choice(new[] { "Solid", "Pixelate", "Blur" }, options.Style, value => Change(settings => settings.AutoRedact.Style = value)))
        };
        var languages = LocalOcr.Languages;
        if (languages.Count > 0)
        {
            var choice = new ComboBox { ItemsSource = languages, SelectedItem = LocalOcr.SelectLanguage(languages, options.Language), MinWidth = 160 };
            choice.SelectionChanged += (_, _) => { if (choice.SelectedItem is OcrLanguage language) Change(settings => settings.AutoRedact.Language = language.Tag); };
            rows.Add(Ui.Row(L.T("OCR language"), L.T("Uses installed Windows OCR language packs."), choice));
        }
        else rows.Add(Ui.Text(L.T("No Windows OCR languages are installed. Add a language pack in Windows Settings → Time & language → Language & region, then reopen this window."), 12, muted: true));
        foreach (string category in AutoRedactOptions.AvailableCategories)
            rows.Add(Ui.Row(L.T(ScreenshotEditorWindow.CategoryLabel(category)), null,
                Ui.Toggle(options.Categories.Contains(category), enabled => Change(settings => settings.AutoRedact.Categories = enabled
                    ? settings.AutoRedact.Categories.Append(category).Distinct().ToArray()
                    : settings.AutoRedact.Categories.Where(c => c != category).ToArray()))));
        rows.Add(Ui.Text(L.T("Use Check screenshot in the editor, then Hide all found."), 12, muted: true));
        Group(L.T("Sensitive data"), rows.ToArray());
    }
}
