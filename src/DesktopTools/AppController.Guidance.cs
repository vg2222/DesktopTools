using DesktopTools.UI;

namespace DesktopTools;

internal sealed partial class AppController
{
    internal void OpenFeatureGuide(string feature)
    {
        string key;
        switch (feature)
        {
            case "recorder": if (!Settings.ScreenRecorderEnabled) return; OpenScreenRecorder(); key = "Recorder"; break;
            case "teleprompter": if (!Settings.TeleprompterEnabled) return; OpenTeleprompter(); key = "Teleprompter"; break;
            case "image-editor": if (!Settings.ImageToolsEnabled) return; OpenImageTools(); key = "Images"; break;
            case "video-editor": if (!Settings.VideoEditorEnabled) return; OpenVideoEditor(); key = "Video"; break;
            case "text-tools": if (!Settings.TranslationEnabled && !Settings.ScreenTextEnabled) return; OpenTextToolsWindow(); key = "Text"; break;
            default: return;
        }
        if (utilityWindows.TryGetValue(key, out var window)) FeatureTourButton.Start(window, repeatSetup: true);
    }
}
