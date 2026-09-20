using DesktopTools.Localization;
using DesktopTools.Native;

namespace DesktopTools.Core;

/// <summary>One list for global shortcut settings, availability, and dispatch.</summary>
public static class FeatureShortcutCatalog
{
    public sealed record Entry(string Action, string Title, string FeatureId, string DefaultGesture,
        Func<AppSettings, string> Read, Action<AppSettings, string> Write, bool Optional);

    private static Entry Legacy(string action, string title, string featureId, string fallback,
        Func<AppSettings, string> read, Action<AppSettings, string> write) =>
        new(action, title, featureId, fallback, read, write, false);

    private static Entry Optional(string action, string title, string featureId, string fallback) =>
        new(action, title, featureId, fallback,
            s => s.FeatureShortcuts != null && s.FeatureShortcuts.TryGetValue(action, out string? value) ? value ?? "" : fallback,
            (s, value) => (s.FeatureShortcuts ??= new())[action] = value, true);

    public static IReadOnlyList<Entry> All { get; } = Array.AsReadOnly(new[]
    {
        Legacy("Translate", "Translate selected text", "translate", "Ctrl+Alt+R", s => s.TranslationShortcut, (s,v) => s.TranslationShortcut = v),
        Legacy("ScreenText", "Scan screen text", "ocr", "Ctrl+Alt+E", s => s.ScreenTextShortcut, (s,v) => s.ScreenTextShortcut = v),
        Legacy("QuickWheel", "Quick actions wheel", "wheel", "Ctrl+Alt+Q", s => s.QuickWheelShortcut, (s,v) => s.QuickWheelShortcut = v),
        Legacy("Teleprompter", "Teleprompter play / pause", "prompter", "Ctrl+Alt+M", s => s.TeleprompterShortcut, (s,v) => s.TeleprompterShortcut = v),
        Legacy("PinWindow", "Pin active window", "window", "Ctrl+Alt+T", s => s.WindowPinShortcut, (s,v) => s.WindowPinShortcut = v),
        Legacy("Eyedropper", "Screen eyedropper", "color", "Ctrl+Alt+P", s => s.EyedropperShortcut, (s,v) => s.EyedropperShortcut = v),
        Legacy("Draw", "Draw / interact", "draw", "Ctrl+Alt+D", s => s.DrawShortcut, (s,v) => s.DrawShortcut = v),
        Legacy("Capture", "Capture region", "capture", "Ctrl+Alt+S", s => s.CaptureShortcut, (s,v) => s.CaptureShortcut = v),
        Legacy("HidePalette", "Hide / show palette", "draw", "Ctrl+Alt+H", s => s.HidePaletteShortcut, (s,v) => s.HidePaletteShortcut = v),
        Legacy("Laser", "Laser pointer", "laser", "Ctrl+Alt+L", s => s.LaserShortcut, (s,v) => s.LaserShortcut = v),
        Legacy("Spotlight", "Spotlight", "spotlight", "Ctrl+Alt+O", s => s.SpotlightShortcut, (s,v) => s.SpotlightShortcut = v),
        Legacy("Freeze", "Freeze frame", "freeze", "Ctrl+Alt+F", s => s.FreezeShortcut, (s,v) => s.FreezeShortcut = v),
        Optional("Recorder", "Screen recorder", "record", "Ctrl+Alt+Shift+R"),
        Optional("Images", "Image tools", "images", "Ctrl+Alt+Shift+I"),
        Optional("Video", "Video editor", "video", "Ctrl+Alt+Shift+V"),
        Optional("QrCodes", "QR codes", "qr", "Ctrl+Alt+Shift+Q"),
        Optional("Notes", "Floating notes", "notes", "Ctrl+Alt+Shift+N"),
        Optional("FileShelf", "File shelf", "files", "Ctrl+Alt+Shift+F"),
        Optional("AudioControls", "Audio controls", "audio", "Ctrl+Alt+Shift+A"),
        Optional("PinLast", "Pin latest screenshot", "pin", "Ctrl+Alt+Shift+P"),
        Optional("AidClickIndicators", "Click indicators", "aid-Click indicators", "Ctrl+Alt+Shift+1"),
        Optional("AidShortcutDisplay", "Shortcut display", "aid-Shortcut display", "Ctrl+Alt+Shift+2"),
        Optional("AidStopwatch", "Stopwatch", "aid-Stopwatch", "Ctrl+Alt+Shift+3"),
        Optional("AidCountdown", "Countdown", "aid-Countdown", "Ctrl+Alt+Shift+4"),
        Optional("AidRuler", "Screen ruler", "aid-Screen ruler", "Ctrl+Alt+Shift+5"),
        Optional("AidBlackout", "Screen blackout", "aid-Screen blackout", "Ctrl+Alt+Shift+6")
    });

