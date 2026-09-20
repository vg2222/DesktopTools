using DesktopTools.Localization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopTools.Core;

namespace DesktopTools.Native;

/// <summary>Local tool presets; machine and general preferences never travel with a profile.</summary>
public sealed class ProfileStore
{
    private readonly string directory;
    private static readonly string[] Builtins = ["Everyday", "Meetings", "Teaching"];
    private static readonly string[] Fields =
    [
        nameof(AppSettings.ClickIndicatorsEnabled), nameof(AppSettings.KeystrokesEnabled), nameof(AppSettings.StopwatchEnabled), nameof(AppSettings.CountdownEnabled), nameof(AppSettings.ScreenRulerEnabled), nameof(AppSettings.ScreenBlackoutEnabled),
        nameof(AppSettings.RecordingRegion), nameof(AppSettings.RecordingMicrophone), nameof(AppSettings.RecordingSystemAudio),
        nameof(AppSettings.RecordingQuality), nameof(AppSettings.RecordingFramesPerSecond),
        nameof(AppSettings.ShortcutDisplaySeconds), nameof(AppSettings.ShortcutAnimations), nameof(AppSettings.ClickColor),
        nameof(AppSettings.ClickSize), nameof(AppSettings.ClickDurationMilliseconds), nameof(AppSettings.CountdownMinutes), nameof(AppSettings.RulerLength),
        nameof(AppSettings.QuickWheelItems), nameof(AppSettings.QuickWheelShortcut), nameof(AppSettings.TeleprompterSpeed), nameof(AppSettings.TeleprompterFontSize), nameof(AppSettings.TeleprompterTopmost), nameof(AppSettings.TeleprompterShortcut), nameof(AppSettings.WindowPinShortcut), nameof(AppSettings.EyedropperShortcut), nameof(AppSettings.DrawingShortcuts), nameof(AppSettings.DrawingEnabled), nameof(AppSettings.CaptureEnabled),
        nameof(AppSettings.DefaultTool), nameof(AppSettings.Color), nameof(AppSettings.Thickness),
        nameof(AppSettings.ArrowHeadSize), nameof(AppSettings.FillShapes), nameof(AppSettings.ShapeSnapping), nameof(AppSettings.CaptureDelaySeconds), nameof(AppSettings.Opacity), nameof(AppSettings.FontSize), nameof(AppSettings.IncludeAnnotations),
        nameof(AppSettings.HideControlsFromCapture), nameof(AppSettings.BlackoutSharingOnly), nameof(AppSettings.DrawShortcut),
        nameof(AppSettings.FeatureShortcuts), nameof(AppSettings.ShortcutEnabled), nameof(AppSettings.HiddenCaptureTools),
        nameof(AppSettings.CaptureVisibilityOverrides),
        nameof(AppSettings.TranslationShortcut), nameof(AppSettings.ScreenTextShortcut), nameof(AppSettings.TranslationDirection), nameof(AppSettings.ScreenTextLanguage),
        nameof(AppSettings.CaptureShortcut), nameof(AppSettings.HidePaletteShortcut),
        nameof(AppSettings.LaserEnabled), nameof(AppSettings.SpotlightEnabled), nameof(AppSettings.FreezeEnabled),
        nameof(AppSettings.LaserColor), nameof(AppSettings.LaserSize), nameof(AppSettings.LaserFadeSeconds),
        nameof(AppSettings.LaserMonitorMode), nameof(AppSettings.SpotlightMonitorMode),
        nameof(AppSettings.SpotlightRadius), nameof(AppSettings.SpotlightDim),
        nameof(AppSettings.LaserShortcut), nameof(AppSettings.SpotlightShortcut), nameof(AppSettings.FreezeShortcut)
    ];
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public ProfileStore(string? directory = null) => this.directory = Path.GetFullPath(directory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopTools", "profiles"));

    public IReadOnlyList<string> ListNames()
    {
        var names = new HashSet<string>(Builtins, StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(directory))
            foreach (string file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (IsValidName(name)) names.Add(name);
            }
        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public AppSettings Load(string name)
    {
        string path = GetPath(name);
        if (!File.Exists(path)) return Builtin(name);
        JsonObject data = ReadDocument(path);
        var filtered = new JsonObject();
        foreach (string field in Fields)
            if (data.TryGetPropertyValue(field, out JsonNode? value)) filtered[field] = value?.DeepClone();
        AppSettings profile = filtered.Deserialize<AppSettings>() ?? throw new JsonException(L.T("The profile is empty."));
        SettingsStore.Validate(profile);
        return profile;
    }

    public void Save(string name, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string path = GetPath(name);
        if (settings.Version > 1) throw new InvalidOperationException(L.T("Cannot save a profile from a newer DesktopTools version."));
        if (File.Exists(path)) ReadDocument(path);
        AppSettings profile = ApplyTo(new AppSettings(), settings);
        SettingsStore.Validate(profile);
        JsonObject source = JsonSerializer.SerializeToNode(profile)!.AsObject();
        var data = new JsonObject { ["Version"] = 1 };
        foreach (string field in Fields) data[field] = source[field]?.DeepClone();
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $"profile-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, data, Options); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public bool HasSaved(string name) => File.Exists(GetPath(name));
    public static bool IsBuiltin(string name) => Builtins.Contains(name, StringComparer.OrdinalIgnoreCase);
    public void Delete(string name) => File.Delete(GetPath(name));

    public static AppSettings ApplyTo(AppSettings current, AppSettings profile)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(profile);
        JsonObject result = JsonSerializer.SerializeToNode(current)!.AsObject();
        JsonObject preferences = JsonSerializer.SerializeToNode(profile)!.AsObject();
        foreach (string field in Fields) result[field] = preferences[field]?.DeepClone();
        return result.Deserialize<AppSettings>()!;
    }

    private string GetPath(string name)
    {
        if (!IsValidName(name)) throw new ArgumentException(L.T("Use 1–64 letters, numbers, spaces, hyphens or underscores for a profile name; Windows reserved names are not allowed."), nameof(name));
        return Path.Combine(directory, name + ".json");
    }

    private static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name != name.Trim() ||
            name.Any(c => !char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_')) return false;
        string upper = name.ToUpperInvariant();
        if (upper is "CON" or "PRN" or "AUX" or "NUL") return false;
        return !(upper.Length == 4 && (upper.StartsWith("COM", StringComparison.Ordinal) || upper.StartsWith("LPT", StringComparison.Ordinal)) &&
            (char.IsDigit(upper[3]) || upper[3] is '¹' or '²' or '³'));
    }

    private static JsonObject ReadDocument(string path)
    {
        JsonObject data = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new JsonException(L.T("The profile must contain a JSON object."));
        int version = data["Version"]?.GetValue<int>() ?? 1;
        if (version != 1) throw new InvalidOperationException(L.T("This profile uses an unsupported DesktopTools version and has been left unchanged."));
        return data;
    }

    private static AppSettings Builtin(string name)
    {
        if (name.Equals("Everyday", StringComparison.OrdinalIgnoreCase)) return new();
        if (name.Equals("Meetings", StringComparison.OrdinalIgnoreCase)) return new() { DefaultTool = "Arrow", Thickness = 4, Color = "#FF2870FF", LaserFadeSeconds = 0.6, SpotlightRadius = 150, SpotlightDim = 0.6 };
        if (name.Equals("Teaching", StringComparison.OrdinalIgnoreCase)) return new() { DefaultTool = "Pen", Thickness = 5, FontSize = 32, Color = "#FFFFB020", LaserSize = 16, LaserFadeSeconds = 1, SpotlightRadius = 180 };
        throw new FileNotFoundException(L.F($"Profile '{name}' was not found."));
    }
}
