using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Native;
using DesktopTools.UI;

internal static class BugRegressionChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static async Task RunAsync()
    {
        var failures = new List<string>();
        async Task Run(string name, Func<Task> run)
        {
            try { await run(); Console.WriteLine("PASS " + name); }
            catch (Exception error) { failures.Add(name + ": " + error.Message); Console.WriteLine("FAIL " + failures[^1]); }
        }
        string original = Environment.CurrentDirectory;
        string isolated = Path.Combine(original, "bug-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolated); Environment.CurrentDirectory = isolated;
        try
        {
            await Run("Wheel follows a manual scroll after its animation finishes", ScrollAfterNavigationAsync);
            await Run("Navigation cancels an unfinished wheel movement", ScrollDuringNavigationAsync);
            await Run("Profiles restore optional bindings and shortcut enable switches", () => { ProfileRoundtrip(); return Task.CompletedTask; });
            await Run("Changing setup appearance does not create a separate native dialog", EmbeddedThemeAsync);
        }
        finally { Environment.CurrentDirectory = original; }
        if (failures.Count != 0) throw new Exception(string.Join(Environment.NewLine, failures));
    }
    private static async Task ScrollAfterNavigationAsync()
    {
        bool before = Motion.AppEnabled; Motion.AppEnabled = true;
        var viewer = new ScrollViewer { Content = new Border { Height = 3000 }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        SmoothScroll.Enable(viewer);
        var window = new Window { Content = viewer, Width = 320, Height = 250 };
        try
        {
            window.Show(); window.UpdateLayout();
            void Wheel() => viewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
            Wheel(); await Task.Delay(350);
            double firstOffset = viewer.VerticalOffset;
            Check(firstOffset > 0, "Initial wheel did not scroll");
            viewer.ScrollToTop(); window.UpdateLayout(); await Task.Delay(40);
            Check(Math.Abs(viewer.VerticalOffset) < 1, "Navigation did not return to top");
            Wheel(); await Task.Delay(350);
            Check(Math.Abs(viewer.VerticalOffset - firstOffset) < 1, "Wheel reused the previous page target: expected " + firstOffset + ", got " + viewer.VerticalOffset);
        }
        finally { window.Close(); Motion.AppEnabled = before; }
    }
    private static async Task ScrollDuringNavigationAsync()
    {
        bool before = Motion.AppEnabled; Motion.AppEnabled = true;
        var viewer = new ScrollViewer { Content = new Border { Height = 3000 } };
        SmoothScroll.Enable(viewer);
        var window = new Window { Content = viewer, Width = 320, Height = 250 };
        try
        {
            window.Show(); window.UpdateLayout();
            viewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
            await Task.Delay(40);
            SmoothScroll.ScrollToOffset(viewer, 0); window.UpdateLayout();
            await Task.Delay(300);
            Check(viewer.VerticalOffset < 1, "Previous page's animation moved the new page to " + viewer.VerticalOffset);
        }
        finally { window.Close(); Motion.AppEnabled = before; }
    }
    private static void ProfileRoundtrip()
    {
        var store = new ProfileStore(Path.Combine(Environment.CurrentDirectory, "profiles"));
        var saved = new AppSettings();
        var capture = FeatureShortcutCatalog.All.Single(e => e.Action == "Capture");
        var recorder = FeatureShortcutCatalog.All.Single(e => e.Action == "Recorder");
        FeatureShortcutCatalog.SetEnabled(saved, capture, false);
        FeatureShortcutCatalog.SetEnabled(saved, recorder, true);
        recorder.Write(saved, "Ctrl+Alt+Shift+G");
        saved.HiddenCaptureTools = ["Teleprompter"];
        saved.CaptureVisibilityOverrides["Screen recorder"] = false;
        saved.CaptureVisibilityOverrides["Notifications"] = true;
        store.Save("Custom shortcuts", saved);
        var restored = ProfileStore.ApplyTo(new AppSettings(), store.Load("Custom shortcuts"));
        Check(!FeatureShortcutCatalog.IsEnabled(restored, capture), "Disabled Capture shortcut was re-enabled");
        Check(FeatureShortcutCatalog.IsEnabled(restored, recorder) && recorder.Read(restored) == "Ctrl+Alt+Shift+G", "Custom recorder shortcut was lost");
        Check(restored.HiddenCaptureTools.SequenceEqual(saved.HiddenCaptureTools), "Individual tool privacy was lost");
        Check(restored.CaptureVisibilityOverrides.Count == 2 &&
            restored.CaptureVisibilityOverrides.TryGetValue("Screen recorder", out bool recorderHidden) && !recorderHidden &&
            restored.CaptureVisibilityOverrides.GetValueOrDefault("Notifications"), "Explicit capture visibility choices were lost");
    }
    private static async Task EmbeddedThemeAsync()
    {
        using var controller = new AppController(true);
        controller.UpdateSettings(s => { s.SharingWelcomeSeen = true; s.Theme = "Dark"; });
        controller.OpenSetup(restart: true); await Task.Delay(50);
        var setup = Application.Current.Windows.OfType<SetupWindow>().Single();
        try
        {
            Check(new WindowInteropHelper(setup).Handle == 0, "Embedded setup already has a native window");
            Check(controller.UpdateSettings(s => s.Theme = "Light"), "Theme preference failed to save");
            Check(new WindowInteropHelper(setup).Handle == 0, "Theme change created a native window for embedded setup");
        }
        finally { setup.Close(); }
    }
}
