using DesktopTools.Localization;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopTools.UI;
using DesktopTools.Native;

namespace DesktopTools.Extras;

public sealed class ScreenRulerWindow : Window
{
    private readonly RulerMarks marks = new();
    private readonly Border strip;
    private readonly TextBlock label;
    private readonly Slider slider;
    private bool refreshing;
    private double length = 600;
    private bool vertical;
    public double Length { get => length; set { length = double.IsFinite(value) ? Math.Clamp(value, 100, 1600) : 600; Refresh(); } }
    public bool IsVertical { get => vertical; set { vertical = value; Refresh(); } }
    public ScreenRulerWindow()
    {
        Title = L.T("DesktopTools Ruler"); WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        Motion.WindowEntrance(this);
        var stack = new StackPanel();
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(Ui.Button(L.T("Rotate"), () => IsVertical = !IsVertical));
        controls.Children.Add(Ui.Button(L.T("Close"), Close)); stack.Children.Add(controls);
        label = Ui.Text("", 12); label.Margin = new Thickness(0, 8, 0, 6); stack.Children.Add(label);
        slider = new Slider { Minimum = 100, Maximum = 1600, TickFrequency = 10, IsSnapToTickEnabled = true, Value = 600, ToolTip = L.T("Ruler length in logical pixels") };
        slider.ValueChanged += (_, _) => Length = slider.Value; stack.Children.Add(slider);
        strip = new Border { Background = new SolidColorBrush(Color.FromRgb(255, 226, 137)), Child = marks, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        strip.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) { DragMove(); Refresh(); } };
        stack.Children.Add(strip);
        var card = Ui.Card(stack, 12); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "Surface"); Content = card;
        Refresh();
    }
    private void Refresh()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
        nint handle = new WindowInteropHelper(this).Handle;
        var monitor = MonitorService.GetCurrent();
        if (handle != 0 && GetWindowRect(handle, out var bounds))
            monitor = MonitorService.GetAll().FirstOrDefault(m => m.Bounds.Contains(new Point(bounds.Left, bounds.Top))) ?? monitor;
        double maximum = Math.Max(100, Math.Min(1600, vertical
            ? monitor.WorkingArea.Height / monitor.ScaleY - 234
            : monitor.WorkingArea.Width / monitor.ScaleX - 104));
        length = Math.Min(length, maximum);
        slider.Maximum = maximum; slider.Value = length;
        marks.Vertical = vertical;
        strip.Width = vertical ? 60 : length; strip.Height = vertical ? length : 60;
        Width = Math.Max(260, strip.Width + 26); Height = strip.Height + 126;
        label.Text = L.F($"{length:0} logical pixels · drag ruler to move");
        marks.InvalidateVisual();
        if (handle != 0 && GetWindowRect(handle, out var position))
        {
            int x = (int)Math.Clamp(position.Left, monitor.WorkingArea.Left, Math.Max(monitor.WorkingArea.Left, monitor.WorkingArea.Right - Width * monitor.ScaleX));
            int y = (int)Math.Clamp(position.Top, monitor.WorkingArea.Top, Math.Max(monitor.WorkingArea.Top, monitor.WorkingArea.Bottom - Height * monitor.ScaleY));
            NativeMethods.SetWindowPos(handle, 0, x, y, 0, 0, 0x01 | 0x04 | 0x10);
        }
        }
        finally { refreshing = false; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct WindowBounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle, out WindowBounds bounds);
    private sealed class RulerMarks : FrameworkElement
    {
        public bool Vertical { get; set; }
        protected override void OnRender(DrawingContext dc)
        {
            var pen = new Pen(Brushes.Black, 1);
            double extent = Vertical ? ActualHeight : ActualWidth;
            for (int position = 0; position <= extent; position += 10)
            {
                int depth = position % 100 == 0 ? 24 : position % 50 == 0 ? 17 : 10;
                dc.DrawLine(pen, Vertical ? new Point(0, position) : new Point(position, 0), Vertical ? new Point(depth, position) : new Point(position, depth));
                if (position % 100 == 0)
                {
                    var text = new FormattedText(position.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    dc.DrawText(text, Vertical ? new Point(26, Math.Min(position + 2, Math.Max(0, extent - 14))) : new Point(Math.Min(position + 2, Math.Max(0, extent - text.Width)), 29));
                }
            }
        }
    }
}
