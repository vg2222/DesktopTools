using System.Windows;
using System.Windows.Controls;
using DesktopTools.Extras;
using DesktopTools.UI;

namespace DesktopTools.Native;

internal static class AppCapturePrivacy
{
    private static bool initialized;
    internal static readonly string[] FeatureGroups = ["Drawing palette", "Selection controls", "Floating notes", "File shelf", "Audio controls", "Image tools", "Video editor", "Screen recorder", "Text tools", "Notifications", "Other feature windows"];
    internal static readonly string[] IndividualTools = ["Teleprompter", "Countdown", "Stopwatch", "Screen ruler"];
    internal static readonly string[] DefaultHidden = ["Drawing palette", "Selection controls", "Floating notes", "Screen recorder", "Notifications", "Teleprompter"];
    internal static bool ShouldHideFeature(string feature, DesktopTools.Core.AppSettings settings)
    {
        if (settings.CaptureVisibilityOverrides.TryGetValue(feature, out bool hidden)) return hidden;
        if (IndividualTools.Contains(feature)) return DefaultHidden.Contains(feature) || settings.HiddenCaptureTools.Contains(feature);
        if (settings.VisibleCaptureFeatures.Contains(feature)) return false;
        return DefaultHidden.Contains(feature) || settings.HideControlsFromCapture;
    }
    private static string? IndividualTool(Window window) => window.Tag is string tag && IndividualTools.Contains(tag) ? tag : window.GetType().Name switch
    {
        "TeleprompterWindow" => "Teleprompter", "CountdownWindow" => "Countdown", "ScreenRulerWindow" => "Screen ruler", _ => null
    };
    private static Window PrivacyOwner(Window window)
    {
        // Shared dialogs belong to the tool that opened them. A separately named tool
        // (for example OCR opened from an image editor) keeps its own privacy setting.
        while (!(window.Tag is string tag && (FeatureGroups.Contains(tag) || IndividualTools.Contains(tag))) &&
            (window is ConfirmationDialog or SaveBeforeDisableDialog or FeatureSetupWindow or ColorPickerWindow || window.GetType() == typeof(Window)) &&
            window.Owner is { } owner)
            window = owner;
        return window;
    }
    internal static string FeatureGroup(Window window)
    {
        window = PrivacyOwner(window);
        return window.Tag is string tag && FeatureGroups.Contains(tag) ? tag : window.GetType().Name switch
    {
        "PaletteWindow" or "ColorPickerWindow" => "Drawing palette",
        "SelectionWindow" => "Selection controls",
        "FloatingNoteWindow" or "NotesHubWindow" or "NoteWindow" => "Floating notes",
        "FileShelfWindow" => "File shelf", "AudioControlsWindow" => "Audio controls",
        "ScreenshotEditorWindow" or "ImageToolsWindow" or "PinnedImageWindow" or "ImageExportDialog" => "Image tools",
        "VideoEditorWindow" => "Video editor", "ScreenRecorderWindow" or "RecordingHudWindow" or "RecordingQualityWindow" or "RecordingSourcePickerWindow" => "Screen recorder",
        "TextToolsWindow" or "OcrTextWindow" => "Text tools",
        "NotificationWindow" => "Notifications", _ => "Other feature windows"
        };
    }
    internal static bool ShouldHide(Window window, DesktopTools.Core.AppSettings settings)
    {
        if (!IsControlWindow(window)) return false;
        window = PrivacyOwner(window);
        if (AlwaysVisible(window)) return ShouldHideFeature("Drawing palette", settings);
        if (window is MainWindow) return settings.HideMainWindowFromCapture ?? false;
        return ShouldHideFeature(IndividualTool(window) ?? FeatureGroup(window), settings);
    }
    internal static readonly object PresentationSurface = new();
    internal static readonly object BlackoutSurface = new();
    internal static bool AlwaysVisible(Window window) => window is OverlayWindow or PresentationEffectWindow || ReferenceEquals(window.Tag, PresentationSurface);
    internal static bool ShouldHidePopup(Window owner, DesktopTools.Core.AppSettings settings) =>
        AlwaysVisible(owner) ? ShouldHideFeature("Drawing palette", settings) : ShouldHide(owner, settings);
    public static bool IsControlWindow(Window window) => !AlwaysVisible(window) && !ReferenceEquals(window.Tag, BlackoutSurface);
    public static void Initialize()
    {
        if (initialized) return; initialized = true;
        NativeWindowService.CapturePrivacyResolver = window => AlwaysVisible(window) ? false : (Application.Current as App)?.Controller is { } controller && IsControlWindow(window) ? ShouldHide(window, controller.Settings) : null;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, args) =>
        {
            if (ReferenceEquals(sender, args.OriginalSource) && sender is Window window && (Application.Current as App)?.Controller is { } controller)
                Apply(window, controller.Settings);
        }));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is ContextMenu menu) menu.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() => { if (menu.IsOpen) Ui.ExcludePopup(menu); }));
        }));
        EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is ToolTip tip) tip.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() => { if (tip.IsOpen) Ui.ExcludePopup(tip); }));
        }));
        EventManager.RegisterClassHandler(typeof(MenuItem), MenuItem.SubmenuOpenedEvent, new RoutedEventHandler((sender, args) =>
        {
            if (!ReferenceEquals(sender, args.OriginalSource) || sender is not MenuItem menu) return;
            menu.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (!menu.IsSubmenuOpen || menu.Template?.FindName("PART_Popup", menu) is not System.Windows.Controls.Primitives.Popup { IsOpen: true } popup || popup.Child is not System.Windows.Media.Visual visual) return;
                Ui.ExcludePopup(visual, Ui.PopupOwner(menu));
            }));
        }));
    }
    internal static void TrackCombo(ComboBox combo, bool enabled)
    {
        combo.DropDownOpened -= ComboOpened;
        if (enabled) combo.DropDownOpened += ComboOpened;
    }
    private static void ComboOpened(object? sender, EventArgs args)
    {
        if (sender is not ComboBox combo) return;
        combo.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (combo.IsDropDownOpen && combo.Template?.FindName("PART_Popup", combo) is System.Windows.Controls.Primitives.Popup { IsOpen: true } popup && popup.Child is System.Windows.Media.Visual visual)
                Ui.ExcludePopup(visual, Ui.PopupOwner(combo));
        }));
    }
    public static void Apply(Window window, DesktopTools.Core.AppSettings settings)
    {
        if ((IsControlWindow(window) || AlwaysVisible(window)) && window.IsLoaded) NativeWindowService.TryExcludeFromCapture(window, ShouldHide(window, settings), out _);
    }
}
