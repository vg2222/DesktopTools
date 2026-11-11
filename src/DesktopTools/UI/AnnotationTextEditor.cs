using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using DesktopTools.Core;

namespace DesktopTools.UI;

/// <summary>In-canvas editor with an explicit text inset shared by export.</summary>
internal sealed partial class AnnotationTextEditor : TextBox
{
    public const double TextInset = 13;
    public double ExportWidth => Math.Max(1, Width - TextInset * 2);
    public Point ExportOrigin => new(Canvas.GetLeft(this) + TextInset, Canvas.GetTop(this) + TextInset);
    internal event Action? CommitRequested, CancelRequested;
    private void RequestCommit() => CommitRequested?.Invoke();
    private void RequestCancel() => CancelRequested?.Invoke();
    partial void InitializeFormatting();
    private readonly double areaWidth;
    private bool manualWidth, ready;
    /// <summary>The box is as wide as its longest line (up to the canvas edge) until the user drags the grip; it then keeps that width and wraps.</summary>
    private void FitWidth()
    {
        if (!ready || manualWidth) return;
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip, widest = 0;
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretches.Normal);
        foreach (string line in (Text ?? "").Replace("\r", "").Split('\n'))
            widest = Math.Max(widest, new FormattedText(line.Length == 0 ? " " : line, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, FontSize, Brushes.Black, dip).WidthIncludingTrailingWhitespace);
        double room = Math.Max(MinimumWidth, areaWidth - Math.Max(0, Canvas.GetLeft(this)));
        Width = Math.Clamp(widest + FontSize * 0.75 + TextInset * 2, MinimumWidth, room);
    }
    private double MinimumWidth => TextInset * 2 + Math.Max(60, FontSize * 2);
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == FontSizeProperty || e.Property == FontFamilyProperty || e.Property == FontWeightProperty || e.Property == FontStyleProperty) FitWidth();
    }
    protected override void OnTextChanged(TextChangedEventArgs e) { base.OnTextChanged(e); FitWidth(); }
    private void OnGripDrag(object sender, DragDeltaEventArgs e)
    {
        manualWidth = true;
        Width = Math.Clamp(ActualWidth + e.HorizontalChange, MinimumWidth, Math.Max(MinimumWidth, areaWidth - Math.Max(0, Canvas.GetLeft(this))));
        e.Handled = true;
    }
    private void OnGripDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { manualWidth = false; FitWidth(); e.Handled = true; }
    public AnnotationTextEditor(Point point, Size area, Color color, double fontSize)
    {
        areaWidth = area.Width;
        Width = Math.Max(27, Math.Min(400, area.Width)); MinWidth = 0; MinHeight = 0;
        Width = Math.Min(Width, TextInset * 2 + Math.Max(60, fontSize * 2));
        Canvas.SetLeft(this, Math.Clamp(point.X - TextInset, 0, Math.Max(0, area.Width - Width)));
        Canvas.SetTop(this, Math.Clamp(point.Y - TextInset, 0, Math.Max(0, area.Height - fontSize * 2 - 58)));
        FontFamily = AnnotationTypography.Resolve(AnnotationTypography.DefaultFamily); FontSize = fontSize; FontWeight = FontWeights.Normal;
        Foreground = new SolidColorBrush(color); CaretBrush = Foreground;
        Background = new SolidColorBrush(color.R * .2126 + color.G * .7152 + color.B * .0722 > 155 ? Color.FromRgb(28, 32, 40) : Color.FromRgb(250, 251, 254));
        BorderBrush = new SolidColorBrush(Color.FromRgb(40, 112, 255)); BorderThickness = new Thickness(1); Padding = new Thickness(0);
        AcceptsReturn = true; TextWrapping = TextWrapping.Wrap; VerticalContentAlignment = VerticalAlignment.Top;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal); UseLayoutRounding = false;
        AutomationProperties.SetName(this, L.T("Annotation text. Control Enter to finish; Escape to cancel."));
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        frame.SetValue(Border.BorderBrushProperty, BorderBrush); frame.SetValue(Border.BorderThicknessProperty, new Thickness(1)); frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(10)); frame.SetValue(Border.PaddingProperty, new Thickness(12));
        var stack = new FrameworkElementFactory(typeof(StackPanel));
        var host = new FrameworkElementFactory(typeof(ScrollViewer)); host.Name = "PART_ContentHost"; host.SetValue(FrameworkElement.MinHeightProperty, fontSize * 1.4); stack.AppendChild(host);
        var hint = new FrameworkElementFactory(typeof(TextBlock)); hint.SetValue(TextBlock.TextProperty, L.T("Ctrl+Enter to finish · Esc to cancel")); hint.SetValue(TextBlock.FontFamilyProperty, AnnotationTypography.Resolve(AnnotationTypography.BundledFamily)); hint.SetValue(TextBlock.FontSizeProperty, 11d); hint.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(color.R * .2126 + color.G * .7152 + color.B * .0722 > 155 ? Colors.LightGray : Colors.DimGray)); hint.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 10, 0, 0)); hint.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); stack.AppendChild(hint);
        frame.AppendChild(stack);
        // Drag the right edge to choose the wrap width; double-click it to fit the text again.
        var gripPill = new FrameworkElementFactory(typeof(Border));
        gripPill.SetValue(FrameworkElement.WidthProperty, 4d); gripPill.SetValue(FrameworkElement.HeightProperty, 26d); gripPill.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        gripPill.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(40, 112, 255))); gripPill.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); gripPill.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var gripHit = new FrameworkElementFactory(typeof(Border)); gripHit.SetValue(Border.BackgroundProperty, Brushes.Transparent); gripHit.AppendChild(gripPill);
        var grip = new FrameworkElementFactory(typeof(Thumb)); grip.SetValue(Control.TemplateProperty, new ControlTemplate(typeof(Thumb)) { VisualTree = gripHit });
        grip.SetValue(FrameworkElement.WidthProperty, 14d); grip.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right); grip.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        grip.SetValue(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.SizeWE); grip.SetValue(UIElement.FocusableProperty, false);
        grip.AddHandler(Thumb.DragDeltaEvent, new DragDeltaEventHandler(OnGripDrag)); grip.AddHandler(UIElement.MouseLeftButtonDownEvent, new System.Windows.Input.MouseButtonEventHandler((s, e) => { if (e.ClickCount == 2) OnGripDoubleClick(s, e); }), true);
        var root = new FrameworkElementFactory(typeof(Grid)); root.AppendChild(frame); root.AppendChild(grip);
        Template = new ControlTemplate(typeof(TextBox)) { VisualTree = root };
        InitializeFormatting();
        ready = true; FitWidth();
    }
}
