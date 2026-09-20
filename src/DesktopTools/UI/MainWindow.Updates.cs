using System.Windows;
using System.Windows.Controls;
using DesktopTools.Localization;
using DesktopTools.Updates;

namespace DesktopTools.UI;

internal sealed partial class MainWindow
{
    private Button? updateIndicator, updateDownloadButton, updateCheckButton, updateNotesButton;
    private TextBlock? updateStatusText;
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
        body.Children.Add(Ui.Text(L.T("Updates"), 18, true));
        body.Children.Add(Ui.Text(L.T("Stable releases from GitHub. Notes and settings are kept when updating."), 12, muted: true));
        updateStatusText = Ui.Text("", 13, true); updateStatusText.Tag = "update-status"; updateStatusText.Margin = new Thickness(0, 14, 0, 8); body.Children.Add(updateStatusText);
        updateProgressBar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 5, Margin = new Thickness(0, 0, 0, 12) }; body.Children.Add(updateProgressBar);
        var actions = new WrapPanel();
        updateCheckButton = Ui.Button(L.T("Check for updates"), async () => await controller.CheckForUpdatesAsync()); updateCheckButton.Tag = "check-updates";
        updateDownloadButton = Ui.Button(L.T("Update in background"), async () => await controller.BeginBackgroundUpdateAsync(), true); updateDownloadButton.Tag = "download-update";
        updateNotesButton = Ui.Button(L.T("Release notes"), controller.OpenUpdateNotes);
        actions.Children.Add(updateCheckButton); actions.Children.Add(updateDownloadButton); actions.Children.Add(updateNotesButton); body.Children.Add(actions);
        if (preferences)
        {
            body.Children.Add(Ui.Row(L.T("Check automatically"), L.T("Check at startup and at the selected interval while DesktopTools is running."),
                Ui.Toggle(controller.Settings.AutomaticUpdateChecks, value => Change(s => s.AutomaticUpdateChecks = value))));
            string[] labels = ["At startup only", "Every hour", "Every 6 hours", "Every 12 hours", "Every day", "Every week"];
            int[] hours = [0, 1, 6, 12, 24, 168];
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
        if (updateProgressBar != null) { updateProgressBar.Visibility = controller.DownloadingUpdate ? Visibility.Visible : Visibility.Collapsed; updateProgressBar.Value = controller.UpdateDownloadProgress; }
        if (updateCheckButton != null) updateCheckButton.IsEnabled = !controller.CheckingUpdate && !controller.DownloadingUpdate;
        if (updateDownloadButton != null) { updateDownloadButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed; updateDownloadButton.IsEnabled = !controller.DownloadingUpdate; }
        if (updateNotesButton != null) updateNotesButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
    }
}
