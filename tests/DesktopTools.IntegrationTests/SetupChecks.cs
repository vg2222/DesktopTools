using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.UI;

internal static class SetupChecks
{
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static Window Surface(Window window) => window is SetupWindow ? window.Owner : window;
    private static void Click(Window window, string tag) => Walk(Surface(window)).OfType<Button>().Single(b => (b.Tag as string) == tag).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        controller.UpdateSettings(s => { s.Setup = new(); s.StartAtLogin = false; s.CaptureEnabled = false; s.Animations = false; });
        for (int step = 0; step < 4; step++)
        {
            controller.UpdateSettings(s => { s.Setup.Status = SetupStatus.InProgress; s.Setup.Step = step; });
            controller.OpenSetup(); await Task.Delay(35);
            var window = Application.Current.Windows.OfType<SetupWindow>().Single(); Surface(window).UpdateLayout();
            Check(controller.Settings.CaptureEnabled && !controller.Settings.StartAtLogin, "Setup changed shortcut or login preferences");
            if (step > 0) { Click(window, "setup-back"); Check(controller.Settings.Setup.Step == step - 1, "Back lost progress"); Click(window, "setup-next"); }
            await Task.Delay(40); Surface(window).UpdateLayout();
            var bmp = new RenderTargetBitmap((int)Surface(window).ActualWidth, (int)Surface(window).ActualHeight, 96, 96, PixelFormats.Pbgra32); Surface(window).UpdateLayout(); bmp.Render(Surface(window));
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp)); using (var file = System.IO.File.Create($"setup-step-{step}.png")) png.Save(file);
            window.Close(); Check(controller.Settings.Setup.Status == SetupStatus.InProgress && controller.Settings.Setup.Step == step, "Closing completed setup or lost step");
        }
        controller.OpenSetup(); await Task.Delay(35);
        var last = Application.Current.Windows.OfType<SetupWindow>().Single(); Surface(last).UpdateLayout(); Click(last, "setup-next");
        Check(controller.Settings.Setup.Status == SetupStatus.Completed, "Finish did not persist completion");
        controller.OpenSetup(); Check(!Application.Current.Windows.OfType<SetupWindow>().Any(), "Completed setup reopened automatically");
        controller.OpenSetup(restart: true); await Task.Delay(35);
        var restarted = Application.Current.Windows.OfType<SetupWindow>().Single(); Surface(restarted).UpdateLayout();
        Check(controller.Settings.Setup.Step == 0, "Manual restart did not reset step");
        Click(restarted, "setup-skip"); Check(controller.Settings.Setup.Status == SetupStatus.Skipped, "Set up later did not save progress");
        controller.OpenSetup(); Check(!Application.Current.Windows.OfType<SetupWindow>().Any(), "Skipped setup reopened automatically");
        foreach (string language in DesktopTools.Localization.L.Languages)
        foreach (string theme in new[] { "Light", "Dark" })
        {
            DesktopTools.Localization.L.Use(language);
            controller.UpdateSettings(s => { s.Theme = theme; s.Setup.Step = 1; });
            controller.OpenSetup(restart: true);
            await Task.Delay(35);
            var localized = Application.Current.Windows.OfType<SetupWindow>().Single(); Surface(localized).UpdateLayout();
            Click(localized, "setup-next"); await Task.Delay(35); Surface(localized).UpdateLayout();
            var rendered = new RenderTargetBitmap((int)Surface(localized).ActualWidth, (int)Surface(localized).ActualHeight, 96, 96, PixelFormats.Pbgra32); rendered.Render(Surface(localized));
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(rendered)); using (var file = System.IO.File.Create($"setup-{language}-{theme}.png")) encoder.Save(file);
            foreach (string tag in new[] { "setup-next", "setup-back", "setup-skip" })
            {
                var button = Walk(Surface(localized)).OfType<Button>().Single(b => (b.Tag as string) == tag);
                var bounds = button.TransformToAncestor(Surface(localized)).TransformBounds(new Rect(button.RenderSize));
                Check(bounds.Left >= 0 && bounds.Right <= Surface(localized).ActualWidth && bounds.Bottom <= Surface(localized).ActualHeight, "Localized setup button clipped");
            }
            localized.Close();
        }
        DesktopTools.Localization.L.Use("en");
        string previousDirectory = Environment.CurrentDirectory;
        string freshDirectory = System.IO.Path.Combine(previousDirectory, "fresh-setup-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(freshDirectory);
        try
        {
            Environment.CurrentDirectory = freshDirectory;
            using var fresh = new AppController(true); fresh.OpenSetup(); await Task.Delay(40);
            Check(FeatureAvailability.All.All(f => f.Read(fresh.Settings)), "Fresh setup should leave every tool available");
            Check(!FeatureShortcutCatalog.IsEnabled(fresh.Settings, FeatureShortcutCatalog.All.Single(e => e.Action == "Recorder")), "Enabling the recorder also enabled its optional global shortcut");
            Check(Application.Current.Windows.OfType<SetupWindow>().Count() == 1, "Fresh setup did not open exactly once");
        }
        finally { Environment.CurrentDirectory = previousDirectory; }
        var panel = new StackPanel(); var first = new Button { Content = "First target" }; var second = new Button { Content = "Second target" };
        panel.Children.Add(first); panel.Children.Add(second);
        var tourWindow = new Window { Width = 500, Height = 350, Content = new System.Windows.Documents.AdornerDecorator { Child = panel } };
        try
        {
            tourWindow.Show(); tourWindow.UpdateLayout(); await Task.Delay(40);
            int actions = 0; first.Click += (_, _) => actions++; second.Click += (_, _) => actions++;
            var progress = new OnboardingProgress();
            bool Save(SetupStatus status, int step) { progress.Status = status; progress.Step = step; return true; }
            GuidedTour.Step[] tourSteps = [new(() => first, "First target", "Read the first hint"), new(() => second, "Second target", "Read the next hint")];
            using (var tour = new GuidedTour(tourWindow, tourSteps, 0, Save))
            {
                Check(tour.IsOpen && progress.Status == SetupStatus.InProgress, "Tour did not start");
                tour.Next(); Check(progress.Step == 1 && actions == 0, "Tour lost step or invoked target");
                tourWindow.Left += 20; await Task.Delay(40);
                tour.Next(); Check(progress.Status == SetupStatus.Completed && !tour.IsOpen, "Tour did not complete or clean up");
                Check(System.Windows.Documents.AdornerLayer.GetAdornerLayer(second).GetAdorners(second) == null, "Completed tour retained highlight");
            }
            using (var tour = new GuidedTour(tourWindow, tourSteps, 0, Save))
            {
                tour.Skip(); Check(progress.Status == SetupStatus.Skipped && !tour.IsOpen, "Tour skip not saved");
            }
            using (var tour = new GuidedTour(tourWindow, tourSteps, 0, Save))
            {
                tourWindow.Close(); Check(!tour.IsOpen && progress.Status == SetupStatus.InProgress, "Window close completed tour or retained popup");
            }
        }
        finally { tourWindow.Close(); }
    }
}
