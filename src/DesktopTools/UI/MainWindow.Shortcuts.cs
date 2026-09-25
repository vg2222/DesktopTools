using DesktopTools.Core;
using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;

namespace DesktopTools.UI;

internal sealed partial class MainWindow
{
    private void ShortcutsCatalog()
    {
        var settings = controller.Settings;
        var availability = ShortcutCatalogView.AvailabilityNotice(controller, () => Navigate("Shortcuts"));
        if (availability is not null) page.Children.Add(availability);
        var search = new TextBox { Tag = "shortcut-search", Margin = new Thickness(0, 0, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetName(search, L.T("Find a shortcut"));
        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var searchBox = new Grid();
        searchBox.Children.Add(search);
        var searchHint = Ui.IconLabel("Search", L.T("Find a shortcut"));
        searchHint.Margin = new Thickness(14, 0, 8, 12); searchHint.Opacity = .65; searchHint.IsHitTestVisible = false;
        searchBox.Children.Add(searchHint);
        search.TextChanged += (_, _) => searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        page.Children.Add(searchBox);
        page.Children.Add(tabs);
        var searchable = new Dictionary<UIElement, string>();

        Grid Row(string title, string? description, FrameworkElement control)
        {
            var row = Ui.Row(title, description, control);
            searchable.Add(row, title);
            return row;
        }

        var global = new StackPanel();
        foreach (FeatureShortcutCatalog.Entry entry in FeatureShortcutCatalog.All)
        {
            var row = ShortcutCatalogView.Row(controller, entry, () => Navigate("Shortcuts"));
            searchable.Add(row, L.T(entry.Title));
            global.Children.Add(row);
        }
        var globalCard = Ui.Card(global);
        page.Children.Add(globalCard);

        var drawing = new StackPanel();
        foreach (var (action, value) in settings.DrawingShortcuts)
        {
            var change = Ui.Button(L.T("Change shortcut"), () =>
            {
                controller.RecordDrawingShortcut(action);
                Navigate("Shortcuts");
            });
            change.Content = Ui.Shortcut(value);
            change.Padding = new Thickness(10, 6, 10, 6);
            drawing.Children.Add(Row(L.T(action == "FinishText" ? "Finish text" : action),
                L.T(action == "Delete" ? "Delete selected annotation, or clear the canvas." : null), change));
        }
        drawing.Children.Add(Row(L.T("Cancel / hide"), L.T("Always available as an emergency exit."), Ui.Shortcut("Esc")));
        drawing.Children.Add(Row(L.T("Snap shapes"), L.T("Hold while drawing a shape."), Ui.Shortcut("Shift")));
        var drawingCard = Ui.Card(drawing);
        page.Children.Add(drawingCard);
        var empty = Ui.Text(L.T("No shortcuts found."), 13, muted: true);
        page.Children.Add(empty);

        bool drawingSelected = false;
        var globalTab = Ui.Button(L.T("Global shortcuts"), () => { });
        var drawingTab = Ui.Button(L.T("While drawing"), () => { });
        globalTab.Tag = "shortcut-tab-global";
        drawingTab.Tag = "shortcut-tab-drawing";
        tabs.Children.Add(globalTab);
        tabs.Children.Add(drawingTab);
        globalTab.Click += (_, _) => { drawingSelected = false; Refresh(); };
        drawingTab.Click += (_, _) => { drawingSelected = true; Refresh(); };
        search.TextChanged += (_, _) => Refresh();
        Refresh();

        void Refresh()
        {
            globalCard.Visibility = drawingSelected ? Visibility.Collapsed : Visibility.Visible;
            drawingCard.Visibility = drawingSelected ? Visibility.Visible : Visibility.Collapsed;
            globalTab.SetResourceReference(BackgroundProperty, drawingSelected ? "Field" : "Selected");
            drawingTab.SetResourceReference(BackgroundProperty, drawingSelected ? "Selected" : "Field");
            foreach (var (row, title) in searchable)
                row.Visibility = title.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;
            var active = drawingSelected ? drawing : global;
            empty.Visibility = active.Children.Cast<UIElement>().Any(row => row.Visibility == Visibility.Visible)
                ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
