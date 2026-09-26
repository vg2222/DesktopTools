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
        var search = new TextBox { Tag = "shortcut-search", MinHeight = 46,
            Padding = new Thickness(42, 8, 48, 8) };
        System.Windows.Automation.AutomationProperties.SetName(search, L.T("Find a shortcut"));
        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var searchBox = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        searchBox.Children.Add(search);
        var searchIcon = Ui.Icon("Search", 18);
        searchIcon.HorizontalAlignment = HorizontalAlignment.Left;
        searchIcon.Margin = new Thickness(15, 0, 0, 0);
        searchIcon.IsHitTestVisible = false;
        searchBox.Children.Add(searchIcon);
        var searchHint = Ui.Text(L.T("Find a shortcut"), 14, muted: true);
        searchHint.Margin = new Thickness(45, 0, 12, 0); searchHint.IsHitTestVisible = false;
        searchBox.Children.Add(searchHint);
        var clearSearch = Ui.SearchClearButton(search); clearSearch.Tag = "search-clear-shortcuts";
        searchBox.Children.Add(clearSearch);
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
            var changeLabel = new StackPanel { Orientation = Orientation.Horizontal };
            var editIcon = Ui.Icon("Pen", 14); editIcon.Margin = new Thickness(0, 0, 8, 0); changeLabel.Children.Add(editIcon);
            changeLabel.Children.Add(Ui.Shortcut(value)); change.Content = changeLabel;
            change.Padding = new Thickness(10, 6, 10, 6);
            drawing.Children.Add(Row(L.T(action == "FinishText" ? "Finish text" : action),
                L.T(action == "Delete" ? "Delete selected annotation, or clear the canvas." : null), change));
        }
        drawing.Children.Add(Row(L.T("Cancel / hide"), L.T("Always available as an emergency exit."), Ui.Shortcut("Esc")));
        drawing.Children.Add(Row(L.T("Snap shapes"), L.T("Hold while drawing a shape."), Ui.Shortcut("Shift")));
        var drawingCard = Ui.Card(drawing);
        page.Children.Add(drawingCard);
        var empty = Ui.SearchEmptyState(L.T("No shortcuts found."), L.T("Try a different search."));
        empty.Tag = "search-empty-shortcuts";
        page.Children.Add(empty);

        bool drawingSelected = false;
        bool wasEmpty = false;
        bool previousDrawingSelected = false;
        var globalTab = Ui.Button(L.T("Global shortcuts"), () => { });
        var drawingTab = Ui.Button(L.T("While drawing"), () => { });
        globalTab.Content = Ui.IconLabel("Shortcuts", L.T("Global shortcuts"), 16, textSize: 13);
        drawingTab.Content = Ui.IconLabel("Pen", L.T("While drawing"), 16, textSize: 13);
        globalTab.Padding = drawingTab.Padding = new Thickness(14, 8, 14, 8);
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
            bool tabChanged = previousDrawingSelected != drawingSelected;
            globalTab.SetResourceReference(BackgroundProperty, drawingSelected ? "Field" : "Selected");
            drawingTab.SetResourceReference(BackgroundProperty, drawingSelected ? "Selected" : "Field");
            foreach (var (row, title) in searchable)
                row.Visibility = title.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;
            var active = drawingSelected ? drawing : global;
            bool isEmpty = !active.Children.Cast<UIElement>().Any(row => row.Visibility == Visibility.Visible);
            globalCard.Visibility = !drawingSelected && !isEmpty ? Visibility.Visible : Visibility.Collapsed;
            drawingCard.Visibility = drawingSelected && !isEmpty ? Visibility.Visible : Visibility.Collapsed;
            empty.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            if (isEmpty && (!wasEmpty || tabChanged)) Motion.Transition(empty);
            else if (!isEmpty && (wasEmpty || tabChanged)) Motion.Transition(drawingSelected ? drawingCard : globalCard);
            wasEmpty = isEmpty;
            previousDrawingSelected = drawingSelected;
        }
    }
}
