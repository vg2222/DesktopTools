using DesktopTools.Localization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using DesktopTools.Core;
using DesktopTools.Native;

namespace DesktopTools.Extras;

/// <summary>A capturable, non-activating visual effect. It never receives ordinary input.</summary>
public sealed class PresentationEffectWindow : Window
{
    private readonly EffectCanvas _canvas;
    private readonly MonitorInfo _monitor;
    private bool _rendering;
    private TimeSpan _lastFrame = TimeSpan.MinValue;
    public MonitorInfo ActiveMonitor => _monitor;

    public PresentationEffectWindow(MonitorInfo monitor, string mode, AppSettings settings)
    {
        if (mode is not ("Laser" or "Spotlight")) throw new ArgumentException(L.T("Unknown presentation effect."), nameof(mode));
        _monitor = monitor;
        Title = "DesktopTools " + L.T(mode);
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; Focusable = false;
        Width = monitor.Bounds.Width / monitor.ScaleX; Height = monitor.Bounds.Height / monitor.ScaleY;
        _canvas = new EffectCanvas(mode, settings); Content = _canvas;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        IsVisibleChanged += OnVisibilityChanged;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs args) => NativeWindowService.ConfigureOverlay(this, true);
    private void OnLoaded(object sender, RoutedEventArgs args) { MonitorService.PlaceWindow(this, _monitor); StartFrames(); }
    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (IsVisible && IsLoaded) StartFrames(); else { StopFrames(); _canvas.Reset(); }
    }
    private void StartFrames()
    {
        if (_rendering || !IsVisible) return;
        _rendering = true; _lastFrame = TimeSpan.MinValue;
        UpdateCursor();
        CompositionTarget.Rendering += OnFrame;
    }
    private void StopFrames()
    {
        if (!_rendering) return;
        CompositionTarget.Rendering -= OnFrame; _rendering = false;
    }
    private void OnFrame(object? sender, EventArgs args)
    {
        // WPF can notify more than once for one composition frame.
        if (args is RenderingEventArgs frame)
        {
            if (frame.RenderingTime == _lastFrame) return;
            _lastFrame = frame.RenderingTime;
        }
        UpdateCursor();
    }
    private void UpdateCursor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor)) { _canvas.Reset(); return; }
        var physical = new Point(cursor.X, cursor.Y);
        _canvas.Update(_monitor.Bounds.Contains(physical) ? MonitorService.PhysicalToLocal(physical, _monitor) : null);
    }
    private void OnClosed(object? sender, EventArgs args)
    {
        StopFrames();
        SourceInitialized -= OnSourceInitialized; Loaded -= OnLoaded; IsVisibleChanged -= OnVisibilityChanged; Closed -= OnClosed;
        _canvas.Reset(); Content = null;
    }

    private sealed class EffectCanvas : FrameworkElement
    {
        private readonly bool _spotlight;
        private readonly List<(Point Point, double Time)> _trail = [];
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly RectangleGeometry _area = new();
        private readonly EllipseGeometry _opening;
        private readonly GeometryGroup _mask;
        private readonly SolidColorBrush _dimBrush;
        private readonly SolidColorBrush _laserBrush;
        private readonly Pen _laserPen;
        private double _lastMotion;
        private readonly double _fade;
        private readonly double _laserRadius;
        private Point? _cursor;
        public EffectCanvas(string mode, AppSettings settings)
        {
            _spotlight = mode == "Spotlight";
            ClipToBounds = true;
            double radius = Number(settings.SpotlightRadius, 20, 1000, 120);
            _opening = new EllipseGeometry(default(Point), radius, radius);
            // EvenOdd retains the exact circular hole without recomputing a boolean path.
            // ClipToBounds removes any ellipse portion extending outside the monitor rectangle.
            _mask = new GeometryGroup { FillRule = FillRule.EvenOdd };
            _mask.Children.Add(_area); _mask.Children.Add(_opening);
            _dimBrush = new SolidColorBrush(Color.FromArgb((byte)(255 * Number(settings.SpotlightDim, .1, .95, .65)), 0, 0, 0));
            _dimBrush.Freeze();
            Color color;
            try { color = (Color)ColorConverter.ConvertFromString(settings.LaserColor); }
            catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException) { color = Colors.Red; }
            _laserBrush = new SolidColorBrush(color); _laserBrush.Freeze();
            _laserRadius = Number(settings.LaserSize, 2, 100, 12) / 2;
            _fade = Number(settings.LaserFadeSeconds, .1, 5, .7);
            _laserPen = new Pen(_laserBrush, _laserRadius * 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; _laserPen.Freeze();

        }
        private static double Number(double value, double minimum, double maximum, double fallback) => double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            _area.Rect = new Rect(RenderSize);
        }
        public void Update(Point? point)
        {
            if (_spotlight)
            {
                if (_cursor == point) return;
                bool visibilityChanged = _cursor.HasValue != point.HasValue;
                _cursor = point;
                if (point.HasValue) _opening.Center = point.Value;
                // Center changes propagate through the retained Freezable geometry to WPF.
                // Only entering/leaving the monitor needs the drawing commands rebuilt.
                if (visibilityChanged) InvalidateVisual();
                return;
            }
            double now = _clock.Elapsed.TotalSeconds;
            bool changed = _cursor != point;
            bool hadTrail = _trail.Count != 0;
            if (point.HasValue && (!_cursor.HasValue || (point.Value - _cursor.Value).Length > .5)) _trail.Add((point.Value, now));
            if (changed && point.HasValue) _lastMotion = now;
            int expired = 0;
            while (expired < _trail.Count && now - _trail[expired].Time >= _fade) expired++;
            if (expired != 0) _trail.RemoveRange(0, expired);
            _cursor = point;
            if (changed || hadTrail || _trail.Count != 0) InvalidateVisual();
        }
        public void Reset()
        {
            if (_cursor == null && _trail.Count == 0) return;
            _trail.Clear(); _cursor = null; InvalidateVisual();
        }
        protected override void OnRender(DrawingContext dc)
        {
            if (_spotlight)
            {
                if (_cursor.HasValue) dc.DrawGeometry(_dimBrush, null, _mask);
                return;
            }
            double opacity = .5 * Math.Clamp(1 - (_clock.Elapsed.TotalSeconds - _lastMotion) / _fade, 0, 1);
            if (_trail.Count == 0 || opacity <= 0) return;
            // One stroked geometry and one opacity operation prevent bright joins and point halos.
            var path = new StreamGeometry();
            using (var context = path.Open())
            {
                context.BeginFigure(_trail[0].Point, false, false);
                for (int i = 1; i < _trail.Count; i++) context.LineTo(_trail[i].Point, true, true);
                if (_trail.Count == 1) context.LineTo(_trail[0].Point + new Vector(.01, 0), true, true);
            }
            path.Freeze();
            dc.PushOpacity(opacity);
            dc.DrawGeometry(null, _laserPen, path);
            dc.Pop();
        }
    }
}
