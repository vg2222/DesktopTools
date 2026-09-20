namespace DesktopTools.Core;

public sealed class AppSettings
{
    public bool AutomaticUpdateChecks { get; set; } = true;
    public int UpdateCheckHours { get; set; } = 1;
    public bool ClickIndicatorsEnabled { get; set; } = true;
    public bool KeystrokesEnabled { get; set; } = true;
    public bool StopwatchEnabled { get; set; } = true;
    public bool CountdownEnabled { get; set; } = true;
    public bool ScreenRulerEnabled { get; set; } = true;
    public bool ScreenBlackoutEnabled { get; set; } = true;
    public OnboardingProgress Setup { get; set; } = new();
    public int SetupExperienceVersion { get; set; }
    public Dictionary<string, OnboardingProgress> FeatureSetup { get; set; } = new();
    public Dictionary<string, OnboardingProgress> FeatureTours { get; set; } = new();
    public double NotificationSeconds { get; set; } = 6;
    public string ScreenshotNotificationStyle { get; set; } = "Preview";
    public string ScreenshotEditorLayout { get; set; } = "B";
    public string MessageNotificationStyle { get; set; } = "Capsule";
    public string[] HomeFavorites { get; set; } = ["capture", "draw", "record", "notes"];
    public bool ScreenRecorderEnabled { get; set; } = true;
    public bool RecordingRegion { get; set; }
    public bool RecordingMicrophone { get; set; }
    public bool RecordingSystemAudio { get; set; }
    public int RecordingFramesPerSecond { get; set; } = 30;
    public string RecordingQuality { get; set; } = "Balanced";
    public bool RecordingHardwareAcceleration { get; set; } = true;
    public bool VideoMuteOnOpen { get; set; }
    public bool TranslationEnabled { get; set; } = true;
    public bool ScreenTextEnabled { get; set; } = true;
    public string TranslationShortcut { get; set; } = "Ctrl+Alt+R";
    public string ScreenTextShortcut { get; set; } = "Ctrl+Alt+E";
    public string TranslationDirection { get; set; } = "en-ru";
    public string ScreenTextLanguage { get; set; } = "en-US";
    public bool QuickWheelEnabled { get; set; } = true;
    public string QuickWheelShortcut { get; set; } = "Ctrl+Alt+Q";
    public string[] QuickWheelItems { get; set; } = QuickWheelActions.Defaults;
    public bool TeleprompterEnabled { get; set; } = true;
    public string TeleprompterShortcut { get; set; } = "Ctrl+Alt+M";
    public string TeleprompterText { get; set; } = "";
    public double TeleprompterSpeed { get; set; } = 40;
    public double TeleprompterFontSize { get; set; } = 32;
    public bool TeleprompterTopmost { get; set; } = true;
    public bool WindowPinEnabled { get; set; } = true;
    public string WindowPinShortcut { get; set; } = "Ctrl+Alt+T";
    public bool EyedropperEnabled { get; set; } = true;
    public string EyedropperShortcut { get; set; } = "Ctrl+Alt+P";
    public bool QrCodesEnabled { get; set; } = true;
    public bool VideoEditorEnabled { get; set; } = true;
    public bool ImageToolsEnabled { get; set; } = true;
    public bool FileShelfEnabled { get; set; } = true;
    public bool FloatingNotesEnabled { get; set; } = true;
    public bool AudioControlsEnabled { get; set; } = true;
    public bool NotesTopmost { get; set; } = true;
    public bool FileShelfTopmost { get; set; } = true;

    public int Version { get; set; } = 1;
    public bool DrawingEnabled { get; set; } = true;
    public bool CaptureEnabled { get; set; } = true;
    public string ThemePreset { get; set; } = "Classic";
    public string PrimaryColor { get; set; } = "#2563EB";
    public string BackgroundColor { get; set; } = "#F3F5FA";
    public bool UseCustomBackground { get; set; }
    public bool Animations { get; set; } = true;
    public string Theme { get; set; } = "System";
    public string Language { get; set; } = "en";
    public bool Transparency { get; set; } = true;
    public bool StartAtLogin { get; set; } = true;
    public bool TrayHintShown { get; set; }
    public string MonitorMode { get; set; } = "Cursor";
    public string DefaultTool { get; set; } = "Pen";
    public Dictionary<string, string> DrawingShortcuts { get; set; } = DrawingBindings.Defaults;
    public Dictionary<string, string> FeatureShortcuts { get; set; } = FeatureShortcutCatalog.Defaults;
    public Dictionary<string, bool> ShortcutEnabled { get; set; } = new();
    public double ArrowHeadSize { get; set; } = 1;
    public bool FillShapes { get; set; }
    public bool ShapeSnapping { get; set; } = true;
    public bool FreezeRegionBeforeSelection { get; set; } = true;
    public int CaptureDelaySeconds { get; set; }
    public string CaptureMonitorMode { get; set; } = "All";
    public string Color { get; set; } = "#FF2870FF";
    public double Thickness { get; set; } = 3;
    public double Opacity { get; set; } = 1;
    public double FontSize { get; set; } = 24;
    public bool IncludeAnnotations { get; set; } = true;
    public string CaptureOutput { get; set; } = "Clipboard";
    public string SaveDirectory { get; set; } = "";
    public bool? HideMainWindowFromCapture { get; set; } = false;
    public string[] VisibleCaptureFeatures { get; set; } = [];
    public bool HideControlsFromCapture { get; set; }
    public string[] HiddenCaptureTools { get; set; } = [];
    public Dictionary<string, bool> CaptureVisibilityOverrides { get; set; } = new();
    public bool SharingWelcomeSeen { get; set; }
    public bool BlackoutSharingOnly { get; set; }
    public string DrawShortcut { get; set; } = "Ctrl+Alt+D";
    public string CaptureShortcut { get; set; } = "Ctrl+Alt+S";
    public string HidePaletteShortcut { get; set; } = "Ctrl+Alt+H";
    public double? PaletteX { get; set; }
    public double? PaletteY { get; set; }
    public bool LaserEnabled { get; set; } = true;
    public bool SpotlightEnabled { get; set; } = true;
    public bool FreezeEnabled { get; set; } = true;
    public string LaserMonitorMode { get; set; } = "All";
    public string SpotlightMonitorMode { get; set; } = "All";
    public string LaserColor { get; set; } = "#FFFF4545";
    public double LaserSize { get; set; } = 12;
    public double LaserFadeSeconds { get; set; } = 0.7;
    public double SpotlightRadius { get; set; } = 120;
    public double SpotlightDim { get; set; } = 0.65;
    public string LaserShortcut { get; set; } = "Ctrl+Alt+L";
    public string SpotlightShortcut { get; set; } = "Ctrl+Alt+O";
    public string FreezeShortcut { get; set; } = "Ctrl+Alt+F";
    public double ShortcutDisplaySeconds { get; set; } = 1.6;
    public bool ShortcutAnimations { get; set; } = true;
    public string ClickColor { get; set; } = "#63D0FF";
    public double ClickSize { get; set; } = 64;
    public double ClickDurationMilliseconds { get; set; } = 450;
    public double CountdownMinutes { get; set; } = 5;
    public double RulerLength { get; set; } = 600;
}
