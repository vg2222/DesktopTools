using DesktopTools.Localization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopTools.Native;

namespace DesktopTools.UI;

internal sealed class SelectionWindow : Window
{
    private readonly SelectionSurface surface = new();
    private Point? start;
    private Rect? pressedSuggestion;
    private Rect? suggestedRegion;
    private readonly bool smartRegion;
    private readonly IReadOnlyList<SmartRegionWindow>? frozenWindows;
    private readonly DispatcherTimer hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(65) };
    private static readonly SemaphoreSlim elementGate = new(1, 1);
    private int hoverRevision;
    private SmartRegionWindow? hoveredWindow;
    private Rect? refinedPhysicalRegion;
    private readonly TaskCompletionSource<Rect?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Rect? selected;
    private readonly MonitorInfo monitor;
    public Task<Rect?> Result => completion.Task;
    internal BitmapSource? FrozenFrame { get; }
    internal bool SmartRegionEnabled => smartRegion;
    internal bool CanRefineLiveElements => smartRegion && frozenWindows == null;
    public SelectionWindow(MonitorInfo monitor, bool textScan = false, BitmapSource? frozenFrame = null,
        bool smartRegion = false, IReadOnlyList<SmartRegionWindow>? frozenWindows = null)
    {
        this.monitor = monitor;
        this.smartRegion = smartRegion && !textScan;
        this.frozenWindows = frozenWindows;
        FrozenFrame = frozenFrame;
        surface.FrozenFrame = frozenFrame;
        surface.TextScan = textScan;
        surface.SmartRegion = this.smartRegion;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; Cursor = Cursors.Cross;
        Left = monitor.Bounds.X / monitor.ScaleX; Top = monitor.Bounds.Y / monitor.ScaleY; Width = monitor.Bounds.Width / monitor.ScaleX; Height = monitor.Bounds.Height / monitor.ScaleY; Content = surface;
        SourceInitialized += (_, _) => { NativeWindowService.ConfigureOverlay(this, false); MonitorService.PlaceWindow(this, monitor); NativeWindowService.TryExcludeFromCapture(this, true, out _); };
        hoverTimer.Tick += (_, _) => { hoverTimer.Stop(); ProbeHover(); };
        Loaded += (_, _) => QueueHover();
        MouseLeftButtonDown += (_, e) => { start = Clamp(e.GetPosition(surface)); pressedSuggestion = suggestedRegion is Rect r && r.Contains(start.Value) ? r : null; hoverTimer.Stop(); CaptureMouse(); };
        MouseMove += (_, e) =>
        {
            if (start.HasValue)
            {
                var position = Clamp(e.GetPosition(surface));
                if (!this.smartRegion || (position - start.Value).Length >= 6)
                {
                    surface.Selection = new Rect(start.Value, position);
                    surface.IsSuggestion = false;
                    surface.InvalidateVisual();
                }
            }
            else QueueHover();
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!start.HasValue) return;
            var result = SelectionForGesture(start.Value, Clamp(e.GetPosition(surface)), pressedSuggestion, this.smartRegion);
            start = null; pressedSuggestion = null; ReleaseMouseCapture();
            if (result != null || !this.smartRegion) CompleteSelection(result);
            else { SetSuggestedRegion(null); QueueHover(); }
        };
        MouseLeave += (_, _) => { if (!start.HasValue) { hoverTimer.Stop(); hoverRevision++; SetSuggestedRegion(null); } };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closed += (_, _) => { hoverTimer.Stop(); hoverRevision++; completion.TrySetResult(selected); };
    }
    internal static Rect? SelectionForGesture(Point down, Point up, Rect? suggestion, bool smartMode = true)
    {
        if (smartMode && (up - down).Length < 6) return suggestion is Rect region && region.Contains(down) ? region : null;
        var manual = new Rect(down, up);
        return manual.Width >= 2 && manual.Height >= 2 ? manual : null;
    }
    internal static Rect PhysicalToSurface(Rect physical, Rect monitorBounds, Size surfaceSize)
    {
        if (monitorBounds.Width <= 0 || monitorBounds.Height <= 0 || surfaceSize.Width <= 0 || surfaceSize.Height <= 0) return Rect.Empty;
        physical.Intersect(monitorBounds);
        if (physical.IsEmpty) return Rect.Empty;
        return new Rect((physical.X - monitorBounds.X) * surfaceSize.Width / monitorBounds.Width,
            (physical.Y - monitorBounds.Y) * surfaceSize.Height / monitorBounds.Height,
            physical.Width * surfaceSize.Width / monitorBounds.Width,
            physical.Height * surfaceSize.Height / monitorBounds.Height);
    }
    private void QueueHover()
    {
        if (!smartRegion || start.HasValue || completion.Task.IsCompleted || hoverTimer.IsEnabled) return;
        hoverTimer.Start();
    }
    private void ProbeHover()
    {
        if (!smartRegion || start.HasValue || !IsVisible || !NativeMethods.GetCursorPos(out var cursor)) return;
        var point = new Point(cursor.X, cursor.Y);
        int revision = ++hoverRevision;
        var window = HoverWindowAt(point);
        if (window != hoveredWindow) refinedPhysicalRegion = null;
        hoveredWindow = window;
        SetSuggestedRegion(CanRefineLiveElements && refinedPhysicalRegion is Rect refined && refined.Contains(point)
            ? refined : window?.Bounds);
        if (!CanRefineLiveElements || window is not SmartRegionWindow hit || !elementGate.Wait(0)) return;
        Rect captureBounds = monitor.Bounds;
        var weak = new WeakReference<SelectionWindow>(this);
        _ = Task.Run(() =>
        {
            try { return SmartRegionDetector.FindElement(hit.Handle, point, captureBounds, hit.Bounds); }
            catch { return null; } // A stale or unsupported provider leaves the window suggestion available.
            finally { elementGate.Release(); }
        }).ContinueWith(task =>
        {
            if (!weak.TryGetTarget(out var target) || target.Dispatcher.HasShutdownStarted) return;
            target.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!target.IsVisible || target.start.HasValue || target.completion.Task.IsCompleted) return;
                if (revision != target.hoverRevision) { target.QueueHover(); return; }
                if (task.Result is Rect region)
                {
                    target.refinedPhysicalRegion = region;
                    target.SetSuggestedRegion(region);
                }
                else
                {
                    target.refinedPhysicalRegion = null;
                    target.SetSuggestedRegion(hit.Bounds);
                }
            }));
        }, TaskScheduler.Default);
    }
    internal SmartRegionWindow? HoverWindowAt(Point physicalPoint) => frozenWindows != null
        ? SmartRegionDetector.ChooseWindow(frozenWindows, physicalPoint)
        : SmartRegionDetector.FindWindow(physicalPoint, monitor.Bounds);
    private void SetSuggestedRegion(Rect? physical)
    {
        suggestedRegion = physical is Rect bounds
            ? PhysicalToSurface(bounds, monitor.Bounds, new Size(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height))
            : null;
        if (suggestedRegion is Rect region && (region.IsEmpty || region.Width < 2 || region.Height < 2)) suggestedRegion = null;
        surface.Selection = suggestedRegion ?? Rect.Empty;
        surface.IsSuggestion = suggestedRegion != null;
        surface.InvalidateVisual();
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
        public bool SmartRegion { get; set; }
        public bool IsSuggestion { get; set; }
        public BitmapSource? FrozenFrame { get; set; }
        public Rect Selection { get; set; }
        protected override void OnRender(DrawingContext dc)
        {
            if (FrozenFrame != null) dc.DrawImage(FrozenFrame, new Rect(RenderSize));
            var full = new RectangleGeometry(new Rect(RenderSize));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(95, 0, 0, 0)), null, new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(Selection)));
            dc.DrawRectangle(Brushes.Transparent, new Pen(IsSuggestion ? Application.Current?.TryFindResource("Accent") as Brush ?? Brushes.DodgerBlue : Brushes.White, IsSuggestion ? 2 : 1), Selection);
            var hint = TextScan ? "Drag to scan screen text   ·   Esc to cancel" : SmartRegion
                ? "Click highlighted area · Drag a custom region · Esc to cancel" : "Drag to capture a region   ·   Esc to cancel";
            var text = new FormattedText(L.T(hint), System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 15, Brushes.White, 1)
            { MaxTextWidth = Math.Max(120, RenderSize.Width - 72) };
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(220, 25, 25, 28)), null, new Rect(20, 20, text.Width + 32, text.Height + 24), 12, 12);
            dc.DrawText(text, new Point(36, 32));
        }
    }
}
