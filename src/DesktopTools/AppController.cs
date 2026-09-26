using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Win32;
using DesktopTools.Core;
using DesktopTools.Native;
using DesktopTools.UI;
using DesktopTools.Extras;
using Forms = System.Windows.Forms;

namespace DesktopTools;

internal sealed partial class AppController : IDisposable
{
    private readonly SettingsStore store;
    private readonly HotkeyService hotkeys = new();
    private IReadOnlyDictionary<string, string> unavailableShortcuts = new Dictionary<string, string>();
    internal IReadOnlyDictionary<string, string> UnavailableShortcuts => unavailableShortcuts;
    internal IReadOnlyDictionary<string, string> RegisteredShortcuts => hotkeys.RegisteredHotkeys;
    private readonly EscapeKeyService escape = new();
    private QuickWheelWindow? quickWheel;
    public void OpenQuickWheel(bool held = false)
    {
        if (!Settings.QuickWheelEnabled || IsBusy || quickWheel != null) return;
        uint? key = held && HotkeyGesture.TryParse(Settings.QuickWheelShortcut, out var gesture, out _) ? gesture.VirtualKey : null;
        nint previous = NativeWindowService.GetForegroundWindowHandle();
        bool Available(string action) => FeatureAvailability.IsWheelAvailable(Settings, action);
        quickWheel = new QuickWheelWindow(Settings.QuickWheelItems, Available, action =>
        {
            quickWheel = null; UpdateEscape();
            if (disposed || action == null) return;
            NativeWindowService.RestoreForeground(previous);
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (disposed || !Available(action)) return;
                switch(action)
                {
                    case "Draw": ToggleDraw(); break; case "Capture": _ = CaptureAsync(); break;
                    case "Laser": TogglePresentation("Laser"); break; case "Spotlight": TogglePresentation("Spotlight"); break; case "Freeze": _ = FreezeAsync(); break;
                    case "File shelf": OpenFileShelf(); break; case "Notes": OpenFloatingNotes(); break; case "Audio": OpenAudioControls(); break;
                    case "Text tools": OpenTextToolsWindow(); break; case "Scan screen text": _ = CaptureAsync(textOnly: true); break; case "Screen recorder": OpenScreenRecorder(); break; case "Video editor": OpenVideoEditor(); break; case "QR codes": OpenQrCodes(); break; case "Image tools": OpenImageTools(); break; case "Eyedropper": _ = PickScreenColorAsync(); break; case "Teleprompter": OpenTeleprompter(); break;
                }
            });
        }, key);
        quickWheel.Show(); quickWheel.Activate(); UpdateEscape();
    }
    private bool scanningText;
    private bool pickingColor;
    private readonly WindowPinService windowPins = new();
    private readonly Dictionary<string, Window> utilityWindows = new();
    private void OpenUtility(string key, Func<Window> create)
    {
        if (!utilityWindows.TryGetValue(key, out var window)) { window = create(); utilityWindows[key] = window; window.Closed += (_, _) => utilityWindows.Remove(key); }
        NativeWindowService.ShowForeground(window);
    }
    public void OpenTeleprompter() { if (Settings.TeleprompterEnabled) OpenUtility("Teleprompter", () => new TeleprompterWindow(Settings, UpdateSettings)); }
    private void ToggleTeleprompter()
    {
        if (!Settings.TeleprompterEnabled) return;
        if (utilityWindows.TryGetValue("Teleprompter", out var window)) ((TeleprompterWindow)window).TogglePlayback(); else OpenTeleprompter();
    }
    public void OpenQrCodes() { if (Settings.QrCodesEnabled) OpenUtility("QR", () => new QrCodeWindow(Report)); }
    public void OpenScreenRecorder()
        => OpenScreenRecorder(null);
    internal void OpenScreenRecorder(Func<Window, string?>? chooseOutput)
    {
        if (!Settings.ScreenRecorderEnabled) return;
        try { OpenUtility("Recorder", () => new ScreenRecorderWindow(this, chooseOutput)); }
        catch (Exception ex) { Report(L.T("Recorder unavailable. Install Microsoft Visual C++ x64 Runtime and the Windows Media Feature Pack if required. ") + ex.Message); }
    }
    private async Task QuitAsync()
    {
        if (utilityWindows.TryGetValue("Recorder", out var window)) await ((ScreenRecorderWindow)window).StopAsync();
        Application.Current.Shutdown();
    }
    public void OpenVideoEditor() { if (Settings.VideoEditorEnabled) OpenUtility("Video", () => new VideoEditorWindow(Report)); }
    internal TextToolsWindow? OpenTextTools()
    {
        if (!Settings.TranslationEnabled && !Settings.ScreenTextEnabled) return null;
        OpenUtility("Text", () => new TextToolsWindow(this)); return (TextToolsWindow)utilityWindows["Text"];
    }
    private int textRequestVersion;
    public void OpenTextToolsWindow() { textRequestVersion++; OpenTextTools(); }
    public async Task TranslateSelectionAsync()
    {
        if (IsBusy || !Settings.TranslationEnabled) return;
        int version = ++textRequestVersion;
        nint foreground = NativeWindowService.GetForegroundWindowHandle();
        string? text = await SelectedTextService.ReadAsync(foreground);
        if (!disposed && !IsBusy && version == textRequestVersion && Settings.TranslationEnabled) OpenTextTools()?.SetSource(text ?? "");
    }
    public void OpenImageTools() { if (Settings.ImageToolsEnabled) OpenUtility("Images", () => new ImageToolsWindow(Report)); }
    public void ToggleWindowPin()
    {
        if (!Settings.WindowPinEnabled) return;
        try { Report(windowPins.ToggleForeground() ? "Window pinned above other windows." : "Window unpinned."); }
        catch (Exception ex) { Report(L.T("Could not pin window: ") + ex.Message); }
    }
    public async Task PickScreenColorAsync()
    {
        if (IsBusy || !Settings.EyedropperEnabled) return;
        IsBusy = true; pickingColor = true; using var cancellation = new CancellationTokenSource(); captureCancellation = cancellation; UpdateEscape();
        try { var color = await ScreenEyedropperWindow.PickSampleAsync(cancellation.Token); if (color != null && !disposed) { if (utilityWindows.TryGetValue("Sample", out var previous)) previous.Close(); OpenUtility("Sample", () => new SampledColorWindow(color.Color, Report, color.Pixels, () => _ = PickScreenColorAsync())); } }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Report(L.T("Could not sample screen color: ") + ex.Message); }
        finally { captureCancellation = null; pickingColor = false; IsBusy = false; UpdateEscape(); }
    }
    private Window? controlDialog;
    private FileShelfWindow? fileShelf;
    private FloatingNotesService? floatingNotes;
    private AudioControlsWindow? audioControls;
    public void OpenFileShelf()
    {
        if (!Settings.FileShelfEnabled) return;
        fileShelf ??= new FileShelfWindow(Report);
        fileShelf.Topmost = Settings.FileShelfTopmost; NativeWindowService.ShowForeground(fileShelf);
    }
    public void OpenFloatingNotes()
    {
        if (!Settings.FloatingNotesEnabled) return;
        floatingNotes ??= new FloatingNotesService(smoke ? Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke-notes") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTools", "notes"), Report);
        floatingNotes.DefaultTopmost = Settings.NotesTopmost; floatingNotes.Show();
    }
    public void OpenAudioControls()
    {
        if (!Settings.AudioControlsEnabled) return;
        if (audioControls == null) { audioControls = new AudioControlsWindow(Report); audioControls.Closed += (_, _) => audioControls = null; }
        NativeWindowService.ShowForeground(audioControls);
    }
    private readonly OverlayStateMachine machine = new();
    private readonly Forms.NotifyIcon? tray;
    private readonly System.Drawing.Icon? trayIcon;
    private TrayMenuPopup? trayMenu;
    private readonly bool smoke;
    private MainWindow? main;
    private OverlayWindow? overlay;
    private Window? captureNotice;
    private Window? statusNotice;
    private SelectionWindow? selector;
    private PresentationSession? presentation;
    private Window? presentationControls;
    private string? presentationMode;
    private BitmapSource? frozenFrame;
    private nint previousForeground;
    private bool disposed;
    private CancellationTokenSource? captureCancellation;
    private (MonitorInfo Monitor, Rect Region)? lastRegion;
    public CaptureHistory CaptureHistory { get; } = new();
    public PresentationHudService Hud { get; }
    public bool CanRepeatCapture => lastRegion.HasValue;
    public AppSettings Settings { get; private set; }
    public AnnotationDocument Document { get; } = new();
    public string Tool { get; private set; }
    public string? ActiveProfile { get; private set; }
    public PaletteWindow? Palette { get; private set; }
    public OverlayState State => machine.State;
    public bool IsBusy { get; private set; }
    public BitmapSource? LastCapture { get; private set; }
    public event Action? SettingsChanged;
    public event Action<string>? StatusChanged;
    public string Status { get; private set; } = "";
    public bool Dark => Settings.Theme == "Dark" || (Settings.Theme == "System" && !SystemUsesLight());
    public ProfileStore Profiles { get; } = new();
    public AppController(bool smoke)
    {
        this.smoke = smoke; AppCapturePrivacy.Initialize();
        store = new SettingsStore(smoke ? Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke-settings") : null);
        Settings = store.Load(); L.Use(Settings.Language); Tool = Settings.DefaultTool;
        Hud = new PresentationHudService(Report, () => Settings.HideControlsFromCapture)
        { IsAvailable = id => FeatureAvailability.IsAvailable(Settings, id) };
        Hud.Changed += UpdateEscape;
        ApplyHudSettings();
        escape.Pressed += CancelActiveTool;
        ApplyTheme();
        hotkeys.Pressed += HandleHotkey;
        if (!smoke)
        {
            unavailableShortcuts = hotkeys.RegisterAvailable(Bindings(Settings));
            if (unavailableShortcuts.Count > 0)
                Report(L.T("A shortcut is unavailable. Open Shortcuts to choose another binding."), NotificationKind.Warning);
        }
        if (store.RecoveryMessage != null) Report(store.RecoveryMessage);
        if (!smoke)
        {
            trayIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
            tray = new Forms.NotifyIcon { Icon = trayIcon ?? System.Drawing.SystemIcons.Application, Text = "DesktopTools", Visible = true };
            tray.MouseClick += (_, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left) { trayMenu?.SetCurrentValue(System.Windows.Controls.Primitives.Popup.IsOpenProperty, false); OpenMain(); }
                else if (e.Button == Forms.MouseButtons.Right)
                {
                    trayMenu ??= new TrayMenuPopup(OpenMain, OpenScreenRecorder, PinLast, () => OpenQuickWheel(), () => _ = QuitAsync());
                    trayMenu.IsOpen = !trayMenu.IsOpen;
                }
            };
            tray.BalloonTipClicked += (_, _) => OpenMain();
            if (Settings.StartAtLogin && store.RecoveryMessage == null)
            {
                try { StartupService.SetEnabled(true); } catch (Exception ex) { Report(ex.Message, NotificationKind.Error); }
            }
            SystemEvents.DisplaySettingsChanged += DisplayChanged;
            SystemEvents.UserPreferenceChanged += PreferenceChanged;
        }
    }
    private static Dictionary<string, string> Bindings(AppSettings s) => FeatureShortcutCatalog.Bindings(s);
    internal void RetryShortcuts()
    {
        if (smoke || disposed) return;
        unavailableShortcuts = hotkeys.RegisterAvailable(Bindings(Settings));
        Report(L.T(unavailableShortcuts.Count == 0
            ? "All enabled shortcuts are ready."
            : "A shortcut is unavailable. Open Shortcuts to choose another binding."),
            unavailableShortcuts.Count == 0 ? NotificationKind.Info : NotificationKind.Warning);
        SettingsChanged?.Invoke();
    }
    private void HandleHotkey(string action)
    {
        if (IsBusy || main?.HasModal == true) return;
        switch (action) { case "Translate": _ = TranslateSelectionAsync(); break; case "ScreenText": _ = CaptureAsync(textOnly: true); break; case "QuickWheel": OpenQuickWheel(true); break; case "Teleprompter": ToggleTeleprompter(); break; case "PinWindow": ToggleWindowPin(); break; case "Eyedropper": _ = PickScreenColorAsync(); break; case "Draw": ToggleDraw(); break; case "Capture": _ = CaptureAsync(); break; case "HidePalette": TogglePalette(); break; case "Laser": TogglePresentation("Laser"); break; case "Spotlight": TogglePresentation("Spotlight"); break; case "Freeze": _ = FreezeAsync(); break; default: HandleFeatureHotkey(action); break; }
    }
    public bool UpdateSettings(Action<AppSettings> update)
    {
        var candidate = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(Settings))!; update(candidate);
        return ReplaceSettings(candidate);
    }
    private bool ReplaceSettings(AppSettings candidate)
    {
        try
        {
            // Legacy availability fields are retained for profile compatibility only.
            // Normalize before validation and side effects so old profiles cannot
            // accidentally close a tool or suppress a shortcut conflict.
            foreach (var feature in FeatureAvailability.All) feature.Write(candidate, true);
            if (!candidate.FloatingNotesEnabled && floatingNotes != null && !floatingNotes.TryFlush()) return false;
            foreach (var (key, enabled) in new[] { ("Images", candidate.ImageToolsEnabled), ("Video", candidate.VideoEditorEnabled) })
                if (!enabled && utilityWindows.TryGetValue(key, out var editor) && !SaveBeforeDisableDialog.Prepare(editor)) return false;
            if (!DrawingBindings.Validate(candidate.DrawingShortcuts, out var drawingError)) { Report(drawingError!, NotificationKind.Warning); return false; }
            foreach (var binding in Bindings(candidate).Values)
                if (DrawingBindings.TryParse(binding, out var key, out var modifiers, out _) && DrawingBindings.Find(candidate.DrawingShortcuts, key, modifiers) is { } action)
                { Report(L.F($"Shortcut conflict: {binding} is already used by drawing action {action}."), NotificationKind.Warning); return false; }
            // Validate the requested binding before load-time repair can clear a conflict.
            if (!FeatureShortcutCatalog.TryValidate(candidate, out var requestedShortcutError)) { Report(requestedShortcutError!, NotificationKind.Warning); return false; }
            SettingsStore.Validate(candidate);
            if (!FeatureShortcutCatalog.TryValidate(candidate, out var shortcutError)) { Report(shortcutError!, NotificationKind.Warning); return false; }
            var oldBindings = Bindings(Settings); var newBindings = Bindings(candidate);
            bool bindingsChanged = oldBindings.Count != newBindings.Count || oldBindings.Any(pair => !newBindings.TryGetValue(pair.Key, out var value) || value != pair.Value);
            var previousFailures = unavailableShortcuts;
            IReadOnlyDictionary<string, string>? nextFailures = null;
            if (!smoke && bindingsChanged)
            {
                if (previousFailures.Count == 0)
                {
                    if (!hotkeys.TryReplace(newBindings, out string? error)) { Report(error ?? L.T("Shortcut conflict. Previous bindings kept.")); return false; }
                    nextFailures = new Dictionary<string, string>();
                }
                else
                {
                    nextFailures = hotkeys.RegisterAvailable(newBindings);
                    string? newlyFailed = nextFailures.Keys.FirstOrDefault(action =>
                        !previousFailures.ContainsKey(action) ||
                        !oldBindings.TryGetValue(action, out string? previous) ||
                        !newBindings.TryGetValue(action, out string? current) || previous != current);
                    if (newlyFailed is not null)
                    {
                        unavailableShortcuts = hotkeys.RegisterAvailable(oldBindings);
                        Report(L.F($"{newBindings[newlyFailed]}: shortcut is unavailable. {nextFailures[newlyFailed]} Your previous shortcuts are unchanged."), NotificationKind.Warning);
                        return false;
                    }
                }
            }
            try
            {
                if (!smoke && candidate.StartAtLogin != Settings.StartAtLogin) StartupService.SetEnabled(candidate.StartAtLogin);
                if (!candidate.WindowPinEnabled && Settings.WindowPinEnabled) windowPins.RestoreAll();
                store.Save(candidate);
            }
            catch
            {
                if (!smoke && bindingsChanged)
                {
                    if (previousFailures.Count == 0) hotkeys.TryReplace(oldBindings, out _);
                    else unavailableShortcuts = hotkeys.RegisterAvailable(oldBindings);
                }
                if (!smoke && candidate.StartAtLogin != Settings.StartAtLogin) StartupService.SetEnabled(Settings.StartAtLogin);
                throw;
            }
            if (nextFailures is not null) unavailableShortcuts = nextFailures;
            bool textFeatureDisabled = (Settings.TranslationEnabled && !candidate.TranslationEnabled) || (Settings.ScreenTextEnabled && !candidate.ScreenTextEnabled);
            bool monitorChanged = candidate.MonitorMode != Settings.MonitorMode;
            bool themeChanged = candidate.Theme != Settings.Theme || candidate.Transparency != Settings.Transparency || candidate.ThemePreset != Settings.ThemePreset || candidate.PrimaryColor != Settings.PrimaryColor || candidate.BackgroundColor != Settings.BackgroundColor || candidate.UseCustomBackground != Settings.UseCustomBackground || candidate.Animations != Settings.Animations;
            bool exclusionChanged = candidate.HideControlsFromCapture != Settings.HideControlsFromCapture || candidate.HideMainWindowFromCapture != Settings.HideMainWindowFromCapture || !candidate.VisibleCaptureFeatures.SequenceEqual(Settings.VisibleCaptureFeatures) || !candidate.HiddenCaptureTools.SequenceEqual(Settings.HiddenCaptureTools) || !candidate.CaptureVisibilityOverrides.OrderBy(p => p.Key).SequenceEqual(Settings.CaptureVisibilityOverrides.OrderBy(p => p.Key));
            bool presentationChanged = candidate.LaserColor != Settings.LaserColor || candidate.LaserSize != Settings.LaserSize || candidate.LaserFadeSeconds != Settings.LaserFadeSeconds || candidate.SpotlightRadius != Settings.SpotlightRadius || candidate.SpotlightDim != Settings.SpotlightDim || candidate.LaserMonitorMode != Settings.LaserMonitorMode || candidate.SpotlightMonitorMode != Settings.SpotlightMonitorMode;
            Settings = candidate;
            if (!Settings.QuickWheelEnabled) quickWheel?.Close();
            if (!Settings.EyedropperEnabled && pickingColor) captureCancellation?.Cancel();
            if (!Settings.TeleprompterEnabled && utilityWindows.Remove("Teleprompter", out var prompter)) prompter.Close();
            if (!Settings.QrCodesEnabled && utilityWindows.TryGetValue("QR", out var qr)) qr.Close();
            if (!Settings.ScreenRecorderEnabled && utilityWindows.TryGetValue("Recorder", out var recorderWindow)) recorderWindow.Close();
            if (!Settings.VideoEditorEnabled && utilityWindows.TryGetValue("Video", out var videoTool)) videoTool.Close();
            if (!Settings.ImageToolsEnabled && utilityWindows.TryGetValue("Images", out var imageTool)) imageTool.Close();
            if (!Settings.FileShelfEnabled) { fileShelf?.Shutdown(); fileShelf = null; }
            else if (fileShelf != null) fileShelf.Topmost = Settings.FileShelfTopmost;
            if (!Settings.FloatingNotesEnabled) { floatingNotes?.Dispose(); floatingNotes = null; }
            if (!Settings.AudioControlsEnabled) audioControls?.Close();
            ApplyHudSettings(); if (themeChanged) ApplyTheme();
            if (exclusionChanged) { Palette?.ApplyExclusion(); foreach (Window window in Application.Current.Windows) AppCapturePrivacy.Apply(window, Settings); }
            if (!Settings.DrawingEnabled || monitorChanged) RemoveOverlay();
            if (monitorChanged) StopPresentation();
            if ((!Settings.CaptureEnabled && !scanningText) || (!Settings.ScreenTextEnabled && scanningText)) { captureCancellation?.Cancel(); selector?.Close(); }
            if (textFeatureDisabled && utilityWindows.TryGetValue("Text", out var textTool)) textTool.Close();
            if ((!Settings.LaserEnabled && presentationMode == "Laser") || (!Settings.SpotlightEnabled && presentationMode == "Spotlight")) StopPresentation();
            else if (presentationChanged && presentation != null && presentationMode != null)
            {
                string mode = presentationMode; StopPresentation(); TogglePresentation(mode);
            }
            if (!Settings.FreezeEnabled && frozenFrame != null) { frozenFrame = null; overlay?.SetFrozen(null); }
            SettingsChanged?.Invoke();
            if (Palette != null) Palette.Refresh(); return true;
        }
        catch (Exception ex) { Report(L.T("Settings could not be saved: ") + ex.Message); return false; }
    }
    public void ApplyProfile(string name)
    {
        try { if (ReplaceSettings(ProfileStore.ApplyTo(Settings, Profiles.Load(name)))) { ActiveProfile = name; SetTool(Settings.DefaultTool); Report(L.F($"Applied {name} profile.")); } } catch (Exception ex) { Report(ex.Message, NotificationKind.Error); }
    }
    private void ApplyHudSettings()
    {
        Hud.ShortcutDisplaySeconds = Settings.ShortcutDisplaySeconds; Hud.ShortcutAnimations = Settings.ShortcutAnimations;
        Hud.ClickColor = Settings.ClickColor; Hud.ClickSize = Settings.ClickSize; Hud.ClickDurationMilliseconds = Settings.ClickDurationMilliseconds;
        Hud.CountdownMinutes = Settings.CountdownMinutes; Hud.RulerLength = Settings.RulerLength;
        Hud.BlackoutSharingOnly = Settings.BlackoutSharingOnly;
        Hud.ApplyAvailability();
    }
    public void RecordShortcut(string current, Action<AppSettings, string> set, bool drawing = false)
    {
        bool wasBusy = IsBusy; IsBusy = true;
        escape.SetEnabled(false);
        hotkeys.DispatchSuspended = true;
        try
        {
            var dialog = new ShortcutRecorderWindow(current, value =>
            {
                if (drawing)
                {
                    var candidate = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(Settings))!;
                    set(candidate, value);
                    if (!DrawingBindings.Validate(candidate.DrawingShortcuts, out var validationError)) { Report(validationError!, NotificationKind.Warning); return false; }
                }
                bool saved = UpdateSettings(s => set(s, value));
                return saved;
            }, () => Status, drawing, value =>
            {
                var candidate = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(Settings))!;
                set(candidate, value);
                if (!FeatureShortcutCatalog.TryValidate(candidate, out var catalogError)) return catalogError;
                if (!DrawingBindings.Validate(candidate.DrawingShortcuts, out var error)) return error;
                foreach (var binding in Bindings(candidate).Values)
                    if (DrawingBindings.TryParse(binding, out var key, out var modifiers, out _) && DrawingBindings.Find(candidate.DrawingShortcuts, key, modifiers) is { } action)
                        return L.F($"Shortcut conflict: {binding} is already used by drawing action {action}.");
                // An existing unavailable binding must not prevent changing a different one.
                // ReplaceSettings checks newly requested bindings when the user saves.
                return smoke || unavailableShortcuts.Count > 0 || hotkeys.TryValidate(Bindings(candidate), out error) ? null : error;
            }) { Owner = main };
            dialog.SourceInitialized += (_, _) => NativeWindowService.TryExcludeFromCapture(dialog, Settings.HideControlsFromCapture, out _);
            if (main == null) OpenMain();
            dialog.Embedded = true;
            main!.ShowEmbeddedDialog(dialog, wait: true);
        }
        finally
        {
            if (!smoke)
            {
                if (unavailableShortcuts.Count > 0) unavailableShortcuts = hotkeys.RegisterAvailable(Bindings(Settings));
                else if (!hotkeys.TryReplace(Bindings(Settings), out var error))
                {
                    unavailableShortcuts = hotkeys.RegisterAvailable(Bindings(Settings));
                    Report(error ?? L.T("Could not restore shortcuts."));
                }
            }
            hotkeys.DispatchSuspended = false;
            IsBusy = wasBusy; UpdateEscape();
        }
    }
    public void OpenMain()
    {
        if (State == OverlayState.Draw) Interact(false);
        NativeWindowService.ShowForeground(EnsureMainWindow());
        if (!smoke && !Settings.SharingWelcomeSeen)
            Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, OpenSharingWelcome);
    }
    private MainWindow EnsureMainWindow()
    {
        if (main == null) { main = new MainWindow(this); main.Closed += (_, _) => main = null; }
        return main;
    }
    public void RecordDrawingShortcut(string action) => RecordShortcut(Settings.DrawingShortcuts[action], (s, value) => s.DrawingShortcuts[action] = value, true);
    internal void ShowFirstTrayHint()
    {
        if (disposed || Settings.TrayHintShown) return;
        if (tray != null)
        {
            tray.BalloonTipTitle = L.T("DesktopTools is still open");
            tray.BalloonTipText = L.T("DesktopTools is in the system tray. Click this notification or the tray icon to open it again.");
            tray.BalloonTipIcon = Forms.ToolTipIcon.Info;
            try { tray.ShowBalloonTip(8000); }
            catch (InvalidOperationException) { return; }
        }
        UpdateSettings(s => s.TrayHintShown = true);
    }
    public void Report(string message) => Report(message, ClassifyNotification(message));
    internal static NotificationKind ClassifyNotification(string message)
    {
        string text = L.EnglishHint(message).ToLowerInvariant();
        if (new[] { "failed", "could not", "cannot", "error", "access denied" }.Any(text.Contains)) return NotificationKind.Error;
        if (new[] { "warning:", "conflict", "unavailable", "busy", "recovered", "already exists", "capture a screenshot first" }.Any(text.Contains)) return NotificationKind.Warning;
        return NotificationKind.Info;
    }
    public void Report(string message, NotificationKind kind, double? seconds = null)
    {
        Status = message; StatusChanged?.Invoke(message);
        if (smoke || string.IsNullOrWhiteSpace(message) || message == L.T("Screenshot copied to clipboard.")) return;
        statusNotice?.Close();
        var notice = new NotificationWindow(message, kind, main, Settings.MessageNotificationStyle, seconds: seconds ?? Settings.NotificationSeconds);
        notice.Closed += (_, _) => { if (statusNotice == notice) statusNotice = null; };
        statusNotice = notice; notice.Show();
    }
    public void DuplicateSelected() => overlay?.DuplicateSelected();
    public void SetTool(string tool) { overlay?.CommitText(); Tool = tool; Palette?.Refresh(); }
    public void PickColor() => PickColorValue(false);
    public void PickLaserColor() => PickColorValue(true);
    private void PickColorValue(bool laser)
    {
        var dialog = new ColorPickerWindow(laser ? Settings.LaserColor : Settings.Color, value => UpdateSettings(s => { if (laser) s.LaserColor = value; else s.Color = value; })) { Owner = Palette?.IsVisible == true ? Palette : main, Tag = "Drawing palette" };
        ShowControlDialog(dialog);
    }
    public void ShowControlDialog(Window window)
    {
        bool wasBusy = IsBusy; IsBusy = true; controlDialog = window;
        if (Palette?.IsVisible == true) { window.Owner ??= Palette; window.Topmost = true; }
        UpdateEscape();
        PrepareControlSurface(window);
        window.Loaded += (_, _) => { if (window.Content is FrameworkElement content) Motion.Reveal(content); };
        window.SourceInitialized += (_, _) => { NativeWindowService.TryExcludeFromCapture(window, Settings.HideControlsFromCapture, out _); NativeWindowService.ApplyBackdrop(window, Dark, Settings.Transparency); };
        window.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { window.Close(); e.Handled = true; } };
        try { window.ShowDialog(); } finally { controlDialog = null; IsBusy = wasBusy; if (State == OverlayState.Draw) { overlay?.Activate(); Palette?.Show(); } UpdateEscape(); }
    }
    private void EnsureOverlay(MonitorInfo? selectedMonitor = null)
    {
        var monitor = selectedMonitor ?? MonitorService.GetCurrent(Settings.MonitorMode == "Primary");
        if (SameMonitor(overlay?.Monitor, monitor)) return;
        if (overlay != null) RemoveOverlay();
        overlay = new OverlayWindow(this, monitor); overlay.Show(); Palette = new PaletteWindow(this, monitor) { Owner = overlay };
        overlay.IsVisibleChanged += (_, _) => UpdateEscape();
    }
    // Geometry and scale are part of session identity, not just the display device name.
    internal static bool SameMonitor(MonitorInfo? session, MonitorInfo target) => session == target;
    public void ToggleDraw()
    {
        if (!Settings.DrawingEnabled || IsBusy) return;
        try
        {
            var monitor = MonitorService.GetCurrent(Settings.MonitorMode == "Primary");
            if (State == OverlayState.Draw && SameMonitor(overlay?.Monitor, monitor)) { Interact(); return; }
            if (State != OverlayState.Draw) previousForeground = NativeWindowService.GetForegroundWindowHandle();
            StopPresentation();
            EnsureOverlay(monitor); machine.ToggleDraw(); overlay!.Show(); overlay.Mode(true); ShowPalette(); Palette!.Refresh(); UpdateEscape();
        }
        catch (Exception ex) { RemoveOverlay(); Report(L.T("Could not start drawing: ") + ex.Message); }
    }
    public void Interact(bool restoreFocus = true)
    {
        if (State != OverlayState.Draw) return;
        overlay?.CommitText(); overlay?.CancelPending(); machine.ToggleDraw(); Palette?.Hide(); overlay?.Mode(false); if (restoreFocus) NativeWindowService.RestoreForeground(previousForeground); UpdateEscape();
    }
    public void CheckDrawingFocus() => Application.Current.Dispatcher.BeginInvoke(() =>
    {
        if (State != OverlayState.Draw || overlay?.IsActive == true || Palette?.IsActive == true || IsBusy) return;
        if (overlay != null && !SameMonitor(overlay.Monitor, MonitorService.GetCurrent())) return;
        Interact(false);
    });
    public void HideAnnotations() => HideAnnotations(animateControls: false);
    internal void HideAnnotations(bool animateControls)
    {
        frozenFrame = null; overlay?.SetFrozen(null);
        bool ownedFocus = State == OverlayState.Draw && (overlay?.IsActive == true || Palette?.IsActive == true);
        selector?.Close(); overlay?.CancelPending(); machine.Hide();
        if (Palette is { } palette && animateControls) DesktopTools.Presentation.WindowDismissal.Hide(palette, () => Motion.Enabled);
        else Palette?.Hide();
        overlay?.Hide(); if (ownedFocus) NativeWindowService.RestoreForeground(previousForeground); UpdateEscape();
    }
    internal void CancelActiveTool()
    {
        if (quickWheel != null) { quickWheel.Close(); return; }
        if (controlDialog != null) { controlDialog.Close(); return; }
        if (Hud.IsBlackoutVisible) { Hud.ToggleBlackout(); return; }
        if (captureCancellation != null) { captureCancellation.Cancel(); selector?.Close(); return; }
        if (selector != null) { selector.Close(); return; }
        if (overlay?.HasPending == true) { overlay.CancelPending(); return; }
        StopPresentation(); HideAnnotations(animateControls: true);
    }
    internal void RefreshModalInput() => UpdateEscape();
    private void UpdateEscape()
    {
        if (!smoke && !disposed) escape.SetEnabled(main?.HasModal != true && (quickWheel != null || controlDialog != null || IsBusy || presentation != null || Hud.IsBlackoutVisible || State is OverlayState.Draw or OverlayState.Interact));
    }
    public void ChooseMonitor()
    {
        var monitors = MonitorService.GetAll(); var panel = new StackPanel();
        var choice = new ComboBox { ItemsSource = monitors, DisplayMemberPath = "Id", SelectedItem = monitors.FirstOrDefault(m => m.Id == overlay?.Monitor.Id) ?? monitors.First(), MinWidth = 260 };
        panel.Children.Add(Ui.Row(L.T("Drawing monitor"), L.T("Switching starts a fresh annotation session."), choice));
        var dialog = new Window { Title = L.T("Choose monitor"), Content = Ui.Card(panel), SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, Owner = Palette, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        MonitorInfo? selected = null; panel.Children.Add(Ui.Button(L.T("Start new session"), () => { selected = choice.SelectedItem as MonitorInfo; dialog.Close(); }, true)); ShowControlDialog(dialog);
        if (selected == null) return; RemoveOverlay(); EnsureOverlay(selected); machine.ToggleDraw(); overlay!.Mode(true); ShowPalette(); Palette!.Refresh();
    }
    private void RemoveOverlay()
    {
        selector?.Close(); Palette?.Close(); Palette = null; overlay?.Close(); overlay = null; frozenFrame = null; machine.Disable(); Document.Reset();
    }
    private void ShowPalette()
    {
        if (Palette is not { } palette) return;
        DesktopTools.Presentation.WindowDismissal.Cancel(palette); palette.Show();
    }
    public void TogglePalette()
    {
        if (State != OverlayState.Draw || Palette == null) return;
        if (Palette.IsVisible && !DesktopTools.Presentation.WindowDismissal.IsDismissing(Palette))
        { overlay?.Activate(); DesktopTools.Presentation.WindowDismissal.Hide(Palette, () => Motion.Enabled); }
        else ShowPalette();
    }
    public void ForwardKey(object sender, System.Windows.Input.KeyEventArgs e) => overlay?.KeyPressed(sender, e);
    public async Task CaptureAsync(bool repeat = false, bool textOnly = false)
    {
        if (IsBusy || (textOnly ? !Settings.ScreenTextEnabled : !Settings.CaptureEnabled)) return;
        BitmapSource? textImage = null; scanningText = textOnly; if (textOnly) textRequestVersion++;
        nint captureForeground = State == OverlayState.Draw ? previousForeground : NativeWindowService.GetForegroundWindowHandle();
        IsBusy = true; using var cancellation = new CancellationTokenSource(); captureCancellation = cancellation;
        overlay?.CommitText(); overlay?.CancelPending(); StopPresentation(); machine.BeginCapture(); Palette?.Hide(); UpdateEscape();
        try
        {
            var monitor = Settings.CaptureMonitorMode == "All" ? MonitorService.GetVirtualDesktop() : MonitorService.GetCurrent(Settings.MonitorMode == "Primary");
            bool sameSession = SameMonitor(overlay?.Monitor, monitor);
            BitmapSource? matchingFrozenFrame = !textOnly && sameSession ? frozenFrame : null;
            BitmapSource? selectionFrame = null;
            if (!repeat && !textOnly && Settings.FreezeRegionBeforeSelection)
            {
                selectionFrame = matchingFrozenFrame;
                if (selectionFrame == null)
                {
                    using (new HiddenWindowsScope())
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                        await Task.Delay(45, cancellation.Token);
                        NativeWindowService.SynchronizeDesktop();
                        cancellation.Token.ThrowIfCancellationRequested();
                        selectionFrame = CaptureService.Capture(monitor);
                    }
                }
            }
            Rect? rect;
            if (repeat)
            {
                if (lastRegion is not { } previous || !SameMonitor(previous.Monitor, monitor)) { Report(L.T("Select a new region first. The capture display or layout has changed.")); return; }
                rect = previous.Region;
            }
            else
            {
                selector = new SelectionWindow(monitor, textOnly, selectionFrame); selector.Show(); selector.Activate(); rect = await selector.Result; selector = null;
            }
            if (rect == null) return;
            for (int seconds = textOnly ? 0 : Settings.CaptureDelaySeconds; seconds > 0; seconds--)
            {
                Report(L.F($"Capturing in {seconds}… Press Esc to cancel."));
                await Task.Delay(1000, cancellation.Token);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            BitmapSource desktop;
            if (selectionFrame != null) desktop = selectionFrame;
            else using (new HiddenWindowsScope()) { await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render); await Task.Delay(45, cancellation.Token); NativeWindowService.SynchronizeDesktop(); desktop = matchingFrozenFrame ?? CaptureService.Capture(monitor); }
            if (disposed) return;
            var r = rect.Value;
            int x = Math.Clamp((int)Math.Floor(r.X * monitor.ScaleX), 0, desktop.PixelWidth - 1), y = Math.Clamp((int)Math.Floor(r.Y * monitor.ScaleY), 0, desktop.PixelHeight - 1);
            int right = Math.Clamp((int)Math.Ceiling(r.Right * monitor.ScaleX), x + 1, desktop.PixelWidth), bottom = Math.Clamp((int)Math.Ceiling(r.Bottom * monitor.ScaleY), y + 1, desktop.PixelHeight);
            if (textOnly) { textImage = new CroppedBitmap(desktop, new Int32Rect(x, y, right - x, bottom - y)); textImage.Freeze(); return; }
            var inkMonitor = overlay?.Monitor;
            bool includeInk = Settings.IncludeAnnotations && inkMonitor != null && (sameSession || monitor.Id == "VirtualDesktop");
            var offset = inkMonitor == null ? new Vector() : inkMonitor.Bounds.TopLeft - monitor.Bounds.TopLeft;
            LastCapture = CaptureComposition.Composite(desktop, includeInk ? Document.Items : [], inkMonitor?.ScaleX ?? 1, inkMonitor?.ScaleY ?? 1, offset, new Int32Rect(x, y, right - x, bottom - y));
            CaptureHistory.Add(LastCapture); lastRegion = (monitor, r);
            statusNotice?.Close();
            if (Settings.CaptureOutput == "Save") SaveLast(); else { await CopyAsync(LastCapture); ShowCaptureNotice(); }
        }
        catch (OperationCanceledException) { statusNotice?.Close(); Report(L.T("Capture cancelled.")); }
        catch (Exception ex) { Report(L.T("Capture failed: ") + ex.Message); }
        finally
        {
            selector?.Close(); selector = null; captureCancellation = null; machine.EndCapture(); IsBusy = false; UpdateEscape();
            if (overlay != null) { if (State == OverlayState.Hidden) overlay.Hide(); else { overlay.Show(); overlay.Mode(State == OverlayState.Draw); } }
            if (State == OverlayState.Draw) Palette?.Show();
            else if (!disposed) NativeWindowService.RestoreForeground(captureForeground);
            scanningText = false;
            if (!disposed && !cancellation.IsCancellationRequested && textImage != null && Settings.ScreenTextEnabled) { var textWindow = OpenTextTools(); if (textWindow != null) _ = textWindow.RecognizeScreenAsync(textImage); }
        }
    }
    public void OpenRecentCapture(BitmapSource image) { LastCapture = image; RedactLast(); }
    public async Task CopyAsync(BitmapSource image)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                var data = new DataObject(); data.SetImage(image);
                using var stream = new MemoryStream(); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(stream); stream.Position = 0;
                data.SetData("PNG", stream); Clipboard.SetDataObject(data, true); Report(L.T("Screenshot copied to clipboard.")); return;
            }
            catch (System.Runtime.InteropServices.ExternalException ex) { last = ex; await Task.Delay(80); }
        }
        Report(L.T("Clipboard is busy. Your screenshot is retained; use Save PNG. ") + last?.Message);
    }
    public void SaveLast() => SaveLast((dialog, owner) => dialog.ShowDialog(owner));
    internal void SaveLast(Func<SaveFileDialog, Window, bool?> showDialog)
    {
        var image = LastCapture;
        if (image == null) { Report(L.T("Capture a screenshot first.")); return; }
        bool wasBusy = IsBusy; IsBusy = true;
        var dialog = new SaveFileDialog { Filter = L.T("PNG image|*.png"), FileName = $"DesktopTools-{DateTime.Now:yyyyMMdd-HHmmss}.png", DefaultExt = ".png", InitialDirectory = Directory.Exists(Settings.SaveDirectory) ? Settings.SaveDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) };
        try
        {
            // A notification can expire inside the native dialog's nested message loop.
            // Explicitly own it with the persistent main window, even when hidden in the tray.
            var owner = EnsureMainWindow();
            _ = new System.Windows.Interop.WindowInteropHelper(owner).EnsureHandle();
            if (showDialog(dialog, owner) != true) return;
            using (var stream = File.Create(dialog.FileName)) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(stream); }
            UpdateSettings(s => s.SaveDirectory = Path.GetDirectoryName(dialog.FileName)!); Report(L.T("Saved ") + Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex) { Report(L.T("Could not save PNG: ") + ex.Message); }
        finally { IsBusy = wasBusy; if (!IsBusy && State == OverlayState.Draw) overlay?.Activate(); }
    }
    private void ShowCaptureNotice()
    {
        captureNotice?.Close();
        var notice = new NotificationWindow(L.T("Screenshot ready"), NotificationKind.Info, main,
            Settings.ScreenshotNotificationStyle, LastCapture,
            [("Edit", RedactLast), ("Save PNG", SaveLast), ("Pin", PinLast)], Settings.NotificationSeconds);
        captureNotice = notice;
        notice.Closed += (_, _) => { if (captureNotice == notice) captureNotice = null; };
        notice.Show();
    }
    public void PinLast() { if (!FeatureAvailability.IsAvailable(Settings, "pin")) return; if (LastCapture == null) { Report(L.T("Capture a screenshot first.")); return; } var window = new PinnedImageWindow(LastCapture, Report); window.SourceInitialized += (_, _) => NativeWindowService.ApplyBackdrop(window, Dark, Settings.Transparency); NativeWindowService.ShowForeground(window); }
    public void RedactLast()
    {
        if (LastCapture == null) { Report(L.T("Capture a screenshot first.")); return; }
        var window = new ScreenshotEditorWindow(LastCapture, image => { LastCapture = image; CaptureHistory.Add(image); _ = CopyAsync(image); ShowCaptureNotice(); }, Report); window.SourceInitialized += (_, _) => NativeWindowService.ApplyBackdrop(window, Dark, Settings.Transparency); NativeWindowService.ShowForeground(window);
    }
    public void TogglePresentation(string mode)
    {
        if (IsBusy || (mode == "Laser" ? !Settings.LaserEnabled : !Settings.SpotlightEnabled)) return;
        try
        {
        var monitor = MonitorService.GetCurrent(Settings.MonitorMode == "Primary");
            bool allMonitors = (mode == "Laser" ? Settings.LaserMonitorMode : Settings.SpotlightMonitorMode) == "All";
            if (presentationMode == mode && (allMonitors || SameMonitor(presentation?.ActiveMonitor, monitor))) { StopPresentation(); return; }
        HideAnnotations(); StopPresentation(); presentationMode = mode;
            presentation = new PresentationSession(mode, Settings, allMonitors, monitor);
            presentation.Failed += message => { StopPresentation(); Report(message); };
            presentation.Show();
        var stop = Ui.Button(L.T(mode == "Laser" ? "Stop laser" : "Stop spotlight"), StopPresentation); stop.Margin = new Thickness(0);
            presentationControls = new Window { Tag = "Drawing palette", Title = L.T(mode), Content = Ui.Card(stop, 8), SizeToContent = SizeToContent.WidthAndHeight, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true, Left = monitor.WorkingArea.Left / monitor.ScaleX + 24, Top = monitor.WorkingArea.Top / monitor.ScaleY + 24 };
        PrepareControlSurface(presentationControls);
            presentationControls.SourceInitialized += (_, _) => { NativeWindowService.TryExcludeFromCapture(presentationControls, true, out _); NativeWindowService.ApplyBackdrop(presentationControls, Dark, Settings.Transparency); }; presentationControls.Show(); UpdateEscape();
        }
        catch (Exception ex) { StopPresentation(); Report(L.T("Could not start presentation: ") + ex.Message); }
    }
    private static void PrepareControlSurface(Window window)
    {
        Motion.WindowEntrance(window);
        // A single rounded surface owns these pixels; no differently rounded DWM backplate.
        if (window.WindowStyle == WindowStyle.None) { window.AllowsTransparency = true; window.Background = Brushes.Transparent; }
        if (window.Content is Border card) { card.Margin = new Thickness(0); card.CornerRadius = new CornerRadius(window.WindowStyle == WindowStyle.None ? 16 : 0); card.SetResourceReference(Border.BackgroundProperty, "Surface"); }
    }
    public void StopPresentation() { presentation?.Close(); presentation = null; presentationControls?.Close(); presentationControls = null; presentationMode = null; UpdateEscape(); }
    public async Task FreezeAsync()
    {
        if (!Settings.DrawingEnabled || !Settings.FreezeEnabled || IsBusy) return;
        using var cancellation = new CancellationTokenSource();
        try
        {
        var monitor = MonitorService.GetCurrent(Settings.MonitorMode == "Primary");
        if (frozenFrame != null && SameMonitor(overlay?.Monitor, monitor)) { frozenFrame = null; overlay?.SetFrozen(null); Report(L.T("Live desktop restored.")); return; }
        if (main?.IsVisible == true)
        {
            using (DesktopTools.Presentation.WindowDismissal.Suppress())
                DesktopTools.Presentation.WindowDismissal.Hide(main, () => Motion.Enabled);
        }
        if (State != OverlayState.Draw) previousForeground = NativeWindowService.GetForegroundWindowHandle();
        HideAnnotations(); StopPresentation(); IsBusy = true; captureCancellation = cancellation; UpdateEscape();
            BitmapSource image;
            using (new HiddenWindowsScope()) { await Task.Delay(45, cancellation.Token); NativeWindowService.SynchronizeDesktop(); image = CaptureService.Capture(monitor); }
            if (disposed) return;
            EnsureOverlay(monitor); frozenFrame = image; overlay!.SetFrozen(image);
            machine.ToggleDraw(); overlay.Show(); overlay.Mode(true); ShowPalette(); Palette!.Refresh();
            string hint = FeatureShortcutCatalog.Bindings(Settings).TryGetValue("Freeze", out string? shortcut)
                ? L.F($"Screen frozen. Press Esc or {shortcut} to stop freeze frame.")
                : L.T("Screen frozen. Press Esc to stop freeze frame.");
            Report(hint, NotificationKind.Info, 5);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Report(L.T("Freeze frame failed: ") + ex.Message); }
        finally { captureCancellation = null; IsBusy = false; UpdateEscape(); }
    }
    private static bool SystemUsesLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }
    public void ApplyTheme()
    {
        bool dark = Dark; Motion.AppEnabled = Settings.Animations;
        string accentHex = Settings.ThemePreset switch { "Ocean" => "#007EAA", "Forest" => "#218557", "Violet" => "#8854D0", "Custom" => Settings.PrimaryColor, _ => "#2563EB" };
        if (Settings.ThemePreset != "Custom") Settings.PrimaryColor = accentHex;
        Color accent = (Color)ColorConverter.ConvertFromString(accentHex); accent.A = 255;
        Application.Current.Resources["Accent"] = new SolidColorBrush(accent);
        Application.Current.Resources["AccentText"] = new SolidColorBrush(.2126 * accent.R + .7152 * accent.G + .0722 * accent.B > 160 ? Colors.Black : Colors.White);
        foreach (var (key, light, night) in new[] { ("Surface", "#F3F5FA", "#17181C"), ("Shell", "#B3F3F5FA", "#D0121316"), ("Card", "#EEFFFFFF", "#FF202226"), ("Text", "#19202D", "#F3F5FA"), ("Muted", "#626D7E", "#A5ACB9"), ("Stroke", "#1C293A55", "#FF35383F"), ("Divider", "#D2D9E5", "#FF454A54"), ("Hover", "#E8EDF6", "#FF2C3038"), ("Selected", "#DFE9FC", "#FF303949"), ("Field", "#FAFBFE", "#FF292C32") }) Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? night : light));
        if (Settings.UseCustomBackground)
        {
            Color background = (Color)ColorConverter.ConvertFromString(Settings.BackgroundColor); background.A = 255;
            bool light = .2126 * background.R + .7152 * background.G + .0722 * background.B > 145;
            Color Mix(Color a, Color b, double amount) => Color.FromRgb((byte)(a.R*(1-amount)+b.R*amount), (byte)(a.G*(1-amount)+b.G*amount), (byte)(a.B*(1-amount)+b.B*amount));
            Application.Current.Resources["Surface"] = new SolidColorBrush(background);
            Application.Current.Resources["Shell"] = new SolidColorBrush(background);
            foreach (string key in new[] { "Card", "Field", "Hover" }) Application.Current.Resources[key] = new SolidColorBrush(Mix(background, light ? Colors.White : Colors.Black, .18));
            Application.Current.Resources["Divider"] = new SolidColorBrush(Mix(background, light ? Colors.Black : Colors.White, light ? .13 : .17));
            Application.Current.Resources["Text"] = new SolidColorBrush(light ? Color.FromRgb(25,32,45) : Colors.White);
            Application.Current.Resources["Muted"] = new SolidColorBrush(light ? Color.FromRgb(65,72,85) : Color.FromRgb(205,210,220));
        }
        var surface = ((SolidColorBrush)Application.Current.Resources["Surface"]).Color;
        Application.Current.Resources["Selected"] = new SolidColorBrush(Color.FromRgb((byte)(surface.R*.82+accent.R*.18), (byte)(surface.G*.82+accent.G*.18), (byte)(surface.B*.82+accent.B*.18)));
        if (SystemParameters.HighContrast)
        {
            foreach (string key in new[] { "Surface", "Shell", "Card", "Field", "Selected", "Hover" }) Application.Current.Resources[key] = new SolidColorBrush(SystemColors.WindowColor);
            foreach (string key in new[] { "Text", "Muted" }) Application.Current.Resources[key] = new SolidColorBrush(SystemColors.WindowTextColor);
            Application.Current.Resources["Stroke"] = new SolidColorBrush(SystemColors.WindowTextColor);
            Application.Current.Resources["Divider"] = new SolidColorBrush(SystemColors.WindowTextColor);
        }
        DesignTokens.ApplySurfaces(Application.Current.Resources, Settings.Transparency);
        // Embedded dialogs have no HWND. Their brushes update through resources on the main window.
        foreach (Window w in Application.Current.Windows) { if (!w.AllowsTransparency && new System.Windows.Interop.WindowInteropHelper(w).Handle != IntPtr.Zero) NativeWindowService.ApplyBackdrop(w, dark, Settings.Transparency && !SystemParameters.HighContrast); }
    }
    private void DisplayChanged(object? sender, EventArgs e) => Application.Current.Dispatcher.BeginInvoke(() => { quickWheel?.Close(); captureCancellation?.Cancel(); Hud.StopAll(); RemoveOverlay(); StopPresentation(); lastRegion = null; Report(L.T("Display configuration changed. Activate a tool to use the updated layout.")); });
    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Application.Current.Dispatcher.BeginInvoke(ApplyTheme);
    public async Task RunSmokeAsync()
    {
        try
        {
            await Task.Delay(600); var folder = Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke"); Directory.CreateDirectory(folder);
            foreach (var theme in new[] { "Light", "Dark" })
            {
                Settings.Theme = theme; ApplyTheme(); main!.Navigate("Home"); await Task.Delay(250); main.UpdateLayout();
                var image = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(main);
                using var stream = File.Create(Path.Combine(folder, $"home-{theme.ToLowerInvariant()}.png")); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(stream);
            }
            foreach (var page in new[] { "Draw", "Capture", "Present", "Profiles", "Shortcuts", "Settings", "About" }) { main!.Navigate(page); main.UpdateLayout(); }
            File.WriteAllText(Path.Combine(folder, "result.txt"), "PASS: main window constructed, both themes rendered, all pages constructed. Native interaction/sharing not tested.");
            Application.Current.Shutdown();
        }
        catch (Exception ex) { Directory.CreateDirectory("artifacts"); File.WriteAllText("artifacts/smoke-error.txt", ex.ToString()); Application.Current.Shutdown(1); }
    }
    public void Dispose()
    {
        using var immediateExit = DesktopTools.Presentation.WindowDismissal.Suppress();
        if (disposed) return; disposed = true; SystemEvents.DisplaySettingsChanged -= DisplayChanged; SystemEvents.UserPreferenceChanged -= PreferenceChanged;
        StopUpdateChecks();
        trayMenu?.SetCurrentValue(System.Windows.Controls.Primitives.Popup.IsOpenProperty, false); setupWindow?.Close(); quickWheel?.Close(); captureCancellation?.Cancel(); windowPins.Dispose(); foreach (var utility in utilityWindows.Values.ToArray()) utility.Close(); fileShelf?.Shutdown(); floatingNotes?.Dispose(); audioControls?.Close(); Hud.Dispose(); selector?.Close(); StopPresentation(); RemoveOverlay(); CaptureHistory.Clear(); captureNotice?.Close(); statusNotice?.Close(); tray?.Dispose(); trayIcon?.Dispose(); hotkeys.Dispose(); escape.Dispose();
    }
}
