using DesktopTools.Core;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Extras;
using DesktopTools.Localization;

internal static class AutoRedactTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Auto redact defaults preserve opt-in capture", () =>
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(new AppSettings()));
            check(json.RootElement.TryGetProperty("AutoRedact", out var options), "Missing screenshot review preferences");
            check(!options.GetProperty("Enabled").GetBoolean(), "Automatic review must be opt-in");
            check(options.GetProperty("Style").GetString() == "Solid", "Default redaction must be opaque");
        });
        test("Auto redact preferences normalize without changing unrelated settings", () =>
        {
            var settings = JsonSerializer.Deserialize<AppSettings>("{\"Language\":\"ru\",\"AutoRedact\":{\"Enabled\":true,\"Style\":\"Unknown\",\"Categories\":[\"email\",\"email\",\"bogus\"],\"Language\":\"de-DE\"}}")!;
            SettingsStore.Validate(settings);
            check(settings.AutoRedact.Enabled && settings.AutoRedact.Style == "Solid" && settings.AutoRedact.Categories.SequenceEqual(new[] { "email" }), "Invalid preferences survived");
            check(settings.AutoRedact.Language == "de-DE" && settings.Language == "ru", "Language preferences interfered");
        });
        test("Sensitive emails cover all OCR words with clipped padding", () =>
        {
            var layout = new OcrLayout(200, 100, [new("alice", new Rect(1, 2, 30, 15), 0), new("@", new Rect(32, 2, 10, 15), 0), new("example.test", new Rect(44, 2, 90, 15), 0)]);
            var found = SensitiveDataDetector.Detect(layout, ["email"]);
            check(found.Count == 1 && found[0].Bounds == new Int32Rect(0, 0, 137, 20), "Split email geometry missed characters");
            check(!found[0].MaskedExcerpt.Contains("alice"), "Finding label exposed private text");
        });
        test("Sensitive detection validates addresses and context", () =>
        {
            var layout = new OcrLayout(600, 100, [new("999.1.1.1 2026-09-30 12345 Alice", new Rect(10, 10, 300, 15), 0)]);
            check(SensitiveDataDetector.Detect(layout, ["ip", "phone", "username", "account"]).Count == 0, "Ordinary numbers/name flagged");
            layout = layout with { Words = [new("Account ID: 12345", new Rect(10, 10, 160, 15), 0), new("192.168.0.1", new Rect(10, 50, 100, 15), 1)] };
            var found = SensitiveDataDetector.Detect(layout, ["ip", "account"]);
            check(found.Count == 2 && found.Any(f => f.Categories.Contains("account")) && found.Any(f => f.Categories.Contains("ip")), "Context/address not detected");
        });
        test("Sensitive categories isolate paths and labelled credentials", () =>
        {
            var layout = new OcrLayout(600, 160, [new(@"C:\Users\Alice\Documents\private.txt", new Rect(10, 10, 320, 15), 0), new("API key: invented-test-key-1234", new Rect(10, 50, 320, 15), 1), new("Benutzername: alice123", new Rect(10, 90, 250, 15), 2)]);
            check(SensitiveDataDetector.Detect(layout, ["path"]).Count == 1, "Path detection missing");
            var found = SensitiveDataDetector.Detect(layout, ["credential", "username"]);
            check(found.Count == 2, "Credential/contextual username missing");
            check(SensitiveDataDetector.Detect(layout, []).Count == 0, "Disabled categories produced findings");
        });
        test("OCR slashed zeros retain sensitive IP and key geometry", () =>
        {
            var layout = new OcrLayout(700, 120, [new("192.ø.2.145", new Rect(150, 10, 140, 20), 0),
                new("sk-test-8f4c2a91d7e6b3Ø5c9f2a48e1d6b7cø3", new Rect(150, 55, 500, 20), 1)]);
            var found = SensitiveDataDetector.Detect(layout, ["ip", "credential"]);
            check(found.Count == 2 && found[0].Categories.Contains("ip") && found[1].Categories.Contains("credential"), "Slashed zeros prevented detection");
            check(found[0].Bounds == new Int32Rect(147, 7, 146, 26) && found[1].Bounds == new Int32Rect(147, 52, 506, 26), "OCR normalization changed source geometry");
            check(SensitiveDataDetector.Detect(layout, ["email"]).Count == 0, "Normalization ignored category preferences");
        });
        test("Russian API labels and password values are suggested", () =>
        {
            var layout = new OcrLayout(700, 160, [new("API-ключ:", new Rect(10, 10, 100, 20), 0), new("fictional-value-1234", new Rect(130, 10, 200, 20), 0),
                new("Пароль:", new Rect(10, 55, 100, 20), 1), new("fake!pass123", new Rect(130, 55, 150, 20), 1),
                new("Password security recommendations", new Rect(10, 100, 400, 20), 2)]);
            var found = SensitiveDataDetector.Detect(layout, ["credential"]);
            check(found.Count == 2 && found.All(f => f.Bounds.X == 127), "Russian key/password context lost values or flagged ordinary text");
        });
        test("Detached password symbols retain both surrounding fragments", () =>
        {
            var layout = new OcrLayout(300, 100, [new("Access", new Rect(10, 10, 32.5, 7.5), 0), new("!", new Rect(45, 10, 1.5, 7.5), 0), new("Denied#7492", new Rect(49, 10, 60, 7.5), 0)]);
            var found = SensitiveDataDetector.Detect(layout, ["credential"]);
            check(found.Count == 1 && found[0].Bounds.X == 7 && found[0].Bounds.X + found[0].Bounds.Width >= 112, "Password text before detached symbol remains visible");
        });
        test("Spaced arithmetic operators do not join into password suggestions", () =>
        {
            foreach (string symbol in new[] { "*", "&", "?" })
            {
                var layout = new OcrLayout(300, 100, [new("total", new Rect(10, 10, 30, 10), 0), new(symbol, new Rect(45, 10, 5, 10), 0), new("rate2026", new Rect(55, 10, 55, 10), 0)]);
                check(SensitiveDataDetector.Detect(layout, ["credential"]).Count == 0, "Spaced code operator became a password: " + symbol);
            }
        });
        test("Tiny prefixed keys tolerate measured five-for-s without losing the prefix", () =>
        {
            var layout = new OcrLayout(600, 100, [new("5k", new Rect(10, 10, 10, 8), 0), new("-test-9c2f7a4d1eBb6e3sf9d2a7c4b1eBE53", new Rect(21, 10, 180, 8), 0)]);
            var found = SensitiveDataDetector.Detect(layout, ["credential"]);
            check(found.Count == 1 && found[0].Bounds.X == 7 && found[0].Bounds.X + found[0].Bounds.Width >= 204, "OCR five-for-s hid part of the key");
        });
        test("OCR fragments cover complete labelled values", () =>
        {
            var layout = new OcrLayout(600, 160, [new("Username:", new Rect(10, 10, 80, 15), 0), new("alice", new Rect(100, 10, 40, 15), 0), new("test123", new Rect(148, 10, 60, 15), 0),
                new("Account", new Rect(10, 55, 60, 15), 1), new("ID", new Rect(76, 55, 15, 15), 1), new(":", new Rect(93, 55, 3, 15), 1), new("DEMO", new Rect(110, 55, 40, 15), 1), new("482719", new Rect(158, 55, 55, 15), 1),
                new("Serial number:", new Rect(10, 100, 90, 15), 2), new("TEST", new Rect(110, 100, 40, 15), 2), new("ABCD-1234-5678", new Rect(155, 100, 140, 15), 2)]);
            var found = SensitiveDataDetector.Detect(layout, ["username", "account", "serial"]);
            check(found.Count == 3 && (found[0].Bounds.X + found[0].Bounds.Width) >= 211 && (found[1].Bounds.X + found[1].Bounds.Width) >= 216 && (found[2].Bounds.X + found[2].Bounds.Width) >= 298, "Fragmented field suffix remains visible");
        });
        test("OCR separators preserve full paths and IPv6", () =>
        {
            var layout = new OcrLayout(600, 160, [new(@"C : \Users \TestUser \private. txt", new Rect(10, 10, 320, 15), 0),
                new("2001 :db8: : 1234", new Rect(10, 55, 160, 15), 1), new("198.51. lee. 27", new Rect(10, 100, 160, 15), 2)]);
            var found = SensitiveDataDetector.Detect(layout, ["path", "ip"]);
            check(found.Count == 3 && found[0].Categories.Contains("path") && found.Skip(1).All(f => f.Categories.Contains("ip")), "OCR spacing or zero/one confusion hid machine data");
            layout = layout with { Words = [new("999.1.1.1 2026.10.01 Read me.txt", new Rect(10, 10, 350, 15), 0)] };
            check(SensitiveDataDetector.Detect(layout, ["path", "ip"]).Count == 0, "Invalid addresses or ordinary filenames flagged");
        });
        test("Adjacent OCR email fragments cover the local part", () =>
        {
            var layout = new OcrLayout(400, 100, [new("ali", new Rect(10, 10, 20, 12), 0), new("ce.test@example.com", new Rect(32, 10, 170, 12), 0)]);
            var found = SensitiveDataDetector.Detect(layout, ["email"]);
            check(found.Count == 1 && found[0].Bounds.X == 7 && found[0].Bounds.Width == 198, "Email prefix stays visible");
        });
        test("Small-text phones and account ID label confusions retain full bounds", () =>
        {
            var layout = new OcrLayout(500, 120, [new("+1", new Rect(10, 10, 15, 10), 0), new("(2ø2)", new Rect(32, 10, 35, 10), 0), new("sss-ø147", new Rect(74, 10, 65, 10), 0),
                new("Account 10:", new Rect(10, 45, 80, 10), 1), new("DEMO-482719", new Rect(100, 45, 90, 10), 1),
                new("status 12345 TEST-ABCD-1234-5678 2026-10-01", new Rect(10, 80, 440, 10), 2)]);
            var found = SensitiveDataDetector.Detect(layout, ["phone", "account"]);
            check(found.Count == 2 && found[0].Categories.Contains("phone") && found[0].Bounds.X == 7 && found[0].Bounds.Width == 135 && found[1].Categories.Contains("account"), "Phone OCR confusions or ID label dropped private values");
        });
        test("Close spaced phone groups retain their formatting context", () =>
        {
            var layout = new OcrLayout(200, 50, [new("202", new Rect(10, 10, 18, 10), 0), new("555", new Rect(30, 10, 18, 10), 0), new("0147", new Rect(50, 10, 24, 10), 0)]);
            var found = SensitiveDataDetector.Detect(layout, ["phone"]);
            check(found.Count == 1 && found[0].Bounds == new Int32Rect(7, 7, 70, 16), "Compact joining lost local telephone groups");
        });
        test("Complementary OCR readings connect a known label to its value", () =>
        {
            var layout = new OcrLayout(400, 100, [new("Password:", new Rect(20, 10, 60, 10), 0), new("ExampleSecret456!", new Rect(88, 10, 120, 12), 1),
                new("unrelated next column", new Rect(310, 10, 80, 12), 2)]);
            var found = SensitiveDataDetector.Detect(layout, ["credential"]);
            check(found.Count == 1 && found[0].Bounds == new Int32Rect(85, 7, 126, 18), "Complementary field value missing or included distant column");
        });
        test("Two-column table covers complete user ID password and nickname cells", () =>
        {
            var layout = new OcrLayout(700, 160, [new("User ID", new Rect(35, 10, 40, 12), 0), new("usr_", new Rect(195, 10, 25, 12), 1), new("8392047156", new Rect(225, 10, 70, 12), 1),
                new("Пароль", new Rect(35, 50, 45, 12), 2), new("TestOnly!", new Rect(195, 50, 60, 12), 3), new("X9m#42qL", new Rect(260, 50, 60, 12), 3),
                new("Никнейм 2", new Rect(35, 90, 65, 12), 4), new("GlitchMaster_X", new Rect(195, 90, 110, 12), 5),
                new("unrelated", new Rect(530, 50, 70, 12), 6)]);
            var found = SensitiveDataDetector.Detect(layout, ["account", "credential", "username"]);
            check(found.Count == 3 && found[0].Categories.Contains("account") && found[0].Bounds == new Int32Rect(192, 7, 106, 18)
                && found[1].Categories.Contains("credential") && found[1].Bounds == new Int32Rect(192, 47, 131, 18)
                && found[2].Categories.Contains("username"), "Separated table cells lost complete values or included another column");
            check(SensitiveDataDetector.Detect(layout, ["account"]).Count == 1, "Disabled table categories leaked into results");
        });
        test("Table values span complementary native lines without reaching another column", () =>
        {
            var layout = new OcrLayout(700, 120, [new("User ID", new Rect(35, 10, 40, 12), 0), new("usr_", new Rect(195, 10, 25, 12), 1), new("8392047156", new Rect(225, 10, 70, 12), 2),
                new("Пароль", new Rect(35, 50, 45, 12), 3), new("TestOnly!", new Rect(195, 50, 60, 12), 4), new("X9m#42qL", new Rect(260, 50, 60, 12), 5),
                new("unrelated", new Rect(530, 50, 70, 12), 6)]);
            var found = SensitiveDataDetector.Detect(layout, ["account", "credential"]);
            check(found.Count == 2 && found[0].Bounds == new Int32Rect(192, 7, 106, 18) && found[1].Bounds == new Int32Rect(192, 47, 131, 18), "Complementary native lines leave table value suffixes visible");
        });
        test("Short first OCR fragment does not discard a complete table identifier", () =>
        {
            var layout = new OcrLayout(700, 120, [new("User ID", new Rect(35, 10, 40, 12), 0), new("a_", new Rect(195, 10, 14, 12), 1), new("8392047156", new Rect(214, 10, 70, 12), 2),
                new("Пароль", new Rect(35, 50, 45, 12), 3), new("TestOnly!X9m#42qL", new Rect(195, 50, 125, 12), 4)]);
            var found = SensitiveDataDetector.Detect(layout, ["account"]);
            check(found.Count == 1 && found[0].Bounds == new Int32Rect(192, 7, 95, 18), "Short first fragment discarded the complete identifier");
        });
        test("Table email suggestion includes a disconnected local-part prefix", () =>
        {
            var layout = new OcrLayout(700, 120, [new("Email 3", new Rect(35, 10, 50, 12), 0), new("neon", new Rect(195, 10, 28, 12), 1), new("phantom@example.net", new Rect(229, 10, 140, 12), 1),
                new("User ID", new Rect(35, 50, 40, 12), 2), new("usr_1234567", new Rect(195, 50, 80, 12), 3)]);
            var found = SensitiveDataDetector.Detect(layout, ["email"]);
            check(found.Count == 1 && found[0].Bounds == new Int32Rect(192, 7, 180, 18), "Table email local-part prefix remains visible");
        });
        test("Table detection does not redact password prose or unaligned distant text", () =>
        {
            var layout = new OcrLayout(700, 160, [new("Password security recommendations", new Rect(35, 10, 260, 12), 0), new("Read more", new Rect(400, 10, 75, 12), 1),
                new("Password", new Rect(35, 50, 60, 12), 2), new("unrelated", new Rect(530, 50, 70, 12), 3),
                new("User ID", new Rect(35, 90, 40, 12), 4), new("documentation", new Rect(340, 90, 100, 12), 5)]);
            check(SensitiveDataDetector.Detect(layout, ["credential", "account"]).Count == 0, "Advice or unrelated columns were treated as table secrets");
        });
        test("Unlabelled handle list retains different font sizes and split suffixes", () =>
        {
            var layout = new OcrLayout(700, 230, [new("DarkSpectre", new Rect(58, 20, 120, 19), 0), new("404", new Rect(190, 22, 33, 14), 0),
                new("PixelHunter", new Rect(51, 65, 101, 13), 1), new("X7", new Rect(160, 67, 20, 12), 1),
                new("QuantumVoid99", new Rect(47, 108, 108, 14), 2), new("user@example.test", new Rect(47, 148, 180, 12), 3)]);
            var found = SensitiveDataDetector.Detect(layout, ["username"]);
            check(found.Count == 3 && found[0].Bounds == new Int32Rect(55, 17, 171, 25) && found[1].Bounds == new Int32Rect(48, 62, 135, 20)
                && found[2].Bounds == new Int32Rect(44, 105, 114, 20), "Unlabelled aliases or complete suffix geometry missing");
            check(SensitiveDataDetector.Detect(layout, ["email"]).Count == 1, "Disabled handle category produced findings");
        });
        test("Unlabelled handle fragments can occupy separate native OCR lines", () =>
        {
            var layout = new OcrLayout(400, 150, [new("a_", new Rect(50, 20, 12, 12), 0), new("8392047156", new Rect(67, 20, 70, 12), 1),
                new("QuantumVoid", new Rect(50, 65, 80, 14), 2), new("99", new Rect(135, 65, 15, 12), 3), new("unrelated", new Rect(290, 20, 70, 12), 4)]);
            var found = SensitiveDataDetector.Detect(layout, ["username"]);
            check(found.Count == 2 && found[0].Bounds == new Int32Rect(47, 17, 93, 18) && found[1].Bounds == new Int32Rect(47, 62, 106, 20), "Separate native lines or short prefix leave handle suffixes visible");
        });
        test("Small monospace phone OCR zero substitutions preserve full values", () =>
        {
            var layout = new OcrLayout(500, 120, [new("+1 2e2-555-e164", new Rect(40, 20, 125, 12), 0), new("2e26-1e-e1", new Rect(40, 65, 100, 12), 1)]);
            var found = SensitiveDataDetector.Detect(layout, ["phone"]);
            check(found.Count == 1 && found[0].Bounds == new Int32Rect(37, 17, 131, 18), "Measured phone zero substitution lost value or flagged a date");
        });
        test("Overlapping complementary handle suffixes retain complete cover bounds", () =>
        {
            var layout = new OcrLayout(500, 120, [new("DarkSpectre404", new Rect(50, 20, 145, 14), 0), new("404", new Rect(170, 20, 40, 14), 1),
                new("QuantumVoid99", new Rect(50, 65, 100, 14), 2)]);
            var found = SensitiveDataDetector.Detect(layout, ["username"]);
            check(found.Count == 2 && found[0].Bounds == new Int32Rect(47, 17, 166, 20), "Overlapping suffix extends beyond its cover");
        });
        test("OCR-distorted table labels are not unlabelled handles", () =>
        {
            var layout = new OcrLayout(500, 160, [new("HnkHeiM2", new Rect(35, 10, 55, 12), 0), new("GlitchMaster_X7", new Rect(195, 10, 100, 12), 1),
                new("HnkHeiM3", new Rect(35, 50, 55, 12), 2), new("QuantumVoid99", new Rect(195, 50, 100, 12), 3),
                new("User10", new Rect(35, 90, 45, 12), 4), new("usr_1234567", new Rect(195, 90, 90, 12), 5)]);
            var found = SensitiveDataDetector.Detect(layout, ["username"]);
            check(found.Count == 3 && found.All(f => f.Bounds.X >= 192), "Table label column became false username findings");
        });
        test("Unlabelled handle suggestions require a list rather than ordinary text", () =>
        {
            var layout = new OcrLayout(700, 200, [new("DesktopTools127", new Rect(40, 10, 150, 15), 0),
                new("Password security recommendations", new Rect(40, 50, 300, 15), 1), new("FPS 60", new Rect(40, 90, 70, 15), 2),
                new("Progress 100", new Rect(40, 130, 100, 15), 3)]);
            check(SensitiveDataDetector.Detect(layout, ["username"]).Count == 0, "Ordinary prose or isolated product/version text became a handle list");
        });
        test("Narrative passwords and comma-separated secret lists retain full values", () =>
        {
            var layout = new OcrLayout(900, 150, [new("Для входа использовался пароль", new Rect(10, 10, 270, 12), 0), new("Secure!", new Rect(290, 10, 55, 12), 0),
                new("Test#4829", new Rect(10, 35, 70, 12), 1),
                new("Пароли", new Rect(10, 65, 55, 12), 2), new("Quantum#Shield2026!,", new Rect(75, 65, 135, 12), 2), new("Dark!Protocol#8472,", new Rect(220, 65, 130, 12), 2),
                new("Neоn@Firewаll5931", new Rect(360, 65, 120, 12), 2), new("Crystal#Access!6284.", new Rect(490, 65, 135, 12), 2),
                new("Password security recommendations counter=100% alice@example.test", new Rect(10, 110, 700, 12), 3)]);
            var found = SensitiveDataDetector.Detect(layout, ["credential"]);
            check(found.Count == 6 && found[0].Bounds == new Int32Rect(287, 7, 61, 18) && found[1].Bounds == new Int32Rect(7, 32, 76, 18)
                && found.Skip(2).All(f => f.Bounds.Y == 62), "Inline/wrapped/list passwords missing or ordinary text flagged");
            check(SensitiveDataDetector.Detect(layout, ["email"]).Count == 1, "Password suggestions ignored categories");
        });
        test("Wrapped credentials survive a text column moving within a full monitor", () =>
        {
            var words = new OcrWordBox[] { new("Password Secure!", new Rect(290,10,120,10),0), new("Test#4829",new Rect(10,27,80,10),1),
                new("sk-test-",new Rect(500,65,60,10),2), new("a1b2c3d4e5f6a7b8c9d0",new Rect(10,82,180,10),3) };
            foreach (int offset in new[]{0,1100})
            {
                var shifted = words.Select(w => w with { Bounds = new Rect(w.Bounds.X+offset,w.Bounds.Y+100,w.Bounds.Width,w.Bounds.Height) }).ToArray();
                var found = SensitiveDataDetector.Detect(new(2560,1440,shifted), ["credential"]);
                check(found.Count == 4 && found.Any(f=>f.Bounds.X==offset+497) && found.Any(f=>f.Bounds.X==offset+287), "Desktop margins changed wrapped credential detection");
            }
        });
        test("Wrapped numeric password recovery excludes URL fragments", () =>
        {
            var layout=new OcrLayout(2560,1440,[new("See",new Rect(1180,600,30,10),0),new("https://example.test/Alpha#Beta!",new Rect(1720,600,300,10),0),new("2026",new Rect(1180,620,32,10),1)]);
            check(SensitiveDataDetector.Detect(layout,["credential"]).Count==0,"URL fragment and following year became a password");
        });
        test("Many wrapped prefixes keep intermediate allocation bounded", () =>
        {
            var words=Enumerable.Range(0,2000).SelectMany(i=>new[]{new OcrWordBox("API key",new Rect(100,20+i*30,80,10),i),new OcrWordBox("sk-",new Rect(650,20+i*30,20,10),i)}).ToArray();
            long before=GC.GetAllocatedBytesForCurrentThread();
            var found=SensitiveDataDetector.Detect(new(900,61000,words),["credential"]);
            long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            check(found.Count==0 && allocated<128_000_000,"Wrapped-prefix analysis exceeded128MB intermediate allocations: "+allocated);
        });
        test("Wrapped password with numeric tail covers both pieces without swallowing prose", () =>
        {
            var layout=new OcrLayout(2560,1440,[new("Combination",new Rect(1180,600,90,10),0),new("Example#Access!",new Rect(1720,600,140,10),0),
                new("Sidebar",new Rect(200,615,70,10),1),new("5824.",new Rect(1180,620,32,10),2),new("Ordinary prose",new Rect(1225,620,150,10),2)]);
            var found=SensitiveDataDetector.Detect(layout,["credential"]);
            check(found.Count==2 && found.Any(f=>f.Bounds==new Int32Rect(1177,617,38,16)),"Numeric password continuation was missed or ordinary prose included");
            layout=layout with {Words=layout.Words.Select(w=>w.Text=="Example#Access!" ? w with {Text="Important!"}:w).ToArray()};
            check(SensitiveDataDetector.Detect(layout,["credential"]).Count==0,"Sentence ending in exclamation became a wrapped password");
        });
        test("Wrapped key continuation stays in its text column despite an earlier sidebar row", () =>
        {
            var layout = new OcrLayout(2560,1440,[new("API key",new Rect(1180,280,80,10),0),new("sk-",new Rect(1740,280,20,10),0),
                new("Sidebar navigation",new Rect(200,292,170,12),1),new("test-a1b2c3d4e5f6a7b8c9d0",new Rect(1180,300,240,10),2)]);
            var found=SensitiveDataDetector.Detect(layout,["credential"]);
            check(found.Count==2 && found.Any(f=>f.Bounds.X==1177 && f.Bounds.Width>=246),"Sidebar displaced the actual key continuation");
            var separated=layout with {Words=layout.Words.Select(w=>w.Text=="sk-" ? w with {LineIndex=5}:w).ToArray()};
            check(SensitiveDataDetector.Detect(separated,["credential"]).Count==2,"Separate native prefix line lost paragraph context");
            separated=separated with {Words=separated.Words.Concat(new[]{new OcrWordBox("access",new Rect(1630,280,80,10),6),new OcrWordBox("Sidebar context",new Rect(200,280,170,10),7)}).ToArray()};
            check(SensitiveDataDetector.Detect(separated,["credential"]).Count==2,"Split native context cropped away paragraph start or pulled in a sidebar");
            layout=layout with { Words=layout.Words.Append(new OcrWordBox("ordinary prose",new Rect(1180,292,150,7),3)).ToArray() };
            check(SensitiveDataDetector.Detect(layout,["credential"]).Count==0,"Skipped intervening prose in the same column");
        });
        test("Wrapped prefixed keys cover both physical rows without unrelated prose", () =>
        {
            var layout = new OcrLayout(700, 160, [new("Ключ доступа", new Rect(10, 10, 85, 10), 0), new("sk-", new Rect(630, 10, 20, 10), 0),
                new("test-a1b2c3d4e5f6a7b8c9d0e1f234567890.", new Rect(10, 26, 215, 10), 1), new("Последний зарегистрированный адрес", new Rect(235, 26, 300, 10), 1),
                new("sk-test-", new Rect(450, 65, 60, 10), 2), new("a1b2c3d4e5f6a7b8c9d0e1f234567890,", new Rect(10, 83, 190, 10), 3),
                new("сканирование завершено", new Rect(210, 83, 170, 10), 3)]);
            var found = SensitiveDataDetector.Detect(layout, ["credential"]);
            check(found.Count == 4 && found[0].Bounds.X == 627 && found[1].Bounds.X == 7 && found[1].Bounds.X + found[1].Bounds.Width <= 228
                && found[2].Bounds.X == 447 && found[3].Bounds.X == 7, "Wrapped key missing or following prose included");
            check(SensitiveDataDetector.Detect(layout, ["email"]).Count == 0, "Wrapped keys ignored category preferences");
            layout = layout with { Words = [layout.Words[1], new("unrelated-documentation-example", new Rect(10, 26, 200, 10), 1),
                new("a1b2c3d4e5f6a7b8c9d0e1f234567890", new Rect(10, 130, 200, 10), 2)] };
            check(SensitiveDataDetector.Detect(layout, ["credential"]).Count == 0, "Wrapped prefix attached to distant/plain continuation");
        });
        test("Wrapped keys stop at the nearest physical row and ordinary spacing", () =>
        {
            var layout = new OcrLayout(700, 90, [new("sk-test-", new Rect(630, 10, 60, 10), 0),
                new("a1b2c3d4e5f6a7b8c9d0e1f234567890", new Rect(10, 26, 200, 10), 1),
                new("ordinary", new Rect(215, 26, 60, 10), 1), new("Version20261003", new Rect(10, 44, 100, 10), 2)]);
            var found = SensitiveDataDetector.Detect(layout, ["credential"]);
            check(found.Count == 2 && found[1].Bounds == new Int32Rect(7, 23, 206, 16), "Wrapped key included later row or following prose");
            layout = layout with { Words = [layout.Words[0], new("ordinary prose", new Rect(10, 26, 100, 10), 1), layout.Words[3]] };
            check(SensitiveDataDetector.Detect(layout, ["credential"]).Count == 0, "Wrapped key skipped the nearest prose row");
        });
        test("Wrapped continuation joins adjacent fragments across native OCR lines", () =>
        {
            var layout = new OcrLayout(700, 90, [new("sk-test-", new Rect(630, 10, 60, 10), 0),
                new("a1b2c3d4e5f6", new Rect(10, 26, 100, 10), 1), new("a7b8c9d0", new Rect(113, 26, 80, 10), 2),
                new("ordinary", new Rect(198, 26, 60, 10), 3), new("Version20261003", new Rect(10, 44, 100, 10), 4)]);
            var found = SensitiveDataDetector.Detect(layout, ["credential"]);
            check(found.Count == 2 && found[1].Bounds == new Int32Rect(7, 23, 189, 16), "Wrapped continuation left a separate native-line suffix visible or included prose");
        });
        test("Wrapped complementary readings preserve terminal bounds without corrupting valid text", () =>
        {
            var layout = new OcrLayout(700, 90, [new("sk-test-", new Rect(630, 10, 60, 10), 0),
                new("a1b2c3d4e5f6.", new Rect(10, 26, 100, 10), 1), new("a1b2c3d4e5f6.", new Rect(10, 26, 120, 10), 2)]);
            var found = SensitiveDataDetector.Detect(layout, ["credential"]);
            check(found.Count == 2 && found[1].Bounds == new Int32Rect(7, 23, 126, 16), "Terminal punctuation discarded wider complementary bounds");
            layout = layout with { Words = [layout.Words[0], layout.Words[1] with { Text = "a1b2c3%4e5f6." }, layout.Words[2]] };
            check(SensitiveDataDetector.Detect(layout, ["credential"]).Count == 2, "Corrupt complementary word suppressed a valid continuation");
            layout = layout with { Words = [layout.Words[0], layout.Words[1] with { Text = "abcd12", Bounds = new Rect(10,26,60,10) },
                layout.Words[2] with { Text = "abcd12", Bounds = new Rect(10,26,62,10) }] };
            check(SensitiveDataDetector.Detect(layout, ["credential"]).Count == 0, "Duplicate short words manufactured a valid key");
        });
        test("Phone beside an IP keeps its own complete cover", () =>
        {
            var layout = new OcrLayout(400, 90, [new("555-0147", new Rect(10, 10, 70, 10), 0), new("192.0.2.1", new Rect(95, 10, 80, 10), 0),
                new("192.0.2.1", new Rect(10, 45, 80, 10), 1), new("+1 202-555-0183", new Rect(110, 45, 120, 10), 1)]);
            var found = SensitiveDataDetector.Detect(layout, ["phone"]);
            check(found.Count == 2 && found[0].Bounds == new Int32Rect(7, 7, 76, 16) && found[1].Bounds == new Int32Rect(107, 42, 126, 16), "IPv4 swallowed a real neighbouring phone or became a phone itself");
        });
        test("Isolated OCR symbol and one digit do not make prose a password", () =>
        {
            var layout = new OcrLayout(400, 120, [new("paCwu0$poBaTb", new Rect(10, 10, 120, 12), 0),
                new("Quantum#Shield2026", new Rect(10, 50, 160, 12), 1), new("Password: Tiny!9", new Rect(10, 90, 160, 12), 2)]);
            var found = SensitiveDataDetector.Detect(layout, ["credential"]);
            check(found.Count == 2 && found.All(f => f.Bounds.Y >= 47), "Distorted prose became an unlabelled password, or labelled small password was lost");
        });
        test("Phone matching excludes numeric OCR tails and embedded IPv4", () =>
        {
            var layout = new OcrLayout(500, 160, [new("процесс•0 7777070 продолжается", new Rect(10, 10, 250, 10), 0),
                new("0 198.51.100.91.", new Rect(10, 50, 160, 10), 1), new("192. 0. 2.184", new Rect(10, 90, 160, 10), 2),
                new("+1 202-555-0147", new Rect(10, 130, 160, 10), 3)]);
            var found = SensitiveDataDetector.Detect(layout, ["phone"]);
            check(found.Count == 1 && found[0].Bounds.Y == 127, "OCR prose/IP became a phone, or actual formatted phone was lost");
        });
        test("Padding overlap does not combine neighbouring lines into one large cover", () =>
        {
            var layout = new OcrLayout(400, 80, [new("192.0.2.184", new Rect(160, 10, 80, 8), 0),
                new("sk-test-a1b2c3d4e5f6a7b8c9d0e1f234567890", new Rect(10, 24, 260, 8), 1)]);
            var found = SensitiveDataDetector.Detect(layout, ["ip", "credential"]);
            check(found.Count == 2 && found[0].Bounds.Y == 7 && found[0].Bounds.Height == 14 && found[1].Bounds.Y == 21 && found[1].Bounds.Height == 14,
                "Touching padded covers hid ordinary text between different rows");
        });
        test("Dotted password shapes retain full bounds without classifying ordinary mail or versions as passwords", () =>
        {
            var layout = new OcrLayout(600, 180, [new("Test@Vault.5931", new Rect(20,20,150,12),0),
                new("Secret#part.42",new Rect(20,50,150,12),1),new("user42@example.com",new Rect(20,80,170,12),2),
                new("Version2026.10.03",new Rect(20,110,160,12),3),new("https://example.test/a#part.42",new Rect(20,140,250,12),4)]);
            layout = layout with { Words = layout.Words.Concat(new[] {
                new OcrWordBox("user42@example.com!",new Rect(220,20,170,12),5),
                new OcrWordBox("user42@example.com?",new Rect(220,50,170,12),6),
                new OcrWordBox("user42@xn--e1afmkfd.xn--p1ai",new Rect(220,80,250,12),7) }).ToArray() };
            var found = SensitiveDataDetector.Detect(layout,["credential"]);
            check(found.Count == 2 && found.All(f => f.Bounds.Y < 70 && f.Bounds.X <=20 && f.Bounds.X+f.Bounds.Width>=170), "Dotted secret was missed or ordinary mail/version/URL became a password");
        });
        test("Full monitor small-text rows retain both columns without scanning empty margins", () =>
        {
            var words = Enumerable.Range(0, 70).SelectMany(i => new[] {
                new OcrWordBox("Navigation item", new Rect(30, 20 + i * 28, 180, 8), i * 2),
                new OcrWordBox("user@example.test", new Rect(1700, 20 + i * 28, 650, 8), i * 2 + 1) }).ToArray();
            var regions = OcrRowRegions.Find(new(3840, 2160, words), containsDetail: r => words.Any(w => new Rect(r.X,r.Y,r.Width,r.Height).IntersectsWith(w.Bounds)));
            check(regions.Count > 0 && words.All(w => regions.Any(r => new Rect(r.X,r.Y,r.Width,r.Height).Contains(w.Bounds))), "Full-screen rows/columns lost source coverage");
            check(regions.All(r => r.Width < 1000) && regions.Sum(r => (long)r.Width * r.Height) < 4_000_000, "Empty monitor margins consumed the small-text OCR budget");
        });
        test("Full monitor row recovery retains a trailing value omitted by primary OCR", () =>
        {
            var label = new Rect(1700,100,70,8); var omitted = new Rect(1785,100,200,8);
            var regions = OcrRowRegions.Find(new(2560,1440,[new("Password:",label,0)]), containsDetail: r =>
                new Rect(r.X,r.Y,r.Width,r.Height).IntersectsWith(label) || new Rect(r.X,r.Y,r.Width,r.Height).IntersectsWith(omitted));
            check(regions.Any(r=>new Rect(r.X,r.Y,r.Width,r.Height).Contains(omitted)), "Recognized label bounds clipped the omitted trailing password");
        });
        test("Small-image row crops preserve context and row planning is bounded and cancellable", () =>
        {
            var regions = OcrRowRegions.Find(new(732, 200, [new("tiny text", new Rect(20,20,60,8),0)]));
            check(regions.SequenceEqual(new[]{new Int32Rect(0,10,732,40)}), "Existing narrow-page OCR context changed");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { OcrRowRegions.Find(new(732,200,[]), cancelled.Token); throw new Exception("Cancelled row planning continued"); } catch (OperationCanceledException) { }
            var dense = Enumerable.Range(0, 180).Select(i => new OcrWordBox("tiny text", new Rect(10,20+i*45,3800,8),i)).ToArray();
            try { OcrRowRegions.Find(new(3840,9000,dense)); throw new Exception("Unbounded row work accepted"); } catch (ArgumentException) { }
        });
        test("Missing inline text gaps are cropped without moving source coordinates", () =>
        {
            var layout = new OcrLayout(400, 80, [new("Пароль", new Rect(10, 20, 80, 10), 0), new(".", new Rect(350, 25, 3, 2), 0),
                new("Обычный", new Rect(10, 50, 60, 10), 1), new("текст", new Rect(76, 50, 40, 10), 1)]);
            var regions = OcrGapRegions.Find(layout);
            check(regions.Count == 1 && regions[0] == new Int32Rect(90, 16, 263, 18), "Missing text gap lost source bounds or normal spacing was rescanned");
        });
        test("Inline gap planning is bounded and cancellable", () =>
        {
            var words = Enumerable.Range(0, 70).SelectMany(i => new[] { new OcrWordBox("Поле", new Rect(10, i * 20, 30, 10), i), new OcrWordBox(".", new Rect(200, i * 20 + 4, 2, 2), i) }).ToArray();
            var layout = new OcrLayout(300, 1500, words);
            try { OcrGapRegions.Find(layout); throw new Exception("Unbounded gap work was accepted"); } catch (ArgumentException) { }
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { OcrGapRegions.Find(layout, cancelled.Token); throw new Exception("Cancelled gap planning continued"); } catch (OperationCanceledException) { }
        });
        test("Secret suggestions exclude ordinary URLs and password advice punctuation", () =>
        {
            var layout = new OcrLayout(900, 220, [new("https://example.test/docs?print=1", new Rect(10, 10, 280, 12), 0),
                new("Password policy?", new Rect(10, 45, 160, 12), 1), new("Passwords work!", new Rect(10, 80, 150, 12), 2),
                new("Password Policy!", new Rect(10, 115, 160, 12), 3), new("password Secure!", new Rect(10, 150, 150, 12), 4)]);
            check(SensitiveDataDetector.Detect(layout, ["credential"]).Count == 0, "Ordinary URL/advice or uncorroborated sentence punctuation became secrets");
            layout = layout with { Words = [new("https://example.test/?api_key=sk-test-fictional-key-1234567", new Rect(10, 10, 500, 12), 0)] };
            check(SensitiveDataDetector.Detect(layout, ["credential"]).Count == 1, "Known API key inside a URL must remain a suggestion");
        });
        test("Complementary narrative lookup also excludes ordinary URL values", () =>
        {
            var layout = new OcrLayout(600, 100, [new("Password", new Rect(10, 10, 65, 12), 0), new("https://example.test/docs?print=1", new Rect(90, 10, 280, 12), 1)]);
            check(SensitiveDataDetector.Detect(layout, ["credential"]).Count == 0, "Complementary prose lookup bypassed URL exclusion");
        });
        test("Complementary narrative label and wrapped value preserve both pieces", () =>
        {
            var layout = new OcrLayout(700, 130, [new("Для входа использовался пароль", new Rect(10, 10, 270, 12), 0), new("Secure!", new Rect(290, 10, 55, 12), 1),
                new("Test#4829", new Rect(10, 35, 70, 12), 2)]);
            var found = SensitiveDataDetector.Detect(layout, ["credential"]);
            check(found.Count == 2 && found[0].Bounds == new Int32Rect(287, 7, 61, 18) && found[1].Bounds == new Int32Rect(7, 32, 76, 18), "Complementary label or wrapped secret fragment missing");
        });
        test("IP punctuation and measured five substitutions keep complete address bounds", () =>
        {
            var layout = new OcrLayout(600, 130, [new("203.0.113.91.", new Rect(10, 10, 110, 12), 0), new("192.ø.2.S4,", new Rect(10, 50, 95, 12), 1),
                new("198.51.100.241.", new Rect(10, 90, 120, 12), 2), new("192.0.2.3.4 999.1.1.1", new Rect(180, 90, 280, 12), 2)]);
            var found = SensitiveDataDetector.Detect(layout, ["ip"]);
            check(found.Count == 3 && found[0].Bounds == new Int32Rect(7, 7, 116, 18) && found[1].Bounds == new Int32Rect(7, 47, 101, 18), "Sentence punctuation/five OCR lost addresses or matched invalid dotted numbers: " + string.Join(";", found.Select(f => f.Bounds)));
        });
        test("Email-shaped OCR asterisk substitutions remain suggestions", () =>
        {
            var layout = new OcrLayout(400, 100, [new("boris.demo*example.org", new Rect(20, 10, 160, 12), 0), new("a*b a*file.c", new Rect(20, 50, 160, 12), 1)]);
            check(SensitiveDataDetector.Detect(layout, ["email"]).Count == 1, "OCR asterisk lost email or flagged simple expressions");
        });
        test("Redaction group has one undo step", () =>
        {
            var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
            var doc = new ScreenshotEditDocument(image);
            doc.AddRange([new() { Kind = AnnotationKind.Redaction, Points = [new(0, 0), new(1, 1)] }, new() { Kind = AnnotationKind.Redaction, Points = [new(1, 1), new(2, 2)] }]);
            doc.Undo(); check(doc.Items.Count == 0, "Accepting a group required multiple undo actions");
            doc.Redo(); check(doc.Items.Count == 2, "Redo lost regions");
        });
        test("Redaction styles preserve bounds and source pixels", () =>
        {
            var pixels = new byte[24 * 24 * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = (byte)(i % 247); pixels[i + 1] = 150; pixels[i + 2] = 220; pixels[i + 3] = 255; }
            var image = BitmapSource.Create(24, 24, 96, 96, PixelFormats.Bgra32, null, pixels, 96);
            foreach (var style in Enum.GetValues<RedactionStyle>())
            {
                var result = RedactionRenderer.Apply(image, new RedactionRegion[] { new(new(3, 3, 15, 15), style) });
                var actual = new byte[pixels.Length]; result.CopyPixels(actual, 96, 0);
                check(actual.Take(96).SequenceEqual(pixels.Take(96)), "Style modified pixels outside its region");
                int offset = (5 * 24 + 5) * 4;
                check(style == RedactionStyle.Solid ? actual[offset + 2] == 0 && actual[offset + 3] == 255 : actual[offset + 2] == 220 && actual[offset] != pixels[offset], "Wrong redaction style pixels");
            }
            var original = new byte[pixels.Length]; image.CopyPixels(original, 96, 0); check(original.SequenceEqual(pixels), "Source was altered");
        });
        test("Cropped redaction export exactly matches reviewed preview", () =>
        {
            const int width = 40, height = 32;
            var pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = (byte)(i % 251); pixels[i + 1] = (byte)((i / 7) % 241); pixels[i + 2] = 200; pixels[i + 3] = 255; }
            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            foreach (var style in Enum.GetValues<RedactionStyle>())
            {
                var document = new ScreenshotEditDocument(source);
                document.Add(new() { Kind = AnnotationKind.Redaction, RedactionStyle = style, Points = [new(2, 2), new(35, 29)] });
                document.SetCrop(new(8, 7, 22, 18));
                var expected = new CroppedBitmap(document.RenderPreview(), document.Crop);
                var actual = document.Export();
                var previewPixels = new byte[22 * 18 * 4]; var exportPixels = new byte[previewPixels.Length];
                expected.CopyPixels(previewPixels, 22 * 4, 0); actual.CopyPixels(exportPixels, 22 * 4, 0);
                check(previewPixels.SequenceEqual(exportPixels), "Crop changed reviewed " + style + " pixels");
            }
        });
        test("Review rejects stale scan results and preserves unresolved findings", () =>
        {
            var state = new RedactionReviewState(true);
            check(!state.CanExport, "Automatic review exported before scanning");
            long first = state.BeginScan(); state.Invalidate();
            var one = new SensitiveFinding(Guid.NewGuid(), ["email"], new(1, 1, 20, 10), "••••");
            var two = one with { Id = Guid.NewGuid(), Bounds = new(40, 1, 20, 10) };
            check(!state.CompleteScan(first, [one]), "Stale scan accepted");
            long second = state.BeginScan(); check(state.CompleteScan(second, [one, two]), "Current scan rejected");
            state.Resolve([one.Id]); check(state.Findings.Count == 1 && !state.CanExport, "Accepting one discarded another");
            state.SkipRemaining(); check(state.CanExport, "Explicit dismissal did not unlock export");
            state.Invalidate(); check(!state.CanExport, "Edit did not invalidate scan");
            state.Close(); check(!state.CompleteScan(second, [one]) && !state.CanExport, "Closed review accepted work");
        });
        test("Ordinary editor can always export once no analysis is running or unresolved", () =>
        {
            var state = new RedactionReviewState(); check(state.CanExport, "Manual editor unexpectedly blocked");
            state.Invalidate(); check(state.CanExport, "Unscanned editing needs no privacy gate");
            long revision = state.BeginScan(); check(!state.CanExport, "Running analysis exported");
            state.FailScan(revision); check(state.CanExport && state.NeedsScan && !state.RequiresScanToExport, "A failed check blocked the ordinary editor");
            long second = state.BeginScan(); check(state.CompleteScan(second, []), "Clean scan rejected");
            state.Invalidate(); check(state.CanExport && state.NeedsScan && !state.RequiresScanToExport, "Editing after a check blocked Copy and Save");
            long third = state.BeginScan(); check(state.CompleteScan(third, [new SensitiveFinding(Guid.NewGuid(), ["email"], new(1, 1, 20, 10), "••••")]) && !state.CanExport, "Unresolved findings stopped gating output");
            state.SkipRemaining(); check(state.CanExport, "Keeping the remaining findings visible did not unlock export");
        });
        test("Automatic review still requires a check or an explicit skip after the image changes", () =>
        {
            var state = new RedactionReviewState(true);
            long revision = state.BeginScan(); check(state.CompleteScan(revision, []) && state.CanExport, "Clean automatic review blocked");
            state.Invalidate(); check(!state.CanExport && state.RequiresScanToExport, "Edit after the automatic review did not require another check");
            state.SkipRemaining(); check(state.CanExport && !state.RequiresScanToExport, "Explicit skip did not unlock the automatic review");
        });
        test("Auto redact copy is localized in all supported catalogs", () =>
        {
            try { foreach (var language in new[] { "ru", "de", "fr", "es" }) { L.Use(language); foreach (string label in new[] { "Find sensitive data", "Check screenshot", "Hide all found", "Keys, tokens and passwords" }) check(L.T(label) != label, "Privacy button untranslated: " + language); } }
            finally { L.Use("en"); }
        });
        test("Credential labels cover the secret rather than its auth scheme", () =>
        {
            var layout = new OcrLayout(400, 100, [new("Authorization:", new Rect(10, 10, 90, 20), 0), new("Bearer", new Rect(110, 10, 50, 20), 0), new("opaque-key-value", new Rect(180, 10, 100, 20), 0)]);
            var found = SensitiveDataDetector.Detect(layout, ["credential"]);
            check(found.Count == 1 && found[0].Bounds == new Int32Rect(177, 7, 106, 26), "The secret was left visible behind a covered Bearer label");
            layout = layout with { Words = [new("Password: \"p@$$word&!\"", new Rect(10, 10, 220, 20), 0)] };
            check(SensitiveDataDetector.Detect(layout, ["credential"]).Count == 1, "Quoted password punctuation prevented detection");
        });
    }
}
