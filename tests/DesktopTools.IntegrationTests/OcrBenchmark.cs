using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Media;
using Color = System.Drawing.Color;
using Point = System.Drawing.Point;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using System.Windows.Media.Imaging;
using DesktopTools.Extras;

/// <summary>
/// Wide OCR benchmark on text drawn the way real programs draw it (GDI ClearType, coloured sub-pixel edges): many fonts, sizes, weights,
/// colour schemes and everyday user-interface phrases. Reports how often a phrase comes back exactly right, per size, scheme and font,
/// and lists the worst misreadings. Knobs: OCR_BENCH_IMAGES (default 120), OCR_BENCH_SEED (default 7), OCR_BENCH_MODES (comma list).
/// </summary>
internal static class OcrBenchmark
{
    private sealed record Scheme(string Name, Color Foreground, Color Background);

    private static readonly Scheme[] Schemes =
    [
        new("black on white", Color.Black, Color.White),
        new("grey on light", Color.FromArgb(60, 64, 72), Color.FromArgb(243, 244, 246)),
        new("white on dark", Color.FromArgb(235, 237, 240), Color.FromArgb(24, 24, 27)),
        new("light on navy", Color.FromArgb(203, 213, 225), Color.FromArgb(30, 41, 59)),
        new("dim on dark", Color.FromArgb(128, 134, 145), Color.FromArgb(24, 24, 27)),
        new("white on blue", Color.White, Color.FromArgb(37, 99, 235)),
        new("white on green", Color.White, Color.FromArgb(22, 163, 74)),
        new("black on yellow", Color.Black, Color.FromArgb(250, 204, 21)),
        new("white on red", Color.White, Color.FromArgb(220, 38, 38)),
        new("blue on white", Color.FromArgb(29, 78, 216), Color.White),
    ];

    private static readonly string[] English =
    [
        "Claim offer", "Sign in", "Add to cart", "Download now", "Settings", "Subscribe", "Learn more", "Get started", "Buy now", "Continue",
        "Cancel", "Free trial", "Accept all cookies", "Privacy policy", "Terms of service", "Next step", "Skip for now", "Remove item", "Share link",
        "Open in browser", "Notifications", "Account settings", "Recent files", "Search results", "Create new project", "Save changes", "Delete account",
        "Your order has shipped", "Limited time deal", "Join the waitlist", "Enter your password", "Forgot password?", "Remember me", "Back to home",
        "Premium members only", "Reward unlocked", "Daily bonus", "Invite friends", "Total: $1,249.99", "Order #48213 confirmed", "support@example.com",
        "https://example.com/offers", "Version 2.4.1 (build 5821)", "Updated 3 minutes ago", "12 new messages", "Mon, Oct 5, 2026", "Line 128, Column 14",
        "Install update", "Restart now", "Network connected", "Battery 87%", "Volume 45", "Microsoft Edge", "Visual Studio Code", "File  Edit  View  Help",
        "Welcome back, Alex", "Choose a plan", "Start your journey", "Claim your reward", "Quick actions", "Open menu",
    ];

    private static readonly string[] Russian =
    [
        "Получить предложение", "Войти", "Настройки", "Скачать сейчас", "Забыли пароль?", "Новые сообщения", "Сохранить изменения", "Назад на главную",
        "Ваш заказ отправлен", "Подписаться", "Уведомления", "Подробнее", "Начать", "Выберите тариф",
    ];

    private static readonly (string Name, FontStyle Style)[] Fonts =
    [
        ("Segoe UI", FontStyle.Regular), ("Segoe UI", FontStyle.Bold), ("Arial", FontStyle.Regular), ("Arial", FontStyle.Bold), ("Calibri", FontStyle.Regular),
        ("Tahoma", FontStyle.Regular), ("Verdana", FontStyle.Regular), ("Consolas", FontStyle.Regular), ("Times New Roman", FontStyle.Regular),
        ("Georgia", FontStyle.Regular), ("Trebuchet MS", FontStyle.Regular), ("Cambria", FontStyle.Regular), ("Segoe UI Semibold", FontStyle.Regular),
        ("Courier New", FontStyle.Regular), ("Bahnschrift", FontStyle.Regular),
    ];

    private static readonly int[] Sizes = [9, 10, 11, 12, 13, 14, 16, 18, 22, 28];

    private sealed record Sample(BitmapSource Image, string[] Lines, string Font, int Size, string Scheme, bool Russian);

