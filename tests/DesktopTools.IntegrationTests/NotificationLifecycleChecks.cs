using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;
using System.IO;
using DesktopTools;
using DesktopTools.UI;

internal static class NotificationLifecycleChecks
{
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    internal static async Task RunAsync()
    {
        if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
        {
            Motion.AppEnabled = true;
            var entrance = new NotificationWindow("Entrance animation", NotificationKind.Info, seconds: 30);
            try
            {
                if (((FrameworkElement)entrance.Content).Opacity != 0)
                    throw new Exception("Notification was not prepared for a visible entrance before Show");
                entrance.UpdateMessage("Updated before first render");
                if (((FrameworkElement)entrance.Content).Opacity != 0)
                    throw new Exception("Replacing an unrendered notification lost its entrance state");
                entrance.Show();
                await Task.Delay(350);
                if (Math.Abs(((FrameworkElement)entrance.Content).Opacity - 1) > .01)
                    throw new Exception("Notification entrance did not finish fully visible");
                double compactHeight = entrance.ActualHeight;
                string downloadDetails = string.Join(" ", Enumerable.Repeat("Downloading and verifying the new DesktopTools version.", 10));
                entrance.UpdateMessage(downloadDetails,
                    progress: .45, seconds: 30, actions: [("Cancel", () => { }), ("Show details", () => { })]);
                if (entrance.SizeToContent != SizeToContent.Manual || !entrance.HasAnimatedProperties)
                    throw new Exception("Updating a visible notification did not animate its height");
                await Task.Delay(50);
                entrance.UpdateMessage(downloadDetails, progress: .55, actions: [("Cancel", () => { }), ("Show details", () => { })]);
                await Task.Delay(350);
                double expandedHeight = entrance.ActualHeight;
                if (expandedHeight <= compactHeight + 30)
                    throw new Exception("Notification did not grow for update details");
                var progressBar = Descendants((DependencyObject)entrance.Content).OfType<ProgressBar>().Single();
                progressBar.ApplyTemplate();
                var indicator = progressBar.Template.FindName("PART_Indicator", progressBar) as Border;
                if (indicator is null || Math.Abs(progressBar.Value - .55) > .001 || indicator.CornerRadius.TopLeft < 3 ||
                    !Equals(progressBar.Foreground, Application.Current.Resources["Accent"]) ||
                    indicator.ActualWidth < 8 || indicator.ActualWidth >= progressBar.ActualWidth)
                    throw new Exception("Update progress lacks a rounded indicator in the app accent color");
                entrance.UpdateMessage("Update ready", actions: []);
                if (entrance.SizeToContent != SizeToContent.Manual || !entrance.HasAnimatedProperties)
                    throw new Exception("Notification did not animate back to compact size");
                await Task.Delay(350);
                if (entrance.ActualHeight >= expandedHeight - 30)
                    throw new Exception("Notification did not shrink after update details closed");
            }
            finally { entrance.Close(); }
        }
        uint activityTick = 100;
        bool hovering = true;
        var hoverNotice = new Window { Content = new TextBlock { Text = "Hover pause" }, Width = 180, Height = 80, ShowActivated = false, IsHitTestVisible = false, Left = -10000, Top = -10000 };
        bool hoverRendered = false;
        hoverNotice.ContentRendered += (_, _) => hoverRendered = true;
        Motion.AutoDismiss(hoverNotice, TimeSpan.FromMilliseconds(400), activitySource: () => activityTick, hoverSource: () => hovering);
        try
        {
            hoverNotice.Show(); await Task.Delay(80); activityTick++;
            await Task.Delay(750);
            if (!hoverNotice.IsVisible) throw new Exception("Notification expired while the cursor was over its window");
            hovering = false; await Task.Delay(1250);
            if (hoverNotice.IsVisible) throw new Exception($"Notification failed to resume its timer after hover ended; content rendered={hoverRendered}, mouse over={hoverNotice.IsMouseOver}, focus={hoverNotice.IsKeyboardFocusWithin}");
        }
        finally { hoverNotice.Close(); }
        using var controller = new AppController(true);
        controller.UpdateSettings(s => s.Animations = false);
        controller.OpenMain();
        var main = (MainWindow)typeof(AppController).GetField("main", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        foreach (string theme in new[] { "Light", "Dark" })
        {
            controller.UpdateSettings(s => s.Theme = theme);
            typeof(MainWindow).GetField("settingsTab", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, "Notifications");
            main.Navigate("Settings"); main.UpdateLayout();
            var duration = Descendants(main).OfType<Slider>().Single();
            duration.Value = 12;
            if (controller.Settings.NotificationSeconds != 12) throw new Exception("Duration UI did not update settings");
            var image = new RenderTargetBitmap((int)main.ActualWidth, (int)main.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            image.Render(main);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create("notification-settings-" + theme + ".png"); png.Save(file);
        }
        main.Hide();
        var anchor = new Window { Width = 600, Height = 400, Left = 100, Top = 100, ShowActivated = false };
        NotificationWindow? notice = null;
        try
        {
            anchor.Show(); anchor.UpdateLayout();
            foreach (bool accept in new[] { false, true })
            {
                var dialog = new ConfirmationDialog("Reset preferences", "Your drawing will be cleared.", "Reset") { Owner = anchor };
                try
                {
                    dialog.Show(); dialog.UpdateLayout();
                    if (dialog.WindowStyle != WindowStyle.None || dialog.Background == null)
                        throw new Exception("Confirmation kept native frame or lost its background");
                    var buttons = Descendants(dialog).OfType<Button>().ToArray();
                    var cancel = buttons.Single(b => b.Name == "CancelConfirmation");
                    if (!cancel.IsDefault || !cancel.IsCancel || dialog.Accepted) throw new Exception("Confirmation has unsafe default");
                    buttons.Single(b => b.Name == (accept ? "AcceptConfirmation" : "CancelConfirmation"))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (dialog.Accepted != accept) throw new Exception("Confirmation result disagrees with selected action");
                }
                finally { dialog.Close(); }
            }
            notice = new NotificationWindow("A notification with actions", NotificationKind.Info, anchor,
                "Preview", null, [("Save PNG", () => { })]);
            notice.Show(); notice.UpdateLayout(); await Task.Delay(80);
            if (notice.Owner != null) throw new Exception("Notice retained native ownership of anchor");
            if (notice.Background != Brushes.Transparent || notice.WindowStyle != WindowStyle.None)
                throw new Exception("Notice has a second window background");
            anchor.Hide(); await Task.Delay(80);
            if (!notice.IsVisible) throw new Exception("Hiding anchor also hid notice");
            notice.Close(); notice = new NotificationWindow("Replacement", NotificationKind.Warning);
            notice.Show(); await Task.Delay(250);
            if (!notice.IsVisible) throw new Exception("Old dismissal closed a replacement");
            var source = HwndSource.FromHwnd(new WindowInteropHelper(notice).Handle);
            if (source == null || source.IsDisposed) throw new Exception("Replacement lost its surface");
        }
        finally { notice?.Close(); anchor.Close(); }
    }
}
