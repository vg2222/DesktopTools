using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopTools.UI;

internal sealed class ImageEditCanvas : Grid
{
    internal Image Image { get; } = new() { Stretch = Stretch.Uniform };
    private readonly Image comparison = new() { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Border dividerLine = new() { Width = 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, IsHitTestVisible = false, Background = Brushes.White };
    private readonly System.Windows.Controls.Primitives.Thumb divider = new() { Width = 18, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Cursor = System.Windows.Input.Cursors.SizeWE, Visibility = Visibility.Collapsed, Opacity = 0 };
    private readonly TextBlock beforeLabel = ComparisonLabel();
    private readonly TextBlock afterLabel = ComparisonLabel();
    private double comparisonPosition = .5;
    internal event Action<double>? ComparisonMoved;
    internal TransformHandles Handles { get; } = new() { Visibility = Visibility.Collapsed };
    private Int32Rect selection;
    internal bool ResizePreview { get; set; }
    internal event Action<Int32Rect, bool>? SelectionChanged;
    internal bool KeepRatio { get => Handles.KeepRatio; set => Handles.KeepRatio = value; }
    internal Int32Rect Selection => selection;
    internal bool IsComparing => comparison.Visibility == Visibility.Visible;
    // Touch-up brush: while active, a left-button drag reports image-pixel points (begin, move, end) and a circle shows the brush size.
    private readonly System.Windows.Shapes.Ellipse brushOutline = new() { Stroke = Brushes.White, StrokeThickness = 1.5, StrokeDashArray = [3, 2], HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private bool brushActive, brushDown;
    internal double BrushDiameter { get; set; } = 24;
    internal event Action<Point>? BrushStarted, BrushMoved;
    internal event Action? BrushEnded;
    internal bool BrushActive
    {
        get => brushActive;
        set
        {
            if (brushActive == value) return;
            brushActive = value; Cursor = value ? System.Windows.Input.Cursors.Cross : null;
            if (!value) { EndBrush(); brushOutline.Visibility = Visibility.Collapsed; }
        }
    }
    internal Rect ImageBounds
    {
        get
        {
            if (Image.Source is not BitmapSource bitmap || ActualWidth <= 0 || ActualHeight <= 0) return Rect.Empty;
            double ratio = bitmap.Width / bitmap.Height, width = Math.Min(ActualWidth, ActualHeight * ratio), height = width / ratio;
            return new Rect((ActualWidth - width) / 2, (ActualHeight - height) / 2, width, height);
        }
    }
    internal ImageEditCanvas()
    {
        beforeLabel.HorizontalAlignment = HorizontalAlignment.Left; afterLabel.HorizontalAlignment = HorizontalAlignment.Right;
        Children.Add(Image); Children.Add(comparison); Children.Add(dividerLine); Children.Add(divider); Children.Add(beforeLabel); Children.Add(afterLabel); Children.Add(Handles); Children.Add(brushOutline); ClipToBounds = false;
        SizeChanged += (_, _) => { UpdateHandles(); UpdateResizePreview(); UpdateComparison(); };
        divider.DragDelta += (_, e) =>
        {
            var bounds = ImageBounds; if (bounds.IsEmpty || bounds.Width <= 0) return;
            comparisonPosition = Math.Clamp(comparisonPosition + e.HorizontalChange / bounds.Width, 0, 1); UpdateComparison(); ComparisonMoved?.Invoke(comparisonPosition);
        };
        Handles.Changed += (rect, completed) =>
        {
            var bounds = ImageBounds;
            if (Image.Source is not BitmapSource bitmap || bounds.IsEmpty) return;
            int x = Math.Clamp((int)Math.Round((rect.Left - bounds.Left) / bounds.Width * bitmap.PixelWidth), 0, bitmap.PixelWidth - 1);
            int y = Math.Clamp((int)Math.Round((rect.Top - bounds.Top) / bounds.Height * bitmap.PixelHeight), 0, bitmap.PixelHeight - 1);
            int right = Math.Clamp((int)Math.Round((rect.Right - bounds.Left) / bounds.Width * bitmap.PixelWidth), x + 1, bitmap.PixelWidth);
            int bottom = Math.Clamp((int)Math.Round((rect.Bottom - bounds.Top) / bounds.Height * bitmap.PixelHeight), y + 1, bitmap.PixelHeight);
            selection = new(x, y, right - x, bottom - y);
            if (ResizePreview)
            {
                double sx = rect.Width / bounds.Width, sy = rect.Height / bounds.Height;
                Image.RenderTransform = new MatrixTransform(sx, 0, 0, sy, rect.Left - bounds.Left * sx, rect.Top - bounds.Top * sy);
                selection = new(0, 0, selection.Width, selection.Height);
            }
            SelectionChanged?.Invoke(selection, completed);
        };
        Background = Brushes.Transparent;
        MouseLeftButtonDown += (_, e) =>
        {
            if (!brushActive || ToPixel(e.GetPosition(this)) is not Point point) return;
            brushDown = CaptureMouse(); e.Handled = true; BrushStarted?.Invoke(point);
        };
        MouseMove += (_, e) =>
        {
            if (!brushActive) return;
            var position = e.GetPosition(this); ShowBrush(position);
            if (brushDown && ToPixel(position, clamp: false) is Point point) BrushMoved?.Invoke(point);
        };
        MouseLeave += (_, _) => { if (!brushDown) brushOutline.Visibility = Visibility.Collapsed; };
        MouseLeftButtonUp += (_, e) => { if (brushDown) { e.Handled = true; EndBrush(); } };
        LostMouseCapture += (_, _) => EndBrush();
    }
    private void EndBrush()
    {
        if (!brushDown) return;
        brushDown = false; if (IsMouseCaptured) ReleaseMouseCapture(); BrushEnded?.Invoke();
    }
    /// <summary>Canvas point → image pixel; null outside the picture when <paramref name="clamp"/> is off for the first press.</summary>
    private Point? ToPixel(Point position, bool clamp = true)
    {
        var bounds = ImageBounds;
        if (Image.Source is not BitmapSource bitmap || bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return null;
        if (clamp && !bounds.Contains(position)) return null;
        return new Point((position.X - bounds.Left) / bounds.Width * bitmap.PixelWidth, (position.Y - bounds.Top) / bounds.Height * bitmap.PixelHeight);
    }
    private void ShowBrush(Point position)
    {
        var bounds = ImageBounds;
        if (Image.Source is not BitmapSource bitmap || bounds.IsEmpty) { brushOutline.Visibility = Visibility.Collapsed; return; }
        double size = Math.Max(2, BrushDiameter * bounds.Width / bitmap.PixelWidth);
        brushOutline.Width = brushOutline.Height = size; brushOutline.Margin = new Thickness(position.X - size / 2, position.Y - size / 2, 0, 0);
        brushOutline.Visibility = bounds.Contains(position) || brushDown ? Visibility.Visible : Visibility.Collapsed;
    }
    internal void SetComparison(BitmapSource? original, double position, string? before = null, string? after = null)
    {
        if (original != null && Image.Source is BitmapSource current &&
            (original.PixelWidth != current.PixelWidth || original.PixelHeight != current.PixelHeight))
            original = null;
        comparison.Source = original; comparisonPosition = Math.Clamp(position, 0, 1);
        var visibility = original == null ? Visibility.Collapsed : Visibility.Visible;
        comparison.Visibility = divider.Visibility = dividerLine.Visibility = beforeLabel.Visibility = afterLabel.Visibility = visibility;
        beforeLabel.Text = before ?? string.Empty; afterLabel.Text = after ?? string.Empty;
        if (original != null) ShowHandles(false);
        UpdateComparison();
    }
    private void UpdateComparison()
    {
        var bounds = ImageBounds; if (bounds.IsEmpty) return;
        double x = bounds.Left + bounds.Width * comparisonPosition;
        comparison.Clip = new RectangleGeometry(new Rect(bounds.Left, bounds.Top, Math.Max(0, x - bounds.Left), bounds.Height));
        divider.Margin = new Thickness(x - divider.Width / 2, bounds.Top, 0, 0); divider.Height = bounds.Height;
        dividerLine.Margin = new Thickness(x - dividerLine.Width / 2, bounds.Top, 0, 0); dividerLine.Height = bounds.Height;
        beforeLabel.Margin = new Thickness(bounds.Left + 10, bounds.Top + 10, 0, 0);
        afterLabel.Margin = new Thickness(0, bounds.Top + 10, Math.Max(0, ActualWidth - bounds.Right + 10), 0);
    }
    private static TextBlock ComparisonLabel()
    {
        var label = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Padding = new Thickness(7, 3, 7, 3), VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, IsHitTestVisible = false, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(180, 20, 20, 24)) };
        return label;
    }
    internal void ResetSelection()
    {
        Image.RenderTransform = Transform.Identity;
        if (Image.Source is BitmapSource bitmap) selection = new(0, 0, bitmap.PixelWidth, bitmap.PixelHeight);
        else selection = default;
        UpdateHandles();
    }
    internal void SetSelection(Int32Rect value)
    {
        if (Image.Source is not BitmapSource bitmap || value.Width < 1 || value.Height < 1 || value.X < 0 || value.Y < 0 ||
            (long)value.X + value.Width > bitmap.PixelWidth || (long)value.Y + value.Height > bitmap.PixelHeight) throw new ArgumentOutOfRangeException(nameof(value));
        Handles.EndDrag(true); selection = value; Image.RenderTransform = Transform.Identity; UpdateHandles();
        UpdateResizePreview();
        SelectionChanged?.Invoke(selection, true);
    }
    private void UpdateResizePreview()
    {
        if (ResizePreview && !ImageBounds.IsEmpty)
        {
            var bounds = ImageBounds; var rect = Handles.Selection;
            double sx = rect.Width / bounds.Width, sy = rect.Height / bounds.Height;
            Image.RenderTransform = new MatrixTransform(sx, 0, 0, sy, rect.Left - bounds.Left * sx, rect.Top - bounds.Top * sy);
        }
    }
    internal void ShowHandles(bool show) { Handles.EndDrag(true); Handles.Visibility = show ? Visibility.Visible : Visibility.Collapsed; UpdateHandles(); }
    private void UpdateHandles()
    {
        if (Image.Source is not BitmapSource bitmap) { Handles.Bounds = Rect.Empty; Handles.InvalidateVisual(); return; }
        var bounds = ImageBounds; if (bounds.IsEmpty) return;
        Handles.Bounds = bounds; Handles.Selection = new Rect(bounds.Left + selection.X * bounds.Width / bitmap.PixelWidth, bounds.Top + selection.Y * bounds.Height / bitmap.PixelHeight, selection.Width * bounds.Width / bitmap.PixelWidth, selection.Height * bounds.Height / bitmap.PixelHeight); Handles.InvalidateVisual();
    }
}
