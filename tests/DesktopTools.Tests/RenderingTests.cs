using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Core;
using DesktopTools.Extras;
using DesktopTools.UI;

internal static class RenderingTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Annotation move erase and clear preserve independent undo steps", () =>
        {
            var document = new ScreenshotEditDocument(White(100, 100));
            var line = new Annotation { Kind = AnnotationKind.Line, Points = [new(10, 10), new(30, 10)], Color = Colors.Red, Thickness = 4 };
            document.Add(line); document.Replace(AnnotationTransform.Move(line, new Vector(0, 30)));
            check(Pixel(document.Export(), 20, 40).R == 255 && Pixel(document.Export(), 20, 40).G == 0, "Moved line missing");
            check(Pixel(document.Export(), 20, 10) == Colors.White, "Move left the original line behind");
            document.Remove(new[] { line.Id }); check(document.Items.Count == 0, "Erase retained annotation");
            document.Undo(); check(document.Items.Count == 1 && document.Items[0].Points[0].Y == 40, "Erase undo lost moved position");
            document.Undo(); check(document.Items[0].Points[0].Y == 10, "Move undo did not restore original position");
            document.SetCrop(new Int32Rect(5, 5, 80, 80)); document.Clear();
            check(document.Items.Count == 0 && document.Crop.Width == 80, "Clear changed crop or retained ink");
            document.Undo(); check(document.Items.Count == 1 && document.Crop.Width == 80, "Clear undo lost annotations or crop");
        });
        test("Virtual desktop composition maps negative monitor origin and scaled ink once", () =>
        {
            // Virtual origin (-200,-100), annotation monitor (-100,-60): offset (100,40).
            var ink = new Annotation { Kind = AnnotationKind.Line, Points = [new(10, 10), new(30, 10)], Color = Colors.Red, Thickness = 4, Opacity = .5 };
            var source = White(400, 200);
            var result = CaptureComposition.Composite(source, [ink], 2, 2, new Vector(100, 40), new Int32Rect(110, 50, 80, 50));
            var pixel = Pixel(result, 20, 10);
            check(pixel.R == 255 && pixel.G is >= 126 and <= 129 && pixel.B is >= 126 and <= 129, "Scaled virtual ink misplaced or drawn twice");
            check(Pixel(result, 65, 10) == Colors.White && Pixel(source, 140, 60) == Colors.White, "Crop/source changed outside ink");
            var bare = CaptureComposition.Composite(source, [], 2, 2, new(100, 40), new(110, 50, 80, 50));
            check(Pixel(bare, 20, 10) == Colors.White, "Excluded annotations appeared");
        });
        test("Recent captures retain newest eight and clear releases entries", () =>
        {
            var history = new CaptureHistory();
            for (int i = 1; i <= 10; i++) history.Add(White(i, 2));
            check(history.Entries.Count == 8 && history.Entries[0].Image.PixelWidth == 10 && history.Entries[^1].Image.PixelWidth == 3, "History count/order wrong");
            check(history.Entries.Select(e => e.Id).Distinct().Count() == 8, "Entry IDs are not unique");
            history.Clear(); check(history.Entries.Count == 0, "Clear retained images");
        });
        test("Recent capture byte limit evicts old images and rejects oversized image", () =>
        {
            var history = new CaptureHistory(maxBytes: 80);
            history.Add(White(4, 4)); history.Add(White(2, 2)); history.Add(White(3, 3));
            check(history.Entries.Count == 2 && history.Entries[0].Image.PixelWidth == 3, "Memory budget eviction wrong");
            history.Add(White(10, 10)); check(history.Entries.Count == 2, "Oversized image destroyed or exceeded bounded history");
        });
        test("Shift constraints snap diagonal lines and square reversed shapes", () =>
        {
            var line = DrawingGeometry.Constrain(new(10, 10), new(40, 20), AnnotationKind.Line);
            check(Math.Abs(line.Y - 10) < .001 && line.X > 40, "Line did not snap to nearest axis");
            var square = DrawingGeometry.Constrain(new(50, 50), new(20, 40), AnnotationKind.Rectangle);
            check(square == new Point(20, 20), "Reverse-drag square constraint wrong");
        });
        test("Filled shapes erase from interior and number markers render opaque", () =>
        {
            var shape = new Annotation { Kind = AnnotationKind.Rectangle, Points = [new(10, 10), new(60, 60)], Filled = true, Color = Colors.Blue };
            check(AnnotationRenderer.Hit(shape, new(30, 30)), "Filled interior cannot be erased");
            var filled = AnnotationRenderer.Composite(White(80, 80), [shape], 1, 1, new(0, 0, 80, 80));
            check(Pixel(filled, 30, 30) == Colors.Blue, "Filled shape interior did not render");
            var marker = new Annotation { Kind = AnnotationKind.Number, Number = 12, Points = [new(40, 40)], FontSize = 24, Color = Colors.Red, Opacity = .1 };
            var image = AnnotationRenderer.Composite(White(80, 80), [marker], 1, 1, new(0, 0, 80, 80));
            check(Pixel(image, 40, 26).R == 255 && Pixel(image, 40, 26).G < 30, "Marker circle is not opaque");
            check(AnnotationRenderer.Hit(marker, new(40, 40)) && !AnnotationRenderer.Hit(marker, new(70, 70)), "Marker eraser bounds wrong");
        });
        test("Compact text editor clamps at screen edge and exports text inset", () =>
        {
            var editor = new AnnotationTextEditor(new Point(990, 590), new Size(1000, 600), Colors.White, 24);
            editor.Measure(new Size(1000, 600)); editor.Arrange(new Rect(0, 0, editor.DesiredSize.Width, editor.DesiredSize.Height)); editor.ApplyTemplate();
            check(editor.Width == 400 && editor.ExportWidth == 374, "Editor width or text inset differs from export");
            check(editor.ExportOrigin.X == 613 && editor.ExportOrigin.Y < 590, "Editor did not clamp within screen edge");
            check(editor.FontFamily.Source == AnnotationTypography.DefaultFamily && ((SolidColorBrush)editor.Foreground).Color == Colors.White, "Preview font/color differs from committed text");
        });
        test("Text annotations retain selected font through history and renderer layout", () =>
        {
            var mono = new Annotation { Kind = AnnotationKind.Text, Points = [new(10, 10)], Text = "iiiiiiii", FontSize = 24, FontFamily = "Consolas", Color = Colors.Black };
            var doc = new AnnotationDocument(); doc.Add(mono); doc.Undo(); doc.Redo();
            check(doc.Items[0].FontFamily == "Consolas", "Font lost in history");
            check(AnnotationRenderer.Hit(mono, new(95, 20)), "Renderer ignored wider selected monospace font");
            check(!AnnotationRenderer.Hit(mono with { FontFamily = "Segoe UI" }, new(95, 20)), "Font selection does not affect text bounds");
        });
        test("Editor undo redo restores crop and annotation branch", () =>
        {
            var doc = new ScreenshotEditDocument(White(100, 80));
            doc.Add(new Annotation { Points = [new(20, 20), new(40, 20)] });
            doc.SetCrop(new Int32Rect(10, 10, 60, 40));
            check(doc.Export().PixelWidth == 60, "Crop not applied");
            doc.Undo(); check(doc.Crop.Width == 100 && doc.Items.Count == 1, "Undo crop lost ink");
            doc.Undo(); check(doc.Items.Count == 0, "Undo ink failed");
            doc.Redo(); doc.SetCrop(new Int32Rect(0, 0, 20, 20));
            check(!doc.CanRedo && doc.Items.Count == 1, "New crop did not discard redo branch");
        });
        test("Editor crop keeps source pixel alignment and redaction opaque above ink", () =>
        {
            var source = White(100, 80); var doc = new ScreenshotEditDocument(source);
            doc.Add(new Annotation { Kind = AnnotationKind.Redaction, Points = [new(20, 20), new(40, 40)] });
            doc.Add(new Annotation { Kind = AnnotationKind.Line, Points = [new(15, 30), new(50, 30)], Thickness = 8, Color = Colors.Red });
            doc.SetCrop(new Int32Rect(10, 10, 60, 50)); var result = doc.Export();
            check(Pixel(result, 15, 20) == Colors.Black && Pixel(result, 35, 20).R == 255, "Redaction or crop alignment wrong");
            check(Pixel(source, 25, 30) == Colors.White, "Editor modified original source");
        });
        test("Editor clips partially cropped redactions and bounds history", () =>
        {
            var doc = new ScreenshotEditDocument(White(100, 80));
            doc.Add(new Annotation { Kind = AnnotationKind.Redaction, Points = [new(0, 0), new(30, 30)] });
            doc.SetCrop(new Int32Rect(20, 20, 40, 40));
            var image = doc.Export();
            check(Pixel(image, 0, 0) == Colors.Black && Pixel(image, 10, 10) == Colors.White, "Cover crossing crop edge clipped incorrectly");
            for (int i = 0; i < 205; i++) doc.Add(new Annotation { Points = [new(50, 50)] });
            int undos = 0; while (doc.CanUndo) { doc.Undo(); undos++; }
            check(undos == 200 && doc.Items.Count == 6, "Editor history exceeds bound or discards current content");
        });
        test("200% crop aligns translucent annotation and composites it exactly once", () =>
        {
            var source = White(120, 100);
            var line = new Annotation { Kind = AnnotationKind.Line, Points = [new(10, 15), new(30, 15)], Thickness = 4, Opacity = 0.5, Color = Colors.Red };
            var image = AnnotationRenderer.Composite(source, [line], 2, 2, new Int32Rect(10, 20, 60, 40));
            var interior = Pixel(image, 20, 10);
            check(interior.R == 255 && interior.G is >= 126 and <= 129 && interior.B is >= 126 and <= 129, $"Expected one 50% red pass; got {interior}");
            check(Pixel(image, 20, 1) == Colors.White && Pixel(image, 55, 10) == Colors.White, "Ink is outside hand-calculated scaled bounds");
            check(image.PixelWidth == 60 && image.PixelHeight == 40 && image.IsFrozen, "Crop dimensions/frozen output changed");
            check(Pixel(source, 40, 30) == Colors.White, "Compositing modified source");
        });
        test("Rectangle and ellipse export outlines with transparent interiors", () =>
        {
            foreach (var kind in new[] { AnnotationKind.Rectangle, AnnotationKind.Ellipse })
            {
                var shape = new Annotation { Kind = kind, Points = [new(10, 10), new(90, 70)], Thickness = 4, Color = Colors.Blue };
                var result = AnnotationRenderer.Composite(White(100, 80), [shape], 1, 1, new Int32Rect(0, 0, 100, 80));
                check(Pixel(result, 50, 10).B == 255 && Pixel(result, 50, 10).R < 30, kind + " top outline missing");
                check(Pixel(result, 50, 40) == Colors.White, kind + " interior unexpectedly filled");
                check(AnnotationRenderer.Hit(shape, new(50, 10)) && !AnnotationRenderer.Hit(shape, new(50, 40)), kind + " eraser did not follow visible outline");
            }
        });
        test("Arrowhead renders beyond shaft and can be erased", () =>
        {
            var arrow = new Annotation { Kind = AnnotationKind.Arrow, Points = [new(20, 40), new(80, 40)], Thickness = 6, Color = Colors.Blue };
            var result = AnnotationRenderer.Composite(White(100, 80), [arrow], 1, 1, new Int32Rect(0, 0, 100, 80));
            check(Pixel(result, 58, 30).R < 50, "Arrowhead missing independently of shaft");
            check(AnnotationRenderer.Hit(arrow, new(57, 30)) && !AnnotationRenderer.Hit(arrow, new(10, 10)), "Arrowhead eraser hit region wrong");
        });
        test("Text is rendered near anchor and erased within its text bounds", () =>
        {
            var text = new Annotation { Kind = AnnotationKind.Text, Text = "HELLO", Points = [new(10, 10)], FontSize = 24, Color = Colors.Black };
            var result = AnnotationRenderer.Composite(White(140, 80), [text], 1, 1, new Int32Rect(0, 0, 140, 80));
            int dark = 0;
            for (int y = 10; y < 45; y++) for (int x = 10; x < 100; x++) if (Pixel(result, x, y).R < 100) dark++;
            check(dark > 30 && Pixel(result, 5, 5) == Colors.White, "Text is missing or misplaced");
            check(AnnotationRenderer.Hit(text, new(15, 20)) && !AnnotationRenderer.Hit(text, new(130, 60)), "Text erase bounds wrong");
        });
        test("Wrapped text exports within editor width and second line can be erased", () =>
        {
            var text = new Annotation { Kind = AnnotationKind.Text, Text = "HELLO HELLO HELLO", Points = [new(10, 10)], FontSize = 24, Color = Colors.Black, TextWidth = 70 };
            var result = AnnotationRenderer.Composite(White(160, 160), [text], 1, 1, new Int32Rect(0, 0, 160, 160));
            int secondLinePixels = 0;
            for (int y = 42; y < 100; y++) for (int x = 10; x < 80; x++) if (Pixel(result, x, y).R < 100) secondLinePixels++;
            check(secondLinePixels > 20, "Wrapped text has no second line");
            for (int y = 0; y < 160; y++) for (int x = 82; x < 160; x++) check(Pixel(result, x, y) == Colors.White, "Text overflowed editor width");
            check(AnnotationRenderer.Hit(text, new(20, 50)), "Wrapped second line cannot be erased");
            check(!AnnotationRenderer.Hit(text, new(100, 50)), "Wrapped text hit region overflows editor width");
        });
        test("Single-point ink dots render and can be erased", () =>
        {
            foreach (var kind in new[] { AnnotationKind.Pen, AnnotationKind.Highlighter })
            {
                var dot = new Annotation { Kind = kind, Points = [new(20, 20)], Thickness = 8, Color = Colors.Blue };
                var result = AnnotationRenderer.Composite(White(40, 40), [dot], 1, 1, new Int32Rect(0, 0, 40, 40));
                check(Pixel(result, 20, 20).R == 0, "Ink dot not rendered");
                check(AnnotationRenderer.Hit(dot, new(20, 20)), "Visible single-point ink cannot be erased");
                check(!AnnotationRenderer.Hit(dot, new(35, 35)), "Dot erase radius is too broad");
            }
        });
        test("Invalid crop is rejected instead of producing misleading output", () =>
        {
            var rejected = false;
            try { AnnotationRenderer.Composite(White(20, 20), [], 1, 1, new Int32Rect(15, 15, 10, 10)); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected, "Out-of-image crop accepted");
        });
        test("Pixel redaction covers exact opaque bounds without changing source", () =>
        {
            var source = White(4, 4, 144);
            var result = RedactionRenderer.Apply(source, [new Int32Rect(1, 1, 2, 2)]);
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
            {
                bool inside = x is 1 or 2 && y is 1 or 2;
                check(Pixel(result, x, y) == (inside ? Colors.Black : Colors.White), $"Unexpected pixel at {x},{y}");
                check(Pixel(source, x, y) == Colors.White, "Original image changed");
            }
            check(result.IsFrozen && result.PixelWidth == 4 && result.DpiX == 144, "Export dimensions/DPI/freeze changed");
        });
        test("Pixel redaction clamps covers to image edge", () =>
        {
            var image = RedactionRenderer.Apply(White(4, 4), [new Int32Rect(3, 3, 20, 20)]);
            check(Pixel(image, 3, 3) == Colors.Black && Pixel(image, 2, 3) == Colors.White, "Clipped redaction changed wrong pixels");
        });
        test("Empty and off-image pixel covers preserve image", () =>
        {
            var image = RedactionRenderer.Apply(White(4, 4), [Int32Rect.Empty, new Int32Rect(20, 20, 2, 2)]);
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) check(Pixel(image, x, y) == Colors.White, "Off-image cover changed a pixel");
        });
    }

    private static BitmapSource White(int width, int height, double dpi = 96)
    {
        var bytes = Enumerable.Repeat((byte)255, width * height * 4).ToArray();
        var source = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgra32, null, bytes, width * 4);
        source.Freeze(); return source;
    }
    private static Color Pixel(BitmapSource image, int x, int y)
    {
        var pixel = new byte[4]; image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
}
