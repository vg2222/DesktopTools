using System;
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
using DesktopTools.Extras;
using DesktopTools.Localization;

internal static class VideoWindowChecks
{
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Render(Window window, string name)
    {
        window.UpdateLayout(); var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(name + ".png"); encoder.Save(stream);
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root)
    { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var item in Walk(VisualTreeHelper.GetChild(root, i))) yield return item; }
    public static Task RunFrameLayoutAsync()
    {
        var window = new VideoEditorWindow(_ => { }) { Width = 900, Height = 600 };
        try
        {
            window.Show();
            Walk(window).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == L.T("Frame")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            var cropExpander = Field<Expander>(window, "cropExpander");
            var crop = (WrapPanel)((StackPanel)cropExpander.Content).Children[^1];
            var rotation = Field<ComboBox>(window, "rotation");
            var rotationField = (StackPanel)rotation.Parent;
            var rotationRow = (StackPanel)rotationField.Parent;
            var rotateAction = rotationRow.Children.OfType<Button>().Single();
            var cropAt = crop.TranslatePoint(new Point(), window);
            var rotationAt = rotationField.TranslatePoint(new Point(), window);
            Check(Math.Abs(rotationAt.X - cropAt.X) <= 2, "Rotation field is centered instead of aligning with crop fields");
            Check(rotationAt.Y - (cropAt.Y + crop.ActualHeight) <= 24, "Rotation field is separated from crop by an excessive blank gap");
            Check(rotateAction.TranslatePoint(new Point(), window).Y >= rotationAt.Y, "Rotate action is on a separate row");
            rotateAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(rotation.SelectedIndex == 1, "Clockwise rotate action no longer advances the selected angle");
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    }
    public static async Task RunAsync(bool focused = false)
    {
        using var controller = new AppController(true);
        string source = await VideoEditingChecks.FixtureAsync(Path.GetFullPath("video-ui-" + Guid.NewGuid().ToString("N")));
        try
        {
            foreach (var language in focused ? new[] { "en" } : L.Languages)
            foreach (var theme in focused ? new[] { "Light" } : new[] { "Light", "Dark" })
            {
                L.Use(language); controller.UpdateSettings(s => { s.Theme = theme; s.Animations = false; });
                string? error = null; var window = new VideoEditorWindow(message => error = message) { Width = 800, Height = 650 };
                window.Show(); await window.LoadAsync(source);
                Check(!Field<bool>(window, "busy"), "Thumbnail loading blocked video editing");
                await Field<Task>(window, "thumbnailTask").WaitAsync(TimeSpan.FromSeconds(15));
                for (int attempt = 0; attempt < 40 && !Field<bool>(window, "mediaOpened"); attempt++) await Task.Delay(100);
                Check(Field<bool>(window, "mediaOpened"), "Windows playback never opened the fixture");
                Check(error == null, "Video window load: " + error);
                Check(!Field<DispatcherTimer>(window, "playbackTimer").IsEnabled, "Opening video autoplayed");
                var timeline = Field<DesktopTools.UI.VideoTrimTimeline>(window, "timeline");
                Check(Math.Abs(timeline.End - Field<VideoInfo>(window, "source").Duration) < .01, "Timeline did not initialize with source duration");
                Check(timeline.Thumbnails.Count == 10 && timeline.Thumbnails.All(t => t.IsFrozen && t.PixelWidth == 128), "Timeline thumbnails missing or not bounded");
                var firstColor = ScreenshotPixel.Read(timeline.Thumbnails[0], 32, 24); var lastColor = ScreenshotPixel.Read(timeline.Thumbnails[^1], 32, 24);
                Check(firstColor.R > 200 && firstColor.G < 40 && lastColor.G > 200, "Thumbnails do not represent distinct video times");
                window.UpdateLayout();
                foreach (var input in new[] { Field<TextBox>(window, "trimStart"), Field<TextBox>(window, "trimEnd") })
                {
                    var caret = input.GetRectFromCharacterIndex(0);
                    Check(!caret.IsEmpty && caret.Top >= 0 && caret.Bottom <= input.ActualHeight, "Video numeric text is clipped");
                }
                Render(window, "video-ui-" + language + "-" + theme);
                var aspectPreset = Field<ComboBox>(window, "cropRatio");
                Walk(window).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == L.T("Frame")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Expander? cropOptions = null;
                for (DependencyObject? parent = aspectPreset; parent != null; parent = LogicalTreeHelper.GetParent(parent))
                    if (parent is Expander expander) { cropOptions = expander; break; }
                Check(cropOptions != null, "Aspect presets are unreachable from crop options");
                cropOptions!.IsExpanded = true; aspectPreset.SelectedItem = "1:1"; window.Height = window.MinHeight;
                window.UpdateLayout(); aspectPreset.BringIntoView(); await Task.Delay(40);
                Check(Field<DesktopTools.UI.TransformHandles>(window, "cropHandles").DimOutsideSelection, "Video crop does not distinguish the discarded area");
                Render(window, "video-crop-presets-" + language + "-" + theme);
                var presetOrigin = aspectPreset.TranslatePoint(new Point(), window);
                Check(presetOrigin.X >= 0 && presetOrigin.X + aspectPreset.ActualWidth <= window.ActualWidth && presetOrigin.Y >= 0 && presetOrigin.Y + aspectPreset.ActualHeight <= window.ActualHeight, "Crop aspect control clipped in compact localized editor");
                typeof(VideoEditorWindow).GetMethod("Reset", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                cropOptions.IsExpanded = false; window.Height = 650; window.UpdateLayout();
                if (language == "en" && theme == "Light")
                {
                    var ratioField = typeof(VideoEditorWindow).GetField("cropRatio", BindingFlags.Instance | BindingFlags.NonPublic);
                    Check(ratioField != null, "Video crop aspect presets are missing");
                    var ratio = (ComboBox)ratioField!.GetValue(window)!;
                    var ratioHandles = Field<DesktopTools.UI.TransformHandles>(window, "cropHandles");
                    cropOptions.IsExpanded = true; window.UpdateLayout();
                    ratio.SelectedItem = "1:1";
                    Check(Field<TextBox>(window, "cropX").Text == "40" && Field<TextBox>(window, "cropY").Text == "0" && Field<TextBox>(window, "cropWidth").Text == "240" && Field<TextBox>(window, "cropHeight").Text == "240" && ratioHandles.KeepRatio, "Square preset did not center source-pixel crop");
                    await Task.Delay(800);
                    Check(Field<bool>(window, "cropEditing") && !Field<bool>(window, "busy") && !Field<DispatcherTimer>(window, "previewTimer").IsEnabled && ratioHandles.IsVisible,
                        "Preview timer interrupted an unfinished crop");
                    typeof(VideoEditorWindow).GetMethod("CancelCrop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    Check(Field<TextBox>(window, "cropWidth").Text == "320" && !Field<bool>(window, "cropEditing"), "Cancel did not restore the applied crop");
                    ratio.SelectedItem = "1:1";
                    typeof(VideoEditorWindow).GetMethod("ApplyCrop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    Check(!Field<bool>(window, "cropEditing") && Field<DispatcherTimer>(window, "previewTimer").IsEnabled, "Apply crop did not schedule preview");
                    await (Task)typeof(VideoEditorWindow).GetMethod("RenderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object?[] { true, null })!;
                    var square = await VideoEditorService.ProbeAsync(Field<string>(window, "renderedPreview"));
                    Check(square.Width == 240 && square.Height == 240, "Square crop did not reach encoded preview");
                    typeof(VideoEditorWindow).GetMethod("ShowOriginal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    var corner = ratioHandles.Selection.BottomRight;
                    ratioHandles.BeginDrag(corner); ratioHandles.DragTo(new Point(corner.X - 25, corner.Y - 50)); ratioHandles.EndDrag();
                    Check(Math.Abs(int.Parse(Field<TextBox>(window, "cropWidth").Text) - int.Parse(Field<TextBox>(window, "cropHeight").Text)) <= 1, "Crop drag lost locked aspect ratio");
                    Field<TextBox>(window, "cropWidth").Text = "100";
                    Check((string)ratio.SelectedItem == "Free" && !ratioHandles.KeepRatio, "Exact input retained a misleading aspect constraint");
                    ratio.SelectedItem = "9:16";
                    Check(Field<TextBox>(window, "cropWidth").Text == "135" && Field<TextBox>(window, "cropHeight").Text == "240", "Portrait crop preset incorrect");
                    ratio.SelectedItem = "Original";
                    Check(Field<TextBox>(window, "cropWidth").Text == "320" && Field<TextBox>(window, "cropHeight").Text == "240", "Original ratio did not restore full source frame");
                    typeof(VideoEditorWindow).GetMethod("Reset", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    Check((string)ratio.SelectedItem == "Free" && !ratioHandles.KeepRatio, "Reset retained crop constraint");
                    double full = timeline.Duration;
                    Field<CheckBox>(window, "removeSection").IsChecked = true;
                    double oldCut = timeline.CutStart!.Value;
                    Check(timeline.BeginCutDrag(10 + (timeline.ActualWidth - 20) * oldCut / full), "Cut handle was not interactive");
                    timeline.DragTo(10 + (timeline.ActualWidth - 20) * .5 / full); timeline.EndDrag();
                    Check(Math.Abs(double.Parse(Field<TextBox>(window, "cutStart").Text) - .5) < .002, "Cut drag did not update editor");
                    Check(timeline.BeginCutDrag(10 + (timeline.ActualWidth - 20) * timeline.CutEnd / full), "Cut end handle was not interactive");
                    double oldEnd = timeline.CutEnd; timeline.DragTo(timeline.ActualWidth); timeline.EndDrag(cancel: true);
                    Check(Math.Abs(timeline.CutEnd - oldEnd) < .002, "Cancelled cut drag changed range");
                    Field<CheckBox>(window, "removeSection").IsChecked = false;
                    timeline.BeginDrag(10); timeline.DragTo(10 + (timeline.ActualWidth - 20) / 4); timeline.EndDrag();
                    Check(Math.Abs(timeline.Start - full / 4) < .002, "Timeline drag did not set trim start");
                    timeline.BeginDrag(timeline.ActualWidth - 10); timeline.DragTo(-100); timeline.EndDrag(cancel: true);
                    Check(Math.Abs(timeline.End - full) < .002, "Canceled timeline gesture lost end position");
                    var handles = Field<DesktopTools.UI.TransformHandles>(window, "cropHandles"); handles.Visibility = Visibility.Visible; window.UpdateLayout();
                    typeof(VideoEditorWindow).GetMethod("UpdateCropHandles", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    var bounds = handles.Bounds; Check(!bounds.IsEmpty && bounds.Width > 0, "Video crop bounds are missing");
                    handles.BeginDrag(bounds.TopLeft); handles.DragTo(new Point(bounds.Left + bounds.Width / 4, bounds.Top + bounds.Height / 4)); handles.EndDrag();
                    Check(Field<TextBox>(window, "cropX").Text == "80" && Field<TextBox>(window, "cropY").Text == "60", "Video crop did not map source pixels");
                    await (Task)typeof(VideoEditorWindow).GetMethod("RenderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object?[] { true, null })!;
                    var croppedInfo = await VideoEditorService.ProbeAsync(Field<string>(window, "renderedPreview"));
                    Check(croppedInfo.Width == 240 && croppedInfo.Height == 180, "Mouse crop was not applied to encoded video");
                    Field<Slider>(window, "outputPercent").Value = 50;
                    await (Task)typeof(VideoEditorWindow).GetMethod("RenderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object?[] { true, null })!;
                    var reduced = await VideoEditorService.ProbeAsync(Field<string>(window, "renderedPreview"));
                    Check(reduced.Width == 120 && reduced.Height == 90 && Field<TextBox>(window, "cropWidth").Text == "240", "Output reduction changed crop or encoded wrong dimensions");
                    handles.BeginDrag(handles.Selection.BottomRight); handles.DragTo(bounds.BottomRight); handles.EndDrag(cancel: true);
                    typeof(VideoEditorWindow).GetMethod("Reset", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null); handles.Visibility = Visibility.Collapsed;
                    for (int attempt = 0; attempt < 40 && !Field<bool>(window, "mediaOpened"); attempt++) await Task.Delay(100);
                    typeof(VideoEditorWindow).GetMethod("TogglePlayback", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    Check(Field<DispatcherTimer>(window, "playbackTimer").IsEnabled, "Play did not start timer");
                    window.WindowState = WindowState.Minimized; await Task.Delay(30);
                    Check(!Field<DispatcherTimer>(window, "playbackTimer").IsEnabled, "Minimized playback timer active");
                    window.WindowState = WindowState.Normal;
                    Field<TextBox>(window, "trimStart").Text = "0.5"; Field<TextBox>(window, "trimEnd").Text = "2";
                    Check(timeline.Start == .5 && timeline.End == 2, "Typed trim did not update timeline");
                    await (Task)typeof(VideoEditorWindow).GetMethod("RenderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object?[] { true, null })!;
                    Check(error == null, "Preview render: " + error); Check(Field<bool>(window, "showingEdited"), "Edited preview not selected");
                    string preview = Field<string>(window, "renderedPreview"); Check(File.Exists(preview), "Preview missing");
                    Check(Math.Abs((await VideoEditorService.ProbeAsync(preview)).Duration - 1.5) < .12, "Preview did not apply edits");
                    Field<TextBox>(window, "trimEnd").Text = "3";
                    Check(!Field<bool>(window, "showingEdited"), "Changing edit retained stale preview");
                    window.Hide(); Check(!Field<DispatcherTimer>(window, "playbackTimer").IsEnabled, "Hidden playback timer active"); window.Show();
                    window.Close(); await Task.Delay(400); Check(!File.Exists(preview), "Preview file retained after closing");
                }
                else window.Close();
                Check(!Field<DispatcherTimer>(window, "playbackTimer").IsEnabled, "Closed timer active");
            }
            L.Use("en"); controller.OpenVideoEditor(); controller.UpdateSettings(s => FeatureShortcutCatalog.SetEnabled(s, FeatureShortcutCatalog.All.Single(e => e.Action == "Video"), false));
            Check(Application.Current.Windows.OfType<VideoEditorWindow>().Any(), "Disabling editor shortcut closed the editor");
            Application.Current.Windows.OfType<VideoEditorWindow>().Single().Close();
        }
        finally { L.Use("en"); }
    }
}
