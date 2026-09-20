using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Extras;
using DesktopTools.UI;

internal static class ImageHandlesChecks
{
    private static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root)
    { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj)!;
    private static void Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(obj, args);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static async Task RunAsync()
    {
        using var controller = new AppController(true); controller.UpdateSettings(s => { s.Theme = "Dark"; s.Animations = false; s.Language = "ru"; });
        DesktopTools.Localization.L.Use("ru");
        byte[] pixels = new byte[400 * 200 * 4];
        for (int y = 0; y < 200; y++) for (int x = 0; x < 400; x++)
        { int i = (y * 400 + x) * 4; pixels[i] = (byte)y; pixels[i + 1] = (byte)(x % 256); pixels[i + 2] = 180; pixels[i + 3] = 255; }
        var bitmap = BitmapSource.Create(400, 200, 96, 96, PixelFormats.Bgra32, null, pixels, 1600); bitmap.Freeze();
        var canvas = new ImageEditCanvas { Width = 400, Height = 400 }; canvas.Image.Source = bitmap;
        var helper = new Window { Width = 440, Height = 460, Content = canvas }; helper.Show(); await Task.Delay(40);
        canvas.ResetSelection(); canvas.ShowHandles(true); var handles = canvas.Handles;
        Check(canvas.ImageBounds == new Rect(0, 100, 400, 200), "Letterbox projection is wrong");
        Check(!handles.BeginDrag(new Point(100, 30)), "Letterbox accepted an image gesture");
        Check(handles.BeginDrag(new Point(0, 100)), "Corner handle cannot start"); handles.DragTo(new Point(100, 150)); handles.EndDrag();
        Check(canvas.Selection == new Int32Rect(100, 50, 300, 150), "View coordinates did not map into crop pixels");
        handles.BeginDrag(new Point(200, 200)); handles.DragTo(new Point(0, 100)); handles.EndDrag();
        Check(canvas.Selection.X == 0 && canvas.Selection.Y == 0, "Moving crop escaped image bounds");
        canvas.ResetSelection(); canvas.KeepRatio = true; handles.BeginDrag(new Point(400, 300)); handles.DragTo(new Point(200, 240)); handles.EndDrag();
        Check(Math.Abs(canvas.Selection.Width / (double)canvas.Selection.Height - 2) < .02, "Locked ratio was not preserved");
        var before = canvas.Selection; handles.BeginDrag(handles.Selection.BottomRight); handles.DragTo(new Point(50, 130)); handles.EndDrag(cancel: true);
        Check(canvas.Selection == before, "Canceled resize changed selection"); helper.Close();
        var editor = new ImageToolsWindow(_ => { }); Call(editor, "SetImage", bitmap); editor.Show(); await Task.Delay(50);
        Check(Field<ImageEditCanvas>(editor, "editCanvas").Handles.IsVisible, "Resize handles must be visible when Size opens, before moving the reduction slider");
        foreach (string fieldName in new[] { "width", "height", "cropX", "cropY", "cropWidth", "cropHeight" })
        {
            var input = Field<TextBox>(editor, fieldName); input.ApplyTemplate();
            Check(input.MinHeight >= 36 && double.IsNaN(input.Height), fieldName + ": numeric field has a clipping-prone fixed height");
            var caret = input.GetRectFromCharacterIndex(0);
            if (input.IsVisible) Check(!caret.IsEmpty && caret.Top >= 0 && caret.Bottom <= input.ActualHeight, fieldName + ": numeric text is clipped");
        }
        var zoom = Field<Slider>(editor, "previewZoom"); var scroll = Field<ScrollViewer>(editor, "imageScroll");
        var zoomCanvas = Field<ImageEditCanvas>(editor, "editCanvas");
        zoomCanvas.ResizePreview = false; zoomCanvas.SetSelection(new Int32Rect(100, 50, 200, 100));
        zoom.Value = 2; editor.UpdateLayout(); scroll.ScrollToHorizontalOffset(80); scroll.ScrollToVerticalOffset(40); editor.UpdateLayout();
        Check(zoomCanvas.Selection == new Int32Rect(100, 50, 200, 100) && ReferenceEquals(Field<BitmapSource>(editor, "bitmap"), bitmap), "Preview zoom changed pixels or crop");
        Check(scroll.ScrollableWidth > 0 && scroll.ScrollableHeight > 0, "Zoomed image cannot pan");
        var zoomBounds = zoomCanvas.ImageBounds;
        Check(Math.Abs(zoomCanvas.Handles.Selection.Width / zoomBounds.Width - .5) < .001, "Zoom changed crop coordinate mapping");
        zoomCanvas.ShowHandles(true); zoomCanvas.KeepRatio = false;
        zoomCanvas.Handles.BeginDrag(zoomCanvas.Handles.Selection.BottomRight);
        zoomCanvas.Handles.DragTo(new Point(zoomBounds.Left + zoomBounds.Width, zoomBounds.Top + zoomBounds.Height)); zoomCanvas.Handles.EndDrag();
        Check(zoomCanvas.Selection == new Int32Rect(100, 50, 300, 150), "Zoomed handle mapped to wrong image pixels");
        zoom.Value = 1; zoomCanvas.ResetSelection(); editor.UpdateLayout();
        Walk(editor).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == DesktopTools.Localization.L.T("Crop")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        string resizeWidthBeforeCrop = Field<TextBox>(editor, "width").Text, resizeHeightBeforeCrop = Field<TextBox>(editor, "height").Text;
        var imageCanvas = Field<ImageEditCanvas>(editor, "editCanvas"); imageCanvas.ResizePreview = false; imageCanvas.KeepRatio = false; imageCanvas.ShowHandles(true); var bounds = imageCanvas.ImageBounds;
        imageCanvas.Handles.BeginDrag(bounds.TopLeft); imageCanvas.Handles.DragTo(new Point(bounds.Left + bounds.Width / 4, bounds.Top + bounds.Height / 4)); imageCanvas.Handles.EndDrag();
        Check(ReferenceEquals(Field<BitmapSource>(editor, "bitmap"), bitmap), "Dragging created bitmap history entries");
        Check(Field<TextBox>(editor, "width").Text == resizeWidthBeforeCrop && Field<TextBox>(editor, "height").Text == resizeHeightBeforeCrop, "Crop gesture overwrote resize dimensions");
        Check(Field<TextBox>(editor, "cropWidth").Text == "300" && Field<TextBox>(editor, "cropHeight").Text == "150", "Crop size fields did not follow the crop frame");
        Call(editor, "CropImage"); var cropped = Field<BitmapSource>(editor, "bitmap");
        Check(cropped.PixelWidth == 300 && cropped.PixelHeight == 150 && ScreenshotPixel.Read(cropped, 0, 0) == ScreenshotPixel.Read(bitmap, 100, 50), "Applied crop disagrees with canvas coordinates");
        Field<Button>(editor, "undoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(ReferenceEquals(Field<BitmapSource>(editor, "bitmap"), bitmap), "Crop did not undo as one operation");
        Field<Button>(editor, "redoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(Field<BitmapSource>(editor, "bitmap").PixelWidth == 300, "Crop redo failed");
        Walk(editor).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == DesktopTools.Localization.L.T("Size")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        imageCanvas.ResizePreview = true; imageCanvas.ResetSelection(); bounds = imageCanvas.ImageBounds;
        imageCanvas.Handles.BeginDrag(bounds.BottomRight); imageCanvas.Handles.DragTo(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2)); imageCanvas.Handles.EndDrag();
        Call(editor, "ResizeImage"); Check(Field<BitmapSource>(editor, "bitmap").PixelWidth == 150 && Field<BitmapSource>(editor, "bitmap").PixelHeight == 75, "Mouse resize did not produce requested dimensions");
        Field<Button>(editor, "undoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(Field<BitmapSource>(editor, "bitmap").PixelWidth == 300, "Mouse resize did not undo as one operation");
        Walk(editor).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == DesktopTools.Localization.L.T("Crop")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var ratio = Walk(editor).OfType<ComboBox>().Single(c => c.Tag as string == "crop-ratio"); ratio.SelectedItem = "1:1";
        Check(imageCanvas.Selection == new Int32Rect(75, 0, 150, 150), "Square preset did not center the crop");
        Check(Field<BitmapSource>(editor, "bitmap").PixelWidth == 300 && imageCanvas.KeepRatio, "Preset modified bitmap or did not lock ratio");
        Walk(editor).OfType<Button>().Single(b => Equals(b.Tag, "reset-image-crop")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(imageCanvas.Selection == new Int32Rect(0, 0, 300, 150) && Field<TextBox>(editor, "cropWidth").Text == "300" && Field<TextBox>(editor, "cropHeight").Text == "150", "Reset crop did not restore the full current image");
        ratio.SelectedItem = "1:1";
        Call(editor, "CropImage"); Check(Field<BitmapSource>(editor, "bitmap").PixelWidth == 150 && Field<BitmapSource>(editor, "bitmap").PixelHeight == 150, "Preset crop did not apply");
        Check((string?)ratio.SelectedItem == "Free" && !imageCanvas.KeepRatio, "Applied crop left a stale preset and ratio lock on the reset selection");
        Field<Button>(editor, "undoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check((string?)ratio.SelectedItem == "Free" && imageCanvas.Selection == new Int32Rect(0, 0, 300, 150), "Crop undo restored an image under a mismatched preset");
        Walk(editor).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == DesktopTools.Localization.L.T("Size")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var reduction = Field<Slider>(editor, "reductionSlider"); reduction.Value = 50;
        Check(imageCanvas.Selection == new Int32Rect(0, 0, 150, 75) && Field<BitmapSource>(editor, "bitmap").PixelWidth == 300, $"Reduction preview modified bitmap or dimensions were wrong: selection={imageCanvas.Selection}, bitmap={Field<BitmapSource>(editor, "bitmap").PixelWidth}x{Field<BitmapSource>(editor, "bitmap").PixelHeight}, mode={Field<string>(editor, "activeMode")}");
        editor.Width = 1100; editor.Height = 760; editor.UpdateLayout();
        var transformed = imageCanvas.Image.RenderTransform.TransformBounds(imageCanvas.ImageBounds);
        Check(Math.Abs(transformed.Width - imageCanvas.Handles.Selection.Width) < 1 && Math.Abs(transformed.X - imageCanvas.Handles.Selection.X) < 1, "Resize preview drifted from handles after window resize");
        Call(editor, "ResizeImage"); Check(Field<BitmapSource>(editor, "bitmap").PixelWidth == 150 && reduction.Value == 100, "Reduction did not apply or reset for new image");
        Field<Button>(editor, "undoButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        imageCanvas.ShowHandles(false); editor.Width = editor.MinWidth;
        editor.UpdateLayout(); var render = new RenderTargetBitmap((int)editor.ActualWidth, (int)editor.ActualHeight, 96, 96, PixelFormats.Pbgra32); render.Render(editor);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(render)); using (var output = File.Create("image-reference-Dark.png")) encoder.Save(output);
        foreach (string language in new[] { "ru", "en", "de", "fr", "es" }) foreach (string theme in new[] { "Dark", "Light" })
        {
            DesktopTools.Localization.L.Use(language); controller.Settings.Theme = theme; controller.ApplyTheme();
            var localized = new ImageToolsWindow(_ => { }) { Width = 940, Height = 500 }; Call(localized, "SetImage", bitmap); localized.Show(); await Task.Delay(20);
            try
            {
                foreach (string mode in language == "en" ? new[] { "Size", "Crop", "Format", "Background" } : new[] { "Size", "Crop" })
                {
                    Walk(localized).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == DesktopTools.Localization.L.T(mode)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); localized.UpdateLayout();
                    foreach (var field in Walk(localized).OfType<TextBox>().Where(b => b.IsVisible && b.Text.Length > 0))
                    { var caret = field.GetRectFromCharacterIndex(0); Check(!caret.IsEmpty && caret.Top >= 0 && caret.Bottom <= field.ActualHeight, language + mode + ": numeric text clipped"); }
                    foreach (var button in Walk(localized).OfType<Button>().Where(b => b.IsVisible).ToArray())
                    {
                        button.BringIntoView(); localized.UpdateLayout();
                        var rect = button.TransformToAncestor(localized).TransformBounds(new Rect(button.RenderSize));
                        Check(rect.Left >= -1 && rect.Top >= -1 && rect.Right <= localized.ActualWidth + 1 && rect.Bottom <= localized.ActualHeight + 1,
                            language + mode + ": control unreachable after scrolling: " + System.Windows.Automation.AutomationProperties.GetName(button) + " " + rect);
                    }
                    foreach (var viewer in Walk(localized).OfType<ScrollViewer>()) viewer.ScrollToHome(); await Task.Delay(20); localized.UpdateLayout();
                    var shot = new RenderTargetBitmap((int)localized.ActualWidth, (int)localized.ActualHeight, 96, 96, PixelFormats.Pbgra32); shot.Render(localized);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(shot)); using var output = File.Create($"image-presets-{mode}-{language}-{theme}.png"); png.Save(output);
                }
            }
            finally { localized.Close(); }
        }
        var compareToggle = Field<CheckBox>(editor, "compare");
        Check(!compareToggle.IsEnabled, "Compare was enabled without a matching background-removal pair");
        var removalResult = ImageTransforms.Resize(bitmap, bitmap.PixelWidth, bitmap.PixelHeight);
        typeof(ImageToolsWindow).GetField("backgroundBefore", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(editor, bitmap);
        typeof(ImageToolsWindow).GetField("backgroundAfter", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(editor, removalResult);
        Call(editor, "SetImage", removalResult);
        Check(compareToggle.IsEnabled, "Compare was not enabled for the matching removal result");
        var encodedCopy = ImageTransforms.Resize(removalResult, removalResult.PixelWidth, removalResult.PixelHeight);
        typeof(ImageToolsWindow).GetField("encodedPreview", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(editor, encodedCopy);
        Field<Image>(editor, "preview").Source = encodedCopy;
        compareToggle.IsChecked = true;
        Check(ReferenceEquals(Field<Image>(editor, "preview").Source, removalResult), "Comparison used an encoded export preview instead of the edited image");
        compareToggle.IsChecked = false;
        Check(ReferenceEquals(Field<Image>(editor, "preview").Source, encodedCopy), "Leaving comparison lost the export preview");
        compareToggle.IsChecked = true;
        Check(!imageCanvas.Handles.IsVisible && imageCanvas.IsComparing, "Compare left edit handles active or did not enter comparison mode");
        Call(editor, "SetImage", ImageTransforms.Rotate(removalResult));
        Check(!compareToggle.IsEnabled && !imageCanvas.IsComparing, "Compare survived a transform that no longer aligns with its before image");
        Call(editor, "SetImage", bitmap);
        editor.Close();
    }
}
