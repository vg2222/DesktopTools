using DesktopTools.Core;
using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DesktopTools.UI;

internal sealed partial class MainWindow
{
    private string featureSettingsQuery = "";

    private void FeatureSettingsSearch()
    {
        var content = new StackPanel();
        var heading = Ui.IconLabel("Search", L.T("Find feature settings"), 19);
        heading.Margin = new Thickness(0, 0, 0, 12);
        content.Children.Add(heading);
        var field = new Grid();
        var search = new TextBox { Text = featureSettingsQuery, Tag = "settings-feature-search",
            Padding = new Thickness(14, 8, 48, 8), MinHeight = 46 };
        System.Windows.Automation.AutomationProperties.SetName(search, L.T("Find feature settings"));
        field.Children.Add(search);
        var hint = Ui.Text(L.T("Search tools, options or shortcuts"), 14, muted: true);
        hint.Margin = new Thickness(16, 0, 15, 0);
        hint.IsHitTestVisible = false;
        field.Children.Add(hint);
        var clearSearch = Ui.SearchClearButton(search); clearSearch.Tag = "search-clear-settings";
        field.Children.Add(clearSearch);
        content.Children.Add(field);
        var results = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        content.Children.Add(results);
        page.Children.Add(Ui.Card(content, 18));

        DashboardTool[] matches = [];
        int previousState = 0; // 0: no query, 1: results, 2: empty
        void Render()
        {
            featureSettingsQuery = search.Text;
            string query = search.Text.Trim();
            hint.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            results.Children.Clear();
            results.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (query.Length == 0) { previousState = 0; return; }
            matches = DashboardTools().Where(tool => FeatureSearchText(tool).Contains(query,
                StringComparison.CurrentCultureIgnoreCase)).ToArray();
            if (matches.Length == 0)
            {
                var empty = Ui.SearchEmptyState(L.T("No feature settings found."), L.T("Try a different search."));
                empty.Tag = "search-empty-settings"; results.Children.Add(empty);
                if (previousState != 2) Motion.Transition(empty);
                previousState = 2;
                return;
            }
            foreach (var tool in matches)
            {
                var item = tool;
                var button = Ui.Button(L.T("Feature settings") + ": " + L.T(tool.Title), () => Navigate(item.Settings));
                button.Tag = "settings-result-" + tool.Id;
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.MinHeight = 52;
                button.Padding = new Thickness(10, 7, 10, 7);
                button.Margin = new Thickness(0, 3, 0, 3);
                var row = new DockPanel();
                var arrow = Ui.Icon("Chevron", 15); DockPanel.SetDock(arrow, Dock.Right); row.Children.Add(arrow);
                var icon = Ui.Icon(tool.Icon, 20); icon.Margin = new Thickness(0, 0, 12, 0);
                DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon);
                var labels = new StackPanel();
                labels.Children.Add(Ui.Text(L.T(tool.Title), 13, true));
                labels.Children.Add(Ui.Text(L.T(tool.Group), 11, muted: true));
                row.Children.Add(labels);
                button.Content = row;
                results.Children.Add(button);
            }
            if (previousState != 1) Motion.Transition(results);
            previousState = 1;
        }
        search.TextChanged += (_, _) => Render();
        search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { search.Clear(); e.Handled = true; }
            else if (e.Key == Key.Enter && matches.Length > 0)
            {
                Navigate(matches[0].Settings); e.Handled = true;
            }
        };
        Render();
    }

    private string FeatureSearchText(DashboardTool tool)
    {
        var shortcuts = FeatureShortcutCatalog.All.Where(entry => entry.FeatureId == tool.Id)
            .SelectMany(entry => new[] { entry.Title, L.T(entry.Title), entry.Read(controller.Settings) });
        return string.Join(" ", new[] { tool.Title, L.T(tool.Title), tool.Group, L.T(tool.Group),
            tool.Detail, L.T(tool.Detail), FeatureSettingsSection(tool.Id), L.T(FeatureSettingsSection(tool.Id)) }
            .Concat(shortcuts));
    }
}
