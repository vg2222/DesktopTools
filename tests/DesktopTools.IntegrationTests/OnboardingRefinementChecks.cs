using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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

internal static class OnboardingRefinementChecks
{
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(nint handle, out uint affinity);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Click(MainWindow main, string tag) => Walk(main).OfType<Button>().Single(b => Equals(b.Tag, tag)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Render(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); png.Save(file);
    }
    internal static async Task RunAsync()
    {
        string original = Environment.CurrentDirectory;
        string isolated = Path.Combine(original, "onboarding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolated); Environment.CurrentDirectory = isolated;
        try
        {
            AppCapturePrivacy.Initialize();
            using var controller = new AppController(true);
            controller.UpdateSettings(s => { s.Theme = "Dark"; s.Animations = false; s.SharingWelcomeSeen = true; s.StartAtLogin = false; });
            controller.OpenSetup(restart: true); await Task.Delay(80);
            var main = Application.Current.Windows.OfType<MainWindow>().Single();
            var setup = Application.Current.Windows.OfType<SetupWindow>().Single();
            main.UpdateLayout();
            Check(!setup.IsVisible && new WindowInteropHelper(setup).Handle == 0, "Setup created a separate native window");
            Check(Walk(main).OfType<Grid>().Any(g => Equals(g.Tag, "app-modal")), "Setup is not inside the main window");
            Check(Walk(main).OfType<TextBlock>().Any(t => t.Text == "Live preview"), "Appearance preview missing");
            Render(main, Path.Combine(original, "onboarding-appearance.png"));
            Click(main, "setup-next"); Click(main, "setup-next"); main.UpdateLayout();
            var switches = Walk(main).OfType<CheckBox>().Where(b => (b.Tag as string)?.StartsWith("shortcut-enabled-") == true).ToArray();
            Check(switches.Length == FeatureShortcutCatalog.All.Count, "Setup omitted shortcut switches");
            foreach (var entry in FeatureShortcutCatalog.All)
                Check(FeatureShortcutCatalog.IsEnabled(controller.Settings, entry) == !entry.Optional, "Incorrect initial shortcut state: " + entry.Action);
            foreach (var item in switches)
            {
                var thumb = (FrameworkElement)item.Template.FindName("Thumb", item);
                Check(((TranslateTransform)thumb.RenderTransform).X == (item.IsChecked == true ? 20 : 0), "Switch thumb does not match its state");
            }
            var recorder = FeatureShortcutCatalog.All.Single(e => e.Action == "Recorder");
            var toggle = switches.Single(b => Equals(b.Tag, "shortcut-enabled-Recorder"));
            controller.UpdateSettings(s => s.ScreenRecorderEnabled = true);
            toggle.IsChecked = true;
            Check(FeatureShortcutCatalog.Bindings(controller.Settings).ContainsKey("Recorder"), "Enabled shortcut is not registered");
            toggle.IsChecked = false;
            Check(!FeatureShortcutCatalog.Bindings(controller.Settings).ContainsKey("Recorder"), "Disabled shortcut is still registered");
            Check(controller.UpdateSettings(s => recorder.Write(s, s.CaptureShortcut)), "Disabled shortcut cannot retain a candidate");
            Check(!controller.SetFeatureShortcutEnabled(recorder, true), "Conflicting shortcut was enabled");
            Check(recorder.Read(controller.Settings) == controller.Settings.CaptureShortcut, "Conflict damaged retained binding");
            controller.UpdateSettings(s => recorder.Write(s, recorder.DefaultGesture));
            var persisted = new SettingsStore(Path.Combine(isolated, "roundtrip")); persisted.Save(controller.Settings);
            var loaded = persisted.Load();
            Check(!FeatureShortcutCatalog.IsEnabled(loaded, recorder) && recorder.Read(loaded) == recorder.DefaultGesture, "Shortcut switch/binding did not persist independently");
            Render(main, Path.Combine(original, "onboarding-shortcuts.png"));

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            bool nested = false; Exception? modalError = null;
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    var dialog = Application.Current.Windows.OfType<ShortcutRecorderWindow>().Single();
                    Check(Walk(main).OfType<Grid>().Count(g => Equals(g.Tag, "app-modal")) == 2, "Shortcut recorder is not a nested modal");
                    Check(!dialog.IsVisible, "Shortcut recorder opened a native window");
                    dialog.RecordGesture(System.Windows.Input.Key.G, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt | System.Windows.Input.ModifierKeys.Shift);
                    Render(main, Path.Combine(original, "shortcut-modal.png"));
                    dialog.Close(); nested = true;
                }
                catch (Exception ex) { modalError = ex; Application.Current.Windows.OfType<ShortcutRecorderWindow>().FirstOrDefault()?.Close(); }
            };
            timer.Start(); controller.RecordFeatureShortcut(recorder);
            if (modalError != null) throw modalError;
            Check(nested && Walk(main).OfType<Grid>().Count(g => Equals(g.Tag, "app-modal")) == 1, "Nested modal did not restore setup");
            Click(main, "setup-next"); Click(main, "setup-next");
            Check(controller.Settings.Setup.Status == SetupStatus.Completed, "Setup completion not saved");
            Check(!Walk(main).OfType<Grid>().Any(g => Equals(g.Tag, "app-modal")), "Finished setup retained overlay");
            Check(((Grid)main.Content).Children.OfType<FrameworkElement>().All(e => e.IsEnabled && e.Effect == null), "Setup retained disabled/blurred app");
            main.Navigate("Shortcuts"); main.UpdateLayout();
            Check(Walk(main).OfType<CheckBox>().Count(b => (b.Tag as string)?.StartsWith("shortcut-enabled-") == true) == FeatureShortcutCatalog.All.Count, "Main shortcuts page omitted actions");
            Render(main, Path.Combine(original, "shortcuts-switches.png"));

            var privacy = new AppSettings { HideControlsFromCapture = true, HideMainWindowFromCapture = true };
            foreach (string tool in AppCapturePrivacy.IndividualTools)
            {
                var window = new Window { Tag = tool, Width = 160, Height = 90 };
                Check(AppCapturePrivacy.ShouldHide(window, privacy) == (tool == "Teleprompter"), tool + " has the wrong independent default");
                privacy.CaptureVisibilityOverrides[tool] = false; Check(!AppCapturePrivacy.ShouldHide(window, privacy), tool + " cannot be made visible independently");
                privacy.CaptureVisibilityOverrides[tool] = true; Check(AppCapturePrivacy.ShouldHide(window, privacy), tool + " cannot be hidden independently");
                window.Close(); privacy.HiddenCaptureTools = [];
            }
            using (var hud = new PresentationHudService(_ => { }, () => true))
            {
                var effect = (Window)typeof(PresentationHudService).GetMethod("Shell", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hud, new object[] { "Click", 64d, 64d, false })!;
                effect.Show();
                Check(!AppCapturePrivacy.IsControlWindow(effect), "Audience indicator is classified as a control");
                NativeWindowService.TryExcludeFromCapture(effect, true, out _);
                Check(GetWindowDisplayAffinity(new WindowInteropHelper(effect).Handle, out uint affinity) && affinity == 0, "Audience effect can be excluded from capture");
                effect.Close();
            }
            foreach (string key in new[] { "Close", "Swap", "Pin", "Settings" })
            {
                var bounds = ToolIcons.GeometryFor(key).Bounds;
                Check(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= 24.1 && bounds.Bottom <= 24.1, key + " extends beyond its SVG viewbox");
            }
            var qr = new QrCodeWindow(_ => { }); qr.Show(); await Task.Delay(50);
            Render(qr, Path.Combine(original, "qr-refined-empty.png"));
            Walk(qr).OfType<TextBox>().Single().Text = "https://example.com";
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!Walk(qr).OfType<Image>().Any(i => i.Source != null) && DateTime.UtcNow < deadline) await Task.Delay(40);
            Check(Walk(qr).OfType<Image>().Any(i => i.Source != null), "QR live preview did not generate");
            Render(qr, Path.Combine(original, "qr-refined-preview.png")); qr.Close();
            var notice = new NotificationWindow("Your screenshot is ready.", NotificationKind.Info, main, seconds: 30); notice.Show();
            Render(notice, Path.Combine(original, "notification-refined.png")); notice.Close();
            controller.UpdateSettings(s => { s.SharingWelcomeSeen = false; s.Animations = true; });
            controller.OpenSetup(restart: true); main.UpdateLayout(); Click(main, "setup-skip");
            for (int i = 0; i < 100 && !Application.Current.Windows.OfType<SharingWelcomeWindow>().Any(); i++)
                await Task.Delay(20);
            main.UpdateLayout();
            Check(main.HasModal && Walk(main).OfType<Grid>().Count(g => Equals(g.Tag, "app-modal")) == 1, "Setup-to-sharing transition retained an extra layer");
            Check(!((FrameworkElement)((Grid)main.Content).Children[0]).IsEnabled, "Sharing modal enabled the background");
            var sharing = Application.Current.Windows.OfType<SharingWelcomeWindow>().SingleOrDefault();
            Check(sharing != null, "Sharing guide did not open after setup closed");
            sharing!.Close();
            for (int i = 0; i < 100 && main.HasModal; i++) await Task.Delay(20);
            Check(!main.HasModal && ((FrameworkElement)((Grid)main.Content).Children[0]).IsEnabled, "Sharing dismissal did not restore the main app");
            string installerAssembly = Path.GetFullPath(Path.Combine(original, "../../installer/bin/Release/net10.0-windows/win-x64/DesktopTools.Installer.dll"));
            if (File.Exists(installerAssembly))
            {
                // The installer runs in its own Application and must not inherit the app's implicit control styles.
                var dictionaries = Application.Current.Resources.MergedDictionaries.ToArray();
                Application.Current.Resources.MergedDictionaries.Clear();
                try
                {
                    var type = Assembly.LoadFrom(installerAssembly).GetType("DesktopTools.Installer.InstallerWindow")!;
                    var installer = (Window)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { false }, null)!;
                    installer.Show(); await Task.Delay(300); Render(installer, Path.Combine(original, "installer-refined.png")); installer.Close();
                }
                finally { foreach (var dictionary in dictionaries) Application.Current.Resources.MergedDictionaries.Add(dictionary); }
            }
            Console.WriteLine("PASS embedded setup/shortcut modal, complete switch catalog, persisted disabled bindings, conflict rollback, audience affinity, independent privacy, SVG bounds and QR preview");
        }
        finally { Environment.CurrentDirectory = original; }
    }
}
