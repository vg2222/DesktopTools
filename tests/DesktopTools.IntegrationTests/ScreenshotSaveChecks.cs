using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopTools;
using DesktopTools.UI;

internal static class ScreenshotSaveChecks
{
    internal static Task RunAsync()
    {
        using var controller = new AppController(true);
        controller.UpdateSettings(s => s.Animations = false);
        var capture = typeof(AppController).GetProperty("LastCapture")!;
        var image = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 0, 0, 255, 255, 0, 255, 0, 255 }, 8);
        image.Freeze(); capture.SetValue(controller, image);
        var directory = Path.GetFullPath("save-notification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (bool save in new[] { false, true })
        {
            var notice = new NotificationWindow("Screenshot ready", NotificationKind.Info);
            notice.Show();
            bool survived = false, stableOwner = false;
            string output = Path.Combine(directory, save ? "saved.png" : "cancelled.png");
            controller.SaveLast((dialog, owner) =>
            {
                stableOwner = owner is MainWindow && !owner.IsVisible && new WindowInteropHelper(owner).Handle != 0;
                dialog.FileName = output;
                var modal = new Window { Owner = owner, Width = 200, Height = 100, ShowInTaskbar = false };
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                int ticks = 0;
                timer.Tick += (_, _) =>
                {
                    if (++ticks == 1) { notice.Close(); return; }
                    survived = modal.IsVisible && !notice.IsVisible;
                    // Nested message loops must not change which screenshot gets saved.
                    capture.SetValue(controller, null);
                    timer.Stop(); modal.DialogResult = save;
                };
                timer.Start();
                try { return modal.ShowDialog(); }
                finally { timer.Stop(); modal.Close(); }
            });
            notice.Close();
            if (!stableOwner || !survived || controller.IsBusy)
                throw new Exception("Save dialog lost its stable hidden owner, closed with the notification, or left capture busy: " + controller.Status);
            if (File.Exists(output) != save) throw new Exception("Save/cancel produced the wrong file result");
            if (save)
            {
                using var stream = File.OpenRead(output);
                var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                if (decoded.PixelWidth != 2 || decoded.PixelHeight != 1) throw new Exception("Wrong screenshot saved");
            }
            capture.SetValue(controller, image);
        }
        return Task.CompletedTask;
    }
}