    private static BitmapSource Render(string[] lines, string font, FontStyle style, int size, Scheme scheme)
    {
        int lineHeight = (int)(size * 2.1), width = 760, height = lineHeight * lines.Length + 32;
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(scheme.Background);
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using var f = new Font(font, size, style, GraphicsUnit.Pixel);
            for (int i = 0; i < lines.Length; i++)
                TextRenderer.DrawText(g, lines[i], f, new Point(18, 16 + i * lineHeight), scheme.Foreground, scheme.Background, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, data.Scan0, data.Stride * height, data.Stride);
            result.Freeze(); return result;
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static IReadOnlyList<Sample> Build(int images, int seed)
    {
        var random = new Random(seed);
        var installed = new HashSet<string>(new InstalledFontCollection().Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        var fonts = Fonts.Where(f => installed.Contains(f.Name)).ToArray();
        var samples = new List<Sample>();
        for (int i = 0; i < images; i++)
        {
            var (font, style) = fonts[random.Next(fonts.Length)];
            int size = Sizes[random.Next(Sizes.Length)];
            var scheme = Schemes[random.Next(Schemes.Length)];
            bool russian = i % 8 == 7;
            var pool = russian ? Russian : English;
            var lines = Enumerable.Range(0, 7).Select(_ => pool[random.Next(pool.Length)]).Distinct().ToArray();
            samples.Add(new Sample(Render(lines, font, style, size, scheme), lines, font + (style == FontStyle.Bold ? " Bold" : ""), size, scheme.Name, russian));
        }
        return samples;
    }

    private static string Normalize(string text) => string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static int Distance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1]; current[0] = i;
            for (int j = 1; j <= b.Length; j++) current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[b.Length];
    }

    /// <summary>How much of this phrase came back: 1 for an exact copy; otherwise the best match against any output line or neighbouring pair of lines.</summary>
    private static double LineScore(string phrase, string[] outputLines)
    {
        string expected = Normalize(phrase);
        double best = 0;
        for (int i = 0; i < outputLines.Length; i++)
            foreach (string candidate in new[] { outputLines[i], i + 1 < outputLines.Length ? outputLines[i] + " " + outputLines[i + 1] : outputLines[i] })
            {
                string c = Normalize(candidate);
                if (c.Contains(expected, StringComparison.Ordinal)) return 1;
                best = Math.Max(best, 1 - Distance(expected, c.Length > expected.Length * 2 ? c[..(expected.Length * 2)] : c) / (double)Math.Max(1, expected.Length));
            }
        return Math.Max(0, best);
    }

    private sealed class Tally
    {
        public int Phrases, Exact; public double Score;
        public void Add(double score) { Phrases++; if (score >= 1) Exact++; Score += score; }
        public override string ToString() => $"{Exact * 100.0 / Math.Max(1, Phrases),5:0.0}% exact  {Score * 100.0 / Math.Max(1, Phrases),5:0.0}% chars  ({Phrases})";
    }

    private static Func<BitmapSource, string, Task<string>> Mode(string name)
    {
        if (name == "single") return (image, tag) => LocalOcr.RecognizeSinglePassAsync(image, tag);
        if (name == "current") return (image, tag) => LocalOcr.RecognizeAsync(image, tag);
        if (name.StartsWith("v:"))   // v:<plain|ink|gray>:<scale>:<hq|linear|nearest|fant|legacy>
        {
            var p = name.Split(':');
            return (image, tag) => LocalOcr.ReadVariantAsync(image, tag, p[1], int.Parse(p[2]), p.Length > 3 ? p[3] : "hq");
        }
        throw new Exception("Unknown OCR benchmark mode " + name);
    }

