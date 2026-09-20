using DesktopTools.Core;

namespace DesktopTools;

internal sealed partial class AppController
{
    public void RecordFeatureShortcut(FeatureShortcutCatalog.Entry entry) =>
        RecordShortcut(entry.Read(Settings), entry.Write);

    public bool ClearFeatureShortcut(FeatureShortcutCatalog.Entry entry) =>
        entry.Optional && UpdateSettings(settings => { entry.Write(settings, ""); FeatureShortcutCatalog.SetEnabled(settings, entry, false); });

    public bool SetFeatureShortcutEnabled(FeatureShortcutCatalog.Entry entry, bool enabled) => UpdateSettings(settings =>
    {
        if (enabled && string.IsNullOrWhiteSpace(entry.Read(settings))) entry.Write(settings, entry.DefaultGesture);
        FeatureShortcutCatalog.SetEnabled(settings, entry, enabled);
    });

    private void HandleFeatureHotkey(string action)
    {
        switch (action)
        {
            case "Recorder": OpenScreenRecorder(); break;
            case "Images": OpenImageTools(); break;
            case "Video": OpenVideoEditor(); break;
            case "QrCodes": OpenQrCodes(); break;
            case "Notes": OpenFloatingNotes(); break;
            case "FileShelf": OpenFileShelf(); break;
            case "AudioControls": OpenAudioControls(); break;
            case "PinLast": PinLast(); break;
            case "AidClickIndicators": Hud.ToggleClickIndicators(); break;
            case "AidShortcutDisplay": Hud.ToggleKeystrokes(); break;
            case "AidStopwatch": Hud.ToggleTimer(); break;
            case "AidCountdown": Hud.ToggleCountdown(); break;
            case "AidRuler": Hud.ToggleRuler(); break;
            case "AidBlackout":
                try
                {
                    Hud.ToggleBlackout();
                    if (Hud.IsBlackoutVisible) main?.Hide();
                }
                catch (Exception error) { Report(error.Message, DesktopTools.UI.NotificationKind.Error); }
                break;
        }
    }
}
