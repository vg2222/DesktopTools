using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;

internal static class LocalizationChecks
{
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Render(Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create("locale-" + name + ".png"); encoder.Save(stream);
    }
    public static async Task RunAsync()
    {
        try
        {
            foreach (var language in L.Languages)
            {
                using var controller = new AppController(true);
                L.Use(language); controller.OpenMain();
                var main = (MainWindow)typeof(AppController).GetField("main", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
                foreach (var theme in new[] { "Light", "Dark" })
                {
                    controller.UpdateSettings(s => { s.Theme = theme; s.Animations = false; });
                    main.Width = 800; main.Height = 560;
                    foreach (var page in new[] { "Home", "Draw", "Capture", "Utilities", "Present", "Profiles", "Shortcuts", "Settings", "About", "Click indicators", "Shortcut display", "Stopwatch", "Countdown", "Screen ruler", "Screen blackout" })
                    {
                        main.Navigate(page); await Task.Delay(20); main.UpdateLayout();
                        var text = Descendants(main).OfType<TextBlock>().Select(t => t.Text).ToArray();
                        if (text.Contains("Settings") && language != "en") throw new Exception("Untranslated navigation: " + language);
                        if (text.Any(t => t.Contains('\uFFFD'))) throw new Exception("Broken Unicode: " + language + "/" + page);
                        if (page is "Home" or "Settings" or "Utilities" or "Shortcuts") Render(main, language + "-" + theme + "-" + page);
                    }
                    var combo = Ui.Choice(new[] { "Pen", "Arrow" }, "Pen", _ => { });
                    if (!Equals(combo.SelectedItem, "Pen")) throw new Exception("Translated stored choice");
                    var custom = Ui.Choice(new[] { "Settings" }, "Settings", _ => { }, false);
                    if (custom.ItemTemplate != null) throw new Exception("User profile names translated");
                    var recorder = new ShortcutRecorderWindow("Ctrl+Alt+D", _ => true);
                    recorder.Show(); await Task.Delay(25); Render(recorder, language + "-" + theme + "-recorder"); recorder.Close();
                    var wheel = new QuickWheelWindow(QuickWheelActions.Defaults, _ => true, _ => { });
                    wheel.Show(); await Task.Delay(25); Render(wheel, language + "-" + theme + "-wheel"); wheel.Close();
                    foreach (var utility in new Window[] { new QrCodeWindow(_ => { }), new ImageToolsWindow(_ => { }), new TeleprompterWindow(new AppSettings { TeleprompterText = "Settings — Привет — Bonjour" }, _ => true), new AudioControlsWindow(_ => { }), new FileShelfWindow(_ => { }) })
                    {
                        utility.Show(); await Task.Delay(35); Render(utility, language + "-" + theme + "-" + utility.GetType().Name);
                        if (utility is FileShelfWindow shelf) shelf.Shutdown(); else utility.Close();
                    }
                    using (var notes = new FloatingNotesService(Path.Combine(Path.GetTempPath(), "DesktopTools-locale-notes-" + Guid.NewGuid()), _ => { }))
                    {
                        notes.Show();
                        var manager = (Window)typeof(FloatingNotesService).GetField("manager", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(notes)!;
                        Render(manager, language + "-" + theme + "-notes");
                    }
                    var blackout = new Window { Title = L.T("DesktopTools Blackout"), Tag = AppCapturePrivacy.PresentationSurface };
                    if (AppCapturePrivacy.IsControlWindow(blackout)) throw new Exception("Blackout privacy changed with language");
                    blackout.Close();
                    if (AppController.ClassifyNotification(L.T("Capture failed: ") + "native error") != NotificationKind.Error) throw new Exception("Error color lost");
                    if (AppController.ClassifyNotification(L.F($"{ "Ctrl+Alt+D" }: shortcut is unavailable. { "detail" } Your previous shortcuts are unchanged.")) != NotificationKind.Warning) throw new Exception("Warning color lost");
                }
                main.Hide();
            }
        }
        finally { L.Use("en"); }
    }
}
