using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Localization;
using DesktopTools.Native;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopTools.UI;

internal sealed partial class MainWindow
{
    private void Diagnostics()
    {
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var rerun = Ui.Button(L.T("Run checks again"), () => Navigate("Diagnostics"));
        rerun.Content = Ui.IconLabel("Refresh", L.T("Run checks again"));
        rerun.Tag = "diagnostics-rerun";
        DockPanel.SetDock(rerun, Dock.Right);
        heading.Children.Add(rerun);
        heading.Children.Add(Ui.Text(L.T("Checks run locally and do not change your files or settings."), 13, muted: true));
        page.Children.Add(heading);

        var missingRuntime = VisualCppRuntime.FindMissingFiles();
        var missingMedia = NativeDependencyDiagnostics.FindMissingMediaFoundationFiles();
        IReadOnlyList<OcrLanguage> ocrLanguages;
        string? ocrError = null;
        try { ocrLanguages = LocalOcr.Languages; }
        catch (Exception ex) { ocrLanguages = []; ocrError = ex.Message; }

        var requested = FeatureShortcutCatalog.Bindings(controller.Settings);
        var unavailable = controller.UnavailableShortcuts;
        var registered = controller.RegisteredShortcuts;
        var inactive = requested.Keys.Where(action => !registered.ContainsKey(action)).ToArray();
        int issues = (missingRuntime.Count > 0 ? 1 : 0) + (missingMedia.Count > 0 ? 1 : 0)
            + (ocrLanguages.Count == 0 ? 1 : 0) + (inactive.Length > 0 ? 1 : 0);
        var summary = Ui.Text(issues == 0 ? L.T("All checks look ready.") : L.F($"{issues} checks need attention."), 16, true);
        summary.Margin = new Thickness(0, 0, 0, 12);
        page.Children.Add(summary);

        var runtimeDetail = missingRuntime.Count == 0
            ? L.T("Required files found for screen recording, offline translation and background removal.")
            : L.T("Recording, offline translation and background removal need this runtime.")
                + "\n" + L.T("Missing files: ") + string.Join(", ", missingRuntime);
        page.Children.Add(DiagnosticCard("Microsoft Visual C++ x64", missingRuntime.Count == 0, runtimeDetail, "Check",
            missingRuntime.Count == 0 ? null : Ui.Button(L.T("Open Microsoft download"),
                () => OpenDiagnosticLink(VisualCppRuntime.HelpUri.AbsoluteUri))));

        var mediaDetail = missingMedia.Count == 0
            ? L.T("Windows Media Foundation files were found. Recording and video editing still need a real device and codec check.")
            : L.T("Recording and video editing need Windows media components. Windows N editions may need the Media Feature Pack.")
                + "\n" + L.T("Missing files: ") + string.Join(", ", missingMedia);
        page.Children.Add(DiagnosticCard("Windows Media Foundation", missingMedia.Count == 0, mediaDetail, "Video",
            missingMedia.Count == 0 ? null : Ui.Button(L.T("Media Feature Pack help"),
                () => OpenDiagnosticLink(NativeDependencyDiagnostics.MediaFeaturePackHelpUri.AbsoluteUri))));

        var ocrDetail = ocrLanguages.Count > 0
            ? L.F($"{ocrLanguages.Count} Windows OCR languages available.") + " "
                + string.Join(", ", ocrLanguages.Take(4).Select(language => language.Name))
            : L.T("No Windows OCR languages are available. Add a language pack in Windows Settings.")
                + (ocrError == null ? "" : "\n" + ocrError);
        page.Children.Add(DiagnosticCard("Windows OCR", ocrLanguages.Count > 0, ocrDetail, "ScanText",
            ocrLanguages.Count > 0 ? null : Ui.Button(L.T("Open language settings"),
                () => OpenDiagnosticLink("ms-settings:regionlanguage"))));

        var shortcutDetail = inactive.Length == 0
            ? L.F($"{registered.Count} of {requested.Count} enabled shortcuts are active.")
            : L.F($"{registered.Count} of {requested.Count} enabled shortcuts are active.") + "\n"
                + L.T("Close another app using these keys, retry, or choose a different shortcut.");
        var shortcutActions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var retry = Ui.Button(L.T("Retry shortcuts"), () => { controller.RetryShortcuts(); Navigate("Diagnostics"); });
        retry.Tag = "diagnostics-retry-shortcuts";
        shortcutActions.Children.Add(retry);
        var settings = Ui.Button(L.T("Open shortcut settings"), () => Navigate("Shortcuts"));
        settings.Tag = "diagnostics-shortcuts";
        shortcutActions.Children.Add(settings);
        var shortcutCard = DiagnosticCard("Global shortcuts", inactive.Length == 0, shortcutDetail, "Shortcuts", shortcutActions);
        if (inactive.Length > 0)
        {
            var list = (StackPanel)shortcutCard.Child;
            foreach (var action in inactive)
            {
                var entry = FeatureShortcutCatalog.All.FirstOrDefault(item => item.Action == action);
                var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
                if (entry != null)
                {
                    var open = Ui.Button(L.T("Feature settings"), () => Navigate("Feature:" + entry.FeatureId));
                    open.Tag = "diagnostics-feature-" + entry.FeatureId;
                    DockPanel.SetDock(open, Dock.Right);
                    row.Children.Add(open);
                }
                string name = entry == null ? action : L.T(entry.Title);
                string reason = unavailable.TryGetValue(action, out var error) ? " · " + error : "";
                row.Children.Add(Ui.Text(name + " · " + requested[action] + reason, 12));
                list.Children.Add(row);
            }
        }
        page.Children.Add(shortcutCard);
        var boundary = Ui.Text(L.T("These checks detect missing files and shortcut registration. They do not test capture quality, codecs or what a screen-sharing viewer sees."), 12, muted: true);
        boundary.Margin = new Thickness(0, 2, 0, 8);
        page.Children.Add(boundary);
    }

    private static Border DiagnosticCard(string title, bool ready, string detail, string icon, FrameworkElement? action)
    {
        var content = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
        var badge = Ui.Text(L.T(ready ? "Ready" : "Needs attention"), 12, true);
        badge.SetResourceReference(TextBlock.ForegroundProperty, ready ? "Accent" : "Text");
        DockPanel.SetDock(badge, Dock.Right); header.Children.Add(badge);
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        var symbol = Ui.Icon(icon, 18); symbol.Margin = new Thickness(0, 0, 9, 0);
        label.Children.Add(symbol); label.Children.Add(Ui.Text(L.T(title), 15, true)); header.Children.Add(label);
        content.Children.Add(header);
        content.Children.Add(Ui.Text(detail, 12, muted: true));
        if (action != null) { action.Margin = new Thickness(0, 12, 0, 0); action.HorizontalAlignment = HorizontalAlignment.Left; content.Children.Add(action); }
        return Ui.Card(content, 17);
    }

    private void OpenDiagnosticLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { controller.Report(L.T("Could not open help: ") + ex.Message, NotificationKind.Warning); }
    }
}
