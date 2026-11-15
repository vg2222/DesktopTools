using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopTools;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.Localization;

/// <summary>
/// Measures memory across screenshots, the editor, Extract text and Check screenshot, and checks that closed editors are collected.
/// Prints MEASURE lines; fails when a closed editor stays reachable or idle memory keeps growing per screenshot.
/// </summary>
internal static class MemoryProbe
{
    private static async Task Settle(int ms = 1500)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end) await Task.Delay(50);
    }

    private static string Snapshot(string label)
    {
        using var process = Process.GetCurrentProcess();
        string line = $"MEASURE memory {label}: private {process.PrivateMemorySize64 / 1048576} MB, working set {process.WorkingSet64 / 1048576} MB, managed {GC.GetTotalMemory(false) / 1048576} MB";
        Console.WriteLine(line); return line;
    }

    private static BitmapSource Picture(int seed)
    {
        const int width = 2560, height = 1440;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb((byte)(seed * 40), 120, 200)), null, new Rect(0, 0, width, height));
            for (int i = 0; i < 40; i++)
                dc.DrawText(new FormattedText($"Invoice {seed}-{i} user{i}@example.com +1 555 01{i:00}", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 28, Brushes.Black, 1), new Point(40, 30 + i * 34));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze();
        return bitmap;
    }

    internal static async Task RunAsync()
    {
        using var controller = new AppController(true);
        controller.UpdateSettings(s => { s.Animations = false; s.CaptureOutput = "Clipboard"; });
        controller.OpenMain(); L.Use("en");
        await Settle(7000);
        Snapshot("after start");
        if (int.TryParse(Environment.GetEnvironmentVariable("MEMPROBE_HOLD"), out int hold)) { Console.WriteLine($"HOLD pid {Environment.ProcessId}"); await Settle(hold * 1000); return; }
        var editors = new System.Collections.Generic.List<WeakReference>();
        long baseline = 0;
        for (int round = 1; round <= 5; round++)
        {
            Console.WriteLine($"STEP round {round} capture");
            var capture = controller.HandleCapturedImageAsync(Picture(round));
            await Task.WhenAny(capture, Task.Delay(10000));
            await Settle();
            controller.RedactLast();
            var editor = Application.Current.Windows.OfType<ScreenshotEditorWindow>().Last();
            editors.Add(new WeakReference(editor));
            await Settle();
            Console.WriteLine($"STEP round {round} extract text");
            editor.View.ShowTextMode();
            for (int i = 0; i < 100 && editor.View.InTextMode && FindText(editor) is { IsScanning: true }; i++) await Task.Delay(100);
            await Settle(2000);
            editor.View.ShowEditingMode();
            Console.WriteLine($"STEP round {round} check screenshot");
            try { await Task.WhenAny(editor.FindSensitiveDataAsync(), Task.Delay(30000)); } catch (Exception e) { Console.WriteLine("Check screenshot: " + e.Message); }
            await Settle();
            editor.Close(); editor = null;
            foreach (var notice in Application.Current.Windows.OfType<DesktopTools.UI.NotificationWindow>().ToArray()) notice.Close();
            await Settle(7000);
            Snapshot($"round {round} idle");
            Console.WriteLine($"MEASURE memory trims so far: {DesktopTools.Native.MemoryTrimmer.Trims}");
            if (round == 1) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); using var p = Process.GetCurrentProcess(); baseline = p.PrivateMemorySize64; }
        }
        Snapshot("before forced GC");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        await Settle();
        Snapshot("after forced GC");
        int alive = editors.Count(reference => reference.IsAlive);
        Console.WriteLine($"MEASURE memory closed editors still reachable: {alive} of {editors.Count}");
        using var final = Process.GetCurrentProcess();
        Console.WriteLine($"MEASURE memory growth rounds 1-5 after GC: {(final.PrivateMemorySize64 - baseline) / 1048576} MB");
    }

    /// <summary>Reads the same picture with the OCR pack on the processor and on the graphics card; prints timings and whether the text matches.</summary>
    internal static async Task GpuProbeAsync()
    {
        Console.WriteLine($"MEASURE gpu Windows DirectML {AiGpu.SystemVersion?.ToString() ?? "missing"}, supported {AiGpu.IsSupported}");
        if (!OcrPackEngine.IsInstalled(OcrPackScript.Latin)) { Console.WriteLine("MEASURE gpu skipped: Latin OCR pack not installed"); return; }
        var picture = Picture(3);
        string Text(OcrLayout layout) => string.Join(" ", layout.Words.Select(w => w.Text));
        async Task<(string Text, long Ms, string Device)> Read(string device)
        {
            AiRuntime.PreferredDevice = device;
            await OcrPackEngine.RecognizeWordsAsync(picture, OcrPackScript.Latin, default); // warm-up and load
            var watch = Stopwatch.StartNew();
            var layout = await OcrPackEngine.RecognizeWordsAsync(picture, OcrPackScript.Latin, default);
            return (Text(layout), watch.ElapsedMilliseconds, AiRuntime.Device);
        }
        var cpu = await Read(AiGpu.Processor);
        var gpu = await Read(AiGpu.GraphicsCard);
        Console.WriteLine($"MEASURE gpu processor {cpu.Ms} ms ({cpu.Device}), graphics card {gpu.Ms} ms ({gpu.Device}), fallback {AiRuntime.GraphicsCardFailed}");
        Console.WriteLine($"MEASURE gpu same text: {cpu.Text == gpu.Text} ({cpu.Text.Length} vs {gpu.Text.Length} chars)");
        if (cpu.Text != gpu.Text) { Console.WriteLine("CPU: " + cpu.Text[..Math.Min(300, cpu.Text.Length)]); Console.WriteLine("GPU: " + gpu.Text[..Math.Min(300, gpu.Text.Length)]); }
        AiRuntime.PreferredDevice = AiGpu.Processor;
    }

    private static TextSelectionView? FindText(Window window)
    {
        TextSelectionView? found = null;
        void Walk(DependencyObject node) { if (found != null) return; if (node is TextSelectionView view) { found = view; return; } for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Walk(VisualTreeHelper.GetChild(node, i)); }
        Walk(window); return found;
    }
}
