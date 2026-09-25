using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using DesktopTools.Updates;
using DesktopTools.Localization;

namespace DesktopTools.Installer;

internal sealed partial class InstallerWindow : Window
{
    private static readonly Brush BackgroundBrush = Brush("#14171C");
    private static readonly Brush RailBrush = Brush("#1B2028");
    private static readonly Brush CardBrush = Brush("#1D222A");
    private static readonly Brush StrokeBrush = Brush("#343B46");
    private static readonly Brush TextBrush = Brush("#F2F4F7");
    private static readonly Brush MutedBrush = Brush("#A8B0BC");
    private static readonly Brush AccentBrush = Brush("#3478F6");
    private static readonly Brush AccentHoverBrush = Brush("#4A8AFF");
    private readonly bool uninstall;
    private readonly bool automaticUpdate;
    private readonly Program.SetupMode setupMode;
    private TextBlock headline = null!;
    private TextBlock status = null!;
    private TextBlock statusHint = null!;
    private Border progressTrack = null!;
    private Border progressFill = null!;
    private Button primary = null!;
    private Button cancel = null!;
    private Button? browse;
    private Button? repair;
    private Button? remove;
    private Button? moreOptions;
    private Border? advancedOptions;
    private Button? githubUpdate;
    private Button? languageChoice;
    private Popup? languageMenu;
    private CheckBox launch = null!;
    private RadioButton? keepData;
    private RadioButton? deleteData;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Func<CancellationToken, Task<GitHubRelease?>> latestCheck;
    private readonly string? displayedInstalledVersion;
    private readonly string displayedSetupVersion;
    private GitHubRelease? latestRelease;
    private string selectedInstallDir;
    private bool busy;
    private bool cancellableBusy;
    private bool finished;
    private bool closed;
    private int progressValue;

    internal InstallerWindow(bool uninstall) : this(uninstall, false) { }

    internal InstallerWindow(bool uninstall, bool automaticUpdate) : this(uninstall, automaticUpdate, null, null, null, null) { }

