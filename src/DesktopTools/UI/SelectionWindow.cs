using DesktopTools.Localization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Native;

namespace DesktopTools.UI;

internal sealed class SelectionWindow : Window
{
    private readonly SelectionSurface surface = new();
    private Point? start;
    private readonly TaskCompletionSource<Rect?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Rect? selected;
    private readonly MonitorInfo monitor;
    public Task<Rect?> Result => completion.Task;
    internal BitmapSource? FrozenFrame { get; }
    public SelectionWindow(MonitorInfo monitor, bool textScan = false, BitmapSource? frozenFrame = null)
    {
        this.monitor = monitor;
        FrozenFrame = frozenFrame;
        surface.FrozenFrame = frozenFrame;
        surface.TextScan = textScan;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; Cursor = Cursors.Cross;
        Left = monitor.Bounds.X / monitor.ScaleX; Top = monitor.Bounds.Y / monitor.ScaleY; Width = monitor.Bounds.Width / monitor.ScaleX; Height = monitor.Bounds.Height / monitor.ScaleY; Content = surface;
        SourceInitialized += (_, _) => { NativeWindowService.ConfigureOverlay(this, false); MonitorService.PlaceWindow(this, monitor); NativeWindowService.TryExcludeFromCapture(this, true, out _); };
        MouseLeftButtonDown += (_, e) => { start = e.GetPosition(surface); CaptureMouse(); };
        MouseMove += (_, e) => { if (start.HasValue) { surface.Selection = new Rect(start.Value, Clamp(e.GetPosition(surface))); surface.InvalidateVisual(); } };
        MouseLeftButtonUp += (_, e) => { if (!start.HasValue) return; var rect = new Rect(start.Value, Clamp(e.GetPosition(surface))); ReleaseMouseCapture(); CompleteSelection(rect.Width >= 2 && rect.Height >= 2 ? rect : null); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => completion.TrySetResult(selected);
    }
    internal void CompleteSelection(Rect? result)
    {
        // A spanning HWND takes its host display DPI, which can differ from the
        // virtual desktop's canonical 1:1 scale. Return canonical monitor DIPs.
        if (result is Rect r && ActualWidth > 0 && ActualHeight > 0)
        {
            double sx = monitor.Bounds.Width / ActualWidth / monitor.ScaleX;
            double sy = monitor.Bounds.Height / ActualHeight / monitor.ScaleY;
            selected = new Rect(r.X * sx, r.Y * sy, r.Width * sx, r.Height * sy);
        }
        else selected = result;
        Close();
    }
    private Point Clamp(Point p) => new(Math.Clamp(p.X, 0, ActualWidth), Math.Clamp(p.Y, 0, ActualHeight));
    private sealed class SelectionSurface : FrameworkElement
    {
        public bool TextScan { get; set; }
        public BitmapSource? FrozenFrame { get; set; }
        public Rect Selection { get; set; }
        protected override void OnRender(DrawingContext dc)
        {
            if (FrozenFrame != null) dc.DrawImage(FrozenFrame, new Rect(RenderSize));
            var full = new RectangleGeometry(new Rect(RenderSize));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(95, 0, 0, 0)), null, new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(Selection)));
            dc.DrawRectangle(Brushes.Transparent, new Pen(Brushes.White, 1), Selection);
            var text = new FormattedText(L.T(TextScan ? "Drag to scan screen text   ·   Esc to cancel" : "Drag to capture a region   ·   Esc to cancel"), System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 15, Brushes.White, 1);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(220, 25, 25, 28)), null, new Rect(20, 20, text.Width + 32, 46), 12, 12); dc.DrawText(text, new Point(36, 32));
        }
    }
}
