using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using DesktopTools;
using DesktopTools.Extras;
using DesktopTools.UI;

internal static class VideoSeekChecks
{
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static void Invoke(object instance, string name) => instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, null);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        controller.UpdateSettings(s => { s.Theme = "Dark"; s.Animations = false; });
        string path = await VideoEditingChecks.FixtureAsync(Path.GetFullPath("video-seek-" + Guid.NewGuid().ToString("N")));
        var window = new VideoEditorWindow(message => throw new Exception(message));
        try
        {
            window.Show(); await window.LoadAsync(path);
            for (int i = 0; i < 50 && !Field<bool>(window, "mediaOpened"); i++) await Task.Delay(80);
            Check(Field<bool>(window, "mediaOpened"), "Seek fixture playback did not open");
            var slider = Field<Slider>(window, "seek"); var media = Field<MediaElement>(window, "media");
            var timer = Field<DispatcherTimer>(window, "seekTimer"); var timeline = Field<VideoTrimTimeline>(window, "timeline");
            var initial = media.Position;
            for (int i = 1; i <= 200; i++) slider.Value = i / 100d;
            Check(media.Position == initial && Field<double?>(window, "pendingSeek") == 2 && timer.IsEnabled, "Rapid slider changes reached the decoder before the scheduled seek");
            await Task.Delay(120);
            Check(Field<double?>(window, "pendingSeek") == null && !timer.IsEnabled && Math.Abs(media.Position.TotalSeconds - 2) < .1, "Scheduled seek did not apply the latest position");

            double start = timeline.Start, end = timeline.End;
            double X(double time) => 10 + (timeline.ActualWidth - 20) * time / timeline.Duration;
            Check(timeline.BeginScrub(X(.4)), "Timeline could not start scrubbing");
            timeline.DragTo(X(1.25)); timeline.EndDrag();
            Check(!timeline.IsScrubbing && Field<double?>(window, "pendingSeek") == null && Math.Abs(media.Position.TotalSeconds - 1.25) < .1, "Releasing playhead did not flush its final seek");
            Check(timeline.Start == start && timeline.End == end, "Playhead drag changed the trim range");
            timeline.BeginScrub(X(.2)); timeline.DragTo(X(3)); timeline.EndDrag(cancel: true);
            Check(Math.Abs(timeline.Position - 1.25) < .001 && Math.Abs(media.Position.TotalSeconds - 1.25) < .1, "Cancelled scrub did not restore its prior position");

            slider.Value = 2.4; window.Hide();
            Check(Field<double?>(window, "pendingSeek") == null && !timer.IsEnabled, "Hidden editor retained a seek request");
            window.Show(); slider.Value = 2.6; Invoke(window, "ShowOriginal");
            Check(Field<double?>(window, "pendingSeek") == null && !timer.IsEnabled, "Playback source change retained a stale seek");
            slider.Value = 1.6;
            for (int i = 0; i < 50 && !Field<bool>(window, "mediaOpened"); i++) await Task.Delay(80);
            await Task.Delay(80);
            Check(Math.Abs(media.Position.TotalSeconds - 1.6) < .1 && Field<double?>(window, "pendingSeek") == null, "Seek requested while playback opened was lost");
            var play = Field<Button>(window, "playButton"); var icon = play.Content;
            Invoke(window, "PausePlayback"); Invoke(window, "PausePlayback");
            Check(ReferenceEquals(icon, play.Content), "Idle pause unnecessarily recreated the play icon");
            slider.Value = 2.7; window.Close();
            Check(!timer.IsEnabled && Field<double?>(window, "pendingSeek") == null, "Closed editor retained a seek request");
        }
        finally { if (window.IsVisible) window.Close(); }
    }
}
