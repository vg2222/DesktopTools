using DesktopTools.Core;
using System.IO;
using DesktopTools.Extras;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class AutoRedactOcrChecks
{
    internal static void Run()
    {
        var method = typeof(LocalOcr).GetMethod("RecognizeLayoutAsync") ?? throw new Exception("Missing positioned OCR for sensitive regions");
        var language = LocalOcr.Languages.FirstOrDefault(l => l.Tag.StartsWith("en"));
        if (language == null) { Console.WriteLine("SKIP Auto Redact real OCR: English recognizer unavailable"); return; }
        if (Environment.GetCommandLineArgs().Contains("--auto-redact-sidebar")) { DesktopSidebarWrappedSample(language.Tag); return; }
        if (Environment.GetCommandLineArgs().Contains("--auto-redact-desktop")) { NarrativeJournalSample(language.Tag, desktop: true); return; }
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 2200, 150));
            dc.DrawText(new FormattedText("alice@example.test", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Arial"), 32, Brushes.Black, 1), new Point(1460, 40));
        }
        var image = new RenderTargetBitmap(2200, 150, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze();
        var layout = Task.Run(async () => await (Task<OcrLayout>)method.Invoke(null, [image, language.Tag, CancellationToken.None])!).GetAwaiter().GetResult();
        var findings = SensitiveDataDetector.Detect(layout, ["email"]);
        if (findings.Count != 1 || findings[0].Bounds.X < 1400 || findings[0].Bounds.X + findings[0].Bounds.Width < 1700) throw new Exception("Tiled OCR lost email coordinates or duplicated findings: " + string.Join("; ", layout.Words.Select(w => w.Text + "=" + w.Bounds)) + " findings=" + string.Join("; ", findings.Select(f => f.Bounds)));
        Console.WriteLine("PASS positioned/tiled local OCR detects generated email");
        var listVisual = new DrawingVisual();
        using (var dc = listVisual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(32, 33, 36)), null, new Rect(0, 0, 396, 500));
            for (int i = 0; i < 7; i++)
                dc.DrawText(new FormattedText($"sample{i + 1}4322@gmail.com", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), i < 2 ? 14 : 10, Brushes.WhiteSmoke, 1), new Point(70, 75 + i * 58));
        }
        var listImage = new RenderTargetBitmap(396, 500, 96, 96, PixelFormats.Pbgra32); listImage.Render(listVisual); listImage.Freeze();
        var listLayout = Task.Run(() => LocalOcr.RecognizeLayoutAsync(listImage, language.Tag)).GetAwaiter().GetResult();
        var emails = SensitiveDataDetector.Detect(listLayout, ["email"]);
        if (emails.Count != 7) throw new Exception("Seven-row email list missed lower small-text rows: count=" + emails.Count + "; generated OCR=" + string.Join("; ", listLayout.Words.Select(w => w.Text + "=" + w.Bounds)));
        Console.WriteLine("PASS seven email rows, including small lower text, in physical image coordinates");
        var primary = new OcrLayout(396, 500, [new("first@gmail.com", new Rect(70, 80, 120, 12), 0), new("second@gmailcom", new Rect(70, 138, 120, 12), 1)]);
        var english = primary with { Words = [primary.Words[0], primary.Words[1] with { Text = "second@gmail.com" }] };
        var combined = Task.Run(() => SensitiveDataAnalyzer.AnalyzeAsync(listImage, "ru", ["email"], CancellationToken.None,
            (_, tag, _) => Task.FromResult(tag.StartsWith("en") ? english : primary))).GetAwaiter().GetResult();
        if (combined.Count != 2) throw new Exception("English data recognition did not recover a missed email or duplicated a primary result: count=" + combined.Count);
        Console.WriteLine("PASS additional English data OCR recovers email without duplicate findings");
        var codeVisual = new DrawingVisual();
        using (var dc = codeVisual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 800, 360));
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 100, 800, 150));
            string[] lines = ["IP: 192.0.2.145", "API-key: sk-test-8f4c2a91d7e6b305c9f2a48e1d6b7c03", "Password: fictional!pass123"];
            for (int i = 0; i < lines.Length; i++)
                dc.DrawText(new FormattedText(lines[i], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Consolas"), 22, Brushes.WhiteSmoke, 1), new Point(20, 110 + i * 45));
        }
        var codeImage = new RenderTargetBitmap(800, 360, 96, 96, PixelFormats.Pbgra32); codeImage.Render(codeVisual); codeImage.Freeze();
        var privateCode = Task.Run(() => SensitiveDataAnalyzer.AnalyzeAsync(codeImage, language.Tag, ["ip", "credential"], CancellationToken.None)).GetAwaiter().GetResult();
        if (privateCode.Count != 3 || privateCode.Count(f => f.Categories.Contains("ip")) != 1 || privateCode.Count(f => f.Categories.Contains("credential")) != 2)
            throw new Exception("Monospace reversed-text IP/key/password missed: regions=" + privateCode.Count);
        if (privateCode.Any(f => f.Bounds.Y < 100 || f.Bounds.Y + f.Bounds.Height > 250)) throw new Exception("Sensitive code boxes lost physical source coordinates");
        Console.WriteLine("PASS local OCR detects monospace IP, API key and labelled password on dark code strip");
        FullSample(language.Tag);
        TableSample(language.Tag);
        HandleListSample(language.Tag);
        NarrativeJournalSample(language.Tag);
        NarrativeJournalSample(language.Tag, desktop: true);
        WrappedInlineSample(language.Tag);
        DesktopSidebarWrappedSample(language.Tag);
    }

    private static void DesktopSidebarWrappedSample(string english)
    {
        var visual = new DrawingVisual(); var expected = new List<Rect>();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black,null,new Rect(0,0,2560,1440));
            void Write(string text,double x,double y,double size=10,bool sensitive=false)
            {
                var value=new FormattedText(text,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Consolas"),size,Brushes.WhiteSmoke,1);
                dc.DrawText(value,new Point(x,y));
                if(sensitive) expected.Add(new Rect(x,y,value.WidthIncludingTrailingWhitespace,size+6));
            }
            Write("Test document",1180,210,18);
            Write("API key",1180,280); Write("sk-",1740,280,10,true);
            Write("Navigation first",200,293,12);
            Write("test-a1b2c3d4e5f6a7b8c9d0e1f234567890.",1180,300,10,true);
            Write("API key",1180,400,8); Write("sk-test-",1700,400,8,true);
            Write("Navigation second",200,410,12);
            Write("b2c3d4e5f6a7b8c9d0e1f234567890a1.",1180,420,8,true);
            Write("Combination",1180,600); Write("Example#Access!",1720,600,10,true);
            Write("Navigation third",200,613,12);
            Write("5824",1180,620,10,true); Write(". Ordinary prose",1210,620);
        }
        var image=new RenderTargetBitmap(2560,1440,96,96,PixelFormats.Pbgra32); image.Render(visual);image.Freeze();
        foreach(string tag in new[]{english,"ru"})
        {
            if(!LocalOcr.Languages.Any(l=>l.Tag.Split('-')[0]==tag.Split('-')[0]))continue;
            var found=Task.Run(()=>SensitiveDataAnalyzer.AnalyzeAsync(image,tag,["credential"],CancellationToken.None)).GetAwaiter().GetResult();
            foreach(var target in expected)
                if(!found.Any(f=>f.Bounds.X<=target.Left+3 && f.Bounds.X+f.Bounds.Width>=target.Right-3 && f.Bounds.Y<target.Bottom && f.Bounds.Y+f.Bounds.Height>target.Top))
                    throw new Exception($"Desktop sidebar fixture missed wrapped value at {target}, OCR={tag}");
            Console.WriteLine($"PASS desktop sidebar and wrapped numeric password: all six complete pieces, OCR={tag}");
        }
    }
    private static void WrappedInlineSample(string english)
    {
        var visual = new DrawingVisual(); var expected = new List<Rect>();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 732, 340));
            void Write(string text, double x, double y, double size = 10, bool secret = false)
            {
                var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Consolas"), size, Brushes.WhiteSmoke, 1);
                dc.DrawText(formatted, new Point(x, y));
                if (secret) expected.Add(new Rect(x, y, formatted.WidthIncludingTrailingWhitespace, size + 6));
            }
            Write("Тестовый журнал", 20, 10, 20);
            Write("Для доступа использовался пароль", 20, 65, 13); Write("Example!Memory9831", 580, 65, 8, true); Write(".", 690, 65, 8);
            Write("Ключ доступа", 20, 130, 8); Write("sk-test-", 480, 130, 10, true);
            Write("a1b2c3d4e5f6a7b8c9d0e1f234567890,", 20, 149, 10, true); Write("обычный текст", 240, 149, 10);
            Write("Ключ интеграции", 20, 210, 8); Write("sk-", 600, 210, 10, true);
            Write("test-b2c3d4e5f6a7b8c9d0e1f234567890a1.", 20, 229, 10, true); Write("следующая запись", 260, 229, 10);
            Write("Version20261003", 20, 249, 10);
        }
        var image = new RenderTargetBitmap(732, 340, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze();
        foreach (string selected in new[] { english, "ru" })
        {
            if (!LocalOcr.Languages.Any(l => l.Tag.Split('-')[0] == selected.Split('-')[0])) continue;
            var found = Task.Run(() => SensitiveDataAnalyzer.AnalyzeAsync(image, selected, ["credential"], CancellationToken.None)).GetAwaiter().GetResult();
            foreach (var target in expected)
                if (!found.Any(f => f.Bounds.Y < target.Bottom && f.Bounds.Y + f.Bounds.Height > target.Y &&
                    f.Bounds.X <= target.Left + 3 && f.Bounds.X + f.Bounds.Width >= target.Right - 3))
                {
                    foreach (var installed in LocalOcr.Languages)
                    {
                        var diagnostic = Task.Run(() => LocalOcr.RecognizeLayoutAsync(image, installed.Tag)).GetAwaiter().GetResult();
                        Console.WriteLine("Generated wrapped reading " + installed.Tag + ": " + string.Join(" | ", diagnostic.Words.Where(w => w.Bounds.Y >= 125 && w.Bounds.Y < 168).Select(w => w.Text + "=" + w.Bounds + ",line=" + w.LineIndex)));
                    }
                    throw new Exception($"Wrapped inline fixture lost complete bounds at {target}, OCR={selected}");
                }
            if (found.Any(f => f.Bounds.Y <= 254 && f.Bounds.Y + f.Bounds.Height >= 254)) throw new Exception("Wrapped key included an unrelated later row");
            Console.WriteLine($"PASS wrapped inline keys and small password: all five physical pieces, OCR={selected}");
        }
    }

    private static void NarrativeJournalSample(string english, bool desktop = false)
    {
        var visual = new DrawingVisual(); var expected = new List<(string Category, Rect Bounds)>();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 900, 1100));
            double x = 10, y = 10, rowHeight = 24;
            void NewLine() { x = 10; y += rowHeight; rowHeight = 24; }
            void Write(string text, double size = 11, string? category = null)
            {
                var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), size, Brushes.WhiteSmoke, 1);
                if (x + formatted.WidthIncludingTrailingWhitespace > 870) NewLine();
                dc.DrawText(formatted, new Point(x, y));
                if (category != null) expected.Add((category, new Rect(x, y, formatted.WidthIncludingTrailingWhitespace, size + 7)));
                x += formatted.WidthIncludingTrailingWhitespace + 6; rowHeight = Math.Max(rowHeight, size + 12);
            }
            void List(string prose, string category, string[] values, double size)
            {
                NewLine(); Write(prose);
                foreach (string value in values) { Write(value, size, category); Write(",", size); }
                Write("."); NewLine();
            }
            List("Для авторизации использовались адреса", "email", ["security.check@example.com", "admin.control@example.org", "system.monitor@example.net"], 14);
            Write("Первое подключение выполнено с IP-адреса"); Write("192.0.2.147", 10, "ip"); Write(". Для входа использовался пароль"); Write("Secure!", 10, "credential");
            NewLine(); Write("Test#4829", 10, "credential"); Write("и тестовый API-ключ"); Write("sk-test-a8f2c9d4e1b70365f9a2d8c4b6e0f137", 10, "credential"); NewLine();
            Write("Дополнительная проверка с адреса"); Write("198.51.100.83", 10, "ip"); Write("с паролем"); Write("Access$$Vault!7294", 10, "credential"); Write(".");
            List("Система обнаружила IP-адреса", "ip", ["203.0.113.42", "192.0.2.186", "198.51.100.217", "203.0.113.91"], 11);
            List("Электронная почта", "email", ["network.alert@example.net", "cloud.backup@example.org", "server.logs@example.com", "test.connection@example.net"], 11);
            List("В журнале указаны пароли", "credential", ["Quantum#Shield2026!", "Dark!Protocol#8472", "Neon@Firewall5931", "Crystal#Access!6284"], 8);
            List("Для проверки созданы API-ключи", "credential", ["sk-test-7d9f2a4c8e1b6035d9a7f2c4e8b1d650", "sk-test-4b8e1d7f9a2c6035e8f4b1d9a7c20563", "sk-test-9c2f7a4d1e8b6035f9d2a7c4b1e80653", "sk-test-2e7a9f4c1d8b6035a9c2f7e4b1d80653", "sk-test-6f1d8a3c9e2b7045d8f1a6c3e9b20754"], 8);
            List("Дополнительные адреса", "email", ["database.sync@example.com", "private.access@example.org", "authentication.test@example.net", "backup.storage@example.com", "security.gateway@example.org"], 13);
            List("Использовались пароли", "credential", ["Shadow!Matrix#9274", "Binary@Control!5831", "Firewall#Omega2026", "Access!Denied#7492", "Protocol@Secure#1836"], 10);
            // This fixture checks readable numeric punctuation (10px font).
            // At 8px Consolas the raster has only 5-6px glyphs: Windows OCR
            // corrupts octets even after crop, 4x/6x, inversion or thresholding.
            // Do not relax IP validation to turn that corruption into addresses.
            List("Последние подключения с IP-адресов", "ip", ["192.0.2.54", "198.51.100.169", "203.0.113.228", "192.0.2.103", "198.51.100.241"], 10);
        }
        var rendered = new RenderTargetBitmap(900, 1100, 96, 96, PixelFormats.Pbgra32); rendered.Render(visual); rendered.Freeze();
        BitmapSource image = rendered;
        if (desktop)
        {
            var originalPixels = new byte[900*1100*4]; image.CopyPixels(originalPixels,900*4,0);
            var frame = new DrawingVisual();
            using (var dc = frame.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 2560, 1440));
                for (int i = 0; i < 44; i++) dc.DrawText(new FormattedText($"Navigation item {i + 1}", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, Brushes.WhiteSmoke, 1), new Point(30, 30 + i * 27));
            }
            var desktopFrame = new RenderTargetBitmap(2560, 1440, 96, 96, PixelFormats.Pbgra32); desktopFrame.Render(frame);
            var embedded = new WriteableBitmap(desktopFrame);
            embedded.WritePixels(new Int32Rect(1100,100,900,1100),originalPixels,900*4,0); embedded.Freeze(); image = embedded;
            // Copy exact pixels: drawing a bitmap through WPF can resample its
            // tiny glyphs and would compare two different source images.
            var embeddedPixels = new byte[originalPixels.Length]; image.CopyPixels(new Int32Rect(1100,100,900,1100),embeddedPixels,900*4,0);
            if (!originalPixels.SequenceEqual(embeddedPixels)) throw new Exception("Desktop fixture changed source pixels");
            expected = expected.Select(t => (t.Category, new Rect(t.Bounds.X + 1100, t.Bounds.Y + 100, t.Bounds.Width, t.Bounds.Height))).ToList();
        }
        string previous = DesktopTools.Localization.L.Language;
        try
        {
            DesktopTools.Localization.L.Use("ru");
            foreach (string selected in new[] { english, "ru" })
            {
                if (!LocalOcr.Languages.Any(l => l.Tag.Split('-')[0] == selected.Split('-')[0])) continue;
                var found = Task.Run(() => SensitiveDataAnalyzer.AnalyzeAsync(image, selected, AutoRedactOptions.AvailableCategories, CancellationToken.None)).GetAwaiter().GetResult();
                foreach (var target in expected)
                {
                    bool covered = found.Any(f => f.Categories.Contains(target.Category) && f.Bounds.Y < target.Bounds.Bottom && f.Bounds.Y + f.Bounds.Height > target.Bounds.Y
                        && f.Bounds.X <= target.Bounds.X + 3 && f.Bounds.X + f.Bounds.Width >= target.Bounds.Right - 3);
                    if (!covered)
                    {
                        if (desktop)
                        {
                            Directory.CreateDirectory("artifacts");
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                            using var output = File.Create("artifacts/auto-redact-generated-desktop.png"); encoder.Save(output);
                        }
                        foreach (var installed in LocalOcr.Languages)
                        {
                            var diagnostic = Task.Run(() => LocalOcr.RecognizeLayoutAsync(image, installed.Tag)).GetAwaiter().GetResult();
                            Console.WriteLine("Generated narrative row " + installed.Tag + ": " + string.Join(" | ", diagnostic.Words.Where(w => w.Bounds.Y < target.Bounds.Bottom && w.Bounds.Bottom > target.Bounds.Y).Select(w => w.Text + "=" + w.Bounds)));
                        }
                        throw new Exception($"Narrative journal lost complete {target.Category}, y={target.Bounds.Y}, x={target.Bounds.X}, OCR={selected}");
                    }
                }
                if (expected.Count != 41) throw new Exception("Journal fixture must cover 40 values with two pieces of the wrapped password");
                Console.WriteLine($"PASS narrative journal: all 41 text pieces, 8/10/11/13/14 fonts, desktop={desktop}, OCR={selected}");
            }
        }
        finally { DesktopTools.Localization.L.Use(previous); }
    }

    private static void HandleListSample(string english)
    {
        (string Category, string Text, double Size, double X, double Y)[] rows =
        [
            ("username", "DarkSpectre_404", 18, 58, 20), ("username", "PixelHunter_X7", 14, 51, 65), ("username", "QuantumVoid99", 11, 47, 108),
            ("email", "dark.spectre@example.com", 14, 47, 148), ("email", "pixelhunter@example.net", 11, 40, 185), ("email", "quantum.void@example.org", 9, 38, 218),
            ("phone", "+1 202-555-0164", 14, 46, 254), ("phone", "+1 202-555-0192", 11, 40, 292), ("ip", "IP: 203.0.113.87", 14, 47, 336),
            ("credential", "API: sk-test-f8a41d92e6b703c5d9a12f48b6e03725", 9, 36, 364), ("account", "User ID: usr_7391820465", 11, 40, 399), ("credential", "Пароль: Vortex!92#DarkX7", 14, 45, 439)
        ];
        var expected = new List<Rect>(); var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 744, 490));
            foreach (var row in rows)
            {
                var text = new FormattedText(row.Text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), row.Size, Brushes.WhiteSmoke, 1);
                dc.DrawText(text, new Point(row.X, row.Y));
                int start = row.Text.IndexOf(':') + 1;
                double prefix = start == 0 ? 0 : new FormattedText(row.Text[..(start + 1)], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), row.Size, Brushes.WhiteSmoke, 1).WidthIncludingTrailingWhitespace;
                expected.Add(new(row.X + prefix, row.Y, text.WidthIncludingTrailingWhitespace - prefix, row.Size + 7));
            }
        }
        var image = new RenderTargetBitmap(744, 490, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze();
        string previous = DesktopTools.Localization.L.Language;
        try
        {
            DesktopTools.Localization.L.Use("ru");
            foreach (string selected in new[] { english, "ru" })
            {
                if (!LocalOcr.Languages.Any(l => l.Tag.Split('-')[0] == selected.Split('-')[0])) continue;
                var found = Task.Run(() => SensitiveDataAnalyzer.AnalyzeAsync(image, selected, AutoRedactOptions.AvailableCategories, CancellationToken.None)).GetAwaiter().GetResult();
                for (int i = 0; i < rows.Length; i++)
                {
                    var target = expected[i]; var finding = found.FirstOrDefault(f => f.Categories.Contains(rows[i].Category) && f.Bounds.Y < target.Bottom && f.Bounds.Y + f.Bounds.Height > target.Y);
                    if (finding == null || finding.Bounds.X > target.X + 3 || finding.Bounds.X + finding.Bounds.Width < target.Right - 3)
                    {
                        var diagnostic = Task.Run(() => LocalOcr.RecognizeLayoutAsync(image, selected)).GetAwaiter().GetResult();
                        Console.WriteLine("Generated failing row: " + string.Join(" | ", diagnostic.Words.Where(w => w.Bounds.Y < target.Bottom && w.Bounds.Bottom > target.Y).Select(w => w.Text + "=" + w.Bounds)));
                        throw new Exception($"Mixed-size unlabelled sample lost complete {rows[i].Category} row {i + 1}, OCR={selected}; bounds={finding?.Bounds}");
                    }
                }
                if (found.Count != rows.Length) throw new Exception($"Mixed-size unlabelled sample region count {found.Count}/{rows.Length}, OCR={selected}");
                Console.WriteLine($"PASS mixed-size 9/11/14/18 text: 12 complete values including unlabelled handles, OCR={selected}");
            }
        }
        finally { DesktopTools.Localization.L.Use(previous); }
    }

    private static void TableSample(string english)
    {
        (string Category, string Label, string Value)[] rows =
        [
            ("username", "Никнейм", "ShadowWalker_777"), ("username", "Никнейм 2", "GlitchMaster_X"), ("username", "Никнейм 3", "NeonPhantom99"),
            ("email", "Email", "shadow.walker@example.com"), ("email", "Email 2", "glitchmaster@example.org"), ("email", "Email 3", "neon.phantom@example.net"),
            ("phone", "Телефон", "+1 202-555-0147"), ("phone", "Телефон 2", "+1 202-555-0183"), ("ip", "IP-адрес", "198.51.100.42"),
            ("credential", "API-ключ", "sk-test-7f9a2c4e8b1d6f03a5c9e2b7d4f0186a"), ("account", "User ID", "usr_8392047156"), ("credential", "Пароль", "TestOnly!X9m#42qL")
        ];
        var visual = new DrawingVisual(); var expected = new List<Rect>();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 744, 470));
            for (int i = 0; i < rows.Length; i++)
            {
                double y = 25 + i * 35;
                var label = new FormattedText(rows[i].Label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.WhiteSmoke, 1);
                var value = new FormattedText(rows[i].Value, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), 11, Brushes.WhiteSmoke, 1);
                dc.DrawText(label, new Point(35, y)); dc.DrawText(value, new Point(195, y));
                expected.Add(new(195, y, value.WidthIncludingTrailingWhitespace, 15));
            }
        }
        var image = new RenderTargetBitmap(744, 470, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze();
        string previous = DesktopTools.Localization.L.Language;
        try
        {
            DesktopTools.Localization.L.Use("ru");
            foreach (string selected in new[] { english, "ru" })
            {
                if (!LocalOcr.Languages.Any(l => l.Tag.Split('-')[0] == selected.Split('-')[0])) continue;
                var found = Task.Run(() => SensitiveDataAnalyzer.AnalyzeAsync(image, selected, AutoRedactOptions.AvailableCategories, CancellationToken.None)).GetAwaiter().GetResult();
                for (int i = 0; i < rows.Length; i++)
                {
                    var target = expected[i]; var finding = found.FirstOrDefault(f => f.Categories.Contains(rows[i].Category) && f.Bounds.Y < target.Bottom && f.Bounds.Y + f.Bounds.Height > target.Y);
                    if (finding == null || finding.Bounds.X > target.X + 3 || finding.Bounds.X + finding.Bounds.Width < target.Right - 3)
                        throw new Exception($"Two-column table lost complete {rows[i].Category} row {i + 1}, OCR={selected}; bounds={finding?.Bounds}");
                }
                if (found.Count != rows.Length) throw new Exception($"Two-column table region count {found.Count}/{rows.Length}, OCR={selected}");
                Console.WriteLine($"PASS real OCR two-column table: 12 complete values including user ID/password, OCR={selected}");
            }
        }
        finally { DesktopTools.Localization.L.Use(previous); }
    }

    private static void FullSample(string english)
    {
        (string Category, string Prefix, string Value)[] rows =
        [
            ("email", "", "alice.test@example.com"), ("email", "", "boris.demo@example.org"), ("email", "", "test.user123@example.net"),
            ("phone", "", "+1 (202) 555-0147"), ("phone", "", "+1 (202) 555-0183"),
            ("ip", "", "192.0.2.145"), ("ip", "", "198.51.100.27"), ("ip", "", "203.0.113.84"), ("ip", "", "2001:db8::1234"),
            ("path", "", @"C:\Users\TestUser\Documents\private.txt"), ("path", "", @"D:\Projects\DemoApp\config.json"),
            ("credential", "API-ключ: ", "sk-test-8f4c2a91d7e6b305c9f2a48e1d6b7c03"), ("credential", "API key: ", "fictional-demo-key-123456789"),
            ("credential", "GitHub token: ", "ghp_FAKEdemoToken1234567890abcdef"), ("credential", "Token: ", "fictional-access-token-987654321"),
            ("credential", "JWT: ", "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ0ZXN0In0.fakeSignature123456"),
            ("credential", "Пароль: ", "DemoPass123!"), ("credential", "Password: ", "ExampleSecret456!"), ("credential", "Password: ", "\"fictional password 789!\""),
            ("username", "Логин: ", "demo_user42"), ("username", "Username: ", "alice_test123"),
            ("account", "Account ID: ", "DEMO-482719"), ("account", "Номер счёта: ", "1234567890123456"),
            ("serial", "Serial number: ", "TEST-ABCD-1234-5678"), ("serial", "Серийный номер: ", "DEMO-9876-XYZ123")
        ];
        string previousLanguage = DesktopTools.Localization.L.Language;
        // Rows labelled in Russian can only be read when a Russian OCR recognizer is installed; hosted CI images ship en-US only.
        bool russianOcr = LocalOcr.Languages.Any(l => l.Tag.StartsWith("ru", StringComparison.OrdinalIgnoreCase));
        bool Needed(int row) => russianOcr || !rows[row].Prefix.Any(c => c >= 'Ѐ' && c <= 'ӿ');
        int neededRows = Enumerable.Range(0, rows.Length).Count(Needed);
        var failures = new List<string>();
        try
        {
            DesktopTools.Localization.L.Use("ru");
            foreach (double size in new[] { 14d, 10d })
            {
                int height = rows.Length * 26 + 40;
                var visual = new DrawingVisual(); var expected = new List<Rect>();
                FormattedText Text(string value) => new(value, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), size, Brushes.WhiteSmoke, 1);
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(55, 55, 54)), null, new Rect(0, 0, 800, height));
                    for (int i = 0; i < rows.Length; i++)
                    {
                        var row = rows[i]; double y = 20 + i * 26;
                        dc.DrawText(Text(row.Prefix + row.Value), new Point(20, y));
                        double prefixWidth = Text(row.Prefix).WidthIncludingTrailingWhitespace;
                        expected.Add(new(20 + prefixWidth, y, Text(row.Value).WidthIncludingTrailingWhitespace, size));
                    }
                }
                var image = new RenderTargetBitmap(800, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze();
                foreach (string selected in new[] { english, "ru" })
                {
                    if (!LocalOcr.Languages.Any(l => l.Tag.Split('-')[0] == selected.Split('-')[0])) continue;
                    var found = Task.Run(() => SensitiveDataAnalyzer.AnalyzeAsync(image, selected, AutoRedactOptions.AvailableCategories, CancellationToken.None)).GetAwaiter().GetResult();
                    for (int i = 0; i < rows.Length; i++)
                    {
                        if (!Needed(i)) continue;
                        var target = expected[i];
                        var finding = found.FirstOrDefault(f => f.Categories.Contains(rows[i].Category) && f.Bounds.Y < target.Y + 22 && f.Bounds.Y + f.Bounds.Height > target.Y);
                        if (finding == null || finding.Bounds.X > target.X + 3 || finding.Bounds.X + finding.Bounds.Width < target.Right - 3)
                            failures.Add($"{rows[i].Category} row {i + 1}, font={size}, OCR={selected}; region={finding?.Bounds}");
                    }
                    if (found.Count != neededRows) failures.Add($"Region count {found.Count}/{neededRows}, font={size}, OCR={selected}");
                    if (found.Count != neededRows && size == 10 && selected == english)
                    {
                        var diagnostic = Task.Run(() => LocalOcr.RecognizeLayoutAsync(image, english)).GetAwaiter().GetResult();
                        foreach (int index in new[] { 3, 4, 21 })
                            Console.WriteLine("Generated row " + (index + 1) + ": " + string.Join(" | ", diagnostic.Words.Where(w => w.Bounds.Y < expected[index].Y + 22 && w.Bounds.Bottom > expected[index].Y).Select(w => w.Text)));
                    }
                    Console.WriteLine($"Full 25-value privacy sample: font={size}, OCR={selected}, regions={found.Count}");
                }
            }
        }
        finally { DesktopTools.Localization.L.Use(previousLanguage); }
        if (failures.Count > 0) throw new Exception("Full privacy sample missed/partially covered: " + string.Join("; ", failures));
        Console.WriteLine("PASS all 25 values with complete bounds in both font sizes and OCR language selections");
    }
}
