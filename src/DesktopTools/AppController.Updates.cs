using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;
using DesktopTools.Updates;
using Microsoft.Win32;

namespace DesktopTools;

internal sealed partial class AppController
{
    internal static string CurrentVersion => typeof(AppController).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    internal GitHubReleaseClient UpdateClient { get; set; } = new();
    internal Action<Uri> UpdateLinkLauncher { get; set; } = uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    private readonly CancellationTokenSource updateLifetime = new();
    private readonly DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private DateTimeOffset lastUpdateAttempt = DateTimeOffset.MinValue;
    private string? notifiedUpdate;
    private NotificationWindow? updateNotice;
    private bool updatePromptOpen;
    internal GitHubRelease? AvailableUpdate { get; private set; }
    internal bool CheckingUpdate { get; private set; }
    internal bool DownloadingUpdate { get; private set; }
    internal double UpdateDownloadProgress { get; private set; }
    internal string UpdateStatus { get; private set; } = "Check GitHub for updates.";
    internal event Action? UpdateChanged;
    private string UpdateDirectory => Path.Combine(smoke ? Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke-settings") :
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTools"), "Updates");

    internal void StartUpdateChecks()
    {
        if (smoke) return;
        updateTimer.Tick += async (_, _) =>
        {
            if (Settings.AutomaticUpdateChecks && Settings.UpdateCheckHours > 0 &&
                DateTimeOffset.UtcNow - lastUpdateAttempt >= TimeSpan.FromHours(Settings.UpdateCheckHours))
                await CheckForUpdatesAsync(false);
        };
        updateTimer.Start(); _ = CheckAfterStartupAsync();
    }
    private async Task CheckAfterStartupAsync()
    {
        try { await Task.Delay(10000, updateLifetime.Token); if (Settings.AutomaticUpdateChecks) await CheckForUpdatesAsync(false); }
        catch (OperationCanceledException) { }
    }
    internal async Task CheckForUpdatesAsync(bool manual = true)
    {
        if (disposed || CheckingUpdate || DownloadingUpdate) return;
        string previousStatus = UpdateStatus;
        CheckingUpdate = true; lastUpdateAttempt = DateTimeOffset.UtcNow;
        if (manual) UpdateStatus = L.T("Checking GitHub…");
        UpdateChanged?.Invoke();
        try
        {
            var release = await UpdateClient.GetLatestAsync(updateLifetime.Token);
            if (disposed) return;
            AcceptUpdate(release, manual);
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception ex)
        {
            if (manual)
            {
                UpdateStatus = L.T("Could not check for updates. Try again later.");
                if (!disposed) Report(UpdateStatus + " " + ex.Message, NotificationKind.Warning);
            }
            else UpdateStatus = previousStatus;
        }
        finally { CheckingUpdate = false; if (!disposed) UpdateChanged?.Invoke(); }
    }
    internal void AcceptUpdate(GitHubRelease? release, bool manual)
    {
        if (release == null || !GitHubReleaseClient.IsNewer(release.Version, CurrentVersion))
        { AvailableUpdate = null; UpdateStatus = L.T(release == null ? "No public release is available yet." : "DesktopTools is up to date."); }
        else
        {
            AvailableUpdate = release; UpdateStatus = L.T("Update available") + " · " + release.Version;
            if (manual || notifiedUpdate != release.Version) { notifiedUpdate = release.Version; ShowUpdateNotice(); }
        }
        UpdateChanged?.Invoke();
    }
    private void ShowUpdateNotice()
    {
        updateNotice?.Close();
        var notice = new NotificationWindow(UpdateStatus, NotificationKind.Info, main, Settings.MessageNotificationStyle,
            actions: [("Update in background", () => _ = BeginBackgroundUpdateAsync()), ("Release notes", OpenUpdateNotes), ("Dismiss", () => updateNotice?.Close())], seconds: Settings.NotificationSeconds);
        notice.ClickAction = () => _ = BeginBackgroundUpdateAsync();
        updateNotice = notice; notice.Closed += (_, _) => { if (ReferenceEquals(updateNotice, notice)) { updateNotice = null; updatePromptOpen = false; } }; notice.Show();
    }
    internal void OpenUpdateNotes()
    {
        if (AvailableUpdate == null) return;
        if (OpenUpdateLink(AvailableUpdate.NotesUrl))
            updateNotice?.UpdateMessage(L.T("Release notes opened in browser."), seconds: Math.Max(30, Settings.NotificationSeconds * 3));
    }
    internal bool OpenUpdateLink(Uri uri)
    {
        try { UpdateLinkLauncher(uri); return true; }
        catch (Exception ex) { Report(L.T("Could not open the browser.") + " " + ex.Message, NotificationKind.Warning); return false; }
    }
    internal Task BeginBackgroundUpdateAsync()
    {
        if (AvailableUpdate == null || DownloadingUpdate || disposed || updatePromptOpen) return Task.CompletedTask;
        bool installed;
        try
        {
            installed = IsRunningInstalledCopy();
            string warning = installed
                ? "DesktopTools will download the update, close and restart. Unsaved edits and current drawings will be lost. Saved notes and settings will be kept."
                : "This portable copy will close after downloading. Setup will open so you can install DesktopTools. Unsaved edits and current drawings will be lost; saved notes and settings will be kept.";
            if (updateNotice == null) ShowUpdateNotice();
            updatePromptOpen = true;
            updateNotice!.HoldOpen = true;
            updateNotice.ClickAction = null;
            updateNotice.UpdateMessage(L.T(warning), seconds: 30,
                actions: [("Download and update", () => _ = ConfirmBackgroundUpdateAsync(installed)), ("Cancel", CancelUpdatePrompt)]);
        }
        catch (Exception ex) { updatePromptOpen = false; ShowUpdateFailure(ex.Message, minimized: false); }
        return Task.CompletedTask;
    }
    private void CancelUpdatePrompt()
    {
        if (!updatePromptOpen) return;
        updatePromptOpen = false;
        if (updateNotice == null) return;
        updateNotice.HoldOpen = false;
        updateNotice.ClickAction = () => _ = BeginBackgroundUpdateAsync();
        updateNotice.UpdateMessage(UpdateStatus,
            actions: [("Update in background", () => _ = BeginBackgroundUpdateAsync()), ("Release notes", OpenUpdateNotes), ("Dismiss", () => updateNotice?.Close())]);
    }
    private async Task ConfirmBackgroundUpdateAsync(bool installed)
    {
        if (!updatePromptOpen || DownloadingUpdate || disposed) return;
        updatePromptOpen = false;
        await DownloadAndInstallUpdateAsync(installed);
    }
    private async Task DownloadAndInstallUpdateAsync(bool installed)
    {
        var release = AvailableUpdate; if (release == null || DownloadingUpdate) return;
        DownloadingUpdate = true; UpdateDownloadProgress = 0; UpdateStatus = L.T("Downloading update…");
        if (updateNotice == null) ShowUpdateNotice();
        updateNotice!.HoldOpen = true;
        updateNotice.UpdateMessage(UpdateStatus, 0, actions: []); UpdateChanged?.Invoke();
        try
        {
            var progress = new Progress<double>(value =>
            {
                if (disposed || !DownloadingUpdate) return;
                UpdateDownloadProgress = value; UpdateStatus = L.T("Downloading update…") + " " + Math.Round(value * 100) + "%";
                updateNotice?.UpdateMessage(UpdateStatus, value); UpdateChanged?.Invoke();
            });
            string installer = await UpdateClient.DownloadInstallerAsync(release, UpdateDirectory, progress, updateLifetime.Token);
            if (disposed) return;
            // Do not close a capture selection/dialog or interrupt a native recording during finalization.
            if (IsBusy) throw new InvalidOperationException(L.T("Finish the current capture or dialog, then try updating again."));
            UpdateStatus = L.T("Preparing background installation…"); updateNotice?.UpdateMessage(UpdateStatus, 1); UpdateChanged?.Invoke();
            if (utilityWindows.TryGetValue("Recorder", out var recorder)) await ((Extras.ScreenRecorderWindow)recorder).StopAsync();
            var start = new ProcessStartInfo(installer) { UseShellExecute = false, CreateNoWindow = true };
            // A named event gives the installer time to unpack, validate this process and take a process handle.
            // Keep the app alive until that validation succeeds; the installer then waits for this app to exit.
            string readyName = "Local\\DesktopTools.Update." + Guid.NewGuid().ToString("N");
            using var handoffReady = new EventWaitHandle(false, EventResetMode.ManualReset, readyName);
            start.ArgumentList.Add(installed ? "--background-update" : "--portable-update");
            start.ArgumentList.Add("--wait-for-exit"); start.ArgumentList.Add(Environment.ProcessId.ToString());
            start.ArgumentList.Add("--ready-event"); start.ArgumentList.Add(readyName);
            using var process = Process.Start(start) ?? throw new InvalidOperationException(L.T("Could not start the update installer."));
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (!handoffReady.WaitOne(0))
            {
                if (process.HasExited || DateTime.UtcNow >= deadline) throw new InvalidOperationException(L.T("Setup could not prepare the update. DesktopTools is still running."));
                await Task.Delay(100, updateLifetime.Token);
            }
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception ex) { if (!disposed) ShowUpdateFailure(ex.Message, minimized: false); }
        finally { DownloadingUpdate = false; if (!disposed) { if (updateNotice != null) updateNotice.HoldOpen = false; UpdateChanged?.Invoke(); } }
    }
    private bool IsRunningInstalledCopy()
    {
        using var registration = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopTools");
        return registration?.GetValue("InstallLocation") is string location &&
            string.Equals(Path.GetFullPath(Path.Combine(location, "DesktopTools.exe")), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
    }
    internal void ShowBackgroundUpdateError()
    {
        string result = Path.Combine(UpdateDirectory, "update-error.txt");
        string message = L.T("The background update could not be completed. Your previous installation was kept where possible.");
        try { if (File.Exists(result)) { if (new FileInfo(result).Length <= 16384) message = File.ReadAllText(result); File.Delete(result); } } catch (IOException) { } catch (UnauthorizedAccessException) { }
        ShowUpdateFailure(message, minimized: true);
    }
    private void ShowUpdateFailure(string error, bool minimized)
    {
        UpdateStatus = L.T("Update failed") + ": " + error;
        if (minimized) { main ??= new MainWindow(this); main.WindowState = WindowState.Minimized; main.Show(); }
        else if (main == null) OpenMain();
        if (updateNotice == null)
        {
            updateNotice = new NotificationWindow(L.T("Update failed"), NotificationKind.Info, main, Settings.MessageNotificationStyle);
            var notice = updateNotice; notice.Closed += (_, _) => { if (ReferenceEquals(updateNotice, notice)) updateNotice = null; }; notice.Show();
        }
        updateNotice.HoldOpen = false;
        updateNotice.UpdateMessage(UpdateStatus, kind: NotificationKind.Error, seconds: 30,
            actions: [("Open DesktopTools", OpenMain), ("Dismiss", () => updateNotice?.Close())]);
        try { if (!smoke) System.Media.SystemSounds.Exclamation.Play(); } catch (InvalidOperationException) { }
        if (main != null) UserActivity.Flash(new WindowInteropHelper(main).Handle);
        UpdateChanged?.Invoke();
    }
    private void StopUpdateChecks()
    {
        updateTimer.Stop(); updateLifetime.Cancel(); updateNotice?.Close(); UpdateClient.Dispose();
    }
}
