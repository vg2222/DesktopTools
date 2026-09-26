using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.UI;
using DesktopTools.Updates;

internal static class UpdateChecks
{
    private static void Check(bool value, string error) { if (!value) throw new Exception(error); }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static void Render(Window window, string path)
    {
        window.UpdateLayout(); var pixels = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); pixels.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(pixels)); using var output = File.Create(path); encoder.Save(output);
    }
    internal static async Task RunAsync()
    {
        string original = Environment.CurrentDirectory;
        string isolated = Path.Combine(original, "updates-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(isolated); Environment.CurrentDirectory = isolated;
        try
        {
            using var controller = new AppController(true);
            controller.UpdateSettings(s => { s.Animations = false; s.Language = "en"; s.Theme = "Dark"; });
            DesktopTools.Localization.L.Use("en");
            Check(controller.Settings.AutomaticUpdateChecks && controller.Settings.UpdateCheckHours == 1, "Default hourly checks missing");
            controller.OpenMain(); var main = Application.Current.Windows.OfType<MainWindow>().Single();
            string repo = GitHubReleaseClient.RepositoryUrl;
            string json = JsonSerializer.Serialize(new { draft = false, prerelease = false, tag_name = "v2.0.0", assets = new[] {
                new { name = "DesktopTools-2.0.0-win-x64-setup.exe", size = 100, browser_download_url = repo + "/releases/download/v2.0.0/DesktopTools-2.0.0-win-x64-setup.exe" },
                new { name = "SHA256SUMS.txt", size = 100, browser_download_url = repo + "/releases/download/v2.0.0/SHA256SUMS.txt" } } });
            using var http = new HttpClient(new FixtureHandler(json)); controller.UpdateClient.Dispose(); controller.UpdateClient = new GitHubReleaseClient(http);
            await controller.CheckForUpdatesAsync(); await Task.Delay(60); main.UpdateLayout();
            Check(controller.AvailableUpdate?.Version == "2.0.0" && !controller.CheckingUpdate, "Manual update check did not finish");
            var indicator = Walk(main).OfType<Button>().Single(b => Equals(b.Tag, "update-indicator")); Check(indicator.IsVisible, "Update missing from main navigation");
            var notice = Application.Current.Windows.OfType<NotificationWindow>().Single(); notice.UpdateLayout();
            Check(Walk(notice).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "Update"),
                "Update notification did not show its short update action");
            notice.ActivateBody();
            await Task.Delay(30);
            Check(notice.IsVisible && Application.Current.Windows.OfType<ConfirmationDialog>().Count() == 0,
                "Update click opened a separate app dialog instead of confirming in the notification");
            Check(Walk(notice).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "Download and update"),
                "Update notification did not show its confirmation action");
            Render(notice, Path.Combine(original, "update-confirmation.png"));
            Walk(notice).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Cancel")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(notice.IsVisible, "Cancelling notification confirmation dismissed the update offer");
            notice.UpdateLayout();
            Check(Walk(notice).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "Update"),
                "Cancelling confirmation did not restore the short update action");
            Render(notice, Path.Combine(original, "update-available.png"));
            Uri? opened = null; controller.UpdateLinkLauncher = uri => opened = uri;
            controller.OpenUpdateNotes(); notice.UpdateLayout();
            Check(opened?.AbsoluteUri == repo + "/releases/tag/v2.0.0" && notice.IsVisible, "Release notes closed notice or used wrong release");
            Check(Walk(notice).OfType<TextBlock>().Any(t => t.Text == "Release notes opened in browser."), "Release notes feedback missing");
            notice.Close(); Check(controller.AvailableUpdate != null && indicator.IsVisible, "Dismissing notification lost update access");
            indicator.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); main.UpdateLayout();
            var interval = Walk(main).OfType<ComboBox>().Single(c => Equals(c.Tag, "update-interval")); interval.SelectedItem = "Every 6 hours";
            var store = new SettingsStore(Path.Combine(isolated, "artifacts", "smoke-settings")); Check(store.Load().UpdateCheckHours == 6, "Update interval did not persist");
            interval.SelectedItem = "Every 15 minutes";
            Check(store.Load().UpdateCheckHours == .25, "15-minute update interval did not persist");
            interval.SelectedItem = "Every 30 minutes";
            Check(store.Load().UpdateCheckHours == .5, "30-minute update interval did not persist");
            controller.UpdateSettings(s => s.AutomaticUpdateChecks = false); Check(!store.Load().AutomaticUpdateChecks, "Automatic checks cannot be disabled");
            Render(main, Path.Combine(original, "update-settings.png"));
            foreach (string theme in new[] { "Dark", "Light" })
            {
                controller.UpdateSettings(s => s.Theme = theme);
                var progress = new NotificationWindow("Downloading update…", NotificationKind.Info) { HoldOpen = true }; progress.Show();
                try
                {
                    progress.UpdateMessage("Downloading update… 55%", .55, actions: []); progress.UpdateLayout();
                    Check(Walk(progress).OfType<ProgressBar>().Single().Value == .55, "Progress bar missing"); Render(progress, Path.Combine(original, "update-download-" + theme + ".png"));
                    progress.HoldOpen = false; progress.UpdateMessage("Update failed: connection interrupted. Try again.", kind: NotificationKind.Error, seconds: 30);
                    Check(progress.Kind == NotificationKind.Error && progress.IsVisible, "Failure state dismissed or not marked error");
                    if (theme == "Light") Check(((SolidColorBrush)((Border)progress.Content).Background).Color.G > 150, "Light-theme error tint makes dark text unreadable");
                    await Task.Delay(900); Render(progress, Path.Combine(original, "update-error-" + theme + ".png"));
                }
                finally { progress.Close(); }
            }
            var absent = new GitHubReleaseClient(new HttpClient(new FixtureHandler(null))); controller.UpdateClient.Dispose(); controller.UpdateClient = absent;
            await controller.CheckForUpdatesAsync(); Check(controller.AvailableUpdate == null, "Unavailable public release leaves obsolete update state");
            string quietStatus = controller.UpdateStatus;
            var seenStatuses = new List<string>(); controller.UpdateChanged += () => seenStatuses.Add(controller.UpdateStatus);
            controller.UpdateClient.Dispose(); controller.UpdateClient = new GitHubReleaseClient(new HttpClient(new FixtureHandler("invalid json")));
            await controller.CheckForUpdatesAsync(manual: false);
            Check(seenStatuses.All(status => status == quietStatus) && controller.UpdateStatus == quietStatus && !Application.Current.Windows.OfType<NotificationWindow>().Any(),
                "Failed automatic update check interrupted the user");
            string result = Path.Combine(isolated, "artifacts", "smoke-settings", "Updates", "update-error.txt"); Directory.CreateDirectory(Path.GetDirectoryName(result)!);
            File.WriteAllText(result, "Fixture install failed; previous app preserved."); controller.ShowBackgroundUpdateError();
            Check(main.WindowState == WindowState.Minimized && !File.Exists(result), "Installer error did not reopen minimized or consume its result");
            Check(Application.Current.Windows.OfType<NotificationWindow>().Single().Kind == NotificationKind.Error && controller.UpdateStatus.Contains("Fixture install failed"), "Installer failure was not surfaced");
        }
        finally { Environment.CurrentDirectory = original; }
    }
    private sealed class FixtureHandler(string? json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(json == null ? HttpStatusCode.NotFound : HttpStatusCode.OK) { Content = new StringContent(json ?? "") });
    }
}
