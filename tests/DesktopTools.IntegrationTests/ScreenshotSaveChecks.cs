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
        await SmartNamesAsync(controller, image, Path.Combine(Path.GetDirectoryName(folder)!, "smart"), Copy);
    }

    /// <summary>File names: the date-and-time default is unchanged; Smart uses the window under the capture and never a flagged title.</summary>
    private static async Task SmartNamesAsync(AppController controller, BitmapSource image, string folder,
        Func<BitmapSource, Task<DesktopTools.Native.ClipboardResult>> copy)
    {
        controller.UpdateSettings(s => { s.SaveDirectory = folder; s.CaptureOutput = DesktopTools.Core.CaptureAutoSave.Mode; s.CaptureFileNames = "Date and time"; });
        var target = new Window { Title = "Name target", Width = 360, Height = 220, Topmost = true, ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual };
        var screen = DesktopTools.Native.MonitorService.GetAll().OrderByDescending(m => m.Bounds.Left != 0 || m.Bounds.Top != 0).First();
        target.Left = screen.Bounds.Left / screen.ScaleX + 240; target.Top = screen.Bounds.Top / screen.ScaleY + 240;
        try
        {
            target.Show();
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); await Task.Delay(150);
            var hwnd = new WindowInteropHelper(target).Handle;
            GetWindowRect(hwnd, out var r);
            // The real capture excludes DesktopTools' own process; this harness is that process, so it looks itself up explicitly.
            var window = DesktopTools.Native.NativeWindowService.DescribeTopWindowAt((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2, 0)
                ?? throw new Exception("The name target window was not found under its own centre");
            string? dated = await controller.CopyAndSaveAsync(image, copy, window);
            if (dated == null || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(dated), @"^DesktopTools-\d{8}-\d{6}\.png$"))
                throw new Exception("Date-and-time names changed: " + dated);

            controller.UpdateSettings(s => s.CaptureFileNames = "Smart");
            string? first = await controller.CopyAndSaveAsync(image, copy, window), second = await controller.CopyAndSaveAsync(image, copy, window);
            string firstName = Path.GetFileName(first ?? "");
            if (first == null || second == null || first == second || !File.Exists(first) || !File.Exists(second))
                throw new Exception("Two smart captures did not produce two files: " + controller.Status);
            if (!firstName.StartsWith(window.App, StringComparison.Ordinal) || !firstName.Contains("Name target", StringComparison.Ordinal))
                throw new Exception($"Smart name does not start with the app \"{window.App}\" and contain the title: {firstName}");

            target.Title = "Message for anna@example.com"; await Task.Delay(100);
            var flagged = DesktopTools.Native.NativeWindowService.DescribeTopWindowAt((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2, 0);
            if (flagged?.Title != target.Title) throw new Exception("The renamed target was not described: " + flagged);
            string? safe = await controller.CopyAndSaveAsync(image, copy, flagged);
            if (safe == null || Path.GetFileName(safe).Contains('@') || Path.GetFileName(safe).Contains("anna", StringComparison.OrdinalIgnoreCase))
                throw new Exception("A flagged window title reached the file name: " + safe);
            if (controller.SuggestedSaveName(image, flagged, DateTime.Now).Contains('@')) throw new Exception("A flagged title reached the Save dialog name");
            if (!controller.SuggestedSaveName(image, window, DateTime.Now).Contains("Name target")) throw new Exception("The Save dialog does not suggest the smart name");
            // Review Focus 2 against the real sensitive-data check: none of these may reach a name.
            foreach (var (title, secret) in new[]
            {
                ("Call +1 415 555 0132 – Contacts", "555"),
                ("Vault – API key sk_" + "live_4f9a8b7c6d5e4f3a2b1c0d9e", "sk_live"),
                ("Transfer to DE89 3704 0044 0532 0130 00 – Bank", "3704"),
                ("Card 4111 1111 1111 1111 – Checkout", "4111"),
            })
            {
                string stem = controller.CaptureFileStem(new DesktopTools.Native.WindowDescription("Mail", title), DateTime.Now, null);
                if (stem.Contains(secret, StringComparison.OrdinalIgnoreCase)) throw new Exception($"A flagged title reached the name: \"{title}\" -> \"{stem}\"");
            }
            string harmless = controller.CaptureFileStem(new DesktopTools.Native.WindowDescription("Notepad", "Quarterly plan notes"), DateTime.Now, null);
            if (!harmless.Contains("Quarterly plan notes")) throw new Exception("A harmless title was dropped: " + harmless);
        }
        finally
        {
            target.Close();
            controller.UpdateSettings(s => s.CaptureFileNames = "Date and time");
            foreach (var notice in Application.Current.Windows.OfType<NotificationWindow>().ToArray()) notice.Close();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
}
