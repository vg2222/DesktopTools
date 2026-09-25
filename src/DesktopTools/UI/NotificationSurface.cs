using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using DesktopTools.Localization;
namespace DesktopTools.UI;

internal static class NotificationSurface
{
    internal static Border Create(string message, NotificationKind kind, string style, Action dismiss, BitmapSource? image = null, (string Label, Action Run)[]? actions = null, double? progress = null, double? previousProgress = null)
    {
        bool capsule = style == "Capsule";
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        FrameworkElement leading;
        if (style == "Preview" && image != null)
        {
            var thumbnail = new Image { Source = image, Width = 88, Height = 70, Stretch = Stretch.UniformToFill, Clip = new RectangleGeometry(new Rect(0,0,88,70), 12,12) };
            leading = thumbnail;
        }
        else
        {
            leading = Ui.AppIcon(40);
        }
        leading.Margin = new Thickness(0, 0, 14, 0); leading.VerticalAlignment = VerticalAlignment.Center; layout.Children.Add(leading);
        var body = new StackPanel { Margin = new Thickness(0, 2, 10, 2), VerticalAlignment = VerticalAlignment.Center };
        var app = Ui.Text("DesktopTools", 11, muted: true); app.Margin = new Thickness(0,0,0,3); body.Children.Add(app);
        if (kind != NotificationKind.Info)
        {
            var severity = Ui.Text(L.T(kind == NotificationKind.Error ? "Error" : "Warning"), 12, true);
            severity.Foreground = new SolidColorBrush(kind == NotificationKind.Error ? Color.FromRgb(225,84,99) : Color.FromRgb(196,132,27));
            severity.Margin = new Thickness(0,0,0,3); body.Children.Add(severity);
        }
        var text = Ui.Text(message, 13, true);
        text.Name = "NotificationMessage";
        var scroll = new ScrollViewer { Content = text, MaxHeight = 210, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; body.Children.Add(scroll);
        if (progress.HasValue) body.Children.Add(CreateProgressBar(progress.Value, previousProgress));
        if (actions?.Length > 0)
        {
            var buttons = new WrapPanel { Margin = new Thickness(0,10,0,0) };
            foreach (var action in actions)
            {
                var button = RoundedButton(L.T(action.Label), action.Run, false); button.Margin = new Thickness(0,0,6,4); buttons.Children.Add(button);
            }
            body.Children.Add(buttons);
        }
        Grid.SetColumn(body,1); layout.Children.Add(body);
        var close = RoundedButton(L.T("Dismiss notification"), dismiss, true); close.Name = "NotificationClose"; close.VerticalAlignment = capsule ? VerticalAlignment.Center : VerticalAlignment.Top;
        Grid.SetColumn(close,2); layout.Children.Add(close);
        var surface = new Border { Child = layout, CornerRadius = new CornerRadius(capsule ? 28 : 22), Padding = new Thickness(18,14,14,14), Margin = new Thickness(0), BorderThickness = new Thickness(1) };
        surface.SetResourceReference(Border.BackgroundProperty,"GlassSurface");
        surface.SetResourceReference(Border.BorderBrushProperty,"GlassRim"); return surface;
    }
    internal static ProgressBar CreateProgressBar(double value, double? previous)
    {
        double target = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
        var bar = new ProgressBar { Name = "UpdateProgress", Minimum = 0, Maximum = 1, Value = target,
            Height = 8, Margin = new Thickness(0, 11, 0, 3), BorderThickness = new Thickness(0) };
        bar.SetResourceReference(Control.ForegroundProperty, "Accent");
        bar.SetResourceReference(Control.BackgroundProperty, "Field");
        var track = new FrameworkElementFactory(typeof(Grid)); track.Name = "PART_Track";
        track.SetValue(UIElement.ClipToBoundsProperty, true);
        var background = new FrameworkElementFactory(typeof(Border));
        background.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        background.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Control.Background))
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        track.AppendChild(background);
        var indicator = new FrameworkElementFactory(typeof(Border)); indicator.Name = "PART_Indicator";
        indicator.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        indicator.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        indicator.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Control.Foreground))
            { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        track.AppendChild(indicator);
        bar.Template = new ControlTemplate(typeof(ProgressBar)) { VisualTree = track };
        AutomationProperties.SetName(bar, L.T("Downloading update…"));
        AutomationProperties.SetHelpText(bar, $"{Math.Round(target * 100)}%");
        if (previous.HasValue && Motion.Enabled)
        {
            double start = double.IsFinite(previous.Value) ? Math.Clamp(previous.Value, 0, 1) : target;
            if (Math.Abs(start - target) > .001)
                bar.Loaded += (_, _) => bar.BeginAnimation(RangeBase.ValueProperty,
                    new DoubleAnimation(start, target, TimeSpan.FromMilliseconds(200))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }
        return bar;
    }
    private static Button RoundedButton(string label, Action action, bool close)
    {
        var button = new Button { Padding = close ? new Thickness(0) : new Thickness(13,6,13,6), MinHeight = 28, BorderThickness = new Thickness(0), Focusable = false };
        button.SetResourceReference(Control.BackgroundProperty,"Field"); button.SetResourceReference(Control.ForegroundProperty,"Text");
        var root = new FrameworkElementFactory(typeof(Border)); root.Name = "Pill"; root.SetValue(Border.CornerRadiusProperty,new CornerRadius(16)); root.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        root.SetBinding(Border.PaddingProperty,new System.Windows.Data.Binding("Padding") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center); presenter.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center); root.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = root };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(UIElement.OpacityProperty,.8,"Pill")); template.Triggers.Add(hover);
        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true }; pressed.Setters.Add(new Setter(UIElement.OpacityProperty,.6,"Pill")); template.Triggers.Add(pressed); button.Template = template;
        if(close) { button.Width=button.Height=32; button.Content=Ui.Icon("Close",12); } else button.Content=Ui.Text(label,12);
        AutomationProperties.SetName(button,label); button.Click += (_,_) => action(); return button;
    }
}
