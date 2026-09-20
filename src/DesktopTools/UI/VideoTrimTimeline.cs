using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopTools.UI;

internal sealed class VideoTrimTimeline : FrameworkElement
{
    internal double Duration { get; private set; }
    internal double Start { get; private set; }
    internal double End { get; private set; }
    internal event Action<double, double>? RangeChanged;
    internal event Action<double, double>? CutChanged;
    internal event Action<double>? SeekRequested;
    internal event Action? SeekCompleted;
    internal bool IsScrubbing => dragging && scrubbing;
    internal IReadOnlyList<System.Windows.Media.Imaging.BitmapSource> Thumbnails { get; set; } = Array.Empty<System.Windows.Media.Imaging.BitmapSource>();
    internal double Position { get; set; }
    internal double? CutStart { get; set; }
    internal double CutEnd { get; set; }
    private bool dragging, left;
    private bool cutting;
    private bool scrubbing;
    private double savedPosition;
    private double savedCutStart, savedCutEnd;
    private double savedStart, savedEnd;
    internal VideoTrimTimeline() { Height = 58; Focusable = true; Cursor = Cursors.SizeWE; }
    internal void SetRange(double duration, double start, double end)
    {
        Duration = Math.Max(0, duration); Start = Math.Clamp(start, 0, Duration); End = Math.Clamp(end, Start, Duration); InvalidateVisual();
    }
    private double X(double time) => 10 + (ActualWidth - 20) * time / Math.Max(.001, Duration);
    protected override void OnRender(DrawingContext dc)
    {
        double width = Math.Max(0, ActualWidth - 20);
        dc.DrawRoundedRectangle(Ui.Brush("Field"), new Pen(Ui.Brush("Stroke"), 1), new Rect(10, 8, width, 38), 6, 6);
        if (Duration <= 0) return;
        dc.PushClip(new RectangleGeometry(new Rect(10, 8, width, 38), 6, 6));
        for (int i = 0; i < Thumbnails.Count; i++)
        {
            var thumbnail = Thumbnails[i];
            var tile = new Rect(10 + width * i / Thumbnails.Count, 8, width / Thumbnails.Count + .5, 38);
            double scale = Math.Max(tile.Width / thumbnail.PixelWidth, tile.Height / thumbnail.PixelHeight);
            double imageWidth = thumbnail.PixelWidth * scale, imageHeight = thumbnail.PixelHeight * scale;
            dc.PushClip(new RectangleGeometry(tile));
            dc.DrawImage(thumbnail, new Rect(tile.X + (tile.Width - imageWidth) / 2, tile.Y + (tile.Height - imageHeight) / 2, imageWidth, imageHeight));
            dc.Pop();
        }
        var shade = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0));
        dc.DrawRectangle(shade, null, new Rect(10, 8, Math.Max(0, X(Start) - 10), 38));
        dc.DrawRectangle(shade, null, new Rect(X(End), 8, Math.Max(0, width + 10 - X(End)), 38));
        if (CutStart is double cut && CutEnd > cut)
        {
            double a = X(Math.Clamp(cut, Start, End)), b = X(Math.Clamp(CutEnd, Start, End));
            dc.DrawRectangle(shade, null, new Rect(a, 8, Math.Max(0, b - a), 38));
            dc.PushClip(new RectangleGeometry(new Rect(a, 8, Math.Max(0, b - a), 38)));
            for (double x = a - 38; x < b; x += 10) dc.DrawLine(new Pen(Brushes.IndianRed, 1), new Point(x, 46), new Point(x + 38, 8));
            dc.Pop();
        }
        dc.Pop();
        if (CutStart is double cutHandle && CutEnd > cutHandle)
            foreach (double time in new[] { cutHandle, CutEnd })
            { double x = X(Math.Clamp(time, Start, End)); dc.DrawRoundedRectangle(Brushes.IndianRed, null, new Rect(x - 4, 12, 8, 30), 3, 3); dc.DrawLine(new Pen(Brushes.White, 1), new Point(x, 20), new Point(x, 34)); }
        var selection = new Rect(X(Start), 8, Math.Max(0, X(End) - X(Start)), 38);
        dc.DrawRoundedRectangle(null, new Pen(Ui.Brush("Accent"), 2), selection, 4, 4);
        foreach (double time in new[] { Start, End }) { double x = X(time); dc.DrawRoundedRectangle(Ui.Brush("Accent"), null, new Rect(x - 5, 8, 10, 38), 4, 4); dc.DrawLine(new Pen(Brushes.White, 2), new Point(x, 18), new Point(x, 36)); }
        double position = X(Math.Clamp(Position, 0, Duration)); dc.DrawLine(new Pen(Ui.Brush("Accent"), 2), new Point(position, 2), new Point(position, 52)); dc.DrawEllipse(Ui.Brush("Accent"), null, new Point(position, 3), 3, 3);
        if (IsKeyboardFocused) dc.DrawRectangle(null, new Pen(Ui.Brush("Accent"), 1), new Rect(1, 1, Math.Max(0, ActualWidth - 2), ActualHeight - 2));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var point = e.GetPosition(this); double x = point.X;
        if (point.Y < 8 && Math.Abs(x - X(Position)) < 10 && BeginScrub(x))
        { Focus(); CaptureMouse(); e.Handled = true; return; }
        if (BeginCutDrag(x)) { Focus(); CaptureMouse(); e.Handled = true; return; }
        if (Duration > 0 && Math.Min(Math.Abs(x - X(Start)), Math.Abs(x - X(End))) > 10)
        { if (BeginScrub(x)) { Focus(); CaptureMouse(); e.Handled = true; } return; }
        if (!BeginDrag(x)) return; Focus(); CaptureMouse(); e.Handled = true;
    }
    internal bool BeginDrag(double x)
    {
        if (Duration <= 0) return false;
        cutting = scrubbing = false; left = Math.Abs(x - X(Start)) <= Math.Abs(x - X(End)); savedStart = Start; savedEnd = End; dragging = true; return true;
    }
    internal bool BeginScrub(double x)
    {
        if (Duration <= 0) return false;
        cutting = false; scrubbing = dragging = true; savedPosition = Position; DragTo(x); return true;
    }
    internal bool BeginCutDrag(double x)
    {
        if (Duration <= 0 || CutStart is not double cut || cut < Start || CutEnd > End || CutEnd - cut < .05 || End - Start < .15 ||
            Math.Min(Math.Abs(x - X(cut)), Math.Abs(x - X(CutEnd))) > 8) return false;
        scrubbing = false; cutting = true; left = Math.Abs(x - X(cut)) <= Math.Abs(x - X(CutEnd));
        savedCutStart = cut; savedCutEnd = CutEnd; dragging = true; return true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
        => DragTo(e.GetPosition(this).X);
    internal void DragTo(double x)
    {
        if (!dragging) return;
        double time = Math.Clamp((x - 10) / Math.Max(1, ActualWidth - 20) * Duration, 0, Duration);
        if (scrubbing) { Position = time; InvalidateVisual(); SeekRequested?.Invoke(time); return; }
        if (cutting)
        {
            if (left) CutStart = Math.Clamp(time, Start, Math.Max(Start, CutEnd - .05));
            else CutEnd = Math.Clamp(time, Math.Min(End, CutStart!.Value + .05), End);
            if (CutStart - Start < .05) CutStart = Start;
            if (End - CutEnd < .05) CutEnd = End;
            if (CutEnd - CutStart > End - Start - .05)
            { if (left) CutStart = Start + .05; else CutEnd = End - .05; }
            InvalidateVisual(); CutChanged?.Invoke(CutStart!.Value, CutEnd); return;
        }
        if (left) Start = Math.Clamp(time, 0, Math.Max(0, End - .05)); else End = Math.Clamp(time, Math.Min(Duration, Start + .05), Duration);
        InvalidateVisual(); RangeChanged?.Invoke(Start, End);
    }
    internal void EndDrag(bool cancel = false)
    {
        if (cancel) { Cancel(); return; }
        bool completeSeek = IsScrubbing; dragging = scrubbing = false; ReleaseMouseCapture();
        if (completeSeek) SeekCompleted?.Invoke();
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { EndDrag(); e.Handled = true; }
    protected override void OnLostMouseCapture(MouseEventArgs e) { if (dragging) Cancel(); }
    private void Cancel()
    {
        if (!dragging) return;
        dragging = false; ReleaseMouseCapture();
        if (scrubbing)
        {
            scrubbing = false; Position = savedPosition; InvalidateVisual(); SeekRequested?.Invoke(Position); SeekCompleted?.Invoke(); return;
        }
        if (cutting) { CutStart = savedCutStart; CutEnd = savedCutEnd; CutChanged?.Invoke(savedCutStart, savedCutEnd); }
        else { Start = savedStart; End = savedEnd; RangeChanged?.Invoke(Start, End); }
        InvalidateVisual();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && dragging) { Cancel(); e.Handled = true; return; }
        if (Duration <= 0 || e.Key is not (Key.Left or Key.Right)) return;
        double delta = e.Key == Key.Left ? -.1 : .1;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) End = Math.Clamp(End + delta, Math.Min(Duration, Start + .05), Duration);
        else Start = Math.Clamp(Start + delta, 0, Math.Max(0, End - .05));
        InvalidateVisual(); RangeChanged?.Invoke(Start, End); e.Handled = true;
    }
}
