using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Extras;

/// <summary>
/// Renders text the way real screenshots show it (dark theme, tiny UI text, coloured bars, mixed backgrounds) and compares what the
/// original single-pass OCR and the enhanced reader return, as character accuracy against the known text.
/// Set OCR_IMAGE to also dump both readings of an arbitrary local image for eyeballing (the file is only read, never copied).
/// </summary>
internal static class OcrQualityChecks
{
    private sealed record Case(string Name, string[] Lines, double Size, Color Foreground, Color Background, string Font = "Segoe UI", bool Bold = false, string? Language = null, bool Gradient = false);

    private static readonly string[] Ui = ["Settings Capture tools Screen recorder", "Region capture Ctrl + Alt + S", "Recent screenshots appear here until you quit.", "Total: 1,234.56 USD on 2026-10-05"];
    private static readonly string[] Code = ["var path = C:\\Users\\vlad\\Documents\\report.txt;", "mail = user@example.com  ip = 192.168.10.24", "if (count >= 10) return Math.Max(width, height);"];

    private static readonly string[] Chat = ["Edited 44 files    +8982 -0", "uk02.txt    +240 -0    zh02.txt    +240 -0", "vg2222/editor-style    library and search", "Ran 4 commands, created stage.py", "Status: splitting the work into a chain of branches"];
    private static readonly string[] Russian = ["Разнести изменения по веткам", "Каждую ветку собрал в чистой копии", "Ветки идут цепочкой, поверх предыдущей"];

    private static readonly Case[] Cases =
    [
        new("tiny 9px dark", Chat, 9, Color.FromRgb(200, 204, 212), Color.FromRgb(16, 16, 18)),
        new("tiny 10px dark gradient", Chat, 10, Color.FromRgb(190, 194, 204), Color.FromRgb(22, 22, 30), Gradient: true),
        new("tiny 9px light", Chat, 9, Color.FromRgb(50, 52, 60), Color.FromRgb(246, 247, 250)),
        new("tiny 9px dim dark", Chat, 9, Color.FromRgb(120, 124, 134), Color.FromRgb(16, 16, 18)),
        new("russian 10px dark", Russian, 10, Color.FromRgb(200, 204, 212), Color.FromRgb(16, 16, 18), Language: "ru"),
        new("russian 14px light", Russian, 14, Color.FromRgb(30, 30, 30), Colors.White, Language: "ru"),
        new("light 16px", Ui, 16, Colors.Black, Colors.White),
        new("light 11px", Ui, 11, Color.FromRgb(40, 40, 40), Colors.White),
        new("dark theme 16px", Ui, 16, Color.FromRgb(225, 228, 235), Color.FromRgb(14, 14, 16)),
        new("dark theme 11px", Ui, 11, Color.FromRgb(200, 204, 212), Color.FromRgb(18, 18, 20)),
        new("dim grey on dark 12px", Ui, 12, Color.FromRgb(140, 144, 152), Color.FromRgb(24, 24, 28)),
        new("white on blue 14px", Ui, 14, Colors.White, Color.FromRgb(37, 99, 235)),
        new("yellow on purple 13px", Ui, 13, Color.FromRgb(255, 224, 74), Color.FromRgb(88, 40, 160)),
        new("code on dark 12px", Code, 12, Color.FromRgb(210, 214, 220), Color.FromRgb(30, 30, 30), "Consolas"),
        new("code on light 12px", Code, 12, Color.FromRgb(30, 30, 30), Color.FromRgb(250, 250, 250), "Consolas"),
        new("large bold 30px dark", ["Screenshot library", "Search text in screenshots"], 30, Colors.White, Color.FromRgb(20, 20, 24), "Segoe UI", true),
    ];

