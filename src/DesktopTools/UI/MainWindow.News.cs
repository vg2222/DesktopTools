using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopTools.Localization;
using DesktopTools.Updates;

namespace DesktopTools.UI;

internal sealed partial class MainWindow
{
    private Border? newsBadge;
    private Button? newsHomeButton, newsCheckButton;
    private StackPanel? newsContent;
    private TextBlock? newsStatusText;

    private void OnNewsChanged() => Dispatcher.BeginInvoke(new Action(() => { RefreshNewsUi(); RefreshUpdateUi(); }));
    private void RefreshNewsUi()
    {
        bool security = controller.ActiveSecurityAlert != null;
        if (newsBadge != null)
        {
            newsBadge.Visibility = security || controller.HasUnreadNews ? Visibility.Visible : Visibility.Collapsed;
            if (security) newsBadge.Background = new SolidColorBrush(Color.FromRgb(196, 132, 27));
            else newsBadge.SetResourceReference(Border.BackgroundProperty, "Accent");
        }
        if (newsHomeButton != null && currentPage == "Home")
        {
            newsHomeButton.Visibility = security || controller.HasUnreadNews ? Visibility.Visible : Visibility.Collapsed;
            if (newsHomeButton.Visibility == Visibility.Visible)
            {
                string title = security ? L.T("Security notice") + " · " + controller.ActiveSecurityAlert!.LocalTitle :
                    controller.NewsItems.First(item => !item.IsSecurity && !controller.Settings.ReadNewsIds.Contains(item.Id)).LocalTitle;
                newsHomeButton.Content = Ui.IconLabel(security ? "Shield" : "Notifications", title);
                newsHomeButton.SetResourceReference(Control.BackgroundProperty, "Field");
                if (security) newsHomeButton.BorderBrush = new SolidColorBrush(Color.FromRgb(196, 132, 27));
                else newsHomeButton.SetResourceReference(Control.BorderBrushProperty, "Stroke");
                newsHomeButton.BorderThickness = new Thickness(1);
            }
        }
        if (currentPage == "News") RenderNewsContent();
    }
    private void News()
    {
        newsContent = null;
        controller.MarkNewsRead();
        RefreshNewsUi();
        var checkRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        newsCheckButton = Ui.Button(L.T("Check news"), async () => await controller.CheckNewsAsync());
        newsCheckButton.Content = Ui.IconLabel("Refresh", L.T("Check news"));
        newsCheckButton.HorizontalAlignment = HorizontalAlignment.Right;
        newsCheckButton.Margin = new Thickness(0);
        checkRow.Children.Add(newsCheckButton); page.Children.Add(checkRow);

        newsStatusText = Ui.Text("", 12, muted: true);
        newsStatusText.Margin = new Thickness(0, 0, 0, 10);
        newsStatusText.Visibility = Visibility.Collapsed;
        page.Children.Add(newsStatusText);
        newsContent = new StackPanel(); page.Children.Add(newsContent);
        var preference = new Grid();
        preference.ColumnDefinitions.Add(new ColumnDefinition());
        preference.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        preference.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        preference.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        preference.Children.Add(Ui.Text(L.T("Check news automatically"), 14, true));
        var toggle = Ui.Toggle(controller.Settings.AutomaticNewsChecks, enabled => Change(settings => settings.AutomaticNewsChecks = enabled));
        toggle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(toggle, 1); preference.Children.Add(toggle);
        System.Windows.Automation.AutomationProperties.SetName(toggle, L.T("Check news automatically"));
        var description = Ui.Text(L.T("Checks once an hour while DesktopTools is running."), 12, muted: true);
        description.Margin = new Thickness(0, 4, 0, 0);
        Grid.SetRow(description, 1); Grid.SetColumnSpan(description, 2); preference.Children.Add(description);
        var preferenceCard = Ui.Card(preference, 18);
        preferenceCard.Margin = new Thickness(0, 4, 0, 0);
        page.Children.Add(preferenceCard);
        RenderNewsContent();
    }
    private void RenderNewsContent()
    {
        if (newsContent == null || currentPage != "News") return;
        newsContent.Children.Clear();
        if (newsStatusText != null)
        {
            bool failedManualCheck = controller.NewsStatus == L.T("Could not check news. Showing saved announcements.");
            newsStatusText.Text = controller.CheckingNews ? L.T("Checking news…") :
                failedManualCheck ? controller.NewsStatus : "";
            newsStatusText.Visibility = newsStatusText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (newsCheckButton != null) newsCheckButton.IsEnabled = !controller.CheckingNews;
        var security = controller.ActiveSecurityAlert;
        if (security != null) newsContent.Children.Add(NewsCard(security, urgent: true));
        var ordinary = controller.NewsItems.Where(item => !item.IsSecurity).ToArray();
        if (ordinary.Length == 0 && security == null)
            newsContent.Children.Add(Ui.Card(Ui.Text(L.T("No announcements yet. Check again later."), 13, muted: true), 18));
        foreach (var item in ordinary) newsContent.Children.Add(NewsCard(item, urgent: false));
    }
    private FrameworkElement NewsCard(Announcement item, bool urgent)
    {
        var body = new StackPanel();
        var eyebrow = Ui.Text(L.T(urgent ? "Security notice" : "Announcement") + " · " +
            item.PublishedUtc.ToLocalTime().ToString("d", L.Culture), 11, true, muted: !urgent);
        if (urgent) eyebrow.Foreground = new SolidColorBrush(Color.FromRgb(196, 132, 27));
        body.Children.Add(eyebrow);
        var title = Ui.Text(item.LocalTitle, 17, true); title.TextWrapping = TextWrapping.Wrap;
        title.Margin = new Thickness(0, 7, 0, 6); body.Children.Add(title);
        var message = Ui.Text(item.LocalMessage, 13); message.TextWrapping = TextWrapping.Wrap; body.Children.Add(message);
        if (urgent)
        {
            var detail = Ui.Text(controller.SafeSecurityReleaseAvailable
                ? L.T("A fixed update is ready. DesktopTools will ask before downloading or restarting.")
                : L.T("A fixed update is not available yet. Check for updates or read the details."), 12, muted: true);
            detail.TextWrapping = TextWrapping.Wrap; detail.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(detail);
        }
        var actions = new WrapPanel { Margin = new Thickness(0, 13, 0, 0) };
        if (urgent)
            actions.Children.Add(Ui.Button(L.T(controller.SafeSecurityReleaseAvailable ? "Update" : "Check for updates"),
                () => { if (controller.SafeSecurityReleaseAvailable) _ = controller.BeginBackgroundUpdateAsync(); else _ = controller.CheckForUpdatesAsync(); }, true));
        if (item.Url != null)
        {
            var details = Ui.Button(L.T("View details"), () => controller.OpenNewsLink(item));
            actions.Children.Add(details);
        }
        if (actions.Children.Count > 0) body.Children.Add(actions);
        var card = Ui.Card(body, 18); card.Margin = new Thickness(0, 0, 0, 12);
        if (urgent) { card.SetResourceReference(Border.BackgroundProperty, "Field"); card.BorderBrush = new SolidColorBrush(Color.FromRgb(196, 132, 27)); card.BorderThickness = new Thickness(1); }
        return card;
    }
}
