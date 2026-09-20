using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Native;
using DesktopTools.UI;

namespace DesktopTools.Extras;

internal sealed record ScreenColorSample(Color Color, BitmapSource Pixels);

internal sealed class ScreenEyedropperWindow : Window
{
    private readonly MonitorInfo monitor;
    private readonly Image zoom = new() { Width = 104, Height = 104, Stretch = Stretch.Fill };
    internal ScreenColorSample? CurrentSample { get; private set; }
    private readonly BitmapSource source;
    private readonly Border swatch = new() { Width = 28, Height = 28, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 10, 0) };
    private readonly TextBlock label = Ui.Text(L.T("Move to sample • Click to choose • Esc to cancel"), 13, true);
    public ScreenEyedropperWindow(MonitorInfo monitor, BitmapSource image, Action<Color?> complete)
    {
        this.monitor = monitor;
        source = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0); source.Freeze();
        Title = L.T("DesktopTools color sampler"); WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; Cursor = Cursors.Cross;
        Width = monitor.Bounds.Width / monitor.ScaleX; Height = monitor.Bounds.Height / monitor.ScaleY;
        var canvas = new Grid(); canvas.Children.Add(new Image { Source = source, Stretch = Stretch.Fill });
        var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(swatch); row.Children.Add(PixelZoom(zoom)); row.Children.Add(label);
        var card = Ui.Card(row, 12); card.Margin = new Thickness(18); card.HorizontalAlignment = HorizontalAlignment.Left; card.VerticalAlignment = VerticalAlignment.Top; card.IsHitTestVisible = false; canvas.Children.Add(card); Content = canvas;
        SourceInitialized += (_, _) => { NativeWindowService.ConfigureOverlay(this, false); MonitorService.PlaceWindow(this, monitor); };
        MouseMove += (_, _) => { var color = SamplePointer(); if (color.HasValue) { swatch.Background = new SolidColorBrush(color.Value); label.Text = $"#{color.Value.R:X2}{color.Value.G:X2}{color.Value.B:X2}   RGB({color.Value.R}, {color.Value.G}, {color.Value.B})   ·   " + L.T("Click to choose"); } };
        MouseLeftButtonDown += (_, e) => { complete(SamplePointer()); e.Handled = true; };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; complete(null); } };
        MouseRightButtonDown += (_, _) => complete(null);
    }
    private Color? SamplePointer()
    {
        if (!NativeMethods.GetCursorPos(out var point)) return null;
        int x = point.X - (int)monitor.Bounds.Left, y = point.Y - (int)monitor.Bounds.Top;
        if (x < 0 || y < 0 || x >= source.PixelWidth || y >= source.PixelHeight) return null;
        var color = ScreenshotPixel.Read(source, x, y);
        var pixels = Magnify(source, x, y); zoom.Source = pixels; CurrentSample = new(color, pixels); return color;
    }
    internal static BitmapSource Magnify(BitmapSource source, int x, int y)
    {
        if (x < 0 || y < 0 || x >= source.PixelWidth || y >= source.PixelHeight) throw new ArgumentOutOfRangeException(nameof(x));
        if (source.Format != PixelFormats.Bgra32) source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int left = Math.Max(0, x - 6), top = Math.Max(0, y - 6), right = Math.Min(source.PixelWidth, x + 7), bottom = Math.Min(source.PixelHeight, y + 7);
        int width = right - left, height = bottom - top;
        byte[] crop = new byte[width * height * 4], pixels = new byte[13 * 13 * 4];
        source.CopyPixels(new Int32Rect(left, top, width, height), crop, width * 4, 0);
        for (int dy = 0; dy < 13; dy++) for (int dx = 0; dx < 13; dx++)
        {
            int sx = Math.Clamp(x + dx - 6 - left, 0, width - 1), sy = Math.Clamp(y + dy - 6 - top, 0, height - 1);
            Buffer.BlockCopy(crop, (sy * width + sx) * 4, pixels, (dy * 13 + dx) * 4, 4);
        }
        var bitmap = BitmapSource.Create(13, 13, 96, 96, PixelFormats.Bgra32, null, pixels, 52); bitmap.Freeze(); return bitmap;
    }
    internal static Grid PixelZoom(Image image)
    {
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var panel = new Grid { Margin = new Thickness(0, 0, 12, 0), IsHitTestVisible = false }; panel.Children.Add(image);
        panel.Children.Add(new Border { Width = image.Width / 13 + 2, Height = image.Height / 13 + 2, BorderThickness = new Thickness(1), BorderBrush = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(new Border { Width = image.Width / 13, Height = image.Height / 13, BorderThickness = new Thickness(1), BorderBrush = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }
    public static async Task<Color?> PickAsync(CancellationToken cancellation) => (await PickSampleAsync(cancellation))?.Color;
    internal static async Task<ScreenColorSample?> PickSampleAsync(CancellationToken cancellation)
    {
        var monitors = MonitorService.GetAll();
        if (monitors.Sum(m => m.Bounds.Width * m.Bounds.Height) > 80_000_000) throw new InvalidOperationException(L.T("The desktop is too large to sample safely."));
        var images = new List<BitmapSource>();
        using (new HiddenWindowsScope())
        {
            await Task.Delay(60, cancellation); NativeWindowService.SynchronizeDesktop();
            foreach (var monitor in monitors) { cancellation.ThrowIfCancellationRequested(); images.Add(CaptureService.Capture(monitor)); }
        }
        var result = new TaskCompletionSource<ScreenColorSample?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var windows = new List<ScreenEyedropperWindow>();
        using var registration = cancellation.Register(() => result.TrySetCanceled(cancellation));
        try
        {
            for (int i = 0; i < monitors.Count; i++)
            {
                ScreenEyedropperWindow? window = null;
                window = new ScreenEyedropperWindow(monitors[i], images[i], c => result.TrySetResult(c.HasValue ? window!.CurrentSample : null));
                window.Closed += (_, _) => result.TrySetResult(null); windows.Add(window); window.Show();
            }
            windows.FirstOrDefault(w => w.monitor.Bounds.Contains(new Point(System.Windows.Forms.Cursor.Position.X, System.Windows.Forms.Cursor.Position.Y)))?.Activate();
            return await result.Task;
        }
        finally { foreach (var window in windows) window.Close(); images.Clear(); }
    }
}

internal sealed class SampledColorWindow : Window
{
    public SampledColorWindow(Color color, Action<string> report, BitmapSource? pixels = null, Action? pickAgain = null)
    {
        string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}", rgb = $"rgb({color.R}, {color.G}, {color.B})";
        Title = L.T("Sampled color"); Topmost = true; Width = 350; SizeToContent = SizeToContent.Height; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel(); panel.Children.Add(UtilityWindowChrome.Header(this, L.T("Sampled color"), Close, L.T("Close"), 16));
        var samples = new Grid { Margin = new Thickness(0, 8, 0, 14) }; samples.ColumnDefinitions.Add(new ColumnDefinition()); samples.ColumnDefinitions.Add(new ColumnDefinition());
        samples.Children.Add(new Border { Background = new SolidColorBrush(color), Height = 104, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 12, 0) });
        if (pixels != null) { var magnifier = ScreenEyedropperWindow.PixelZoom(new Image { Source = pixels, Width = 104, Height = 104, Stretch = Stretch.Fill }); Grid.SetColumn(magnifier, 1); samples.Children.Add(magnifier); }
        panel.Children.Add(samples);
        void Copy(string value) { try { Clipboard.SetText(value); report(L.T("Color copied.")); } catch (Exception ex) { report(L.T("Could not copy color: ") + ex.Message); } }
        void ColorValue(string label, string value)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) }; var name = Ui.Text(label, 12); name.Width = 42; DockPanel.SetDock(name, Dock.Left); row.Children.Add(name);
            var field = new DockPanel(); var copy = Ui.IconButton("Copy", L.T(label == "HEX" ? "Copy HEX" : "Copy RGB"), () => Copy(value)); DockPanel.SetDock(copy, Dock.Right); field.Children.Add(copy);
            var text = new TextBox { Text = value, IsReadOnly = true, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(10, 6, 0, 6), MinHeight = 34, FontSize = 13 }; System.Windows.Automation.AutomationProperties.SetName(text, label); field.Children.Add(text);
            var frame = new Border { Child = field, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) }; frame.SetResourceReference(Border.BackgroundProperty, "Field"); frame.SetResourceReference(Border.BorderBrushProperty, "Stroke"); row.Children.Add(frame); panel.Children.Add(row);
        }
        ColorValue("HEX", hex); ColorValue("RGB", rgb);
        if (pickAgain != null) { var pick = Ui.Button(L.T("Pick from screen"), () => { Close(); pickAgain(); }, true); pick.Content = Ui.IconLabel("Eyedropper", L.T("Pick from screen"), primary: true); pick.Margin = new Thickness(0, 8, 0, 0); panel.Children.Add(pick); }
        var card = Ui.Card(panel); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); card.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); Content = card; Motion.WindowEntrance(this);
    }
}
