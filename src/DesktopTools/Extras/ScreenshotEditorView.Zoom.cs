using DesktopTools.Localization;
using DesktopTools.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopTools.Extras;

/// <summary>
/// Zoom and pan for the editing canvas. The wheel zooms at the pointer, Shift+wheel moves sideways, Ctrl+wheel moves up and down,
/// Alt+wheel changes the stroke thickness, and dragging with the middle button pans. Ctrl+0 (or the zoom badge) fits the picture again.
/// Only the view is transformed: pointer positions still map to image pixels through WPF, so drawing works at any zoom.
/// </summary>
public sealed partial class ScreenshotEditorView
{
    private const double MaxZoom = 16, WheelStep = 1.2, PanStep = 80;
    private Viewbox _viewbox = null!;
    private Grid _viewport = null!;
    private Button _zoomBadge = null!;
    private Slider _widthChoice = null!;
    private double _zoom = 1, _panX, _panY;
    private Point? _panStart;
    private (double X, double Y) _panOrigin;

    internal double ZoomLevel => _zoom;

    /// <summary>Wraps the fitted picture in a clipped viewport with a zoom badge, and hooks wheel, middle-drag and size changes.</summary>
    private Grid CreateViewport(Viewbox viewbox)
    {
        _viewbox = viewbox;
        viewbox.RenderTransform = new MatrixTransform();
        _viewport = new Grid { ClipToBounds = true, Background = Brushes.Transparent };
        _viewport.Children.Add(viewbox);
        _zoomBadge = Ui.Button("100%", () => SetZoom(1, null));
        _zoomBadge.Name = "ZoomBadge"; _zoomBadge.MinHeight = 28; _zoomBadge.Padding = new Thickness(10, 3, 10, 3); _zoomBadge.FontSize = 12;
        _zoomBadge.HorizontalAlignment = HorizontalAlignment.Right; _zoomBadge.VerticalAlignment = VerticalAlignment.Top; _zoomBadge.Margin = new Thickness(0, 10, 10, 0);
        _zoomBadge.Visibility = Visibility.Collapsed;
        Ui.Tip(_zoomBadge, L.T("Fit the picture (Ctrl+0)"));
        _viewport.Children.Add(_zoomBadge);
        Ui.Tip(_viewport, L.T("Scroll to zoom. Shift+scroll moves sideways, Ctrl+scroll moves up and down, Alt+scroll changes the thickness. Drag with the middle button to move around."));
        ToolTipService.SetInitialShowDelay(_viewport, 1200);
        _viewport.PreviewMouseWheel += OnViewportWheel;
        _viewport.PreviewMouseDown += OnViewportMouseDown;
        _viewport.PreviewMouseMove += OnViewportMouseMove;
        _viewport.PreviewMouseUp += OnViewportMouseUp;
        _viewport.LostMouseCapture += (_, _) => EndPan();
        _viewport.SizeChanged += (_, _) => ApplyView();
        return _viewport;
    }

    private void OnViewportWheel(object sender, MouseWheelEventArgs e)
    {
        if (_textView != null || e.Delta == 0) return;
        double notches = e.Delta / 120.0;
        switch (Keyboard.Modifiers)
        {
            case ModifierKeys.Shift: Pan(notches * PanStep, 0); break;
            case ModifierKeys.Control: Pan(0, notches * PanStep); break;
            case ModifierKeys.Alt:
                _widthChoice.Value = Math.Clamp(Math.Round(_widthChoice.Value + Math.Sign(notches)), _widthChoice.Minimum, _widthChoice.Maximum);
                _status.Text = L.F($"Stroke thickness: {_widthChoice.Value:0} px");
                break;
            case ModifierKeys.None: SetZoom(_zoom * Math.Pow(WheelStep, notches), e.GetPosition(_viewport)); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || _textView != null) return;
        _panStart = e.GetPosition(_viewport); _panOrigin = (_panX, _panY);
        _viewport.CaptureMouse(); _viewport.Cursor = Cursors.SizeAll; e.Handled = true;
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        if (_panStart is not { } start) return;
        var now = e.GetPosition(_viewport);
        _panX = _panOrigin.X + now.X - start.X; _panY = _panOrigin.Y + now.Y - start.Y;
        ApplyView(); e.Handled = true;
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || _panStart == null) return;
        EndPan(); e.Handled = true;
    }

    private void EndPan()
    {
        if (_panStart == null) return;
        _panStart = null; _viewport.Cursor = null;
        if (_viewport.IsMouseCaptured) _viewport.ReleaseMouseCapture();
    }

    private void Pan(double dx, double dy)
    {
        if (_zoom <= 1) return;
        _panX += dx; _panY += dy; ApplyView();
    }

    /// <summary>Zooms to <paramref name="zoom"/> keeping the picture point under <paramref name="anchor"/> (viewport coordinates) in place; null zooms at the centre.</summary>
    internal void SetZoom(double zoom, Point? anchor)
    {
        zoom = Math.Clamp(zoom, 1, MaxZoom);
        if (Math.Abs(zoom - 1) < 0.01) zoom = 1;
        var offset = ViewboxOffset();
        var point = anchor ?? new Point(_viewport.ActualWidth / 2, _viewport.ActualHeight / 2);
        // A viewport point P shows the local point x = (P - offset - pan) / zoom; keep that x under P after the change.
        double localX = (point.X - offset.X - _panX) / _zoom, localY = (point.Y - offset.Y - _panY) / _zoom;
        _zoom = zoom;
        _panX = point.X - offset.X - zoom * localX; _panY = point.Y - offset.Y - zoom * localY;
        ApplyView();
    }

    private Vector ViewboxOffset() => _viewbox.IsArrangeValid ? VisualTreeHelper.GetOffset(_viewbox) : new Vector();

    /// <summary>Keeps the zoomed picture covering the viewport (centred along an axis where it is still smaller) and updates the badge.</summary>
    private void ApplyView()
    {
        if (_viewbox?.RenderTransform is not MatrixTransform transform) return;
        var offset = ViewboxOffset();
        double width = _viewbox.ActualWidth * _zoom, height = _viewbox.ActualHeight * _zoom;
        double Clamp(double pan, double size, double viewport, double start) =>
            size <= viewport ? (viewport - size) / 2 - start : Math.Clamp(pan, viewport - size - start, -start);
        if (_zoom == 1) { _panX = 0; _panY = 0; }
        else { _panX = Clamp(_panX, width, _viewport.ActualWidth, offset.X); _panY = Clamp(_panY, height, _viewport.ActualHeight, offset.Y); }
        transform.Matrix = new Matrix(_zoom, 0, 0, _zoom, _panX, _panY);
        // Past 2x, show real pixels instead of a blur so details can be checked.
        RenderOptions.SetBitmapScalingMode(_viewbox, _zoom >= 2 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        _zoomBadge.Content = $"{_zoom * 100:0}%";
        System.Windows.Automation.AutomationProperties.SetName(_zoomBadge, L.F($"Zoom {_zoom * 100:0}%. Fit the picture"));
        _zoomBadge.Visibility = _zoom > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Ctrl+0 fits the picture; Ctrl+plus and Ctrl+minus zoom at the centre. Returns true when the key was a zoom key.</summary>
    private bool HandleZoomKey(KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return false;
        switch (e.Key)
        {
            case Key.D0 or Key.NumPad0: SetZoom(1, null); return true;
            case Key.OemPlus or Key.Add: SetZoom(_zoom * WheelStep, null); return true;
            case Key.OemMinus or Key.Subtract: SetZoom(_zoom / WheelStep, null); return true;
            default: return false;
        }
    }
}
