using DesktopTools.Localization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopTools.Native;
using DesktopTools.UI;

namespace DesktopTools.Extras;

public sealed class AudioControlsWindow : Window
{
    private readonly IAudioControlsService audio;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly StackPanel rows = new();
    private readonly StackPanel endpoints = new();
    private readonly StackPanel microphone = new();
    private AudioEndpoint? previousOutput, previousInput;
    private IReadOnlyList<AudioOutputDevice> previousDevices = Array.Empty<AudioOutputDevice>();
    private ComboBox? outputChoice;
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Dictionary<string, Border> applicationRows = new();
    private readonly TextBlock status = Ui.Text("", 12, muted: true);
    private readonly Action<string> report;
    private bool closed;
    private bool synchronizing;
    private bool refreshAfterDrag;
    private readonly Dictionary<string, Action<AudioApplication>> applicationUpdates = new();
    private readonly Dictionary<string, Action<AudioEndpoint>> endpointUpdates = new();

    public AudioControlsWindow(Action<string> report) : this(report, new AudioSessionService()) { }
    internal AudioControlsWindow(Action<string> report, IAudioControlsService audio)
    {
        this.report = report; this.audio = audio;
        Title = L.T("DesktopTools Audio controls"); Width = 720; Height = 540; MinWidth = 600; MinHeight = 460;
        WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanResizeWithGrip; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var layout = new DockPanel();
        var header = UtilityWindowChrome.Header(this, L.T("Audio controls"), Close, L.T("Close audio controls"), 16);
        var refresh = UtilityWindowChrome.CaptionButton("RotateRight", L.T("Refresh"), Refresh); DockPanel.SetDock(refresh, Dock.Right); header.Children.Insert(1, refresh);
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        var description = Ui.Text(L.T("Choose your output and adjust each app below."), 12, muted: true);
        description.Margin = new Thickness(0, 0, 0, 18); DockPanel.SetDock(description, Dock.Top); layout.Children.Add(description);
        status.Margin = new Thickness(0, 12, 0, 0); DockPanel.SetDock(status, Dock.Bottom); layout.Children.Add(status);
        var sections = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        sections.Children.Add(endpoints);
        microphone.Margin = new Thickness(0, 0, 0, 16); sections.Children.Add(microphone);
        var heading = Ui.Text(L.T("Applications"), 14, true); heading.Margin = new Thickness(0, 0, 0, 4); sections.Children.Add(heading);
        var hint = Ui.Text(L.T("Apps appear here when they play audio."), 12, muted: true); hint.Margin = new Thickness(0, 0, 0, 10); sections.Children.Add(hint);
        sections.Children.Add(rows); scroll.Content = sections; layout.Children.Add(scroll);
        var card = Ui.Card(layout); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); card.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); Content = card;
        Motion.WindowEntrance(this);
        timer.Tick += OnTick;
        LostMouseCapture += (_, _) => { if (refreshAfterDrag && !IsMouseCaptureWithin) Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Refresh)); };
        IsVisibleChanged += (_, _) => { if (IsVisible && !closed) { Refresh(); timer.Start(); } else timer.Stop(); };
        Closed += (_, _) => { closed = true; timer.Stop(); timer.Tick -= OnTick; rows.Children.Clear(); endpoints.Children.Clear(); microphone.Children.Clear(); applicationUpdates.Clear(); applicationRows.Clear(); endpointUpdates.Clear(); audio.Dispose(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }
    private void OnTick(object? sender, EventArgs e)
    {
        // Do not replace a row while a slider is being dragged or used with the keyboard.
        if (!IsMouseCaptureWithin) Refresh();
    }
    internal void Refresh()
    {
        if (closed || IsMouseCaptureWithin || outputChoice?.IsDropDownOpen == true) return;
        refreshAfterDrag = false;
        synchronizing = true;
        try
        {
            AudioEndpoint? output = ReadEndpoint(false), input = ReadEndpoint(true);
            var devices = audio.ReadOutputDevices();
            if (output?.Id != previousOutput?.Id || output?.Name != previousOutput?.Name || !devices.SequenceEqual(previousDevices) || endpoints.Children.Count == 0)
            {
                if (previousOutput != null) endpointUpdates.Remove(previousOutput.Id);
                previousOutput = output; previousDevices = devices; endpoints.Children.Clear();
                var device = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; device.Children.Add(Ui.Text(L.T("Output device"), 11, muted: true));
                var picker = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                var settings = Ui.IconButton("Settings", L.T("Windows sound settings"), () => Run(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:sound") { UseShellExecute = true })));
                DockPanel.SetDock(settings, Dock.Right); settings.Margin = new Thickness(5, 0, 0, 0); picker.Children.Add(settings);
                outputChoice = new ComboBox { ItemsSource = devices, DisplayMemberPath = nameof(AudioOutputDevice.Name), SelectedValuePath = nameof(AudioOutputDevice.Id), SelectedValue = output?.Id, MinWidth = 0, IsEnabled = devices.Count > 0 && audio.CanSelectOutput };
                outputChoice.ToolTip = output?.Name ?? L.T("No output device"); AutomationProperties.SetName(outputChoice, L.T("Output device"));
                outputChoice.SelectionChanged += (_, _) => { if (!synchronizing && outputChoice.SelectedValue is string id && id != previousOutput?.Id) SelectOutput(id); };
                picker.Children.Add(outputChoice); device.Children.Add(picker);
                var top = new StackPanel(); top.Children.Add(device);
                if (output != null) { top.Children.Add(EndpointControls(output, L.T("Master volume"))); }
                else top.Children.Add(Ui.Text(L.T("No output device"), 12, muted: true));
                endpoints.Children.Add(top);
            }
            else if (output != null && endpointUpdates.TryGetValue(output.Id, out var updateOutput)) updateOutput(output);
            if (outputChoice != null) outputChoice.SelectedValue = output?.Id;
            if (input?.Id != previousInput?.Id || input?.Name != previousInput?.Name || microphone.Children.Count == 0)
            {
                if (previousInput != null) endpointUpdates.Remove(previousInput.Id);
                previousInput = input; microphone.Children.Clear();
                if (input != null) microphone.Children.Add(EndpointControls(input, L.T("Microphone")));
                else microphone.Children.Add(Ui.Text(L.T("No microphone found"), 12, muted: true));
            }
            else if (input != null && endpointUpdates.TryGetValue(input.Id, out var updateInput)) updateInput(input);
            var applications = output == null ? Array.Empty<AudioApplication>() : audio.ReadApplications();
            status.Text = applications.Count == 0 ? L.T("No audio applications found.") : (applications.Count == 1 ? L.F($"{applications.Count} application") : L.F($"{applications.Count} applications"));
            var activeKeys = applications.Select(a => a.Key).ToHashSet(StringComparer.Ordinal);
            foreach (string key in applicationRows.Keys.Where(key => !activeKeys.Contains(key)).ToArray())
            {
                rows.Children.Remove(applicationRows[key]); applicationRows.Remove(key); applicationUpdates.Remove(key);
            }
            int index = 0;
            foreach (var app in applications.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Key, StringComparer.Ordinal))
            {
                if (!applicationRows.TryGetValue(app.Key, out var row))
                { row = ApplicationControls(app); applicationRows.Add(app.Key, row); }
                else applicationUpdates[app.Key](app);
                if (rows.Children.IndexOf(row) != index) { rows.Children.Remove(row); rows.Children.Insert(index, row); }
                index++;
            }
        }
        catch (Exception ex) when (IsAudioError(ex))
        {
            rows.Children.Clear(); applicationUpdates.Clear(); applicationRows.Clear();
            status.Text = L.T("Audio unavailable. Check your playback device and refresh.");
        }
        finally { synchronizing = false; }
    }
    private Border ApplicationControls(AudioApplication app)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        var heading = new DockPanel { Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        var icon = ApplicationIcon(app.ProcessId); icon.Margin = new Thickness(0, 0, 12, 0); DockPanel.SetDock(icon, Dock.Left); heading.Children.Add(icon);
        var name = Ui.Text(app.Name, 13, true); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.ToolTip = app.Name; heading.Children.Add(name); grid.Children.Add(heading);
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = app.Volume, SmallChange = 1, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        AutomationProperties.SetName(slider, L.F($"{app.Name} volume")); Grid.SetColumn(slider, 1); grid.Children.Add(slider);
        var value = Ui.Text(L.F($"{app.Volume:0}%"), 12); Grid.SetColumn(value, 2); grid.Children.Add(value);
        var updateVolume = BindVolume(slider, value, app.Volume, level => audio.SetVolume(app.Key, level));
        bool muted = app.Muted; string currentName = app.Name;
        var mute = Ui.IconButton(muted ? "MutedAudio" : "Audio", L.T(muted ? "Unmute" : "Mute"), () => { });
        mute.Margin = new Thickness(0);
        void LabelMute()
        {
            Ui.Tip(mute, L.T(muted ? "Unmute" : "Mute"));
            AutomationProperties.SetName(mute, muted ? L.F($"Unmute {currentName}") : L.F($"Mute {currentName}"));
        }
        LabelMute();
        mute.Click += (_, _) => Run(() => { audio.SetMuted(app.Key, !muted); muted = !muted; mute.Content = Ui.Icon(muted ? "MutedAudio" : "Audio"); LabelMute(); });
        Grid.SetColumn(mute, 3); grid.Children.Add(mute);
        applicationUpdates[app.Key] = snapshot =>
        {
            updateVolume(snapshot.Volume);
            if (snapshot.Name != currentName)
            {
                currentName = snapshot.Name; name.Text = currentName; name.ToolTip = currentName;
                AutomationProperties.SetName(slider, L.F($"{currentName} volume")); LabelMute();
            }
            if (muted == snapshot.Muted) return;
            muted = snapshot.Muted; mute.Content = Ui.Icon(muted ? "MutedAudio" : "Audio"); LabelMute();
        };
        var card = Ui.Card(grid, 14); card.Margin = new Thickness(0, 0, 0, 8); card.CornerRadius = new CornerRadius(10);
        return card;
    }
    private AudioEndpoint? ReadEndpoint(bool input)
    {
        try { return audio.ReadEndpoint(input); }
        catch (Exception ex) when (IsAudioError(ex)) { return null; }
    }
    private void SelectOutput(string id)
    {
        string? failure = null;
        try { audio.SelectOutput(id); }
        catch (Exception ex) when (IsAudioError(ex)) { failure = L.T("Could not change the output device. You can choose it in Windows sound settings."); }
        if (outputChoice != null) outputChoice.IsDropDownOpen = false;
        Refresh();
        if (failure != null) { status.Text = failure; report(failure); }
    }
    private static FrameworkElement ApplicationIcon(uint processId)
    {
        try
        {
            if (processId > 0 && processId <= int.MaxValue)
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)processId);
                string? path = process.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path))
                {
                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    if (icon != null)
                    {
                        var image = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(24, 24));
                        image.Freeze(); return new System.Windows.Controls.Image { Source = image, Width = 24, Height = 24 };
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException or NotSupportedException) { }
        return Ui.Icon("Audio", 24);
    }
    private UIElement EndpointControls(AudioEndpoint endpoint, string label, bool compact = false)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 0 : 130) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        if (compact) { row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); row.RowDefinitions.Add(new RowDefinition()); }
        var name = Ui.Text(label, 13); name.ToolTip = endpoint.Name; name.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(name);
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = endpoint.Volume, SmallChange = 1, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        AutomationProperties.SetName(slider, label); Grid.SetColumn(slider, 1); row.Children.Add(slider);
        var value = Ui.Text(L.F($"{endpoint.Volume:0}%"), 12); value.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(value, 2); row.Children.Add(value);
        var updateVolume = BindVolume(slider, value, endpoint.Volume, level => audio.SetEndpointVolume(endpoint.Id, level));
        bool muted = endpoint.Muted;
        var mute = Ui.IconButton(muted ? "MutedAudio" : "Audio", L.T(muted ? "Unmute" : "Mute"), () => { });
        AutomationProperties.SetName(mute, label + " · " + L.T(muted ? "Unmute" : "Mute"));
        mute.Click += (_, _) => Run(() => { audio.SetEndpointMuted(endpoint.Id, !muted); muted = !muted; mute.Content = Ui.Icon(muted ? "MutedAudio" : "Audio"); Ui.Tip(mute, L.T(muted ? "Unmute" : "Mute")); AutomationProperties.SetName(mute, label + " · " + L.T(muted ? "Unmute" : "Mute")); });
        Grid.SetColumn(mute, 3); row.Children.Add(mute);
        endpointUpdates[endpoint.Id] = snapshot =>
        {
            updateVolume(snapshot.Volume);
            if (muted == snapshot.Muted) return;
            muted = snapshot.Muted; mute.Content = Ui.Icon(muted ? "MutedAudio" : "Audio");
            Ui.Tip(mute, L.T(muted ? "Unmute" : "Mute")); AutomationProperties.SetName(mute, label + " · " + L.T(muted ? "Unmute" : "Mute"));
        };
        if (compact) { name.FontSize = 11; name.Margin = new Thickness(0, 0, 0, 6); Grid.SetColumnSpan(name, 4); Grid.SetRow(slider, 1); Grid.SetRow(value, 1); Grid.SetRow(mute, 1); }
        return row;
    }
    private Action<double> BindVolume(Slider slider, TextBlock label, double initial, Action<double> write)
    {
        double confirmed = initial;
        void Show(double level)
        {
            bool previousSync = synchronizing; synchronizing = true;
            try { slider.Value = level; }
            finally { synchronizing = previousSync; }
        }
        slider.ValueChanged += (_, _) =>
        {
            label.Text = L.F($"{slider.Value:0}%");
            if (synchronizing) return;
            double requested = slider.Value;
            if (Run(() => write(requested))) confirmed = requested;
            else Show(confirmed);
        };
        return level => { confirmed = level; Show(level); };
    }
    private bool Run(Action action)
    {
        try { action(); return true; }
        catch (Exception ex) when (IsAudioError(ex)) { refreshAfterDrag = true; status.Text = L.T("Could not change audio. Refresh and try again."); report(status.Text); return false; }
    }
    private static bool IsAudioError(Exception ex) => ex is COMException or InvalidCastException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception;
}
