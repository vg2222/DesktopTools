using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using DesktopTools.Extras;
using DesktopTools.Native;

internal static class RecordingMemoryChecks
{
    private sealed record Sample(double Seconds, double PrivateMiB, double WorkingMiB, int Handles);

    internal static async Task FixtureAsync()
    {
        string directory = Environment.GetEnvironmentVariable("PROBE_OUT")!;
        string? oldPrimary = Environment.GetEnvironmentVariable("DESKTOPTOOLS_TEST_PRIMARY");
        Environment.SetEnvironmentVariable("DESKTOPTOOLS_TEST_PRIMARY", "1");
        using var target = new RecordingThroughputChecks.CounterWindow(fullScreen: true);
        bool timerResolution = timeBeginPeriod(1) == 0;
        var clock = Stopwatch.StartNew();
        int seconds = int.Parse(Environment.GetEnvironmentVariable("PROBE_SECONDS") ?? "330") + 120;
        using var toneBytes = new MemoryStream();
        using var tone = new System.Media.SoundPlayer(toneBytes);
        try
        {
            if (Environment.GetEnvironmentVariable("PROBE_TONE") == "1")
            {
                using (var wave = new BinaryWriter(toneBytes, System.Text.Encoding.ASCII, leaveOpen: true))
                {
                    const int rate = 48000, count = rate * 2;
                    wave.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); wave.Write(36 + count * 2);
                    wave.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); wave.Write(16); wave.Write((short)1); wave.Write((short)1);
                    wave.Write(rate); wave.Write(rate * 2); wave.Write((short)2); wave.Write((short)16);
                    wave.Write(System.Text.Encoding.ASCII.GetBytes("data")); wave.Write(count * 2);
                    for (int i = 0; i < count; i++) wave.Write((short)(1000 * Math.Sin(2 * Math.PI * 440 * i / rate)));
                }
                toneBytes.Position = 0; tone.PlayLooping();
            }
            target.Show(); target.Refresh(); await Task.Delay(300);
            File.WriteAllText(Path.Combine(directory, "fixture.ready"), "ready");
            int nextCheck = 0;
            while (clock.Elapsed.TotalSeconds < seconds && target.Visible)
            {
                target.Advance();
                if (clock.ElapsedMilliseconds >= nextCheck)
                {
                    if (File.Exists(Path.Combine(directory, "fixture.stop"))) break;
                    nextCheck += 100;
                }
                await Task.Delay(1);
            }
        }
        finally
        {
            tone.Stop();
            using var process = Process.GetCurrentProcess(); process.Refresh();
            File.WriteAllText(Path.Combine(directory, "fixture.json"), JsonSerializer.Serialize(new { target.Frame, Seconds = clock.Elapsed.TotalSeconds,
                PrivateMiB = process.PrivateMemorySize64 / 1048576d, WorkingMiB = process.WorkingSet64 / 1048576d }));
            target.Close();
            if (timerResolution) timeEndPeriod(1);
            Environment.SetEnvironmentVariable("DESKTOPTOOLS_TEST_PRIMARY", oldPrimary);
        }
    }

    // GDI source generation avoids the WPF texture uploader sharing the recorder's
    // process-memory measurement. All captured pixels belong to this full-screen fixture.
    internal static async Task RunAsync()
    {
        string E(string key, string fallback) => Environment.GetEnvironmentVariable(key) is { Length: > 0 } value ? value : fallback;
        int fps = int.Parse(E("PROBE_FPS", "30")), seconds = int.Parse(E("PROBE_SECONDS", "330"));
        string directory = Path.GetFullPath(E("PROBE_OUT", "recorder-memory-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        bool audio = E("PROBE_AUDIO", "sys") == "sys";
        bool hardware = E("PROBE_HW", "1") == "1";
        bool externalFixture = E("PROBE_EXTERNAL_FIXTURE", "0") == "1";
        string? oldPrimary = Environment.GetEnvironmentVariable("DESKTOPTOOLS_TEST_PRIMARY");
        Process? fixture = null;
        string quality = E("PROBE_QUALITY", "Balanced");
        var originalTuning = ScreenRecordingService.Tuning;
        ScreenRecordingService.Tuning = new RecordingTuning(
            DisableThrottling: E("PROBE_THROTTLE", "") switch { "on" => false, "off" => true, _ => null },
            LowLatency: E("PROBE_LOWLAT", "") switch { "1" => true, "0" => false, _ => null },
            LogPath: Path.Combine(directory, "recorder.log"));
        Environment.SetEnvironmentVariable("DESKTOPTOOLS_TEST_PRIMARY", "1");
        using var target = new RecordingThroughputChecks.CounterWindow(fullScreen: true);
        using var process = Process.GetCurrentProcess();
        var samples = new List<Sample>();
        var service = new ScreenRecordingService();
        Task<string>? result = null;
        bool timerResolution = timeBeginPeriod(1) == 0;
        var clock = new Stopwatch();
        string path = Path.Combine(directory, "recording.mp4");
        bool exceededBudget = false;
        int lastSample = -1;
        double duration = 0;
        try
        {
            if (!externalFixture)
            {
                foreach (string marker in new[] { "fixture.ready", "fixture.stop" }) File.Delete(Path.Combine(directory, marker));
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--record-fixture-only");
                start.Environment["PROBE_OUT"] = directory;
                start.Environment["PROBE_SECONDS"] = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                fixture = Process.Start(start) ?? throw new Exception("Cannot start isolated capture fixture.");
                var startup = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(directory, "fixture.ready")))
                {
                    if (fixture.HasExited || startup.Elapsed.TotalSeconds > 15) throw new Exception("Isolated capture fixture did not become ready.");
                    await Task.Delay(50);
                }
            }
            var monitor = MonitorService.GetAll().Single(m => m.Id == System.Windows.Forms.Screen.PrimaryScreen!.DeviceName);
            result = service.Start(monitor, null, path, false, audio, fps, quality, hardware);
            clock.Start();
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                if (result.IsCompleted) { await result; throw new Exception("Memory test recording stopped early."); }
                int now = (int)clock.Elapsed.TotalSeconds;
                if (now != lastSample)
                {
                    lastSample = now; process.Refresh();
                    var sample = new Sample(clock.Elapsed.TotalSeconds, process.PrivateMemorySize64 / 1048576d, process.WorkingSet64 / 1048576d, process.HandleCount);
                    samples.Add(sample);
                    if (now % 30 == 0) Console.WriteLine("MEASURE " + JsonSerializer.Serialize(sample));
                    if (sample.PrivateMiB > double.Parse(E("PROBE_MAX_PRIVATE_MIB", "1536"), System.Globalization.CultureInfo.InvariantCulture)) { exceededBudget = true; break; }
                }
                await Task.Delay(1);
            }
            duration = clock.Elapsed.TotalSeconds;
            service.Stop(); await result.WaitAsync(TimeSpan.FromSeconds(60));
            var info = await VideoEditorService.ProbeAsync(path);
            if (info.Width != target.Width || info.Height != target.Height || info.HasAudio != audio || info.Duration < duration - 3)
                throw new Exception("Incomplete recorder memory fixture: " + JsonSerializer.Serialize(info));
            File.WriteAllText(Path.Combine(directory, "clip.json"), JsonSerializer.Serialize(new
            {
                fps, quality, hardware, audio, seconds, recordedSeconds = duration,
                sourceMeasurement = "fixture.json (isolated source process)", width = target.Width, height = target.Height,
                info.Duration, file = "recording.mp4", cellX = 32, cellWidth = 32, cellY = 180, bits = 16,
                samples, exceededBudget
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"MEASURE recording: target={fps}, duration={info.Duration:F2}s, peak-private={samples.Max(s => s.PrivateMiB):F1}MiB");
            if (exceededBudget) throw new Exception("Recording exceeded its memory safety budget.");
            CheckPlateau(samples);
        }
        finally
        {
            service.Stop();
            try { if (result != null) await result.WaitAsync(TimeSpan.FromSeconds(60)); }
            finally
            {
                await service.DisposeAsync(); target.Close();
                if (timerResolution) timeEndPeriod(1);
                ScreenRecordingService.Tuning = originalTuning;
                Environment.SetEnvironmentVariable("DESKTOPTOOLS_TEST_PRIMARY", oldPrimary);
                if (fixture != null)
                {
                    File.WriteAllText(Path.Combine(directory, "fixture.stop"), "stop");
                    try { await fixture.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (TimeoutException) { fixture.Kill(); await fixture.WaitForExitAsync(); }
                    finally { fixture.Dispose(); }
                }
                File.WriteAllText(Path.Combine(directory, "samples.json"), JsonSerializer.Serialize(samples));
            }
        }
    }

    internal static Task VerifySavedAsync()
    {
        string directory = Environment.GetEnvironmentVariable("PROBE_OUT") ?? throw new Exception("Set PROBE_OUT to an existing generated measurement.");
        var samples = JsonSerializer.Deserialize<List<Sample>>(File.ReadAllText(Path.Combine(directory, "samples.json")))!;
        using var clip = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "clip.json")));
        if (clip.RootElement.GetProperty("exceededBudget").GetBoolean() ||
            clip.RootElement.GetProperty("recordedSeconds").GetDouble() < clip.RootElement.GetProperty("seconds").GetDouble() - 1)
            throw new Exception("Saved recording did not finish the requested measurement.");
        CheckPlateau(samples);
        return Task.CompletedTask;
    }

    private static void CheckPlateau(List<Sample> samples)
    {
        if (samples.Count <= 25) throw new Exception("At least 26 seconds are required for a memory plateau measurement.");
        // Long measurements evaluate the final minute. Initial driver/encoder
        // caches and lazy bounded surfaces can warm up well after second 20;
        // this must not be confused with sustained growth after five minutes.
        bool longRun = samples[^1].Seconds >= 180;
        double start = longRun ? samples[^1].Seconds - 60 : 20;
        var tail = samples.Where(s => s.Seconds >= start).ToArray();
        double growth = tail[^1].PrivateMiB - tail[0].PrivateMiB;
        double limit = longRun ? 32 : 128;
        Console.WriteLine($"MEASURE plateau: final-{tail[^1].Seconds - tail[0].Seconds:F0}s growth={growth:F1}MiB; peak={samples.Max(s => s.PrivateMiB):F1}MiB");
        if (growth > limit) throw new Exception($"Recorder did not reach a memory plateau: growth={growth:F1}MiB, limit={limit:F0}MiB.");
    }

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);
}