    public static Dictionary<string, string> Defaults => All.Where(x => x.Optional)
        .ToDictionary(x => x.Action, x => x.DefaultGesture, StringComparer.Ordinal);

    public static bool IsEnabled(AppSettings settings, Entry entry) =>
        settings.ShortcutEnabled != null && settings.ShortcutEnabled.TryGetValue(entry.Action, out bool enabled)
            ? enabled : !entry.Optional;

    public static void SetEnabled(AppSettings settings, Entry entry, bool enabled) =>
        (settings.ShortcutEnabled ??= new())[entry.Action] = enabled;

    public static Dictionary<string, string> Bindings(AppSettings settings) => All
        .Where(x => IsEnabled(settings, x) && FeatureAvailability.IsAvailable(settings, x.FeatureId))
        .Select(x => (x.Action, Gesture: x.Read(settings)))
        .Where(x => !string.IsNullOrWhiteSpace(x.Gesture))
        .ToDictionary(x => x.Action, x => x.Gesture, StringComparer.Ordinal);

    public static void Normalize(AppSettings settings)
    {
        settings.FeatureShortcuts ??= new();
        settings.ShortcutEnabled ??= new();
        foreach (string key in settings.ShortcutEnabled.Keys.Except(All.Select(x => x.Action)).ToArray())
            settings.ShortcutEnabled.Remove(key);
        foreach (string key in settings.FeatureShortcuts.Keys.Except(All.Where(x => x.Optional).Select(x => x.Action)).ToArray())
            settings.FeatureShortcuts.Remove(key);
        var used = new HashSet<HotkeyGesture>();
        foreach (Entry entry in All.Where(x => !x.Optional))
            if (IsEnabled(settings, entry) && HotkeyGesture.TryParse(entry.Read(settings), out var gesture, out _)) used.Add(gesture);
        foreach (Entry entry in All.Where(x => x.Optional))
        {
            string value = (entry.Read(settings) ?? "").Trim();
            if (value.Length > 100 || (value.Length != 0 &&
                (!HotkeyGesture.TryParse(value, out var gesture, out _) || (IsEnabled(settings, entry) && !used.Add(gesture))))) value = "";
            entry.Write(settings, value);
        }
    }

    public static bool TryValidate(AppSettings settings, out string? error)
    {
        error = null;
        var used = new Dictionary<HotkeyGesture, string>();
        foreach (Entry entry in All)
        {
            if (!IsEnabled(settings, entry)) continue;
            string value = entry.Read(settings) ?? "";
            if (entry.Optional && string.IsNullOrWhiteSpace(value)) continue;
            if (!HotkeyGesture.TryParse(value, out var gesture, out error))
            { error = L.T(entry.Title) + ": " + error; return false; }
            if (used.TryGetValue(gesture, out string? other))
            { error = L.F($"{L.T(entry.Title)} and {L.T(other)} use the same shortcut."); return false; }
            used.Add(gesture, entry.Title);
        }
        return true;
    }
}
