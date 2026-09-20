using System.Windows;
using DesktopTools.Core;
using DesktopTools.UI;

internal static class DrawingEditTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Replace preserves stacking and supports undo redo with frozen points", () =>
        {
            var doc = new AnnotationDocument(); var a = new Annotation { Points = [new(10, 20), new(30, 40)] }; var b = new Annotation();
            doc.Add(a); doc.Add(b); var points = new List<Point> { new(20, 30), new(40, 50) };
            doc.Replace(a with { Points = points }); points.Clear();
            check(doc.Items[0].Id == a.Id && doc.Items[1].Id == b.Id && doc.Items[0].Points.Count == 2, "Replacement changed order or retained mutable points");
            doc.Undo(); check(doc.Items[0].Points[0] == new Point(10, 20), "Undo did not restore original");
            doc.Redo(); check(doc.Items[0].Points[0] == new Point(20, 30), "Redo did not restore transform");
        });
        test("Transform previews leave source unchanged and duplicates have new identity", () =>
        {
            var a = new Annotation { Kind = AnnotationKind.Pen, Points = [new(10, 20), new(20, 30), new(30, 40)] };
            var moved = AnnotationTransform.Move(a, new(5, -3));
            var resized = AnnotationTransform.Resize(a, new Rect(10, 20, 20, 20), new Rect(0, 0, 40, 60));
            var duplicate = AnnotationTransform.Duplicate(a);
            check(a.Points[0] == new Point(10, 20) && moved.Points[0] == new Point(15, 17) && moved.Id == a.Id, "Move mutated original");
            check(resized.Points[1] == new Point(20, 30) && resized.Points[^1] == new Point(40, 60), "Resize lost stroke shape");
            check(duplicate.Id != a.Id && duplicate.Points[0] == new Point(26, 36), "Duplicate identity or offset wrong");
        });
        test("Arrowhead size changes rendered geometry without moving endpoints", () =>
        {
            var a = new Annotation { Kind = AnnotationKind.Arrow, Points = [new(0, 0), new(100, 0)] };
            check(AnnotationRenderer.GeometryFor(a with { ArrowHeadSize = 2 }).Bounds.Height > AnnotationRenderer.GeometryFor(a).Bounds.Height, "Arrowhead multiplier ignored");
        });
        test("Resized text and numbered markers remain hittable at their transformed position", () =>
        {
            foreach (var kind in new[] { AnnotationKind.Text, AnnotationKind.Number })
            {
                var a = new Annotation { Kind = kind, Points = [new(100, 100)], Text = "Resize me", TextWidth = 120 };
                var bounds = AnnotationRenderer.Bounds(a);
                var resized = AnnotationTransform.Resize(a, bounds, new Rect(200, 200, bounds.Width * 2, bounds.Height * 2));
                var visible = AnnotationRenderer.Bounds(resized);
                check(visible.Width > bounds.Width && AnnotationRenderer.Hit(resized, new Point(visible.X + visible.Width / 2, visible.Y + visible.Height / 2)), "Resized label lost visual size or hit testing");
            }
        });
    }
}
