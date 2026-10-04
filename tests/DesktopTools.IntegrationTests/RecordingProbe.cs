using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Extras;
using DesktopTools.Native;

/// <summary>
/// Records a full display (the way the app does) while that display shows a moving, numbered fixture, then writes a JSON
/// next to the MP4. scripts/analyze-recording-probe.py decodes every frame to count lost, repeated and corrupt frames.
/// Settings come from environment variables so one build can run any combination:
/// PROBE_MONITOR (DISPLAY1 = MSI, DISPLAY2 = primary), PROBE_FPS, PROBE_QUALITY, PROBE_HW (1/0), PROBE_SECONDS, PROBE_OUT.
/// </summary>
internal static class RecordingProbe
{
    private sealed class Fixture : FrameworkElement
    {
        internal int Frame;
        // Moving interference pattern from integer maths only, so the analyzer can rebuild any frame exactly from its ID.
        internal const int TexW = 560, TexH = 300;
        private static readonly int[] Sin = Enumerable.Range(0, 1024).Select(i => (int)Math.Round(127 * Math.Sin(2 * Math.PI * i / 1024))).ToArray();
        private readonly WriteableBitmap texture = new(TexW, TexH, 96, 96, PixelFormats.Bgra32, null);
        private readonly byte[] texBuffer = new byte[TexW * TexH * 4];
        private void DrawTexture()
        {
            int f = Frame;
            for (int y = 0; y < TexH; y++)
                for (int x = 0; x < TexW; x++)
                {
                    int s1 = Sin[(x * 5 + f * 3) & 1023], s2 = Sin[(y * 4 - f * 2) & 1023], s3 = Sin[((x + y) * 2 + f) & 1023];
                    int o = (y * TexW + x) * 4;
                    texBuffer[o] = (byte)(128 + (s1 + s2) / 2); texBuffer[o + 1] = (byte)(128 + (s2 + s3) / 2); texBuffer[o + 2] = (byte)(128 + (s1 + s3) / 2); texBuffer[o + 3] = 255;
                }
            texture.WritePixels(new Int32Rect(0, 0, TexW, TexH), texBuffer, TexW * 4, 0);
        }
        private readonly DrawingGroup staticPart;
        private readonly Typeface face = new("Segoe UI");
        private readonly FormattedText[] scroll;
        internal Fixture(Size size)
        {
            Width = size.Width; Height = size.Height;
            var group = new DrawingGroup();
            using (var dc = group.Open())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(18, 22, 40), Color.FromRgb(36, 70, 120), 35), null, new Rect(0, 0, size.Width, size.Height));
                // Fine static detail: small text and a one-pixel grid. Heavy compression smears these first.
                double top = size.Height * 0.55;
                for (int line = 0; line < 14; line++)
                {
                    var text = new FormattedText($"Line {line:00}  The quick brown fox jumps over the lazy dog 0123456789 — DesktopTools recording probe", CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, face, 15, Brushes.White, 1);
                    dc.DrawText(text, new Point(60, top + line * 22));
                }
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(200, 210, 230)), 1);
                for (int x = 0; x < 600; x += 12) dc.DrawLine(pen, new Point(size.Width - 700 + x, top), new Point(size.Width - 700 + x, top + 300));
                for (int y = 0; y < 300; y += 12) dc.DrawLine(pen, new Point(size.Width - 700, top + y), new Point(size.Width - 100, top + y));
            }
            group.Freeze(); staticPart = group;
            scroll = Enumerable.Range(0, 6).Select(i => new FormattedText($"MOTION {i}  scrolling headline text moves every frame", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 54, Brushes.White, 1)).ToArray();
        }
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawDrawing(staticPart);
            // Frame ID: 20 big cells in the top-left (black = 0, white = 1), cell 0 and 1 are fixed anchors.
            for (int i = 0; i < 20; i++)
            {
                bool white = i == 0 || i >= 2 && (Frame & (1 << (i - 2))) != 0;
                dc.DrawRectangle(white ? Brushes.White : Brushes.Black, null, new Rect(40 + i * 40, 40, 40, 90));
            }
            if (Environment.GetEnvironmentVariable("PROBE_TEXTURE") != "0") { DrawTexture(); dc.DrawImage(texture, new Rect(Width - 620, 40, TexW, TexH)); }
            double w = Width;
            dc.DrawRectangle(Brushes.OrangeRed, null, new Rect(Frame * 9 % (w - 60), 160, 60, 140)); // fast bar
            for (int i = 0; i < scroll.Length; i++)
                dc.DrawText(scroll[i], new Point(((Frame * 6 + i * 700) % (w + 1400)) - 1400, 340 + i * 70));
        }
    }

    /// <summary>Bitrate targets must grow with resolution, rate and preset, and stay inside sane bounds.</summary>
    internal static Task CheckBitratesAsync()
    {
        void Check(bool ok, string what) { if (!ok) throw new Exception("Bitrate rule failed: " + what); }
        int Kbps(double w, double h, int fps, string q) => ScreenRecordingService.BitrateKbps(new Size(w, h), fps, q);
        Check(Kbps(1920, 1080, 60, "Economy") < Kbps(1920, 1080, 60, "Balanced") && Kbps(1920, 1080, 60, "Balanced") < Kbps(1920, 1080, 60, "High"), "presets must rise");
        Check(Kbps(1920, 1080, 30, "High") < Kbps(1920, 1080, 60, "High") && Kbps(1920, 1080, 60, "High") < Kbps(1920, 1080, 144, "High"), "frame rate must raise the target");
        Check(Kbps(1920, 1080, 60, "High") < Kbps(2560, 1440, 60, "High") && Kbps(2560, 1440, 60, "High") < Kbps(3840, 2160, 60, "High"), "resolution must raise the target");
        Check(Kbps(2560, 1440, 144, "High") is >= 30000 and <= 80000, "1440p144 High should be a few tens of Mbit/s, was " + Kbps(2560, 1440, 144, "High"));
        Check(Kbps(320, 200, 24, "Economy") == 4000 && Kbps(7680, 4320, 144, "High") == 80000, "clamps");
        Console.WriteLine($"Bitrates: 1080p60 Balanced {Kbps(1920, 1080, 60, "Balanced")} kbit/s, 1440p144 High {Kbps(2560, 1440, 144, "High")} kbit/s");
        return Task.CompletedTask;
    }

    internal static async Task RunAsync()
    {
        string E(string name, string fallback) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;
        string monitorName = "\\\\.\\" + E("PROBE_MONITOR", "DISPLAY1");
        int fps = int.Parse(E("PROBE_FPS", "144")); string quality = E("PROBE_QUALITY", "High");
        bool hardware = E("PROBE_HW", "1") == "1"; int seconds = int.Parse(E("PROBE_SECONDS", "60"));
        string directory = Path.GetFullPath(E("PROBE_OUT", "probe-" + DateTime.Now.ToString("HHmmss"))); Directory.CreateDirectory(directory);
        string tag = E("PROBE_TAG", "");
        string? logPath = E("PROBE_LOG", "") == "1" ? Path.Combine(directory, tag + "recorder.log") : null;
        ScreenRecordingService.Tuning = new RecordingTuning(
            E("PROBE_API", "") switch { "dd" => ScreenRecorderLib.RecorderApi.DesktopDuplication, "wgc" => ScreenRecorderLib.RecorderApi.WindowsGraphicsCapture, _ => null },
            E("PROBE_THROTTLE", "") switch { "off" => true, "on" => false, _ => null },
            E("PROBE_LOWLAT", "") switch { "1" => true, "0" => false, _ => null },
            E("PROBE_KBPS", "") is { Length: > 0 } k ? int.Parse(k) : null,
            E("PROBE_MODE", "") switch { "cbr" => ScreenRecorderLib.H264BitrateControlMode.CBR, "vbr" => ScreenRecorderLib.H264BitrateControlMode.UnconstrainedVBR, "q" => ScreenRecorderLib.H264BitrateControlMode.Quality, _ => null },
            E("PROBE_PROFILE", "") switch { "main" => ScreenRecorderLib.H264Profile.Main, "high" => ScreenRecorderLib.H264Profile.High, "base" => ScreenRecorderLib.H264Profile.Baseline, _ => null },
            E("PROBE_FIXED", "") switch { "1" => true, "0" => false, _ => null }, null, logPath);
        string name = $"{tag}{E("PROBE_MONITOR", "DISPLAY1").ToLowerInvariant()}-{fps}fps-{quality}-{(hardware ? "hw" : "sw")}";
        var monitor = MonitorService.GetAll().Single(m => m.Id == monitorName);

        var fixture = new Fixture(new Size(monitor.Bounds.Width / monitor.ScaleX, monitor.Bounds.Height / monitor.ScaleY));
        var window = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, AllowsTransparency = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Content = fixture, Background = Brushes.Black,
            Left = monitor.Bounds.X / monitor.ScaleX, Top = monitor.Bounds.Y / monitor.ScaleY, Width = fixture.Width, Height = fixture.Height
        };
        window.SourceInitialized += (_, _) => MonitorService.PlaceWindow(window, monitor);
        window.Show();
        // Capture exclusion check: a magenta window that asks to be hidden from capture, and a cyan control that does not.
        Window? hidden = null, shown = null;
        if (E("PROBE_EXCLUDE", "") == "1")
        {
            Window Box(double x, Brush color)
            {
                var box = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, ShowActivated = false, Background = color,
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = monitor.Bounds.X / monitor.ScaleX + x, Top = monitor.Bounds.Y / monitor.ScaleY + 560, Width = 300, Height = 200 };
                return box;
            }
            hidden = Box(700, Brushes.Magenta); shown = Box(1100, Brushes.Cyan);
            hidden.SourceInitialized += (_, _) => NativeWindowService.TryExcludeFromCapture(hidden, true, out _);
            hidden.Show(); shown.Show();
        }
        var renderTimes = new System.Collections.Generic.List<double>();
        var clock = new Stopwatch();
        EventHandler onRender = (_, _) => { fixture.Frame++; fixture.InvalidateVisual(); if (clock.IsRunning) renderTimes.Add(clock.Elapsed.TotalMilliseconds); };
        System.Windows.Media.CompositionTarget.Rendering += onRender;
        await Task.Delay(800);

        // A pristine copy of the static detail, for a quality score after decoding.
        var shot = new RenderTargetBitmap((int)monitor.Bounds.Width, (int)monitor.Bounds.Height, 96 * monitor.ScaleX, 96 * monitor.ScaleY, PixelFormats.Pbgra32);
        shot.Render(fixture);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(shot));
        using (var s = File.Create(Path.Combine(directory, name + ".reference.png"))) encoder.Save(s);

        string path = Path.Combine(directory, name + ".mp4");
        await using var service = new ScreenRecordingService();
        var states = new System.Collections.Generic.List<string>();
        service.StatusChanged += state => states.Add($"{DateTime.UtcNow:O} {state}");
        bool resolution = timeBeginPeriod(1) == 0;
        string audio = E("PROBE_AUDIO", "");
        var result = service.Start(monitor, null, path, audio is "1" or "mic", audio is "1" or "sys", fps, quality, hardware);
        clock.Start();
        int startFrame = fixture.Frame;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            if (result.IsCompleted) { await result; throw new Exception("Recording stopped early."); }
            await Task.Delay(100);
        }
        int endFrame = fixture.Frame;
        service.Stop();
        await result.WaitAsync(TimeSpan.FromSeconds(60));
        double wall = clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering -= onRender;
        if (resolution) timeEndPeriod(1);
        hidden?.Close(); shown?.Close(); window.Close();

        var intervals = renderTimes.Zip(renderTimes.Skip(1), (a, b) => b - a).OrderBy(x => x).ToArray();
        File.WriteAllText(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(new
        {
            name, monitor = monitorName, fps, quality, hardware, seconds, wallSeconds = wall,
            width = monitor.Bounds.Width, height = monitor.Bounds.Height,
            sourceFrames = endFrame - startFrame, sourceFps = (endFrame - startFrame) / wall,
            sourceIntervalMedianMs = intervals.Length > 0 ? intervals[intervals.Length / 2] : 0, sourceIntervalP99Ms = intervals.Length > 0 ? intervals[(int)(intervals.Length * .99)] : 0,
            exclusion = hidden == null ? null : new { x = 700 * monitor.ScaleX, y = 560 * monitor.ScaleY, w = 300 * monitor.ScaleX, h = 200 * monitor.ScaleY, hiddenX = 700 * monitor.ScaleX, shownX = 1100 * monitor.ScaleX },
            startFrame, endFrame, states, file = Path.GetFileName(path), reference = name + ".reference.png"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{name}: wall {wall:0.0}s, source painted {(endFrame - startFrame) / wall:0.0} FPS -> {path}");
        File.WriteAllText("probe-directory.txt", directory);
    }

    [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);
}
