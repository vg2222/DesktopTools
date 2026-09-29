using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
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

internal static class NewsChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static string Feed(params object[] items) => JsonSerializer.Serialize(new { schemaVersion = 1, items });
    private static object Item(string id, string kind = "news", string? safeVersion = null) => new
    {
        id, kind, publishedUtc = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O"),
        title = new { en = kind == "security" ? "Important security fix" : "A new feature" },
        message = new { en = kind == "security" ? "Please install the corrected version." : "Read the latest DesktopTools news." },
        url = GitHubReleaseClient.RepositoryUrl + (kind == "security" ? "/security/advisories/GHSA-example" : ""),
        minimumSafeVersion = safeVersion
    };
    private static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Render(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
    internal static async Task RunAsync()
    {
        string original = Environment.CurrentDirectory;
        string isolated = Path.Combine(original, "news-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(isolated);
        Environment.CurrentDirectory = isolated;
        try
        {
            var bundled = AnnouncementClient.LoadBundled();
            Check(bundled.Items.Any(item => item.Id == "news-center-1-2-6") &&
                bundled.Items.Any(item => item.Id == "star-desktoptools-2026-09"), "Bundled announcements missing");
            foreach (string language in DesktopTools.Localization.L.Languages)
            {
                DesktopTools.Localization.L.Use(language);
                Check(bundled.Items.All(item => item.LocalTitle.Length > 0 && item.LocalMessage.Length > 0),
                    "Bundled news has no text in " + language);
                if (language != "en")
                    Check(DesktopTools.Localization.L.T("Latest announcements") != "Latest announcements" &&
                        DesktopTools.Localization.L.T("Security notice") != "Security notice",
                        "News UI labels are not localized in " + language);
            }
            DesktopTools.Localization.L.Use("en");
            string ordinary = Feed(Item("normal-1"));
            Check(AnnouncementFeed.Parse(Encoding.UTF8.GetBytes(ordinary)).Items.Count == 1, "Valid feed rejected");
            foreach (string invalid in new[]
            {
                Feed(Item("repeat"), Item("repeat")),
                Feed(new { id = "external", kind = "news", publishedUtc = DateTimeOffset.UtcNow.ToString("O"), title = new { en = "Title" }, message = new { en = "Message" }, url = "https://example.com/redirect" }),
                Feed(Item("unsafe-version", "security", "not-a-version"))
            })
            {
                try { AnnouncementFeed.Parse(Encoding.UTF8.GetBytes(invalid)); throw new Exception("Unsafe feed accepted"); }
                catch (Exception ex) when (ex is InvalidDataException or FormatException or JsonException) { }
            }
            using var controller = new AppController(true);
            controller.UpdateSettings(s => { s.Language = "en"; s.Theme = "Dark"; s.Animations = false; s.ReadNewsIds = []; s.NotifiedNewsIds = []; });
            Check(Application.Current.Resources["Surface"] is SolidColorBrush darkSurface && darkSurface.Color == Color.FromRgb(6, 6, 6),
                "Default dark app surface is not #060606");
            DesktopTools.Localization.L.Use("en");
            controller.OpenMain(); var main = Application.Current.Windows.OfType<MainWindow>().Single();
            controller.NewsClient.Dispose(); controller.NewsClient = new AnnouncementClient(new HttpClient(new FixtureHandler(ordinary)));
            await controller.CheckNewsAsync(false); await Task.Delay(70);
            Check(controller.NewsItems.Single().Id == "normal-1" && controller.HasUnreadNews, "New announcement was not retained as unread");
            Check(Application.Current.Windows.OfType<NotificationWindow>().Any(n => n.Kind == NotificationKind.Info), "New announcement was not notified");
            main.Navigate("Home"); Render(main, Path.Combine(original, "news-home-dark.png"));
            main.Navigate("News"); main.UpdateLayout();
            Check(!controller.HasUnreadNews && Walk(main).OfType<TextBlock>().Any(t => t.Text == "A new feature"), "News page or read state missing");
            var itemTitle = Walk(main).OfType<TextBlock>().Single(t => t.Text == "A new feature");
            var detailsAction = Walk(main).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "View details");
            var pageSubtitle = Walk(main).OfType<TextBlock>().Single(t => t.Text == "Announcements and important update notices.");
            double ItemX(FrameworkElement element) => element.TransformToAncestor(main).Transform(new Point()).X;
            double ItemY(FrameworkElement element) => element.TransformToAncestor(main).Transform(new Point()).Y;
            Check(Math.Abs(ItemX(detailsAction) - ItemX(itemTitle)) <= 1,
                "News details action is indented past the announcement text.");
            Check(ItemY(itemTitle) - ItemY(pageSubtitle) < 100,
                "The separate Check news row pushes announcements too far below the page description.");
            var pageTitle = Walk(main).OfType<TextBlock>().Single(t => t.Text == "News" && t.FontSize >= 27);
            var checkAction = Walk(main).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Check news");
            double pageTitleCenter = ItemY(pageTitle) + pageTitle.ActualHeight / 2;
            double checkActionCenter = ItemY(checkAction) + checkAction.ActualHeight / 2;
            Check(Math.Abs(pageTitleCenter - checkActionCenter) < 35,
                "Check news should live beside the page title instead of in a separate row.");
            var autoCheckTitle = Walk(main).OfType<TextBlock>().Single(t => t.Text == "Check news automatically");
            var autoCheckSwitch = Walk(main).OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "Check news automatically");
            double titleCenter = ItemY(autoCheckTitle) + autoCheckTitle.ActualHeight / 2;
            double switchCenter = ItemY(autoCheckSwitch) + autoCheckSwitch.ActualHeight / 2;
            Check(Math.Abs(titleCenter - switchCenter) <= 3,
                "Automatic news switch does not align with its title.");
            Check(!Walk(main).OfType<TextBlock>().Any(t => t.Text.StartsWith("News is up to date.") && t.Visibility == Visibility.Visible),
                "Routine news status adds unnecessary page copy.");
            Render(main, Path.Combine(original, "news-page-dark.png"));
            controller.NewsClient.Dispose(); controller.NewsClient = new AnnouncementClient(new HttpClient(new FixtureHandler("", HttpStatusCode.NotFound)));
            await controller.CheckNewsAsync(false); main.UpdateLayout();
            Check(!Walk(main).OfType<TextBlock>().Any(t => t.Text.StartsWith("Showing bundled news.") && t.Visibility == Visibility.Visible),
                "Bundled-news fallback status should not crowd the page.");
            var saved = new SettingsStore(Path.Combine(isolated, "artifacts", "smoke-settings")).Load();
            Check(saved.ReadNewsIds.Contains("normal-1") && saved.NotifiedNewsIds.Contains("normal-1"), "News read/notified state not persisted");
            foreach (var notice in Application.Current.Windows.OfType<NotificationWindow>().ToArray()) notice.Close();

            string urgent = Feed(Item("security-1", "security", "9.0.0"));
            controller.NewsClient.Dispose(); controller.NewsClient = new AnnouncementClient(new HttpClient(new FixtureHandler(urgent)));
            await controller.CheckNewsAsync(false); await Task.Delay(70);
            Check(controller.ActiveSecurityAlert?.Id == "security-1", "Affected installation did not retain the security alert");
            var warning = Application.Current.Windows.OfType<NotificationWindow>().Single();
            Check(warning.Kind == NotificationKind.Warning && warning.HoldOpen, "Security notification is not persistent");
            Check(!Walk(warning).OfType<Button>().Any(b => AutomationProperties.GetName(b) == "Update"),
                "Security alert offered installation before a fixed stable release existed");
            main.Navigate("News"); Render(main, Path.Combine(original, "news-security-dark.png"));
            var fixedRelease = new GitHubRelease("9.0.0", new Uri(GitHubReleaseClient.RepositoryUrl + "/releases/tag/v9.0.0"),
                new Uri(GitHubReleaseClient.RepositoryUrl + "/releases/download/v9.0.0/DesktopTools-9.0.0-win-x64-setup.exe"),
                "DesktopTools-9.0.0-win-x64-setup.exe", 100,
                new Uri(GitHubReleaseClient.RepositoryUrl + "/releases/download/v9.0.0/SHA256SUMS.txt"));
            controller.AcceptUpdate(fixedRelease, false); await Task.Delay(50);
            Check(controller.SafeSecurityReleaseAvailable, "Stable fixed release did not activate emergency update path");
            var urgentNotice = Application.Current.Windows.OfType<NotificationWindow>().Single();
            Check(urgentNotice.Kind == NotificationKind.Warning && urgentNotice.HoldOpen &&
                Walk(urgentNotice).OfType<Button>().Any(b => AutomationProperties.GetName(b) == "Update"),
                "Emergency update notice lacks a safe update action");
            urgentNotice.ActivateBody(); urgentNotice.UpdateLayout();
            Check(Walk(urgentNotice).OfType<Button>().Any(b => AutomationProperties.GetName(b) == "Download and update"),
                "Emergency notice skipped the existing restart/download confirmation");
            Check(Application.Current.Windows.OfType<ConfirmationDialog>().Count() == 0, "Emergency update opened an unrelated dialog");
            Render(urgentNotice, Path.Combine(original, "news-emergency-confirmation.png"));
        }
        finally { Environment.CurrentDirectory = original; }
    }
    private sealed class FixtureHandler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
    }
}
