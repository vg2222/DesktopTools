using DesktopTools.Localization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopTools.UI;

internal sealed class ColorSpectrum : FrameworkElement
{
    private double hue, saturation = 1, value = 1;
    public event Action<Color>? Changed;
    public double Hue { get => hue; set { hue = value; InvalidateVisual(); Changed?.Invoke(SelectedColor); } }
    public Color SelectedColor => FromHsv(hue, saturation, value);
    public ColorSpectrum()
    {
        Height = 150; Focusable = true; Cursor = Cursors.Cross;
        System.Windows.Automation.AutomationProperties.SetName(this, L.T("Color saturation and brightness. Arrow keys adjust."));
        MouseLeftButtonDown += (_, e) => { Focus(); CaptureMouse(); Choose(e.GetPosition(this)); e.Handled = true; };
        MouseMove += (_, e) => { if (IsMouseCaptured) Choose(e.GetPosition(this)); };
        MouseLeftButtonUp += (_, _) => ReleaseMouseCapture();
        KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down)) return;
            saturation = Math.Clamp(saturation + (e.Key == Key.Right ? .01 : e.Key == Key.Left ? -.01 : 0), 0, 1);
            value = Math.Clamp(value + (e.Key == Key.Up ? .01 : e.Key == Key.Down ? -.01 : 0), 0, 1);
            InvalidateVisual(); Changed?.Invoke(SelectedColor); e.Handled = true;
        };
    }
    public void SetColor(Color color)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        value = max; saturation = max == 0 ? 0 : delta / max;
        if (delta > 0) hue = ((max == r ? (g - b) / delta : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4) * 60 + 360) % 360;
        InvalidateVisual();
    }
    private void Choose(Point point)
    {
        saturation = Math.Clamp(point.X / Math.Max(1, ActualWidth), 0, 1);
        value = 1 - Math.Clamp(point.Y / Math.Max(1, ActualHeight), 0, 1);
        InvalidateVisual(); Changed?.Invoke(SelectedColor);
    }
    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(RenderSize);
        dc.PushClip(new RectangleGeometry(rect, 9, 9));
        dc.DrawRectangle(new LinearGradientBrush(Colors.White, FromHsv(hue, 1, 1), 0), null, rect);
        dc.DrawRectangle(new LinearGradientBrush(Colors.Transparent, Colors.Black, 90), null, rect);
        var point = new Point(saturation * ActualWidth, (1 - value) * ActualHeight);
        dc.DrawEllipse(null, new Pen(Brushes.Black, 3), point, 5, 5);
        dc.DrawEllipse(null, new Pen(Brushes.White, 1.5), point, 5, 5); dc.Pop();
    }
    internal static Color FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h switch { < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x), < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x) };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
