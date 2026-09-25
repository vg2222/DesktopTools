using System;
using System.Collections.Generic;
using DesktopTools.Native;
internal static class Program
{
    [STAThread]
    public static void Main()
    {
        int checks = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); checks++; }
        ShortcutRecorderTests.Run(Check);
        var missingRecorderRuntime = RecordingPrerequisites.FindMissingVisualCppRuntimeFiles(
            _ => false,
            "C:/DesktopTools",
            "C:/Windows/System32");
        Check(missingRecorderRuntime.Count == 3
            && missingRecorderRuntime.Contains("vcruntime140.dll", StringComparer.OrdinalIgnoreCase)
            && missingRecorderRuntime.Contains("vcruntime140_1.dll", StringComparer.OrdinalIgnoreCase)
            && missingRecorderRuntime.Contains("msvcp140.dll", StringComparer.OrdinalIgnoreCase),
            "recorder prerequisite check reports required Visual C++ runtime files");
        Check(RecordingPrerequisites.FindMissingVisualCppRuntimeFiles(
            path => path.StartsWith("C:/DesktopTools", StringComparison.OrdinalIgnoreCase),
            "C:/DesktopTools",
            "C:/Windows/System32").Count == 0,
            "recorder prerequisite check accepts app-local Visual C++ runtime files");
        Check(RecordingPrerequisites.VisualCppRuntimeHelpUri.Scheme == Uri.UriSchemeHttps
            && RecordingPrerequisites.VisualCppRuntimeHelpUri.Host.Equals("learn.microsoft.com", StringComparison.OrdinalIgnoreCase)
            && RecordingPrerequisites.VisualCppRuntimeHelpUri.AbsolutePath.Contains("latest-supported-vc-redist", StringComparison.OrdinalIgnoreCase),
            "recorder prerequisite guidance uses the official Microsoft runtime documentation");
        Check(HotkeyGesture.TryParse("Ctrl+Alt+D", out var parsed, out _) && parsed.Modifiers == 3 && parsed.VirtualKey == 68, "parse normal shortcut");
        Check(!HotkeyGesture.TryParse("Ctrl+Ctrl+D", out _, out _), "reject duplicate modifier");
        Check(!HotkeyGesture.TryParse("Ctrl+F12", out _, out _), "reject reserved F12");
        Check(!HotkeyGesture.TryParse("D", out _, out _), "reject bare key");
        var monitors = MonitorService.GetAll();
        Check(monitors.Count > 0, "enumerate physical monitors");
        var monitor = new MonitorInfo("test", new System.Windows.Rect(-3840, -200, 3840, 2160), new System.Windows.Rect(-3840, -200, 3840, 2120), 2, 2);
        var local = new System.Windows.Point(73, 42);
        Check(MonitorService.PhysicalToLocal(MonitorService.LocalToPhysical(local, monitor), monitor) == local, "negative-origin 200% coordinate round-trip");
        using var original = new HotkeyService();
        using var rival = new HotkeyService();
        var bindings = new Dictionary<string, string> { ["Draw"] = "Ctrl+Alt+Shift+F21", ["Capture"] = "Ctrl+Alt+Shift+F22" };
        Check(original.TryReplace(bindings, out string? error), "register initial bindings: " + error);
        Check(!rival.TryReplace(new Dictionary<string, string> { ["Occupied"] = "Ctrl+Alt+Shift+F21" }, out _), "detect process hotkey conflict");
        Check(original.TryReplace(new Dictionary<string, string> { ["Draw"] = "Ctrl+Alt+Shift+F22", ["Capture"] = "Ctrl+Alt+Shift+F21" }, out error), "swap retained bindings: " + error);
        Check(rival.TryReplace(new Dictionary<string, string> { ["Occupied"] = "Ctrl+Alt+Shift+F23" }, out error), "register rival binding: " + error);
        Check(!original.TryValidate(new Dictionary<string, string> { ["Draw"] = "Ctrl+Alt+Shift+F24", ["Capture"] = "Ctrl+Alt+Shift+F23" }, out _), "preflight rejects occupied proposal");
        Check(original.TryValidate(new Dictionary<string, string> { ["Draw"] = "Ctrl+Alt+Shift+F24", ["Capture"] = "Ctrl+Alt+Shift+F21" }, out _), "preflight accepts retained and new combinations");
        Check(original.RegisteredHotkeys["Draw"] == "Ctrl+Alt+Shift+F22", "preflight does not apply bindings");
        Check(!original.TryReplace(new Dictionary<string, string> { ["Draw"] = "Ctrl+Alt+Shift+F24", ["Capture"] = "Ctrl+Alt+Shift+F23" }, out _), "conflicting transaction rejected");
        Check(original.RegisteredHotkeys["Draw"] == "Ctrl+Alt+Shift+F22", "previous mapping preserved");
        Check(!rival.TryReplace(new Dictionary<string, string> { ["Occupied"] = "Ctrl+Alt+Shift+F22" }, out _), "previous OS registration preserved");
        Check(rival.TryReplace(new Dictionary<string, string> { ["Occupied"] = "Ctrl+Alt+Shift+F24" }, out _), "new tentative registration rolled back");
        Check(original.TryReplace(new Dictionary<string, string>(), out _), "release disabled bindings");
        using (var occupied = new HotkeyService())
        using (var fresh = new HotkeyService())
        {
            Check(occupied.TryReplace(new Dictionary<string, string> { ["OtherApp"] = "Ctrl+Alt+Shift+F23" }, out _),
                "reserve one shortcut for startup conflict test");
            var requested = new Dictionary<string, string>
            {
                ["Draw"] = "Ctrl+Alt+Shift+F21",
                ["Capture"] = "Ctrl+Alt+Shift+F23",
                ["Freeze"] = "Ctrl+Alt+Shift+F22"
            };
            var unavailable = fresh.RegisterAvailable(requested);
            Check(unavailable.Count == 1 && unavailable.ContainsKey("Capture"), "only occupied startup shortcut is reported");
            Check(fresh.RegisteredHotkeys.Count == 2 && fresh.RegisteredHotkeys.ContainsKey("Draw") && fresh.RegisteredHotkeys.ContainsKey("Freeze"),
                "available startup shortcuts remain registered");
            Check(occupied.TryReplace(new Dictionary<string, string>(), out _), "release occupied shortcut");
            Check(fresh.RegisterAvailable(requested).Count == 0 && fresh.RegisteredHotkeys.Count == 3,
                "retry activates the formerly occupied shortcut");
        }
        Check(!NativeWindowService.RestoreForeground(IntPtr.Zero), "invalid foreground handle rejected");
        string profileDirectory = System.IO.Path.Combine(Environment.CurrentDirectory, "artifacts", "native-check", "profiles-" + Guid.NewGuid().ToString("N"));
        var profiles = new ProfileStore(profileDirectory);
        Check(profiles.ListNames().Count == 3, "three builtin profiles available");
        bool rejected = false;
        try { profiles.Save("../escape", new DesktopTools.Core.AppSettings()); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "traversal profile name rejected");
        rejected = false;
        try { profiles.Load("CON"); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "reserved profile name rejected");
        var current = new DesktopTools.Core.AppSettings { StartAtLogin = true, Theme = "Dark", Transparency = false, SaveDirectory = "C:/keep", CaptureOutput = "Save", MonitorMode = "Primary", PaletteX = -330, PaletteY = 123 };
        var teaching = profiles.Load("Teaching");
        var applied = ProfileStore.ApplyTo(current, teaching);
        Check(applied.StartAtLogin && applied.Theme == "Dark" && !applied.Transparency && applied.SaveDirectory == "C:/keep" && applied.CaptureOutput == "Save" && applied.MonitorMode == "Primary" && applied.PaletteX == -330 && applied.PaletteY == 123, "profile preserves all general and machine settings");
        Check(applied.Thickness == 5 && current.Thickness == 3, "profile applies tool settings without mutating current");
        profiles.Save("My tools", applied);
        applied.ClickColor = "#FF112233"; applied.ClickSize = 88; applied.ShortcutDisplaySeconds = 3; applied.ShortcutAnimations = false; applied.CountdownMinutes = 12; applied.RulerLength = 450;
        profiles.Save("Presentation", applied);
        var presentation = profiles.Load("Presentation");
        Check(presentation.ClickColor == applied.ClickColor && presentation.ClickSize == 88 && presentation.ShortcutDisplaySeconds == 3 && !presentation.ShortcutAnimations && presentation.CountdownMinutes == 12 && presentation.RulerLength == 450, "profile persists presentation aid preferences");
        profiles.Delete("Presentation");
        string json = System.IO.File.ReadAllText(System.IO.Path.Combine(profileDirectory, "My tools.json"));
        Check(!json.Contains("StartAtLogin") && !json.Contains("Theme") && !json.Contains("SaveDirectory") && !json.Contains("PaletteX"), "profile file excludes general and machine settings");
        Check(profiles.Load("My tools").Thickness == 5, "saved profile roundtrip");
        applied.Thickness = 7;
        profiles.Save("My tools", applied);
        Check(profiles.Load("My tools").Thickness == 7, "atomic profile replacement");
        profiles.Save("Teaching", applied);
        Check(profiles.Load("Teaching").Thickness == 7, "builtin profile override");
        profiles.Delete("Teaching");
        Check(profiles.Load("Teaching").Thickness == 5, "deleting override restores builtin");
        profiles.Delete("My tools");
        Check(profiles.ListNames().Count == 3 && System.IO.Directory.GetFiles(profileDirectory).Length == 0, "delete profile and clean temporary files");
        Check(!NativeWindowService.TryExcludeHandle(IntPtr.Zero, true, out string? affinityError) && !string.IsNullOrWhiteSpace(affinityError), "invalid popup handle reports capture exclusion failure");
        var backdropWindow = new System.Windows.Window();
        try
        {
            backdropWindow.Resources["Surface"] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 30, 40));
            NativeWindowService.ApplyBackdrop(backdropWindow, true, false);
            Check(backdropWindow.Background is System.Windows.Media.SolidColorBrush solid && solid.Color.A == 255 && solid.Color.R == 20, "disabled backdrop restores opaque Surface");
            NativeWindowService.ApplyBackdrop(backdropWindow, true, true);
            NativeWindowService.ApplyBackdrop(backdropWindow, true, false);
            Check(backdropWindow.Background is System.Windows.Media.SolidColorBrush restored && restored.Color.A == 255, "backdrop toggle restores opaque client background");
        }
        finally { backdropWindow.Close(); }
        var canvasWindow = new System.Windows.Window { WindowStyle = System.Windows.WindowStyle.None, AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent };
        NativeWindowService.ApplyBackdrop(canvasWindow, true, true);
        Check(canvasWindow.Background == System.Windows.Media.Brushes.Transparent && new System.Windows.Interop.WindowInteropHelper(canvasWindow).Handle == IntPtr.Zero, "annotation canvas bypasses backdrop and HWND creation");
        canvasWindow.Close();
        System.IO.Directory.Delete(profileDirectory);
        Console.WriteLine($"{checks} native checks passed.");
    }
}
