using DesktopTools.Localization;

namespace DesktopTools.UI;

/// <summary>
/// Chooses an icon for a label that was written as plain text, so settings rows, buttons and menu items read at a glance without every
/// call site naming one. Matching is on the English source, so it gives the same icon in every language. The first rule that matches wins.
/// </summary>
internal static class IconHints
{
    // Ordered: specific actions first, broad subjects last.
    private static readonly (string[] Words, string Icon)[] Rules =
    [
        (["extract text", "scan screen text", "recognized", " ocr"], "ScanText"),
        (["translat", "language"], "Translate"),
        (["undo"], "Undo"), (["redo"], "Redo"),
        (["start at", "startup", "login", "sign-in", " tray"], "Behavior"),
        (["folder", "location", "browse", "directory"], "Folder"),
        (["save", "export", "download", "install", "apply"], "Save"),
        (["copy", "clipboard", "duplicate"], "Copy"),
        (["open file", "import", "choose file", "choose image", "choose video", "add file", "attach"], "File"),
        (["delete", "remove", "clear", "reset", "erase", "discard"], "Clear"),
        (["cancel", "close", "dismiss", "not now", " skip", " later"], "Close"),
        (["done", " ok ", "confirm", "accept", "finish", "continue", " yes ", "enable", "turn on", "got it"], "Check"),
        ([" add ", " new ", "create"], "Plus"),
        (["search", " find"], "Search"),
        (["refresh", "reload", "retry", "try again", "check again", "check for updates", "check now", "update", "restart", "repair"], "Refresh"),
        (["shortcut", "hotkey", "keystroke", " key"], "Shortcuts"),
        (["help", "guide", " tour", "learn", "how to", "documentation", " tips"], "Help"),
        (["about", "version", "license", "release notes", "what's new"], "About"),
        (["privacy", "private", "hide", "hidden", "sensitive", "redact", "secure", "protect", "exclude"], "Shield"),
        ([" lock", "click-through", "click through"], "Lock"),
        (["notification", "notice", " news", "announcement", "alert", "remind"], "Notifications"),
        (["microphone", "audio", "sound", "volume", " mute"], "Audio"),
        (["record"], "Record"),
        (["frame rate", " fps", "quality", "hardware", "encoder", "video", " trim", " gif", "webp"], "Video"),
        (["delay", "timer", "countdown", "stopwatch", "seconds", "minutes", "duration", "interval", " hours", "speed"], "Timer"),
        ([" play", "preview", " start", " run ", "resume", "animation", "motion"], "Play"),
        (["pause"], "Pause"), ([" stop "], "Stop"),
        (["freeze"], "Freeze"), (["laser"], "Laser"), (["spotlight", " dim ", "dimming"], "Spotlight"),
        (["region", "select"], "Select"),
        ([" crop"], "Crop"), (["rotate"], "RotateRight"), ([" flip"], "FlipHorizontal"),
        ([" pin"], "Pin"),
        (["eyedropper", "pick color", "sample"], "Eyedropper"),
        (["background", "transparen", "opacity", "backdrop", " blur", "acrylic"], "Background"),
        (["color", "colour", "palette", "theme", "appearance", "accent"], "Color"),
        (["thickness", " size", "width", "height", "length", "radius", "ruler", "scale", "zoom", "resize"], "Ruler"),
        (["annotation", " draw", " ink", " pen ", "stroke", " tool "], "Pen"),
        (["arrow"], "Arrow"), (["shape", " fill", " snap"], "Rectangle"),
        ([" font", " text", "prompter", "script", "caption", " label"], "Text"),
        ([" qr"], "QR"),
        ([" note"], "Notes"),
        (["display", "monitor", "screen "], "Monitor"),
        (["window", "topmost", "above", "always on top"], "Window"),
        (["capture", "screenshot", "snapshot", "library", "history", "recent", "latest"], "Capture"),
        (["image", "picture", "photo", "original"], "Image"),
        (["click", "pointer", "cursor", "mouse"], "Click"),
        (["present", "sharing", " share", "meeting", "audience"], "Present"),
        (["profile", "preset"], "Profiles"),
        (["favorite", "favourite"], "Star"),
        ([" file", "shelf", " data", "backup", "portable"], "File"),
        (["behavior", "behaviour", "automatic", " auto", "default", " mode", " after "], "Behavior"),
        (["setting", "option", "preference", "configure", "customi", "advanced", " more"], "Settings"),
        (["diagnos", "status", "check", " test", "verify"], "Check"),
    ];
    private static readonly Dictionary<string, string?> Cache = new(StringComparer.Ordinal);

    /// <summary>An icon name for this label (in any supported language), or null when nothing fits.</summary>
    internal static string? For(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        lock (Cache)
        {
            string key = L.Language + "\n" + label;
            if (Cache.TryGetValue(key, out var known)) return known;
            string english = " " + L.Source(label).ToLowerInvariant() + " ";
            string? icon = null;
            foreach (var (words, name) in Rules)
                if (words.Any(word => english.Contains(word, StringComparison.Ordinal))) { icon = name; break; }
            if (Cache.Count > 4096) Cache.Clear();
            return Cache[key] = icon;
        }
    }
}
