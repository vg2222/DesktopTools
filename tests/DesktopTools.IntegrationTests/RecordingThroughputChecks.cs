using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using DesktopTools.Extras;
using ScreenRecorderLib;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

internal static class RecordingThroughputChecks
{
    // Each paint carries a binary ID. The offline decoder reads every encoded frame,
    // avoiding both metadata-only FPS claims and compression-noise pixel hashes.
    private sealed class CounterWindow : Forms.Form
    {
        internal int Frame;
        internal CounterWindow()
        {
            SetStyle(Forms.ControlStyles.AllPaintingInWmPaint | Forms.ControlStyles.UserPaint | Forms.ControlStyles.OptimizedDoubleBuffer, true);
            ClientSize = new Drawing.Size(640, 360); FormBorderStyle = Forms.FormBorderStyle.None;
            StartPosition = Forms.FormStartPosition.CenterScreen; TopMost = true;
            Text = "DesktopTools numbered recording fixture";
        }
        protected override void OnPaint(Forms.PaintEventArgs e)
        {
            e.Graphics.Clear(Drawing.Color.CornflowerBlue);
            for (int i = 0; i < 18; i++)
            {
                bool white = i == 0 || i >= 2 && (Frame & (1 << (i - 2))) != 0;
                e.Graphics.FillRectangle(white ? Drawing.Brushes.White : Drawing.Brushes.Black, 32 + i * 32, 132, 32, 96);
            }
        }
    }
    internal static Task RunAsync() => RunAsync(false);
    internal static Task RunSustainedAsync() => RunAsync(true);
    private static async Task RunAsync(bool sustainedOnly)
    {
        string directory = Path.GetFullPath("throughput-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        File.WriteAllText("throughput-directory.txt", directory);
        using var target = new CounterWindow(); target.Show(); target.Refresh();
        bool resolution = timeBeginPeriod(1) == 0;
        try
        {
            foreach (var run in new[] { (Name: "hardware-144", Hardware: true, Fps: 144, Seconds: 8),
                (Name: "software-144", Hardware: false, Fps: 144, Seconds: 8),
                (Name: "sustained-60", Hardware: true, Fps: 60, Seconds: 60) })
            {
                if (sustainedOnly && run.Fps != 60) continue;
                target.Frame = 0; target.Refresh(); await Task.Delay(200);
                string path = Path.Combine(directory, run.Name + ".mp4");
                await using var service = new ScreenRecordingService();
                service.StatusChanged += state => File.AppendAllText(Path.Combine(directory, run.Name + ".states.txt"), $"{DateTime.UtcNow:O} {state}\n");
                var result = service.StartSource(new WindowRecordingSource(target.Handle) { IsCursorCaptureEnabled = false, IsBorderRequired = true },
                    path, framesPerSecond: run.Fps, hardwareAcceleration: run.Hardware);
                var clock = Stopwatch.StartNew();
                int lastProgress = -1;
                while (clock.Elapsed.TotalSeconds < run.Seconds)
                {
                    if (result.IsCompleted) { await result; throw new Exception("Recording stopped before the requested interval."); }
                    target.Frame++; target.Refresh(); await Task.Delay(1);
                    int progress = (int)clock.Elapsed.TotalSeconds / 10;
                    if (progress != lastProgress) { lastProgress = progress; File.AppendAllText(Path.Combine(directory, run.Name + ".states.txt"), $"{DateTime.UtcNow:O} Source paints={target.Frame}; elapsed={service.Elapsed.TotalSeconds:0.00}; state={service.Status}\n"); }
                }
                service.Stop();
                try { await result.WaitAsync(TimeSpan.FromSeconds(30)); }
                catch (Exception ex) { File.AppendAllText(Path.Combine(directory, run.Name + ".states.txt"), ex.ToString()); throw; }
                var info = await VideoEditorService.ProbeAsync(path);
                if (info.Width != 640 || info.Height != 360 || info.HasAudio || info.Duration < run.Seconds - 2)
                    throw new Exception("Incomplete numbered recording: " + JsonSerializer.Serialize(info));
                File.WriteAllText(Path.ChangeExtension(path, ".json"), JsonSerializer.Serialize(new { run.Name, run.Hardware, run.Fps,
                    RequestedSeconds = run.Seconds, PaintedFrames = target.Frame, WallSeconds = clock.Elapsed.TotalSeconds, info.Duration,
                    Width = 640, Height = 360, Note = "Silent owned window; source paint rate is not captured-frame throughput." }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{run.Name}: {info.Duration:0.00}s saved; source painted {target.Frame} frames; decode required.");
            }
            File.WriteAllText("throughput-directory.txt", directory);
        }
        finally { if (resolution) timeEndPeriod(1); target.Close(); }
    }
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);
}
