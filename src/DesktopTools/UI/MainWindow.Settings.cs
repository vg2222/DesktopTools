using System.Windows;
using System.Windows.Controls;
using DesktopTools.Core;
using DesktopTools.Localization;

namespace DesktopTools.UI;

internal sealed partial class MainWindow
{
    private IEnumerable<UIElement> FeatureShortcutRows(string groupTitle)
    {
        if (currentPage.StartsWith("Feature:", StringComparison.Ordinal))
        {
            string id = currentPage[8..];
            if (!id.StartsWith("aid-", StringComparison.Ordinal) && groupTitle == L.T(FeatureSettingsSection(id)))
                return FeatureShortcutCatalog.All.Where(entry => entry.FeatureId == id)
                    .Select(entry => (UIElement)ShortcutCatalogView.Row(controller, entry));
            return [];
        }
        string[] ids = currentPage switch
        {
            "Draw" when groupTitle == L.T("Drawing defaults") => ["draw"],
            "Capture" when groupTitle == L.T("Capture behavior") => ["capture"],
            "Capture" when groupTitle == L.T("Latest screenshot") => ["pin"],
            "Laser pointer" when groupTitle == L.T("Laser pointer") => ["laser"],
            "Cursor spotlight" when groupTitle == L.T("Cursor spotlight") => ["spotlight"],
            "Freeze frame" when groupTitle == L.T("Freeze frame") => ["freeze"],
            "Utilities" when groupTitle == L.T("Text tools") => ["translate", "ocr"],
            "Utilities" when groupTitle == L.T("Screen recorder") => ["record"],
            "Utilities" when groupTitle == L.T("Quick actions wheel") => ["wheel"],
            "Utilities" when groupTitle == L.T("Teleprompter") => ["prompter"],
            "Utilities" when groupTitle == L.T("Pin active window") => ["window"],
            "Utilities" when groupTitle == L.T("QR codes") => ["qr"],
            "Utilities" when groupTitle == L.T("Screen eyedropper") => ["color"],
            "Utilities" when groupTitle == L.T("Video editor") => ["video"],
            "Utilities" when groupTitle == L.T("Image tools") => ["images"],
            "Utilities" when groupTitle == L.T("File shelf") => ["files"],
            "Utilities" when groupTitle == L.T("Floating notes") => ["notes"],
            "Utilities" when groupTitle == L.T("Audio controls") => ["audio"],
            _ => []
        };
        return FeatureShortcutCatalog.All.Where(entry => ids.Contains(entry.FeatureId)).Select(entry => (UIElement)ShortcutCatalogView.Row(controller, entry));
    }
}
