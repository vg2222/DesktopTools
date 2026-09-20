using System.Windows;

namespace DesktopTools.Core;

public static class AnnotationTransform
{
    public static Annotation Move(Annotation item, Vector offset) => item with
    {
        Points = Array.AsReadOnly(item.Points.Select(p => p + offset).ToArray())
    };

    public static Annotation Duplicate(Annotation item) => Move(item, new Vector(16, 16)) with { Id = Guid.NewGuid() };

    public static Annotation Resize(Annotation item, Rect source, Rect target)
    {
        if (source.IsEmpty || target.IsEmpty) return item;
        double sx = source.Width > .001 ? target.Width / source.Width : 1;
        double sy = source.Height > .001 ? target.Height / source.Height : 1;
        double scale = Math.Max(.05, Math.Min(sx, sy));
        return item with
        {
            Points = Array.AsReadOnly(item.Points.Select(p => new Point(target.X + (p.X - source.X) * sx, target.Y + (p.Y - source.Y) * sy)).ToArray()),
            FontSize = item.Kind is AnnotationKind.Text or AnnotationKind.Number ? Math.Clamp(item.FontSize * scale, 4, 500) : item.FontSize,
            TextWidth = item.Kind == AnnotationKind.Text && item.TextWidth > 0 ? Math.Max(1, item.TextWidth * sx) : item.TextWidth
        };
    }
}
