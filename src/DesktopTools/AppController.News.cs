using System.Windows;
using DesktopTools.Localization;
using DesktopTools.UI;
using DesktopTools.Updates;

namespace DesktopTools;

internal sealed partial class AppController
{
    internal AnnouncementClient NewsClient { get; set; } = new();
    internal IReadOnlyList<Announcement> NewsItems { get; private set; } = [];
    internal Announcement? ActiveSecurityAlert => NewsItems.Where(item => item.Affects(CurrentVersion))
        .OrderByDescending(item => Version.Parse(item.MinimumSafeVersion!)).FirstOrDefault();
    internal bool HasUnreadNews => NewsItems.Any(item => !item.IsSecurity && !Settings.ReadNewsIds.Contains(item.Id));
    internal bool CheckingNews { get; private set; }
    internal string NewsStatus { get; private set; } = "News is available offline. Check for newer announcements.";
    internal DateTimeOffset? LastNewsCheck { get; private set; }
    internal event Action? NewsChanged;

    private DateTimeOffset lastNewsAttempt = DateTimeOffset.MinValue;
    private DateTimeOffset securitySnoozeUntil = DateTimeOffset.MinValue;
    private string? snoozedSecurityId;
    private string? displayedSecurityId;
    private readonly HashSet<string> notifiedThisSession = new(StringComparer.Ordinal);
    private NotificationWindow? newsNotice, securityNotice;

