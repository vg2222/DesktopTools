using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using DesktopTools.Core;
using DesktopTools.Extras;

/// <summary>Choosing the right words out of several readings of the same picture (--ocr-consensus-only). Uses a small fixed word list, not Windows' dictionary.</summary>
internal static class OcrConsensusChecks
{
    private static readonly HashSet<string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        "claim", "offer", "remove", "item", "left", "right", "support", "settings", "sign", "in", "total", "reward", "unlocked", "get", "started", "battery", "volume", "visit", "our", "site", "today",
    };

    private static bool? Lexicon(OcrEnhancer.Script script, string word) => script == OcrEnhancer.Script.Latin ? Words.Contains(word) : null;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    /// <summary>A line of words laid out left to right, each 10 px per character wide.</summary>
    private static OcrEnhancer.Line Line(double y, double x, params string[] words)
    {
        var boxes = new List<OcrWordBox>(); double cursor = x;
        foreach (string word in words) { boxes.Add(new OcrWordBox(word, new Rect(cursor, y, word.Length * 10, 14), 0)); cursor += word.Length * 10 + 8; }
        var bounds = Rect.Empty; foreach (var b in boxes) bounds.Union(b.Bounds);
        return new OcrEnhancer.Line(string.Join(" ", words), bounds, boxes);
    }

    private static OcrConsensus.Reading Reading(string name, params OcrEnhancer.Line[] lines) => new(name, lines);
    private static string Text(IReadOnlyList<OcrEnhancer.Line> lines) => string.Join(" | ", lines.OrderBy(l => l.Bounds.Top).ThenBy(l => l.Bounds.Left).Select(l => l.Text));

    public static Task RunAsync()
    {
        // The dictionary decides between two readings of one phrase.
        var merged = OcrConsensus.Merge([Reading("a", Line(10, 10, "Claün", "offer")), Reading("b", Line(10, 10, "Claim", "offer"))], Lexicon);
        Check(Text(merged) == "Claim offer", "Dictionary words should win: " + Text(merged));

        // Agreement of independent readings outweighs a single reading, even when neither phrase is in the dictionary.
        merged = OcrConsensus.Merge([Reading("a", Line(10, 10, "Zorbix", "kit")), Reading("b", Line(10, 10, "Zorbtx", "kit")), Reading("c", Line(10, 10, "Zorbix", "kit"))], Lexicon);
        Check(Text(merged) == "Zorbix kit", "Majority should win when the dictionary cannot tell: " + Text(merged));

        // Words are chosen one place at a time: each reading is wrong somewhere else.
        merged = OcrConsensus.Merge([Reading("a", Line(10, 10, "Remouve", "item")), Reading("b", Line(10, 10, "Remove", "itern"))], Lexicon);
        Check(Text(merged) == "Remove item", "Per-word choice failed: " + Text(merged));

        // One reading splits a word that the other keeps whole: the whole word wins, the geometry stays in the chosen words.
        merged = OcrConsensus.Merge([Reading("a", Line(10, 10, "supp", "ort")), Reading("b", Line(10, 10, "support"))], Lexicon);
        Check(Text(merged) == "support" && merged[0].Words!.Count == 1, "A split word should lose to the whole word: " + Text(merged));

        // Different rows stay different rows, in reading order.
        merged = OcrConsensus.Merge([Reading("a", Line(10, 10, "Total", "item"), Line(40, 10, "Reward", "unlocked")), Reading("b", Line(41, 10, "Reward", "unlocked"), Line(11, 10, "Total", "item"))], Lexicon);
        Check(Text(merged) == "Total item | Reward unlocked", "Rows were mixed up: " + Text(merged));

        // One reading sees two text rows as a single tall line: the rows must not be mixed together word by word.
        var tall = new OcrEnhancer.Line("Total item Reward unlocked", new Rect(10, 10, 160, 44), [new("Total", new Rect(10, 10, 50, 14), 0), new("item", new Rect(68, 10, 40, 14), 0), new("Reward", new Rect(10, 40, 60, 14), 0), new("unlocked", new Rect(78, 40, 80, 14), 0)]);
        merged = OcrConsensus.Merge([Reading("a", Line(10, 10, "Total", "item"), Line(40, 10, "Reward", "unlocked")), Reading("b", tall)], Lexicon);
        Check(Text(merged) == "Total item | Reward unlocked", "Rows linked by a tall line were mixed: " + Text(merged));
        merged = OcrConsensus.Merge([Reading("a", Line(10, 10, "Zorbix", "kit"), Line(40, 10, "Claim", "offer")), Reading("b", new OcrEnhancer.Line("Zorbix kit Claim offer", new Rect(10, 10, 130, 44), [new("Zorbix", new Rect(10, 10, 60, 14), 0), new("kit", new Rect(78, 10, 30, 14), 0), new("Claim", new Rect(10, 40, 50, 14), 0), new("offer", new Rect(68, 40, 50, 14), 0)]))], Lexicon);
        Check(Text(merged) == "Zorbix kit | Claim offer", "A word from the row below replaced a word of the row above: " + Text(merged));

        // Two columns read as one line by one reading and as two by the other keep both columns.
        merged = OcrConsensus.Merge([Reading("a", Line(10, 10, "Left"), Line(10, 300, "Right")), Reading("b", new OcrEnhancer.Line("Left   Right", new Rect(10, 10, 340, 14), [new("Left", new Rect(10, 10, 40, 14), 0), new("Right", new Rect(300, 10, 50, 14), 0)]))], Lexicon);
        Check(Text(merged) == "Left    Right", "Two columns should stay on one row with a wide gap: '" + Text(merged) + "'");
        Check(merged.SelectMany(l => l.Words!).Select(w => w.Text).SequenceEqual(["Left", "Right"]), "Both column words must survive exactly once");

        // A single reading's symbol soup is dropped; a single reading's real text is kept.
        merged = OcrConsensus.Merge([Reading("a", Line(10, 10, "Battery", "87%"), Line(60, 10, "|||", "~~")), Reading("b", Line(10, 10, "Battery", "87%"))], Lexicon);
        Check(Text(merged) == "Battery 87%", "Noise from one reading should not appear: " + Text(merged));
        merged = OcrConsensus.Merge([Reading("a", Line(10, 10, "Cortana", "Volume"))], Lexicon);
        Check(Text(merged) == "Cortana Volume", "A lone reading must pass through unchanged: " + Text(merged));

        // Several readings of an empty picture give nothing.
        Check(OcrConsensus.Merge([Reading("a"), Reading("b")], Lexicon).Count == 0, "Empty readings must give an empty result");
        // Repair: an unknown word becomes the real word that differs only by letters OCR confuses; nothing else is touched.
        var suggestions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { ["Claün"] = ["Cluan", "Clan", "Claim"], ["Zebra"] = ["Zebu"], ["Cortana"] = ["Cortina", "Curtana"] };
        IReadOnlyList<string> Suggest(OcrEnhancer.Script script, string word) => suggestions.TryGetValue(word, out var list) ? list : [];
        string Repaired(string text) => Text(OcrConsensus.Repair([Line(10, 10, text.Split(' '))], Lexicon, Suggest));
        Check(Repaired("Claün offer") == "Claim offer", "Windows' suggestion with an OCR-like difference should repair Claün: " + Repaired("Claün offer"));
        Check(Repaired("Clairn offer") == "Claim offer", "rn read for m should be repaired: " + Repaired("Clairn offer"));
        Check(Repaired("CLAÜN OFFER") == "CLAIM OFFER", "Repair should keep capitals: " + Repaired("CLAÜN OFFER"));
        Check(Repaired("Claim offer") == "Claim offer", "Correct words must not change");
        Check(Repaired("Zebra Cortana") == "Zebra Cortana", "Unknown words without an OCR-like neighbour must stay: " + Repaired("Zebra Cortana"));
        Check(Repaired("Sl,249.99 87%") == "Sl,249.99 87%", "Numbers are not words: " + Repaired("Sl,249.99 87%"));
        Check(Repaired("Left Rigth") == "Left Rigth", "A transposition is not an OCR confusion; leave it: " + Repaired("Left Rigth"));
        Console.WriteLine("PASS OCR consensus: dictionary choice, majority, per-word choice, split words, rows, columns, noise, lone reading, empty, repair of confusable letters");
        return Task.CompletedTask;
    }
}
