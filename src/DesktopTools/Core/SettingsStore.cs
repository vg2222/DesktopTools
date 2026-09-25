using DesktopTools.Localization;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;

namespace DesktopTools.Core;

public sealed class SettingsStore
{
    private readonly string _directory;
    private readonly string _path;
    private bool _futureSchema;
    private bool _preservationFailed;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public string? RecoveryMessage { get; private set; }
    public bool IsNew { get; private set; }

    public SettingsStore(string? directory = null)
    {
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTools");
        _path = Path.Combine(_directory, "settings.json");
    }

    public AppSettings Load()
    {
        RecoveryMessage = null; _futureSchema = false; _preservationFailed = false;
        IsNew = !File.Exists(_path);
        if (IsNew) return new();
        try
        {
            var json = File.ReadAllText(_path);
            using var document = JsonDocument.Parse(json);
            if (HasFutureVersion(document.RootElement))
            {
                _futureSchema = true;
                RecoveryMessage = L.T("Preferences were created by a newer DesktopTools version. Defaults are in use; the original file will not be changed.");
                return new();
            }
            var settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? throw new JsonException(L.T("Empty settings document."));
            if (settings.Version < 1) throw new JsonException(L.T("Unsupported settings version."));
            Validate(settings); return settings;
        }
        catch (JsonException)
        {
            var backup = Path.Combine(_directory, $"settings.damaged-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            try
            {
                File.Move(_path, backup);
                RecoveryMessage = L.F($"Preferences could not be read. Defaults restored; the damaged file is retained at {backup}.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _preservationFailed = true;
                RecoveryMessage = L.T("Defaults are in use. The damaged preferences could not be backed up and remain unchanged; saving is disabled until they can be recovered. ") + ex.Message;
            }
            return new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RecoveryMessage = L.T("Preferences could not be read: ") + ex.Message;
            return new();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_preservationFailed) throw new InvalidOperationException(L.T("Cannot save until the damaged preferences have been safely recovered. Restart DesktopTools after releasing the file."));
        if (_futureSchema || settings.Version > 1) throw new InvalidOperationException(L.T("Cannot overwrite preferences from a newer DesktopTools version."));
        // Re-check the on-disk version even when this instance has not loaded it.
        if (File.Exists(_path))
        {
            using var existing = JsonDocument.Parse(File.ReadAllText(_path));
            if (HasFutureVersion(existing.RootElement))
                throw new InvalidOperationException(L.T("Cannot overwrite preferences from a newer DesktopTools version."));
        }
        Validate(settings);
        Directory.CreateDirectory(_directory);
        var temporary = Path.Combine(_directory, $"settings-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, settings, Options); stream.Flush(true); }
            if (File.Exists(_path)) File.Replace(temporary, _path, null);
            else File.Move(temporary, _path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static bool HasFutureVersion(JsonElement root) => root.ValueKind == JsonValueKind.Object &&
        root.EnumerateObject().Any(p => p.Name.Equals("Version", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt64(out var version) && version > 1);

    public static void Validate(AppSettings s)
    {
        // Tool availability is no longer a preference. Retain the old fields for
        // reading existing settings and profiles, but migrate every tool to ready.
        // ShortcutEnabled remains independent and is never changed here.
        foreach (var feature in FeatureAvailability.All) feature.Write(s, true);
        s.Setup ??= new(); s.Setup.Validate(4);
        s.FeatureSetup ??= new(); s.FeatureTours ??= new();
        foreach (var progress in new[] { s.FeatureSetup, s.FeatureTours })
        {
            foreach (string key in progress.Keys.ToArray())
            {
                if (string.IsNullOrWhiteSpace(key) || key.Length > 80) { progress.Remove(key); continue; }
                progress[key] ??= new(); progress[key].Validate(32);
            }
        }
        s.NotificationSeconds = Number(s.NotificationSeconds, 3, 30, 6);
        if (s.RecordingFramesPerSecond is not (24 or 30 or 60 or 90 or 120 or 144)) s.RecordingFramesPerSecond = 30;
        if (s.RecordingQuality is not ("Economy" or "Balanced" or "High")) s.RecordingQuality = "Balanced";
        if (s.ScreenshotEditorLayout is not ("A" or "B")) s.ScreenshotEditorLayout = "B";
        if (s.ScreenshotNotificationStyle is not ("Preview" or "Card" or "Capsule")) s.ScreenshotNotificationStyle = "Preview";
        if (s.MessageNotificationStyle is not ("Card" or "Capsule")) s.MessageNotificationStyle = "Capsule";
        s.HideMainWindowFromCapture ??= false;
        s.HiddenCaptureTools = (s.HiddenCaptureTools ?? []).Where(x => x is "Teleprompter" or "Countdown" or "Stopwatch" or "Screen ruler").Distinct().ToArray();
        s.VisibleCaptureFeatures = (s.VisibleCaptureFeatures ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(32).ToArray();
        s.CaptureVisibilityOverrides = (s.CaptureVisibilityOverrides ?? new()).Where(p => !string.IsNullOrWhiteSpace(p.Key)).Take(32).ToDictionary(p => p.Key, p => p.Value);
        if (s.UpdateCheckHours is not (0 or .25 or .5 or 1 or 2 or 3 or 6 or 12 or 24 or 168)) s.UpdateCheckHours = 1;
        s.HomeFavorites = (s.HomeFavorites ?? ["capture", "draw", "record", "notes"]).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(8).ToArray();
        if (!DrawingBindings.Validate(s.DrawingShortcuts, out _)) s.DrawingShortcuts = DrawingBindings.Defaults;
        s.Version = 1;
        s.Language = DesktopTools.Localization.L.Normalize(s.Language);
        s.QuickWheelShortcut = Shortcut(s.QuickWheelShortcut, "Ctrl+Alt+Q");
        if (s.QuickWheelItems == null || s.QuickWheelItems.Length != 8 || s.QuickWheelItems.Any(x => !QuickWheelActions.Available.Contains(x))) s.QuickWheelItems = QuickWheelActions.Defaults;
        s.TeleprompterSpeed = Number(s.TeleprompterSpeed, 10, 180, 40); s.TeleprompterFontSize = Number(s.TeleprompterFontSize, 18, 64, 32);
        s.TeleprompterText ??= ""; if (s.TeleprompterText.Length > 50000) s.TeleprompterText = s.TeleprompterText[..50000];
        s.TeleprompterShortcut = Shortcut(s.TeleprompterShortcut, "Ctrl+Alt+M");
        s.ThemePreset = Known(s.ThemePreset, "Classic", "Ocean", "Forest", "Violet", "Custom");
        s.PrimaryColor = ValidColor(s.PrimaryColor, "#2563EB"); s.BackgroundColor = ValidColor(s.BackgroundColor, "#F3F5FA");
        s.Theme = Known(s.Theme, "System", "Light", "Dark");
        s.MonitorMode = Known(s.MonitorMode, "Cursor", "Primary");
        s.LaserMonitorMode = Known(s.LaserMonitorMode, "All", "Selected");
        s.SpotlightMonitorMode = Known(s.SpotlightMonitorMode, "All", "Selected");
        s.DefaultTool = Known(s.DefaultTool, "Pen", "Highlighter", "Arrow", "Line", "Rectangle", "Ellipse", "Text", "Number", "Select", "Eraser", "Redaction");
        s.CaptureOutput = Known(s.CaptureOutput, "Clipboard", "Save");
        s.CaptureMonitorMode = Known(s.CaptureMonitorMode, "All", "Selected");
        s.CaptureDelaySeconds = new[] { 0, 3, 5, 10 }.Contains(s.CaptureDelaySeconds) ? s.CaptureDelaySeconds : 0;
        s.Color = ValidColor(s.Color, "#FF2870FF"); s.LaserColor = ValidColor(s.LaserColor, "#FFFF4545");
        s.ClickColor = ValidColor(s.ClickColor, "#63D0FF");
        s.ShortcutDisplaySeconds = Number(s.ShortcutDisplaySeconds, .5, 5, 1.6);
        s.ClickSize = Number(s.ClickSize, 24, 128, 64);
        s.ClickDurationMilliseconds = Number(s.ClickDurationMilliseconds, 150, 1500, 450);
        s.CountdownMinutes = Number(s.CountdownMinutes, 1, 120, 5);
        s.RulerLength = Number(s.RulerLength, 100, 1600, 600);
        s.ArrowHeadSize = Number(s.ArrowHeadSize, .5, 3, 1); s.Thickness = Number(s.Thickness, 1, 100, 3); s.Opacity = Number(s.Opacity, 0.05, 1, 1); s.FontSize = Number(s.FontSize, 8, 200, 24);
        s.LaserSize = Number(s.LaserSize, 2, 100, 12); s.LaserFadeSeconds = Number(s.LaserFadeSeconds, 0.1, 5, 0.7);
        s.SpotlightRadius = Number(s.SpotlightRadius, 20, 1000, 120); s.SpotlightDim = Number(s.SpotlightDim, 0.1, 0.95, 0.65);
        if (s.PaletteX.HasValue && !double.IsFinite(s.PaletteX.Value)) s.PaletteX = null;
        if (s.PaletteY.HasValue && !double.IsFinite(s.PaletteY.Value)) s.PaletteY = null;
        s.SaveDirectory ??= "";
        s.WindowPinShortcut = Shortcut(s.WindowPinShortcut, "Ctrl+Alt+T"); s.EyedropperShortcut = Shortcut(s.EyedropperShortcut, "Ctrl+Alt+P");
        s.TranslationShortcut = Shortcut(s.TranslationShortcut, "Ctrl+Alt+R"); s.ScreenTextShortcut = Shortcut(s.ScreenTextShortcut, "Ctrl+Alt+E");
        s.TranslationDirection = Known(s.TranslationDirection, "en-ru", "ru-en");
        if (string.IsNullOrWhiteSpace(s.ScreenTextLanguage) || s.ScreenTextLanguage.Length > 32) s.ScreenTextLanguage = "en-US";
        s.DrawShortcut = Shortcut(s.DrawShortcut, "Ctrl+Alt+D"); s.CaptureShortcut = Shortcut(s.CaptureShortcut, "Ctrl+Alt+S");
        s.HidePaletteShortcut = Shortcut(s.HidePaletteShortcut, "Ctrl+Alt+H"); s.LaserShortcut = Shortcut(s.LaserShortcut, "Ctrl+Alt+L");
        s.SpotlightShortcut = Shortcut(s.SpotlightShortcut, "Ctrl+Alt+O"); s.FreezeShortcut = Shortcut(s.FreezeShortcut, "Ctrl+Alt+F");
        FeatureShortcutCatalog.Normalize(s);
    }
    private static string Known(string? value, params string[] choices) => choices.FirstOrDefault(c => c.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
    private static double Number(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    private static string Shortcut(string? value, string fallback) => string.IsNullOrWhiteSpace(value) || value.Length > 100 ? fallback : value.Trim();
    private static string ValidColor(string? value, string fallback)
    {
        try { return ColorConverter.ConvertFromString(value ?? "") is Color ? value! : fallback; }
        catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException) { return fallback; }
    }
}
