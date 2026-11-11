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
            else
            {
                QueueHover();
                // The loupe follows the pointer over a still frame; a live screen keeps its cheaper repaint.
                if (FrozenFrame != null) { surface.Pointer = Clamp(e.GetPosition(surface)); surface.InvalidateVisual(); }
            }
            if (start.HasValue && FrozenFrame != null) surface.Pointer = Clamp(e.GetPosition(surface));
        };
        SizeChanged += (_, _) => { if (ActualWidth > 0 && ActualHeight > 0) surface.PixelScale = new Vector(monitor.Bounds.Width / ActualWidth, monitor.Bounds.Height / ActualHeight); };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!start.HasValue) return;
            var result = SelectionForGesture(start.Value, Clamp(e.GetPosition(surface)), pressedSuggestion, this.smartRegion);
            start = null; pressedSuggestion = null; ReleaseMouseCapture();
            if (result != null || !this.smartRegion) CompleteSelection(result);
            else { SetSuggestedRegion(null); QueueHover(); }
        };
        MouseLeave += (_, _) => { if (!start.HasValue) { hoverTimer.Stop(); hoverRevision++; surface.Pointer = null; SetSuggestedRegion(null); } };
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
    /// <summary>
    /// Three layers so a mouse move repaints only what moved: the still frame and its dimming never change, the hint never changes,
    /// and <see cref="Marks"/> redraws the selection, its size and the loupe.
    /// </summary>
    private sealed class SelectionSurface : System.Windows.Controls.Grid
    {
        private static readonly Brush Dim = Frozen(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)));
        private static readonly Brush HitFill = Frozen(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));
        private static readonly Brush Plate = Frozen(new SolidColorBrush(Color.FromArgb(232, 22, 23, 27)));
        private static readonly Pen PlateRim = FrozenPen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1);
        private static readonly Pen Outline = FrozenPen(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), 3);
        private static readonly Pen White = FrozenPen(Brushes.White, 1.5);
        private static readonly Pen Bracket = FrozenPen(Brushes.White, 3);
        private static readonly Pen LoupeGrid = FrozenPen(new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)), 1);
        private static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static T Frozen<T>(T value) where T : Freezable { value.Freeze(); return value; }
        private static Pen FrozenPen(Brush brush, double thickness) => Frozen(new Pen(brush, thickness));

        private readonly Marks marks;
        private readonly HintPlate hint;
        private BitmapSource? frozenFrame;
        public bool TextScan { get => hint.TextScan; set => hint.TextScan = value; }
        public bool SmartRegion { get => hint.SmartRegion; set => hint.SmartRegion = value; }
        public bool IsSuggestion { get; set; }
        public Rect Selection { get; set; }
        /// <summary>Pointer position in surface DIPs, or null while the pointer is elsewhere; drives the loupe.</summary>
        public Point? Pointer { get; set; }
        /// <summary>Physical pixels per surface DIP, so the size badge shows what the PNG will measure.</summary>
        public Vector PixelScale { get; set; } = new(1, 1);
        public BitmapSource? FrozenFrame
        {
            get => frozenFrame;
            set
            {
                frozenFrame = value;
                // With a still frame the dimming is one unchanging rectangle and the selection is the frame drawn again on top.
                Children.Clear();
                if (value != null)
                {
                    Children.Add(new System.Windows.Controls.Image { Source = value, Stretch = Stretch.Fill, IsHitTestVisible = false });
                    Children.Add(new System.Windows.Shapes.Rectangle { Fill = Dim, IsHitTestVisible = false });
                }
                Children.Add(marks); Children.Add(hint);
            }
        }
        public SelectionSurface()
        {
            marks = new Marks(this); hint = new HintPlate();
            Background = Brushes.Transparent; // the whole surface takes the mouse, including the clear selection
            Children.Add(marks); Children.Add(hint);
            Loaded += (_, _) =>
            {
                if (!Motion.Enabled) return;
                // The hint drops into place; the frame and dimming appear at once so the screen never seems to blink.
                hint.Opacity = 0; hint.RenderTransform = new TranslateTransform(0, -8);
                var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
                hint.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
                hint.RenderTransform.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            };
        }
        public new void InvalidateVisual() => marks.InvalidateVisual();

        private sealed class HintPlate : FrameworkElement
        {
            public bool TextScan { get; set; }
            public bool SmartRegion { get; set; }
            public HintPlate() => IsHitTestVisible = false;
            protected override void OnRender(DrawingContext dc)
            {
                var hint = TextScan ? "Drag to scan screen text   ·   Esc to cancel" : SmartRegion
                    ? "Click highlighted area · Drag a custom region · Esc to cancel" : "Drag to capture a region   ·   Esc to cancel";
                var text = new FormattedText(L.T(hint), System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 14, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip)
                { MaxTextWidth = Math.Max(120, RenderSize.Width - 72) };
                var plate = new Rect(20, 20, text.Width + 36, text.Height + 22);
                dc.DrawRoundedRectangle(Plate, PlateRim, plate, plate.Height / 2, plate.Height / 2);
                dc.DrawText(text, new Point(plate.X + 18, plate.Y + 11));
            }
        }

        private sealed class Marks(SelectionSurface owner) : FrameworkElement
        {
            protected override void OnRender(DrawingContext dc)
            {
                var size = RenderSize; var selection = owner.Selection; var frame = owner.FrozenFrame;
                bool has = !selection.IsEmpty && selection.Width > 0 && selection.Height > 0;
                if (frame == null)
                {
                    // Live screen: dim everything except the selection.
                    Geometry mask = has ? new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(size)), new RectangleGeometry(selection)) : new RectangleGeometry(new Rect(size));
                    dc.DrawGeometry(Dim, null, mask);
                    // A fully transparent pixel of a layered window lets the click fall through to the desktop; this keeps the opening clickable.
                    if (has) dc.DrawRectangle(HitFill, null, selection);
                }
                else if (has)
                {
                    dc.PushClip(new RectangleGeometry(selection)); dc.DrawImage(frame, new Rect(size)); dc.Pop();
                }
                if (has)
                {
                    var accent = Application.Current?.TryFindResource("Accent") as Brush ?? Brushes.DodgerBlue;
                    dc.DrawRectangle(null, Outline, selection);
                    dc.DrawRectangle(null, owner.IsSuggestion ? new Pen(accent, 2) : White, selection);
                    if (!owner.IsSuggestion) DrawBrackets(dc, selection);
                    DrawSize(dc, selection, size);
                }
                if (frame != null && owner.Pointer is Point pointer) DrawLoupe(dc, frame, pointer, size);
            }

            private static void DrawBrackets(DrawingContext dc, Rect r)
            {
                double arm = Math.Min(14, Math.Min(r.Width, r.Height) / 3);
                if (arm < 4) return;
                foreach (var (corner, dx, dy) in new[] { (r.TopLeft, 1, 1), (r.TopRight, -1, 1), (r.BottomLeft, 1, -1), (r.BottomRight, -1, -1) })
                {
                    dc.DrawLine(Bracket, corner, new Point(corner.X + dx * arm, corner.Y));
                    dc.DrawLine(Bracket, corner, new Point(corner.X, corner.Y + dy * arm));
                }
            }

            private void DrawSize(DrawingContext dc, Rect r, Size surface)
            {
                int width = Math.Max(1, (int)Math.Round(r.Width * owner.PixelScale.X)), height = Math.Max(1, (int)Math.Round(r.Height * owner.PixelScale.Y));
                var text = new FormattedText($"{width} × {height}", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 12, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                var plate = new Size(text.Width + 16, text.Height + 8);
                // Below the selection when there is room, otherwise inside its lower edge; always fully on the surface.
                double y = r.Bottom + 8 + plate.Height <= surface.Height ? r.Bottom + 8 : Math.Max(0, r.Bottom - plate.Height - 8);
                // Centred, so it stays clear of the loupe that follows the pointer at a corner.
                double x = Math.Clamp(r.X + (r.Width - plate.Width) / 2, 0, Math.Max(0, surface.Width - plate.Width));
                dc.DrawRoundedRectangle(Plate, PlateRim, new Rect(new Point(x, y), plate), 6, 6);
                dc.DrawText(text, new Point(x + 8, y + 4));
            }

            private readonly byte[] loupePixels = new byte[15 * 15 * 4];
            private readonly Dictionary<Color, Brush> loupeBrushes = [];
            private BitmapSource? loupeSource; private BitmapSource? loupeBgra;
            private bool TryReadPixels(BitmapSource frame, Int32Rect area)
            {
                try
                {
                    if (!ReferenceEquals(loupeSource, frame))
                    {
                        // Converted once per frame; captures are already BGRA so this is normally the frame itself.
                        loupeSource = frame;
                        loupeBgra = frame.Format == PixelFormats.Bgra32 || frame.Format == PixelFormats.Bgr32 || frame.Format == PixelFormats.Pbgra32 ? frame : Frozen(new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0));
                    }
                    loupeBgra!.CopyPixels(area, loupePixels, area.Width * 4, 0); return true;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException) { return false; }
            }

            /// <summary>An 8x view of the pixels under the pointer, so an edge can be placed exactly.</summary>
            private void DrawLoupe(DrawingContext dc, BitmapSource frame, Point pointer, Size surface)
            {
                if (surface.Width <= 0 || surface.Height <= 0) return;
                const int cells = 15; const double side = 120, cell = side / cells;
                double px = pointer.X * frame.PixelWidth / surface.Width, py = pointer.Y * frame.PixelHeight / surface.Height;
                int cx = Math.Clamp((int)Math.Floor(px), 0, frame.PixelWidth - 1), cy = Math.Clamp((int)Math.Floor(py), 0, frame.PixelHeight - 1);
                // Beside the pointer, flipping to the other side near the right and bottom edges.
                double x = pointer.X + 22 + side > surface.Width ? pointer.X - 22 - side : pointer.X + 22;
                double y = pointer.Y + 22 + side + 26 > surface.Height ? pointer.Y - 22 - side - 26 : pointer.Y + 22;
                var box = new Rect(Math.Max(0, x), Math.Max(0, y), side, side);
                var shape = new RectangleGeometry(new Rect(box.X, box.Y, side, side + 26), 10, 10);
                dc.DrawGeometry(Plate, null, shape);
                dc.PushClip(new RectangleGeometry(box, 10, 10)); dc.DrawRectangle(Brushes.Black, null, box);
                // Each source pixel is drawn as its own square: exact colours and hard edges, whatever the renderer's image filtering.
                int left = cx - cells / 2, top = cy - cells / 2;
                int x0 = Math.Max(0, left), y0 = Math.Max(0, top), x1 = Math.Min(frame.PixelWidth, left + cells), y1 = Math.Min(frame.PixelHeight, top + cells);
                if (x1 > x0 && y1 > y0 && TryReadPixels(frame, new Int32Rect(x0, y0, x1 - x0, y1 - y0)))
                {
                    int stride = (x1 - x0) * 4;
                    for (int row = y0; row < y1; row++)
                        for (int column = x0; column < x1; column++)
                        {
                            int o = (row - y0) * stride + (column - x0) * 4;
                            var color = Color.FromRgb(loupePixels[o + 2], loupePixels[o + 1], loupePixels[o]);
                            if (!loupeBrushes.TryGetValue(color, out var fill))
                            {
                                if (loupeBrushes.Count > 512) loupeBrushes.Clear();
                                loupeBrushes[color] = fill = Frozen(new SolidColorBrush(color));
                            }
                            // Squares overlap by a hair so anti-aliasing leaves no seams between them.
                            dc.DrawRectangle(fill, null, new Rect(box.X + (column - left) * cell, box.Y + (row - top) * cell, cell + .5, cell + .5));
                        }
                }
                var centre = new Rect(box.X + (cells / 2) * cell, box.Y + (cells / 2) * cell, cell, cell);
                dc.DrawLine(LoupeGrid, new Point(centre.X + cell / 2, box.Y), new Point(centre.X + cell / 2, box.Bottom));
                dc.DrawLine(LoupeGrid, new Point(box.X, centre.Y + cell / 2), new Point(box.Right, centre.Y + cell / 2));
                dc.DrawRectangle(null, Outline, centre); dc.DrawRectangle(null, White, centre);
                dc.Pop();
                dc.DrawGeometry(null, PlateRim, shape);
                var label = new FormattedText($"{cx}, {cy}", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 11.5, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(label, new Point(box.X + (side - label.Width) / 2, box.Bottom + (26 - label.Height) / 2));
            }
        }
    }
}
