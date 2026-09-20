using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Native;
using DesktopTools.UI;

internal static class InteractionRefinementChecks
{
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static T Field<T>(object item, string name) => (T)item.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(item)!;
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static void Click(Window window, string tag) => Walk(window).OfType<Button>().Single(b => Equals(b.Tag, tag)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Until(Func<bool> ready, string message)
    { for (int i = 0; i < 200 && !ready(); i++) await Task.Delay(100); Check(ready(), message); }
    private static void Render(Window window, string name)
    {
        window.UpdateLayout(); var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(name + ".png"); png.Save(stream);
    }
    internal static async Task RunAsync()
    {
        string original = Environment.CurrentDirectory;
        string isolated = Path.Combine(original, "interaction-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(isolated); Environment.CurrentDirectory = isolated;
        try
        {
            using var controller = new AppController(true);
            controller.UpdateSettings(s => { s.Theme = "Dark"; s.Animations = false; s.SharingWelcomeSeen = true; });
            foreach (string feature in AppCapturePrivacy.DefaultHidden)
                Check(AppCapturePrivacy.ShouldHideFeature(feature, controller.Settings), feature + " is visible by default");
            var drawing = new Window { Tag = AppCapturePrivacy.PresentationSurface };
            Check(!AppCapturePrivacy.ShouldHide(drawing, controller.Settings), "Audience artwork hidden"); drawing.Close();
            Check(controller.Settings.HideMainWindowFromCapture == false, "Main app default changed");
            controller.OpenSetup(restart: true); await Task.Delay(60);
            var main = Application.Current.Windows.OfType<MainWindow>().Single();
            var appearancePicker = new ColorPickerWindow("#2563EB", _ => true) { Owner = main };
            Check(!AppCapturePrivacy.ShouldHide(appearancePicker, controller.Settings), "Appearance picker ignores visible main window"); appearancePicker.Close();
            var drawingOwner = new Window { Tag = AppCapturePrivacy.PresentationSurface }; drawingOwner.Show();
            var drawingPicker = new ColorPickerWindow("#2563EB", _ => true) { Owner = drawingOwner };
            Check(AppCapturePrivacy.ShouldHide(drawingPicker, controller.Settings), "Drawing picker inherited audience visibility");
            Check(AppCapturePrivacy.ShouldHidePopup(drawingOwner, controller.Settings), "Drawing formatting controls inherited audience visibility");
            controller.UpdateSettings(s => s.CaptureVisibilityOverrides["Drawing palette"] = false);
            Check(!AppCapturePrivacy.ShouldHidePopup(drawingOwner, controller.Settings), "Drawing popup ignores visibility override");
            controller.UpdateSettings(s => s.CaptureVisibilityOverrides["Drawing palette"] = true);
            drawingPicker.Close(); drawingOwner.Close();
            for (int i = 0; i < 3; i++) Click(main, "setup-next"); main.UpdateLayout();
            Check(Walk(main).OfType<CheckBox>().Count(c => (c.Tag as string)?.StartsWith("setup-privacy-") == true) == AppCapturePrivacy.FeatureGroups.Length + AppCapturePrivacy.IndividualTools.Length, "Setup privacy choices missing");
            var recorderChoice = Walk(main).OfType<CheckBox>().Single(c => Equals(c.Tag, "setup-privacy-Screen recorder")); recorderChoice.IsChecked = false;
            Check(!AppCapturePrivacy.ShouldHideFeature("Screen recorder", controller.Settings), "Setup choice did not apply"); recorderChoice.IsChecked = true;
            Render(main, Path.Combine(original, "interaction-setup-privacy")); Click(main, "setup-next");
            var loaded = new SettingsStore(Path.Combine(isolated, "artifacts", "smoke-settings")).Load();
            Check(loaded.CaptureVisibilityOverrides.GetValueOrDefault("Screen recorder"), "Capture choice not persisted");
            main.Navigate("Capture tools"); main.UpdateLayout();
            Check(Walk(main).OfType<TextBlock>().Any(t => Equals(t.Tag, "category-breadcrumb")), "Home breadcrumb missing"); Render(main, Path.Combine(original, "interaction-category"));

            var recorder = new ScreenRecorderWindow(controller); recorder.Show(); AppCapturePrivacy.Apply(recorder, controller.Settings);
            try
            {
                Check(GetWindowDisplayAffinity(new WindowInteropHelper(recorder).Handle, out uint affinity) && affinity == 0x11, "Recorder default exclusion not applied");
                Click(recorder, "recording-source-preview"); await Task.Delay(80);
                Check(Application.Current.Windows.OfType<RecordingSourcePickerWindow>().Count() == 1, "Preview click did not open source chooser");
                Application.Current.Windows.OfType<RecordingSourcePickerWindow>().Single().Close(); await Task.Delay(50);
                Render(recorder, Path.Combine(original, "interaction-recorder"));
            }
            finally { recorder.Close(); }
            var hud = new RecordingHudWindow("Test display", () => { }, () => { }, AppCapturePrivacy.ShouldHideFeature("Screen recorder", controller.Settings)); hud.Show();
            try
            {
                Check(GetWindowDisplayAffinity(new WindowInteropHelper(hud).Handle, out uint affinity) && affinity == 0x11, "Recording HUD leaks by default");
                controller.UpdateSettings(s => s.CaptureVisibilityOverrides["Screen recorder"] = false);
                Check(GetWindowDisplayAffinity(new WindowInteropHelper(hud).Handle, out affinity) && affinity == 0, "Open recording HUD did not accept visibility override");
            }
            finally { hud.Finish(); }
            var text = new TextToolsWindow(controller); text.Show();
            try
            {
                var tab = Walk(text).OfType<RadioButton>().Single(r => Equals(r.Tag, "text-mode-screenshot")); tab.IsChecked = true;
                Check(Field<bool>(text, "ocrMode"), "Labeled screenshot tab did not switch modes"); Render(text, Path.Combine(original, "interaction-text-tabs"));
            }
            finally { text.Close(); }
            var image = new ImageToolsWindow(_ => { }); image.Show(); Render(image, Path.Combine(original, "interaction-image-import")); image.Close();
            await AutoPreviewAsync(original);
            Console.WriteLine("PASS privacy defaults/overrides/native HUD, setup choices, source chooser, import affordances, tabs and automatic video preview");
        }
        finally { Environment.CurrentDirectory = original; }
    }
    private static async Task AutoPreviewAsync(string output)
    {
        string fixture = await VideoEditingChecks.FixtureAsync(Path.GetFullPath("video")); byte[] before = SHA256.HashData(File.ReadAllBytes(fixture));
        string? error = null; var editor = new VideoEditorWindow(message => error = message); editor.Show();
        try
        {
            Render(editor, Path.Combine(output, "interaction-video-import")); await editor.LoadAsync(fixture);
            Field<TextBox>(editor, "trimStart").Text = ".5"; Field<TextBox>(editor, "trimEnd").Text = "2";
            Check(Field<TextBlock>(editor, "previewLabel").Text == "Preview pending", "Stale source is not labeled while edits wait");
            await Until(() => Field<bool>(editor, "showingEdited") && !Field<bool>(editor, "busy"), "Automatic preview did not render: " + error);
            string preview = Field<string>(editor, "renderedPreview");
            Check(Math.Abs((await VideoEditorService.ProbeAsync(preview)).Duration - 1.5) < .12, "Auto preview uses stale edits");
            Check(!Field<DispatcherTimer>(editor, "playbackTimer").IsEnabled, "Auto preview autoplayed");
            Field<TextBox>(editor, "trimEnd").Text = "3";
            await Until(() => Field<bool>(editor, "previewRendering"), "Second preview did not start");
            Field<TextBox>(editor, "trimEnd").Text = "2.5";
            await Until(() => Field<bool>(editor, "showingEdited") && !Field<bool>(editor, "busy"), "Interrupted preview did not recover: " + error);
            Check(Math.Abs((await VideoEditorService.ProbeAsync(Field<string>(editor, "renderedPreview"))).Duration - 2) < .12, "An outdated render replaced the newest edit");
            Render(editor, Path.Combine(output, "interaction-video-preview"));
            Field<TextBox>(editor, "trimEnd").Text = "3"; editor.Hide();
            Check(!Field<DispatcherTimer>(editor, "previewTimer").IsEnabled, "Hidden editor keeps rendering");
            Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(fixture))), "Video source file was changed");
        }
        finally { editor.Close(); }
    }
}