    private static BitmapSource Render(Case c)
    {
        double lineHeight = c.Size * 1.55;
        int width = 980, height = (int)(lineHeight * c.Lines.Length + 48);
        var visual = new DrawingVisual();
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Display);
        using (var dc = visual.RenderOpen())
        {
            Brush back = c.Gradient ? new LinearGradientBrush(c.Background, Color.FromRgb((byte)(c.Background.R + 28), (byte)(c.Background.G + 20), (byte)(c.Background.B + 44)), 20) : new SolidColorBrush(c.Background);
            dc.DrawRectangle(back, null, new Rect(0, 0, width, height));
            for (int i = 0; i < c.Lines.Length; i++)
            {
                var text = new FormattedText(c.Lines[i], CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(new FontFamily(c.Font), FontStyles.Normal, c.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), c.Size, new SolidColorBrush(c.Foreground), 1.0);
                dc.DrawText(text, new Point(24, 24 + i * lineHeight));
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    private static string Normalize(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static int Distance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1]; current[0] = i;
            for (int j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[b.Length];
    }

    private static double Accuracy(string expected, string actual)
    {
        string e = Normalize(expected), a = Normalize(actual);
        return Math.Max(0, 1 - Distance(e, a) / (double)Math.Max(1, e.Length));
    }

    public static async Task RunAsync()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("OCR_INKR"), out int inkRadius)) OcrEnhancer.InkRadius = inkRadius;
        if (double.TryParse(Environment.GetEnvironmentVariable("OCR_GAIN"), NumberStyles.Float, CultureInfo.InvariantCulture, out double inkGain)) OcrEnhancer.InkGain = inkGain;
        if (double.TryParse(Environment.GetEnvironmentVariable("OCR_TARGET"), NumberStyles.Float, CultureInfo.InvariantCulture, out double target)) OcrEnhancer.TargetTextHeight = target;
        var languages = LocalOcr.Languages;
        var language = LocalOcr.SelectLanguage(languages, "en-US") ?? throw new Exception("No Windows OCR language installed.");
        var russian = languages.FirstOrDefault(l => l.Tag.StartsWith("ru", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine("OCR language: " + language.Name);
        double plainTotal = 0, enhancedTotal = 0;
        Console.WriteLine($"{"case",-26}{"single pass",12}{"enhanced",12}");
        foreach (var testCase in Cases)
        {
            var image = Render(testCase);
            string expected = string.Join("\n", testCase.Lines);
            var useLanguage = testCase.Language == "ru" ? russian : language;
            if (useLanguage == null) continue;
            string plainText = await LocalOcr.RecognizeSinglePassAsync(image, useLanguage.Tag);
            double plain = Accuracy(expected, plainText);
            string enhancedText = await LocalOcr.RecognizeAsync(image, useLanguage.Tag);
            double enhanced = Accuracy(expected, enhancedText);
            plainTotal += plain; enhancedTotal += enhanced;
            Console.WriteLine($"{testCase.Name,-26}{plain,11:P0}{enhanced,12:P0}");
            if (enhanced + .001 < plain - .05) throw new Exception($"Enhanced OCR is clearly worse on '{testCase.Name}': {plain:P0} -> {enhanced:P0}\n{enhancedText}");
        }
        Console.WriteLine($"{"average",-26}{plainTotal / Cases.Length,11:P0}{enhancedTotal / Cases.Length,12:P0}");
        if (Environment.GetEnvironmentVariable("OCR_DUMP") == "1") foreach (var c in Cases.Take(6)) { var img = Render(c); var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(img)); using var f = File.Create(Path.Combine(AppContext.BaseDirectory, "ocr-case-" + c.Name.Replace(' ', '-') + ".png")); enc.Save(f); }
        if (enhancedTotal < plainTotal) throw new Exception("Enhanced OCR did not improve the average accuracy.");

        string? path = Environment.GetEnvironmentVariable("OCR_IMAGE");
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            var decoder = BitmapDecoder.Create(new Uri(Path.GetFullPath(path)), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            string output = Environment.GetEnvironmentVariable("OCR_OUT") ?? Path.Combine(AppContext.BaseDirectory, "ocr-compare");
            Directory.CreateDirectory(output);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            string single = await LocalOcr.RecognizeSinglePassAsync(frame, language.Tag); long t1 = watch.ElapsedMilliseconds; watch.Restart();
            string enhanced = await LocalOcr.RecognizeAsync(frame, language.Tag); long t2 = watch.ElapsedMilliseconds;
            File.WriteAllText(Path.Combine(output, "single-pass.txt"), single); File.WriteAllText(Path.Combine(output, "enhanced.txt"), enhanced);
            Console.WriteLine($"{frame.PixelWidth}x{frame.PixelHeight}: single pass {single.Length} chars in {t1} ms, enhanced {enhanced.Length} chars in {t2} ms -> {output}");
        }
    }
}
