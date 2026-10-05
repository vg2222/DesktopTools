using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;
using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;

namespace DesktopTools.Extras;

internal sealed class ScreenRecorderWindow : Window
{
    private readonly Button sourceChoice;
    private RecordingSelection? selectedSource;
    private RecordingSourcePickerWindow? sourcePicker;
    private readonly WindowThumbnail sourceThumbnail = new();
    private readonly ComboBox qualityChoice, fpsChoice;
    private readonly CheckBox hardwareAcceleration;
    private readonly Image preview = new() { Stretch = Stretch.Uniform };
    private readonly Button refreshPreview;
    private readonly Button sourcePreview;
    private readonly FrameworkElement previewHint;
    private readonly TextBlock previewTitle, previewDescription;
    private readonly FrameworkElement sourceKinds;
    private readonly CheckBox microphone, systemAudio;
    private readonly Button start, testRecording, playTest, pause, stop, runtimeHelp;
    private readonly ProgressBar microphoneMeter = RecordingAudioMeter.Create(6, new Thickness(0, -2, 0, 8));
    private readonly ProgressBar systemMeter = RecordingAudioMeter.Create(6, new Thickness(0, -2, 0, 8));
    private readonly DispatcherTimer meterTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly TextBlock status = Ui.Text(L.T("Choose a display, then start recording."), 13, muted: true);
    private readonly TextBlock time = Ui.Text("00:00:00", 32, true);
    private readonly StackPanel markerList = new();
    private readonly RecordingMarkerTimeline markerTimeline = new();
    private HotkeyService? markerHotkey;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private ScreenRecordingService? service;
    private Task? session;
    private int sessionGeneration;
    private RecordingHudWindow? hud;
    private bool paused, closing, closed, stopping;
    private readonly AppController controller;
    private readonly Func<Window, string?>? chooseOutput;
    private readonly Func<IReadOnlyList<MonitorInfo>> readMonitors;
    private readonly Func<IReadOnlyList<string>> readMissingRuntime;
    private bool runtimeMissing;
    private IReadOnlyList<string> missingRuntimeFiles = [];
    private string? testClipPath;
    internal bool IsRecording => service?.Active == true;
    public ScreenRecorderWindow(AppController controller, Func<Window, string?>? chooseOutput = null, Func<IReadOnlyList<MonitorInfo>>? readMonitors = null, Func<IReadOnlyList<string>>? readMissingRuntime = null)
    {
        this.readMonitors = readMonitors ?? MonitorService.GetAll;
        this.readMissingRuntime = readMissingRuntime ?? RecordingPrerequisites.FindMissingVisualCppRuntimeFiles;
        this.controller = controller; this.chooseOutput = chooseOutput; Title = L.T("Screen recorder"); Width = 1120; Height = 800; MinWidth = 900; MinHeight = 640; WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent; ResizeMode = ResizeMode.CanResizeWithGrip; Topmost = false; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel(); var header = UtilityWindowChrome.Header(this, "DesktopTools — " + Title, Close, L.T("Close recorder"), 13, allowMinimize: true); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var panel = new StackPanel { Margin = new Thickness(12, 0, 6, 0) };
        sourceChoice = Ui.Button(L.T("Recording source"), async () => await ChooseSourceAsync()); sourceChoice.Content = Ui.IconLabel("Monitor", L.T("Recording source"));
        microphone = Ui.Toggle(controller.Settings.RecordingMicrophone, value => controller.UpdateSettings(s => s.RecordingMicrophone = value));
        systemAudio = Ui.Toggle(controller.Settings.RecordingSystemAudio, value => controller.UpdateSettings(s => s.RecordingSystemAudio = value));
        System.Windows.Automation.AutomationProperties.SetName(microphoneMeter, L.T("Microphone activity"));
        System.Windows.Automation.AutomationProperties.SetName(systemMeter, L.T("System audio activity"));
        testRecording = Ui.ActionButton("Record", L.T("Record 5-second test"), () => StartRecording(test: true));
        testRecording.HorizontalContentAlignment = HorizontalAlignment.Center;
        playTest = Ui.ActionButton("Play", L.T("Play test clip"), PlayTestClip); playTest.Visibility = Visibility.Collapsed; playTest.Margin = new Thickness(8, 0, 0, 0);
        var testActions = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(playTest, Dock.Right); testActions.Children.Add(playTest); testRecording.Margin = new Thickness(0); testActions.Children.Add(testRecording);
        panel.Children.Add(Ui.Group("Audio", L.T("Sound"), L.T("Meters show device activity. Use a test clip to verify recorded sound."),
            Ui.Row(L.T("Microphone"), L.T("Default Windows input device."), microphone), microphoneMeter,
            Ui.Row(L.T("System audio"), L.T("Default Windows output device."), systemAudio), systemMeter,
            testActions));
        var qualityGrid = new Grid(); qualityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) }); qualityGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.8, GridUnitType.Star) });
        var qualityColumn = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        qualityColumn.Children.Add(Ui.Text(L.T("Quality"), 12, muted: true));
        qualityChoice = Ui.Choice(new[] { "Economy", "Balanced", "High" }, controller.Settings.RecordingQuality, value => controller.UpdateSettings(s => s.RecordingQuality = value));
        qualityChoice.HorizontalAlignment = HorizontalAlignment.Stretch; qualityChoice.Margin = new Thickness(0, 6, 0, 0); qualityColumn.Children.Add(qualityChoice); qualityGrid.Children.Add(qualityColumn);
        var fpsColumn = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        fpsColumn.Children.Add(Ui.Text("FPS", 12, muted: true));
        fpsChoice = Ui.Choice(new[] { "24", "30", "60", "90", "120", "144" }, controller.Settings.RecordingFramesPerSecond.ToString(), value => controller.UpdateSettings(s => s.RecordingFramesPerSecond = int.Parse(value)), translate: false);
        fpsChoice.MinWidth = 100; fpsChoice.HorizontalAlignment = HorizontalAlignment.Stretch; fpsChoice.Margin = new Thickness(0, 6, 0, 0); fpsColumn.Children.Add(fpsChoice); Grid.SetColumn(fpsColumn, 1); qualityGrid.Children.Add(fpsColumn);
        var fpsHint = Ui.Text(L.T("Frame rate is a target. A display or region records at the screen’s rate; a single window is limited to about 48 FPS."), 11, muted: true);
        fpsHint.TextWrapping = TextWrapping.Wrap; fpsHint.Margin = new Thickness(0, 9, 0, 2);
        hardwareAcceleration = Ui.Toggle(controller.Settings.RecordingHardwareAcceleration, value => controller.UpdateSettings(s => s.RecordingHardwareAcceleration = value));
        panel.Children.Add(Ui.Group("Video", L.T("Recording quality"), null, qualityGrid, fpsHint,
            Ui.Row(L.T("Hardware acceleration"), L.T("Uses the graphics card to encode. Without it, recording is limited to 30 FPS."), hardwareAcceleration)));
        var guide = FeatureTourButton.Create(this, "recorder", () => new GuidedTour.Step[]
        {
            new(() => sourceChoice, "Recording source", "Choose a display, window or region. Selecting a source does not start recording."),
            new(() => microphone, "Microphone", "Enable microphone audio only when you want your voice in the recording."),
            new(() => systemAudio, "System audio", "System audio records sounds played by the computer."),
            new(() => qualityGrid, "Recording quality", "Choose quality and target FPS before starting. Stop in the floating capsule finishes the MP4.")
        }, controller.Settings, controller.UpdateSettings); DockPanel.SetDock(guide, Dock.Right); header.Children.Insert(header.Children.Count - 1, guide);
        start = Ui.ActionButton("Record", L.T("Start recording"), StartRecording, ButtonKind.Primary, 14);
        start.MinHeight = 44; start.Margin = new Thickness(0);
        pause = Ui.ActionButton("Pause", L.T("Pause"), TogglePause); pause.Margin = new Thickness(0, 0, 4, 0);
        stop = Ui.ActionButton("Stop", L.T("Stop and save"), () => _ = StopAsync()); stop.Margin = new Thickness(4, 0, 0, 0);
        SetPauseContent();
        runtimeHelp = Ui.Button(L.T("Open Microsoft Visual C++ Runtime download page"), OpenVisualCppRuntimeDownloadPage); runtimeHelp.Visibility = Visibility.Collapsed;
        var transport = new StackPanel();
        var recordingState = new StackPanel();
        time.FontSize = 25; recordingState.Children.Add(time);
        status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 4, 0, 0); recordingState.Children.Add(status);
        runtimeHelp.Margin = new Thickness(0, 8, 0, 0); runtimeHelp.HorizontalAlignment = HorizontalAlignment.Left; recordingState.Children.Add(runtimeHelp);
        recordingState.Children.Add(new ScrollViewer { Content = markerList, MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        transport.Children.Add(recordingState);
        var actions = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        actions.RowDefinitions.Add(new RowDefinition()); actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumnSpan(start, 2); actions.Children.Add(start);
        Grid.SetRow(pause, 1); Grid.SetRow(stop, 1); Grid.SetColumn(stop, 1); pause.Margin = new Thickness(0, 8, 4, 0); stop.Margin = new Thickness(4, 8, 0, 0);
        actions.Children.Add(pause); actions.Children.Add(stop); transport.Children.Add(actions);
        var transportSurface = new Border { Child = transport, Padding = new Thickness(14), Margin = new Thickness(12, 10, 0, 0), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        transportSurface.SetResourceReference(Border.BackgroundProperty, "Field"); transportSurface.SetResourceReference(Border.BorderBrushProperty, "Stroke");
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(392) }); root.Children.Add(columns);
        var inspectorPanel = new DockPanel(); Grid.SetColumn(inspectorPanel, 1); columns.Children.Add(inspectorPanel);
        DockPanel.SetDock(transportSurface, Dock.Bottom); inspectorPanel.Children.Add(transportSurface);
        var inspector = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; inspectorPanel.Children.Add(inspector);
        var left = new DockPanel { Margin = new Thickness(0, 8, 20, 0) }; columns.Children.Add(left);
        var sourceHeading = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var formatBadge = Ui.Text("MP4", 11, true, muted: true); DockPanel.SetDock(formatBadge, Dock.Right); sourceHeading.Children.Add(formatBadge);
        sourceHeading.Children.Add(Ui.Text(L.T("Recording source"), 17, true)); DockPanel.SetDock(sourceHeading, Dock.Top); left.Children.Add(sourceHeading);
        refreshPreview = Ui.Button(L.T("Refresh preview"), async () => await RefreshPreviewAsync()); refreshPreview.Content = Ui.IconLabel("Capture", L.T("Refresh preview"));
        var previewTools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        previewTools.Children.Add(sourceChoice); previewTools.Children.Add(refreshPreview);
        DockPanel.SetDock(previewTools, Dock.Bottom); left.Children.Add(previewTools);
        var stageContent = new Grid(); stageContent.Children.Add(preview);
        var hint = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var display = Ui.Icon("Monitor", 42); display.HorizontalAlignment = HorizontalAlignment.Center; display.Margin = new Thickness(0, 0, 0, 13); hint.Children.Add(display);
        previewTitle = Ui.Text(L.T("Choose what to record."), 18, true); previewTitle.TextAlignment = TextAlignment.Center; hint.Children.Add(previewTitle);
        previewDescription = Ui.Text(L.T("Click to choose a display, window or region."), 12, muted: true); previewDescription.TextAlignment = TextAlignment.Center; previewDescription.Margin = new Thickness(0, 4, 0, 0); hint.Children.Add(previewDescription);
        var kinds = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 19, 0, 0) };
        foreach (var (icon, label) in new[] { ("Monitor", "Display"), ("Window", "Window"), ("Crop", "Region") })
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal }; item.Children.Add(Ui.Icon(icon, 15));
            var text = Ui.Text(L.T(label), 11); text.Margin = new Thickness(6, 0, 0, 0); item.Children.Add(text);
            var badge = new Border { Child = item, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(3, 0, 3, 0), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
            badge.SetResourceReference(Border.BackgroundProperty, "Field"); badge.SetResourceReference(Border.BorderBrushProperty, "Stroke"); kinds.Children.Add(badge);
        }
        sourceKinds = kinds; hint.Children.Add(sourceKinds);
        previewHint = hint; stageContent.Children.Add(previewHint);
        sourcePreview = Ui.Button(L.T("Choose recording source"), async () => await ChooseSourceAsync()); sourcePreview.Tag = "recording-source-preview";
        sourcePreview.Content = stageContent; sourcePreview.HorizontalContentAlignment = HorizontalAlignment.Stretch; sourcePreview.VerticalContentAlignment = VerticalAlignment.Stretch;
        sourcePreview.Padding = new Thickness(12); sourcePreview.Margin = new Thickness(0); sourcePreview.SetResourceReference(Control.BackgroundProperty, "Card");
        Ui.Tip(sourcePreview, L.T("Choose recording source")); left.Children.Add(sourcePreview);
        var card = Ui.Card(root, 18); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); card.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); Content = card;
        timer.Tick += (_, _) => UpdateRecordingUi();
        meterTimer.Tick += (_, _) => UpdateMeters();
        Loaded += (_, _) => meterTimer.Start();
        Closing += OnClosing; Closed += (_, _) => { closed = true; sourcePicker?.Close(); sourceThumbnail.Dispose(); timer.Stop(); meterTimer.Stop(); preview.Source = null; SystemEvents.DisplaySettingsChanged -= DisplaysChanged; DeleteTestClip(); };
        SizeChanged += (_, _) => { if (IsLoaded) sourceThumbnail.Update(this, preview); };
        SystemEvents.DisplaySettingsChanged += DisplaysChanged; Refresh();
        var missingOnOpen = this.readMissingRuntime();
        if (missingOnOpen.Count > 0) ShowMissingRuntime(missingOnOpen);
        Motion.WindowEntrance(this);
        AddHandler(FeatureTourButton.SetupAppliedEvent, new RoutedEventHandler((_, _) => { microphone.IsChecked = controller.Settings.RecordingMicrophone; systemAudio.IsChecked = controller.Settings.RecordingSystemAudio; Refresh(); }));
    }
    private async Task RefreshPreviewAsync()
    {
        if (session != null || selectedSource == null) return;
        if (selectedSource.Window is { } window) { sourceThumbnail.Show(this, preview, window.Handle); return; }
        var source = selectedSource;
        try
        {
            await Task.Delay(180);
            if (closed || closing || session != null || !ReferenceEquals(selectedSource, source)) return;
            ValidateSource(source);
            using var exclusion = new PreviewCaptureScope();
            var bitmap = CaptureService.Capture(source.Monitor!);
            if (source.Region is Rect r) { bitmap = new System.Windows.Media.Imaging.CroppedBitmap(bitmap, new Int32Rect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height)); bitmap.Freeze(); }
            preview.Source = bitmap; previewHint.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) { if (!closed) { if (runtimeMissing) ShowMissingRuntime(missingRuntimeFiles); else status.Text = ex.Message; } }
    }
    private async Task ChooseSourceAsync()
    {
        if (session != null || sourcePicker != null) return;
        sourceThumbnail.Dispose(); var picker = sourcePicker = new RecordingSourcePickerWindow { Owner = this }; NativeWindowService.ShowForeground(picker);
        try { var source = await picker.Result; if (!closed && source != null) SetSource(source, picker.SelectedPreview); }
        finally { sourcePicker = null; if (!closed && !closing && selectedSource?.Window is { } window) { UpdateLayout(); sourceThumbnail.Show(this, preview, window.Handle); } }
    }
    internal void SetSource(RecordingSelection source, System.Windows.Media.Imaging.BitmapSource? bitmap = null)
    {
        if (session != null) throw new InvalidOperationException("Cannot change an active recording source.");
        selectedSource = source; sourceThumbnail.Dispose(); preview.Source = bitmap; previewHint.Visibility = bitmap != null || source.Window != null ? Visibility.Collapsed : Visibility.Visible;
        previewTitle.Text = source.Label; previewDescription.Text = L.T("Refresh preview"); sourceKinds.Visibility = Visibility.Collapsed;
        sourceChoice.Content = Ui.IconLabel(source.Window == null ? "Monitor" : "Window", L.T("Recording source")); Ui.Tip(sourceChoice, source.Label); status.Text = source.Label; Refresh();
        if (runtimeMissing) ShowMissingRuntime(missingRuntimeFiles);
    }
    private void StartRecording() => StartRecording(test: false);
    private void StartRecording(bool test)
    {
        if (session != null) return;
        markerTimeline.Clear(); markerList.Children.Clear();
        var missingFiles = readMissingRuntime();
        if (missingFiles.Count > 0)
        {
            ShowMissingRuntime(missingFiles);
            controller.Report(L.T("Screen recording is unavailable until Microsoft Visual C++ x64 is installed."), NotificationKind.Warning);
            return;
        }
        runtimeMissing = false;
        missingRuntimeFiles = [];
        runtimeHelp.Visibility = Visibility.Collapsed;
        if (selectedSource == null) { status.Text = L.T("Choose a display, then start recording."); Refresh(); return; }
        int generation = ++sessionGeneration;
        session = RunRecordingWithBoundaryAsync(generation, test);
        Refresh();
    }
    private async Task RunRecordingWithBoundaryAsync(int generation, bool test)
    {
        await Task.Yield(); // Assign session before the recorder method is invoked or a close can await it.
        try { await RecordAsync(test); }
        catch (Exception ex)
        {
            if (!closed && sessionGeneration == generation) status.Text = L.T("Recording failed: ") + ex.Message;
        }
        finally
        {
            if (sessionGeneration == generation)
            {
                session = null;
                paused = stopping = false;
                if (!closed)
                {
                    Refresh();
                    if (closing) Close();
                }
            }
        }
    }
    private void ShowMissingRuntime(IReadOnlyList<string> missingFiles)
    {
        runtimeMissing = true;
        missingRuntimeFiles = missingFiles;
        runtimeHelp.Visibility = Visibility.Visible;
        status.Text = L.T("Screen recording needs the Microsoft Visual C++ x64 Runtime. Install it, then try Record again.")
            + "\n" + L.T("Missing files: ") + string.Join(", ", missingFiles);
        Refresh();
    }
    private void OpenVisualCppRuntimeDownloadPage()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(RecordingPrerequisites.VisualCppRuntimeHelpUri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            status.Text = L.T("Could not open the Microsoft download page. Use this link: ") + RecordingPrerequisites.VisualCppRuntimeHelpUri.AbsoluteUri + "\n" + ex.Message;
        }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private async Task RecordAsync(bool test)
    {
        await Task.Yield(); // Assign session before any synchronous cancellation can complete.
        Refresh();
        bool acceptingStatus = true;
        try
        {
            if (closed || closing || stopping) return;
            if (selectedSource == null) return;
            var source = selectedSource;
            ValidateSource(source);
            ScreenRecordingService.ValidateAudioSources(microphone.IsChecked == true, systemAudio.IsChecked == true);
            string? outputPath;
            if (test) outputPath = Path.Combine(Path.GetTempPath(), "DesktopTools-recording-test-" + Guid.NewGuid().ToString("N") + ".mp4");
            else
            {
                var save = new SaveFileDialog { Filter = "MP4 video|*.mp4", DefaultExt = ".mp4", AddExtension = true, FileName = "DesktopTools-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), OverwritePrompt = true };
                outputPath = chooseOutput != null ? chooseOutput(this) : save.ShowDialog(this) == true ? save.FileName : null;
            }
            if (outputPath == null || closed || closing || stopping) return;
            if (RecordingReadiness.CheckDisk(outputPath) is string diskProblem) throw new IOException(diskProblem);
            ValidateSource(source);
            service = new ScreenRecordingService(); var current = service;
            service.StatusChanged += native => Dispatcher.BeginInvoke(() => { if (acceptingStatus && !closed && ReferenceEquals(service, current)) { status.Text = L.T(native); UpdateRecordingUi(); } });
            sourceThumbnail.Dispose();
            var result = source.Window is { } capturedWindow
                ? service.StartSource(new ScreenRecorderLib.WindowRecordingSource(capturedWindow.Handle) { IsCursorCaptureEnabled = true, IsBorderRequired = true }, outputPath, microphone.IsChecked == true, systemAudio.IsChecked == true, controller.Settings.RecordingFramesPerSecond, controller.Settings.RecordingQuality, controller.Settings.RecordingHardwareAcceleration)
                : service.Start(source.Monitor!, source.Region, outputPath, microphone.IsChecked == true, systemAudio.IsChecked == true, controller.Settings.RecordingFramesPerSecond, controller.Settings.RecordingQuality, controller.Settings.RecordingHardwareAcceleration);
            if (test)
            {
                _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => Dispatcher.BeginInvoke(() =>
                {
                    if (!closed && ReferenceEquals(service, current) && !stopping) current.Stop();
                }), TaskScheduler.Default);
            }
            hud = new RecordingHudWindow(source.Label, TogglePause, () => _ = StopAsync(), AppCapturePrivacy.ShouldHideFeature("Screen recorder", controller.Settings), source.Monitor ?? MonitorService.GetForWindow(this), AddMarker);
            try
            {
                markerHotkey = new HotkeyService(); markerHotkey.Pressed += MarkerHotkeyPressed;
                if (markerHotkey.RegisterAvailable(new Dictionary<string, string> { ["record-marker"] = "Ctrl+Alt+M" }).Count > 0)
                { markerHotkey.Pressed -= MarkerHotkeyPressed; markerHotkey.Dispose(); markerHotkey = null; }
                hud.SetMarkerShortcut(markerHotkey != null);
            }
            catch (Exception ex) { Debug.WriteLine(ex); markerHotkey?.Dispose(); markerHotkey = null; }
            Hide(); hud.Show();
            timer.Start(); status.Text = L.T(service.Status); Refresh(); UpdateRecordingUi();
            var output = await result; acceptingStatus = false; UpdateRecordingUi();
            string rateInfo = "";
            try
            {
                double? encodedRate = await VideoEditorService.ProbeEncodedFrameRateAsync(output);
                if (encodedRate > 0) rateInfo = "\n" + L.F($"File reports {encodedRate.Value:0.#} FPS. Repeated frames may reduce visible smoothness.");
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
            if (test)
            {
                DeleteTestClip(); testClipPath = output; playTest.Visibility = Visibility.Visible;
                status.Text = L.T("Test clip is ready. Play it to check picture and sound.") + rateInfo;
            }
            else
            {
                status.Text = L.T("Recording saved: ") + output + rateInfo + (current.StopReason is string reason ? "\n" + L.T(reason) : "");
                ShowSavedMarkers(output);
                try { controller.RecordingMarkers.Save(output, markerTimeline.Markers); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    status.Text += "\n" + L.T("Recording saved, but markers could not be saved for later: ") + ex.Message;
                    controller.Report(L.T("Recording saved, but markers could not be saved for later: ") + ex.Message, NotificationKind.Warning);
                }
            }
        }
        catch (Exception ex) { acceptingStatus = false; if (!closed) status.Text = L.T("Recording failed: ") + ex.Message; }
        finally
        {
            acceptingStatus = false; // Late native Idle events must not overwrite the saved/error message.
            timer.Stop(); hud?.Finish(); hud = null;
            if (markerHotkey != null) { markerHotkey.Pressed -= MarkerHotkeyPressed; markerHotkey.Dispose(); markerHotkey = null; }
            try { if (service != null) await service.DisposeAsync(); }
            catch (Exception ex) { if (!closed) status.Text = L.T("Recording failed: ") + ex.Message; }
            finally
            {
                service = null; session = null; paused = stopping = false;
                if (!closed) { Refresh(); if (closing) Close(); else { Show(); Activate(); if (selectedSource?.Window is { } window) sourceThumbnail.Show(this, preview, window.Handle); } }
            }
        }
    }
    private void MarkerHotkeyPressed(string action) { if (action == "record-marker") AddMarker(); }
    private void AddMarker()
    {
        if (service == null || stopping || paused || service.SourceSuspended || service.Status != "Recording") return;
        if (!markerTimeline.Add(service.Elapsed, recording: true)) return;
        var latest = markerTimeline.Markers[^1];
        hud?.SetMarkerCount(latest.Number);
    }
    private void ShowSavedMarkers(string videoPath)
    {
        markerList.Children.Clear();
        if (markerTimeline.Markers.Count == 0) return;
        var heading = Ui.Text(L.T("Recording markers"), 12, true);
        heading.Margin = new Thickness(0, 10, 0, 4); markerList.Children.Add(heading);
        foreach (var marker in markerTimeline.Markers)
        {
            var position = marker.Position;
            var button = Ui.Button($"{L.T("Marker")} {marker.Number}  ·  {RecordingTime.Format(position, compact: true)}", () => controller.OpenVideoEditorAt(videoPath, position));
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 2, 0, 2); markerList.Children.Add(button);
        }
    }
    private void TogglePause()
    {
        if (service == null || stopping || service.SourceSuspended || service.Status is not ("Recording" or "Paused")) return;
        if (paused) service.Resume(); else service.Pause();
        paused = !paused; SetPauseContent();
        UpdateRecordingUi();
    }
    private void UpdateRecordingUi()
    {
        if (service == null) return;
        var duration = service.Elapsed; bool suspended = service.SourceSuspended;
        bool finishing = stopping || service.Status == "Finishing";
        time.Text = RecordingTime.Format(duration);
        pause.IsEnabled = !finishing && !suspended && service.Status is "Recording" or "Paused";
        hud?.Update(duration, paused || suspended, finishing, suspended, service.Status == "Starting recording");
    }
    internal async Task StopAsync(string? reason = null)
    {
        sourcePicker?.Close();
        var pending = session;
        bool firstStop = !stopping;
        if (pending != null) stopping = true;
        if (service != null && firstStop)
        {
            stop.IsEnabled = pause.IsEnabled = false; status.Text = L.T("Finishing"); service.Stop(reason);
            UpdateRecordingUi();
        }
        if (pending != null) await pending;
    }
    private void Refresh()
    {
        bool active = IsRecording; bool busy = session != null; start.IsEnabled = testRecording.IsEnabled = !busy && (selectedSource != null || runtimeMissing); playTest.IsEnabled = !busy; pause.IsEnabled = stop.IsEnabled = active && !stopping;
        sourcePreview.IsEnabled = sourceChoice.IsEnabled = microphone.IsEnabled = systemAudio.IsEnabled = !busy;
        qualityChoice.IsEnabled = fpsChoice.IsEnabled = hardwareAcceleration.IsEnabled = !busy;
        refreshPreview.IsEnabled = !busy && selectedSource != null;
        SetPauseContent();
    }
    private void SetPauseContent()
    {
        string label = L.T(paused ? "Resume" : "Pause");
        pause.Content = Ui.IconLabel(paused ? "Play" : "Pause", label, 16, false, 13);
        System.Windows.Automation.AutomationProperties.SetName(pause, label);
    }
    private void UpdateMeters()
    {
        double mic = 0, output = 0;
        try
        {
            using var audio = new AudioSessionService();
            if (microphone.IsChecked == true) mic = audio.ReadPeakLevel(true);
            if (systemAudio.IsChecked == true) output = audio.ReadPeakLevel(false);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Device changes make activity unavailable; recording validation reports hard failures.
        }
        microphoneMeter.Value = mic; systemMeter.Value = output;
        hud?.UpdateMeters(microphone.IsChecked == true ? mic : null, systemAudio.IsChecked == true ? output : null);
    }
    private void PlayTestClip()
    {
        if (testClipPath == null || !File.Exists(testClipPath)) { playTest.Visibility = Visibility.Collapsed; return; }
        try { Process.Start(new ProcessStartInfo(testClipPath) { UseShellExecute = true }); }
        catch (Exception ex) { status.Text = L.T("Could not open test clip: ") + ex.Message; }
    }
    private void DeleteTestClip()
    {
        if (testClipPath == null) return;
        try { File.Delete(testClipPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        testClipPath = null;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        closing = true; sourcePicker?.Close();
        if (session != null) { e.Cancel = true; _ = StopAsync(); }
    }
    private void ValidateSource(RecordingSelection source)
    {
        bool available = ReferenceEquals(selectedSource, source) && (source.Window is { } window ? RecordingWindows.Available(window) :
            source.Monitor is { } monitor && readMonitors().Any(m => m.Id == monitor.Id && m.Bounds == monitor.Bounds));
        if (!available) throw new InvalidOperationException(L.T("Source is no longer available. Refresh the list."));
    }
    private void DisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(async () => await RefreshDisplaysAsync());
    internal async Task RefreshDisplaysAsync()
    {
        if (closed || selectedSource is not { } source) return;
        try
        {
            // Window captures follow their HWND; unrelated monitor or scale changes do not invalidate them.
            if (source.Window is { } window && RecordingWindows.IsSameWindow(window)) return;
            if (source.Monitor is { } monitor && readMonitors().Any(m => m.Id == monitor.Id && m.Bounds == monitor.Bounds)) return;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        selectedSource = null; sourceThumbnail.Dispose(); preview.Source = null; previewHint.Visibility = Visibility.Visible;
        previewTitle.Text = L.T("Choose what to record."); previewDescription.Text = L.T("Click to choose a display, window or region."); sourceKinds.Visibility = Visibility.Visible;
        sourceChoice.Content = Ui.IconLabel("Monitor", L.T("Recording source"));
        status.Text = L.T("Source is no longer available. Refresh the list."); Refresh();
        if (runtimeMissing) ShowMissingRuntime(missingRuntimeFiles);
        await StopAsync(source.Window != null ? "Source window closed. Recording stopped." : "Source display changed. Recording stopped.");
    }
}
