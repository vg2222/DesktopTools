using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopTools.Core;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;

namespace DesktopTools.Extras;

/// <summary>A local, single-clip editor. Edited playback uses the same render path as export.</summary>
internal sealed class VideoEditorWindow : Window, IUnsavedWork
{
    private readonly Action<string> report;
    private readonly MediaElement media = new()
    {
        LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual,
        Stretch = Stretch.Uniform, ScrubbingEnabled = true
    };
    private readonly DispatcherTimer playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer seekTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer previewTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private bool previewPending, previewRendering;
    private int editRevision;
    private double? pendingSeek;
    private readonly Slider seek = new() { Minimum = 0, Maximum = 1, Margin = new Thickness(10, 0, 10, 0) };
    private readonly TextBlock clock = Ui.Text("0:00 / 0:00", 12, muted: true);
    private readonly TextBlock fileLabel = Ui.Text(L.T("Open a video to begin."), 12, muted: true);
    private readonly TextBlock previewLabel = Ui.Text(L.T("Original video"), 13, true);
    private readonly TextBlock status = Ui.Text(L.T("Trim, remove a section, crop, rotate, and save a copy."), 12, muted: true);
    private readonly Button empty;
    private readonly TextBox trimStart = NumberBox(), trimEnd = NumberBox();
    private readonly TextBox cutStart = NumberBox(), cutEnd = NumberBox();
    private readonly TextBox cropX = NumberBox(), cropY = NumberBox(), cropWidth = NumberBox(), cropHeight = NumberBox();
    private readonly CheckBox removeSection, mute;
    private readonly ComboBox rotation, cropRatio;
    private readonly Slider outputPercent = new() { Minimum = 10, Maximum = 100, Value = 100, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly TextBlock outputSize = Ui.Text("", 12, muted: true);
    private readonly Grid editPanel = new();
    private readonly VideoTrimTimeline timeline = new();
    private readonly TransformHandles cropHandles = new() { Visibility = Visibility.Collapsed, DimOutsideSelection = true };
    private readonly Expander cropExpander;
    private readonly StackPanel cutFields = new() { Orientation = Orientation.Horizontal };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Height = 3, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 5, 0, 5) };
    private readonly Button openButton, playButton, resetButton, previewButton, originalButton, exportButton, cancelButton;
    private readonly HashSet<string> scratchFiles = new(StringComparer.OrdinalIgnoreCase);
    private VideoInfo? source;
    private VideoEdit? savedEdit;
    public bool HasUnsavedChanges
    {
        get { if (source == null) return busy; try { return busy || ReadEdit() != savedEdit; } catch (ArgumentException) { return true; } }
    }
    public async Task<bool> SaveCopyAsync(Window owner) { await RenderAsync(false, owner); return !HasUnsavedChanges; }
    private CancellationTokenSource? operation;
    private CancellationTokenSource? thumbnailLoading;
    private Task thumbnailTask = Task.CompletedTask;
    private string? renderedPreview;
    private bool busy, closed, updating, playing, mediaOpened, seeking, showingEdited;
    private bool frameInspectorActive;
    private bool cropEditing;
    private Int32Rect appliedCrop;
    private readonly StackPanel cropActions = new();
    private readonly Button editCropButton;

    public VideoEditorWindow(Action<string> report)
    {
        this.report = report;
        Title = L.T("Video editor"); Width = 1120; Height = 740; MinWidth = 900; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; Background = Brushes.Transparent;
        UtilityWindowChrome.EnableBackdrop(this);

        var root = new Grid();
        for (int i = 0; i < 7; i++) root.RowDefinitions.Add(new RowDefinition { Height = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        AddRow(root, UtilityWindowChrome.Header(this, Title, Close, L.T("Close video editor")), 0);

        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        openButton = Ui.Button(L.T("Open video"), async () => await OpenAsync()); openButton.Content = Ui.IconLabel("Folder", L.T("Open video")); openButton.Tag = "import-video";
        DockPanel.SetDock(openButton, Dock.Left); toolbar.Children.Add(openButton);
        fileLabel.TextTrimming = TextTrimming.CharacterEllipsis; fileLabel.TextWrapping = TextWrapping.NoWrap;
        fileLabel.VerticalAlignment = VerticalAlignment.Center; toolbar.Children.Add(fileLabel); AddRow(root, toolbar, 1);

        var stage = new Grid { MinHeight = 140 };
        empty = Ui.ImportPrompt("Video", L.T("Open video"), L.T("Choose a video to trim, crop and save a copy."), async () => await OpenAsync()); empty.Tag = "import-video-empty";
        stage.Children.Add(media); empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center; stage.Children.Add(empty);
        stage.Children.Add(cropHandles); stage.SizeChanged += (_, _) => UpdateCropHandles();
        cropHandles.Changed += (rect, _) =>
        {
            if (source == null || cropHandles.Bounds.Width <= 0 || cropHandles.Bounds.Height <= 0) return;
            var bounds = cropHandles.Bounds;
            int x = Math.Clamp((int)Math.Round((rect.X - bounds.X) / bounds.Width * source.Width), 0, source.Width - 1);
            int y = Math.Clamp((int)Math.Round((rect.Y - bounds.Y) / bounds.Height * source.Height), 0, source.Height - 1);
            int right = Math.Clamp((int)Math.Round((rect.Right - bounds.X) / bounds.Width * source.Width), x + 1, source.Width);
            int bottom = Math.Clamp((int)Math.Round((rect.Bottom - bounds.Y) / bounds.Height * source.Height), y + 1, source.Height);
            updating = true; cropX.Text = x.ToString(); cropY.Text = y.ToString(); cropWidth.Text = (right - x).ToString(); cropHeight.Text = (bottom - y).ToString(); updating = false; CropChanged();
        };
        var stageFrame = new Border { Child = stage, Background = Brushes.Black, CornerRadius = new CornerRadius(12), Padding = new Thickness(4) };
        AddRow(root, stageFrame, 2);
        AutomationProperties.SetName(media, L.T("Video playback"));

        var playback = new Grid { Margin = new Thickness(0, 9, 0, 12) };
        playback.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        playback.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        playback.ColumnDefinitions.Add(new ColumnDefinition());
        playback.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        playButton = Ui.IconButton("Play", L.T("Play"), TogglePlayback); playButton.Margin = new Thickness(0, 0, 10, 0); playback.Children.Add(playButton);
        previewLabel.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(previewLabel, 1); playback.Children.Add(previewLabel);
        Grid.SetColumn(seek, 2); playback.Children.Add(seek); clock.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(clock, 3); playback.Children.Add(clock);
        AutomationProperties.SetName(seek, L.T("Playback position")); AddRow(root, playback, 3);

        editPanel.ColumnDefinitions.Add(new ColumnDefinition()); editPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); editPanel.ColumnDefinitions.Add(new ColumnDefinition());
        var timing = new StackPanel(); timing.Children.Add(Ui.Text(L.T("Keep range"), 15, true));
        var trim = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
        trim.Children.Add(Field(L.T("Start (seconds)"), trimStart)); trim.Children.Add(Field(L.T("End (seconds)"), trimEnd)); timing.Children.Add(trim);
        removeSection = Ui.Toggle(false, _ => Changed());
        timing.Children.Add(ToggleLabel(L.T("Remove a middle section"), removeSection));
        cutFields.Margin = new Thickness(0, 6, 0, 0);
        cutFields.Children.Add(Field(L.T("Remove from (seconds)"), cutStart)); cutFields.Children.Add(Field(L.T("Remove to (seconds)"), cutEnd)); timing.Children.Add(cutFields);
        editPanel.Children.Add(timing);

        var frame = new StackPanel(); frame.Children.Add(Ui.Text(L.T("Crop in original pixels"), 15, true));
        var crop = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) };
        crop.Children.Add(Field(L.T("Crop X"), cropX, 68)); crop.Children.Add(Field(L.T("Crop Y"), cropY, 68));
        crop.Children.Add(Field(L.T("Width"), cropWidth, 80)); crop.Children.Add(Field(L.T("Height"), cropHeight, 80)); frame.Children.Add(crop);
        var transform = new WrapPanel();
        rotation = Ui.Choice(new[] { "0°", "90°", "180°", "270°" }, "0°", _ => Changed(), translate: false);
        rotation.MinWidth = 82; rotation.Width = 92;
        transform.Children.Add(Field(L.T("Clockwise rotation"), rotation, 144));
        mute = Ui.Toggle(false, _ => Changed());
        var muteRow = ToggleLabel(L.T("Mute audio"), mute); muteRow.VerticalAlignment = VerticalAlignment.Bottom; muteRow.Margin = new Thickness(0, 22, 0, 0); transform.Children.Add(muteRow); frame.Children.Add(transform);
        Grid.SetColumn(frame, 2); editPanel.Children.Add(frame);
        var editorScroll = new ScrollViewer { Content = editPanel, MaxHeight = 198, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AddRow(root, editorScroll, 4);

        var feedback = new StackPanel { Margin = new Thickness(0, 8, 0, 6) }; feedback.Children.Add(progress); feedback.Children.Add(status); AddRow(root, feedback, 5);
        var actions = new WrapPanel();
        resetButton = Ui.IconButton("RotateLeft", L.T("Reset"), Reset);
        originalButton = Ui.IconButton("Video", L.T("Show original"), ShowOriginal);
        previewButton = Ui.Button(L.T("Update preview"), async () => await RenderAsync(previewOnly: true)); previewButton.Content = Ui.IconLabel("Refresh", L.T("Update preview"));
        exportButton = Ui.Button(L.T("Export MP4"), async () => await RenderAsync(previewOnly: false), true);
        cancelButton = Ui.Button(L.T("Cancel"), () => { previewPending = false; previewTimer.Stop(); operation?.Cancel(); });
        foreach (var button in new[] { resetButton, originalButton, previewButton, exportButton, cancelButton }) actions.Children.Add(button);
        AddRow(root, actions, 6);
        // Preserve the existing editing model while placing controls in the selected C composition.
        var caption = (UIElement)root.Children[0]; root.Children.Clear(); root.RowDefinitions.Clear();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddRow(root, caption, 0);
        var workspace = new Grid(); workspace.ColumnDefinitions.Add(new ColumnDefinition()); workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) }); AddRow(root, workspace, 1);
        var viewer = new DockPanel { Margin = new Thickness(0, 0, 16, 0) }; DockPanel.SetDock(toolbar, Dock.Top); viewer.Children.Add(toolbar); DockPanel.SetDock(playback, Dock.Bottom); viewer.Children.Add(playback); viewer.Children.Add(stageFrame); workspace.Children.Add(viewer);
        editPanel.Children.Clear(); editPanel.ColumnDefinitions.Clear(); editPanel.RowDefinitions.Clear();
        foreach (var box in trim.Children.OfType<StackPanel>()) box.Width = 106;
        foreach (var box in cutFields.Children.OfType<StackPanel>()) box.Width = 106;
        timing.Children.Remove(cutFields);
        cutFields.Visibility = Visibility.Collapsed; timing.Children.Add(cutFields);
        removeSection.Checked += (_, _) => cutFields.Visibility = Visibility.Visible;
        removeSection.Unchecked += (_, _) => cutFields.Visibility = Visibility.Collapsed;
        frame.Children.Clear(); frame.Margin = new Thickness(0);
        if (rotation.Parent is Panel rotationParent) rotationParent.Children.Remove(rotation);
        transform.Children.Clear(); muteRow.Margin = new Thickness(0, 0, 0, 14); frame.Children.Add(muteRow);
        var rotateAction = Ui.IconButton("RotateRight", L.T("Clockwise rotation"), () => rotation.SelectedIndex = (rotation.SelectedIndex + 1) % 4);
        cropRatio = Ui.Choice(new[] { "Free", "Original", "1:1", "4:3", "16:9", "9:16" }, "Free", ApplyCropRatio);
        AutomationProperties.SetName(cropRatio, L.T("Aspect ratio"));
        var cropOptions = new StackPanel();
        var cropHelp = Ui.Text(L.T("Drag the corners to keep an area. Drag inside to move it. Apply crop when ready."), 12, muted: true); cropHelp.Margin = new Thickness(0, 0, 0, 10); cropOptions.Children.Add(cropHelp);
        editCropButton = Ui.Button(L.T("Edit crop"), BeginCrop); editCropButton.Content = Ui.IconLabel("Crop", L.T("Edit crop")); editCropButton.Margin = new Thickness(0, 0, 0, 10); cropOptions.Children.Add(editCropButton);
        cropOptions.Children.Add(Field(L.T("Aspect ratio"), cropRatio, 240));
        foreach (var field in crop.Children.OfType<StackPanel>()) { field.Width = 110; field.Margin = new Thickness(0, 0, 10, 8); }
        cropOptions.Children.Add(crop);
        cropExpander = new Expander { Header = Ui.IconLabel("Crop", L.T("Crop")), Content = cropOptions, IsExpanded = true, Margin = new Thickness(0, 0, 0, 8) }; frame.Children.Add(cropExpander);
        var rotationRow = new StackPanel { Orientation = Orientation.Horizontal };
        rotationRow.Children.Add(Field(L.T("Clockwise rotation"), rotation, 180));
        rotateAction.VerticalAlignment = VerticalAlignment.Bottom; rotationRow.Children.Add(rotateAction);
        frame.Children.Add(rotationRow);
        var applyCrop = Ui.Button(L.T("Apply crop"), ApplyCrop, true); applyCrop.Content = Ui.IconLabel("Check", L.T("Apply crop"), primary: true); applyCrop.Margin = new Thickness(0, 0, 0, 8); cropActions.Children.Add(applyCrop);
        var cropSecondary = new WrapPanel(); cropSecondary.Children.Add(Ui.Button(L.T("Reset crop"), ResetCrop)); cropSecondary.Children.Add(Ui.Button(L.T("Cancel"), CancelCrop)); cropActions.Children.Add(cropSecondary);
        var outputSettings = new StackPanel(); outputSettings.Children.Add(Ui.Text(L.T("Output size"), 13, true));
        var presets = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        foreach (int percent in new[] { 25, 50, 75, 100 }) { int value = percent; var preset = Ui.Button($"{percent}%", () => outputPercent.Value = value); preset.Padding = new Thickness(8, 5, 8, 5); preset.MinWidth = 0; presets.Children.Add(preset); }
        outputSettings.Children.Add(presets); outputSettings.Children.Add(outputPercent); outputSize.Margin = new Thickness(0, 8, 0, 0); outputSettings.Children.Add(outputSize);
        AutomationProperties.SetName(outputPercent, L.T("Output size")); outputPercent.ValueChanged += (_, _) => Changed();
        cropExpander.Expanded += (_, _) => { if (!frameInspectorActive) return; BeginCrop(); };
        cropExpander.Collapsed += (_, _) => cropHandles.Visibility = Visibility.Collapsed;
        var timingSection = MediaWorkspaceLayout.Section("Timer", "Timing", "Drag the timeline or enter exact seconds.", timing);
        var frameSection = MediaWorkspaceLayout.Section("Crop", "Frame and sound", "Crop, rotate, or remove audio from the exported copy.", frame);
        var outputSection = MediaWorkspaceLayout.Section("File", "Output", "Scale the final frame while keeping the source unchanged.", outputSettings);
        editPanel.Children.Add(timingSection); editPanel.Children.Add(frameSection); editPanel.Children.Add(outputSection);
        var tabButtons = new Dictionary<string, Button>();
        var inspectorTabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        void InspectorMode(string mode)
        {
            frameInspectorActive = mode == "Frame";
            timingSection.Visibility = mode == "Timing" ? Visibility.Visible : Visibility.Collapsed;
            frameSection.Visibility = mode == "Frame" ? Visibility.Visible : Visibility.Collapsed;
            outputSection.Visibility = mode == "Output" ? Visibility.Visible : Visibility.Collapsed;
            MediaWorkspaceLayout.SelectTab(tabButtons, mode);
            if (mode != "Frame") cropHandles.Visibility = Visibility.Collapsed;
            else if (cropExpander.IsExpanded) BeginCrop();
        }
        foreach (var (key, icon, label) in new[] { ("Timing", "Timer", "Timing"), ("Frame", "Crop", "Frame"), ("Output", "File", "Output") })
        {
            string selected = key; var button = MediaWorkspaceLayout.Tab(icon, label, () => InspectorMode(selected));
            tabButtons[key] = button; inspectorTabs.Children.Add(button);
        }
        editorScroll.Content = editPanel; editorScroll.MaxHeight = double.PositiveInfinity;
        var inspector = new DockPanel(); Grid.SetColumn(inspector, 1); workspace.Children.Add(inspector);
        actions.Children.Clear();
        var exportActions = new StackPanel(); exportActions.Children.Add(cropActions); exportButton.Content = Ui.IconLabel("Save", L.T("Export MP4"), primary: true); exportButton.Height = 40; exportButton.Margin = new Thickness(0, 12, 0, 0); exportActions.Children.Add(exportButton);
        previewButton.Content = Ui.IconLabel("Refresh", L.T("Refresh preview")); previewButton.Margin = new Thickness(0, 8, 0, 0); exportActions.Children.Add(previewButton);
        var utilityActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; foreach (var button in new[] { resetButton, originalButton, cancelButton }) { button.Margin = new Thickness(0, 0, 6, 0); utilityActions.Children.Add(button); } exportActions.Children.Add(utilityActions);
        DockPanel.SetDock(exportActions, Dock.Bottom); inspector.Children.Add(exportActions); DockPanel.SetDock(inspectorTabs, Dock.Top); inspector.Children.Add(inspectorTabs); inspector.Children.Add(editorScroll);
        var feedbackSurface = MediaWorkspaceLayout.Status(feedback); feedbackSurface.Margin = new Thickness(0, 10, 0, 0); AddRow(root, feedbackSurface, 2);
        InspectorMode("Timing");
        var card = Ui.Card(root, 20); card.Margin = new Thickness(0); card.CornerRadius = new CornerRadius(20); card.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); card.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); Content = card;

        feedback.Children.Insert(0, timeline); AutomationProperties.SetName(timeline, L.T("Keep range"));
        var guide = FeatureTourButton.Create(this, "video-editor", () => new GuidedTour.Step[]
        {
            new(() => openButton, "Open video", "Open a video to edit a copy. The source file stays unchanged."),
            new(() => feedback, "Keep range", "Drag the timeline ends to keep a range. Click inside it to move the playhead."),
            new(() => inspector, "Crop and sound", "Expand Crop to adjust the frame. Mute removes the audio from the exported copy."),
            new(() => exportActions, "Export MP4", "Preview your changes, then export to a new MP4 file.")
        }); DockPanel.SetDock(guide, Dock.Right); toolbar.Children.Insert(0, guide);
        timeline.RangeChanged += (start, end) => { updating = true; trimStart.Text = start.ToString("0.###", CultureInfo.CurrentCulture); trimEnd.Text = end.ToString("0.###", CultureInfo.CurrentCulture); updating = false; Changed(); };
        timeline.CutChanged += (start, end) => { updating = true; cutStart.Text = start.ToString("0.###", CultureInfo.CurrentCulture); cutEnd.Text = end.ToString("0.###", CultureInfo.CurrentCulture); updating = false; Changed(); };
        timeline.SeekRequested += position => { if (source == null || busy) return; if (showingEdited) ShowOriginal(); QueueSeek(position); };
        timeline.SeekCompleted += ApplyPendingSeek;
        foreach (var input in new[] { trimStart, trimEnd, cutStart, cutEnd }) input.TextChanged += (_, _) => Changed();
        foreach (var input in new[] { cropX, cropY, cropWidth, cropHeight }) input.TextChanged += (_, _) => CropChanged();
        foreach (var input in new[] { cropWidth, cropHeight }) input.TextChanged += (_, _) => { if (!updating) cropRatio.SelectedItem = "Free"; };
        seek.ValueChanged += (_, _) =>
        {
            if (updating) return;
            QueueSeek(seek.Value);
        };
        seek.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => seeking = true));
        seek.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => { seeking = false; ApplyPendingSeek(); }));
        playbackTimer.Tick += OnPlaybackTick;
        seekTimer.Tick += OnSeekTick;
        previewTimer.Tick += async (_, _) =>
        {
            previewTimer.Stop();
            if (!previewPending || cropEditing || busy || closed || !IsVisible || WindowState == WindowState.Minimized || source == null) return;
            try { ReadEdit(); } catch (ArgumentException) { return; }
            await RenderAsync(previewOnly: true);
        };
        media.MediaOpened += (_, _) =>
        {
            if (closed || source == null) return;
            mediaOpened = true;
            if (media.NaturalDuration.HasTimeSpan) seek.Maximum = Math.Max(.001, media.NaturalDuration.TimeSpan.TotalSeconds);
            ApplyPendingSeek(); RefreshEnabled(); UpdateClock();
        };
        media.MediaEnded += (_, _) => { CancelPendingSeek(); PausePlayback(); media.Position = TimeSpan.Zero; UpdatePosition(); };
        media.MediaFailed += (_, _) =>
        {
            if (closed) return;
            CancelPendingSeek(); PausePlayback(); mediaOpened = false; RefreshEnabled();
            status.Text = L.T("Windows could not play this video. You can still try exporting it.");
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) { CancelPendingSeek(); PausePlayback(); PausePreview(); } else SchedulePreview(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) { CancelPendingSeek(); PausePlayback(); PausePreview(); } else SchedulePreview(); };
        Closed += (_, _) =>
        {
            closed = true; previewTimer.Stop(); operation?.Cancel(); thumbnailLoading?.Cancel(); ReleaseMedia(); playbackTimer.Tick -= OnPlaybackTick; seekTimer.Tick -= OnSeekTick;
            timeline.Thumbnails = Array.Empty<System.Windows.Media.Imaging.BitmapSource>();
            foreach (string path in scratchFiles) _ = DeleteScratchAsync(path);
        };
        RefreshEnabled();
    }

    private static TextBox NumberBox()
    {
        var box = new TextBox { MinWidth = 58, MinHeight = 38, Text = "0", FontSize = 13, Padding = new Thickness(8, 5, 8, 5) };
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        // Native TextBoxView owns text padding; the outer frame must not apply it twice.
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost"); host.SetValue(FocusableProperty, false);
        border.AppendChild(host); box.Template = new ControlTemplate(typeof(TextBox)) { VisualTree = border }; return box;
    }
    private static StackPanel Field(string label, FrameworkElement input, double width = 155)
    {
        var field = new StackPanel { Width = width, Margin = new Thickness(0, 0, 10, 0) };
        var caption = Ui.Text(label, 11, muted: true); caption.Margin = new Thickness(0, 0, 0, 4);
        field.Children.Add(caption); field.Children.Add(input); AutomationProperties.SetName(input, label); return field;
    }
    private static StackPanel ToggleLabel(string label, CheckBox toggle)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        toggle.Margin = new Thickness(0, 0, 8, 0); row.Children.Add(toggle);
        var text = Ui.Text(label, 12); text.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(text);
        AutomationProperties.SetName(toggle, label); return row;
    }
    private static void AddRow(Grid grid, UIElement element, int row) { Grid.SetRow(element, row); grid.Children.Add(element); }

    private async Task OpenAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = L.T("Open video"), Filter = L.T("Video files|*.mp4;*.mov;*.m4v;*.avi;*.wmv;*.mkv|All files|*.*"), CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) await LoadAsync(dialog.FileName);
    }

    internal async Task LoadAsync(string path)
    {
        if (busy || closed) return;
        var cancellation = BeginOperation(L.T("Opening video…"), indeterminate: true);
        try
        {
            VideoInfo loaded = await VideoEditorService.ProbeAsync(path, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested(); if (closed) return;
            new VideoEdit(0, loaded.Duration).Validate(loaded);
            source = loaded; fileLabel.Text = Path.GetFileName(loaded.Path);
            fileLabel.ToolTip = L.F($"{loaded.Width} × {loaded.Height} px · {loaded.Duration:0.###} seconds");
            empty.Visibility = Visibility.Collapsed; Reset(); savedEdit = ReadEdit();
            if ((Application.Current as App)?.Controller?.Settings.VideoMuteOnOpen == true) mute.IsChecked = true;
            status.Text = L.T("Original loaded. The preview updates automatically after you edit.");
            timeline.Thumbnails = Array.Empty<System.Windows.Media.Imaging.BitmapSource>();
            thumbnailTask = LoadThumbnailsAsync(loaded);
        }
        catch (OperationCanceledException) { if (!closed) status.Text = L.T("Operation canceled."); }
        catch (Exception ex) { ShowError(ex); }
        finally { EndOperation(cancellation); }
    }

    private async Task LoadThumbnailsAsync(VideoInfo loaded)
    {
        thumbnailLoading?.Cancel();
        using var cancellation = new CancellationTokenSource();
        thumbnailLoading = cancellation;
        try
        {
            var thumbnails = await VideoEditorService.ThumbnailsAsync(loaded, cancellation.Token);
            if (!closed && !cancellation.IsCancellationRequested && ReferenceEquals(source, loaded))
            { timeline.Thumbnails = thumbnails; timeline.InvalidateVisual(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); } // Thumbnail failures must not block editing/export.
        finally { if (ReferenceEquals(thumbnailLoading, cancellation)) thumbnailLoading = null; }
    }

    private void Reset()
    {
        if (source == null) return;
        cropEditing = false; appliedCrop = new Int32Rect(0, 0, source.Width, source.Height);
        editRevision++; previewPending = false; previewTimer.Stop(); if (previewRendering) operation?.Cancel();
        updating = true;
        trimStart.Text = "0"; trimEnd.Text = source.Duration.ToString("R", CultureInfo.CurrentCulture);
        cutStart.Text = (source.Duration / 3).ToString("0.###", CultureInfo.CurrentCulture);
        cutEnd.Text = (source.Duration * 2 / 3).ToString("0.###", CultureInfo.CurrentCulture);
        cropX.Text = "0"; cropY.Text = "0";
        cropWidth.Text = source.Width.ToString(CultureInfo.CurrentCulture); cropHeight.Text = source.Height.ToString(CultureInfo.CurrentCulture);
        removeSection.IsChecked = false; mute.IsChecked = false; rotation.SelectedIndex = 0; outputPercent.Value = 100;
        cropRatio.SelectedItem = "Free"; cropHandles.KeepRatio = false;
        updating = false; InvalidatePreview(); RefreshEnabled();
        status.Text = L.T("Original video. Your source file stays unchanged.");
    }

    private void ApplyCropRatio(string value)
    {
        if (updating || source == null) return;
        cropHandles.KeepRatio = value != "Free";
        if (value == "Free") return;
        double ratio = value switch { "1:1" => 1, "4:3" => 4d / 3, "16:9" => 16d / 9, "9:16" => 9d / 16, _ => source.Width / (double)source.Height };
        int width = source.Width, height = Math.Max(2, (int)Math.Round(width / ratio));
        if (height > source.Height) { height = source.Height; width = Math.Max(2, (int)Math.Round(height * ratio)); }
        updating = true;
        cropX.Text = ((source.Width - width) / 2).ToString(CultureInfo.CurrentCulture);
        cropY.Text = ((source.Height - height) / 2).ToString(CultureInfo.CurrentCulture);
        cropWidth.Text = width.ToString(CultureInfo.CurrentCulture); cropHeight.Text = height.ToString(CultureInfo.CurrentCulture);
        updating = false; CropChanged();
    }

    private void BeginCrop()
    {
        if (source == null || closed || (busy && !previewRendering)) return;
        cropEditing = true; previewTimer.Stop();
        editRevision++; if (previewRendering) operation?.Cancel();
        InvalidatePreview(); RefreshEnabled();
        previewLabel.Text = L.T("Editing crop"); status.Text = L.T("Adjust the frame on the original video, then choose Apply crop.");
    }
    private void CropChanged()
    {
        if (updating || source == null || closed) return;
        if (!cropEditing) BeginCrop();
        editRevision++; previewPending = true; previewTimer.Stop();
        if (previewRendering) operation?.Cancel();
        InvalidatePreview(); RefreshEnabled();
        previewLabel.Text = L.T("Editing crop"); status.Text = L.T("Adjust the frame on the original video, then choose Apply crop.");
    }
    private bool CommitCrop()
    {
        try
        {
            ReadEdit();
            appliedCrop = new Int32Rect(ReadPixels(cropX), ReadPixels(cropY), ReadPixels(cropWidth), ReadPixels(cropHeight));
            cropEditing = false; return true;
        }
        catch (ArgumentException ex) { ShowError(ex); return false; }
    }
    private void ApplyCrop() { if (source != null && CommitCrop()) Changed(); }
    private void SetCropFields(Int32Rect rect)
    {
        updating = true;
        try
        {
            cropX.Text = rect.X.ToString(CultureInfo.CurrentCulture); cropY.Text = rect.Y.ToString(CultureInfo.CurrentCulture);
            cropWidth.Text = rect.Width.ToString(CultureInfo.CurrentCulture); cropHeight.Text = rect.Height.ToString(CultureInfo.CurrentCulture);
            cropRatio.SelectedItem = "Free"; cropHandles.KeepRatio = false;
        }
        finally { updating = false; }
    }
    private void ResetCrop() { if (source == null) return; SetCropFields(new Int32Rect(0, 0, source.Width, source.Height)); CropChanged(); }
    private void CancelCrop() { if (source == null) return; SetCropFields(appliedCrop); cropEditing = false; Changed(); }

    private void Changed()
    {
        if (updating || source == null || closed) return;
        editRevision++; previewPending = true; if (previewRendering) operation?.Cancel();
        InvalidatePreview(); RefreshEnabled();
        previewLabel.Text = L.T(cropEditing ? "Editing crop" : "Preview pending");
        status.Text = L.T(cropEditing ? "Adjust the frame on the original video, then choose Apply crop." : "Showing the original while your changes are prepared. The preview updates automatically.");
        SchedulePreview();
    }

    private void SchedulePreview()
    {
        previewTimer.Stop();
        if (previewPending && !cropEditing && !busy && !closed && IsVisible && WindowState != WindowState.Minimized) previewTimer.Start();
    }
    private void PausePreview()
    {
        previewTimer.Stop();
        if (previewRendering) { previewPending = true; operation?.Cancel(); }
    }

    private void InvalidatePreview()
    {
        CancelPendingSeek();
        string? previous = renderedPreview; renderedPreview = null;
        if (source != null && (showingEdited || media.Source == null || media.Source.LocalPath != source.Path)) SetPlaybackSource(source.Path, source.Duration, edited: false);
        else PausePlayback();
        if (previous != null) _ = DeleteScratchAsync(previous);
    }

    private void ShowOriginal()
    {
        if (source == null || busy) return;
        previewPending = false; previewTimer.Stop();
        SetPlaybackSource(source.Path, source.Duration, edited: false);
        status.Text = L.T("Original video. Your source file stays unchanged."); RefreshEnabled();
    }

    private VideoEdit ReadEdit()
    {
        bool remove = removeSection.IsChecked == true;
        var edit = new VideoEdit(ReadSeconds(trimStart), ReadSeconds(trimEnd), remove,
            remove ? ReadSeconds(cutStart) : 0, remove ? ReadSeconds(cutEnd) : 0,
            ReadPixels(cropX), ReadPixels(cropY), ReadPixels(cropWidth), ReadPixels(cropHeight), Math.Max(0, rotation.SelectedIndex) * 90, mute.IsChecked == true, (int)outputPercent.Value);
        edit.Validate(source!); return edit;
    }
    private static double ReadSeconds(TextBox input)
    {
        if ((double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) ||
             double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) && double.IsFinite(value)) return value;
        throw new ArgumentException(L.T("Enter a valid number of seconds."));
    }
    private static int ReadPixels(TextBox input)
    {
        if (int.TryParse(input.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int value) ||
            int.TryParse(input.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return value;
        throw new ArgumentException(L.T("Enter whole pixel crop coordinates and dimensions."));
    }

    private async Task RenderAsync(bool previewOnly, Window? dialogOwner = null)
    {
        if (source == null || busy || closed) return;
        if (cropEditing && !CommitCrop()) return;
        var originalSource = source;
        VideoEdit edit;
        try { edit = ReadEdit(); } catch (Exception ex) { ShowError(ex); return; }
        string output;
        if (previewOnly)
        {
            output = Path.Combine(Path.GetTempPath(), "DesktopTools-video-preview-" + Guid.NewGuid().ToString("N") + ".mp4");
            scratchFiles.Add(output);
        }
        else
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = L.T("Export MP4"), Filter = L.T("MP4 video|*.mp4"), DefaultExt = ".mp4", AddExtension = true,
                FileName = Path.GetFileNameWithoutExtension(source.Path) + "-edited.mp4", OverwritePrompt = true
            };
            if (dialog.ShowDialog(dialogOwner ?? this) != true) return;
            output = dialog.FileName;
        }
        if (closed || busy || !ReferenceEquals(source, originalSource)) return;
        int revision = editRevision;
        previewTimer.Stop(); previewPending = false; previewRendering = previewOnly;
        var cancellation = BeginOperation(previewOnly ? L.T("Preparing edited preview…") : L.T("Exporting video…"));
        if (previewOnly) previewLabel.Text = L.T("Updating preview…");
        bool keepPreview = false;
        try
        {
            var indicator = new Progress<double>(value =>
            {
                if (!closed && ReferenceEquals(operation, cancellation) && double.IsFinite(value)) progress.Value = Math.Clamp(value, 0, 100);
            });
            await VideoEditorService.ExportAsync(source, edit, output, indicator, cancellation.Token);
            if (previewOnly) cancellation.Token.ThrowIfCancellationRequested();
            if (closed || (previewOnly && (revision != editRevision || !ReferenceEquals(source, originalSource)))) return;
            if (previewOnly)
            {
                string? previous = renderedPreview; renderedPreview = output;
                SetPlaybackSource(output, edit.OutputDuration(source), edited: true); keepPreview = true;
                if (previous != null) _ = DeleteScratchAsync(previous);
                status.Text = L.T("Edited preview ready. Press Play to review before exporting.");
            }
            else
            {
                savedEdit = edit; status.Text = L.F($"Saved video copy: {output}"); report(L.T("Video copy saved."));
            }
        }
        catch (OperationCanceledException) { if (!closed) status.Text = L.T("Operation canceled."); }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            if (previewOnly && !keepPreview) _ = DeleteScratchAsync(output);
            EndOperation(cancellation);
        }
    }

    private CancellationTokenSource BeginOperation(string message, bool indeterminate = false)
    {
        CancelPendingSeek(); PausePlayback(); busy = true; operation = new CancellationTokenSource();
        status.Text = message; progress.Value = 0; progress.IsIndeterminate = indeterminate; progress.Visibility = Visibility.Visible;
        RefreshEnabled(); return operation;
    }
    private void EndOperation(CancellationTokenSource cancellation)
    {
        if (ReferenceEquals(operation, cancellation))
        {
            operation = null; busy = false; previewRendering = false;
            if (!showingEdited) previewLabel.Text = L.T(cropEditing ? "Editing crop" : previewPending ? "Preview pending" : "Original video");
            if (!closed) { progress.IsIndeterminate = false; progress.Visibility = Visibility.Collapsed; RefreshEnabled(); }
            SchedulePreview();
        }
        cancellation.Dispose();
    }
    private void ShowError(Exception ex)
    {
        if (closed) return;
        status.Text = L.F($"Video operation failed: {ex.Message}"); report(status.Text);
    }
    private void RefreshEnabled()
    {
        bool ready = source != null && !busy && !closed;
        if (source != null) { try { var size = ReadEdit().OutputSize(source); outputSize.Text = $"{outputPercent.Value:0}% · {size.Width} × {size.Height} px"; } catch (ArgumentException) { outputSize.Text = "—"; } }
        empty.IsEnabled = openButton.IsEnabled = !busy && !closed; editPanel.IsEnabled = source != null && (!busy || previewRendering) && !closed;
        resetButton.IsEnabled = ready; previewButton.IsEnabled = ready; exportButton.IsEnabled = ready;
        originalButton.IsEnabled = ready && showingEdited; playButton.IsEnabled = ready && mediaOpened;
        seek.IsEnabled = ready && mediaOpened; cutFields.IsEnabled = removeSection.IsChecked == true;
        timeline.IsEnabled = editPanel.IsEnabled;
        cropActions.Visibility = cropEditing ? Visibility.Visible : Visibility.Collapsed;
        cropActions.IsEnabled = source != null && (!busy || previewRendering) && !closed;
        editCropButton.IsEnabled = ready && !cropEditing;
        editCropButton.Visibility = cropEditing ? Visibility.Collapsed : Visibility.Visible;
        foreach (var action in new[] { exportButton, previewButton, resetButton, originalButton })
            action.Visibility = cropEditing ? Visibility.Collapsed : Visibility.Visible;
        cropHandles.Visibility = cropEditing && frameInspectorActive && cropExpander.IsExpanded && !showingEdited ? Visibility.Visible : Visibility.Collapsed;
        cropHandles.IsEnabled = editPanel.IsEnabled && !showingEdited; UpdateCropHandles();
        if (source != null && double.TryParse(trimStart.Text, out double start) && double.TryParse(trimEnd.Text, out double end)) timeline.SetRange(source.Duration, start, end);
        timeline.CutStart = removeSection.IsChecked == true && double.TryParse(cutStart.Text, out double removedStart) ? removedStart : null;
        timeline.CutEnd = double.TryParse(cutEnd.Text, out double removedEnd) ? removedEnd : 0;
        cancelButton.IsEnabled = busy && !closed;
        cancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCropHandles()
    {
        if (source == null || cropHandles.IsDragging || cropHandles.ActualWidth <= 0 || cropHandles.ActualHeight <= 0) return;
        double scale = Math.Min(cropHandles.ActualWidth / source.Width, cropHandles.ActualHeight / source.Height);
        var bounds = new Rect((cropHandles.ActualWidth - source.Width * scale) / 2, (cropHandles.ActualHeight - source.Height * scale) / 2, source.Width * scale, source.Height * scale);
        cropHandles.Bounds = bounds;
        if (int.TryParse(cropX.Text, out int x) && int.TryParse(cropY.Text, out int y) && int.TryParse(cropWidth.Text, out int width) && int.TryParse(cropHeight.Text, out int height) && width > 0 && height > 0)
            cropHandles.Selection = new Rect(bounds.X + Math.Clamp(x, 0, source.Width - 1) * scale, bounds.Y + Math.Clamp(y, 0, source.Height - 1) * scale, Math.Min(width, source.Width - Math.Clamp(x, 0, source.Width - 1)) * scale, Math.Min(height, source.Height - Math.Clamp(y, 0, source.Height - 1)) * scale);
        cropHandles.InvalidateVisual();
    }

    private void SetPlaybackSource(string path, double duration, bool edited)
    {
        ReleaseMedia(); showingEdited = edited;
        previewLabel.Text = edited ? L.T("Edited preview") : L.T("Original video");
        updating = true; seek.Maximum = Math.Max(.001, duration); seek.Value = 0; updating = false;
        media.Source = new Uri(Path.GetFullPath(path)); media.Pause(); UpdateClock();
    }
    private void TogglePlayback()
    {
        if (source == null || busy || !mediaOpened || !IsVisible) return;
        if (playing) PausePlayback();
        else { media.Play(); playing = true; playButton.Content = Ui.Icon("Pause"); Ui.Tip(playButton, L.T("Pause")); AutomationProperties.SetName(playButton, L.T("Pause")); playbackTimer.Start(); }
    }
    private void PausePlayback()
    {
        playbackTimer.Stop(); if (!playing) return;
        media.Pause(); playing = false;
        playButton.Content = Ui.Icon("Play"); Ui.Tip(playButton, L.T("Play")); AutomationProperties.SetName(playButton, L.T("Play"));
    }
    private void ReleaseMedia()
    {
        CancelPendingSeek(); PausePlayback(); mediaOpened = false; media.Close(); media.Source = null;
    }
    private void QueueSeek(double position)
    {
        if (source == null || busy || closed || !IsVisible || WindowState == WindowState.Minimized || !double.IsFinite(position)) return;
        pendingSeek = Math.Clamp(position, 0, seek.Maximum);
        updating = true; seek.Value = pendingSeek.Value; updating = false; UpdateClock();
        if (mediaOpened) seekTimer.Start();
    }
    private void OnSeekTick(object? sender, EventArgs args) => ApplyPendingSeek();
    private void ApplyPendingSeek()
    {
        seekTimer.Stop();
        if (closed || busy || !IsVisible || WindowState == WindowState.Minimized) { pendingSeek = null; return; }
        if (!mediaOpened || pendingSeek is not double position) return;
        pendingSeek = null;
        media.Position = TimeSpan.FromSeconds(Math.Clamp(position, 0, seek.Maximum));
    }
    private void CancelPendingSeek() { seekTimer.Stop(); pendingSeek = null; seeking = false; }
    private void OnPlaybackTick(object? sender, EventArgs e)
    {
        if (!IsVisible || !playing) { PausePlayback(); return; }
        if (!seeking && !timeline.IsScrubbing && pendingSeek == null) UpdatePosition();
    }
    private void UpdatePosition()
    {
        updating = true; seek.Value = Math.Clamp(media.Position.TotalSeconds, 0, seek.Maximum); updating = false; UpdateClock();
    }
    private void UpdateClock()
    {
        clock.Text = FormatTime(seek.Value) + " / " + FormatTime(seek.Maximum);
        double position = seek.Value;
        if (showingEdited && source != null)
        {
            try { var edit = ReadEdit(); position += edit.Start; if (edit.RemoveSection && position >= edit.CutStart) position += edit.CutEnd - edit.CutStart; }
            catch (ArgumentException) { }
        }
        timeline.Position = position; timeline.InvalidateVisual();
    }
    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss", CultureInfo.CurrentCulture) : time.ToString(@"m\:ss", CultureInfo.CurrentCulture);
    }
    private static async Task DeleteScratchAsync(string path)
    {
        // Only caller-generated preview paths enter this method. WPF can release its file handle asynchronously.
        for (int attempt = 0; attempt < 30; attempt++)
        {
            try { File.Delete(path); return; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            await Task.Delay(200).ConfigureAwait(false);
        }
    }
}
