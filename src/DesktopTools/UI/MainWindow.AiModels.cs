using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Localization;

namespace DesktopTools.UI;

/// <summary>Settings → AI models: optional model packs, downloaded translation packs, and where models run.</summary>
internal sealed partial class MainWindow
{
    private const string AiModelsTab = "AI models";
    private const string ShortModelWarning = "These models are not part of DesktopTools. They were not created, trained, or owned by the DesktopTools project, and DesktopTools is not affiliated with or endorsed by their publishers.";
    private sealed class ModelDownload { internal double Progress; internal long Total; internal readonly CancellationTokenSource Cancel = new(); internal NotificationWindow? Notice; internal long LastNoticeTicks; }

    /// <summary>"Downloading… 12.4 MB of 98 MB (13%)": the bytes come from the pack's known total, since downloads report a fraction.</summary>
    private static string DownloadProgressText(ModelDownload download) =>
        L.F($"Downloading… {ModelPacks.FormatSize((long)(download.Progress * download.Total))} of {ModelPacks.FormatSize(download.Total)} ({download.Progress * 100:0}%)");
    private readonly Dictionary<string, ModelDownload> modelDownloads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (ProgressBar Bar, TextBlock Text)> modelProgressViews = new(StringComparer.Ordinal);
    private string? aiModelsStatus;
    private bool cancelDownloadsOnClose;

    private static bool MatchesAiModelsSearch(string query) =>
        new[] { L.T(AiModelsTab), AiModelsTab, L.T("AI models, model downloads, licences, translation packs, processor"), "AI models, model downloads, licences, translation packs, processor" }
            .Any(text => text.Contains(query, StringComparison.CurrentCultureIgnoreCase));

