using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Localization;

internal static class RecordingQualityChecks
{
    private static System.Collections.Generic.IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        var settings = new AppSettings { RecordingQuality = "Balanced", RecordingFramesPerSecond = 30 };
        int calls = 0; bool allowSave = false;
        bool Save(string quality, int fps, bool hardware) { calls++; if (!allowSave) return false; settings.RecordingQuality = quality; settings.RecordingFramesPerSecond = fps; settings.RecordingHardwareAcceleration = hardware; return true; }
        foreach (var theme in new[] { "Dark", "Light" })
        {
            controller.Settings.Theme = theme; controller.ApplyTheme();
            foreach (var language in new[] { "ru", "en", "de", "fr", "es" })
            {
                L.Use(language); var window = new RecordingQualityWindow(settings, Save); window.Show(); await Task.Delay(40); window.UpdateLayout();
                var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32); image.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var output = File.Create($"record-quality-{language}-{theme}.png")) encoder.Save(output);
                var choices = Children(window).OfType<ComboBox>().ToArray(); choices[0].SelectedIndex = 2; choices[1].SelectedIndex = 5;
                Children(window).OfType<CheckBox>().Single().IsChecked = false; window.Close();
                if (calls != 0 || settings.RecordingQuality != "Balanced" || settings.RecordingFramesPerSecond != 30 || !settings.RecordingHardwareAcceleration) throw new Exception("Closing quality draft changed settings");
            }
        }
        L.Use("en"); var saveWindow = new RecordingQualityWindow(settings, Save); saveWindow.Show(); await Task.Delay(40);
        var edits = Children(saveWindow).OfType<ComboBox>().ToArray(); edits[0].SelectedIndex = 2; edits[1].SelectedIndex = 5;
        Children(saveWindow).OfType<CheckBox>().Single().IsChecked = false;
        var done = Children(saveWindow).OfType<Button>().Single(b => b.Content as string == "Done"); done.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!saveWindow.IsVisible || settings.RecordingQuality != "Balanced" || calls != 1) throw new Exception("Failed save lost the draft");
        allowSave = true; done.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (saveWindow.IsVisible || settings.RecordingQuality != "High" || settings.RecordingFramesPerSecond != 144 || settings.RecordingHardwareAcceleration || calls != 2) throw new Exception("Recording quality options were not saved together");
        if (ScreenRecordingService.QualityValue("Economy") != 50 || ScreenRecordingService.QualityValue("Balanced") != 70 || ScreenRecordingService.QualityValue("High") != 90) throw new Exception("Encoder quality mapping");
        int toggles = 0, stops = 0;
        foreach (var theme in new[] { "Dark", "Light" })
        {
            controller.Settings.Theme = theme; controller.ApplyTheme(); L.Use("ru");
            var hud = new RecordingHudWindow("Экран 1 · 2560 × 1440", () => toggles++, () => stops++, true); hud.Show(); await Task.Delay(40); hud.Update(TimeSpan.FromSeconds(154), false, false); hud.UpdateLayout();
            var image = new RenderTargetBitmap((int)hud.ActualWidth, (int)hud.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(hud);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var output = File.Create($"record-hud-{theme}.png")) encoder.Save(output);
            var buttons = Children(hud).OfType<Button>().ToArray(); int previous = toggles;
            buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); if (toggles != previous + 1) throw new Exception("HUD pause not connected");
            hud.Update(TimeSpan.FromSeconds(154), true, false);
            if (System.Windows.Automation.AutomationProperties.GetName(buttons[0]) != L.T("Resume")) throw new Exception("Paused HUD did not offer resume");
            previous = stops; hud.Close(); if (!hud.IsVisible || stops != previous + 1) throw new Exception("Closing HUD did not request safe stop");
            hud.Update(TimeSpan.FromSeconds(154), true, true); if (buttons.Any(b => b.IsEnabled)) throw new Exception("Finishing HUD allows duplicate commands");
            hud.Finish(); if (hud.IsVisible) throw new Exception("HUD survived completion");
        }
    }
}
