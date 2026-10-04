using DesktopTools.Localization;
using DesktopTools.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopTools.Extras;

public sealed partial class ScreenshotEditorWindow
{
    private BeautifyOptions _beautify = new();
    private Image _stylePreview = null!;
    private System.Windows.Threading.DispatcherTimer? _previewTimer;

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