    private void InitializeNews()
    {
        try { NewsItems = AnnouncementClient.LoadBundled().Items; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); NewsItems = []; }
        NewsChanged?.Invoke();
    }
    internal async Task CheckNewsAsync(bool manual = true)
    {
        if (disposed || CheckingNews) return;
        string previousStatus = NewsStatus;
        CheckingNews = true; lastNewsAttempt = DateTimeOffset.UtcNow;
        if (manual) NewsStatus = L.T("Checking news…");
        NewsChanged?.Invoke();
        try
        {
            var remote = await NewsClient.GetLatestAsync(updateLifetime.Token);
            if (disposed) return;
            if (remote != null) NewsItems = remote.Items;
            LastNewsCheck = DateTimeOffset.Now;
            NewsStatus = L.T(remote == null ? "Showing bundled news. No online feed is available yet." : "News is up to date.");
            NotifyNews();
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            NewsStatus = manual ? L.T("Could not check news. Showing saved announcements.") : previousStatus;
            // Background failures should not interrupt the user. Bundled and previously fetched items remain visible.
            if (!manual) NotifyNews();
        }
        finally { CheckingNews = false; if (!disposed) NewsChanged?.Invoke(); }
    }
    private void NotifyNews()
    {
        NotifySecurityIfNeeded();
        if (ActiveSecurityAlert != null) return;
        var unseen = NewsItems.Where(candidate => !candidate.IsSecurity &&
            !Settings.NotifiedNewsIds.Contains(candidate.Id) && !notifiedThisSession.Contains(candidate.Id)).ToArray();
        if (unseen.Length == 0) return;
        var item = unseen[0];
        foreach (var candidate in unseen) notifiedThisSession.Add(candidate.Id);
        UpdateSettings(settings => settings.NotifiedNewsIds = settings.NotifiedNewsIds
            .Concat(unseen.Select(candidate => candidate.Id)).Distinct(StringComparer.Ordinal).TakeLast(50).ToArray());
        if (newsNotice != null)
        {
            newsNotice.UpdateMessage(item.LocalTitle + "\n" + item.LocalMessage, seconds: Settings.NotificationSeconds);
            return;
        }
        var notice = new NotificationWindow(item.LocalTitle + "\n" + item.LocalMessage,
            NotificationKind.Info, main, Settings.MessageNotificationStyle,
            actions: [("Open news", OpenNews), ("Dismiss", () => newsNotice?.Close())], seconds: Settings.NotificationSeconds);
        notice.ClickAction = OpenNews;
        newsNotice = notice;
        notice.Closed += (_, _) => { if (ReferenceEquals(newsNotice, notice)) newsNotice = null; };
        notice.Show();
    }
    private void NotifySecurityIfNeeded()
    {
        var alert = ActiveSecurityAlert;
        if (alert == null)
        {
            var oldNotice = securityNotice; securityNotice = null; displayedSecurityId = null; oldNotice?.Close();
            if (updateNotice?.Kind == NotificationKind.Warning && !updatePromptOpen && !DownloadingUpdate)
            {
                updateNotice.Close();
                if (AvailableUpdate != null) { UpdateStatus = L.T("Update available") + " · " + AvailableUpdate.Version; ShowUpdateNotice(); }
            }
            return;
        }
        var ordinaryNotice = newsNotice; newsNotice = null; ordinaryNotice?.Close();
        if (securityNotice != null && displayedSecurityId != alert.Id)
        {
            var oldNotice = securityNotice; securityNotice = null; oldNotice.Close();
        }
        if (IsSecuritySnoozed(alert)) return;
        if (SafeSecurityReleaseAvailable)
        {
            var oldNotice = securityNotice; securityNotice = null; oldNotice?.Close();
            if (updatePromptOpen || DownloadingUpdate) return;
            if (updateNotice == null || updateNotice.Kind != NotificationKind.Warning) ShowUpdateNotice();
            return;
        }
        if (updateNotice != null && !updatePromptOpen && !DownloadingUpdate) updateNotice.Close();
        if (securityNotice != null) return;
        var notice = new NotificationWindow(alert.LocalTitle + "\n" + alert.LocalMessage,
            NotificationKind.Warning, main, Settings.MessageNotificationStyle,
            actions: [("View details", () => OpenNewsLink(alert)), ("Check for updates", () => _ = CheckForUpdatesAsync()),
                ("Remind me later", SnoozeSecurityNotice)], seconds: Settings.NotificationSeconds);
        notice.HoldOpen = true; notice.ClickAction = OpenNews;
        securityNotice = notice;
        displayedSecurityId = alert.Id;
        notice.Closed += (_, _) => { if (ReferenceEquals(securityNotice, notice)) { securityNotice = null; snoozedSecurityId = alert.Id; securitySnoozeUntil = DateTimeOffset.UtcNow.AddHours(1); } };
        notice.Show();
    }
    private void SnoozeSecurityNotice()
    {
        snoozedSecurityId = ActiveSecurityAlert?.Id;
        securitySnoozeUntil = DateTimeOffset.UtcNow.AddHours(1);
        if (SafeSecurityReleaseAvailable) updateNotice?.Close(); else securityNotice?.Close();
    }
    internal bool SafeSecurityReleaseAvailable => ActiveSecurityAlert is { MinimumSafeVersion: { } safe } &&
        AvailableUpdate != null && !GitHubReleaseClient.IsNewer(safe, AvailableUpdate.Version);
    private bool IsSecuritySnoozed(Announcement? alert) => alert != null && snoozedSecurityId == alert.Id &&
        DateTimeOffset.UtcNow < securitySnoozeUntil;
    internal void OpenNewsLink(Announcement item)
    {
        if (item.Url != null) OpenUpdateLink(item.Url);
    }
    internal void OpenNews()
    {
        OpenMain(); main?.Navigate("News");
    }
    internal void MarkNewsRead()
    {
        var unread = NewsItems.Where(item => !item.IsSecurity && !Settings.ReadNewsIds.Contains(item.Id)).Select(item => item.Id).ToArray();
        if (unread.Length == 0) return;
        UpdateSettings(settings => settings.ReadNewsIds = settings.ReadNewsIds.Concat(unread)
            .Distinct(StringComparer.Ordinal).TakeLast(50).ToArray());
        NewsChanged?.Invoke();
    }
    private void StopNewsChecks()
    {
        newsNotice?.Close(); securityNotice?.Close(); NewsClient.Dispose();
    }
}
