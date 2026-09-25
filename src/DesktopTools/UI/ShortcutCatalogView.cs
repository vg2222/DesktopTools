using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using DesktopTools.Core;
using DesktopTools.Localization;

namespace DesktopTools.UI;

internal static class ShortcutCatalogView
{
    internal static Border? AvailabilityNotice(AppController controller, Action refresh)
    {
        if (controller.UnavailableShortcuts.Count == 0) return null;
        var content = new StackPanel();
        content.Children.Add(Ui.Text(L.T("Some shortcuts could not start"), 15, true));
        var explanation = Ui.Text(L.T("The other shortcuts still work. Close any app using these keys, then retry, or choose different keys below."), 12, muted: true);
        explanation.Margin = new Thickness(0, 6, 0, 8);
        content.Children.Add(explanation);
        foreach (var failure in controller.UnavailableShortcuts)
        {
            var entry = FeatureShortcutCatalog.All.FirstOrDefault(item => item.Action == failure.Key);
            string name = entry is null ? failure.Key : L.T(entry.Title);
            string gesture = entry?.Read(controller.Settings) ?? "";
            content.Children.Add(Ui.Text($"{name} · {gesture}", 12));
        }
        var retry = Ui.Button(L.T("Retry shortcuts"), () => { controller.RetryShortcuts(); refresh(); });
        retry.Margin = new Thickness(0, 12, 0, 0);
        retry.HorizontalAlignment = HorizontalAlignment.Left;
        content.Children.Add(retry);
        return Ui.Card(content, 16);
    }

    internal static Grid Row(AppController controller, FeatureShortcutCatalog.Entry entry, Action? refreshPage = null)
    {
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        var change = Ui.Button(L.T("Change shortcut"), () => { });
        change.Padding = new Thickness(8, 5, 4, 5);
        AutomationProperties.SetName(change, L.T("Change shortcut") + ": " + L.T(entry.Title));
        void Refresh()
        {
            string value = entry.Read(controller.Settings);
            change.Content = string.IsNullOrWhiteSpace(value) ? Ui.Text(L.T("Unassigned"), 12, muted: true) : Ui.Shortcut(value);
        }
        change.Click += (_, _) => { controller.RecordFeatureShortcut(entry); Refresh(); refreshPage?.Invoke(); };
        Refresh(); controls.Children.Add(change);
        CheckBox? toggle = null;
        bool updating = false;
        toggle = Ui.Toggle(FeatureShortcutCatalog.IsEnabled(controller.Settings, entry), enabled =>
        {
            if (updating) return;
            if (!controller.SetFeatureShortcutEnabled(entry, enabled))
            {
                updating = true; toggle!.IsChecked = FeatureShortcutCatalog.IsEnabled(controller.Settings, entry); updating = false;
            }
            Refresh();
            refreshPage?.Invoke();
        });
        toggle.Margin = new Thickness(12, 0, 0, 0);
        toggle.Tag = "shortcut-enabled-" + entry.Action;
        AutomationProperties.SetName(toggle, L.T("Enable shortcut") + ": " + L.T(entry.Title));
        controls.Children.Add(toggle);
        string description = entry.Optional ? "Optional · turn on to use from any app." : "Default · available while DesktopTools is running.";
        var row = Ui.Row(L.T(entry.Title), L.T(description), controls);
        row.Tag = "feature-shortcut-row";
        return row;
    }
}
