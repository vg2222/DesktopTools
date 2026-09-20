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
    public AnnotationTextEditor(Point point, Size area, Color color, double fontSize)
    {
        Width = Math.Max(27, Math.Min(400, area.Width)); MinWidth = 0; MinHeight = 0;
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
        frame.AppendChild(stack); Template = new ControlTemplate(typeof(TextBox)) { VisualTree = frame };
        InitializeFormatting();
    }
}
