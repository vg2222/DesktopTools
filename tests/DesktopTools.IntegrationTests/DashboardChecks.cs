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
using DesktopTools.Localization;
internal static class DashboardChecks
{
    private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o)!;
    private static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root)
    { yield return root; for (int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) foreach(var child in Walk(VisualTreeHelper.GetChild(root,i))) yield return child; }
    private static void Check(bool value, string message) { if(!value) throw new Exception(message); }
    public static async Task EnableToolsAsync()
    {
        L.Use("en");
        using var controller = new AppController(true);
        Check(controller.UpdateSettings(s =>
        {
            foreach (var feature in DesktopTools.Core.FeatureAvailability.All) feature.Write(s, false);
            s.Animations = false;
        }), "Could not migrate legacy tool settings");
        Check(DesktopTools.Core.FeatureAvailability.All.All(f => f.Read(controller.Settings)), "Legacy disabled tools remained unavailable");
        controller.OpenMain();
        var main = Field<MainWindow>(controller, "main");
        try
        {
            foreach (var group in new[] { "Presentation tools", "Capture tools", "Media tools", "Text tools", "Desktop utilities" })
            {
                main.Navigate(group); main.UpdateLayout();
                Check(!Walk(main).OfType<Button>().Any(b => (b.Tag as string)?.StartsWith("enable-") == true), "Category still has tool enable controls");
            }
            main.Navigate("Capture tools"); main.UpdateLayout();
            Check(!Walk(main).OfType<Button>().Single(b => (b.Tag as string) == "launch-pin").IsEnabled,
                "Pin without a capture became launchable");
            var recorderShortcut = DesktopTools.Core.FeatureShortcutCatalog.All.Single(e => e.Action == "Recorder");
            controller.UpdateSettings(s => DesktopTools.Core.FeatureShortcutCatalog.SetEnabled(s, recorderShortcut, false));
            main.Navigate("Home"); main.UpdateLayout();
            Field<TextBox>(main, "dashboardSearch").Text = L.T("Screen recorder"); main.UpdateLayout();
            Check(Walk(main).OfType<Button>().Single(b => b.IsVisible && (b.Tag as string) == "launch-record").IsEnabled, "Shortcut toggle disabled recorder launch");
            var saved = new DesktopTools.Core.SettingsStore(Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke-settings")).Load();
            Check(DesktopTools.Core.FeatureAvailability.All.All(f => f.Read(saved)), "Available tools were not persisted");
            Check(!DesktopTools.Core.FeatureShortcutCatalog.IsEnabled(saved, recorderShortcut), "Disabled shortcut was not persisted");
            foreach (var (detail, firstSetting) in new[]
            {
                ("Laser pointer", "Color"), ("Cursor spotlight", "Displays"), ("Freeze frame", "Toggle frozen desktop"),
                ("Click indicators", "Active"), ("Shortcut display", "Active"), ("Stopwatch", "Active"),
                ("Countdown", "Active"), ("Screen ruler", "Active"), ("Screen blackout", "Active")
            })
            {
                main.Navigate(detail); main.UpdateLayout();
                var shortcut = Walk(main).OfType<CheckBox>().FirstOrDefault(b => (b.Tag as string)?.StartsWith("shortcut-enabled-") == true);
                var setting = Walk(main).OfType<TextBlock>().FirstOrDefault(t => t.Text == L.T(firstSetting));
                Check(shortcut != null && setting != null, "Missing shortcut or first setting: " + detail +
                    "; tags=" + string.Join(",", Walk(main).OfType<CheckBox>().Select(b => b.Tag)) +
                    "; labels=" + string.Join(",", Walk(main).OfType<TextBlock>().Select(t => t.Text).Take(18)));
                Check(shortcut!.TransformToAncestor(main).Transform(new Point(0, 0)).Y <
                    setting!.TransformToAncestor(main).Transform(new Point(0, 0)).Y, "Shortcut is below feature settings: " + detail);
                Walk(main).OfType<Button>().Single(b => Equals(b.Content, L.T("‹  Presentation tools")))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                main.UpdateLayout();
                Check(Field<string>(main, "currentPage") == "Presentation tools", "Back opened the legacy Present page: " + detail);
            }
            await Task.Delay(30);
        }
        finally { main.Close(); }
    }
    public static async Task RunAsync()
    {
        using var controller = new AppController(true);
        controller.UpdateSettings(s => { s.HomeFavorites = ["capture", "draw", "record", "notes"]; s.CaptureEnabled = s.ScreenRecorderEnabled = s.DrawingEnabled = s.FloatingNotesEnabled = true; });
        controller.OpenMain();
        var main = Field<MainWindow>(controller,"main");
        try
        {
            var privacy = new DesktopTools.Core.AppSettings { HideMainWindowFromCapture = false, HideControlsFromCapture = true, VisibleCaptureFeatures = ["Floating notes"] };
            var note = new Window { Tag = "Floating notes" }; var other = new Window();
            Check(!DesktopTools.Native.AppCapturePrivacy.ShouldHide(main, privacy), "Main privacy follows feature setting");
            Check(!DesktopTools.Native.AppCapturePrivacy.ShouldHide(note, privacy) && DesktopTools.Native.AppCapturePrivacy.ShouldHide(other, privacy), "Per-feature privacy ignored");
            privacy.HideMainWindowFromCapture = true; privacy.HideControlsFromCapture = false;
            Check(DesktopTools.Native.AppCapturePrivacy.ShouldHide(main, privacy) && !DesktopTools.Native.AppCapturePrivacy.ShouldHide(other, privacy), "Main and feature privacy not independent");
            note.Close(); other.Close();

            foreach(var language in L.Languages)
            foreach(var theme in new[]{"Light","Dark"})
            {
                L.Use(language); controller.UpdateSettings(s=>{s.Theme=theme;s.Animations=false;});
                main.Width=1120;main.Height=800;main.Navigate("Home"); await Task.Delay(70);main.UpdateLayout();
                Check(!Walk(main).OfType<CheckBox>().Any(),"Home remains a settings wall");
                Check(Walk(main).OfType<Button>().Count(b=>(b.Tag as string)?.StartsWith("launch-")==true)==4,"Default favorites missing");
                var bmp=new RenderTargetBitmap((int)main.ActualWidth,(int)main.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(main);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create("home-a-"+language+"-"+theme+".png"))png.Save(f);
                var search=Field<TextBox>(main,"dashboardSearch");search.Text=L.T("Screen recorder");main.UpdateLayout();
                var result=Walk(main).OfType<Button>().FirstOrDefault(b=>(b.Tag as string)=="launch-record");Check(result!=null,"Localized search missing recorder");
                search.Text="zzzz-no-match";main.UpdateLayout();Check(Walk(main).OfType<TextBlock>().Any(t=>t.Text==L.T("No tools found. Try another name.")),"No search empty state");
                main.Navigate("Text tools");main.UpdateLayout();Check(Walk(main).OfType<Button>().Any(b=>(b.Tag as string)=="launch-ocr"),"OCR missing in text category");
            }
            L.Use("en"); main.Navigate("Home"); main.UpdateLayout();
            var mediaFilter = Walk(main).OfType<Button>().SingleOrDefault(b => (b.Tag as string) == "filter-Media tools");
            Check(mediaFilter != null, "Home category filters missing");
            mediaFilter!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); main.UpdateLayout();
            var filtered = Walk(main).OfType<Button>().Where(b => b.IsVisible && (b.Tag as string)?.StartsWith("launch-") == true).ToArray();
            Check(filtered.Length == 3 && filtered.Any(b => (b.Tag as string) == "launch-images") && filtered.Any(b => (b.Tag as string) == "launch-video"), "Category filter must show images, video and eyedropper: " + string.Join(", ", filtered.Select(b => b.Tag)));
            Field<TextBox>(main, "dashboardSearch").Text = "recorder"; main.UpdateLayout();
            Check(!Walk(main).OfType<Button>().Any(b => b.IsVisible && (b.Tag as string) == "launch-record"), "Search escaped selected category");
            main.Navigate("Capture tools");main.UpdateLayout();
            main.Navigate("Utilities"); main.UpdateLayout();
            Check(!Walk(main).OfType<CheckBox>().Any(b => (b.Tag as string)?.StartsWith("available-") == true), "Feature settings still have availability switches");
            Check(Walk(main).OfType<CheckBox>().Any(b => (b.Tag as string) == "shortcut-enabled-Recorder"), "Recorder settings lack shortcut switch");
            Check(controller.Settings.ScreenRecorderEnabled, "Recorder unavailable after migration");
            main.Navigate("Capture tools"); main.UpdateLayout();
            main.Navigate("Shortcuts"); main.UpdateLayout();
            Check(Walk(main).OfType<TextBox>().Any(t => (t.Tag as string) == "shortcut-search"), "Shortcut search missing");
            var drawingTab = Walk(main).OfType<Button>().SingleOrDefault(b => (b.Tag as string) == "shortcut-tab-drawing");
            Check(drawingTab != null, "Drawing shortcut tab missing");
            drawingTab!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); main.UpdateLayout();
            var shortcutSearch = Walk(main).OfType<TextBox>().Single(t => (t.Tag as string) == "shortcut-search");
            shortcutSearch.Text = "Finish text"; main.UpdateLayout();
            Check(Walk(main).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "Finish text") &&
                !Walk(main).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "Draw / interact"), "Shortcut tab/search mixed scopes");
            main.Navigate("Capture tools"); main.UpdateLayout();
            var star=Walk(main).OfType<Button>().First(b=>(b.Tag as string)=="favorite-record");star.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!controller.Settings.HomeFavorites.Contains("record"),"Unpin failed");star.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(controller.Settings.HomeFavorites.Contains("record"),"Pin failed");
            controller.UpdateSettings(s=>DesktopTools.Core.FeatureShortcutCatalog.SetEnabled(s, DesktopTools.Core.FeatureShortcutCatalog.All.Single(e=>e.Action=="Recorder"), false));main.Navigate("Home");main.UpdateLayout();Check(Walk(main).OfType<Button>().First(b=>(b.Tag as string)=="launch-record").IsEnabled,"Shortcut disabled feature launch");
            foreach(var destination in new[]{"Capture tools","Presentation tools","Media tools","Text tools","Desktop utilities","Draw","Capture","Present","Utilities","Profiles","Shortcuts","Settings","About"}){main.Navigate(destination); main.UpdateLayout();}
            foreach (string theme in new[] { "Light", "Dark" })
            {
                L.Use("ru"); controller.UpdateSettings(s => s.Theme = theme);
                foreach (string destination in new[] { "Capture tools", "Presentation tools", "Media tools", "Text tools", "Desktop utilities", "Profiles", "Settings", "Shortcuts", "Draw", "Capture", "About" })
                {
                    main.Navigate(destination); main.UpdateLayout();
                    var rendered = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96,96,PixelFormats.Pbgra32); rendered.Render(main);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(rendered)); using var file = File.Create("pages-a-" + destination + "-" + theme + ".png"); encoder.Save(file);
                    if(destination == "Text tools") Check(Walk(main).OfType<FrameworkElement>().Where(e => (e.Tag as string) == "tool-artwork").All(e => e.ActualWidth >= 48 && e.ActualWidth <= 72), "Category artwork too small");
                }
            }
            foreach (string theme in new[] { "Light", "Dark" })
            foreach (string style in new[] { "Preview", "Capsule", "Card" })
            {
                controller.UpdateSettings(s => s.Theme = theme);
                var pixels = new byte[120 * 80 * 4]; for(int i=0;i<pixels.Length;i+=4) { pixels[i]=180; pixels[i+1]=130; pixels[i+2]=55; pixels[i+3]=255; }
                var sample = BitmapSource.Create(120,80,96,96,PixelFormats.Bgra32,null,pixels,120*4);
                var surface = NotificationSurface.Create(L.T("Screenshot ready"), NotificationKind.Info, style, () => { }, sample, [("Edit", () => { }), ("Save PNG", () => { }), ("Pin", () => { })]);
                surface.Measure(new Size(460,double.PositiveInfinity)); surface.Arrange(new Rect(0,0,460,surface.DesiredSize.Height)); surface.UpdateLayout();
                var close = Walk(surface).OfType<Button>().Single(b => b.Name == "NotificationClose");
                var bounds = close.TransformToAncestor(surface).TransformBounds(new Rect(0,0,close.ActualWidth,close.ActualHeight));
                Check(bounds.Left >= 12 && bounds.Top >= 12 && bounds.Right <= surface.ActualWidth - 12 && bounds.Bottom <= surface.ActualHeight - 12, "Close button clipped");
                Check(close.ActualWidth == 32 && close.ActualHeight == 32, "Close target changed");
                var bmp = new RenderTargetBitmap(460,(int)Math.Ceiling(surface.ActualHeight),96,96,PixelFormats.Pbgra32); bmp.Render(surface); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp)); using var output = File.Create("notice-bd-" + style + "-" + theme + ".png"); encoder.Save(output);
            }
            var longSurface = NotificationSurface.Create(string.Join(" ", Enumerable.Repeat("Long notification with details", 60)), NotificationKind.Error, "Capsule", () => { });
            longSurface.Measure(new Size(400,double.PositiveInfinity)); longSurface.Arrange(new Rect(0,0,400,longSurface.DesiredSize.Height)); longSurface.UpdateLayout();
            Check(longSurface.ActualHeight < 320, "Long notification exceeds bounded height");
            Check(Walk(longSurface).OfType<ScrollViewer>().Any(s => s.ExtentHeight > s.ViewportHeight), "Long message clipped instead of scrollable");
            foreach (var kind in new[] { NotificationKind.Info, NotificationKind.Warning, NotificationKind.Error })
            {
                var notice = new NotificationWindow("Screenshot ready", kind);
                var content = (FrameworkElement)notice.Content;
                content.Measure(new Size(380, double.PositiveInfinity)); content.Arrange(new Rect(0,0,380,content.DesiredSize.Height)); content.UpdateLayout();
                Check(!Walk(content).OfType<Border>().Any(b => b.ActualWidth == 4), "Notification stripe retained");
                var bmp = new RenderTargetBitmap(380,(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32); bmp.Render(content); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp)); using(var file = File.Create("notice-compact-" + kind + ".png")) png.Save(file); notice.Close();
            }
            typeof(AppController).GetMethod("ShowCaptureNotice", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
            var captureNotice = Field<Window>(controller, "captureNotice"); Check((captureNotice.Tag as string) == "Notifications", "Screenshot notice bypasses feature privacy");
            Check(captureNotice.Content is Border captureSurface && captureSurface.Margin == new Thickness(0) && captureSurface.CornerRadius.TopLeft == 22, "Screenshot notice surface mismatch"); captureNotice.Close();
            main.Navigate("Settings"); typeof(MainWindow).GetField("settingsTab", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main,"Privacy"); main.Navigate("Settings"); main.UpdateLayout();
            var privacyFeatures = Walk(main).OfType<Expander>().Single(e => (e.Tag as string) == "privacy-features");
            Check(privacyFeatures.IsExpanded, "Privacy controls should start visible for review");
            privacyFeatures.IsExpanded = true; main.UpdateLayout();
            Check(Walk(main).OfType<CheckBox>().Count() >= 12, "Split privacy controls missing");
            main.Width=860;main.Height=600;main.Navigate("Home");main.UpdateLayout();Check(main.ActualWidth==860,"Minimum window layout");
        }
        finally { main.Hide(); L.Use("en"); }
    }
}
