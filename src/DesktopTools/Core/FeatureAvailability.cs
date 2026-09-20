namespace DesktopTools.Core;

/// <summary>One adapter over the persisted Enabled properties; no parallel preference store.</summary>
public static class FeatureAvailability
{
    public sealed record Feature(string Id, Func<AppSettings, bool> Read, Action<AppSettings, bool> Write);
    public static IReadOnlyList<Feature> All { get; } = Array.AsReadOnly(new Feature[]
    {
        new("aid-Click indicators", s => s.ClickIndicatorsEnabled, (s,v) => s.ClickIndicatorsEnabled = v),
        new("aid-Shortcut display", s => s.KeystrokesEnabled, (s,v) => s.KeystrokesEnabled = v),
        new("aid-Stopwatch", s => s.StopwatchEnabled, (s,v) => s.StopwatchEnabled = v),
        new("aid-Countdown", s => s.CountdownEnabled, (s,v) => s.CountdownEnabled = v),
        new("aid-Screen ruler", s => s.ScreenRulerEnabled, (s,v) => s.ScreenRulerEnabled = v),
        new("aid-Screen blackout", s => s.ScreenBlackoutEnabled, (s,v) => s.ScreenBlackoutEnabled = v),
        new("capture", s => s.CaptureEnabled, (s,v) => s.CaptureEnabled = v),
        new("record", s => s.ScreenRecorderEnabled, (s,v) => s.ScreenRecorderEnabled = v),
        new("draw", s => s.DrawingEnabled, (s,v) => s.DrawingEnabled = v),
        new("laser", s => s.LaserEnabled, (s,v) => s.LaserEnabled = v),
        new("spotlight", s => s.SpotlightEnabled, (s,v) => s.SpotlightEnabled = v),
        new("freeze", s => s.FreezeEnabled, (s,v) => s.FreezeEnabled = v),
        new("prompter", s => s.TeleprompterEnabled, (s,v) => s.TeleprompterEnabled = v),
        new("images", s => s.ImageToolsEnabled, (s,v) => s.ImageToolsEnabled = v),
        new("video", s => s.VideoEditorEnabled, (s,v) => s.VideoEditorEnabled = v),
        new("color", s => s.EyedropperEnabled, (s,v) => s.EyedropperEnabled = v),
        new("ocr", s => s.ScreenTextEnabled, (s,v) => s.ScreenTextEnabled = v),
        new("translate", s => s.TranslationEnabled, (s,v) => s.TranslationEnabled = v),
        new("qr", s => s.QrCodesEnabled, (s,v) => s.QrCodesEnabled = v),
        new("notes", s => s.FloatingNotesEnabled, (s,v) => s.FloatingNotesEnabled = v),
        new("files", s => s.FileShelfEnabled, (s,v) => s.FileShelfEnabled = v),
        new("audio", s => s.AudioControlsEnabled, (s,v) => s.AudioControlsEnabled = v),
        new("wheel", s => s.QuickWheelEnabled, (s,v) => s.QuickWheelEnabled = v),
        new("window", s => s.WindowPinEnabled, (s,v) => s.WindowPinEnabled = v)
    });
    public static bool IsAvailable(AppSettings settings, string id) => id switch
    {
        "pin" => settings.CaptureEnabled,
        "freeze" => settings.FreezeEnabled && settings.DrawingEnabled,
        _ => All.FirstOrDefault(f => f.Id == id)?.Read(settings) ?? false
    };
    public static void Enable(AppSettings settings, string id)
    {
        if (id == "pin") id = "capture";
        if (id == "freeze") settings.DrawingEnabled = true;
        All.FirstOrDefault(f => f.Id == id)?.Write(settings, true);
    }
    public static bool IsWheelAvailable(AppSettings settings, string action) => action == "Text tools"
        ? settings.TranslationEnabled || settings.ScreenTextEnabled
        : IsAvailable(settings, action switch
        {
            "Draw" => "draw", "Capture" => "capture", "Laser" => "laser", "Spotlight" => "spotlight",
            "Freeze" => "freeze", "File shelf" => "files", "Notes" => "notes", "Audio" => "audio",
            "Screen recorder" => "record", "Video editor" => "video", "QR codes" => "qr", "Image tools" => "images",
            "Eyedropper" => "color", "Teleprompter" => "prompter", "Scan screen text" => "ocr", _ => ""
        });
}
