using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.UI;
using DesktopTools.Localization;

internal static class HelpChecks
{
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static Button FindButton(Window window, string tag) => Walk(window).OfType<Button>().Single(b => b.Tag as string == tag);
    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        controller.UpdateSettings(s => { s.ScreenRecorderEnabled = true; s.Animations = false; s.FeatureSetup["recorder"] = new OnboardingProgress { Status = SetupStatus.Completed }; });
        controller.OpenMain();
        var main = (MainWindow)typeof(AppController).GetField("main", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        foreach (var language in L.Languages)
        foreach (var theme in new[] { "Light", "Dark" })
        {
            L.Use(language); controller.UpdateSettings(s => s.Theme = theme); main.Width = 1120; main.Height = 800;
            foreach (string page in new[] { "Help", "About" })
            {
                main.Navigate(page); await Task.Delay(35); main.UpdateLayout();
                var bmp = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32); bmp.Render(main);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp)); using (var file = File.Create($"{page.ToLower()}-{language}-{theme}.png")) png.Save(file);
            }
            main.Navigate("Help"); main.UpdateLayout();
            if (Walk(main).OfType<Button>().Count(b => (b.Tag as string)?.StartsWith("help-start-") == true) != 6) throw new Exception("Missing help cards");
            var search = Walk(main).OfType<TextBox>().Single(t => t.Tag as string == "help-search"); search.Text = L.T("Screen recorder"); main.UpdateLayout();
            if (Walk(main).OfType<Button>().Count(b => (b.Tag as string)?.StartsWith("help-start-") == true) != 1) throw new Exception("Localized help search failed");
            search.Text = "zzzz-not-a-guide"; main.UpdateLayout(); if (Walk(main).OfType<Button>().Any(b => (b.Tag as string)?.StartsWith("help-start-") == true)) throw new Exception("No-results retained cards");
        }
        L.Use("en"); controller.UpdateSettings(s => FeatureShortcutCatalog.SetEnabled(s, FeatureShortcutCatalog.All.Single(e => e.Action == "Recorder"), false)); main.Navigate("Help"); main.UpdateLayout();
        if (!FindButton(main, "help-start-recorder").IsEnabled) throw new Exception("Disabled shortcut blocked the recorder guide");
        int fps = controller.Settings.RecordingFramesPerSecond; bool sawSetup = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) => { var setup = Application.Current.Windows.OfType<FeatureSetupWindow>().FirstOrDefault(); if (setup == null) return; sawSetup = true; timer.Stop(); setup.Close(); };
        timer.Start(); try { FindButton(main, "help-start-recorder").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); } finally { timer.Stop(); }
        if (!sawSetup || controller.Settings.RecordingFramesPerSecond != fps || controller.Settings.FeatureSetup["recorder"].Status != SetupStatus.Completed) throw new Exception("Repeat setup cancellation changed preferences or progress");
        var recorder = Application.Current.Windows.OfType<DesktopTools.Extras.ScreenRecorderWindow>().Single();
        bool skipped = false;
        var skipTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        skipTimer.Tick += (_, _) => { var setup = Application.Current.Windows.OfType<FeatureSetupWindow>().FirstOrDefault(); if (setup == null) return; skipTimer.Stop(); skipped = true; Walk(setup).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Skip setup").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
        skipTimer.Start(); try { controller.OpenFeatureGuide("recorder"); } finally { skipTimer.Stop(); }
        if (!skipped || recorder.GetValue(FeatureTourButton.CurrentTourProperty) is not GuidedTour { IsOpen: true }) throw new Exception("Repeat setup did not enter real feature tour");
        if (Application.Current.Windows.OfType<DesktopTools.Extras.ScreenRecorderWindow>().Count() != 1) throw new Exception("Guide duplicated an open tool");
        recorder.Close();
        main.Width = 860; main.Height = 560; main.Navigate("Help"); main.UpdateLayout();
        var compact = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32); compact.Render(main);
        var compactPng = new PngBitmapEncoder(); compactPng.Frames.Add(BitmapFrame.Create(compact)); using (var output = File.Create("help-compact.png")) compactPng.Save(output);
        main.Navigate("About"); main.UpdateLayout(); bool openedLicense = false;
        var licenseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        licenseTimer.Tick += (_, _) =>
        {
            var window = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.Owner == main && Walk(w).OfType<TextBox>().Any(t => t.Tag as string == "license-text"));
            if (window == null) return;
            licenseTimer.Stop(); var text = Walk(window).OfType<TextBox>().Single(t => t.Tag as string == "license-text"); openedLicense = text.IsReadOnly && text.Text.Contains("Inter 4.1"); window.Close();
        };
        licenseTimer.Start(); try { FindButton(main, "about-components").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); } finally { licenseTimer.Stop(); }
        if (!openedLicense) throw new Exception("Component notices could not be opened from About");
        foreach (string resource in new[] { "DesktopTools.License", "DesktopTools.ThirdPartyNotices" })
        { using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream(resource); if (stream == null || stream.Length < 100) throw new Exception("Bundled license missing"); }
        main.Hide();
    }
}
