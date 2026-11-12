using DesktopTools.Core;
using System.IO;

/// <summary>The OCR pack's pure parts: character lists, strips, DB boxes, batches, CTC decoding, layout, script mapping, settings and catalog entries.</summary>
internal static class OcrPackPipelineTests
{
    public static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }

        // Character lists: quoted, doubled quotes, escaped double quotes, plain and non-Latin entries; the list ends at the next key.
        var list = OcrPackPipeline.ParseCharacterList("PostProcess:\n  name: CTCLabelDecode\n  character_dict:\n  - '0'\n  - ''''\n  - \"\\\"\"\n  - a\n  - ё\nNext: 1\n");
        Check(list.SequenceEqual(["0", "'", "\"", "a", "ё"]), "Character list: " + string.Join("|", list));
        Check(OcrPackPipeline.ParseCharacterList("character_dict:\r\n- x\r\n- y\r\n").SequenceEqual(["x", "y"]), "Character list with CRLF and no indent");
        Check(OcrPackPipeline.ParseCharacterList("name: x\n").Length == 0, "No list");

        // Strips: one for short images; overlapping strips with contiguous cores for tall ones.
        var one = OcrPackPipeline.Strips(900);
        Check(one.Count == 1 && one[0] == new OcrPackStrip(0, 900, 0, 900), "Short image is one strip");
        var strips = OcrPackPipeline.Strips(2500);
        Check(strips.Count > 1 && strips[0].Top == 0 && strips[0].CoreTop == 0 && strips[^1].Top + strips[^1].Height == 2500 && strips[^1].CoreBottom == 2500, "Strips cover the image");
        Check(strips.All(s => s.Height <= 1024 && s.CoreTop >= s.Top && s.CoreBottom <= s.Top + s.Height), "Strip sizes");
        for (int i = 1; i < strips.Count; i++)
        {
            Check(strips[i].CoreTop == strips[i - 1].CoreBottom, "Cores are contiguous");
            Check(strips[i - 1].Top + strips[i - 1].Height - strips[i].Top >= 160, "Strips overlap by at least 160");
        }

        // DB boxes.
        float[] Map(int w, int h, int x0, int y0, int bw, int bh, float value)
        {
            var map = new float[w * h];
            for (int y = y0; y < y0 + bh; y++) for (int x = x0; x < x0 + bw; x++) map[y * w + x] = value;
            return map;
        }
        var whole = new OcrPackStrip(0, 32, 0, 32);
        var boxes = OcrPackPipeline.FindBoxes(Map(64, 32, 10, 12, 20, 6, .9f), 64, 32, 64, 32, 0, whole);
        double d = 20 * 6 * 1.5 / (2 * (20 + 6));
        Check(boxes.Count == 1, "One box: " + boxes.Count);
        var b = boxes[0];
        Check(b.X <= 10 && b.Y <= 12 && b.X + b.Width >= 30 && b.Y + b.Height >= 18, "Box contains the block: " + b);
        Check(b.X == (int)Math.Floor(10 - d) && b.Y == (int)Math.Floor(12 - d) && b.X + b.Width == (int)Math.Ceiling(30 + d), "Unclip distance: " + b);
        Check(Math.Abs(b.Score - .9f) < 1e-5, "Score");
        var edge = OcrPackPipeline.FindBoxes(Map(64, 32, 0, 0, 20, 6, .9f), 64, 32, 50, 20, 0, whole);
        Check(edge.Count == 1 && edge[0].X == 0 && edge[0].Y == 0, "Clamped at the top-left");
        var clamp = OcrPackPipeline.FindBoxes(Map(64, 32, 40, 12, 20, 6, .9f), 64, 32, 50, 20, 0, whole);
        Check(clamp.Count == 1 && clamp[0].X + clamp[0].Width <= 50 && clamp[0].Y + clamp[0].Height <= 20, "Clamped to the valid size: " + clamp[0]);
        Check(OcrPackPipeline.FindBoxes(Map(64, 32, 10, 12, 20, 6, .4f), 64, 32, 64, 32, 0, whole).Count == 0, "Low threshold block kept");
        // A picture with more lines than the limit: finding stops one past it (the engine then reports "too much text") instead of throwing.
        var dense = new float[640 * 640];
        for (int y = 0; y < 640; y++) for (int x = 0; x < 640; x++) if (x % 5 < 3 && y % 5 < 3) dense[y * 640 + x] = .9f;
        var many = OcrPackPipeline.FindBoxes(dense, 640, 640, 640, 640, 0, new OcrPackStrip(0, 640, 0, 640));
        Check(many.Count == OcrPackPipeline.MaxBoxes + 1, "Dense map box count: " + many.Count);
        var weak = Map(64, 32, 10, 12, 20, 6, .5f);
        Check(OcrPackPipeline.FindBoxes(weak, 64, 32, 64, 32, 0, whole).Count == 0, "Score below 0.6 kept");
        Check(OcrPackPipeline.FindBoxes(Map(64, 32, 10, 12, 20, 2, .9f), 64, 32, 64, 32, 0, whole).Count == 0, "Two-pixel-high block kept");
        var core = new OcrPackStrip(0, 32, 0, 10);
        Check(OcrPackPipeline.FindBoxes(Map(64, 32, 10, 12, 20, 6, .9f), 64, 32, 64, 32, 0, core).Count == 0, "Box outside the strip core kept");
        var shifted = OcrPackPipeline.FindBoxes(Map(64, 32, 10, 12, 20, 6, .9f), 64, 32, 64, 32, 500, new OcrPackStrip(500, 32, 500, 532));
        Check(shifted.Count == 1 && shifted[0].Y == b.Y + 500, "Offset shifts Y");

        // Batches.
        var batches = OcrPackPipeline.Batches([100, 900, 110, 120, 1000]);
        Check(batches.Count == 2 && batches.Any(x => x.Order().SequenceEqual([0, 2, 3])) && batches.Any(x => x.Order().SequenceEqual([1, 4])), "Batches by width");
        Check(OcrPackPipeline.Batches(Enumerable.Repeat(100, 20).ToArray()).All(x => x.Length <= 8), "At most eight per batch");
        Check(OcrPackPipeline.Batches(Enumerable.Repeat(100, 20).ToArray()).Sum(x => x.Length) == 20, "Every line in one batch");
        Check(OcrPackPipeline.InputWidth(100) == 320 && OcrPackPipeline.InputWidth(321) == 328 && OcrPackPipeline.InputWidth(328) == 328, "Input width");

        // CTC: blank 0, characters 1..3, space 4.
        float[] Steps(params int[] best)
        {
            var scores = new float[best.Length * 5];
            for (int t = 0; t < best.Length; t++) { for (int k = 0; k < 5; k++) scores[t * 5 + k] = .05f; scores[t * 5 + best[t]] = .8f; }
            return scores;
        }
        string[] abc = ["a", "b", "c"];
        var decoded = OcrPackPipeline.DecodeCtc(Steps(1, 1, 0, 2, 4, 3, 3, 0, 0), 9, 5, 9, abc);
        Check(decoded.Text == "ab c", "CTC text: " + decoded.Text);
        Check(decoded.Words.Count == 2 && decoded.Words[0] == (0, 3, "ab") && decoded.Words[1] == (5, 6, "c"), "CTC words: " + string.Join(",", decoded.Words));
        Check(Math.Abs(decoded.Confidence - .8f) < 1e-5, "CTC confidence");
        var cut = OcrPackPipeline.DecodeCtc(Steps(1, 1, 0, 2, 4, 3, 3, 0, 0), 9, 5, 5, abc);
        Check(cut.Text == "ab" && cut.Words.Count == 1, "CTC ignores padding steps: " + cut.Text);
        Check(OcrPackPipeline.DecodeCtc(Steps(0, 0, 0), 3, 5, 3, abc).Text == "", "All blank");

        // Layout: two boxes on one row join one line; the next row is the next line; weak lines are dropped.
        OcrPackLine Line(int x, int y, int w, int h, string text, float confidence = .9f)
        {
            var words = new List<(int, int, string)>(); int step = 0;
            foreach (var word in text.Split(' ')) { words.Add((step, step + word.Length - 1, word)); step += word.Length + 1; }
            return new(new OcrPackBox(x, y, w, h, .9f), new OcrPackDecoded(text, confidence, words), OcrPackPipeline.InputWidth(48 * w / h), OcrPackPipeline.InputWidth(48 * w / h) / 8);
        }
        var layout = OcrPackPipeline.ToLayout([Line(250, 102, 80, 20, "Help"), Line(10, 100, 200, 20, "File Edit"), Line(10, 140, 150, 20, "Next row"), Line(10, 180, 150, 20, "noise", .3f)], 800, 300);
        Check(layout.PixelWidth == 800 && layout.PixelHeight == 300, "Layout size");
        Check(string.Join(" ", layout.Words.Select(w => w.Text)) == "File Edit Help Next row", "Layout order: " + string.Join(" ", layout.Words.Select(w => w.Text)));
        Check(layout.Words.Where(w => w.Text is "File" or "Edit" or "Help").Select(w => w.LineIndex).Distinct().Count() == 1, "One row is one line");
        Check(layout.Words.First(w => w.Text == "Next").LineIndex == layout.Words.First(w => w.Text == "File").LineIndex + 1, "Next row is the next line");
        Check(layout.Words.All(w => w.Bounds.Left >= 10 && w.Bounds.Right <= 330 && w.Bounds.Top >= 100 && w.Bounds.Bottom <= 160), "Word boxes inside their lines");
        var far = OcrPackPipeline.ToLayout([Line(10, 100, 100, 20, "Left"), Line(600, 100, 100, 20, "Right")], 800, 300);
        Check(far.Words[0].LineIndex != far.Words[1].LineIndex, "Columns far apart stay separate lines");

        // Script mapping and tags.
        Check(OcrPackPipeline.ScriptForLanguage("ru-RU") == OcrPackScript.Cyrillic && OcrPackPipeline.ScriptForLanguage("uk") == OcrPackScript.Cyrillic, "Cyrillic languages");
        Check(OcrPackPipeline.ScriptForLanguage("ja") == OcrPackScript.ChineseJapanese && OcrPackPipeline.ScriptForLanguage("zh-Hans-CN") == OcrPackScript.ChineseJapanese, "CJK languages");
        Check(OcrPackPipeline.ScriptForLanguage("de-DE") == OcrPackScript.Latin && OcrPackPipeline.ScriptForLanguage(null) == OcrPackScript.Latin, "Latin default");
        Check(OcrPackPipeline.TryParseTag("pack:cyrillic", out var script) && script == OcrPackScript.Cyrillic, "Parse tag");
        Check(!OcrPackPipeline.TryParseTag("en-US", out _) && !OcrPackPipeline.TryParseTag(null, out _) && !OcrPackPipeline.TryParseTag("pack:klingon", out _), "Reject tags");
        foreach (var s in Enum.GetValues<OcrPackScript>())
            Check(OcrPackPipeline.TryParseTag(OcrPackPipeline.Tag(s), out var back) && back == s, "Tag round trip " + s);
        Check(OcrPackPipeline.PackId(OcrPackScript.Latin) == "ocr-latin" && OcrPackPipeline.PackId(OcrPackScript.Cyrillic) == "ocr-cyrillic" && OcrPackPipeline.PackId(OcrPackScript.ChineseJapanese) == "ocr-chinese-japanese", "Pack ids");

        // Settings.
        var settings = new AppSettings();
        Check(settings.OcrEngine == "Windows" && settings.OcrPackScript == "", "Default engine");
        settings.OcrEngine = "GPU"; settings.OcrPackScript = "klingon"; SettingsStore.Validate(settings);
        Check(settings.OcrEngine == "Windows" && settings.OcrPackScript == "", "Unknown engine values kept");
        settings.OcrEngine = null!; settings.OcrPackScript = null!; SettingsStore.Validate(settings);
        Check(settings.OcrEngine == "Windows" && settings.OcrPackScript == "", "Null engine values kept");
        settings.OcrEngine = "Pack"; settings.OcrPackScript = "cyrillic"; SettingsStore.Validate(settings);
        Check(settings.OcrEngine == "Pack" && settings.OcrPackScript == "cyrillic", "Valid engine values lost");

        // The embedded catalog lists exactly the admitted PP-OCRv5 files.
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "global.json"))) root = Path.GetDirectoryName(root) ?? throw new Exception("Repository root not found");
        using var stream = File.OpenRead(Path.Combine(root, "src", "DesktopTools", "Assets", "Models", "catalog.json"));
        var packs = ModelCatalog.Parse(stream).ToDictionary(p => p.Id);
        var expected = new Dictionary<string, (string Repo, string Revision, (string File, long Bytes, string Sha)[] Files)>
        {
            ["ocr-text-detector"] = ("PP-OCRv5_mobile_det_onnx", "e6f4fa85f00e168c862bc462aebca69eef9b3d3d", [("inference.onnx", 4_826_518, "a431985659dc921974177a95adcfbb90fd9e51989a5e04d70d0b75f597b6e61d")]),
            ["ocr-latin"] = ("latin_PP-OCRv5_mobile_rec_onnx", "89d3a50e2c27e2e7cceeab0e944c25c807d5db4f",
                [("inference.onnx", 8_042_023, "7888113072263cb471b93f66dd5e2ad70548dc526fa1ace760d0d973dd121498"), ("inference.yml", 6_817, "0bbe984570f597af3638e50bdf2e8276f3ab26a61966096538b3b0d1849f5c84")]),
            ["ocr-cyrillic"] = ("eslav_PP-OCRv5_mobile_rec_onnx", "9a32171fc5718746875e1a261818884517975013",
                [("inference.onnx", 7_887_627, "b3018ef2b09a0250b6e0c8e871c927098363e5fd4df890cc68e8358eb0aaf1bd"), ("inference.yml", 4_538, "025039bac23eb4a308efcefa4d58eab3af440767815c6ba6938468bf6353ee5a")]),
            ["ocr-chinese-japanese"] = ("PP-OCRv5_mobile_rec_onnx", "ed152b8b495f84de93cda5709d768548a9127622",
                [("inference.onnx", 16_534_782, "da72dc72ca4dc220df0dfde68c1dedc31c58d3e76a25871122e5056227d50092"), ("inference.yml", 148_345, "5dfeb2777f6d0db8177d8128a8acfcf6e6276dc4ac73ea3bf0dc06d6a5e85d8e")]),
        };
        foreach (var (id, want) in expected)
        {
            Check(packs.TryGetValue(id, out var pack), "Catalog lacks " + id);
            Check(pack!.LicenseId == "apache-2.0" && pack.LicenseFile == "Apache-2.0.txt" && pack.Revision == want.Revision, id + " licence or revision");
            Check(pack.Upstream == "https://huggingface.co/PaddlePaddle/" + want.Repo, id + " upstream");
            Check(pack.Files.Length == want.Files.Length, id + " file count");
            foreach (var (file, bytes, sha) in want.Files)
            {
                var entry = pack.Files.Single(f => f.File == file);
                Check(entry.Bytes == bytes && entry.Sha256 == sha, id + " " + file + " size or hash");
                Check(entry.Url == $"https://huggingface.co/PaddlePaddle/{want.Repo}/resolve/{want.Revision}/{file}", id + " " + file + " url");
            }
            Check(File.Exists(Path.Combine(root, "src", "DesktopTools", "Assets", "Models", "Licenses", pack.LicenseFile)), "Licence text missing");
        }
        Check(packs.Keys.Count(k => k.StartsWith("ocr-", StringComparison.Ordinal)) == 4, "Only the admitted OCR files");
    }
}
