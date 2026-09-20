using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DesktopTools.UI;

internal sealed partial class MainWindow
{
    private static readonly string[] DashboardGroups = ["Capture tools", "Presentation tools", "Media tools", "Text tools", "Desktop utilities"];
    private TextBox? dashboardSearch;
    private sealed record DashboardTool(string Id, string Title, string Group, string Icon, string Detail, Action Launch, Func<bool> Enabled, string Settings);
    private static string NavigationIcon(string page) => page switch
    {
        "Capture tools" => "Capture", "Presentation tools" => "Present", "Media tools" => "Image", "Text tools" => "Text",
        "Desktop utilities" => "Utilities", _ => page
    };
    private DashboardTool[] DashboardTools()
    {
        var tools = new List<DashboardTool>
        {
            new("capture", "Region capture", "Capture tools", "Capture", "Select, edit, and share a screenshot.", () => { HideImmediatelyForCapture(); _ = controller.CaptureAsync(); }, () => controller.Settings.CaptureEnabled, "Capture"),
            new("record", "Screen recorder", "Capture tools", "Record", "Record a monitor or region to MP4.", controller.OpenScreenRecorder, () => controller.Settings.ScreenRecorderEnabled, "Utilities"),
            new("pin", "Pin screenshot", "Capture tools", "Pin", "Pin above applications", controller.PinLast, () => controller.LastCapture != null, "Capture"),
            new("draw", "Screen drawing", "Presentation tools", "Pen", "Annotate over any application.", () => { Hide(); controller.ToggleDraw(); }, () => controller.Settings.DrawingEnabled, "Draw"),
            new("laser", "Laser pointer", "Presentation tools", "Laser", "Guide attention with a laser, spotlight, or frozen screen.", () => { Hide(); controller.TogglePresentation("Laser"); }, () => controller.Settings.LaserEnabled, "Laser pointer"),
            new("spotlight", "Cursor spotlight", "Presentation tools", "Spotlight", "Presentation aids", () => { Hide(); controller.TogglePresentation("Spotlight"); }, () => controller.Settings.SpotlightEnabled, "Cursor spotlight"),
            new("freeze", "Freeze frame", "Presentation tools", "Freeze", "Screen drawing", () => _ = controller.FreezeAsync(), () => controller.Settings.DrawingEnabled && controller.Settings.FreezeEnabled, "Freeze frame"),
            new("prompter", "Teleprompter", "Presentation tools", "Text", "Presentation aids", controller.OpenTeleprompter, () => controller.Settings.TeleprompterEnabled, "Utilities"),
            new("images", "Image tools", "Media tools", "Image", "Resize, rotate, mirror and convert images.", controller.OpenImageTools, () => controller.Settings.ImageToolsEnabled, "Utilities"),
            new("video", "Video editor", "Media tools", "Video", "Trim, cut, crop, rotate and export MP4.", controller.OpenVideoEditor, () => controller.Settings.VideoEditorEnabled, "Utilities"),
            new("color", "Screen eyedropper", "Media tools", "Eyedropper", "Pick color", () => _ = controller.PickScreenColorAsync(), () => controller.Settings.EyedropperEnabled, "Utilities"),
            new("ocr", "Scan screen text", "Text tools", "Text", "Recognize text (OCR)", () => { HideImmediatelyForCapture(); _ = controller.CaptureAsync(textOnly: true); }, () => controller.Settings.ScreenTextEnabled, "Utilities"),
            new("translate", "Local translation", "Text tools", "Translate", "Review, copy or translate recognized text.", controller.OpenTextToolsWindow, () => controller.Settings.TranslationEnabled || controller.Settings.ScreenTextEnabled, "Utilities"),
            new("qr", "QR codes", "Text tools", "QR", "Create a code from text or a link.", controller.OpenQrCodes, () => controller.Settings.QrCodesEnabled, "Utilities"),
            new("notes", "Floating notes", "Desktop utilities", "Notes", "Keep notes and checklists above your work.", controller.OpenFloatingNotes, () => controller.Settings.FloatingNotesEnabled, "Utilities"),
            new("files", "File shelf", "Desktop utilities", "Folder", "Collect files for dragging between apps.", controller.OpenFileShelf, () => controller.Settings.FileShelfEnabled, "Utilities"),
            new("audio", "Audio controls", "Desktop utilities", "Audio", "Adjust volume and mute for individual apps.", controller.OpenAudioControls, () => controller.Settings.AudioControlsEnabled, "Utilities"),
            new("wheel", "Quick actions wheel", "Desktop utilities", "Utilities", "Open wheel", () => controller.OpenQuickWheel(), () => controller.Settings.QuickWheelEnabled, "Utilities"),
            new("window", "Pin active window", "Desktop utilities", "Pin", controller.Settings.WindowPinShortcut, () => Navigate("Utilities"), () => controller.Settings.WindowPinEnabled, "Utilities")
        };
        foreach (string aid in AidNames)
        {
            var name = aid;
            tools.Add(new("aid-" + name, name, "Presentation tools", name is "Countdown" or "Stopwatch" ? "Timer" : "Present", "Presentation aids", () => Navigate(name), () => true, name));
        }
        return tools.Select(tool => tool with
        {
            Enabled = () => DesktopTools.Core.FeatureAvailability.IsAvailable(controller.Settings, tool.Id) && tool.Enabled()
        }).ToArray();
    }
    private void DashboardHeading(string text)
    {
        var label = Ui.Text(L.T(text), 18, true); label.Margin = new Thickness(0, 18, 0, 12); page.Children.Add(label);
    }
    private void Home()
    {
        var header = new DockPanel(); var customize = Ui.Button(L.T("Edit favorites"), () => DashboardCategory(null));
        DockPanel.SetDock(customize, Dock.Right); header.Children.Add(customize); header.Children.Add(Ui.Text(L.T("My dashboard"), 30, true)); page.Children.Add(header);
        var searchRow = new Grid { Margin = new Thickness(0, 18, 0, 4) };
        dashboardSearch = new TextBox { Height = 44, MinHeight = 44, Padding = new Thickness(15, 11, 80, 11) };
        System.Windows.Automation.AutomationProperties.SetName(dashboardSearch, L.T("Find a tool"));
        var placeholder = Ui.Text(L.T("Find a tool"), 13, muted: true); placeholder.Margin = new Thickness(16, 0, 80, 0); placeholder.IsHitTestVisible = false;
        var hint = Ui.Text("Ctrl K", 11, muted: true); hint.HorizontalAlignment = HorizontalAlignment.Right; hint.Margin = new Thickness(0, 0, 16, 0); hint.IsHitTestVisible = false;
        searchRow.Children.Add(dashboardSearch); searchRow.Children.Add(placeholder); searchRow.Children.Add(hint); page.Children.Add(searchRow);
        string selectedGroup = "All";
        var filters = new WrapPanel { Margin = new Thickness(0, 6, 0, 8) };
        var filterButtons = new Dictionary<string, Button>();
        page.Children.Add(filters);
        var results = new StackPanel { Visibility = Visibility.Collapsed }; page.Children.Add(results);
        var normal = new StackPanel(); page.Children.Add(normal);
        var all = DashboardTools();
        void Search()
        {
            string query = dashboardSearch.Text.Trim(); placeholder.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            bool searching = query.Length != 0 || selectedGroup != "All";
            results.Children.Clear(); normal.Visibility = searching ? Visibility.Collapsed : Visibility.Visible; results.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (group, button) in filterButtons)
                button.SetResourceReference(BackgroundProperty, group == selectedGroup ? "Selected" : "Field");
            if (!searching) return;
            var matches = all.Where(t => (selectedGroup == "All" || t.Group == selectedGroup) &&
                $"{t.Title} {L.T(t.Title)} {t.Group} {L.T(t.Group)} {t.Detail} {L.T(t.Detail)}".Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            results.Children.Add(Ui.Text(matches.Length == 0 ? L.T("No tools found. Try another name.") : L.T("Search results"), 15, true));
            foreach (var tool in matches) results.Children.Add(DashboardRow(tool, false));
        }
        foreach (string group in new[] { "All" }.Concat(DashboardGroups))
        {
            var filter = Ui.Button(L.T(group), () => { selectedGroup = group; Search(); });
            filter.Tag = "filter-" + group; filter.Margin = new Thickness(0, 0, 6, 6); filter.Padding = new Thickness(12, 5, 12, 5);
            filterButtons.Add(group, filter); filters.Children.Add(filter);
        }
        Search();
        stateRefreshers.Add(Search);
        dashboardSearch.TextChanged += (_, _) => Search(); dashboardSearch.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { dashboardSearch.Clear(); e.Handled = true; } };
        var pinnedTitle = Ui.Text(L.T("Pinned tools"), 17, true); pinnedTitle.Margin = new Thickness(0, 18, 0, 12); normal.Children.Add(pinnedTitle);
        var pins = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -10, 0) };
        foreach (string id in controller.Settings.HomeFavorites)
            if (all.FirstOrDefault(t => t.Id == id) is { } tool) pins.Children.Add(DashboardTile(tool));
        normal.Children.Add(pins);
        if (pins.Children.Count == 0) normal.Children.Add(Ui.Text(L.T("Use the star beside a tool to pin it here."), 13, muted: true));
        var groupTitle = Ui.Text(L.T("Tool collections"), 17, true); groupTitle.Margin = new Thickness(0, 16, 0, 10); normal.Children.Add(groupTitle);
        var groups = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -10, 0) };
        groups.SizeChanged += (_, _) => groups.Columns = groups.ActualWidth >= 730 ? 3 : 2;
        foreach (string group in DashboardGroups)
        {
            var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition()); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; copy.Children.Add(Ui.Text(L.T(group), 16, true));
            var detail = Ui.Text(L.T(GroupDescription(group)), 12, muted: true); detail.Margin = new Thickness(0, 8, 6, 0); copy.Children.Add(detail); body.Children.Add(copy);
            var art = DashboardArt(NavigationIcon(group), 60); Grid.SetColumn(art, 1); body.Children.Add(art);
            var button = Ui.Button(L.T(group), () => Navigate(group)); button.Content = body; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Padding = new Thickness(16); button.MinHeight = 106; button.Margin = new Thickness(0, 0, 10, 10); groups.Children.Add(button);
        }
        normal.Children.Add(groups);
        var recentTitle = Ui.Text(L.T("Recent screenshots"), 17, true); recentTitle.Margin = new Thickness(0, 14, 0, 12); normal.Children.Add(recentTitle);
        var recent = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -10, 0) };
        foreach (var entry in controller.CaptureHistory.Entries.Take(3))
        {
            var contents = new StackPanel(); contents.Children.Add(new Image { Source = entry.Image, Height = 76, Stretch = Stretch.UniformToFill, ClipToBounds = true });
            var label = Ui.Text(entry.CreatedAt.ToLocalTime().ToString("HH:mm") + $"   {entry.Image.PixelWidth} × {entry.Image.PixelHeight}", 11, muted: true); label.Margin = new Thickness(0, 10, 0, 0); contents.Children.Add(label);
            var button = Ui.Button(L.T("Open screenshot"), () => controller.OpenRecentCapture(entry.Image)); button.Content = contents; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Padding = new Thickness(10); button.Margin = new Thickness(0, 0, 10, 0); recent.Children.Add(button);
        }
        normal.Children.Add(recent);
        if (recent.Children.Count == 0) normal.Children.Add(Ui.Card(Ui.Text(L.T("Your recent screenshots appear here until you quit."), 12, muted: true), 16));
    }
    private static string GroupDescription(string group) => group switch
    {
        "Capture tools" => "Screenshots, recording and pinned captures",
        "Presentation tools" => "Drawing, laser, spotlight and presentation aids",
        "Media tools" => "Edit images and video, remove backgrounds",
        "Text tools" => "Screen OCR, translation and QR codes",
        _ => "Notes, file shelf, audio and quick actions"
    };
    private void DashboardCategory(string? group)
    {
        stateRefreshers.Clear(); page.Children.Clear(); dashboardSearch = null;
        var breadcrumb = new TextBlock { Margin = new Thickness(0, 0, 0, 14), VerticalAlignment = VerticalAlignment.Center, Tag = "category-breadcrumb" };
        var home = new System.Windows.Documents.Hyperlink { TextDecorations = null, Cursor = System.Windows.Input.Cursors.Hand };
        home.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Muted");
        home.Inlines.Add(new System.Windows.Documents.InlineUIContainer(Ui.Icon("Home", 15)) { BaselineAlignment = BaselineAlignment.Center });
        home.Inlines.Add(new System.Windows.Documents.Run("  " + L.T("Home"))); home.Click += (_, _) => Navigate("Home");
        System.Windows.Automation.AutomationProperties.SetName(home, L.T("Home")); breadcrumb.Inlines.Add(home);
        breadcrumb.Inlines.Add(new System.Windows.Documents.Run("   /   " + L.T(group ?? "Edit favorites")));
        breadcrumb.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); page.Children.Add(breadcrumb);
        PageBanner(group ?? "Edit favorites", group == null ? "Use the star beside a tool to pin it here." : GroupDescription(group), NavigationIcon(group ?? "Star"));
        foreach (var tool in DashboardTools().Where(t => group == null || t.Group == group)) page.Children.Add(DashboardRow(tool));
    }
    private FrameworkElement DashboardRow(DashboardTool tool, bool trackState = true)
    {
        var row = new Grid { Margin = new Thickness(0, 10, 0, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var launch = Ui.Button(L.T(tool.Title), tool.Launch); launch.Tag = "launch-" + tool.Id; launch.Padding = new Thickness(12, 10, 12, 10); launch.MinHeight = 72; launch.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var contents = new DockPanel(); var art = ToolArtwork(tool.Id, tool.Icon, 48, plain: true); art.Margin = new Thickness(0, 0, 14, 0); DockPanel.SetDock(art, Dock.Left); contents.Children.Add(art);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; text.Children.Add(Ui.Text(L.T(tool.Title), 14, true)); text.Children.Add(Ui.Text(L.T(tool.Detail), 12, muted: true)); contents.Children.Add(text); launch.Content = contents;
        row.Children.Add(launch);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        void Refresh() => launch.IsEnabled = tool.Enabled();
        if (trackState) stateRefreshers.Add(Refresh);
        Refresh();
        controls.Children.Add(Ui.IconButton("Settings", L.T("Feature settings") + ": " + L.T(tool.Title), () => Navigate(tool.Settings)));
        var favorite = Ui.IconButton("Star", L.T("Pin or unpin tool") + ": " + L.T(tool.Title), () => { }); favorite.Tag = "favorite-" + tool.Id;
        void Mark() { favorite.SetResourceReference(BackgroundProperty, controller.Settings.HomeFavorites.Contains(tool.Id) ? "Selected" : "Surface"); }
        favorite.Click += (_, _) => { var ids = controller.Settings.HomeFavorites.ToList(); if (!ids.Remove(tool.Id)) { if (ids.Count == 8) ids.RemoveAt(0); ids.Add(tool.Id); } Change(s => s.HomeFavorites = ids.ToArray()); Mark(); }; Mark(); controls.Children.Add(favorite);
        Grid.SetColumn(controls, 1); row.Children.Add(controls); return row;
    }
    private Button DashboardTile(DashboardTool tool)
    {
        var content = new StackPanel(); content.Children.Add(DashboardArt(tool.Icon, 64)); var title = Ui.Text(L.T(tool.Title), 13, true); title.Margin = new Thickness(0, 12, 0, 0); content.Children.Add(title);
        var button = Ui.Button(L.T(tool.Title), tool.Launch); button.Tag = "launch-" + tool.Id; button.Content = content; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Padding = new Thickness(12); button.Margin = new Thickness(0, 0, 10, 10);
        void Refresh() => button.IsEnabled = tool.Enabled(); stateRefreshers.Add(Refresh); Refresh(); return button;
    }
    private static FrameworkElement DashboardArt(string icon, double size)
    {
        var glyph = Ui.Icon(icon, size * .38);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        glyph.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Accent");
        var tile = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * .24),
            BorderThickness = new Thickness(1), Child = glyph, IsHitTestVisible = false
        };
        tile.SetResourceReference(Border.BackgroundProperty, "Selected");
        tile.SetResourceReference(Border.BorderBrushProperty, "GlassRim");
        return tile;
    }
}