    private Button AiModelsSearchResult()
    {
        var button = Ui.Button(L.T(AiModelsTab), () => { settingsTab = AiModelsTab; featureSettingsQuery = ""; Navigate("Settings"); });
        button.Tag = "settings-result-ai-models";
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.MinHeight = 52; button.Padding = new Thickness(10, 7, 10, 7); button.Margin = new Thickness(0, 3, 0, 3);
        var row = new DockPanel();
        var arrow = Ui.Icon("Chevron", 15); DockPanel.SetDock(arrow, Dock.Right); row.Children.Add(arrow);
        var icon = Ui.Icon("Star", 20); icon.Margin = new Thickness(0, 0, 12, 0); DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon);
        var labels = new StackPanel();
        labels.Children.Add(Ui.Text(L.T(AiModelsTab), 13, true));
        labels.Children.Add(Ui.Text(L.T("Settings"), 11, muted: true));
        row.Children.Add(labels);
        button.Content = row;
        return button;
    }

    private void AiModelsSettings()
    {
        modelProgressViews.Clear();
        var intro = new StackPanel();
        var heading = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Ui.Icon("Star", 22); icon.Margin = new Thickness(0, 0, 12, 0); heading.Children.Add(icon);
        heading.Children.Add(Ui.Text(L.T(AiModelsTab), 20, true)); intro.Children.Add(heading);
        var description = Ui.Text(L.T("Optional models run only on this PC. Each one downloads from its publisher after you accept its licence."), 12, muted: true);
        description.Margin = new Thickness(0, 8, 0, 14); intro.Children.Add(description);
        // Graphics card uses Windows' own DirectML; it is offered only when Windows has a new enough copy.
        bool gpuSupported = AiGpu.IsSupported;
        string[] devices = gpuSupported ? ["Processor", "Graphics card"] : ["Processor"];
        string chosen = gpuSupported && controller.Settings.AiDevice == AiGpu.GraphicsCard ? "Graphics card" : "Processor";
        var runOn = Ui.Choice(devices, chosen, value => Change(x => x.AiDevice = value == "Graphics card" ? AiGpu.GraphicsCard : AiGpu.Processor), aiChoices: ["Graphics card"]);
        runOn.Tag = "ai-run-on";
        string runOnHelp = !gpuSupported
            ? L.T("Where AI models run. The graphics card needs DirectML 1.15 or later, which comes with current Windows 11 updates.")
            : AiRuntime.GraphicsCardFailed
                ? L.T("The graphics card could not run a model, so the processor is used until DesktopTools restarts.")
                : L.T("The processor works everywhere. The graphics card makes text detection and speech recognition much faster but uses more memory, including video memory. Other models stay on the processor.");
        intro.Children.Add(Ui.Row(L.T("Run on"), runOnHelp, runOn));
        intro.Children.Add(ModelsFolderRow());
        if (aiModelsStatus != null)
        {
            var status = Ui.Text(aiModelsStatus, 13, true); status.Tag = "ai-models-status"; status.Margin = new Thickness(0, 12, 0, 0);
            intro.Children.Add(status);
        }
        var introCard = Ui.Card(intro); introCard.Margin = new Thickness(0, 0, 0, 18); page.Children.Add(introCard);

        var modelRows = new List<UIElement>();
        foreach (var pack in ModelPacks.Catalog) modelRows.Add(ModelPackRow(pack));
        if (modelRows.Count == 0)
        {
            var none = Ui.Text(L.T("No other models are available yet."), 13, muted: true); none.Tag = "ai-models-none";
            modelRows.Add(none);
        }
        var warning = Ui.Text(L.T(ShortModelWarning), 12, muted: true); warning.Tag = "ai-models-warning";
        warning.Margin = new Thickness(0, 4, 0, 0);
        modelRows.Add(warning);
        Group(L.T("Optional models"), modelRows.ToArray());

        var translationRows = new List<UIElement>();
        var languages = TranslationPacks.Languages.ToDictionary(language => language.Code, language => language.Name);
        foreach (var pack in TranslationPacks.Downloaded()) translationRows.Add(TranslationPackRow(pack, languages));
        if (translationRows.Count == 0) translationRows.Add(Ui.Text(L.T("No translation packs downloaded. Text tools offers them when you translate."), 13, muted: true));
        Group(L.T("Translation packs"), translationRows.ToArray());
    }

    private bool movingModels;

    /// <summary>Where model packs are kept, with a folder picker and a way back to the default folder. Translation packs keep their own folder.</summary>
    private FrameworkElement ModelsFolderRow()
    {
        bool custom = !string.IsNullOrEmpty(controller.Settings.AiModelsFolder);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var change = Ui.Button(L.T("Choose folder"), () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L.T("Choose where to keep AI models") };
            if (dialog.ShowDialog(this) == true) MoveModels(ModelPacks.TargetFor(dialog.FolderName));
        });
        change.Tag = "ai-models-folder-change";
        buttons.Children.Add(change);
        if (custom)
        {
            var reset = Ui.Button(L.T("Use default"), () => MoveModels(null)); reset.Tag = "ai-models-folder-default"; reset.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(reset);
        }
        foreach (Button button in buttons.Children) button.IsEnabled = !movingModels && modelDownloads.Count == 0;
        var row = Ui.Row(L.T("AI models folder"), ModelPacks.Root, buttons, "Folder");
        row.Tag = "ai-models-folder";
        return row;
    }

    /// <summary>Moves the installed packs to <paramref name="target"/> (null: the default folder), then saves the setting. A notification shows the copy.</summary>
    private async void MoveModels(string? target)
    {
        if (movingModels || modelDownloads.Count > 0) return;
        string from = ModelPacks.Root, to = target ?? ModelPacks.DefaultRoot;
        if (string.Equals(Path.GetFullPath(from).TrimEnd('\\'), Path.GetFullPath(to).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
        if (!ConfirmationDialog.Ask(this, L.T("Move AI models?"), L.F($"Installed AI models and their licence acceptances move to {to}. The old folder is removed after everything is copied."), L.T("Move"))) return;
        movingModels = true; aiModelsStatus = L.T("Moving AI models…"); RefreshAiModels();
        NotificationWindow? notice = null;
        if (!controller.IsSmoke)
        {
            notice = new NotificationWindow(L.T("Moving AI models…"), NotificationKind.Info, this, controller.Settings.MessageNotificationStyle) { HoldOpen = true };
            notice.UpdateMessage(L.T("Moving AI models…"), 0); notice.Show();
        }
        long last = 0;
        var progress = new Progress<double>(value =>
        {
            long now = Environment.TickCount64; if (now - last < 250 && value < 1) return; last = now;
            notice?.UpdateMessage(L.F($"Moving AI models… {value * 100:0}%"), value);
        });
        try
        {
            await ModelPacks.MoveAsync(from, to, progress, CancellationToken.None);
            Change(x => x.AiModelsFolder = target ?? "");
            aiModelsStatus = L.F($"AI models are now kept in {to}.");
        }
        catch (Exception e) when (e is ModelPackException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            aiModelsStatus = L.F($"Could not move the AI models: {e.Message}");
        }
        finally
        {
            movingModels = false;
            if (notice != null)
            {
                notice.HoldOpen = false;
                bool moved = aiModelsStatus == L.F($"AI models are now kept in {to}.");
                notice.UpdateMessage(aiModelsStatus, moved ? 1 : null, moved ? NotificationKind.Info : NotificationKind.Error, controller.Settings.NotificationSeconds);
            }
            RefreshAiModels();
        }
    }

    private FrameworkElement ModelPackRow(ModelPack pack)
    {
        var state = ModelPacks.StateOf(pack);
        modelDownloads.TryGetValue(pack.Id, out var download);
        var row = new DockPanel { Tag = "ai-model-row-" + pack.Id, Margin = new Thickness(0, 2, 0, 2) };
        var actions = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(actions, Dock.Right); row.Children.Add(actions);
        var details = new StackPanel();
        details.Children.Add(Ui.Text(L.T(pack.Name), 14, true));
        var purpose = Ui.Text(L.T(pack.Purpose), 12, muted: true); purpose.Margin = new Thickness(0, 3, 0, 0); details.Children.Add(purpose);
        string stateText = download != null ? DownloadProgressText(download) : state switch
        {
            ModelPackState.Installed => L.T("Installed"),
            ModelPackState.Damaged => L.T("Damaged. Download it again to use it."),
            _ => L.T("Not downloaded")
        };
        var stateLine = Ui.Text(L.F($"Licence: {ModelPacks.LicenseName(pack.LicenseId)}") + " · " + stateText, 12, muted: state != ModelPackState.Damaged || download != null);
        stateLine.Tag = "ai-model-state-" + pack.Id; stateLine.Margin = new Thickness(0, 6, 0, 0); details.Children.Add(stateLine);
        string size = state == ModelPackState.Installed && download == null
            ? L.F($"Size on disk: {ModelPacks.FormatSize(ModelPacks.SizeOnDisk(pack))}")
            : L.F($"Download size: {ModelPacks.FormatSize(pack.TotalBytes)}");
        var sizeLine = Ui.Text(size, 12, muted: true); sizeLine.Margin = new Thickness(0, 2, 0, 0); details.Children.Add(sizeLine);
        if (download != null)
        {
            var bar = NotificationSurface.CreateProgressBar(download.Progress, null);
            bar.Tag = "ai-model-progress-" + pack.Id; bar.Margin = new Thickness(0, 8, 0, 0);
            details.Children.Add(bar);
            modelProgressViews[pack.Id] = (bar, stateLine);
        }
        row.Children.Add(details);

        Button Action(string iconName, string label, string tag, Action action, ButtonKind kind = ButtonKind.Secondary)
        {
            var button = Ui.ActionButton(iconName, label, action, kind); button.Tag = tag; button.Margin = new Thickness(6, 3, 0, 3);
            actions.Children.Add(button); return button;
        }
        if (download != null) Action("Close", L.T("Cancel"), "ai-model-cancel-" + pack.Id, () => download.Cancel.Cancel());
        else if (state == ModelPackState.Installed) Action("Clear", L.T("Remove"), "ai-model-remove-" + pack.Id, () => RemoveModelPack(pack));
        else Action("Save", L.T(state == ModelPackState.Damaged ? "Download again" : "Download"), "ai-model-download-" + pack.Id, () => DownloadModelPack(pack), ButtonKind.Primary);
        Action("File", L.T("View licence"), "ai-model-licence-" + pack.Id, () => OpenModelLicence(pack));
        return row;
    }

    private FrameworkElement TranslationPackRow(TranslationPack pack, IReadOnlyDictionary<string, string> languages)
    {
        string[] codes = pack.Direction.Split('-');
        string name = string.Join(" → ", codes.Select(code => languages.GetValueOrDefault(code, code)));
        var remove = Ui.ActionButton("Clear", L.T("Remove"), () => RemoveTranslationPack(pack, name));
        remove.Tag = "translation-remove-" + pack.Direction;
        string detail = L.F($"Licence: {ModelPacks.LicenseName(pack.License)}") + " · " + L.F($"Size on disk: {ModelPacks.FormatSize(TranslationPacks.SizeOnDisk(pack.Direction))}");
        var row = Ui.Row(name, detail, remove, "Translate");
        row.Tag = "translation-row-" + pack.Direction;
        return row;
    }

    private void RefreshAiModels()
    {
        if (currentPage == "Settings" && settingsTab == AiModelsTab) Navigate("Settings");
    }

    private async void DownloadModelPack(ModelPack pack)
    {
        if (modelDownloads.ContainsKey(pack.Id) || movingModels) return;
        var consents = ModelPacks.Consents();
        string name = L.T(pack.Name);
        try
        {
            // Nothing is requested until the person accepts the licence in this window.
            if (!ModelConsentWindow.Ask(this, pack, consents)) return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            aiModelsStatus = L.F($"Could not download {name}: {e.Message}"); RefreshAiModels(); return;
        }
        var download = new ModelDownload { Total = pack.TotalBytes };
        modelDownloads[pack.Id] = download;
        // A notification follows the download, so it stays visible after leaving the AI models page; it has its own Cancel.
        if (!controller.IsSmoke)
        {
            download.Notice = new NotificationWindow(L.T(pack.Name) + "\n" + DownloadProgressText(download), NotificationKind.Info, this, controller.Settings.MessageNotificationStyle,
                actions: [(L.T("Cancel"), () => download.Cancel.Cancel())]) { HoldOpen = true };
            download.Notice.Closed += (_, _) => download.Notice = null;
            download.Notice.UpdateMessage(L.T(pack.Name) + "\n" + DownloadProgressText(download), 0);
            download.Notice.Show();
        }
        if (!cancelDownloadsOnClose) { cancelDownloadsOnClose = true; Closed += (_, _) => { foreach (var item in modelDownloads.Values) item.Cancel.Cancel(); }; }
        aiModelsStatus = null; RefreshAiModels();
        var progress = new Progress<double>(value =>
        {
            download.Progress = Math.Clamp(value, 0, 1);
            if (modelProgressViews.TryGetValue(pack.Id, out var view))
            {
                view.Bar.Value = download.Progress;
                view.Text.Text = L.F($"Licence: {ModelPacks.LicenseName(pack.LicenseId)}") + " · " + DownloadProgressText(download);
            }
            // The notification rebuilds its surface on each update, so it changes at most about four times a second.
            long now = Environment.TickCount64;
            if (download.Notice != null && now - download.LastNoticeTicks >= 250)
            {
                download.LastNoticeTicks = now;
                download.Notice.UpdateMessage(L.T(pack.Name) + "\n" + DownloadProgressText(download), download.Progress);
            }
        });
        try
        {
            await ModelPacks.InstallAsync(pack, consents, progress, download.Cancel.Token);
            aiModelsStatus = L.F($"{name} is ready.");
        }
        catch (OperationCanceledException) { aiModelsStatus = L.T("Download canceled."); }
        catch (Exception e) when (e is ModelPackException or HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            aiModelsStatus = L.F($"Could not download {name}: {e.Message}");
        }
        finally
        {
            modelDownloads.Remove(pack.Id);
            if (download.Notice is { } notice)
            {
                bool ready = aiModelsStatus == L.F($"{name} is ready.");
                notice.HoldOpen = false;
                notice.UpdateMessage(aiModelsStatus ?? "", ready ? 1 : null, ready ? NotificationKind.Info : aiModelsStatus == L.T("Download canceled.") ? NotificationKind.Info : NotificationKind.Error,
                    controller.Settings.NotificationSeconds, actions: []);
            }
            download.Cancel.Dispose();
            RefreshAiModels();
        }
    }

    private void RemoveModelPack(ModelPack pack)
    {
        string name = L.T(pack.Name);
        if (!ConfirmationDialog.Ask(this, L.F($"Remove {name}?"), L.T("The model files are deleted from this PC and your licence acceptance is removed. You can download the model again later."), L.T("Remove"))) return;
        try { ModelPacks.Remove(pack, ModelPacks.Consents()); aiModelsStatus = L.F($"{name} was removed."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { aiModelsStatus = L.F($"Could not remove {name}: {e.Message}"); }
        RefreshAiModels();
    }

    private void RemoveTranslationPack(TranslationPack pack, string name)
    {
        if (!ConfirmationDialog.Ask(this, L.F($"Remove {name}?"), L.T("The language pack files are deleted from this PC. Text tools offers the pack again when you translate."), L.T("Remove"))) return;
        try { TranslationPacks.Remove(pack.Direction); aiModelsStatus = L.F($"{name} was removed."); }
        catch (Exception e) when (e is ModelPackException or IOException or UnauthorizedAccessException) { aiModelsStatus = L.F($"Could not remove {name}: {e.Message}"); }
        RefreshAiModels();
    }

    /// <summary>Opens the licence copy kept with the installed pack, or the one shipped with the app.</summary>
    private void OpenModelLicence(ModelPack pack)
    {
        string? installed = ModelPacks.DirectoryFor(pack) is { } directory ? Path.Combine(directory, "LICENSE.txt") : null;
        string path = installed != null && File.Exists(installed) ? installed : Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "Licenses", pack.LicenseFile);
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true })?.Dispose(); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException) { aiModelsStatus = e.Message; RefreshAiModels(); }
    }
}
