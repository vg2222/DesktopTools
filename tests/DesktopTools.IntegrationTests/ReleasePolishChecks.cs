using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Native;
using DesktopTools.UI;

internal static class ReleasePolishChecks
{
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Render(Window window, string path)
    {
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(path); png.Save(stream);
    }
    internal static async Task RunAsync()
    {
        string originalDirectory = Environment.CurrentDirectory;
        string isolated = Path.Combine(originalDirectory, "release-polish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolated); Environment.CurrentDirectory = isolated;
        try
        {
            using var controller = new AppController(true);
            controller.UpdateSettings(s => { s.Animations = false; s.Theme = "Dark"; s.Setup.Status = SetupStatus.Completed; s.SetupExperienceVersion = 0; s.CaptureEnabled = false; s.HideMainWindowFromCapture = false; s.HideControlsFromCapture = false; s.SharingWelcomeSeen = false; });
            controller.OpenMain(); controller.OpenSetup(); await Task.Delay(100);
            var main = Application.Current.Windows.OfType<MainWindow>().Single();
            var setup = Application.Current.Windows.OfType<SetupWindow>().Single();
            var availableIds = FeatureAvailability.All.Select(feature => feature.Id).ToHashSet(StringComparer.Ordinal);
            var shortcutIds = FeatureShortcutCatalog.All.Select(entry => entry.FeatureId).ToHashSet(StringComparer.Ordinal);
            Check(availableIds.All(shortcutIds.Contains), "At least one available feature has no shortcut entry");
            Check(FeatureShortcutCatalog.All.Select(entry => entry.Action).Distinct(StringComparer.Ordinal).Count() == FeatureShortcutCatalog.All.Count,
                "Shortcut actions are not unique");
            Check(FeatureShortcutCatalog.TryValidate(new AppSettings(), out _), "Default shortcuts are invalid or conflict");
            var malformed = new AppSettings(); malformed.FeatureShortcuts["Recorder"] = null!; SettingsStore.Validate(malformed);
            Check(malformed.FeatureShortcuts["Recorder"] == "", "Null persisted feature shortcut was not normalized");
            var conflict = new AppSettings();
            FeatureShortcutCatalog.All.Single(entry => entry.Action == "Recorder").Write(conflict, conflict.CaptureShortcut);
            FeatureShortcutCatalog.SetEnabled(conflict, FeatureShortcutCatalog.All.Single(entry => entry.Action == "Recorder"), true);
            Check(!FeatureShortcutCatalog.TryValidate(conflict, out _), "Global shortcut conflict was accepted");
            main.Navigate("Shortcuts"); main.UpdateLayout();
            Check(Walk(main).OfType<Button>().Count(button => AutomationProperties.GetName(button).StartsWith("Change shortcut", StringComparison.Ordinal))
                == FeatureShortcutCatalog.All.Count, "Shortcuts page does not expose every catalog action");
            var recorderShortcut = FeatureShortcutCatalog.All.Single(entry => entry.Action == "Recorder");
            Check(controller.ClearFeatureShortcut(recorderShortcut) && recorderShortcut.Read(controller.Settings) == "", "Optional shortcut could not be cleared");
            Check(controller.UpdateSettings(settings => recorderShortcut.Write(settings, "Ctrl+Alt+Shift+G")) &&
                recorderShortcut.Read(controller.Settings) == "Ctrl+Alt+Shift+G", "Edited feature shortcut was not persisted");
            Check(controller.Settings.Setup.Status == SetupStatus.InProgress && controller.Settings.SetupExperienceVersion == 3, "Existing profile did not receive updated setup");
            Check(controller.Settings.CaptureEnabled, "Legacy capture availability was not migrated");
            Check(setup.Owner == main && main.IsVisible, "Setup did not retain visible main owner");
            controller.UpdateSettings(s => s.Setup.Status = SetupStatus.Completed); setup.Close(); await Task.Delay(80);
            var welcome = Application.Current.Windows.OfType<SharingWelcomeWindow>().Single();
            Render(main, Path.Combine(originalDirectory, "sharing-welcome-current.png"));
            controller.Settings.SharingWelcomeSeen = true; welcome.Close(); await Task.Delay(60);
            Check(main.IsVisible && main.WindowState != WindowState.Minimized, "Closing sharing notice hid/minimized main");
            controller.OpenSetup(); Check(!Application.Current.Windows.OfType<SetupWindow>().Any(), "Completed current setup reopened");
            var hwnd = new WindowInteropHelper(main).Handle;
            var edge = main.PointToScreen(new Point(100, 3));
            nint hit = SendMessage(hwnd, 0x84, 0, new nint(((int)edge.Y << 16) | ((int)edge.X & 0xffff)));
            Check(hit == 2, "Main top edge is not a native drag area: " + hit);
            Check(main.Content is FrameworkElement { Clip: RectangleGeometry }, "Main shell lacks rounded clip");
            Render(main, Path.Combine(originalDirectory, "main-icons-current.png"));

            var recorder = new ScreenRecorderWindow(controller); recorder.Show(); await Task.Delay(80);
            int visibilityChanges = 0; recorder.IsVisibleChanged += (_, _) => visibilityChanges++;
            nint recorderHandle = new WindowInteropHelper(recorder).Handle;
            NativeWindowService.TryExcludeFromCapture(recorder, false, out _);
            Check(GetWindowDisplayAffinity(recorderHandle, out uint originalAffinity), "Cannot read recorder capture policy");
            foreach (var monitor in MonitorService.GetAll())
            {
                recorder.SetSource(new RecordingSelection("Fixture monitor", Monitor: monitor));
                await (Task)typeof(ScreenRecorderWindow).GetMethod("RefreshPreviewAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(recorder, null)!;
                Check(Field<Image>(recorder, "preview").Source != null, "Display preview failed: " + Field<TextBlock>(recorder, "status").Text);
                Check(GetWindowDisplayAffinity(recorderHandle, out uint restored) && restored == originalAffinity, "Preview changed capture preference");
            }
            Check(visibilityChanges == 0 && recorder.IsVisible, "Preview refresh hid/reopened recorder");
            recorder.Close();

            var notices = Enumerable.Range(0, 12).Select(index => new NotificationWindow(
                $"Notification {index + 1}: this longer message exercises top-center overflow placement.",
                index % 2 == 0 ? NotificationKind.Info : NotificationKind.Warning, main, seconds: 30)).ToArray();
            for (int index = 0; index < notices.Length; index++) notices[index].Width = index % 3 switch { 1 => 410, 2 => 460, _ => 400 };
            foreach (var notice in notices) notice.Show();
            await Task.Delay(100);
            var first = notices[0];
            var primary = MonitorService.GetCurrent(primary: true);
            var firstOrigin = first.PointToScreen(new Point());
            Check(Math.Abs(firstOrigin.X + first.ActualWidth * primary.ScaleX / 2 - (primary.WorkingArea.Left + primary.WorkingArea.Width / 2)) < 3, "Notification is not centered on primary monitor");
            var noticeRects = notices.Select(notice =>
            {
                var origin = notice.PointToScreen(new Point());
                return new Rect(origin.X, origin.Y, notice.ActualWidth * primary.ScaleX, notice.ActualHeight * primary.ScaleY);
            }).ToArray();
            for (int left = 0; left < noticeRects.Length; left++)
            for (int right = left + 1; right < noticeRects.Length; right++)
                Check(!noticeRects[left].IntersectsWith(noticeRects[right]), $"Notifications {left + 1} and {right + 1} overlap");
            foreach (var notice in notices) notice.Close();
            Check(!new AppSettings().HideControlsFromCapture && new AppSettings().HideMainWindowFromCapture != true, "Fresh default capture policy hides tools");
            Console.WriteLine("PASS existing-profile setup, main visibility, native top drag, rounded shell, recorder previews/policy restore, primary-monitor notification stack");
        }
        finally { Environment.CurrentDirectory = originalDirectory; }
    }
}
