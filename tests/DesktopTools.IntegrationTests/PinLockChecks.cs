using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools;
using DesktopTools.Extras;
using DesktopTools.Native;
using DesktopTools.UI;

/// <summary>A locked pinned screenshot ignores the mouse, can be unlocked again, and the tray offers the way back.</summary>
internal static class PinLockChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static long ExStyle(Window window) => NativeMethods.GetWindowLongPtr(new WindowInteropHelper(window).Handle, -20).ToInt64();

    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        DesktopTools.Localization.L.Use("en");
        var pixels = new byte[200 * 120 * 4];
        var image = BitmapSource.Create(200, 120, 96, 96, PixelFormats.Bgra32, null, pixels, 200 * 4); image.Freeze();
        var pin = new PinnedImageWindow(image, _ => { });
        int lockedRaised = 0; pin.Locked += _ => lockedRaised++;
        pin.Show(); await Task.Delay(120);
        const long transparent = 0x20;
        Check(!pin.IsLocked && (ExStyle(pin) & transparent) == 0, "A new pinned screenshot should react to the mouse");

        pin.SetLocked(true); await Task.Delay(60);
        Check(pin.IsLocked && lockedRaised == 1, "Locking did not report back to the app");
        Check((ExStyle(pin) & transparent) != 0, "A locked screenshot still receives mouse input");
        Check(!pin.IsHitTestVisible, "A locked screenshot is still hit-testable");
        pin.SetLocked(true); Check(lockedRaised == 1, "Locking twice should not notify twice");

        pin.SetLocked(false); await Task.Delay(60);
        Check(!pin.IsLocked && (ExStyle(pin) & transparent) == 0 && pin.IsHitTestVisible, "Unlocking did not restore mouse input");
        pin.Close();

        // The tray only offers the unlock actions while something is locked.
        bool locked = false; int unlocked = 0, closed = 0;
        var tray = new TrayMenuPopup(() => { }, () => { }, () => { }, () => { }, () => { }, () => unlocked++, () => closed++, () => locked);
        try
        {
            tray.IsOpen = true; await Task.Delay(150);
            var card = (FrameworkElement)tray.Child;
            Button Find(string text) => Walk(card).OfType<Button>().First(b => System.Windows.Automation.AutomationProperties.GetName(b) == text);
            Check(Find("Unlock pinned screenshots").Visibility != Visibility.Visible, "Unlock was offered with nothing locked");
            tray.IsOpen = false; locked = true; tray.IsOpen = true; await Task.Delay(150);
            Check(Find("Unlock pinned screenshots").Visibility == Visibility.Visible && Find("Close pinned screenshots").Visibility == Visibility.Visible,
                "Unlock actions missing from the tray while a screenshot is locked");
            Find("Unlock pinned screenshots").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Application.Current.Dispatcher.InvokeAsync(() => { });
            Check(unlocked == 1, "Tray unlock did not run");
        }
        finally { tray.IsOpen = false; }
    }
}
