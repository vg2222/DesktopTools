using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopTools;
using DesktopTools.Presentation;
using DesktopTools.UI;

internal static class WindowDismissalChecks
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Finish(Window window)
    {
        for (int i = 0; i < 100 && WindowDismissal.IsDismissing(window); i++) await Task.Delay(20);
    }
    private static Window Fixture() => new()
    {
        Width = 240, Height = 120, ShowActivated = false, ShowInTaskbar = false,
        WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
        Content = new Border { Background = Brushes.SlateBlue }, WindowStartupLocation = WindowStartupLocation.CenterScreen
    };
    internal static async Task RunAsync()
    {
        // Deterministic animation policy isolates lifecycle tests from OS preferences.
        var window = Fixture(); int closes = 0;
        window.Closed += (_, _) => closes++;
        WindowDismissal.Attach(window, () => true);
        double closingOpacity = -1;
        window.Closed += (_, _) => closingOpacity = window.Opacity;
        window.Show(); await Task.Delay(40);
        window.Close(); window.Close();
        Check(window.IsVisible && closes == 0 && WindowDismissal.IsDismissing(window), "Close was not deferred");
        Check(!((UIElement)window.Content).IsHitTestVisible, "Fading window accepted pointer input");
        var windowVisual = (FrameworkElement)window.Content;
        Check(windowVisual.RenderTransform is TransformGroup, "Layered close did not use scale motion");
        await Task.Delay(45);
        Check(window.Opacity < 1, "Layered opacity did not interpolate");
        await Finish(window);
        Check(!window.IsVisible && closes == 1, "Repeated close did not finish exactly once");
        Check(closingOpacity == 0, "Close cleanup restored an opaque last frame");

        var opaque = new Window { Width = 240, Height = 120, ShowActivated = false, Content = new Border { Background = Brushes.SlateBlue } };
        WindowDismissal.Attach(opaque, () => true); opaque.Show(); await Task.Delay(40);
        WindowDismissal.Hide(opaque, () => true);
        Check(!opaque.IsVisible && opaque.Opacity == 1 && !opaque.HasAnimatedProperties,
            "Opaque native hide modified WPF opacity or added a second animation");
        opaque.Show(); opaque.Close();
        Check(!opaque.IsVisible && opaque.Opacity == 1 && !opaque.HasAnimatedProperties,
            "Opaque native close exposed the composition backplate");

        var guarded = Fixture(); bool busy = true;
        guarded.Closing += (_, e) => e.Cancel = busy;
        WindowDismissal.Attach(guarded, () => true); guarded.Show(); await Task.Delay(30);
        guarded.Close(); await Task.Delay(220);
        Check(guarded.IsVisible && guarded.Opacity == 1 && !WindowDismissal.IsDismissing(guarded), "Busy guard was bypassed");
        busy = false; guarded.Close(); busy = true; await Finish(guarded);
        Check(guarded.IsVisible && guarded.Opacity == 1 && ((UIElement)guarded.Content).IsHitTestVisible,
            $"A final-close veto left the window transparent or disabled: visible={guarded.IsVisible}, opacity={guarded.Opacity}, input={((UIElement)guarded.Content).IsHitTestVisible}, running={WindowDismissal.IsDismissing(guarded)}");
        busy = false; guarded.Close(); await Finish(guarded);
        Check(!guarded.IsVisible, "Guarded retry failed");

        var hidden = Fixture(); WindowDismissal.Attach(hidden, () => true);
        hidden.Show(); await Task.Delay(30); WindowDismissal.Hide(hidden, () => true);
        await Task.Delay(50); WindowDismissal.Cancel(hidden); hidden.Show(); await Task.Delay(220);
        Check(hidden.IsVisible && hidden.Opacity == 1, "Stale hide dismissed a reopened window");
        WindowDismissal.Hide(hidden, () => true); await Finish(hidden);
        Check(!hidden.IsVisible && hidden.Opacity == 1 && ((UIElement)hidden.Content).IsHitTestVisible, "Hide did not restore reusable state");
        hidden.Show();
        using (WindowDismissal.Suppress()) hidden.Close();
        Check(!hidden.IsVisible, "Shutdown suppression deferred cleanup");

        var immediate = Fixture(); WindowDismissal.Attach(immediate, () => false);
        immediate.Show(); immediate.Close();
        Check(!immediate.IsVisible, "Reduced motion deferred close");

        foreach (bool accepted in new[] { false, true })
        {
            var dialog = Fixture(); WindowDismissal.Attach(dialog, () => true);
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(() => dialog.DialogResult = accepted));
            bool? actual = dialog.ShowDialog();
            Check(actual == accepted, "Animated ShowDialog lost its result");
        }

        using var controller = new AppController(true);
        controller.UpdateSettings(settings => settings.Animations = true);
        var notice = new NotificationWindow("Dismissal fixture", NotificationKind.Info);
        WindowDismissal.Attach(notice, () => true); notice.Show(); await Task.Delay(80);
        notice.Close();
        Check(notice.IsVisible && WindowDismissal.IsDismissing(notice), "Notification skipped manual-close fade");
        notice.UpdateMessage("New notification state", seconds: 30); await Task.Delay(240);
        Check(notice.IsVisible && notice.Opacity == 1, "Updated notification retained stale close callback");
        notice.Close(); await Finish(notice);
        Check(!notice.IsVisible, "Notification did not finish closing");

        controller.OpenMain();
        var main = (MainWindow)typeof(AppController).GetField("main", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        await Task.Delay(80);
        Check((GetWindowLongPtr(new WindowInteropHelper(main).Handle, -16).ToInt64() & 0x00C00000) == 0x00C00000,
            "Custom chrome removed the native caption style used by DWM transitions");
        var embedded = Fixture();
        main.ShowEmbeddedDialog(embedded); await Task.Delay(260);
        embedded.Close();
        if (Motion.Enabled) Check(main.HasModal, "Embedded modal disappeared before fading");
        await Finish(embedded);
        Check(!main.HasModal, "Embedded modal retained dimmer/input lock");
        controller.Settings.DrawingEnabled = controller.Settings.FreezeEnabled = true; controller.Settings.ShortcutEnabled["Freeze"] = true;
        await controller.FreezeAsync();
        Check(!main.IsVisible, "Freeze did not hide the main window before capturing the desktop");
        Check(controller.Status.Contains("Esc") && controller.Status.Contains(controller.Settings.FreezeShortcut), "Freeze notification did not explain both exit controls");
        controller.HideAnnotations(); main.Show();
        typeof(DesktopTools.Core.AppSettings).GetProperty("TrayHintShown")?.SetValue(controller.Settings, false);
        main.Close();
        Check(!main.IsVisible && main.Opacity == 1, "Main close should hand the complete surface to Windows immediately");
        Check(typeof(DesktopTools.Core.AppSettings).GetProperty("TrayHintShown")?.GetValue(controller.Settings) is true, "First close did not record the one-time Windows tray guidance");
        await Finish(main);
        Check(!main.IsVisible, "Main did not hide to tray");
    }
}
