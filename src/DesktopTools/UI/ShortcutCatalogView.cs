using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using DesktopTools.Core;
using DesktopTools.Localization;

namespace DesktopTools.UI;

internal static class ShortcutCatalogView
{
    internal static Grid Row(AppController controller, FeatureShortcutCatalog.Entry entry)
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
        change.Click += (_, _) => { controller.RecordFeatureShortcut(entry); Refresh(); };
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
