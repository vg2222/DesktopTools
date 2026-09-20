using System;
using System.IO;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using DesktopTools.Extras;
using ScreenRecorderLib;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

internal static class RecordingChecks
{
    internal static async Task EncodingAsync()
    {
        var directory = Path.GetFullPath("encoding-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var settings = new DesktopTools.Core.AppSettings { RecordingHardwareAcceleration = false, RecordingQuality = "High", RecordingFramesPerSecond = 144 };
        var store = new DesktopTools.Core.SettingsStore(directory); store.Save(settings);
        Check(!store.Load().RecordingHardwareAcceleration, "Software compatibility preference was not persisted");
        var profiles = new DesktopTools.Native.ProfileStore(Path.Combine(directory, "profiles")); profiles.Save("Recording", settings);
        var applied = DesktopTools.Native.ProfileStore.ApplyTo(new DesktopTools.Core.AppSettings(), profiles.Load("Recording"));
        Check(applied.RecordingQuality == "High" && applied.RecordingFramesPerSecond == 144 && applied.RecordingHardwareAcceleration,
            "Profile lost recording preferences or overwrote machine encoding preference");
        using var target = new Forms.Form { TopMost = true, StartPosition = Forms.FormStartPosition.CenterScreen,
            FormBorderStyle = Forms.FormBorderStyle.None, ClientSize = new Drawing.Size(320, 200), Text = "DesktopTools encoder helper" };
        try
        {
            target.Show();
            foreach (bool hardware in new[] { true, false })
            {
                target.BackColor = Drawing.Color.RoyalBlue; target.Refresh(); await Task.Delay(200);
                string path = Path.Combine(directory, hardware ? "hardware-requested.mp4" : "software.mp4");
                using var service = new ScreenRecordingService();
                var result = service.StartSource(new WindowRecordingSource(target.Handle) { IsCursorCaptureEnabled = false, IsBorderRequired = true },
                    path, framesPerSecond: 144, hardwareAcceleration: hardware);
                await Task.Delay(1300); target.BackColor = Drawing.Color.OrangeRed; target.Refresh();
                await Task.Delay(1200); service.Stop(); await result.WaitAsync(TimeSpan.FromSeconds(25));
                var info = await VideoEditorService.ProbeAsync(path);
                var first = await VideoEditingChecks.PixelAsync(path, .4); var last = await VideoEditingChecks.PixelAsync(path, info.Duration - .3);
                Check(info.Width == 320 && info.Height == 200 && !info.HasAudio && first[0] > 150 && first[2] < 120 && last[2] > 180 && last[0] < 80,
                    "Encoder output had wrong dimensions, audio, black or stale pixels: hardware=" + hardware);
                var clip = await Windows.Media.Editing.MediaClip.CreateFromFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(path));
                var rate = clip.GetVideoEncodingProperties().FrameRate;
                Console.WriteLine($"Hardware requested={hardware}; target=144; metadata={rate.Numerator / (double)rate.Denominator:0.##} FPS; duration={info.Duration:0.##}; distinct frames not measured");
            }
        }
        finally { target.Close(); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static async Task RunAsync()
    {
        var lockedSource = Path.GetFullPath("record-finalize-" + Guid.NewGuid().ToString("N") + ".tmp"); var unlockedDestination = lockedSource + ".mp4"; File.WriteAllText(lockedSource, "fixture");
        Task finishing;
        using (var locked = new FileStream(lockedSource, FileMode.Open, FileAccess.Read, FileShare.None)) { finishing = ScreenRecordingService.FinalizeFileAsync(lockedSource, unlockedDestination); await Task.Delay(120); Check(!finishing.IsCompleted, "Sharing violation did not wait for writer release"); }
        await finishing; Check(File.ReadAllText(unlockedDestination) == "fixture", "Finalization changed bytes");
        using (var controller = new DesktopTools.AppController(true))
        {
            foreach (var language in DesktopTools.Localization.L.Languages)
            foreach (var theme in new[] { "Light", "Dark" })
            {
                DesktopTools.Localization.L.Use(language); controller.UpdateSettings(s => { s.Theme = theme; s.Animations = false; });
                var window = new ScreenRecorderWindow(controller, _ => null);
                try
                {
                    window.Show(); window.UpdateLayout();
                    var start = (System.Windows.Controls.Button)typeof(ScreenRecorderWindow).GetField("start", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
                    Check(!start.IsEnabled && !window.IsRecording, "Recorder allows recording before choosing a source");
                    var defaultMonitor = DesktopTools.Native.MonitorService.GetAll().First(); window.SetSource(new RecordingSelection("Test display", defaultMonitor));
                    start.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    await Task.Delay(70);
                    Check(start.IsEnabled && !window.IsRecording, "Cancelled save left Start disabled");
                    var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    image.Render(window); var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                    using var output = File.Create("recorder-ui-" + language + "-" + theme + ".png"); png.Save(output);
                }
                finally { window.Close(); }
            }
            DesktopTools.Localization.L.Use("en"); controller.OpenScreenRecorder();
            controller.UpdateSettings(s => DesktopTools.Core.FeatureShortcutCatalog.SetEnabled(s, DesktopTools.Core.FeatureShortcutCatalog.All.Single(e => e.Action == "Recorder"), false));
            Check(System.Windows.Application.Current.Windows.OfType<ScreenRecorderWindow>().Any(), "Disabling recorder shortcut closed its window");
            System.Windows.Application.Current.Windows.OfType<ScreenRecorderWindow>().Single().Close();
            for (int i = 0; i < 100 && System.Windows.Application.Current.Windows.OfType<ScreenRecorderWindow>().Any(); i++) await Task.Delay(30);
        }
        using var target = new Forms.Form { TopMost = true, StartPosition = Forms.FormStartPosition.CenterScreen, Width = 640, Height = 360, Text = "DesktopTools silent recorder fixture", BackColor = Drawing.Color.RoyalBlue };
        target.Show(); target.Refresh(); await Task.Delay(700);
        var directory = Path.GetFullPath("recorder-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        using var service = new ScreenRecordingService();
        var states = new ConcurrentQueue<string>(); service.StatusChanged += states.Enqueue;
        var rectangle = new ScreenRect(20, 30, 320, 200);
        Check(rectangle.Width == 320 && rectangle.Height == 200, "Region constructor dimensions");
        var position = target.PointToScreen(new Drawing.Point(30, 40));
        var monitor = DesktopTools.Native.MonitorService.GetAll().First(m => m.Bounds.Contains(position.X, position.Y));
        var source = new DisplayRecordingSource(monitor.Id) { RecorderApi = RecorderApi.WindowsGraphicsCapture, IsCursorCaptureEnabled = false, IsBorderRequired = true,
            SourceRect = new ScreenRect(position.X - monitor.Bounds.X, position.Y - monitor.Bounds.Y, 320, 200) };
        var path = Path.Combine(directory, "capture.mp4");
        try
        {
            var result = service.StartSource(source, path);
            try { _ = service.StartSource(source, path); throw new Exception("Concurrent session accepted"); } catch (InvalidOperationException) { }
            await Task.Delay(1400); service.Pause(); await Task.Delay(650);
            Check(states.Contains("Paused"), "Pause event missing");
            target.BackColor = Drawing.Color.OrangeRed; target.Refresh(); service.Resume(); await Task.Delay(1400); service.Stop(); service.Stop();
            Check(await result.WaitAsync(TimeSpan.FromSeconds(30)) == path, "Destination mismatch");
            service.Dispose();
            var info = await VideoEditorService.ProbeAsync(path);
            Check(info.Width == 320 && info.Height == 200, "Region output dimensions: " + info.Width + "x" + info.Height);
            Check(!info.HasAudio, "Silent fixture unexpectedly has audio");
            Check(info.Duration > 1.5 && info.Duration < 3.4, "Pause removed from duration: " + info.Duration);
            var first = await VideoEditingChecks.PixelAsync(path, .4); var last = await VideoEditingChecks.PixelAsync(path, info.Duration - .3);
            Console.WriteLine("Recorded duration=" + info.Duration + " first=" + string.Join(",", first) + " last=" + string.Join(",", last));
            Check(first[0] > 150 && first[2] < 120, "First recorded frame should be blue: " + string.Join(",", first));
            Check(last[2] > 180 && last[0] < 80, "Last recorded frame should be orange/red: " + string.Join(",", last));
            var bytes = File.ReadAllBytes(path);
            try { _ = service.StartSource(source, path); throw new Exception("Overwrite accepted"); } catch (IOException) { }
            Check(File.ReadAllBytes(path).SequenceEqual(bytes), "Existing output changed");
            Check(!Directory.EnumerateFiles(directory, "*.partial.mp4").Any(), "Successful staging not removed");
            var second = service.StartSource(source, Path.Combine(directory, "second.mp4"), quality: "Economy"); await Task.Delay(900); service.Stop();
            await second.WaitAsync(TimeSpan.FromSeconds(30));
            service.Dispose();
            target.BackColor = Drawing.Color.RoyalBlue; target.Refresh(); await Task.Delay(300);
            var windowPath = Path.Combine(directory, "window-144.mp4");
            var windowSource = new WindowRecordingSource(target.Handle) { IsCursorCaptureEnabled = false, IsBorderRequired = true };
            var windowResult = service.StartSource(windowSource, windowPath, framesPerSecond: 144, quality: "High");
            await Task.Delay(1500);
            target.BackColor = Drawing.Color.OrangeRed; target.Refresh(); await Task.Delay(1500); service.Stop();
            await windowResult.WaitAsync(TimeSpan.FromSeconds(30)); service.Dispose();
            var windowInfo = await VideoEditorService.ProbeAsync(windowPath);
            var blueFrame = await VideoEditingChecks.PixelAsync(windowPath, .5); var orangeFrame = await VideoEditingChecks.PixelAsync(windowPath, windowInfo.Duration - .4);
            var clip = await Windows.Media.Editing.MediaClip.CreateFromFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(windowPath));
            var rate = clip.GetVideoEncodingProperties().FrameRate;
            Console.WriteLine($"Window capture: {windowInfo.Width}x{windowInfo.Height}; encoded FPS={rate.Numerator}/{rate.Denominator}");
            Console.WriteLine("Window pixels first=" + string.Join(",", blueFrame) + " last=" + string.Join(",", orangeFrame));
            Check(blueFrame[0] > 150 && blueFrame[2] < 120 && orangeFrame[2] > 180 && orangeFrame[0] < 80, "Window capture produced black or stale frames");
            Check(rate.Denominator > 0 && rate.Numerator / (double)rate.Denominator is > 0 and <= 145, "Encoder returned invalid frame rate");
            // Requested FPS is a ceiling, not a guarantee of distinct frames from the capture source.
            File.WriteAllText(Path.Combine(directory, "frame-rate.txt"), $"Requested: 144 FPS\nEncoded metadata: {rate.Numerator / (double)rate.Denominator:0.###} FPS\nDistinct frame throughput: not measured\nStatic initial window: no fixture repaint during startup.\n");
            using var uiController = new DesktopTools.AppController(true);
            uiController.Settings.RecordingRegion = true; uiController.Settings.RecordingMicrophone = uiController.Settings.RecordingSystemAudio = false; uiController.Settings.RecordingFramesPerSecond = 30;
            var uiPath = Path.Combine(directory, "ui-session.mp4");
            var ui = new ScreenRecorderWindow(uiController, _ => uiPath); ui.Show();
            try
            {
                T Field<T>(string name) => (T)typeof(ScreenRecorderWindow).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(ui)!;
                ui.SetSource(new RecordingSelection("Fixture region", monitor, new System.Windows.Rect(position.X - monitor.Bounds.X, position.Y - monitor.Bounds.Y, 320, 200)));
                Field<System.Windows.Controls.Button>("start").RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                for (int attempt = 0; attempt < 100 && !System.Windows.Application.Current.Windows.OfType<RecordingHudWindow>().Any(); attempt++) await Task.Delay(30);
                Check(ui.IsRecording && !ui.IsVisible, "Recording did not switch from studio to HUD");
                var hud = System.Windows.Application.Current.Windows.OfType<RecordingHudWindow>().Single();
                await Task.Delay(1100);
                var pauseButton = (System.Windows.Controls.Button)typeof(RecordingHudWindow).GetField("pause", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(hud)!;
                pauseButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Task.Delay(300);
                Check(System.Windows.Automation.AutomationProperties.GetName(pauseButton) == DesktopTools.Localization.L.T("Resume"), "Live HUD did not pause");
                pauseButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Task.Delay(600);
                await ui.StopAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Check(ui.IsVisible && !ui.IsRecording && !System.Windows.Application.Current.Windows.OfType<RecordingHudWindow>().Any(), "Completed session did not restore studio and release HUD");
                var uiInfo = await VideoEditorService.ProbeAsync(uiPath); Check(uiInfo.Width == 320 && uiInfo.Height == 200 && !uiInfo.HasAudio, "UI session output differs from chosen region/audio");
            }
            finally { await ui.StopAsync(); ui.Close(); }
            string disabledPath = Path.Combine(directory, "disabled-feature.mp4"); uiController.Settings.ScreenRecorderEnabled = true; uiController.OpenScreenRecorder(_ => disabledPath);
            var managed = System.Windows.Application.Current.Windows.OfType<ScreenRecorderWindow>().Single(); managed.SetSource(new RecordingSelection("Fixture region", monitor, new System.Windows.Rect(position.X - monitor.Bounds.X, position.Y - monitor.Bounds.Y, 320, 200)));
            ((System.Windows.Controls.Button)typeof(ScreenRecorderWindow).GetField("start", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(managed)!).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            await Task.Delay(900); Check(managed.IsRecording, "Managed recorder did not start");
            uiController.UpdateSettings(s => DesktopTools.Core.FeatureShortcutCatalog.SetEnabled(s, DesktopTools.Core.FeatureShortcutCatalog.All.Single(e => e.Action == "Recorder"), false));
            Check(managed.IsRecording, "Disabling recorder shortcut stopped active recording");
            await managed.StopAsync().WaitAsync(TimeSpan.FromSeconds(30)); managed.Close();
            for (int i = 0; i < 200 && System.Windows.Application.Current.Windows.OfType<ScreenRecorderWindow>().Any(); i++) await Task.Delay(50);
            Check(!System.Windows.Application.Current.Windows.OfType<ScreenRecorderWindow>().Any() && !System.Windows.Application.Current.Windows.OfType<RecordingHudWindow>().Any(), "Stopping recorder left studio or HUD open");
            Check((await VideoEditorService.ProbeAsync(disabledPath)).Width == 320, "Stopping recorder did not finalize MP4");
            using var minimizedService = new ScreenRecordingService();
            var minimizedPath = Path.Combine(directory, "minimized-window.mp4"); var minimizedResult = minimizedService.StartSource(new WindowRecordingSource(target.Handle) { IsCursorCaptureEnabled = false, IsBorderRequired = true }, minimizedPath);
            await Task.Delay(700); target.WindowState = Forms.FormWindowState.Minimized; await Task.Delay(650); Check(minimizedService.SourceSuspended, "Minimized window did not pause capture");
            minimizedService.Pause(); target.WindowState = Forms.FormWindowState.Normal; await Task.Delay(350); Check(!minimizedService.SourceSuspended, "Restored window retained source pause");
            minimizedService.Resume(); await Task.Delay(650); minimizedService.Stop(); await minimizedResult.WaitAsync(TimeSpan.FromSeconds(15));
            var minimizedInfo = await VideoEditorService.ProbeAsync(minimizedPath); Check(minimizedInfo.Duration < 2, "Minimized interval remained in recording duration");
            minimizedService.Dispose();
            using var closingService = new ScreenRecordingService();
            var interrupted = closingService.StartSource(new WindowRecordingSource(target.Handle) { IsCursorCaptureEnabled = false, IsBorderRequired = true }, Path.Combine(directory, "closed-window.mp4"));
            await Task.Delay(700); target.Close();
            try { await interrupted.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (IOException) { /* Capture reports source closure and retains the staging file for recovery. */ }
            finally { closingService.Stop(); }
            Check(interrupted.IsCompleted, "Closed window retained an active capture session");
            if (interrupted.IsCompletedSuccessfully) { var ended = await VideoEditorService.ProbeAsync(interrupted.Result); Check(ended.Width > 0 && ended.Height > 0 && ended.Duration > 0, "Closed source left an invalid finalized MP4"); }
        }
        finally { service.Stop(); target.Close(); }
    }
}
