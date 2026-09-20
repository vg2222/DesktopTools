using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopTools;
using DesktopTools.UI;

internal static class ToggleAnimationChecks
{
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static async Task RunAsync()
    {
        string original = Environment.CurrentDirectory;
        string isolated = System.IO.Path.Combine(original, "toggle-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(isolated); Environment.CurrentDirectory = isolated;
        try { await RunIsolatedAsync(); }
        finally { Environment.CurrentDirectory = original; }
    }
    private static async Task RunIsolatedAsync()
    {
        using var controller = new AppController(true); controller.UpdateSettings(s => s.Animations = true); controller.OpenMain();
        var main = (MainWindow)typeof(AppController).GetField("main", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        foreach (string page in new[] { "Home", "Utilities", "Present", "Countdown", "Draw", "Capture", "Settings" })
        {
            main.Navigate(page); await Task.Delay(50); main.UpdateLayout();
            var switches = Descendants(main).OfType<CheckBox>().ToArray();
            // Enable/preferences controls only; presentation hook tools are not activated by this test.
            int count = page == "Home" ? 2 : page == "Utilities" ? switches.Length : page == "Present" ? 3 : 1;
            foreach (var toggle in switches.Take(count))
            {
                toggle.BringIntoView(); main.UpdateLayout(); await Task.Delay(20);
                var thumb = (FrameworkElement)toggle.Template.FindName("Thumb", toggle);
                bool original = toggle.IsChecked == true; double start = ((TranslateTransform)thumb.RenderTransform).X;
                toggle.IsChecked = !original;
                Check(Descendants(main).Contains(toggle), page + " replaced clicked toggle: " + controller.Status);
                if (Motion.Enabled) Check(toggle.HasAnimatedProperties, page + " lost animation clock");
                await Task.Delay(65);
                Check(Descendants(main).Contains(toggle), page + " replaced toggle during HUD event");
                double current = ((TranslateTransform)thumb.RenderTransform).X;
                Check(current >= 0 && current <= 20, page + " animation overshot track");
                // Background/occluded WPF windows can throttle composition; wait for the clock to settle.
                for (int attempt = 0; attempt < 60 && Math.Abs(((TranslateTransform)thumb.RenderTransform).X - (original ? 0 : 20)) > .01; attempt++) await Task.Delay(50);
                Check(Math.Abs(((TranslateTransform)thumb.RenderTransform).X - (original ? 0 : 20)) < .01, page + " wrong final thumb position: " + ((TranslateTransform)thumb.RenderTransform).X + "; checked=" + toggle.IsChecked + "; original=" + original);
                toggle.IsChecked = original;
                for (int attempt = 0; attempt < 60 && Math.Abs(((TranslateTransform)thumb.RenderTransform).X - (original ? 20 : 0)) > .01; attempt++) await Task.Delay(50);
            }
        }
        main.Navigate("Countdown"); await Task.Delay(50);
        var active = Descendants(main).OfType<CheckBox>().Single(toggle => System.Windows.Automation.AutomationProperties.GetName(toggle) == "Active");
        controller.Hud.ToggleCountdown(); await Task.Delay(40);
        Check(Descendants(main).Contains(active) && active.IsChecked == true, "External activation replaced toggle or failed synchronization");
        controller.Hud.StopAll(); await Task.Delay(40); Check(active.IsChecked == false, "External close not synchronized");
        controller.UpdateSettings(s => s.Animations = false); active.IsChecked = true;
        var offset = (TranslateTransform)((FrameworkElement)active.Template.FindName("Thumb", active)).RenderTransform;
        Check(!offset.HasAnimatedProperties && offset.X == 20, "Reduced-motion preference ignored");
        controller.Hud.StopAll();
    }
}
