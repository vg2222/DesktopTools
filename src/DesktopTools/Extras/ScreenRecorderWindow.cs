using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;
using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DesktopTools.Extras;

internal sealed class ScreenRecorderWindow : Window
{
    private readonly Button sourceChoice;
    private RecordingSelection? selectedSource;
    private RecordingSourcePickerWindow? sourcePicker;
    private readonly WindowThumbnail sourceThumbnail = new();
    private readonly TextBlock qualitySummary = Ui.Text("", 12, muted: true);
    private readonly Button qualityButton;
    private readonly Image preview = new() { Stretch = Stretch.Uniform };
    private readonly Button refreshPreview;
    private readonly Button sourcePreview;
    private readonly FrameworkElement previewHint;
    private readonly CheckBox microphone, systemAudio;
    private readonly Button start, pause, stop, runtimeHelp;
    private readonly TextBlock status = Ui.Text(L.T("Choose a display, then start recording."), 13, muted: true);
    private readonly TextBlock time = Ui.Text("00:00:00", 32, true);
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
    internal bool IsRecording => service?.Active == true;
    public ScreenRecorderWindow(AppController controller, Func<Window, string?>? chooseOutput = null, Func<IReadOnlyList<MonitorInfo>>? readMonitors = null, Func<IReadOnlyList<string>>? readMissingRuntime = null)
    {
        this.readMonitors = readMonitors ?? MonitorService.GetAll;
        this.readMissingRuntime = readMissingRuntime ?? RecordingPrerequisites.FindMissingVisualCppRuntimeFiles;
        this.controller = controller; this.chooseOutput = chooseOutput; Title = L.T("Screen recorder"); Width = 940; Height = 610; MinWidth = 820; MinHeight = 540; WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent; ResizeMode = ResizeMode.CanResizeWithGrip; Topmost = true; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel(); var header = UtilityWindowChrome.Header(this, "DesktopTools — " + Title, Close, L.T("Close recorder"), 13); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var panel = new StackPanel { Margin = new Thickness(12, 8, 0, 0) };
        var setupHeading = Ui.Text(L.T("Recording setup"), 17, true); setupHeading.Margin = new Thickness(0, 0, 0, 14); panel.Children.Add(setupHeading);
        sourceChoice = Ui.Button(L.T("Recording source"), async () => await ChooseSourceAsync()); sourceChoice.Content = Ui.IconLabel("Monitor", L.T("Recording source"));
        microphone = Ui.Toggle(controller.Settings.RecordingMicrophone, value => controller.UpdateSettings(s => s.RecordingMicrophone = value));
        systemAudio = Ui.Toggle(controller.Settings.RecordingSystemAudio, value => controller.UpdateSettings(s => s.RecordingSystemAudio = value));
        panel.Children.Add(Ui.Row(L.T("Microphone"), L.T("Default Windows input device."), microphone));
        panel.Children.Add(Ui.Row(L.T("System audio"), L.T("Default Windows output device."), systemAudio));
        qualityButton = Ui.Button(L.T("Recording quality"), () => new RecordingQualityWindow(controller.Settings, (quality, fps, hardware) => { bool saved = controller.UpdateSettings(s => { s.RecordingQuality = quality; s.RecordingFramesPerSecond = fps; s.RecordingHardwareAcceleration = hardware; }); if (saved) Refresh(); return saved; }) { Owner = this }.ShowDialog());
        qualityButton.Content = Ui.IconLabel("Settings", L.T("Recording quality")); qualityButton.HorizontalAlignment = HorizontalAlignment.Left; qualityButton.Margin = new Thickness(0, 0, 0, 6); panel.Children.Add(qualityButton);
        qualitySummary.Margin = new Thickness(0, 0, 0, 12); panel.Children.Add(qualitySummary);
        var guide = FeatureTourButton.Create(this, "recorder", () => new GuidedTour.Step[]
        {
            new(() => sourceChoice, "Recording source", "Choose a display, window or region. Selecting a source does not start recording."),
            new(() => microphone, "Microphone", "Enable microphone audio only when you want your voice in the recording."),
            new(() => systemAudio, "System audio", "System audio records sounds played by the computer."),
            new(() => qualityButton, "Recording quality", "Choose quality and target FPS before starting. Stop in the floating capsule finishes the MP4.")
        }, controller.Settings, controller.UpdateSettings); DockPanel.SetDock(guide, Dock.Right); header.Children.Insert(1, guide);
        start = Ui.Button(L.T("Start recording"), StartRecording, true);
        start.Content = Ui.IconLabel("Record", L.T("Start recording"), primary: true);
        pause = Ui.IconButton("Pause", L.T("Pause"), TogglePause); stop = Ui.IconButton("Stop", L.T("Stop and save"), () => _ = StopAsync());
        runtimeHelp = Ui.Button(L.T("Open Microsoft Visual C++ Runtime download page"), OpenVisualCppRuntimeDownloadPage); runtimeHelp.Visibility = Visibility.Collapsed;
        var transport = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        transport.ColumnDefinitions.Add(new ColumnDefinition()); transport.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var recordingState = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };
        time.FontSize = 25; recordingState.Children.Add(time);
        status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 4, 0, 0); recordingState.Children.Add(status);
        runtimeHelp.Margin = new Thickness(0, 8, 0, 0); runtimeHelp.HorizontalAlignment = HorizontalAlignment.Left; recordingState.Children.Add(runtimeHelp);
        transport.Children.Add(recordingState);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        start.MinWidth = 154; pause.Margin = new Thickness(4, 0, 4, 0); actions.Children.Add(start); actions.Children.Add(pause); actions.Children.Add(stop);
        Grid.SetColumn(actions, 1); transport.Children.Add(actions);
        var transportSurface = new Border { Child = transport, Padding = new Thickness(16, 0, 16, 16), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        transportSurface.SetResourceReference(Border.BackgroundProperty, "Field"); transportSurface.SetResourceReference(Border.BorderBrushProperty, "Stroke");
        DockPanel.SetDock(transportSurface, Dock.Bottom); root.Children.Add(transportSurface);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) }); root.Children.Add(columns);
        var inspector = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(inspector, 1); columns.Children.Add(inspector);
        var left = new DockPanel { Margin = new Thickness(0, 8, 20, 0) }; columns.Children.Add(left);
        refreshPreview = Ui.Button(L.T("Refresh preview"), async () => await RefreshPreviewAsync()); refreshPreview.Content = Ui.IconLabel("Capture", L.T("Refresh preview"));
        var previewTools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        previewTools.Children.Add(sourceChoice); previewTools.Children.Add(refreshPreview);
        DockPanel.SetDock(previewTools, Dock.Bottom); left.Children.Add(previewTools);
        var stageContent = new Grid(); stageContent.Children.Add(preview);
        var hint = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var display = Ui.Icon("Monitor", 48); display.HorizontalAlignment = HorizontalAlignment.Center; display.Margin = new Thickness(0, 0, 0, 14); hint.Children.Add(display);
        hint.Children.Add(Ui.Text(L.T("Selected display"), 17, true)); hint.Children.Add(Ui.Text(L.T("Click to choose a display, window or region."), 12, muted: true));
        previewHint = hint; stageContent.Children.Add(previewHint);
        sourcePreview = Ui.Button(L.T("Choose recording source"), async () => await ChooseSourceAsync()); sourcePreview.Tag = "recording-source-preview";
        sourcePreview.Content = stageContent; sourcePreview.HorizontalContentAlignment = HorizontalAlignment.Stretch; sourcePreview.VerticalContentAlignment = VerticalAlignment.Stretch;
        sourcePreview.Padding = new Thickness(12); sourcePreview.Margin = new Thickness(0); sourcePreview.SetResourceReference(Control.BackgroundProperty, "Card");
        Ui.Tip(sourcePreview, L.T("Choose recording source")); left.Children.Add(sourcePreview);
        var card = Ui.Card(root, 18); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); card.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); Content = card;
        timer.Tick += (_, _) => UpdateRecordingUi();
        Closing += OnClosing; Closed += (_, _) => { closed = true; sourcePicker?.Close(); sourceThumbnail.Dispose(); timer.Stop(); preview.Source = null; SystemEvents.DisplaySettingsChanged -= DisplaysChanged; };
        SizeChanged += (_, _) => { if (IsLoaded) sourceThumbnail.Update(this, preview); };
        SystemEvents.DisplaySettingsChanged += DisplaysChanged; Refresh(); Motion.WindowEntrance(this);
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
        catch (Exception ex) { if (!closed) status.Text = ex.Message; }
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
        sourceChoice.Content = Ui.IconLabel(source.Window == null ? "Monitor" : "Window", L.T("Recording source")); Ui.Tip(sourceChoice, source.Label); status.Text = source.Label; Refresh();
    }
    private void StartRecording()
    {
        if (session != null) return;
        var missingFiles = readMissingRuntime();
        if (missingFiles.Count > 0)
        {
            ShowMissingRuntime(missingFiles);
            return;
        }
        runtimeHelp.Visibility = Visibility.Collapsed;
        int generation = ++sessionGeneration;
        session = RunRecordingWithBoundaryAsync(generation);
        Refresh();
    }
    private async Task RunRecordingWithBoundaryAsync(int generation)
    {
        await Task.Yield(); // Assign session before the recorder method is invoked or a close can await it.
        try { await RecordAsync(); }
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
        runtimeHelp.Visibility = Visibility.Visible;
        status.Text = L.T("Screen recording needs the Microsoft Visual C++ x64 Runtime. Install it, then restart DesktopTools.")
            + "\n" + L.T("Missing files: ") + string.Join(", ", missingFiles);
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
    private async Task RecordAsync()
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
            var save = new SaveFileDialog { Filter = "MP4 video|*.mp4", DefaultExt = ".mp4", AddExtension = true, FileName = "DesktopTools-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), OverwritePrompt = true };
            var outputPath = chooseOutput != null ? chooseOutput(this) : save.ShowDialog(this) == true ? save.FileName : null;
            if (outputPath == null || closed || closing || stopping) return;
            ValidateSource(source);
            service = new ScreenRecordingService(); var current = service;
            service.StatusChanged += native => Dispatcher.BeginInvoke(() => { if (acceptingStatus && !closed && ReferenceEquals(service, current)) { status.Text = L.T(native); UpdateRecordingUi(); } });
            sourceThumbnail.Dispose();
            var result = source.Window is { } capturedWindow
                ? service.StartSource(new ScreenRecorderLib.WindowRecordingSource(capturedWindow.Handle) { IsCursorCaptureEnabled = true, IsBorderRequired = true }, outputPath, microphone.IsChecked == true, systemAudio.IsChecked == true, controller.Settings.RecordingFramesPerSecond, controller.Settings.RecordingQuality, controller.Settings.RecordingHardwareAcceleration)
                : service.Start(source.Monitor!, source.Region, outputPath, microphone.IsChecked == true, systemAudio.IsChecked == true, controller.Settings.RecordingFramesPerSecond, controller.Settings.RecordingQuality, controller.Settings.RecordingHardwareAcceleration);
            hud = new RecordingHudWindow(source.Label, TogglePause, () => _ = StopAsync(), AppCapturePrivacy.ShouldHideFeature("Screen recorder", controller.Settings), source.Monitor ?? MonitorService.GetForWindow(this));
            Hide(); hud.Show();
            timer.Start(); status.Text = L.T(service.Status); Refresh(); UpdateRecordingUi();
            var output = await result; acceptingStatus = false; UpdateRecordingUi();
            status.Text = L.T("Recording saved: ") + output + (current.StopReason is string reason ? "\n" + L.T(reason) : "");
        }
        catch (Exception ex) { acceptingStatus = false; if (!closed) status.Text = L.T("Recording failed: ") + ex.Message; }
        finally
        {
            acceptingStatus = false; // Late native Idle events must not overwrite the saved/error message.
            timer.Stop(); hud?.Finish(); hud = null;
            try { if (service != null) await service.DisposeAsync(); }
            catch (Exception ex) { if (!closed) status.Text = L.T("Recording failed: ") + ex.Message; }
            finally
            {
                service = null; session = null; paused = stopping = false;
                if (!closed) { Refresh(); if (closing) Close(); else { Show(); Activate(); if (selectedSource?.Window is { } window) sourceThumbnail.Show(this, preview, window.Handle); } }
            }
        }
    }
    private void TogglePause()
    {
        if (service == null || stopping || service.SourceSuspended || service.Status is not ("Recording" or "Paused")) return;
        if (paused) service.Resume(); else service.Pause();
        paused = !paused; pause.Content = Ui.Icon(paused ? "Play" : "Pause"); Ui.Tip(pause, L.T(paused ? "Resume" : "Pause"));
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
        bool active = IsRecording; bool busy = session != null; start.IsEnabled = !busy && selectedSource != null; pause.IsEnabled = stop.IsEnabled = active && !stopping;
        sourcePreview.IsEnabled = sourceChoice.IsEnabled = microphone.IsEnabled = systemAudio.IsEnabled = !busy;
        qualitySummary.Text = L.T(controller.Settings.RecordingQuality) + " · " + controller.Settings.RecordingFramesPerSecond + " FPS";
        qualityButton.IsEnabled = !busy;
        refreshPreview.IsEnabled = !busy && selectedSource != null;
        pause.Content = Ui.Icon(paused ? "Play" : "Pause");
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
        sourceChoice.Content = Ui.IconLabel("Monitor", L.T("Recording source"));
        status.Text = L.T("Source is no longer available. Refresh the list."); Refresh();
        await StopAsync(source.Window != null ? "Source window closed. Recording stopped." : "Source display changed. Recording stopped.");
    }
}
