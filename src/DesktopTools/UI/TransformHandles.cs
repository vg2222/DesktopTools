using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopTools.UI;

internal enum TransformHandle { None, Move, Left, Top, Right, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>Crop interaction in view coordinates; the parent maps its result back into pixels.</summary>
internal sealed class TransformHandles : FrameworkElement
{
    internal Rect Bounds { get; set; }
    internal Rect Selection { get; set; }
    internal bool KeepRatio { get; set; }
    internal bool DimOutsideSelection { get; set; }
    internal event Action<Rect, bool>? Changed;
    private TransformHandle active;
    private Rect initial;
    private Point origin;
    internal bool IsDragging => active != TransformHandle.None;
    public TransformHandles() { Focusable = true; Cursor = Cursors.Cross; }
    private static (TransformHandle Handle, Point Point)[] Points(Rect rect) =>
    [
        (TransformHandle.TopLeft, rect.TopLeft), (TransformHandle.TopRight, rect.TopRight),
        (TransformHandle.BottomLeft, rect.BottomLeft), (TransformHandle.BottomRight, rect.BottomRight),
        (TransformHandle.Top, new(rect.Left + rect.Width / 2, rect.Top)), (TransformHandle.Bottom, new(rect.Left + rect.Width / 2, rect.Bottom)),
        (TransformHandle.Left, new(rect.Left, rect.Top + rect.Height / 2)), (TransformHandle.Right, new(rect.Right, rect.Top + rect.Height / 2))
    ];
    protected override void OnRender(DrawingContext dc)
    {
        if (Bounds.IsEmpty || Selection.IsEmpty) return;
        dc.DrawRectangle(Brushes.Transparent, null, Bounds);
        if (DimOutsideSelection)
        {
            var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(Bounds), new RectangleGeometry(Selection));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(125, 0, 0, 0)), null, outside);
        }
        var outline = new Pen(Brushes.DodgerBlue, 2); dc.DrawRectangle(null, outline, Selection);
        foreach (var (_, point) in Points(Selection)) dc.DrawRectangle(Brushes.White, outline, new Rect(point.X - 4, point.Y - 4, 8, 8));
    }
    internal bool BeginDrag(Point point)
    {
        if (Bounds.IsEmpty || !new Rect(Bounds.X - 7, Bounds.Y - 7, Bounds.Width + 14, Bounds.Height + 14).Contains(point)) return false;
        active = Points(Selection).Where(h => (h.Point - point).Length <= 9).Select(h => h.Handle).FirstOrDefault();
        origin = point; initial = Selection;
        if (active == TransformHandle.None)
        {
            active = Selection.Contains(point) ? TransformHandle.Move : TransformHandle.BottomRight;
            if (active == TransformHandle.BottomRight) initial = new Rect(Clamp(point), new Size(1, 1));
        }
        Focus(); return true;
    }
    private Point Clamp(Point point) => new(Math.Clamp(point.X, Bounds.Left, Bounds.Right), Math.Clamp(point.Y, Bounds.Top, Bounds.Bottom));
    internal void DragTo(Point point)
    {
        if (!IsDragging) return;
        point = Clamp(point); var delta = point - origin;
        if (active == TransformHandle.Move)
            Selection = new Rect(Math.Clamp(initial.X + delta.X, Bounds.Left, Math.Max(Bounds.Left, Bounds.Right - initial.Width)), Math.Clamp(initial.Y + delta.Y, Bounds.Top, Math.Max(Bounds.Top, Bounds.Bottom - initial.Height)), initial.Width, initial.Height);
        else
        {
            bool left = active is TransformHandle.Left or TransformHandle.TopLeft or TransformHandle.BottomLeft;
            bool right = active is TransformHandle.Right or TransformHandle.TopRight or TransformHandle.BottomRight;
            bool top = active is TransformHandle.Top or TransformHandle.TopLeft or TransformHandle.TopRight;
            bool bottom = active is TransformHandle.Bottom or TransformHandle.BottomLeft or TransformHandle.BottomRight;
            double x1 = left ? Math.Min(point.X, initial.Right - 1) : initial.Left, x2 = right ? Math.Max(point.X, initial.Left + 1) : initial.Right;
            double y1 = top ? Math.Min(point.Y, initial.Bottom - 1) : initial.Top, y2 = bottom ? Math.Max(point.Y, initial.Top + 1) : initial.Bottom;
            if (KeepRatio && initial.Height > 0)
            {
                double ratio = initial.Width / initial.Height, width = x2 - x1, height = y2 - y1;
                if ((left || right) && !(top || bottom)) height = width / ratio;
                else if ((top || bottom) && !(left || right)) width = height * ratio;
                else if (width / height > ratio) width = height * ratio; else height = width / ratio;
                double anchorX = left ? initial.Right : initial.Left, anchorY = top ? initial.Bottom : initial.Top;
                double maximumWidth = left ? anchorX - Bounds.Left : Bounds.Right - anchorX;
                double maximumHeight = top ? anchorY - Bounds.Top : Bounds.Bottom - anchorY;
                double scale = Math.Min(1, Math.Min(maximumWidth / width, maximumHeight / height)); width *= scale; height *= scale;
                x1 = left ? anchorX - width : anchorX; x2 = x1 + width; y1 = top ? anchorY - height : anchorY; y2 = y1 + height;
            }
            Selection = Rect.Intersect(Bounds, new Rect(new Point(x1, y1), new Point(x2, y2)));
        }
        InvalidateVisual(); Changed?.Invoke(Selection, false);
    }
    internal void EndDrag(bool cancel = false)
    {
        if (!IsDragging) return;
        if (cancel) Selection = initial;
        active = TransformHandle.None; ReleaseMouseCapture(); InvalidateVisual(); Changed?.Invoke(Selection, !cancel);
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { if (BeginDrag(e.GetPosition(this))) { CaptureMouse(); e.Handled = true; } }
    protected override void OnMouseMove(MouseEventArgs e) { if (IsDragging) { DragTo(e.GetPosition(this)); e.Handled = true; } }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { if (IsDragging) { DragTo(e.GetPosition(this)); EndDrag(); e.Handled = true; } }
    protected override void OnLostMouseCapture(MouseEventArgs e) { if (IsDragging) EndDrag(true); base.OnLostMouseCapture(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.Key == Key.Escape && IsDragging) { EndDrag(true); e.Handled = true; } }
}
