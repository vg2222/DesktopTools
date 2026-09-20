using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopTools;
using DesktopTools.UI;

internal static class FoundationChecks
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        controller.UpdateSettings(s => { s.Animations = false; s.Transparency = false; });
        controller.UpdateSettings(s =>
        {
            foreach (var feature in DesktopTools.Core.FeatureAvailability.All.Where(f => f.Id.StartsWith("aid-", StringComparison.Ordinal))) feature.Write(s, false);
        });
        controller.Hud.ToggleClickIndicators(); controller.Hud.ToggleKeystrokes(); controller.Hud.ToggleTimer();
        controller.Hud.ToggleCountdown(); controller.Hud.ToggleRuler(); controller.Hud.ToggleBlackout();
        Check(!controller.Hud.IsClickIndicatorsEnabled && !controller.Hud.IsKeystrokesEnabled && !controller.Hud.IsTimerVisible &&
            !controller.Hud.IsCountdownVisible && !controller.Hud.IsRulerVisible && !controller.Hud.IsBlackoutVisible,
            "Disabled presentation aids started through a direct service call");
        var font = (FontFamily)Application.Current.FindResource("BodyFont");
        var annotationFont = new Typeface(DesktopTools.Core.AnnotationTypography.Resolve(DesktopTools.Core.AnnotationTypography.DefaultFamily),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Check(annotationFont.TryGetGlyphTypeface(out var annotationGlyph) &&
            annotationGlyph.FamilyNames.Values.Any(name => name.StartsWith("Montserrat", StringComparison.Ordinal) || name.StartsWith("Inter", StringComparison.Ordinal)),
            "Annotation default did not resolve to Montserrat or bundled Inter: " + DesktopTools.Core.AnnotationTypography.DefaultFamily +
            " => " + (annotationGlyph == null ? "unresolved" : string.Join(", ", annotationGlyph.FamilyNames.Values)));
        foreach (var weight in new[] { FontWeights.Normal, FontWeights.Medium, FontWeights.SemiBold })
        {
            var typeface = new Typeface(font, FontStyles.Normal, weight, FontStretches.Normal);
            Check(typeface.TryGetGlyphTypeface(out var glyph), "UI font did not resolve");
            Check(glyph.FamilyNames.Values.Any(name => name.StartsWith("Inter", StringComparison.Ordinal)), "UI silently fell back to an installed font");
            foreach (char character in "DesktopTools Привет Ёж Français Español Deutsch")
                Check(glyph.CharacterToGlyphMap.ContainsKey(character), "Bundled font is missing " + character);
        }

        // A styled checkbox outside Ui.Toggle must still show its real state.
        int shortcutSaves = 0;
        var recorder = new ShortcutRecorderWindow("Ctrl+Alt+D", _ => { shortcutSaves++; return false; });
        try
        {
            recorder.Show(); recorder.UpdateLayout();
            var suggestions = Children(recorder).OfType<Button>().Where(b => (b.Tag as string)?.StartsWith("suggest-") == true).ToArray();
            Check(suggestions.Length == 3, "Shortcut recorder did not offer three choices");
            suggestions[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(shortcutSaves == 0, "Selecting a suggested shortcut saved without review");
            Children(recorder).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(shortcutSaves == 1 && recorder.IsVisible, "Failed shortcut commit did not retain the dialog");
        }
        finally { recorder.Close(); }
        var conflictRecorder = new ShortcutRecorderWindow("Ctrl+Alt+D", _ => throw new InvalidOperationException("Conflict was committed"), validate: _ => "Occupied test shortcut");
        try
        {
            conflictRecorder.Show(); conflictRecorder.UpdateLayout();
            conflictRecorder.RecordGesture(System.Windows.Input.Key.K, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt);
            Check(!Children(conflictRecorder).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Save").IsEnabled,
                "Occupied proposal enabled Save");
            Check(Children(conflictRecorder).OfType<TextBlock>().Any(t => t.Text == "Occupied test shortcut"), "Conflict explanation missing");
        }
        finally { conflictRecorder.Close(); }
        var pendingSave = new TaskCompletionSource<bool>();
        var saveDialog = new SaveBeforeDisableDialog(() => pendingSave.Task);
        try
        {
            saveDialog.Show(); saveDialog.UpdateLayout();
            Children(saveDialog).OfType<Button>().Single(b => b.Name == "SaveBeforeDisable").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            saveDialog.Close(); Check(saveDialog.IsVisible && !saveDialog.Saved, "Pending export allowed closing and data loss");
            pendingSave.SetResult(false); await Task.Delay(30);
            Check(saveDialog.IsVisible && !saveDialog.Saved, "Cancelled file picker treated as saved");
            saveDialog.Close();
        }
        finally { saveDialog.Close(); }
        var toggle = new CheckBox { IsChecked = true };
        var window = new Window { Content = toggle, Width = 160, Height = 100, ShowActivated = false };
        try
        {
            window.Show(); window.UpdateLayout(); await Task.Delay(40);
            var thumb = (FrameworkElement)toggle.Template.FindName("Thumb", toggle);
            Check(thumb.RenderTransform is TranslateTransform offset && offset.X == 20, "Checked standalone switch is visually off");
            toggle.IsChecked = false;
            Check(thumb.RenderTransform is TranslateTransform off && off.X == 0 && !off.HasAnimatedProperties, "Reduced-motion switch retained motion or wrong position");
            controller.UpdateSettings(s => s.Animations = true);
            if (Motion.Enabled)
            {
                var panel = new Border { Opacity = .93, RenderTransform = new TranslateTransform(0, 1.25) };
                window.Content = panel; window.UpdateLayout();
                var movement = (TranslateTransform)panel.RenderTransform;
                movement.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(1.25, 0, TimeSpan.FromSeconds(2)));
                panel.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(.93, 1, TimeSpan.FromSeconds(2)));
                await Task.Delay(40);
                double beforeY = movement.Y, beforeOpacity = panel.Opacity;
                Motion.Transition(panel);
                await Task.Delay(15);
                Check(movement.Y >= 0 && movement.Y <= beforeY + .01 && panel.Opacity >= beforeOpacity - .01 && panel.Opacity <= 1,
                    $"Interrupted entrance jumps back: Y {beforeY} -> {movement.Y}, opacity {beforeOpacity} -> {panel.Opacity}");
                Motion.AppEnabled = false;
                Motion.Transition(panel);
                Check(movement.Y == 0 && panel.Opacity == 1 && !movement.HasAnimatedProperties,
                    "Reduced motion did not settle an interrupted entrance");
            }
        }
        finally { window.Close(); }
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Children(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
