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
using DesktopTools.UI;
using DesktopTools.Extras;

internal static class VisualChecks
{
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var item in Descendants(VisualTreeHelper.GetChild(root, i))) yield return item;
    }
    private static RenderTargetBitmap Render(Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(name + ".png"); encoder.Save(stream); return bitmap;
    }
    private static BitmapSource Sample()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(234, 240, 248)), null, new Rect(0, 0, 640, 360));
            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(28, 28, 584, 304), 16, 16);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(37, 99, 235)), null, new Rect(56, 64, 188, 22), 6, 6);
            foreach (int y in new[] { 112, 144, 176 }) dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(213, 220, 232)), null, new Rect(56, y, 480 - (y - 112) * 3, 13), 4, 4);
            dc.DrawLine(new Pen(Brushes.Coral, 5), new Point(80, 250), new Point(490, 215));
        }
        var bitmap = new RenderTargetBitmap(640, 360, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    public static async Task RunAsync()
    {
        using var controller = new AppController(true); DesktopTools.Localization.L.Use("en"); controller.OpenMain();
        var main = Field<MainWindow>(controller, "main");
        Check(string.IsNullOrEmpty(controller.Status), "Initial status must not contain filler copy.");
        try
        {
            foreach (string theme in new[] { "Light", "Dark" })
            {
                controller.Settings.Theme = theme; controller.ApplyTheme();
                foreach (bool compact in new[] { false, true })
                {
                    main.Width = compact ? 800 : 940; main.Height = compact ? 560 : 700;
                    foreach (string page in new[] { "Home", "Draw", "Capture", "Present", "Utilities", "Profiles", "Shortcuts", "Settings", "About", "Click indicators", "Shortcut display", "Stopwatch", "Countdown", "Screen ruler", "Screen blackout", "Laser pointer", "Cursor spotlight", "Freeze frame" })
                    {
                        main.Navigate(page); await Task.Delay(250); main.UpdateLayout();
                        Render(main, $"ui-{page}-{theme}-{(compact ? "compact" : "normal")}");
                        var version = Descendants(main).OfType<TextBlock>().Single(t => t.Name == "AppVersion");
                        var bottom = version.TransformToAncestor(main).Transform(new Point(0, version.ActualHeight)).Y;
                        Check(bottom > main.ActualHeight - 50 && bottom < main.ActualHeight - 10, "Version is not anchored to the window bottom.");
                        Check(!Descendants(main).OfType<TextBlock>().Any(t => t.Text.Contains("Ready when you are")), "Removed copy is still visible.");
                        Check(Descendants(main).OfType<Button>().All(b => b.RenderTransform is not ScaleTransform), "Button scaling returned.");
                        if (page == "Shortcuts")
                        {
                            Check(Descendants(main).OfType<TextBox>().All(t => Equals(t.Tag, "shortcut-search")), "Shortcuts page exposes a binding text editor.");
                            Check(Descendants(main).OfType<Button>().Count(b => b.Content is Border) >= 6, "Shortcut recorder launch buttons are missing.");
                        }
                        if (page == "Draw")
                        {
                            var slider = Descendants(main).OfType<Slider>().First(); double original = slider.Value;
                            Slider.IncreaseSmall.Execute(null, slider); Check(slider.Value > original, "Styled slider lost its command behavior."); slider.Value = original;
                            var scroll = Field<ScrollViewer>(main, "scroller"); scroll.ScrollToBottom(); main.UpdateLayout();
                            Check(scroll.VerticalOffset > 0, "Styled page scroller cannot reach lower settings."); scroll.ScrollToTop();
                        }
                    }
                }
                foreach (var tab in new[] { "Appearance", "Behavior", "Privacy" })
                {
                    typeof(MainWindow).GetField("settingsTab", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, tab); main.Navigate("Settings"); await Task.Delay(280); Render(main, $"ui-settings-{tab}-{theme}");
                }
                var prompterSettings = new DesktopTools.Core.AppSettings { TeleprompterText = string.Join("\n\n", Enumerable.Repeat("DesktopTools helps you present clearly. Read at your own pace.", 25)) };
                var prompter = new TeleprompterWindow(prompterSettings, action => { action(prompterSettings); return true; });
                prompter.Show(); await Task.Delay(280); Render(prompter, $"ui-prompter-edit-{theme}");
                prompter.TogglePlayback(); await Task.Delay(400); Check(prompter.IsRunning, "Prompter failed to start");
                Render(prompter, $"ui-prompter-read-{theme}"); prompter.Pause(); Check(!prompter.IsRunning, "Prompter failed to pause");
                prompter.TogglePlayback(); prompter.Hide(); Check(!prompter.IsRunning, "Hidden prompter continued rendering"); prompter.Close();
                main.Activate(); await Task.Delay(120);
                string? wheelAction=null;
                var wheel=new QuickWheelWindow(DesktopTools.Core.QuickWheelActions.Defaults, _ => true, action => wheelAction=action);
                wheel.Show(); wheel.Activate(); await Task.Delay(280); wheel.Select(2); Render(wheel,$"ui-wheel-{theme}"); wheel.Finish(2);
                Check(wheelAction=="Laser" && !wheel.Polling, "Wheel selection or cleanup failed: action=" + wheelAction + ", visible=" + wheel.IsVisible + ", selected=" + wheel.Selected);
                wheelAction=null;
                var blockedWheel=new QuickWheelWindow(DesktopTools.Core.QuickWheelActions.Defaults, _ => false, action => wheelAction=action);
                blockedWheel.Show(); blockedWheel.Select(0); Check(blockedWheel.Selected==-1,"Disabled wheel action selectable"); blockedWheel.Finish(0); Check(wheelAction==null,"Disabled action executed");
                var qrWindow = new QrCodeWindow(_ => { }); qrWindow.Show();
                Field<TextBox>(qrWindow, "input").Text = "https://example.com/DesktopTools";
                typeof(QrCodeWindow).GetMethod("Generate", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(qrWindow, null);
                await Task.Delay(280); Render(qrWindow, $"ui-qr-{theme}"); qrWindow.Close();
                var imageTools = new ImageToolsWindow(_ => { }); imageTools.Show();
                typeof(ImageToolsWindow).GetMethod("SetImage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(imageTools, new object[] { Sample() });
                await (Task)typeof(ImageToolsWindow).GetMethod("Estimate", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(imageTools, null)!;
                Check(Field<TextBlock>(imageTools, "sizeInfo").Text.StartsWith("Export:"), "Image size estimate failed");
                await Task.Delay(280); Render(imageTools, $"ui-image-tools-{theme}"); imageTools.Close();
                var colorResult = new SampledColorWindow(Colors.Coral, _ => { }); colorResult.Show(); await Task.Delay(280); Render(colorResult, $"ui-sampled-color-{theme}"); colorResult.Close();
                var shelf = new FileShelfWindow(_ => { }); shelf.Show(); await Task.Delay(280); Render(shelf, $"ui-file-shelf-{theme}"); shelf.Shutdown();
                var mixer = new AudioControlsWindow(_ => { }); mixer.Show(); await Task.Delay(280); Render(mixer, $"ui-audio-{theme}"); mixer.Close();
                using (var notes = new FloatingNotesService(Path.Combine(Environment.CurrentDirectory, "visual-notes"), _ => { }))
                {
                    notes.Show(); await Task.Delay(280);
                    var manager = Field<Window>(notes, "manager"); Render(manager, $"ui-notes-{theme}");
                    typeof(FloatingNotesService).GetMethod("Create", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(notes, null);
                    typeof(FloatingNotesService).GetMethod("Open", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(notes, new object[] { Field<System.Collections.Generic.List<FloatingNote>>(notes, "notes").Last() });
                    typeof(FloatingNotesService).GetMethod("Open", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(notes, new object[] { Field<System.Collections.Generic.List<FloatingNote>>(notes, "notes").Last() });
                    var windows = Field<System.Collections.Generic.Dictionary<Guid, Window>>(notes, "windows");
                    var note = windows.Values.Last();
                    Check(!Descendants(note).OfType<TextBlock>().Any(t => t.Text.Contains("drag to move") || t.Text == "Always on top"), "Note controls remain outside options");
                    var options = Descendants(note).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Note options");
                    var pinOption = options.ContextMenu.Items.OfType<MenuItem>().First();
                    pinOption.IsChecked = false; pinOption.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Check(!note.Topmost, "Note pin menu did not update window");
                    pinOption.IsChecked = true; pinOption.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    var editors = Descendants(note).OfType<TextBox>().ToArray(); editors[0].Text = "Meeting notes"; editors[1].Text = "Discuss the design\nReview next steps\nKeep useful ideas close";
                    await Task.Delay(280); Render(note, $"ui-floating-note-{theme}");
                    Check(notes.TryFlush(), "Notes did not save"); note.Close();
                    Check(new NotesStore(Path.Combine(Environment.CurrentDirectory, "visual-notes")).Load().Any(n => n.Title == "Meeting notes"), "Closed note did not persist");
                }
                var sample = Sample();
                var picker = new ColorPickerWindow("#2563EB", _ => true);
                var recorder = new ShortcutRecorderWindow("Ctrl+Alt+D", _ => false); recorder.Show(); await Task.Delay(280); Render(recorder, $"ui-recorder-{theme}"); recorder.Close();
                picker.Show(); await Task.Delay(280); Render(picker, $"ui-color-picker-{theme}");
                var spectrum = Descendants(picker).OfType<ColorSpectrum>().Single();
                spectrum.SetColor(Colors.Lime); Check(spectrum.SelectedColor == Colors.Lime, "Spectrum failed color round trip");
                Check(ColorSpectrum.FromHsv(0, 1, 1) == Colors.Red && ColorSpectrum.FromHsv(240, 1, 1) == Colors.Blue, "Hue conversion failed");
                picker.Close();
                using (var hud = new PresentationHudService(_ => { }))
                {
                    hud.ToggleCountdown(); hud.ToggleRuler(); hud.ToggleTimer();
                    foreach (var window in Application.Current.Windows.Cast<Window>().Where(w => w is CountdownWindow or ScreenRulerWindow || w.Title == "DesktopTools Timer").ToArray())
                    { await Task.Delay(280); Render(window, $"ui-{window.GetType().Name}-{theme}"); }
                }
                var editor = new ScreenshotEditorWindow(sample, _ => { }, _ => { }) { Width = 640, Height = 540 };
                editor.Show(); await Task.Delay(180); Render(editor, $"ui-editor-{theme}"); editor.Close();
                var pin = new PinnedImageWindow(sample, _ => { }); pin.Show(); await Task.Delay(180); Render(pin, $"ui-pin-{theme}");
                Check(pin.WindowStyle == WindowStyle.None && Math.Abs(pin.Width / pin.Height - 640d / 360) < .01, "Pin regained chrome or lost image proportions.");
                pin.Width = 120; pin.Height = 67.5; pin.Activate(); pin.Focus(); await Task.Delay(280);
                Check(Field<Border>(pin, "_toolbar").Visibility == Visibility.Collapsed, "Tiny pin still shows the clipped full toolbar.");
                var compactButton = Field<Button>(pin, "_compact"); Check(compactButton.Visibility == Visibility.Visible, "Tiny pin actions are unreachable.");
                compactButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(280);
                var menu = Field<ContextMenu>(pin, "_menu"); Check(menu.IsOpen && menu.Items.Count >= 8, "Compact pin menu failed to open.");
                pin.Close(); Check(!menu.IsOpen, "Pin left an orphan action menu.");
                typeof(AppController).GetProperty("LastCapture")!.SetValue(controller, sample);
                typeof(AppController).GetMethod("ShowCaptureNotice", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, null);
                var notice = Field<Window>(controller, "captureNotice"); await Task.Delay(180); var toast = Render(notice, $"ui-notice-{theme}");
                byte[] pixel = new byte[4]; toast.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
                Check(notice.AllowsTransparency && pixel[3] == 0, "Notification still has a rectangular backplate at its corner."); notice.Close();
            }
        }
        finally { foreach (Window window in Application.Current.Windows.Cast<Window>().ToArray()) if (window is MainWindow) window.Hide(); else window.Close(); }
    }
}
