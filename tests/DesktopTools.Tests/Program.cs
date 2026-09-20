using DesktopTools.Core;
using System.IO;
using System.Windows;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var failures = 0;
        var total = 0;
        void Test(string name, Action test) { total++; try { test(); Console.WriteLine($"PASS {name}"); } catch (Exception e) { failures++; Console.WriteLine($"FAIL {name}: {e.Message}"); } }
        void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
        void InStore(Action<SettingsStore, string> action) { var path = Path.Combine(Path.GetTempPath(), "DesktopTools-tests-" + Guid.NewGuid()); Directory.CreateDirectory(path); try { action(new SettingsStore(path), path); } finally { Directory.Delete(path, true); } }

        Test("Onboarding resumes only unfinished work and persists independent progress", () =>
        {
            foreach (var status in Enum.GetValues<SetupStatus>())
            {
                var progress = new OnboardingProgress { Status = status, Step = 2 };
                Check(progress.ShouldResume == (status is SetupStatus.NotStarted or SetupStatus.InProgress));
            }
            InStore((store, path) =>
            {
                var settings = new AppSettings { Setup = new() { Status = SetupStatus.InProgress, Step = 2 }, StartAtLogin = false, CaptureEnabled = false };
                settings.FeatureSetup["record"] = new() { Status = SetupStatus.Skipped };
                settings.FeatureTours["record"] = new() { Status = SetupStatus.InProgress, Step = 1 };
                store.Save(settings); var loaded = store.Load();
                Check(loaded.Setup.ShouldResume && loaded.Setup.Step == 2 && !loaded.StartAtLogin && loaded.CaptureEnabled);
                Check(!loaded.FeatureSetup["record"].ShouldResume && loaded.FeatureTours["record"].ShouldResume && !loaded.FeatureSetup.ContainsKey("images"));
                loaded.Setup.Status = SetupStatus.Skipped; store.Save(loaded); Check(!store.Load().Setup.ShouldResume);
                loaded.Setup.Status = (SetupStatus)999; loaded.Setup.Step = 90; SettingsStore.Validate(loaded);
                Check(loaded.Setup.Status == SetupStatus.NotStarted && loaded.Setup.Step == 3);
            });
        });
        Test("Feature availability preserves independent settings and dependencies", () =>
        {
            var settings = new AppSettings();
            FeatureAvailability.All.Single(f => f.Id == "draw").Write(settings, false);
            Check(!settings.DrawingEnabled && settings.FreezeEnabled && !FeatureAvailability.IsAvailable(settings, "freeze"));
            Check(!FeatureAvailability.IsWheelAvailable(settings, "Draw") && !FeatureAvailability.IsWheelAvailable(settings, "Freeze"));
            settings.CaptureEnabled = false;
            Check(!FeatureAvailability.IsAvailable(settings, "pin") && settings.ScreenTextEnabled);
            settings.TranslationEnabled = false;
            Check(!FeatureAvailability.IsAvailable(settings, "translate") && FeatureAvailability.IsWheelAvailable(settings, "Text tools"));
            foreach (var feature in FeatureAvailability.All)
            {
                feature.Write(settings, false);
                var saved = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
                Check(!feature.Read(saved) && !FeatureAvailability.IsAvailable(saved, feature.Id), "Disabled feature escaped JSON roundtrip: " + feature.Id);
            }
            Check(!FeatureAvailability.IsWheelAvailable(settings, "Text tools") && !FeatureAvailability.IsAvailable(settings, "unknown"));
        });
        Test("Legacy disabled tools migrate without enabling optional shortcuts", () => InStore((store, path) =>
        {
            File.WriteAllText(Path.Combine(path, "settings.json"), "{\"Version\":1,\"ScreenRecorderEnabled\":false,\"DrawingEnabled\":false,\"ImageToolsEnabled\":false}");
            var restored = new SettingsStore(path).Load();
            Check(restored.ScreenRecorderEnabled && restored.DrawingEnabled && restored.ImageToolsEnabled);
            Check(!FeatureShortcutCatalog.IsEnabled(restored, FeatureShortcutCatalog.All.Single(entry => entry.Action == "Recorder")));
        }));
        Test("Notice placement uses physical origins at mixed DPI", () =>
        {
            var work = new Rect(-2560, -240, 2560, 1400);
            var bounds = NoticePlacement.Calculate(work, new Size(460, 120), 1.5, 1.5);
            Check(bounds == new Rect(-1625, -204, 690, 180), bounds.ToString());
            var anchored = NoticePlacement.Calculate(work, new Size(460, 120), 1.5, 1.5, new Point(-10, 1140));
            Check(anchored.Right == work.Right && anchored.Bottom == work.Bottom);
            var oversized = NoticePlacement.Calculate(new Rect(0, 0, 400, 200), new Size(460, 300), 2, 2);
            Check(oversized == new Rect(0, 0, 400, 200));
        });
        Test("Notification styles default, persist and recover", () => InStore((store, path) =>
        {
            var durationSettings = new AppSettings { NotificationSeconds = 12 };
            store.Save(durationSettings); Check(store.Load().NotificationSeconds == 12);
            durationSettings.NotificationSeconds = double.NaN; SettingsStore.Validate(durationSettings);
            Check(durationSettings.NotificationSeconds == 6);
            durationSettings.NotificationSeconds = 1000; SettingsStore.Validate(durationSettings);
            Check(durationSettings.NotificationSeconds == 30);
            var settings = store.Load(); Check(settings.ScreenshotNotificationStyle == "Preview" && settings.MessageNotificationStyle == "Capsule");
            settings.ScreenshotNotificationStyle = "Capsule"; settings.MessageNotificationStyle = "Card"; store.Save(settings);
            var restored = new SettingsStore(path).Load(); Check(restored.ScreenshotNotificationStyle == "Capsule" && restored.MessageNotificationStyle == "Card");
            restored.ScreenshotNotificationStyle = "invalid"; restored.MessageNotificationStyle = "Preview"; SettingsStore.Validate(restored); Check(restored.ScreenshotNotificationStyle == "Preview" && restored.MessageNotificationStyle == "Capsule");
        }));
        Test("Capture privacy migrates and persists independent choices", () => InStore((store, path) =>
        {
            File.WriteAllText(Path.Combine(path, "settings.json"), "{\"HideControlsFromCapture\":false}");
            var settings = store.Load(); Check(settings.HideMainWindowFromCapture == false);
            settings.HideMainWindowFromCapture = true; settings.VisibleCaptureFeatures = ["Floating notes", "Notifications"]; store.Save(settings);
            var restored = new SettingsStore(path).Load(); Check(restored.HideMainWindowFromCapture == true && !restored.HideControlsFromCapture && restored.VisibleCaptureFeatures.SequenceEqual(settings.VisibleCaptureFeatures));
        }));
        Test("Dashboard favorites persist, deduplicate and recover null", () => InStore((store, path) =>
        {
            var settings = store.Load(); settings.HomeFavorites = ["notes", "record", "notes", "", "images"]; SettingsStore.Validate(settings); store.Save(settings);
            Check(new SettingsStore(path).Load().HomeFavorites.SequenceEqual(new[] { "notes", "record", "images" }));
            settings.HomeFavorites = null!; SettingsStore.Validate(settings); Check(settings.HomeFavorites.Length == 4);
        }));
        Test("Video edit ranges, cuts, crop bounds and rotation dimensions", VideoEditTests.Run);
        Test("Translation and screen OCR preferences persist independently", () => InStore((store, path) =>
        {
            var settings = store.Load(); settings.TranslationDirection = "ru-en"; settings.ScreenTextLanguage = "ru-RU";
            settings.TranslationEnabled = false; settings.ScreenTextEnabled = true; settings.CaptureEnabled = false; store.Save(settings);
            var restored = new SettingsStore(path).Load();
            Check(restored.TranslationEnabled && restored.ScreenTextEnabled && restored.CaptureEnabled && restored.TranslationDirection == "ru-en" && restored.ScreenTextLanguage == "ru-RU");
            restored.TranslationDirection = "invalid"; restored.TranslationShortcut = ""; restored.ScreenTextShortcut = ""; SettingsStore.Validate(restored);
            Check(restored.TranslationDirection == "en-ru" && restored.TranslationShortcut == "Ctrl+Alt+R" && restored.ScreenTextShortcut == "Ctrl+Alt+E");
        }));
        Test("Sharing blackout is opt-in and persists", () => InStore((store, path) =>
        {
            var settings = store.Load(); Check(!settings.BlackoutSharingOnly);
            settings.BlackoutSharingOnly = true; store.Save(settings);
            Check(new SettingsStore(path).Load().BlackoutSharingOnly);
        }));
        Test("Translation catalogs, format placeholders and stable settings", LocalizationTests.Run);
        Test("Language preference persists independently of current session", () => InStore((store, path) =>
        {
            var settings = store.Load(); settings.Language = "ru"; store.Save(settings);
            Check(new SettingsStore(path).Load().Language == "ru");
            Check(DesktopTools.Localization.L.Language == "en");
        }));
        Test("Wheel sectors and settings recovery", () =>
        {
            Check(QuickWheelActions.Sector(0,0)==-1 && QuickWheelActions.Sector(300,0)==-1);
            Check(QuickWheelActions.Sector(0,-140)==0 && QuickWheelActions.Sector(140,0)==2 && QuickWheelActions.Sector(0,140)==4 && QuickWheelActions.Sector(-140,0)==6);
            var settings=new AppSettings { QuickWheelItems=["invalid"] }; SettingsStore.Validate(settings); Check(settings.QuickWheelItems.SequenceEqual(QuickWheelActions.Defaults));
        });
        Test("Teleprompter settings persist and validate", () => InStore((store, path) =>
        {
            var settings = store.Load(); settings.TeleprompterText = "Привет\nSecond line"; settings.TeleprompterSpeed = double.NaN; settings.TeleprompterFontSize = 999;
            SettingsStore.Validate(settings); Check(settings.TeleprompterSpeed == 40 && settings.TeleprompterFontSize == 64); store.Save(settings);
            Check(new SettingsStore(path).Load().TeleprompterText == "Привет\nSecond line");
        }));
        Test("Recording quality and FPS persist and validate", () => InStore((store, path) =>
        {
            var settings = store.Load(); settings.RecordingQuality = "High"; settings.RecordingFramesPerSecond = 144; store.Save(settings);
            var loaded = new SettingsStore(path).Load(); Check(loaded.RecordingQuality == "High" && loaded.RecordingFramesPerSecond == 144);
            loaded.RecordingQuality = "unknown"; loaded.RecordingFramesPerSecond = 500; SettingsStore.Validate(loaded);
            Check(loaded.RecordingQuality == "Balanced" && loaded.RecordingFramesPerSecond == 30);
        }));
        Test("Notes persistence, recovery and bounds", NotesStoreTests.Run);
        Test("History restores erased objects and undoable clear", () =>
        {
            var doc = new AnnotationDocument(); var a = new Annotation(); var b = new Annotation { Kind = AnnotationKind.Text, Text = "hello" };
            doc.Add(a); doc.Add(b); doc.Remove(a.Id); Check(doc.Items.Count == 1); doc.Undo(); Check(doc.Items[0].Id == a.Id && doc.Items[1].Id == b.Id);
            doc.Clear(); Check(doc.Items.Count == 0); doc.Undo(); Check(doc.Items.Count == 2); doc.Redo(); Check(doc.Items.Count == 0);
        });
        Test("New action discards redo and reset drops session", () => { var d = new AnnotationDocument(); d.Add(new()); d.Undo(); d.Add(new()); Check(!d.CanRedo); d.Reset(); Check(!d.CanUndo && !d.CanRedo && d.Items.Count == 0); });
        Test("History retains at most 200 operations", () => { var d = new AnnotationDocument(); for (var i = 0; i < 205; i++) d.Add(new()); for (var i = 0; i < 205; i++) d.Undo(); Check(d.Items.Count == 5 && !d.CanUndo); });
        Test("History freezes caller point lists and suppresses no-op events", () => { var d = new AnnotationDocument(); var points = new List<Point> { new(1, 2) }; var events = 0; d.Changed += () => events++; d.Add(new() { Points = points }); points.Clear(); Check(d.Items[0].Points.Count == 1); d.Remove(Guid.NewGuid()); Check(events == 1); });
        Test("Draw toggles without losing previous capture state", () => { var s = new OverlayStateMachine(); Check(s.State == OverlayState.Hidden); s.ToggleDraw(); Check(s.State == OverlayState.Draw); s.ToggleDraw(); Check(s.State == OverlayState.Interact); s.BeginCapture(); s.BeginCapture(); s.ToggleDraw(); Check(s.State == OverlayState.Capture); s.EndCapture(); Check(s.State == OverlayState.Interact); s.ToggleDraw(); s.Hide(); Check(s.State == OverlayState.Hidden); });
        Test("Disable during capture cannot resurrect overlay", () => { var s = new OverlayStateMachine(); s.ToggleDraw(); s.BeginCapture(); s.Disable(); s.EndCapture(); Check(s.State == OverlayState.Hidden); });
        Test("Settings round trip and replacement preserve latest preference", () => InStore((s, p) => { var a = s.Load(); a.Thickness = 7; a.PaletteX = -100; s.Save(a); a.Thickness = 12; s.Save(a); var b = new SettingsStore(p).Load(); Check(b.Thickness == 12 && b.PaletteX == -100); Check(Directory.GetFiles(p, "*.tmp").Length == 0); }));
        Test("Malformed settings are preserved and defaults recovered", () => InStore((s, p) => { File.WriteAllText(Path.Combine(p, "settings.json"), "{broken"); var a = s.Load(); Check(a.DrawingEnabled && s.RecoveryMessage != null); Check(Directory.GetFiles(p).Any(f => File.ReadAllText(f) == "{broken")); s.Save(a); Check(new SettingsStore(p).Load().DrawingEnabled); }));
        Test("Future settings schema is never overwritten", () => InStore((s, p) => { var file = Path.Combine(p, "settings.json"); File.WriteAllText(file, "{\"Version\":999,\"Future\":true}"); s.Load(); var rejected = false; try { s.Save(new()); } catch (InvalidOperationException) { rejected = true; } Check(rejected && File.ReadAllText(file).Contains("Future")); }));
        Test("Future schema with changed field shapes remains untouched", () => InStore((s, p) => { var file = Path.Combine(p, "settings.json"); var json = "{\"Version\":999,\"Thickness\":{\"value\":10}}"; File.WriteAllText(file, json); s.Load(); Check(File.Exists(file) && File.ReadAllText(file) == json); }));
        Test("Save without load protects a future file", () => InStore((s, p) => { var file = Path.Combine(p, "settings.json"); File.WriteAllText(file, "{\"version\":999}"); var rejected = false; try { s.Save(new()); } catch (InvalidOperationException) { rejected = true; } Check(rejected && File.ReadAllText(file).Contains("999")); }));
        Test("Invalid finite ranges and enums recover safely", () => { var s = new AppSettings { Thickness = double.NaN, Opacity = 8, FontSize = -5, Theme = "bad", DefaultTool = "bad", Color = "nonsense", PaletteX = double.PositiveInfinity, SpotlightDim = double.NaN }; SettingsStore.Validate(s); Check(s.Thickness == 3 && s.Opacity == 1 && s.FontSize > 0 && s.Theme == "System" && s.DefaultTool == "Pen" && s.Color == "#FF2870FF" && s.PaletteX == null && double.IsFinite(s.SpotlightDim)); });
        Test("Locked damaged settings recover without crashing or replacing original", () => InStore((store, path) =>
        {
            var file = Path.Combine(path, "settings.json");
            File.WriteAllText(file, "{broken");
            using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var recovered = store.Load();
                Check(recovered.DrawingEnabled && store.RecoveryMessage != null);
                Check(File.ReadAllText(file) == "{broken");
            }
            var refused = false;
            try { store.Save(new()); } catch (InvalidOperationException) { refused = true; }
            Check(refused && File.ReadAllText(file) == "{broken", "Unpreserved damaged settings must not be overwritten");
            store.Load(); store.Save(new());
            Check(new SettingsStore(path).Load().DrawingEnabled);
        }));
        DrawingEditTests.Run(Test, Check);
        DrawingBindingTests.Run(Test, Check);
        RenderingTests.Run(Test, Check);
        Console.WriteLine($"{total - failures}/{total} tests passed");
        return failures == 0 ? 0 : 1;

    }
}
