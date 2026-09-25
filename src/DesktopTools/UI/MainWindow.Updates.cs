using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using DesktopTools.Localization;
using DesktopTools.Updates;

namespace DesktopTools.UI;

internal sealed partial class MainWindow
{
    private Button? updateIndicator, updateDownloadButton, updateCheckButton, updateNotesButton;
    private TextBlock? updateStatusText, updateVersionText;
    private ProgressBar? updateProgressBar;
    private void AddUpdateIndicator(Panel footer)
    {
        updateIndicator = Ui.Button(L.T("Update available"), () => { settingsTab = "Updates"; Navigate("Settings"); });
        updateIndicator.Tag = "update-indicator"; updateIndicator.HorizontalContentAlignment = HorizontalAlignment.Left;
        updateIndicator.Margin = new Thickness(0, 4, 0, 0); footer.Children.Add(updateIndicator);
        controller.UpdateChanged += RefreshUpdateUi; Closed += (_, _) => controller.UpdateChanged -= RefreshUpdateUi;
        RefreshUpdateUi();
    }
    private FrameworkElement UpdateCard(bool preferences)
    {
        var body = new StackPanel();
        var heading = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Ui.Icon("Refresh", 22); icon.Margin = new Thickness(0, 0, 12, 0); heading.Children.Add(icon);
        heading.Children.Add(Ui.Text(L.T("Updates"), 20, true)); body.Children.Add(heading);
        var intro = Ui.Text(L.T("Stable releases from GitHub. Notes and settings are kept when updating."), 12, muted: true);
        intro.Margin = new Thickness(0, 8, 0, 20); body.Children.Add(intro);

        var overview = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        overview.ColumnDefinitions.Add(new ColumnDefinition()); overview.ColumnDefinitions.Add(new ColumnDefinition());
        var installed = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        installed.Children.Add(Ui.Text(L.T("Installed version"), 11, muted: true));
        installed.Children.Add(Ui.Text(AppController.CurrentVersion, 18, true)); overview.Children.Add(installed);
        var latest = new StackPanel(); latest.Children.Add(Ui.Text(L.T("Available version"), 11, muted: true));
        updateVersionText = Ui.Text("—", 18, true); latest.Children.Add(updateVersionText); Grid.SetColumn(latest, 1); overview.Children.Add(latest);
        var versionSurface = new Border { Child = overview, Padding = new Thickness(18, 15, 18, 0), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 16) };
        versionSurface.SetResourceReference(Border.BackgroundProperty, "Field"); body.Children.Add(versionSurface);

        updateStatusText = Ui.Text("", 13, true); updateStatusText.Tag = "update-status";
        updateStatusText.Margin = new Thickness(0, 0, 0, 10); body.Children.Add(updateStatusText);
        updateProgressBar = NotificationSurface.CreateProgressBar(0, null);
        updateProgressBar.Margin = new Thickness(0, 0, 0, 14);
        body.Children.Add(updateProgressBar);
        var actions = new WrapPanel();
        updateCheckButton = Ui.Button(L.T("Check for updates"), async () => await controller.CheckForUpdatesAsync()); updateCheckButton.Tag = "check-updates";
        updateDownloadButton = Ui.Button(L.T("Update in background"), async () => await controller.BeginBackgroundUpdateAsync(), true); updateDownloadButton.Tag = "download-update";
        updateNotesButton = Ui.Button(L.T("Release notes"), controller.OpenUpdateNotes);
        actions.Children.Add(updateCheckButton); actions.Children.Add(updateDownloadButton); actions.Children.Add(updateNotesButton); body.Children.Add(actions);
        if (preferences)
        {
            var headingPreferences = Ui.Text(L.T("Automatic checks"), 16, true);
            headingPreferences.Margin = new Thickness(0, 24, 0, 8); body.Children.Add(headingPreferences);
            body.Children.Add(Ui.Row(L.T("Check automatically"), L.T("Check at startup and at the selected interval while DesktopTools is running."),
                Ui.Toggle(controller.Settings.AutomaticUpdateChecks, value => Change(s => s.AutomaticUpdateChecks = value))));
            string[] labels = ["At startup only", "Every 15 minutes", "Every 30 minutes", "Every hour", "Every 2 hours", "Every 3 hours", "Every 6 hours", "Every 12 hours", "Every day", "Every week"];
            double[] hours = [0, .25, .5, 1, 2, 3, 6, 12, 24, 168];
            int selected = Array.IndexOf(hours, controller.Settings.UpdateCheckHours);
            var interval = Ui.Choice(labels, labels[Math.Max(0, selected)], value => Change(s => s.UpdateCheckHours = hours[Array.IndexOf(labels, value)]));
            interval.Tag = "update-interval";
            body.Children.Add(Ui.Row(L.T("Check interval"), null, interval));
        }
        RefreshUpdateUi(); return Ui.Card(body);
    }
    private void RefreshUpdateUi()
    {
        bool available = controller.AvailableUpdate != null;
        if (updateIndicator != null)
        {
            updateIndicator.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            updateIndicator.Content = Ui.IconLabel("Refresh", L.T(controller.DownloadingUpdate ? "Updating…" : "Update available"));
        }
        if (updateStatusText != null) updateStatusText.Text = L.T(controller.UpdateStatus);
        if (updateVersionText != null) updateVersionText.Text = controller.AvailableUpdate?.Version ?? "—";
        if (updateProgressBar != null)
        {
            updateProgressBar.Visibility = controller.DownloadingUpdate ? Visibility.Visible : Visibility.Collapsed;
            double from = updateProgressBar.Value;
            double target = Math.Clamp(controller.UpdateDownloadProgress, 0, 1);
            updateProgressBar.BeginAnimation(RangeBase.ValueProperty, null);
            updateProgressBar.Value = target;
            if (Motion.Enabled && controller.DownloadingUpdate && Math.Abs(target - from) > .001)
                updateProgressBar.BeginAnimation(RangeBase.ValueProperty,
                    new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(200))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }
        if (updateCheckButton != null) updateCheckButton.IsEnabled = !controller.CheckingUpdate && !controller.DownloadingUpdate;
        if (updateDownloadButton != null) { updateDownloadButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed; updateDownloadButton.IsEnabled = !controller.DownloadingUpdate; }
        if (updateNotesButton != null) updateNotesButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
    }
}
