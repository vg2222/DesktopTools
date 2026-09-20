using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DesktopTools.UI;
internal sealed partial class MainWindow
{
    private static string SectionIcon(string title)
    {
        foreach (var (label, icon) in new[] {
            ("Appearance", "Color"), ("Theme", "Color"), ("Effects", "Spotlight"), ("Language", "Translate"),
            ("App behavior", "Behavior"), ("Preferences", "Profiles"), ("Notifications", "Notifications"),
            ("Screen sharing", "Shield"), ("Individual tool visibility", "Window"), ("Screen recorder", "Monitor"),
            ("Image tools", "Image"), ("Video editor", "Video"), ("Text tools", "Translate"),
            ("File shelf", "Folder"), ("Floating notes", "Notes"), ("Audio controls", "Audio"),
            ("Quick actions wheel", "Utilities"), ("Teleprompter", "Prompter"), ("Pin active window", "Pin"),
            ("QR codes", "QR"), ("Screen eyedropper", "Eyedropper"), ("Screen blackout", "Shield"),
            ("Display", "Monitor"), ("Controls", "Interact"), ("Availability", "Check"), ("Defaults", "Profiles"),
            ("Profiles", "Profiles"), ("Save as a new profile", "Save"), ("Drawing defaults", "Pen"),
            ("Drawing controls", "Pen"), ("Capture behavior", "Capture"), ("Latest screenshot", "Image"),
            ("Capture again", "Capture"), ("Recent screenshots", "Image"), ("Laser pointer", "Laser"),
            ("Cursor spotlight", "Spotlight"), ("Freeze frame", "Freeze"), ("Using the stopwatch", "Timer") })
            if (title.Equals(L.T(label), StringComparison.CurrentCultureIgnoreCase)) return icon;
        if (title.Contains(L.T("Shortcut"), StringComparison.CurrentCultureIgnoreCase)) return "Shortcuts";
        if (title.Contains(L.T("Theme"), StringComparison.CurrentCultureIgnoreCase)) return "Image";
        if (title.Contains(L.T("Screen sharing"), StringComparison.CurrentCultureIgnoreCase)) return "Shield";
        return "Settings";
    }
    private void PageBanner(string title, string description, string icon)
    {
        var panel = new Grid { Margin = new Thickness(0, 0, 0, 22) };
        panel.ColumnDefinitions.Add(new ColumnDefinition()); panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 18, 0) };
        copy.Children.Add(Ui.Text(L.T(title), 28, true)); var detail = Ui.Text(L.T(description), 13, muted: true); detail.Margin = new Thickness(0, 9, 0, 0); copy.Children.Add(detail); panel.Children.Add(copy);
        var art = DashboardGroups.Contains(title) ? DashboardArt(NavigationIcon(title), 60) : ToolArtwork(title, icon); Grid.SetColumn(art, 1); panel.Children.Add(art); page.Children.Add(panel);
    }
    private static FrameworkElement ToolArtwork(string id, string icon, double size = 56, bool plain = false)
    {
        // Standalone tool symbols, intentionally different from Home's small category scenes.
        icon = id switch
        {
            "record" => "Monitor", "profile-Everyday" => "Home", "profile-Meetings" => "Present", "profile-Teaching" => "Pen", "ocr" => "ScanText", "translate" => "Translate", "prompter" => "Prompter", "freeze" => "Freeze",
            "aid-Click indicators" => "Click", "aid-Shortcut display" => "Shortcuts", "aid-Stopwatch" => "Timer",
            "aid-Countdown" => "Timer", "aid-Screen ruler" => "Ruler", "aid-Screen blackout" => "Shield", _ => icon
        };
        if (plain)
        {
            var symbol = Ui.Icon(icon, 30);
            symbol.HorizontalAlignment = HorizontalAlignment.Center;
            symbol.VerticalAlignment = VerticalAlignment.Center;
            return new Grid { Width = size + 8, Height = size + 8, IsHitTestVisible = false, Tag = "tool-artwork", Children = { symbol } };
        }
        var layout = new Grid { Width = size + 8, Height = size + 8, IsHitTestVisible = false, Tag = "tool-artwork" };
        var tile = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 4), BorderThickness = new Thickness(1) };
        tile.SetResourceReference(Border.BackgroundProperty, "Selected"); tile.SetResourceReference(Border.BorderBrushProperty, "GlassRim");
        var glyph = Ui.Icon(icon, size * .45); glyph.HorizontalAlignment = HorizontalAlignment.Center; glyph.VerticalAlignment = VerticalAlignment.Center;
        glyph.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Accent");
        tile.Child = glyph; layout.Children.Add(tile);
        return layout;
    }
    private void ThemePreviews()
    {
        var previews = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0,0,-10,18) };
        foreach (string mode in new[] { "System", "Light", "Dark" })
        {
            bool dark = mode == "Dark" || mode == "System" && controller.Dark;
            var surface = new SolidColorBrush(dark ? Color.FromRgb(28,30,35) : Color.FromRgb(245,247,252));
            var inset = new SolidColorBrush(dark ? Color.FromRgb(60,63,71) : Color.FromRgb(218,225,239));
            var content = new StackPanel(); var miniature = new Grid { Height = 76, Background = surface, Margin = new Thickness(0,0,0,10) };
            miniature.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); miniature.ColumnDefinitions.Add(new ColumnDefinition());
            miniature.Children.Add(new Border { Background = inset, Margin = new Thickness(4), CornerRadius = new CornerRadius(5) });
            var lines = new StackPanel { Margin = new Thickness(5,10,7,5) }; Grid.SetColumn(lines,1);
            var highlight = new Border { Height = 8, Margin = new Thickness(0,0,22,8), CornerRadius = new CornerRadius(3) }; highlight.SetResourceReference(Border.BackgroundProperty,"Accent"); lines.Children.Add(highlight);
            lines.Children.Add(new Border { Height = 31, Background = inset, CornerRadius = new CornerRadius(6) }); miniature.Children.Add(lines); content.Children.Add(miniature); content.Children.Add(Ui.Text(L.T(mode),13,true));
            var button = Ui.Button(L.T(mode), () => { Change(s => s.Theme = mode); Navigate("Settings"); }); button.Content = content; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Padding = new Thickness(12); button.Margin = new Thickness(0,0,10,0);
            if(controller.Settings.Theme == mode) button.SetResourceReference(BackgroundProperty,"Selected"); previews.Children.Add(button);
        }
        page.Children.Add(previews);
    }
}
