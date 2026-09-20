using System.Windows;

namespace DesktopTools.Core;

public static class DrawingGeometry
{
    public static Point Constrain(Point start, Point end, AnnotationKind kind)
    {
        var delta = end - start;
        if (kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse)
        {
            double side = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
            return start + new Vector(delta.X < 0 ? -side : side, delta.Y < 0 ? -side : side);
        }
        if (kind is AnnotationKind.Line or AnnotationKind.Arrow)
        {
            double angle = Math.Round(Math.Atan2(delta.Y, delta.X) / (Math.PI / 4)) * Math.PI / 4;
            return start + new Vector(Math.Cos(angle), Math.Sin(angle)) * delta.Length;
        }
        return end;
    }
}