    public static async Task RunAsync()
    {
        int images = int.TryParse(Environment.GetEnvironmentVariable("OCR_BENCH_IMAGES"), out int n) ? n : 120;
        int seed = int.TryParse(Environment.GetEnvironmentVariable("OCR_BENCH_SEED"), out int s) ? s : 7;
        string[] modes = (Environment.GetEnvironmentVariable("OCR_BENCH_MODES") ?? "single,current").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var languages = LocalOcr.Languages;
        var english = LocalOcr.SelectLanguage(languages, "en-US") ?? throw new Exception("No English OCR language installed");
        var russian = languages.FirstOrDefault(l => l.Tag.StartsWith("ru", StringComparison.OrdinalIgnoreCase));
        var samples = Build(images, seed);
        Console.WriteLine($"Benchmark: {samples.Count} images, {samples.Sum(x => x.Lines.Length)} phrases, seed {seed}, language {english.Name}");
        string folder = Path.Combine(Environment.CurrentDirectory, "ocr-bench"); Directory.CreateDirectory(folder);
        var report = new System.Text.StringBuilder();
        var bestScores = new Dictionary<(int, string), double>();   // oracle: the best reading any mode produced for every phrase
        var oracleBySize = new SortedDictionary<int, Tally>(); var oracleTotal = new Tally();
        foreach (string modeName in modes)
        {
            var read = Mode(modeName);
            var total = new Tally(); var bySize = new SortedDictionary<int, Tally>(); var byScheme = new SortedDictionary<string, Tally>(); var byFont = new SortedDictionary<string, Tally>();
            var failures = new List<(double Score, string Truth, string Got, Sample Sample)>();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int index = 0; index < samples.Count; index++)
            {
                var sample = samples[index];
                var language = sample.Russian ? russian : english; if (language == null) continue;
                string text = await read(sample.Image, language.Tag);
                var outputLines = text.Split('\n').Select(l => l.Trim('\r')).Where(l => l.Length > 0).ToArray();
                foreach (string phrase in sample.Lines)
                {
                    double score = LineScore(phrase, outputLines);
                    total.Add(score);
                    if (!bestScores.TryGetValue((index, phrase), out double seen) || score > seen) bestScores[(index, phrase)] = score;
                    if (!bySize.TryGetValue(sample.Size, out var a)) bySize[sample.Size] = a = new(); a.Add(score);
                    if (!byScheme.TryGetValue(sample.Scheme, out var b)) byScheme[sample.Scheme] = b = new(); b.Add(score);
                    if (!byFont.TryGetValue(sample.Font, out var c)) byFont[sample.Font] = c = new(); c.Add(score);
                    if (score < 1)
                    {
                        string got = outputLines.OrderByDescending(l => 1 - Distance(Normalize(phrase), Normalize(l)) / (double)Math.Max(1, phrase.Length)).FirstOrDefault() ?? "";
                        failures.Add((score, phrase, got, sample));
                    }
                }
                if (index == 0 && modeName == modes[0]) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(sample.Image)); using var f = File.Create(Path.Combine(folder, "first-sample.png")); enc.Save(f); }
            }
            report.AppendLine($"=== {modeName}: {total}   [{clock.Elapsed.TotalSeconds:0} s]");
            report.AppendLine("  by size:   " + string.Join("  ", bySize.Select(p => $"{p.Key}px {p.Value.Exact * 100.0 / p.Value.Phrases:0}%")));
            report.AppendLine("  by scheme: " + string.Join("  ", byScheme.Select(p => $"{p.Key} {p.Value.Exact * 100.0 / p.Value.Phrases:0}%")));
            report.AppendLine("  by font:   " + string.Join("  ", byFont.Select(p => $"{p.Key} {p.Value.Exact * 100.0 / p.Value.Phrases:0}%")));
            report.AppendLine("  worst misreadings:");
            foreach (var f in failures.OrderBy(x => x.Score).Take(14)) report.AppendLine($"    [{f.Sample.Font} {f.Sample.Size}px, {f.Sample.Scheme}] '{f.Truth}'  ->  '{f.Got}'");
            File.WriteAllLines(Path.Combine(folder, $"failures-{modeName.Replace(':', '_')}.txt"), failures.OrderBy(x => x.Score).Select(f => $"{f.Score:0.00}\t{f.Sample.Font}\t{f.Sample.Size}\t{f.Sample.Scheme}\t{f.Truth}\t{f.Got}"));
        }
        if (modes.Length > 1)
        {
            for (int index = 0; index < samples.Count; index++)
                foreach (string phrase in samples[index].Lines)
                    if (bestScores.TryGetValue((index, phrase), out double best)) { oracleTotal.Add(best); if (!oracleBySize.TryGetValue(samples[index].Size, out var t)) oracleBySize[samples[index].Size] = t = new(); t.Add(best); }
            report.AppendLine($"=== oracle (best of {modes.Length} readings per phrase): {oracleTotal}");
            report.AppendLine("  by size:   " + string.Join("  ", oracleBySize.Select(p => $"{p.Key}px {p.Value.Exact * 100.0 / p.Value.Phrases:0}%")));
        }
        Console.WriteLine(report.ToString());
        File.WriteAllText(Path.Combine(folder, "report.txt"), report.ToString());
    }
}
