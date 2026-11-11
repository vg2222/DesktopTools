using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Native;
using DesktopTools.UI;

/// <summary>
/// Repeats one ordinary action many times at real screen size and samples memory, so intended retention,
/// late release (memory a collection gives back) and true leaks (memory that survives a collection) can be told apart.
/// It measures; it does not gate. Only an owned window that covers the chosen monitor is captured.
/// </summary>
internal static class MemorySoakChecks
{
    internal sealed record Sample(int Round, long PrivateBefore, long PrivateAfter, long ManagedAfter, uint Gdi, uint User, int Handles);
    internal enum Trend { Bounded, LateRelease, Leak }

    private const long MiB = 1048576;
    private static string CopyMode = "formats";
    private static readonly string[] AllScenarios = ["notice-release", "capture", "capture-save", "editor", "ocr", "background", "utilities"];

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    public static async Task RunAsync(Action<string> result, string[] args)
    {
        SelfCheck();
        result("PASS memory soak classifier self-check");
        int rounds = IntArg(args, "--rounds", 200);
        int collectEvery = IntArg(args, "--collect-every", 10);
        int idleSeconds = IntArg(args, "--idle-seconds", 60);
        int maxPrivateMiB = IntArg(args, "--max-private-mib", 6144);
        string? only = StringArg(args, "--scenario");
        CopyMode = StringArg(args, "--copy") ?? "formats";
        Check(CopyMode is "formats" or "test-writer", "--copy must be formats or test-writer");
        Check(rounds >= 8, "--rounds must be at least 8");
        Check(collectEvery >= 1, "--collect-every must be at least 1");
        if (only != null) Check(AllScenarios.Contains(only), "Unknown scenario " + only + "; expected one of " + string.Join(", ", AllScenarios));
        string wanted = Environment.GetEnvironmentVariable("DESKTOPTOOLS_CHECK_MONITOR") is { Length: > 0 } id ? id : "DISPLAY1";
        var monitors = MonitorService.GetAll();
        var monitor = monitors.FirstOrDefault(m => m.Id.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Monitor {wanted} not found; connected: {string.Join(", ", monitors.Select(m => m.Id))}");
        if (only is null or "notice-release")
        {
            await CheckNoticeRelease(result);
            if (only != null) return;
        }
        string output = Path.Combine(Environment.CurrentDirectory);
        foreach (var name in only == null ? AllScenarios.Skip(1) : [only])
            await RunScenarioAsync(name, monitor, rounds, collectEvery, idleSeconds, maxPrivateMiB, output, line => { result(line); Console.WriteLine(line); });
    }

    // ---- classification -------------------------------------------------------------------------------------

    private static void SelfCheck()
    {
        Sample S(int r, long before, long after) => new(r, before, after, 0, 0, 0, 0);
        const long margin = 32 * MiB;
        var flat = Enumerable.Range(0, 100).Select(r => S(r, 300 * MiB, 300 * MiB)).ToArray();
        var late = Enumerable.Range(0, 100).Select(r => S(r, (300 + r * 4) * MiB, 300 * MiB)).ToArray();
        var leak = Enumerable.Range(0, 100).Select(r => S(r, (300 + r * 4) * MiB, (300 + r * 4) * MiB)).ToArray();
        Check(Classify(flat, 20, margin) == Trend.Bounded, "flat is bounded");
        Check(Classify(late, 20, margin) == Trend.LateRelease, "growth that a collection removes is late release");
        Check(Classify(leak, 20, margin) == Trend.Leak, "growth that survives a collection is a leak");
        // Rounds without a collection carry no after-collection value (-1) and must not count as zero.
        var sparse = late.Select(s => s.Round % 10 == 9 ? s : s with { PrivateAfter = -1 }).ToArray();
        Check(Classify(sparse, 20, margin) == Trend.LateRelease, "rounds without a collection are ignored for the after-collection trend");
    }

    /// <summary>
    /// Compares the median of the last quarter of rounds with the median of rounds warmup..warmup+20:
    /// Leak if memory after a collection rose by more than the margin, else LateRelease if memory before a collection did, else Bounded.
    /// Rows whose after-collection value is negative were not collected and are skipped for that comparison.
    /// </summary>
    internal static Trend Classify(IReadOnlyList<Sample> samples, int warmup, long marginBytes)
    {
        if (samples.Count == 0) return Trend.Bounded;
        var baseline = samples.Where(s => s.Round >= warmup && s.Round < warmup + 20).ToArray();
        int lastStart = samples.Count - Math.Max(1, samples.Count / 4);
        var last = samples.Skip(lastStart).ToArray();
        if (baseline.Length == 0) baseline = samples.Take(Math.Max(1, samples.Count / 4)).ToArray();
        long? Rise(Func<Sample, long> value)
        {
            var a = baseline.Select(value).Where(v => v >= 0).ToArray();
            var b = last.Select(value).Where(v => v >= 0).ToArray();
            return a.Length == 0 || b.Length == 0 ? null : Median(b) - Median(a);
        }
        if (Rise(s => s.PrivateAfter) is long after && after > marginBytes) return Trend.Leak;
        if (Rise(s => s.PrivateBefore) is long before && before > marginBytes) return Trend.LateRelease;
        return Trend.Bounded;
    }

    private static long Median(long[] values)
    {
        Array.Sort(values);
        int middle = values.Length / 2;
        return values.Length % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }

    // ---- sampling -------------------------------------------------------------------------------------------

    internal static int SettleTimeouts;
    /// <summary>Waits for the dispatcher to go idle; a window that animates continuously never lets it, so give up after 3 s and count it.</summary>
    private static async Task Settle()
    {
        var idle = Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
        if (await Task.WhenAny(idle, Task.Delay(3000)) != idle) SettleTimeouts++;
        await Task.Delay(60);
    }

    private static async Task<Sample> Measure(Process process, int round, bool collect)
    {
        await Settle(); process.Refresh();
        long before = process.PrivateMemorySize64, after = -1, managed = -1;
        if (collect)
        {
            // Harness-only measurement collection; product code never does this.
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await Settle(); process.Refresh();
            after = process.PrivateMemorySize64; managed = GC.GetTotalMemory(false);
        }
        return new(round, before, after, managed, GetGuiResources(process.Handle, 0), GetGuiResources(process.Handle, 1), process.HandleCount);
    }

    private static async Task RunScenarioAsync(string name, MonitorInfo monitor, int rounds, int collectEvery, int idleSeconds, int maxPrivateMiB, string output, Action<string> result)
    {
        int warmup = Math.Min(20, rounds / 4);
        var samples = new List<Sample>(rounds);
        using var process = Process.GetCurrentProcess();
        string? tempFolder = name == "capture-save" ? Path.Combine(Path.GetTempPath(), "DesktopTools-memory-soak-" + Guid.NewGuid().ToString("N")) : null;
        var target = new PatternWindow(monitor);
        var controller = new AppController(true);
        long idle; int? stoppedAt = null;
        try
        {
            controller.UpdateSettings(s =>
            {
                s.Animations = false; s.ScreenshotLibraryEnabled = false; s.AutoRedact.Enabled = false; s.CaptureDelaySeconds = 0;
                s.CaptureOutput = name == "capture-save" ? CaptureAutoSave.Mode : "Clipboard";
                if (tempFolder != null) s.SaveDirectory = tempFolder;
                s.CaptureEnabled = s.ScreenTextEnabled = s.TranslationEnabled = s.TeleprompterEnabled = s.QrCodesEnabled = true;
                s.ScreenRecorderEnabled = s.VideoEditorEnabled = s.ImageToolsEnabled = true;
            });
            target.Show(); await Settle();
            var scenario = Scenario(name, controller, target, monitor);
            for (int round = 0; round < rounds; round++)
            {
                await scenario(round);
                bool collect = round == warmup || round == rounds - 1 || (round + 1) % collectEvery == 0;
                samples.Add(await Measure(process, round, collect));
                if (samples[^1].PrivateBefore > maxPrivateMiB * MiB)
                {
                    Console.WriteLine($"memory-soak {name}: stopped at round {round}, private bytes above the {maxPrivateMiB} MiB safety cap");
                    stoppedAt = round; break;
                }
                if (round % 10 == 0 || round == rounds - 1)
                    Console.WriteLine($"memory-soak {name} round {round}: private {samples[^1].PrivateBefore / MiB} MiB, settle timeouts {SettleTimeouts}");
            }
            // Review Focus 3: memory released late should fall without user action while the app sits idle.
            await Task.Delay(TimeSpan.FromSeconds(idleSeconds)); await Settle(); process.Refresh();
            idle = process.PrivateMemorySize64;
        }
        finally
        {
            foreach (var notice in Application.Current.Windows.OfType<NotificationWindow>().ToArray()) notice.Close();
            CloseUtilities(controller);
            controller.Dispose(); target.Close();
            if (tempFolder != null && Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
        var trend = Classify(samples, warmup, 32 * MiB);
        int settleTimeouts = SettleTimeouts; SettleTimeouts = 0;
        string csv = Path.Combine(output, $"memory-soak-{name}.csv");
        var text = new StringBuilder("Round,PrivateBefore,PrivateAfter,ManagedAfter,Gdi,User,Handles\n");
        foreach (var s in samples)
            text.Append(CultureInfo.InvariantCulture, $"{s.Round},{s.PrivateBefore},{Opt(s.PrivateAfter)},{Opt(s.ManagedAfter)},{s.Gdi},{s.User},{s.Handles}\n");
        text.Append(CultureInfo.InvariantCulture, $"idle,{idle},,,,,\n");
        File.WriteAllText(csv, text.ToString());
        var first = samples[Math.Min(warmup, samples.Count - 1)]; var end = samples[^1];
        static string M(long bytes) => (bytes / (double)MiB).ToString("F1", CultureInfo.InvariantCulture);
        result($"MEASURE memory-soak {name}: monitor {monitor.Id} {monitor.Bounds.Width:F0}x{monitor.Bounds.Height:F0} px scale {monitor.ScaleX:F2}; rounds {rounds} (warmup {warmup}, collect every {collectEvery}); " +
               $"private before MiB {M(first.PrivateBefore)}->{M(end.PrivateBefore)}; private after MiB {M(first.PrivateAfter)}->{M(end.PrivateAfter)}; managed MiB {M(first.ManagedAfter)}->{M(end.ManagedAfter)}; " +
               $"idle {idleSeconds}s MiB {M(idle)}; GDI {samples[0].Gdi}->{end.Gdi}; USER {samples[0].User}->{end.User}; handles {samples[0].Handles}->{end.Handles}; settle timeouts {settleTimeouts}; " +
               (stoppedAt is int stop ? $"STOPPED at round {stop} by the {maxPrivateMiB} MiB cap; " : "") + $"trend {trend}; csv {csv}");
        Console.WriteLine("memory-soak done " + name + ": " + trend);
    }

    private static string Opt(long value) => value < 0 ? "" : value.ToString(CultureInfo.InvariantCulture);

    // ---- scenarios ------------------------------------------------------------------------------------------

    private static Func<int, Task> Scenario(string name, AppController controller, PatternWindow target, MonitorInfo monitor) => name switch
    {
        "capture" => async round => { await CaptureRound(controller, target, monitor, round, save: false); DismissNotices(); },
        "capture-save" => async round => { await CaptureRound(controller, target, monitor, round, save: true); DismissNotices(); },
        "editor" => round => EditorRound(controller, target, monitor, round),
        "ocr" => OcrRound(target, monitor),
        "background" => BackgroundRound(),
        "utilities" => round => UtilitiesRound(controller, target, monitor, round),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    /// <summary>
    /// The capture half of AppController.CaptureAsync for a fixed region: grab the monitor, crop to the region, then the
    /// controller's own post-capture steps with the clipboard write replaced, so the user's clipboard is never touched.
    /// HiddenWindowsScope is not used: it would hide the owned window and expose the user's desktop.
    /// </summary>
    private static async Task<BitmapSource> CaptureRound(AppController controller, PatternWindow target, MonitorInfo monitor, int round, bool save)
    {
        var image = await CaptureOwned(target, monitor, round);
        if (save)
        {
            // As HandleCapturedImageAsync: remember the capture, then copy and save in one step.
            typeof(AppController).GetProperty(nameof(AppController.LastCapture))!.SetValue(controller, image);
            Invoke(controller, "RememberCapture", image);
            string? path = await controller.CopyAndSaveAsync(image, _ => Task.FromResult(new ClipboardResult(true)));
            Check(path != null && File.Exists(path), "Auto-save did not write the screenshot: " + controller.Status);
            return image;
        }
        typeof(AppController).GetProperty(nameof(AppController.LastCapture))!.SetValue(controller, image);
        Invoke(controller, "RememberCapture", image);
        if (CopyMode == "test-writer")
            Check(await controller.TryCopyAsync(image, writeClipboard: _ => { }), "Copy through the test writer failed: " + controller.Status);
        else
        {
            // The product's own clipboard encoding (ClipboardService.SetImageAsync) without the final write to the user's clipboard.
            var formats = (System.Collections.ICollection)typeof(ClipboardService).GetMethod("ImageFormats", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [image])!;
            Check(formats.Count > 0, "Clipboard image formats were empty");
            controller.Report(DesktopTools.Localization.L.T("Screenshot copied to clipboard."));
        }
        Invoke(controller, "ShowCaptureNotice", null, null);
        return image;
    }

    private static async Task<BitmapSource> CaptureOwned(PatternWindow target, MonitorInfo monitor, int round)
    {
        target.Paint(round); await Settle(); DwmFlush();
        var desktop = CaptureService.Capture(monitor);
        var region = new Rect(0, 0, monitor.Bounds.Width / monitor.ScaleX, monitor.Bounds.Height / monitor.ScaleY);
        var crop = AppController.RegionPixelBounds(region, monitor, desktop.PixelWidth, desktop.PixelHeight);
        var image = CaptureComposition.Composite(desktop, [], monitor.ScaleX, monitor.ScaleY, new Vector(), crop);
        var pixel = new byte[4]; image.CopyPixels(new Int32Rect(4, 4, 1, 1), pixel, 4, 0);
        var color = target.Color;
        Check(Math.Abs(pixel[0] - color.B) <= 2 && Math.Abs(pixel[1] - color.G) <= 2 && Math.Abs(pixel[2] - color.R) <= 2,
            $"Capture at round {round} did not show the owned pattern window: expected {color}, got {pixel[2]},{pixel[1]},{pixel[0]}");
        return image;
    }

    private static async Task EditorRound(AppController controller, PatternWindow target, MonitorInfo monitor, int round)
    {
        await CaptureRound(controller, target, monitor, round, save: false); DismissNotices();
        controller.RedactLast(); await Settle();
        var editor = Application.Current.Windows.OfType<ScreenshotEditorWindow>().Single();
        try
        {
            var document = (ScreenshotEditDocument)typeof(ScreenshotEditorView).GetField("_document", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor.View)!;
            document.Add(new Annotation { Color = Colors.White, Thickness = 8, Points = new[] { new Point(20 + round % 40, 20), new Point(400, 300) } });
            await Settle();
            var exported = editor.View.RenderResult();
            Check(exported.PixelWidth > 0, "Editor export to memory returned an empty image");
        }
        finally { editor.Close(); }
    }

    private static Func<int, Task> OcrRound(PatternWindow target, MonitorInfo monitor)
    {
        string language = LocalOcr.SelectLanguage(LocalOcr.Languages, "en-US")?.Tag ?? throw new InvalidOperationException("No Windows OCR language is installed");
        return async round =>
        {
            var image = await CaptureOwned(target, monitor, round);
            var layout = await LocalOcr.RecognizeWordsAsync(image, language);
            Check(layout != null, "OCR returned no layout");
        };
    }

    private static Func<int, Task> BackgroundRound()
    {
        var pixels = new byte[1920 * 1080 * 4];
        for (int y = 0; y < 1080; y++)
            for (int x = 0; x < 1920; x++)
            {
                int i = (y * 1920 + x) * 4; bool subject = Math.Pow(x - 960, 2) / 160000 + Math.Pow(y - 540, 2) / 90000 < 1;
                pixels[i] = (byte)(subject ? 40 : 200); pixels[i + 1] = (byte)(subject ? 90 : 220); pixels[i + 2] = (byte)(subject ? 200 : (x * 255 / 1920)); pixels[i + 3] = 255;
            }
        var image = BitmapSource.Create(1920, 1080, 96, 96, PixelFormats.Bgra32, null, pixels, 1920 * 4); image.Freeze();
        return async _ =>
        {
            var removed = await BackgroundRemovalService.RemoveAsync(image);
            Check(removed.PixelWidth == 1920 && removed.PixelHeight == 1080, "Background removal returned the wrong size");
        };
    }

    /// <summary>
    /// Opens and closes each utility window OpenUtility can create from a smoke controller. Automation is not created by a smoke
    /// controller, and the color sample window needs an interactive eyedropper pick, so both are left out.
    /// </summary>
    private static async Task UtilitiesRound(AppController controller, PatternWindow target, MonitorInfo monitor, int round)
    {
        if (round == 0 || controller.LastCapture == null) { await CaptureRound(controller, target, monitor, round, save: false); DismissNotices(); }
        var image = controller.LastCapture!;
        var openers = new (string Key, Action Open)[]
        {
            ("Teleprompter", controller.OpenTeleprompter), ("QR", controller.OpenQrCodes), ("Recorder", controller.OpenScreenRecorder),
            ("Video", controller.OpenVideoEditor), ("Text", controller.OpenTextToolsWindow), ("Images", controller.OpenImageTools),
            ("StepGuide", controller.OpenStepGuide), ("ScreenshotLibrary", controller.OpenScreenshotLibrary),
            ("ScreenText", () => Invoke(controller, "OpenUtility", "ScreenText", (Func<Window>)(() => new ScreenTextWindow(image, controller.Report, null)))),
        };
        var windows = Utilities(controller);
        foreach (var (key, open) in openers)
        {
            int timeoutsBefore = SettleTimeouts;
            open(); await Settle();
            if (round == 0 && SettleTimeouts != timeoutsBefore) Console.WriteLine($"memory-soak utilities: dispatcher did not go idle within 3 s after opening {key}");
            Check(windows.TryGetValue(key, out var window) && window.IsVisible, $"Utility {key} did not open at round {round}: {controller.Status}");
            if (window is ScreenTextWindow)
            {
                // A user reads the result before closing; wait for recognition to finish (bounded).
                var view = (TextSelectionView)typeof(ScreenTextWindow).GetField("view", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var clock = Stopwatch.StartNew();
                while (view.IsScanning && clock.Elapsed < TimeSpan.FromSeconds(60)) await Task.Delay(100);
                Check(!view.IsScanning, $"Screen text recognition did not finish within 60 s at round {round}");
                int afterScan = SettleTimeouts; await Settle();
                if (round == 0) Console.WriteLine($"memory-soak utilities: ScreenText recognized in {clock.ElapsedMilliseconds} ms; dispatcher idle afterwards: {SettleTimeouts == afterScan}");
            }
            window!.Close(); await Settle();
            Check(!windows.ContainsKey(key), $"Utility {key} stayed registered after closing at round {round}");
        }
        DismissNotices();
    }

    private static Dictionary<string, Window> Utilities(AppController controller)
        => (Dictionary<string, Window>)typeof(AppController).GetField("utilityWindows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;

    private static void CloseUtilities(AppController controller)
    {
        foreach (var window in Utilities(controller).Values.ToArray()) window.Close();
        foreach (var editor in Application.Current.Windows.OfType<ScreenshotEditorWindow>().ToArray()) editor.Close();
    }

    /// <summary>
    /// A notice closed before its first frame (a quick second capture, or the harness dismissing it at once) must not stay alive.
    /// With animations off, WPF still raises ContentRendered after Closed; the dismissal timer must not restart then.
    /// </summary>
    private static async Task CheckNoticeRelease(Action<string> result)
    {
        bool previous = Motion.AppEnabled;
        try
        {
            foreach (bool animations in new[] { false, true })
            foreach (bool beforeRender in new[] { true, false })
            {
                Motion.AppEnabled = animations;
                var notices = new List<WeakReference>();
                for (int i = 0; i < 4; i++) notices.Add(await ShowAndCloseNotice(beforeRender));
                for (int i = 0; i < 3; i++) { await Settle(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
                int alive = notices.Count(n => n.IsAlive);
                Check(alive == 0, $"{alive} of {notices.Count} closed capture notices stayed alive (animations {(animations ? "on" : "off")}, closed {(beforeRender ? "before" : "after")} the first frame)");
            }
        }
        finally { Motion.AppEnabled = previous; }
        result("PASS closed capture notices and their screenshots are released");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> ShowAndCloseNotice(bool beforeRender)
    {
        var image = BitmapSource.Create(1920, 1080, 96, 96, PixelFormats.Bgra32, null, new byte[1920 * 1080 * 4], 1920 * 4); image.Freeze();
        var notice = new NotificationWindow("Screenshot ready", NotificationKind.Info, null, "Preview", image, [("Edit", () => { }), ("Save PNG", () => { }), ("Pin", () => { })], 6);
        notice.Show();
        if (!beforeRender) { await Settle(); await Task.Delay(300); await Settle(); }
        notice.Close();
        await Settle();
        return new WeakReference(notice);
    }

    private static void DismissNotices()
    {
        foreach (var notice in Application.Current.Windows.OfType<NotificationWindow>().ToArray()) notice.Close();
    }

    private static object? Invoke(object target, string method, params object?[] args)
    {
        var info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new MissingMethodException(target.GetType().Name, method);
        return info.Invoke(target, args);
    }

    // ---- arguments ------------------------------------------------------------------------------------------

    private static string? StringArg(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
    private static int IntArg(string[] args, string name, int fallback)
        => StringArg(args, name) is { } text ? int.Parse(text, CultureInfo.InvariantCulture) : fallback;

    // ---- owned target ---------------------------------------------------------------------------------------

    /// <summary>A borderless, topmost window that covers the whole monitor and shows a different pattern with text each round.</summary>
    private sealed class PatternWindow : Window
    {
        private static readonly Color[] Colors = [System.Windows.Media.Colors.CornflowerBlue, System.Windows.Media.Colors.Coral, System.Windows.Media.Colors.SeaGreen, System.Windows.Media.Colors.Goldenrod];
        private readonly MonitorInfo _monitor;
        private readonly TextBlock _text = new() { FontSize = 36, Foreground = Brushes.Black, Margin = new Thickness(80), TextWrapping = TextWrapping.Wrap };
        private readonly Canvas _shapes = new();
        public Color Color { get; private set; }
        public PatternWindow(MonitorInfo monitor)
        {
            _monitor = monitor;
            Title = "DesktopTools memory soak target"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            ShowActivated = false; ShowInTaskbar = false; Topmost = true; WindowStartupLocation = WindowStartupLocation.Manual;
            var grid = new Grid(); grid.Children.Add(_shapes); grid.Children.Add(_text); Content = grid;
            SourceInitialized += (_, _) => Place();
            Loaded += (_, _) => Place();
            Paint(0);
        }
        private void Place()
        {
            var handle = new WindowInteropHelper(this).Handle;
            var b = _monitor.Bounds;
            SetWindowPos(handle, new IntPtr(-1), (int)b.X, (int)b.Y, (int)b.Width, (int)b.Height, 0x10);
        }
        public void Paint(int round)
        {
            Color = Colors[round % Colors.Length];
            Background = new SolidColorBrush(Color);
            _text.Text = $"Memory soak round {round}\nThe quick brown fox jumps over the lazy dog {round * 7919 % 10007}\nOwned test pattern for DesktopTools";
            _shapes.Children.Clear();
            for (int i = 0; i < 6; i++)
            {
                var box = new System.Windows.Shapes.Rectangle { Width = 120 + (round * 13 + i * 37) % 200, Height = 80 + (round * 7 + i * 29) % 120, Fill = new SolidColorBrush(Color.FromRgb((byte)(round * 31 + i * 40), (byte)(i * 50), (byte)(255 - round % 256))) };
                Canvas.SetLeft(box, 200 + (round * 17 + i * 160) % 1200); Canvas.SetTop(box, 400 + (round * 11 + i * 90) % 400);
                _shapes.Children.Add(box);
            }
            UpdateLayout();
        }
    }

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern uint GetGuiResources(nint process, uint flags);
}
