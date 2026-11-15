using DesktopTools.Localization;
using DesktopTools.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DesktopTools.Extras;

/// <summary>
/// The "Remove hidden details" checkbox and the line that says what the source file carries. Shared by the Hide private data
/// panel and the image export dialog. Never saved as a setting: it starts unticked for every newly opened image.
/// </summary>
internal sealed class HiddenDetailsOption
{
    internal const string CheckBoxName = "RemoveHiddenDetails", SummaryName = "HiddenDetailsSummary", LabelName = "RemoveHiddenDetailsLabel";
    private readonly CheckBox box;
    private readonly TextBlock label, summary;
    private Action<bool>? changed;
    internal FrameworkElement View { get; }
    private bool available;
    // Not box.IsEnabled: a dialog disables its controls while it saves, which must not turn removal off.
    internal bool Remove => available && box.IsChecked == true;

    internal HiddenDetailsOption(double fontSize = 12)
    {
        // The app's checkboxes are drawn as switches without their content, so the sentence sits beside the switch and toggles it too.
        box = new CheckBox { Name = CheckBoxName, IsChecked = false, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        string text = L.T("Remove hidden details (location, device, date, author)");
        System.Windows.Automation.AutomationProperties.SetName(box, text);
        label = Ui.Text(text, fontSize); label.Name = LabelName; label.Cursor = Cursors.Hand; label.VerticalAlignment = VerticalAlignment.Top;
        label.MouseLeftButtonUp += (_, _) => { if (box.IsEnabled) box.IsChecked = box.IsChecked != true; };
        var row = new DockPanel(); DockPanel.SetDock(box, Dock.Left); row.Children.Add(box); row.Children.Add(label);
        summary = Ui.Text("", fontSize - 1, muted: true); summary.Name = SummaryName; summary.Margin = new Thickness(0, 4, 0, 0);
        var panel = new StackPanel(); panel.Children.Add(row); panel.Children.Add(summary);
        View = panel;
        box.Checked += (_, _) => changed?.Invoke(true);
        box.Unchecked += (_, _) => changed?.Invoke(false);
        Show(null, false, null);
    }

    /// <summary>Shows what the source carries. Null means there is no source file (a DesktopTools screenshot).</summary>
    internal void Show(HiddenDetails? details, bool remove, Action<bool>? onChanged)
    {
        changed = null;
        available = details is { Readable: false } || details?.Any == true;
        box.IsEnabled = available; label.Opacity = available ? 1 : 0.55;
        box.IsChecked = available && remove;
        summary.Text = Summary(details);
        changed = onChanged;
    }

    internal static string Summary(HiddenDetails? details)
    {
        if (details is { Readable: false }) return L.T("Hidden details could not be read. Tick the box to save a copy without any.");
        if (details is not { Any: true }) return L.T("This file has no hidden details.");
        var found = new List<string>();
        if (details.Location) found.Add(L.T("location"));
        if (details.Device) found.Add(L.T("device"));
        if (details.Date) found.Add(L.T("date"));
        if (details.Author) found.Add(L.T("author"));
        if (details.Other > 0) found.Add(L.T("other"));
        return L.F($"This file contains hidden details: {string.Join(", ", found)}.");
    }
}
