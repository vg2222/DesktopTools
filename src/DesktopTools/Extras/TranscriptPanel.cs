using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using DesktopTools.Core;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;

namespace DesktopTools.Extras;

/// <summary>
/// The video editor's Transcript tab: makes a transcript on this PC with an optional speech model, saves it as captions next to the video,
/// and lets the user search, review and correct it. Edits are kept in app data; caption files change only when the user saves them.
/// Nothing transcribes on its own.
/// </summary>
internal sealed class TranscriptPanel : DockPanel
{
    internal const string NoAudioText = "This video has no audio track to transcribe.";
    internal const string NoSpeechText = "No speech was found.";
    internal const string NotSavedText = "Captions were not saved next to the video. The transcript is kept in DesktopTools.";
    internal const string LimitationText = "Made by a speech model on this PC. It can mishear words or invent text during silence; check it before you rely on it.";

    // Test seams: the licence dialog and the confirmation dialogs.
    internal static Func<Window?, ModelPack, ModelConsentStore, bool> AskConsent { get; set; } = ModelConsentWindow.Ask;
    internal static Func<Window?, string, string, string, bool> Confirm { get; set; } =
        (owner, title, message, confirm) => owner != null && ConfirmationDialog.Ask(owner, title, message, confirm);

    private readonly AppController? controller;
    private readonly TranscriptStore store;
    private readonly Func<TimeSpan> position;
    private readonly Action<TimeSpan> seek;
    private readonly Action<string> report;
    private readonly ComboBox model = new() { MinWidth = 150 }, language = new() { MinWidth = 150 };
    private readonly Button primary, cancel, saveCaptions;
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 1, Height = 3, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 4) };
    private readonly TextBlock status = Ui.Text("", 12, muted: true);
    private readonly TextBox search = new() { MinHeight = 34, Padding = new Thickness(8, 5, 8, 5) };
    private readonly ObservableCollection<Row> rows = new();
    private readonly ICollectionView view;
    private readonly ItemsControl list = new();
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private VideoInfo? video;
    private TranscriptDocument? document;
    private CancellationTokenSource? operation;
    private bool synchronizing;
    private static bool noticeAcceptedThisSession;

    internal TranscriptPanel(AppController? controller, TranscriptStore store, Func<TimeSpan> position, Action<TimeSpan> seek, Action<string> report)
    {
        this.controller = controller; this.store = store; this.position = position; this.seek = seek; this.report = report;
        LastChildFill = true;

        var header = new StackPanel();
        var choices = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        choices.Children.Add(Field(L.T("Model"), model));
        choices.Children.Add(Field(L.T("Spoken language"), language));
        header.Children.Add(choices);
        foreach (string code in new[] { "auto" }.Concat(WhisperDecoding.KnownLanguages.OrderBy(LanguageName, StringComparer.CurrentCultureIgnoreCase)))
            language.Items.Add(new ComboBoxItem { Content = code == "auto" ? L.T("Automatic") : LanguageName(code), Tag = code });
        AutomationProperties.SetName(model, L.T("Model")); AutomationProperties.SetName(language, L.T("Spoken language"));
        model.SelectionChanged += (_, _) =>
        {
            if (synchronizing || SelectedSize is not { } size) return;
            controller?.UpdateSettings(s => s.TranscriptModel = size.ToString());
            RefreshActions();
        };
        language.SelectionChanged += (_, _) =>
        {
            if (synchronizing || (language.SelectedItem as ComboBoxItem)?.Tag is not string code) return;
            controller?.UpdateSettings(s => s.TranscriptLanguage = code);
        };

        var actions = new WrapPanel();
        primary = Ui.Button(L.T("Transcribe"), async () => await PrimaryAsync(), true); primary.Tag = "transcript-primary";
        cancel = Ui.ActionButton("Close", L.T("Cancel"), Cancel, ButtonKind.Ghost); cancel.Visibility = Visibility.Collapsed;
        saveCaptions = Ui.ActionButton("Save", L.T("Save captions"), SaveCaptions); saveCaptions.Tag = "transcript-save";
        foreach (var button in new[] { primary, cancel, saveCaptions }) { button.Margin = new Thickness(0, 0, 6, 6); actions.Children.Add(button); }
        header.Children.Add(actions);
        header.Children.Add(progress);
        status.TextWrapping = TextWrapping.Wrap; status.Tag = "transcript-status"; header.Children.Add(status);

        var searchRow = new DockPanel { Margin = new Thickness(0, 10, 0, 8) };
        var searchIcon = Ui.Icon("Search", 15); searchIcon.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(searchIcon, Dock.Left); searchRow.Children.Add(searchIcon);
        searchRow.Children.Add(search);
        AutomationProperties.SetName(search, L.T("Search the transcript")); search.ToolTip = L.T("Search the transcript");
        header.Children.Add(searchRow);
        DockPanel.SetDock(header, Dock.Top); Children.Add(header);

        var limitation = Ui.Text(L.T(LimitationText), 11, muted: true);
        limitation.TextWrapping = TextWrapping.Wrap; limitation.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(limitation, Dock.Bottom); Children.Add(limitation);

        view = CollectionViewSource.GetDefaultView(rows);
        view.Filter = item => item is Row row && (search.Text.Length == 0 || row.Text.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase));
        search.TextChanged += (_, _) => view.Refresh();
        list.ItemsSource = view; list.ItemTemplate = RowTemplate(); list.Focusable = false;
        list.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        var scrollHost = new FrameworkElementFactory(typeof(ScrollViewer));
        scrollHost.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scrollHost.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        scrollHost.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scrollHost.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        list.Template = new ControlTemplate(typeof(ItemsControl)) { VisualTree = scrollHost };
        VirtualizingPanel.SetIsVirtualizing(list, true); VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        AutomationProperties.SetName(list, L.T("Transcript"));
        Children.Add(list);

        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveEdits(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) RevealCurrent(); };
        if (controller != null) controller.SettingsChanged += SettingsChanged;
        Unloaded += (_, _) => { if (controller != null) controller.SettingsChanged -= SettingsChanged; };
        Loaded += (_, _) => { if (controller != null) { controller.SettingsChanged -= SettingsChanged; controller.SettingsChanged += SettingsChanged; } };
        SettingsChanged();
    }

    internal bool IsBusy => operation != null;
    internal IReadOnlyList<TranscriptLine> Lines => rows.Select(row => row.Line).ToArray();
    internal string StatusText => status.Text;
    internal string PrimaryLabel => primary.Content is string text ? text : "";
    /// <summary>The transcript now in the panel for <paramref name="videoPath"/>, with unsaved edits; null when it shows another video or none.</summary>
    internal IReadOnlyList<TranscriptLine>? LinesFor(string videoPath) =>
        video != null && string.Equals(video.Path, videoPath, StringComparison.OrdinalIgnoreCase) && rows.Count > 0 ? Lines : null;

    internal void Cancel() => operation?.Cancel();

    /// <summary>Stops following the app's settings; the editor calls this when it closes, since Unloaded is not guaranteed then.</summary>
    internal void Detach() { if (controller != null) controller.SettingsChanged -= SettingsChanged; }

    /// <summary>Saves edits that are still waiting for the idle delay.</summary>
    internal void Flush() { if (saveTimer.IsEnabled) { saveTimer.Stop(); SaveEdits(); } }

    internal async Task LoadVideoAsync(VideoInfo? next)
    {
        Flush();
        if (!ReferenceEquals(video, next)) operation?.Cancel();
        video = next; document = null; search.Text = "";
        ShowLines([]);
        if (next != null)
        {
            string path = next.Path;
            var loaded = await Task.Run(() => store.Load(path));
            if (ReferenceEquals(video, next)) { document = loaded; ShowLines(loaded?.Lines ?? []); }
        }
        if (operation == null) status.Text = video == null ? "" : rows.Count > 0
            ? L.F($"Lines: {rows.Count}. Choose a time to go there; edit text to correct it.")
            : L.T("No transcript yet. Transcribe makes one on this PC; nothing is sent anywhere.");
        RefreshActions();
    }

    private async Task PrimaryAsync()
    {
        bool download = SelectedSize is { } size && !SpeechTranscriber.IsInstalled(size);
        try
        {
            if (download) await DownloadAsync(SelectedSize!.Value);
            else await TranscribeAsync();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The button handler must never take the app down: an out-of-memory model load or an unexpected model format ends here.
            AiRuntime.ReleaseAll();
            status.Text = download ? L.F($"Could not download the model: {e.Message}") : L.F($"Transcription failed: {e.Message}");
            RefreshActions();
        }
    }

    internal async Task TranscribeAsync()
    {
        if (video == null || operation != null || SelectedSize is not { } size) return;
        if (!SpeechTranscriber.IsInstalled(size)) { status.Text = Unavailable(size); RefreshActions(); return; }
        if (!NoticeAccepted())
        {
            if (!Confirm(Window.GetWindow(this), L.T("Before you transcribe"),
                L.T("Recording and transcribing other people can require their consent, depending on where you live. DesktopTools transcribes only on this PC and only when you ask."),
                L.T("Continue"))) return;
            noticeAcceptedThisSession = true;
            controller?.UpdateSettings(s => s.TranscriptNoticeAccepted = true);
        }
        if (rows.Count > 0 && !Confirm(Window.GetWindow(this), L.T("Replace the transcript?"),
            L.T("Transcribing again replaces the current transcript and your corrections."), L.T("Replace"))) return;
        Flush();
        var target = video; string? requested = (language.SelectedItem as ComboBoxItem)?.Tag is string code && code != "auto" ? code : null;
        var cancellation = Begin(L.T("Preparing the transcript…"));
        try
        {
            var indicator = new Progress<TranscriptProgress>(value =>
            {
                if (!ReferenceEquals(operation, cancellation)) return;
                progress.IsIndeterminate = false;
                progress.Value = value.Total > TimeSpan.Zero ? Math.Clamp(value.Processed / value.Total, 0, 1) : 0;
                status.Text = L.F($"Transcribed {RecordingTime.Format(value.Processed, compact: true)} of {RecordingTime.Format(value.Total, compact: true)}");
            });
            var result = await SpeechTranscriber.TranscribeAsync(target.Path, size, requested, indicator, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(video, target)) return;
            if (result.Lines.Count == 0) { status.Text = L.T(NoSpeechText); return; }
            string? previousCaption = store.Load(target.Path)?.CaptionSha256;
            document = new TranscriptDocument(result.Lines, result.Language, SpeechTranscriber.PackId(size), previousCaption);
            ShowLines(result.Lines);
            string? written;
            try
            {
                store.Save(target.Path, document);
                written = store.WriteCaptionFile(target.Path, result.Lines, ConfirmReplace);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                status.Text = L.F($"The transcript could not be saved: {e.Message}"); return;
            }
            document = store.Load(target.Path) ?? document;
            status.Text = written == null ? L.T(NotSavedText) : L.F($"Transcript ready. Lines: {result.Lines.Count}. Captions saved as {Path.GetFileName(written)}.");
            report(L.T("Transcript ready."));
        }
        catch (OperationCanceledException) { if (ReferenceEquals(video, target)) status.Text = L.T("Transcription canceled."); }
        catch (NoAudioTrackException) { if (ReferenceEquals(video, target)) status.Text = L.T(NoAudioText); }
        catch (ModelPackUnavailableException e) { if (ReferenceEquals(video, target)) status.Text = e.Message; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException
            or System.Runtime.InteropServices.COMException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException)
        {
            if (ReferenceEquals(video, target)) status.Text = L.F($"Transcription failed: {e.Message}");
        }
        finally { End(cancellation); }
    }

    private async Task DownloadAsync(TranscriptModelSize size)
    {
        if (operation != null || SpeechTranscriber.Pack(size) is not { } pack) return;
        var consents = ModelPacks.Consents();
        try { if (!AskConsent(Window.GetWindow(this), pack, consents)) { status.Text = L.T("Download canceled."); return; } }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = L.F($"Could not download the model: {e.Message}"); return; }
        var cancellation = Begin(L.F($"Downloading {L.T(pack.Name)}…"));
        try
        {
            progress.IsIndeterminate = false;
            await ModelPacks.InstallAsync(pack, consents, new Progress<double>(value => { if (ReferenceEquals(operation, cancellation)) progress.Value = value; }), cancellation.Token);
            status.Text = L.T("Model ready. Transcripts are made on this PC.");
        }
        catch (OperationCanceledException) { status.Text = L.T("Download canceled."); }
        catch (Exception e) when (e is ModelPackException or IOException or UnauthorizedAccessException or InvalidOperationException or System.Net.Http.HttpRequestException)
        {
            status.Text = L.F($"Could not download the model: {e.Message}");
        }
        finally { End(cancellation); }
    }

    private void SaveCaptions()
    {
        if (video == null || rows.Count == 0 || operation != null) return;
        Flush();
        var lines = Lines;
        string folder = Path.GetDirectoryName(video.Path) ?? "", name = Path.GetFileNameWithoutExtension(video.Path);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = L.T("Save captions"), Filter = L.T("SubRip captions|*.srt|WebVTT captions|*.vtt|Plain text|*.txt"),
            DefaultExt = ".srt", AddExtension = true, FileName = name + ".srt", InitialDirectory = folder, OverwritePrompt = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        string output = dialog.FileName;
        // Only caption and text files are written, so a typed name can never replace the video or another media file.
        if (Path.GetExtension(output).ToLowerInvariant() is not (".srt" or ".vtt" or ".txt") || string.Equals(Path.GetFullPath(output), Path.GetFullPath(video.Path), StringComparison.OrdinalIgnoreCase))
        {
            status.Text = L.T("Choose a file name ending in .srt, .vtt or .txt."); return;
        }
        try
        {
            var format = Path.GetExtension(output).ToLowerInvariant() switch { ".vtt" => CaptionFormat.Vtt, ".txt" => CaptionFormat.Text, _ => CaptionFormat.Srt };
            // The caption file next to the video goes through the store so it keeps track of what it wrote; the dialog already asked before replacing.
            if (format == CaptionFormat.Srt && string.Equals(Path.GetFullPath(output), TranscriptStore.CaptionPath(video.Path), StringComparison.OrdinalIgnoreCase))
                store.WriteCaptionFile(video.Path, lines, _ => true);
            else File.WriteAllText(output, CaptionFiles.Format(lines, format), new System.Text.UTF8Encoding(true));
            status.Text = L.F($"Captions saved: {output}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { status.Text = L.F($"Captions could not be saved: {e.Message}"); }
    }

    private bool ConfirmReplace(string path) => Confirm(Window.GetWindow(this), L.T("Replace the caption file?"),
        L.F($"{Path.GetFileName(path)} is already next to the video and was not written by DesktopTools. Replace it with the new transcript?"), L.T("Replace"));

    private bool NoticeAccepted() => noticeAcceptedThisSession || controller?.Settings.TranscriptNoticeAccepted == true;

    private CancellationTokenSource Begin(string message)
    {
        var cancellation = operation = new CancellationTokenSource();
        status.Text = message; progress.Value = 0; progress.IsIndeterminate = true; progress.Visibility = Visibility.Visible;
        RefreshActions();
        return cancellation;
    }

    private void End(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(operation, cancellation)) { operation = null; progress.Visibility = Visibility.Collapsed; }
        cancellation.Dispose();
        RefreshActions();
    }

    private void SettingsChanged()
    {
        synchronizing = true;
        try
        {
            string wantedModel = controller?.Settings.TranscriptModel ?? (SelectedSize ?? TranscriptModelSize.Fast).ToString();
            string wantedLanguage = controller?.Settings.TranscriptLanguage ?? ((language.SelectedItem as ComboBoxItem)?.Tag as string ?? "auto");
            RefreshModelChoices(wantedModel);
            language.SelectedItem = language.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == wantedLanguage) ?? language.Items[0];
        }
        finally { synchronizing = false; }
        RefreshActions();
    }

    private void RefreshModelChoices(string selected)
    {
        bool wasSynchronizing = synchronizing; synchronizing = true;
        try
        {
            model.Items.Clear();
            foreach (var size in Enum.GetValues<TranscriptModelSize>())
            {
                string label = L.T(size == TranscriptModelSize.Accurate ? "Accurate" : "Fast");
                if (!SpeechTranscriber.IsInstalled(size)) label = L.F($"{label} (not downloaded)");
                var item = new ComboBoxItem { Content = label, Tag = size };
                model.Items.Add(item);
                if (size.ToString() == selected) model.SelectedItem = item;
            }
            model.SelectedItem ??= model.Items[0];
        }
        finally { synchronizing = wasSynchronizing; }
    }

    private TranscriptModelSize? SelectedSize => (model.SelectedItem as ComboBoxItem)?.Tag as TranscriptModelSize?;

    private void RefreshActions()
    {
        bool busy = operation != null;
        if (!busy && SelectedSize is { } current) RefreshModelChoices(current.ToString());
        bool installed = SelectedSize is { } size && SpeechTranscriber.IsInstalled(size);
        string label = L.T(installed ? "Transcribe" : "Download model");
        primary.Content = label; AutomationProperties.SetName(primary, label);
        primary.IsEnabled = !busy && (video != null || !installed);
        cancel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        saveCaptions.IsEnabled = !busy && rows.Count > 0;
        model.IsEnabled = language.IsEnabled = !busy;
    }

    private string Unavailable(TranscriptModelSize size) => SpeechTranscriber.Pack(size) is { } pack
        ? SpeechTranscriber.StateOf(size) == ModelPackState.Damaged ? L.F($"{L.T(pack.Name)} is damaged. Download it again.") : L.F($"{L.T(pack.Name)} is not downloaded.")
        : L.T("The transcript model is not available in this version.");

    private void ShowLines(IReadOnlyList<TranscriptLine> lines)
    {
        foreach (var row in rows) row.PropertyChanged -= RowChanged;
        rows.Clear();
        foreach (var line in lines) { var row = new Row(line); row.PropertyChanged += RowChanged; rows.Add(row); }
        RefreshActions();
    }

    private void RowChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(Row.Text)) { saveTimer.Stop(); saveTimer.Start(); } }

    private void SaveEdits()
    {
        if (video == null || rows.Count == 0) return;
        var current = store.Load(video.Path) ?? document;
        document = new TranscriptDocument(Lines, current?.Language ?? "", current?.Model ?? "", current?.CaptionSha256);
        try { store.Save(video.Path, document); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { status.Text = L.F($"The transcript could not be saved: {e.Message}"); }
    }

    private void RevealCurrent()
    {
        if (rows.Count == 0 || !search.Text.Equals("", StringComparison.Ordinal)) return;
        var now = position();
        int index = rows.ToList().FindLastIndex(row => row.Line.Start <= now);
        if (index >= 0 && list.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement shown) shown.BringIntoView();
    }

    private DataTemplate RowTemplate()
    {
        var grid = new FrameworkElementFactory(typeof(DockPanel));
        grid.SetValue(MarginProperty, new Thickness(0, 0, 0, 6));
        var time = new FrameworkElementFactory(typeof(Button));
        time.SetBinding(ContentControl.ContentProperty, new Binding(nameof(Row.Time)));
        time.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(Row.SeekName)));
        time.SetBinding(ToolTipProperty, new Binding(nameof(Row.SeekName)));
        time.SetValue(VerticalAlignmentProperty, VerticalAlignment.Top);
        time.SetValue(MinWidthProperty, 64d); time.SetValue(MarginProperty, new Thickness(0, 0, 6, 0));
        time.SetValue(Control.PaddingProperty, new Thickness(8, 4, 8, 4));
        time.SetValue(TagProperty, "transcript-time");
        time.SetValue(DockPanel.DockProperty, Dock.Left);
        time.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((sender, _) =>
        {
            if ((sender as FrameworkElement)?.DataContext is Row row) seek(row.Line.Start);
        }));
        var text = new FrameworkElementFactory(typeof(TextBox));
        text.SetBinding(TextBox.TextProperty, new Binding(nameof(Row.Text)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        text.SetValue(TextBox.TextWrappingProperty, TextWrapping.Wrap);
        text.SetValue(Control.PaddingProperty, new Thickness(6, 4, 6, 4));
        text.SetValue(TagProperty, "transcript-text");
        text.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(Row.TextName)));
        grid.AppendChild(time); grid.AppendChild(text);
        return new DataTemplate(typeof(Row)) { VisualTree = grid };
    }

    private static StackPanel Field(string label, FrameworkElement input)
    {
        var field = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        var caption = Ui.Text(label, 11, muted: true); caption.Margin = new Thickness(0, 0, 0, 4);
        field.Children.Add(caption); field.Children.Add(input); return field;
    }

    internal static string LanguageName(string code)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(code switch { "jw" => "jv", "haw" => "haw", _ => code });
            return culture.ThreeLetterWindowsLanguageName == "ZZZ" && culture.DisplayName == code ? code : culture.DisplayName;
        }
        catch (CultureNotFoundException) { return code; }
    }

    private sealed class Row(TranscriptLine line) : INotifyPropertyChanged
    {
        private string text = line.Text;
        public event PropertyChangedEventHandler? PropertyChanged;
        public TranscriptLine Line => line with { Text = text };
        public string Time => RecordingTime.Format(line.Start, compact: true);
        public string SeekName => L.F($"Go to {RecordingTime.Format(line.Start, compact: true)}");
        public string TextName => L.F($"Line at {RecordingTime.Format(line.Start, compact: true)}");
        public string Text
        {
            get => text;
            set { if (text == value) return; text = value ?? ""; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text))); }
        }
    }
}
