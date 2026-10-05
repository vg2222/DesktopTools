using DesktopTools.Localization;
using DesktopTools.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopTools.Extras;

public sealed partial class ScreenshotEditorView
{
    private BeautifyOptions _beautify = new();
    private Image _stylePreview = null!;
    private System.Windows.Threading.DispatcherTimer? _previewTimer;

    // The canvas itself shows the presentation style while you work, so you draw on exactly what Copy and Save will produce.
    private Border _styleFrame = null!, _styleCard = null!, _styleBar = null!;
    private Grid _styleShadows = null!, _styleStack = null!;
    private ScanOverlay? _scanOverlay;

    private FrameworkElement BuildStyleFrame(Canvas surface, int width, int height)
    {
        _styleShadows = new Grid { IsHitTestVisible = false };
        _styleBar = new Border { Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2F, 0x38)), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        var cardGrid = new Grid();
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); cardGrid.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(_styleBar, 0); Grid.SetRow(surface, 1); cardGrid.Children.Add(_styleBar); cardGrid.Children.Add(surface);
        _styleCard = new Border { Child = cardGrid, Width = width, Height = height };
        _styleStack = new Grid { Width = width, Height = height };
        _styleStack.Children.Add(_styleShadows); _styleStack.Children.Add(_styleCard);
        _scanOverlay = new ScanOverlay(); _styleStack.Children.Add(_scanOverlay);
        _styleFrame = new Border { Child = _styleStack };
        return _styleFrame;
    }

    // "Check screenshot" visual: the rainbow rim hugs the (optionally styled) picture's rounded corners.
    private void StartScanEffect()
    {
        double width = _image.PixelWidth, height = _image.PixelHeight + (_beautify.Enabled ? ScreenshotBeautifier.Measure(_image.PixelWidth, _image.PixelHeight, _beautify).Bar : 0);
        double radius = _beautify.Enabled ? Math.Max(ScreenshotBeautifier.Measure(_image.PixelWidth, _image.PixelHeight, _beautify).Radius, 6) : Math.Max(8, Math.Min(width, height) * 0.015);
        _scanOverlay?.Start(width, height, radius);
    }
    private void StopScanEffect() => _scanOverlay?.Stop();

    private void ApplyLiveStyle()
    {
        if (_styleFrame == null) return;
        int width = _image.PixelWidth, height = _image.PixelHeight;
        _styleShadows.Children.Clear();
        if (!_beautify.Enabled)
        {
            _styleFrame.Background = null; _styleFrame.Padding = new Thickness(0); _styleBar.Visibility = Visibility.Collapsed; _styleCard.Clip = null;
            _styleCard.Height = _styleStack.Height = height; return;
        }
        var m = ScreenshotBeautifier.Measure(width, height, _beautify);
        _styleFrame.Background = ScreenshotBeautifier.BackgroundBrush(_beautify.Background); _styleFrame.Padding = new Thickness(m.Margin);
        double total = height + m.Bar;
        _styleCard.Height = _styleStack.Height = total;
        _styleCard.Clip = new RectangleGeometry(new Rect(0, 0, width, total), m.Radius, m.Radius);
        _styleBar.Height = m.Bar; _styleBar.Visibility = m.Bar > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (m.Bar > 0)
        {
            var dots = new Canvas { Width = width, Height = m.Bar }; double dot = m.Bar * 0.17;
            Color[] colors = [Color.FromRgb(0xFF, 0x5F, 0x57), Color.FromRgb(0xFE, 0xBC, 0x2E), Color.FromRgb(0x28, 0xC8, 0x40)];
            for (int i = 0; i < 3; i++)
                dots.Children.Add(new System.Windows.Shapes.Ellipse { Width = dot * 2, Height = dot * 2, Fill = new SolidColorBrush(colors[i]), Margin = new Thickness(m.Bar * 0.5 + i * dot * 2.9 - dot, m.Bar / 2 - dot, 0, 0) });
            _styleBar.Child = dots;
        }
        // Stacked translucent rounded rectangles: a soft shadow without a pixel shader, so dragging a stroke stays smooth.
        if (m.Shadow > 0)
            for (int i = 8; i >= 1; i--)
            {
                double spread = m.Shadow * i / 8, offset = m.Shadow * 0.45;
                _styleShadows.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(18, 0, 0, 0)), CornerRadius = new CornerRadius(m.Radius + spread),
                    Margin = new Thickness(-spread, -spread + offset, -spread, -spread - offset), IsHitTestVisible = false });
            }
    }

    private StackPanel BuildStylePane()
    {
        var pane = new StackPanel();
        pane.Children.Add(Ui.Text(L.T("Presentation style"), 16, true));
        var intro = Ui.Text(L.T("Adds a background, rounded corners and a shadow to the copy you save or copy. The original stays as it is."), 12, muted: true);
        intro.Margin = new Thickness(0, 2, 0, 12); pane.Children.Add(intro);
        var toggle = Ui.Toggle(false, on => { _beautify = _beautify with { Enabled = on }; SchedulePreview(); });
        System.Windows.Automation.AutomationProperties.SetName(toggle, L.T("Add a presentation background"));
        pane.Children.Add(Ui.Row(L.T("Add a presentation background"), null, toggle));

        _stylePreview = new Image { Stretch = Stretch.Uniform, MaxHeight = 190, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(_stylePreview, BitmapScalingMode.HighQuality);
        var previewFrame = new Border { Child = _stylePreview, CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Margin = new Thickness(0, 12, 0, 12), BorderThickness = new Thickness(1) };
        previewFrame.SetResourceReference(Border.BackgroundProperty, "Card"); previewFrame.SetResourceReference(Border.BorderBrushProperty, "Stroke");
        pane.Children.Add(previewFrame);

        pane.Children.Add(Ui.Text(L.T("Background"), 12, true));
        var swatches = new WrapPanel { Margin = new Thickness(0, 8, 0, 6) };
        foreach (string name in BeautifyOptions.Backgrounds)
        {
            var swatch = Ui.Button("", () => { _beautify = _beautify with { Background = name }; SchedulePreview(); });
            swatch.Width = swatch.Height = swatch.MinHeight = 28; swatch.Padding = new Thickness(0); swatch.Margin = new Thickness(0, 0, 6, 6); DesignTokens.SetButtonRadius(swatch, new CornerRadius(14));
            if (name == "None") { swatch.Content = Ui.Icon("Close", 14); }
            else
            {
                var probe = ScreenshotBeautifier.Apply(new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null), new BeautifyOptions(true, name, 0, 0, false));
                swatch.Background = new ImageBrush(probe);
            }
            Ui.Tip(swatch, L.T(name)); System.Windows.Automation.AutomationProperties.SetName(swatch, L.T(name)); swatches.Children.Add(swatch);
        }
        pane.Children.Add(swatches);

        var padding = new Slider { Minimum = 0, Maximum = 20, Value = _beautify.Padding * 100, TickFrequency = 1, IsSnapToTickEnabled = true };
        padding.ValueChanged += (_, _) => { _beautify = _beautify with { Padding = padding.Value / 100 }; SchedulePreview(); };
        var paddingField = Ui.SliderField(L.T("Space around the image"), padding, v => $"{v:0}%"); paddingField.Margin = new Thickness(0, 10, 0, 0); pane.Children.Add(paddingField);
        var corners = new Slider { Minimum = 0, Maximum = 6, Value = _beautify.Corners * 100, TickFrequency = 0.5, IsSnapToTickEnabled = true };
        corners.ValueChanged += (_, _) => { _beautify = _beautify with { Corners = corners.Value / 100 }; SchedulePreview(); };
        var cornersField = Ui.SliderField(L.T("Rounded corners"), corners, v => $"{v:0.#}%"); cornersField.Margin = new Thickness(0, 12, 0, 0); pane.Children.Add(cornersField);
        pane.Children.Add(Ui.Row(L.T("Shadow"), null, Ui.Toggle(_beautify.Shadow, on => { _beautify = _beautify with { Shadow = on }; SchedulePreview(); })));
        pane.Children.Add(Ui.Row(L.T("Window bar"), L.T("A slim title bar with three dots above the image."), Ui.Toggle(_beautify.WindowBar, on => { _beautify = _beautify with { WindowBar = on }; SchedulePreview(); })));
        return pane;
    }

    private void SchedulePreview()
    {
        ApplyLiveStyle();
        _previewTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _previewTimer.Tick -= PreviewTick; _previewTimer.Tick += PreviewTick; _previewTimer.Stop(); _previewTimer.Start();
    }
    private void PreviewTick(object? sender, EventArgs e) { _previewTimer?.Stop(); RefreshStylePreview(); }

    /// <summary>A small rendering of what Copy or Save would produce, so the options can be judged before exporting.</summary>
    private void RefreshStylePreview()
    {
        if (_stylePreview == null) return;
        try
        {
            var current = _document.Export();
            double scale = Math.Min(1, 520.0 / Math.Max(current.PixelWidth, current.PixelHeight));
            BitmapSource small = scale < 1 ? new TransformedBitmap(current, new ScaleTransform(scale, scale)) : current;
            _stylePreview.Source = ScreenshotBeautifier.Apply(small, _beautify);
        }
        catch (Exception) { _stylePreview.Source = null; }
    }
}
