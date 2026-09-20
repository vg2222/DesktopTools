using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Globalization;
using DesktopTools.Core;

namespace DesktopTools.UI;

public static class AnnotationRenderer
{
    public static Geometry GeometryFor(Annotation item)
    {
        if (item.Points.Count == 0) return Geometry.Empty;
        var first = item.Points[0]; var last = item.Points[^1];
        if (item.Kind is AnnotationKind.Rectangle or AnnotationKind.Redaction) return new RectangleGeometry(new Rect(first, last));
        if (item.Kind == AnnotationKind.Ellipse) return new EllipseGeometry(new Rect(first, last));
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(first, false, false);
            if (item.Kind is AnnotationKind.Pen or AnnotationKind.Highlighter) context.PolyLineTo(item.Points.Skip(1).ToArray(), true, true);
            else
            {
                context.LineTo(last, true, false);
                if (item.Kind == AnnotationKind.Arrow)
                {
                    var vector = first - last;
                    if (vector.Length > 0)
                    {
                        vector.Normalize(); var length = Math.Max(12, item.Thickness * 4) * (double.IsFinite(item.ArrowHeadSize) ? Math.Clamp(item.ArrowHeadSize, .5, 3) : 1);
                        var side = new Vector(-vector.Y, vector.X) * length * .45;
                        context.BeginFigure(last + vector * length + side, false, false); context.LineTo(last, true, false); context.LineTo(last + vector * length - side, true, false);
                    }
                }
            }
        }
        geometry.Freeze(); return geometry;
    }
    private static FormattedText Format(Annotation item)
    {
        var text = new FormattedText(item.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(AnnotationTypography.Resolve(string.IsNullOrWhiteSpace(item.FontFamily) ? AnnotationTypography.DefaultFamily : item.FontFamily), item.Italic ? FontStyles.Italic : FontStyles.Normal, item.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal), item.FontSize, new SolidColorBrush(item.Color), 1);
        if (double.IsFinite(item.TextWidth) && item.TextWidth > 0) text.MaxTextWidth = item.TextWidth;
        return text;
    }
    public static Rect Bounds(Annotation item)
    {
        if (item.Points.Count == 0) return Rect.Empty;
        var first = item.Points[0];
        if (item.Kind == AnnotationKind.Text) { var text = Format(item); return new Rect(first, new Size(Math.Max(1, text.Width), Math.Max(1, text.Height))); }
        if (item.Kind == AnnotationKind.Number) { var radius = Math.Max(14, item.FontSize * .8); return new Rect(first - new Vector(radius, radius), new Size(radius * 2, radius * 2)); }
        var bounds = GeometryFor(item).Bounds;
        if (bounds.IsEmpty) bounds = new Rect(first, first);
        bounds.Inflate(Math.Max(2, item.Thickness / 2), Math.Max(2, item.Thickness / 2));
        return bounds;
    }
    public static bool Hit(Annotation item, Point point)
    {
        if (item.Points.Count == 0) return false;
        if (item.Kind == AnnotationKind.Number) return (point - item.Points[0]).Length <= Math.Max(14, item.FontSize * .8);
        if (item.Kind == AnnotationKind.Text) { var text = Format(item); return new Rect(item.Points[0], new Size(text.Width, text.Height)).Contains(point); }
        if (item.Points.Count == 1 && item.Kind is AnnotationKind.Pen or AnnotationKind.Highlighter)
            return (point - item.Points[0]).Length <= Math.Max(item.Thickness, 12) / 2;
        var geometry = GeometryFor(item);
        return geometry.StrokeContains(new Pen(Brushes.Black, Math.Max(item.Thickness, 12)), point) || ((item.Kind == AnnotationKind.Redaction || (item.Filled && item.Kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse)) && geometry.FillContains(point));
    }
    public static void Draw(DrawingContext dc, IEnumerable<Annotation> annotations)
    {
        foreach (var item in annotations)
        {
            if (item.Points.Count == 0) continue;
            var brush = new SolidColorBrush(item.Color);
            dc.PushOpacity(item.Kind is AnnotationKind.Redaction or AnnotationKind.Number ? 1 : item.Opacity);
            if (item.Kind == AnnotationKind.Number)
            {
                double radius = Math.Max(14, item.FontSize * .8);
                var opaque = Color.FromRgb(item.Color.R, item.Color.G, item.Color.B);
                dc.DrawEllipse(new SolidColorBrush(opaque), null, item.Points[0], radius, radius);
                var contrast = opaque.R * .2126 + opaque.G * .7152 + opaque.B * .0722 > 155 ? Colors.Black : Colors.White;
                var label = Format(item with { Text = item.Number.ToString(CultureInfo.InvariantCulture), FontSize = item.FontSize * .85, Color = contrast, TextWidth = 0 });
                if (label.Width > radius * 1.65) label.SetFontSize(item.FontSize * .85 * radius * 1.65 / label.Width);
                dc.DrawText(label, item.Points[0] - new Vector(label.Width / 2, label.Height / 2));
            }
            else if (item.Kind == AnnotationKind.Text) dc.DrawText(Format(item), item.Points[0]);
            else if (item.Points.Count == 1 && item.Kind is AnnotationKind.Pen or AnnotationKind.Highlighter) dc.DrawEllipse(brush, null, item.Points[0], item.Thickness / 2, item.Thickness / 2);
            else dc.DrawGeometry(item.Kind == AnnotationKind.Redaction ? Brushes.Black : item.Filled && item.Kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse ? brush : null, new Pen(brush, item.Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, GeometryFor(item));
            dc.Pop();
        }
    }
    public static BitmapSource Composite(BitmapSource desktop, IEnumerable<Annotation> annotations, double scaleX, double scaleY, Int32Rect crop)
    {
        if (crop.Width <= 0 || crop.Height <= 0 || crop.X < 0 || crop.Y < 0 || crop.X + crop.Width > desktop.PixelWidth || crop.Y + crop.Height > desktop.PixelHeight) throw new ArgumentOutOfRangeException(nameof(crop));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(desktop, new Rect(-crop.X, -crop.Y, desktop.PixelWidth, desktop.PixelHeight));
            dc.PushTransform(new TranslateTransform(-crop.X, -crop.Y)); dc.PushTransform(new ScaleTransform(scaleX, scaleY)); Draw(dc, annotations); dc.Pop(); dc.Pop();
        }
        var result = new RenderTargetBitmap(crop.Width, crop.Height, 96, 96, PixelFormats.Pbgra32); result.Render(visual); result.Freeze(); return result;
    }
}