    internal InstallerWindow(bool uninstall, bool automaticUpdate, Program.SetupMode? modeOverride,
        string? installedVersionOverride, string? setupVersionOverride,
        Func<CancellationToken, Task<GitHubRelease?>>? latestCheckOverride, Func<bool>? runtimeCheckOverride = null)
    {
        this.uninstall = uninstall;
        this.automaticUpdate = automaticUpdate;
        displayedInstalledVersion = installedVersionOverride ?? Program.InstalledVersion;
        displayedSetupVersion = setupVersionOverride ?? Program.Version;
        setupMode = uninstall ? Program.SetupMode.Maintenance : modeOverride ?? Program.DecideSetupMode(displayedInstalledVersion, displayedSetupVersion);
        latestCheck = latestCheckOverride ?? FetchLatestReleaseAsync;
        runtimeCheck = runtimeCheckOverride ?? RecordingRuntime.IsAvailable;
        selectedInstallDir = Program.InstallDir;
        L.Use(L.SystemLanguage());
        Title = L.T(uninstall ? "Remove DesktopTools" : "DesktopTools Setup");
        BuildLayout();
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.GetPosition(this).Y > 64 || IsInteractive(e.OriginalSource as DependencyObject)) return;
            DragMove(); e.Handled = true;
        };
        Activated += (_, _) => { if (!busy) RefreshRuntime(); };
        Closing += (_, e) =>
        {
            if (busy && !cancellableBusy) { e.Cancel = true; return; }
            if (languageMenu is not null) languageMenu.IsOpen = false;
            closed = true;
            lifetime.Cancel();
        };
        Loaded += async (_, _) =>
        {
            DesktopTools.Presentation.WindowDismissal.Attach(this, () => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast);
            if (primary.IsVisible) primary.Focus(); else githubUpdate?.Focus();
            AnimateEntrance((FrameworkElement)Content);
            if (!uninstall)
            {
                if (automaticUpdate) await ExecuteAsync(forceLocal: true);
                else await CheckLatestAsync();
            }
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !busy) { Close(); e.Handled = true; } };
    }

    private async Task CheckLatestAsync()
    {
        if (checkingLatest || busy || finished || closed) return;
        checkingLatest = true;
        SetStatus("Checking GitHub for updates…");
        if (githubUpdate is not null) githubUpdate.IsEnabled = false;
        try
        {
            GitHubRelease? release = await latestCheck(lifetime.Token);
            if (busy || finished || closed) return;
            latestRelease = release is not null && GitHubReleaseClient.IsNewer(release.Version, displayedSetupVersion)
                && (displayedInstalledVersion is null || GitHubReleaseClient.IsNewer(release.Version, displayedInstalledVersion)) ? release : null;
            if (latestRelease is not null)
            {
                SetStatus(L.F($"Version {latestRelease.Version} is available on GitHub"));
                statusHint.Text = L.T("Download is checksum-verified before the newer setup opens.");
                if (githubUpdate is not null)
                {
                    githubUpdate.Visibility = Visibility.Visible;
                    updateDescription!.Text = L.F($"Download version {latestRelease.Version} from GitHub. Your saved data is kept.");
                    if (advancedOptions is not null) advancedOptions.Visibility = Visibility.Visible;
                }
            }
            else
            {
                SetStatus("No newer public release found");
                statusHint.Text = L.T(setupMode == Program.SetupMode.OlderSetup
                    ? "Repair requires a setup matching or newer than the installed version."
                    : "You're ready to continue with this setup.");
                if (updateDescription is not null && setupMode != Program.SetupMode.Update)
                    updateDescription.Text = L.T("No newer public release found. Select to check again.");
                if (setupMode == Program.SetupMode.Install && githubUpdate is not null) githubUpdate.Visibility = Visibility.Collapsed;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (busy || finished || closed) return;
            SetStatus("Could not check GitHub");
            statusHint.Text = L.T("You can continue with local maintenance. ") + ex.Message;
            if (updateDescription is not null) updateDescription.Text = L.T("Couldn't reach GitHub. Select to try again.");
        }
        finally { checkingLatest = false; if (!busy && !closed) RestoreActionState(); }
    }

    private static async Task<GitHubRelease?> FetchLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var client = new GitHubReleaseClient();
        return await client.GetLatestAsync(cancellationToken);
    }

    private async Task DownloadUpdateAsync()
    {
        if (busy || finished || closed || latestRelease is null) return;
        SetBusy(cancellable: true);
        try
        {
            SetStatus("Downloading verified update…"); SetProgress(3);
            var progress = new Progress<double>(value => SetProgress((int)Math.Round(value * 100)));
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTools", "Updates");
            using var client = new GitHubReleaseClient();
            string installer = await client.DownloadInstallerAsync(latestRelease, directory, progress, lifetime.Token);
            Process.Start(new ProcessStartInfo(installer) { UseShellExecute = true });
            finished = true;
            Close();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { Close(); }
        catch (Exception ex)
        {
            SetStatus("Could not download the update"); statusHint.Text = ex.Message; SetProgress(0);
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            busy = cancellableBusy = false;
            if (!finished && !closed) RestoreActionState();
        }
    }

    private void StartBundledUninstall()
    {
        if (busy || finished || closed) return;
        try
        {
            Program.StartBundledUninstall();
            Close();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async Task ExecuteAsync(bool forceLocal = false)
    {
        if (busy || closed) return;
        if (finished) { Close(); return; }
        if (!uninstall && !forceLocal && setupMode is Program.SetupMode.Maintenance or Program.SetupMode.OlderSetup)
        {
            await DownloadUpdateAsync();
            return;
        }
        if (!uninstall && setupMode == Program.SetupMode.OlderSetup)
            throw new InvalidOperationException(L.T("This setup cannot downgrade DesktopTools. Download a matching or newer setup to repair it."));
        bool deleteManagedData = uninstall && deleteData?.IsChecked == true;
        if (deleteManagedData && MessageBox.Show(this,
            L.T("Delete all DesktopTools-managed settings, notes, and cached update files? Original media outside DesktopTools will remain untouched."),
            L.T("Delete DesktopTools data"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        string installDir = selectedInstallDir;
        SetBusy(cancellable: false);
        statusHint.Text = L.T("This may take a moment."); SetProgress(5);
        var reporter = new Progress<(int, string)>(report =>
        {
            SetProgress(report.Item1);
            SetStatus(report.Item2);
        });
        string? languageWarning = null;
        try
        {
            if (uninstall) await Task.Run(() => Program.Remove(reporter, deleteManagedData));
            else await Task.Run(() => Program.Install(installDir, reporter));
            if (!uninstall && setupMode == Program.SetupMode.Install)
            {
                try { Program.SaveInitialLanguage(L.Language); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { languageWarning = L.T("The app was installed, but its initial language could not be saved: ") + ex.Message; }
            }
            SetProgress(100);
            headline.Text = L.T(uninstall ? "DesktopTools removed" : "You're all set");
            AnimateStateChange(headline);
            SetStatus(uninstall ? "Removal complete" : "Installation complete");
            statusHint.Text = L.T(uninstall ? (deleteManagedData ? "DesktopTools-managed data was deleted. Original external media was preserved." : "Your settings and notes were kept.")
                : runtimeCheck() ? "DesktopTools is ready to use." : "App installed. Complete the Microsoft runtime step above to enable screen recording.");
            if (languageWarning is not null) statusHint.Text += "\n" + languageWarning;
            finished = true; cancel.Visibility = Visibility.Collapsed; launch.Visibility = Visibility.Collapsed;
            primary.Content = L.T("Close");
            primary.Visibility = Visibility.Visible;
            if (advancedOptions is not null) advancedOptions.Visibility = Visibility.Collapsed;
            if (githubUpdate is not null) githubUpdate.Visibility = Visibility.Collapsed;
            if (moreOptions is not null) moreOptions.Visibility = Visibility.Collapsed;
            if (!uninstall && launch.IsChecked == true)
            {
                try
                {
                    var start = new ProcessStartInfo(System.IO.Path.Combine(installDir, "DesktopTools.exe"))
                    { WorkingDirectory = installDir, UseShellExecute = true };
                    if (automaticUpdate) start.ArgumentList.Add("--updated");
                    else if (setupMode == Program.SetupMode.Install) start.ArgumentList.Add("--setup");
                    Process.Start(start);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, L.T("DesktopTools was installed, but could not be launched: ") + ex.Message,
                        Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            SetStatus(L.T("Could not complete ") + L.T(uninstall ? "removal" : "setup"));
            statusHint.Text = ex.Message; SetProgress(0);
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            busy = cancellableBusy = false;
            if (!closed) RestoreActionState();
        }
    }

    private Button CreateMoreOptionsButton()
    {
        return Button("More options", false, () =>
        {
            if (!busy && !finished && !closed && advancedOptions is not null)
                advancedOptions.Visibility = advancedOptions.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }, 106);
    }

    private void SetBusy(bool cancellable)
    {
        busy = true;
        cancellableBusy = cancellable;
        if (languageMenu is not null) languageMenu.IsOpen = false;
        if (languageChoice is not null) languageChoice.IsEnabled = false;
        primary.IsEnabled = cancel.IsEnabled = false;
        if (browse is not null) browse.IsEnabled = false;
        if (repair is not null) repair.IsEnabled = false;
        if (remove is not null) remove.IsEnabled = false;
        if (moreOptions is not null) moreOptions.IsEnabled = false;
        if (advancedOptions is not null) advancedOptions.IsEnabled = false;
        if (githubUpdate is not null) githubUpdate.IsEnabled = false;
        if (runtimeDownload is not null) runtimeDownload.IsEnabled = false;
        if (runtimeRecheck is not null) runtimeRecheck.IsEnabled = false;
        progressTrack.Visibility = Visibility.Visible;
    }

    private void RestoreActionState()
    {
        if (languageChoice is not null) languageChoice.IsEnabled = !finished;
        cancel.IsEnabled = !finished;
        primary.IsEnabled = finished || setupMode != Program.SetupMode.OlderSetup || latestRelease is not null;
        if (browse is not null) browse.IsEnabled = !finished;
        if (repair is not null) repair.IsEnabled = !finished && setupMode != Program.SetupMode.OlderSetup;
        if (remove is not null) remove.IsEnabled = !finished;
        if (moreOptions is not null) moreOptions.IsEnabled = !finished;
        if (advancedOptions is not null) advancedOptions.IsEnabled = !finished;
        if (githubUpdate is not null) githubUpdate.IsEnabled = !finished && !checkingLatest;
        if (runtimeDownload is not null) runtimeDownload.IsEnabled = true;
        if (runtimeRecheck is not null) runtimeRecheck.IsEnabled = true;
    }

    private void ChooseFolder(TextBlock path)
    {
        var dialog = new OpenFolderDialog { Title = L.T("Choose where to install DesktopTools"), InitialDirectory = selectedInstallDir };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            selectedInstallDir = Program.ValidatePath(dialog.FolderName);
            path.Text = selectedInstallDir;
            path.ToolTip = selectedInstallDir;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static bool IsInteractive(DependencyObject? source)
    {
        for (DependencyObject? node = source; node is not null;
             node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is ButtonBase or TextBoxBase or ComboBox) return true;
        return false;
    }

    private void SetProgress(int value) { progressValue = Math.Clamp(value, 0, 100); UpdateProgress(); }

    private void UpdateProgress()
    {
        double targetWidth = Math.Max(0, progressTrack.ActualWidth * progressValue / 100d);
        if (!SystemParameters.ClientAreaAnimation || !IsLoaded)
        {
            progressFill.BeginAnimation(WidthProperty, null);
            progressFill.Width = targetWidth;
            return;
        }

        progressFill.BeginAnimation(WidthProperty, new DoubleAnimation
        {
            To = targetWidth,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private void SetStatus(string value)
    {
        if (string.Equals(status.Text, value, StringComparison.Ordinal)) return;
        status.Text = L.T(value);
        AnimateStateChange(status);
    }

    private static void AnimateEntrance(FrameworkElement element)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var transform = new TranslateTransform(0, 6);
        element.RenderTransform = transform;
        element.Opacity = 0;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private static void AnimateStateChange(FrameworkElement element)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0.55, 1, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private static TextBlock Text(string value, double size, FontWeight weight, Brush color, Thickness? margin = null)
        => new() { Text = L.T(value), FontSize = size, FontWeight = weight, Foreground = color, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0), VerticalAlignment = VerticalAlignment.Center };

    private static Button Button(string label, bool accent, Action action, double width)
    {
        var button = new Button
        {
            Content = L.T(label), Width = width, Height = 40, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = accent ? Brushes.White : TextBrush,
            Background = accent ? AccentBrush : CardBrush,
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand
        };
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "ButtonSurface";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(System.Windows.Controls.Button.Background)) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.HorizontalAlignmentProperty, new System.Windows.Data.Binding(nameof(System.Windows.Controls.Button.HorizontalContentAlignment)) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var focus = new Trigger { Property = IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, AccentHoverBrush, "ButtonSurface"));
        template.Triggers.Add(focus);
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(OpacityProperty, 0.45));
        template.Triggers.Add(disabled);
        button.Template = template;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.Click += (_, _) => action();
        void Transition(Brush target)
        {
            var color = ((SolidColorBrush)target).Color;
            var current = button.Background as SolidColorBrush;
            var animated = new SolidColorBrush(current?.Color ?? color);
            button.Background = animated;
            if (!SystemParameters.ClientAreaAnimation) animated.Color = color;
            else animated.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, TimeSpan.FromMilliseconds(140)));
        }
        button.MouseEnter += (_, _) => { if (button.IsEnabled) Transition(accent ? AccentHoverBrush : Brush("#303845")); };
        button.MouseLeave += (_, _) => Transition(accent ? AccentBrush : CardBrush);
        return button;
    }
    private static Brush Brush(string value) => new SolidColorBrush(Color(value));
    private static Color Color(string value) => (Color)ColorConverter.ConvertFromString(value);
}

internal sealed class BackgroundUpdateWindow : Window
{
    private TextBlock status = null!;
    private readonly Border fill;
    private readonly Border track;
    private int currentPercent;

    internal IProgress<(int, string)> Progress { get; }

    internal BackgroundUpdateWindow()
    {
        Width = 420;
        Height = 112;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        IsHitTestVisible = false;
        SourceInitialized += (_, _) =>
        {
            nint handle = new WindowInteropHelper(this).Handle;
            // Continue the app notification's privacy and non-activating, click-through behavior.
            long styles = GetWindowLongPtr(handle, -20).ToInt64();
            SetWindowLongPtr(handle, -20, new nint(styles | 0x08000000 | 0x80 | 0x20));
            SetWindowDisplayAffinity(handle, ReadNotificationHidden() ? 0x11u : 0u);
        };
        var surface = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D222A")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#343B46")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18, 14, 18, 14)
        };
        var stack = new StackPanel();
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/DesktopTools.Installer;component/Assets/Icons/AppIcon.png")),
            Width = 22,
            Height = 22,
            Margin = new Thickness(0, 0, 9, 0)
        });
        title.Children.Add(new TextBlock { Text = "Updating DesktopTools", Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(title);
        status = new TextBlock { Text = "Preparing installation…", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A8B0BC")), FontSize = 11, Margin = new Thickness(0, 5, 0, 10) };
        stack.Children.Add(status);
        track = new Border { Height = 5, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#303640")), CornerRadius = new CornerRadius(3), ClipToBounds = true };
        fill = new Border { Width = 0, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3478F6")), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
        track.Child = fill;
        stack.Children.Add(track);
        surface.Child = stack;
        Content = surface;
        Loaded += (_, _) =>
        {
            DesktopTools.Presentation.WindowDismissal.Attach(this, () => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast);
            Rect work = SystemParameters.WorkArea;
            Left = work.Left + (work.Width - Width) / 2;
            Top = work.Top + 18;
        };
        track.SizeChanged += (_, _) => UpdateProgress(currentPercent, status.Text);
        Progress = new DispatcherProgress(this);
    }

    private void UpdateProgress(int percent, string text)
    {
        currentPercent = Math.Clamp(percent, 0, 100);
        status.Text = text;
        fill.Width = Math.Max(0, track.ActualWidth * currentPercent / 100d);
    }

    private static bool ReadNotificationHidden()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTools", "settings.json");
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length <= 1024 * 1024)
                return NotificationHiddenFromJson(File.ReadAllText(path));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return true;
    }

    internal static bool NotificationHiddenFromJson(string json)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<NotificationPrivacy>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (settings?.CaptureVisibilityOverrides?.TryGetValue("Notifications", out bool hidden) == true) return hidden;
            return settings?.VisibleCaptureFeatures?.Contains("Notifications") != true;
        }
        catch (JsonException) { return true; }
    }

    private sealed class NotificationPrivacy
    {
        public Dictionary<string, bool>? CaptureVisibilityOverrides { get; set; }
        public string[]? VisibleCaptureFeatures { get; set; }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);

    private sealed class DispatcherProgress(BackgroundUpdateWindow owner) : IProgress<(int, string)>
    {
        public void Report((int, string) value) => owner.Dispatcher.BeginInvoke(() => owner.UpdateProgress(value.Item1, value.Item2));
    }
}
