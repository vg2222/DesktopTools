using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DesktopTools.Localization;

namespace DesktopTools.UI;

internal sealed partial class MainWindow
{
    private void Help()
    {
        var search = new TextBox { Padding = new Thickness(38, 10, 12, 10), Tag = "help-search" };
        System.Windows.Automation.AutomationProperties.SetName(search, L.T("Search instructions"));
        var searchField = new Grid { Margin = new Thickness(0, 0, 0, 18) }; searchField.Children.Add(search);
        var searchIcon = Ui.Icon("Search", 18); searchIcon.HorizontalAlignment = HorizontalAlignment.Left; searchIcon.Margin = new Thickness(12, 0, 0, 0); searchField.Children.Add(searchIcon);
        var hint = Ui.Text(L.T("Search instructions"), 13, muted: true); hint.Margin = new Thickness(39, 0, 12, 0); hint.IsHitTestVisible = false; searchField.Children.Add(hint);
        search.TextChanged += (_, _) => hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        page.Children.Add(searchField);
        var cards = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -12, 0), Tag = "help-cards" };
        page.Children.Add(cards);
        var empty = Ui.Text(L.T("No results"), 14, muted: true); page.Children.Add(empty);
        var items = new (string Id, string Title, string Detail, string Icon, Action Launch, Func<bool> Enabled)[]
        {
            ("initial", "Initial setup", "Appearance, startup, and display preferences.", "Settings", () => controller.OpenSetup(restart: true), () => true),
            ("recorder", "Screen recorder", "Source, sound and saving", "Record", () => controller.OpenFeatureGuide("recorder"), () => controller.Settings.ScreenRecorderEnabled),
            ("image-editor", "Image tools", "Size, drawing and export", "Image", () => controller.OpenFeatureGuide("image-editor"), () => controller.Settings.ImageToolsEnabled),
            ("teleprompter", "Teleprompter", "Text, speed and presentation", "Prompter", () => controller.OpenFeatureGuide("teleprompter"), () => controller.Settings.TeleprompterEnabled),
            ("video-editor", "Video editor", "Trim, cut, crop, rotate and export MP4.", "Video", () => controller.OpenFeatureGuide("video-editor"), () => controller.Settings.VideoEditorEnabled),
            ("text-tools", "Text tools", "Screen OCR and translation", "Translate", () => controller.OpenFeatureGuide("text-tools"), () => controller.Settings.TranslationEnabled || controller.Settings.ScreenTextEnabled)
        };
        void Render()
        {
            cards.Children.Clear();
            foreach (var item in items.Where(i => (L.T(i.Title) + " " + L.T(i.Detail)).Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            {
                var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
                var icon = Ui.Icon(item.Icon, 28); icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new Thickness(0, 3, 12, 0); grid.Children.Add(icon);
                var content = new StackPanel(); content.Children.Add(Ui.Text(L.T(item.Title), 14, true));
                var detail = Ui.Text(L.T(item.Detail), 12, muted: true); detail.Margin = new Thickness(0, 6, 0, 12); content.Children.Add(detail);
                var start = Ui.Button(L.T("Start"), item.Launch, primary: true); start.Tag = "help-start-" + item.Id; start.HorizontalAlignment = HorizontalAlignment.Left; start.MinWidth = 100; start.IsEnabled = item.Enabled(); content.Children.Add(start);
                Grid.SetColumn(content, 1); grid.Children.Add(content);
                var card = Ui.Card(grid, 18); card.Margin = new Thickness(0, 0, 12, 12);
                if (!start.IsEnabled) Ui.Tip(card, L.T("Enable this feature in Settings to start its guide."));
                cards.Children.Add(card);
            }
            empty.Visibility = cards.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        search.TextChanged += (_, _) => Render(); Render();
    }
}
