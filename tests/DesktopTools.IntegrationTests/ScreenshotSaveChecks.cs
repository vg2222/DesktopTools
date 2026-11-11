using System;
using System.IO;
using System.Linq;
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
        return AutoSaveAsync(controller, image, Path.Combine(directory, "auto"));
    }

    /// <summary>"Clipboard and folder" writes the PNG without a dialog, copies once, and never replaces an earlier file.</summary>
    private static async Task AutoSaveAsync(AppController controller, BitmapSource image, string folder)
    {
        controller.UpdateSettings(s => { s.SaveDirectory = folder; s.CaptureOutput = DesktopTools.Core.CaptureAutoSave.Mode; });
        int copies = 0;
        Task<DesktopTools.Native.ClipboardResult> Copy(BitmapSource _) { copies++; return Task.FromResult(new DesktopTools.Native.ClipboardResult(true)); }
        string? first = await controller.CopyAndSaveAsync(image, Copy), second = await controller.CopyAndSaveAsync(image, Copy);
        if (first == null || second == null || first == second || !File.Exists(first) || !File.Exists(second))
            throw new Exception("Auto-save did not write two separate files: " + controller.Status);
        if (Path.GetDirectoryName(first) != folder || copies != 2) throw new Exception("Auto-save used the wrong folder or skipped the clipboard");
        using (var stream = File.OpenRead(first))
        {
            var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            if (decoded.PixelWidth != 2 || decoded.PixelHeight != 1) throw new Exception("Auto-save wrote the wrong picture");
        }
        // A busy clipboard still keeps the file; an unwritable folder still copies and reports instead of throwing.
        string? busy = await controller.CopyAndSaveAsync(image, _ => Task.FromResult(new DesktopTools.Native.ClipboardResult(false, "busy")));
        if (busy == null || !File.Exists(busy)) throw new Exception("A busy clipboard lost the saved file");
        string blocked = Path.Combine(folder, "blocked"); File.WriteAllText(blocked, "a file where the folder should be");
        controller.UpdateSettings(s => s.SaveDirectory = blocked);
        if (await controller.CopyAndSaveAsync(image, Copy) != null || copies != 3) throw new Exception("An unusable folder must report, not save or skip the copy");
        foreach (var notice in Application.Current.Windows.OfType<NotificationWindow>().ToArray()) notice.Close();
    }
}
