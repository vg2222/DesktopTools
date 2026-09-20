using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DesktopTools;
using DesktopTools.Extras;
using DesktopTools.Native;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

internal static class RecordingStateChecks
{
    internal static async Task RunAsync()
    {
        // Read endpoint availability only; never open capture, play audio or change volume.
        using (var audio = new AudioSessionService())
        {
            foreach (bool microphone in new[] { true, false })
            {
                bool available = audio.ReadEndpoint(microphone) != null;
                bool accepted = true;
                try
                {
                    ScreenRecordingService.ValidateAudioSources(microphone, !microphone);
                }
                catch (InvalidOperationException) when (!available) { accepted = false; }
                Check(accepted == available, "Recording preflight accepted a missing audio device.");
                Console.WriteLine($"Audio preflight: {(microphone ? "input" : "output")} available={available}; no audio captured.");
            }
        }
        using var controller = new AppController(true);
        // The preceding glass check exposes controls. This fixture needs its HUD
        // excluded because it intentionally overlaps the region being measured.
        controller.Settings.HideControlsFromCapture = true;
        controller.Settings.RecordingMicrophone = controller.Settings.RecordingSystemAudio = false;
        controller.Settings.RecordingFramesPerSecond = 30; controller.Settings.RecordingHardwareAcceleration = false;
        var longHud = new RecordingHudWindow("Long recording helper", () => { }, () => { }, true);
        try
        {
            longHud.Show(); longHud.Update(TimeSpan.FromHours(25) + TimeSpan.FromSeconds(3), false, false); longHud.UpdateLayout();
            var label = Field<TextBlock>(longHud, "time"); var icon = Field<Button>(longHud, "pause").Content;
            Check(label.Text == "25:00:03" && label.TextWrapping == TextWrapping.NoWrap && label.ActualHeight < 32, "Long recording time wrapped or restarted after 24 hours");
            longHud.Update(TimeSpan.FromHours(25) + TimeSpan.FromSeconds(4), false, false);
            Check(ReferenceEquals(icon, Field<Button>(longHud, "pause").Content), "Timer tick recreated unchanged controls");
        }
        finally { longHud.Finish(); }
        using var target = new Forms.Form { TopMost = true, FormBorderStyle = Forms.FormBorderStyle.None,
            ClientSize = new Drawing.Size(320, 200), StartPosition = Forms.FormStartPosition.CenterScreen,
            Text = "DesktopTools recorder state helper", BackColor = Drawing.Color.RoyalBlue };
        target.Show(); target.Refresh(); await Task.Delay(150);
        string directory = Path.GetFullPath("recording-state-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var actualMonitors = MonitorService.GetAll(); IReadOnlyList<MonitorInfo> monitors = actualMonitors;
        string path = Path.Combine(directory, "window.mp4"); Action? choosing = null;
        var studio = new ScreenRecorderWindow(controller, _ => { choosing?.Invoke(); return path; }, () => monitors);
        try
        {
            studio.Show();
            studio.SetSource(new RecordingSelection("Owned window", Window: RecordingWindows.Identify(target.Handle)));
            Click(studio, "start"); await Until(() => Field<ScreenRecordingService?>(studio, "service")?.Status == "Recording");
            var service = Field<ScreenRecordingService>(studio, "service");
            monitors = []; await studio.RefreshDisplaysAsync();
            Check(studio.IsRecording, "Unrelated display change stopped the window source");
            await Task.Delay(750); Click(Application.Current.Windows.OfType<RecordingHudWindow>().Single(), "pause");
            await Until(() => service.Status == "Paused"); TimeSpan pausedTime = service.Elapsed;
            target.WindowState = Forms.FormWindowState.Minimized; await Until(() => service.SourceSuspended);
            target.WindowState = Forms.FormWindowState.Normal; await Until(() => !service.SourceSuspended);
            await Task.Delay(250);
            Check(service.Status == "Paused" && service.Elapsed - pausedTime < TimeSpan.FromMilliseconds(20), "Restoring source resumed a manual pause or its clock");
            Click(Application.Current.Windows.OfType<RecordingHudWindow>().Single(), "pause"); await Until(() => service.Status == "Recording");
            await Task.Delay(350); Check(service.Elapsed > pausedTime + TimeSpan.FromMilliseconds(200), "Resume did not restart clock");
            await studio.StopAsync().WaitAsync(TimeSpan.FromSeconds(20)); TimeSpan stoppedTime = service.Elapsed;
            await Task.Delay(120); Check(service.Elapsed == stoppedTime, "Finalization advanced the clock");
            Check((await VideoEditorService.ProbeAsync(path)).Width == 320, "Window output was not finalized");

            monitors = actualMonitors;
            var position = target.PointToScreen(Drawing.Point.Empty);
            var monitor = actualMonitors.First(m => m.Bounds.Contains(position.X, position.Y));
            var selection = new RecordingSelection("Owned region", monitor, new Rect(position.X - monitor.Bounds.X, position.Y - monitor.Bounds.Y, 320, 200));
            path = Path.Combine(directory, "invalid-after-picker.mp4");
            studio.SetSource(selection); choosing = () => monitors = [];
            Click(studio, "start"); await Until(() => Field<Task?>(studio, "session") == null);
            Check(!studio.IsRecording && !File.Exists(path), "Stale display started after save dialog");

            monitors = actualMonitors; path = Path.Combine(directory, "cancel-during-picker.mp4"); studio.SetSource(selection);
            Task? cancelled = null; choosing = () => cancelled = studio.StopAsync();
            Click(studio, "start"); await Until(() => Field<Task?>(studio, "session") == null);
            if (cancelled != null) await cancelled.WaitAsync(TimeSpan.FromSeconds(3));
            Check(!studio.IsRecording && !File.Exists(path), "Stop during save dialog still started recording");

            choosing = null; path = Path.Combine(directory, "display-changed.mp4"); studio.SetSource(selection);
            Click(studio, "start"); await Until(() => Field<ScreenRecordingService?>(studio, "service")?.Status == "Recording");
            service = Field<ScreenRecordingService>(studio, "service"); await Task.Delay(1100);
            monitors = actualMonitors.Select(m => m with { ScaleX = 1.5, ScaleY = 1.5 }).ToArray();
            await studio.RefreshDisplaysAsync(); Check(studio.IsRecording, "Scale-only change invalidated unchanged source pixels");
            monitors = []; await studio.RefreshDisplaysAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Check(!studio.IsRecording && service.StopReason == "Source display changed. Recording stopped." && Field<RecordingSelection?>(studio, "selectedSource") == null,
                "Invalid display did not stop and clear the source with a reason");
            var info = await VideoEditorService.ProbeAsync(path); var pixel = await VideoEditingChecks.PixelAsync(path, info.Duration / 2);
            Check(info.Width == 320 && info.Height == 200 && pixel[0] > 150 && pixel[2] < 120, "Display-change finalization lost captured helper pixels");
            await Until(() => !Application.Current.Windows.OfType<RecordingHudWindow>().Any());
            path = Path.Combine(directory, "source-closed.mp4");
            studio.SetSource(new RecordingSelection("Closing helper", Window: RecordingWindows.Identify(target.Handle)));
            Click(studio, "start"); await Until(() => Field<ScreenRecordingService?>(studio, "service")?.Status == "Recording");
            service = Field<ScreenRecordingService>(studio, "service"); await Task.Delay(1000); target.Close();
            await Until(() => Field<Task?>(studio, "session") == null);
            Check(service.StopReason == "Source window closed. Recording stopped." && (await VideoEditorService.ProbeAsync(path)).Width == 320,
                "Closing source did not finalize with its stop reason");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Recorder fixture status: " + Field<TextBlock>(studio, "status").Text
                + $"; session={Field<Task?>(studio, "session")?.Status}; closing={Field<bool>(studio, "closing")}; stopping={Field<bool>(studio, "stopping")}; startEnabled={Field<Button>(studio, "start").IsEnabled}; source={Field<RecordingSelection?>(studio, "selectedSource")}", ex);
        }
        finally { await studio.StopAsync(); studio.Close(); target.Close(); }
    }
    private static T Field<T>(object target, string field) => (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Click(object target, string field) => Field<Button>(target, field).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 200; i++) { await Task.Delay(40); if (condition()) return; }
        throw new TimeoutException("Recorder state did not settle");
    }
}
