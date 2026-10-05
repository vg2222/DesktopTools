using DesktopTools.Core;
using System.Windows;

internal static class TextSelectionTests
{
    private static OcrWordBox W(string text, double x, double y, double w = 40, double h = 12, int line = 0) => new(text, new Rect(x, y, w, h), line);

    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Selection model orders words by line then position", () =>
        {
            var model = new TextSelectionModel(new OcrLayout(400, 100, [W("b", 60, 0, line: 0), W("c", 0, 30, line: 1), W("a", 0, 0, line: 0)]));
            check(string.Join(",", model.Words.Select(w => w.Text)) == "a,b,c", "Reading order wrong");
            check(model.GetText(false) == "a b\nc", "Text assembly wrong: " + model.GetText(false));
        });
        test("Nearest word resolves gaps, margins and far-away points", () =>
        {
            var model = new TextSelectionModel(new OcrLayout(400, 100, [W("one", 10, 10), W("two", 200, 10), W("three", 10, 50, line: 1)]));
            check(model.WordAt(new Point(20, 15)) == 0, "Exact hit missed");
            check(model.NearestWord(new Point(120, 14)) == 0 && model.NearestWord(new Point(150, 14)) == 1, "Gap between words resolved to the wrong word");
            check(model.NearestWord(new Point(-50, 16)) == 0 && model.NearestWord(new Point(390, 16)) == 1, "Margins did not clamp to line ends");
            check(model.NearestWord(new Point(100, 45)) == 2, "Point between lines should pick the closer line");
            check(model.NearestWord(new Point(5000, 5000)) == 2, "Far point should pick the last word");
            check(new TextSelectionModel(new OcrLayout(10, 10, [])).NearestWord(new Point(1, 1)) == null, "Empty model returned a word");
        });
        test("Drag selection works in both directions and across lines", () =>
        {
            var model = new TextSelectionModel(new OcrLayout(400, 100, [W("a", 0, 0), W("b", 50, 0), W("c", 0, 30, line: 1), W("d", 50, 30, line: 1)]));
            model.Begin(2); model.Extend(1);
            check(model.Range == (1, 2) && model.IsSelected(1) && model.IsSelected(2) && !model.IsSelected(0), "Backward drag range wrong");
            check(model.GetText(true) == "b\nc", "Cross-line selection text wrong: " + model.GetText(true));
            model.SelectLine(3); check(model.Range == (2, 3) && model.GetText(true) == "c d", "SelectLine wrong");
            model.SelectWord(0); check(model.Range == (0, 0) && model.GetText(true) == "a", "SelectWord wrong");
            model.SelectAll(); check(model.Range == (0, 3), "SelectAll wrong");
            model.Clear(); check(!model.HasSelection && model.GetText(true) == "", "Clear left a selection");
        });
        test("Edited words replace OCR text in copies but not in geometry", () =>
        {
            var model = new TextSelectionModel(new OcrLayout(400, 100, [W("He11o", 0, 0), W("world", 50, 0)]));
            model.SetOverride(0, "Hello");
            check(model.GetText(false) == "Hello world" && model.IsEdited(0) && model.Words[0].Text == "He11o", "Override not applied only to output");
            model.SetOverride(0, "He11o"); check(!model.IsEdited(0), "Restoring the OCR text should clear the edit");
        });
        test("Links are found with trailing punctuation removed", () =>
        {
            var model = new TextSelectionModel(new OcrLayout(900, 100, [W("See", 0, 0), W("https://example.com/a?b=1.", 50, 0, 200), W("or", 260, 0), W("www.site.org,", 300, 0, 100), W("mail", 0, 30, line: 1), W("me@test.dev", 50, 30, 80, line: 1), W("call", 150, 30, line: 1), W("+1", 200, 30, 20, line: 1), W("415", 230, 30, line: 1), W("555", 280, 30, line: 1), W("0199", 330, 30, line: 1)]));
            var links = TextLinkDetector.Find(model);
            check(links.Count == 4, "Expected 4 links, got " + links.Count);
            check(links[0].Kind == TextLinkKind.Url && links[0].Target == "https://example.com/a?b=1" && links[0].FirstWord == 1 && links[0].LastWord == 1, "URL wrong");
            check(links[1].Kind == TextLinkKind.Url && links[1].Target == "https://www.site.org", "www link wrong: " + links[1].Target);
            check(links[2].Kind == TextLinkKind.Email && links[2].Target == "mailto:me@test.dev", "E-mail wrong");
            check(links[3].Kind == TextLinkKind.Phone && links[3].Target == "tel:+14155550199" && links[3].FirstWord == links[3].LastWord - 3, "Phone spanning four words wrong");
        });
        test("Ordinary numbers, dates and prices are not links", () =>
        {
            var model = new TextSelectionModel(new OcrLayout(900, 100, [W("Total:", 0, 0), W("1,234.56", 50, 0), W("USD", 120, 0), W("on", 160, 0), W("2026-10-05", 190, 0, 80), W("v1.2.7", 300, 0), W("12345", 360, 0)]));
            check(TextLinkDetector.Find(model).Count == 0, "False positive link");
        });
    }
}
