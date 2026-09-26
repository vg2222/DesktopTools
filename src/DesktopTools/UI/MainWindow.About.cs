using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using DesktopTools.Localization;

namespace DesktopTools.UI;

internal sealed partial class MainWindow
{
    private void About()
    {
        var body = new StackPanel { MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Stretch };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 28) };
        var icon = Ui.AppIcon(52); icon.Margin = new Thickness(0, 0, 16, 0); brand.Children.Add(icon);
        var title = new StackPanel(); title.Children.Add(Ui.Text("DesktopTools", 28, true)); title.Children.Add(Ui.Text(L.T("Draw, explain, and capture anything on your desktop."), 12, muted: true)); brand.Children.Add(title); body.Children.Add(brand);
        var rows = new StackPanel();
        var license = Ui.Button("MIT", () => ShowLicense("License", "DesktopTools.License")); license.Tag = "about-license";
        rows.Children.Add(Ui.Row(L.T("License"), null, license));
        var components = Ui.Button(L.T("Component licenses"), () => ShowLicense("Component licenses", "DesktopTools.ThirdPartyNotices")); components.Tag = "about-components";
        rows.Children.Add(Ui.Row(L.T("Components"), null, components));
        rows.Children.Add(Ui.Row(L.T("Source code"), null, Ui.Button("GitHub", () => controller.OpenUpdateLink(new Uri(DesktopTools.Updates.GitHubReleaseClient.RepositoryUrl)))));
        body.Children.Add(Ui.Card(rows));
        body.Children.Add(UpdateCard(preferences: false));
        var version = Ui.Text(VersionLabel, 12, muted: true); version.HorizontalAlignment = HorizontalAlignment.Center; version.Margin = new Thickness(0, 12, 0, 0); body.Children.Add(version); page.Children.Add(body);
    }

    private void ShowLicense(string title, string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        if (stream == null) return;
        using var reader = new StreamReader(stream);
        var dialog = new Window { Title = L.T(title), Width = 760, Height = 580, MinWidth = 500, MinHeight = 360, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.WindowStyle = WindowStyle.None;
        System.Windows.Shell.WindowChrome.SetWindowChrome(dialog, new System.Windows.Shell.WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6) });
        var panel = new DockPanel { Margin = new Thickness(18) };
        var header = UtilityWindowChrome.Header(dialog, dialog.Title, dialog.Close, L.T("Close"), 18); DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var close = Ui.Button(L.T("Close"), dialog.Close); close.HorizontalAlignment = HorizontalAlignment.Right; close.Margin = new Thickness(0, 12, 0, 0); DockPanel.SetDock(close, Dock.Bottom); panel.Children.Add(close);
        panel.Children.Add(new TextBox { Text = reader.ReadToEnd(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, Tag = "license-text" });
        dialog.SetResourceReference(BackgroundProperty, "Surface"); dialog.Content = panel; dialog.ShowDialog();
    }
}
