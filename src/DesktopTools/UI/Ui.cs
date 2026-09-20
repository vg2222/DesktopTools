using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Controls.Primitives;
using DesktopTools.Native;
using DesktopTools.Localization;
using System.Windows.Data;
using System.Globalization;

namespace DesktopTools.UI;

internal static class Ui
{
    public static readonly DependencyProperty TrackPopupProperty = DependencyProperty.RegisterAttached("TrackPopup", typeof(bool), typeof(Ui),
        new PropertyMetadata(false, (element, args) => { if (element is ComboBox combo) AppCapturePrivacy.TrackCombo(combo, (bool)args.NewValue); }));
    public static bool GetTrackPopup(DependencyObject element) => (bool)element.GetValue(TrackPopupProperty);
    public static void SetTrackPopup(DependencyObject element, bool value) => element.SetValue(TrackPopupProperty, value);
    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    public static TextBlock Text(string text, double size = 14, bool bold = false, bool muted = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.FontFamilyProperty, size >= 20 ? "DisplayFont" : "BodyFont");
        block.SetResourceReference(TextBlock.ForegroundProperty, muted ? "Muted" : "Text");
        return block;
    }
    public static FrameworkElement Icon(string name, double size = 19)
    {
        var path = new System.Windows.Shapes.Path { Data = ToolIcons.GeometryFor(name), Width = size, Height = size, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        path.SetResourceReference(Shape.FillProperty, "Text");
        return path;
    }
    public static Image AppIcon(double size = 40)
    {
        var image = new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/DesktopTools;component/Assets/Icons/AppIcon.png")), Width = size, Height = size, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }
    public static Button Button(string label, Action action, bool primary = false)
    {
        var b = new Button { Content = label, BorderThickness = new Thickness(primary ? 0 : 1), Margin = new Thickness(0, 0, 6, 0) };
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        if (primary) { b.SetResourceReference(Control.BackgroundProperty, "Accent"); b.SetResourceReference(Control.ForegroundProperty, "AccentText"); }
        Motion.ButtonFeedback(b); AutomationProperties.SetName(b, label); b.Click += (_, _) => action(); return b;
    }
    public static Button IconButton(string icon, string label, Action action)
    {
        var b = Button("", action); b.Content = Icon(icon); b.Width = 36; b.Height = 36; b.MinHeight = 36; b.Padding = new Thickness(8); b.BorderThickness = new Thickness(0); b.Background = Brushes.Transparent;
        Tip(b, label); AutomationProperties.SetName(b, label); return b;
    }
    public static FrameworkElement IconLabel(string icon, string label, double size = 17, bool primary = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var symbol = Icon(icon, size); if (primary) symbol.SetResourceReference(Shape.FillProperty, "AccentText"); content.Children.Add(symbol);
        var text = Text(label, 12); if (primary) text.SetResourceReference(TextBlock.ForegroundProperty, "AccentText"); text.Margin = new Thickness(8, 0, 0, 0); content.Children.Add(text); return content;
    }
    public static Button ImportPrompt(string icon, string title, string detail, Action open)
    {
        var button = Button(title, open);
        button.HorizontalAlignment = HorizontalAlignment.Center; button.VerticalAlignment = VerticalAlignment.Center;
        button.Padding = new Thickness(32, 24, 32, 24); button.Margin = new Thickness(16);
        var content = new StackPanel { MaxWidth = 310 };
        var symbol = Icon(icon, 42); symbol.HorizontalAlignment = HorizontalAlignment.Center; symbol.Margin = new Thickness(0, 0, 0, 14); content.Children.Add(symbol);
        var heading = Text(title, 18, true); heading.TextAlignment = TextAlignment.Center; content.Children.Add(heading);
        var caption = Text(detail, 12, muted: true); caption.TextAlignment = TextAlignment.Center; caption.Margin = new Thickness(0, 8, 0, 0); content.Children.Add(caption);
        button.Content = content; return button;
    }
    public static Border Card(UIElement child, double padding = 20)
    {
        var b = new Border { Child = child, Padding = new Thickness(padding), CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 16) };
        b.SizeChanged += (_, _) => b.Clip = new RectangleGeometry(new Rect(0, 0, b.ActualWidth, b.ActualHeight), b.CornerRadius.TopLeft, b.CornerRadius.TopLeft);
        b.SetResourceReference(Border.BackgroundProperty, "Card"); b.SetResourceReference(Border.BorderBrushProperty, "Stroke"); return b;
    }
    public static Border Shortcut(string gesture)
    {
        var keys = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var key in gesture.Split('+'))
        {
            var label = Text(key.Trim(), 11, true, true);
            var cap = new Border { Child = label, Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1) };
            cap.SetResourceReference(Border.BackgroundProperty, "Field"); cap.SetResourceReference(Border.BorderBrushProperty, "Stroke"); keys.Children.Add(cap);
        }
        return new Border { Child = keys, VerticalAlignment = VerticalAlignment.Center };
    }
    public static Button ColorButton(string color, Action action)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), BorderBrush = Brush("Stroke"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 10, 0) });
        content.Children.Add(Text(L.T("Choose color"), 13, true));
        var button = Button(L.T("Choose color"), action); button.Content = content; AutomationProperties.SetName(button, L.F($"Choose color, current {color}")); return button;
    }
    public static Grid Row(string title, string? description, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labels = new StackPanel { Margin = new Thickness(0, 0, 24, 0), VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(Text(title, 14, true));
        if (description != null) { var t = Text(description, 12, muted: true); t.Margin = new Thickness(0, 5, 0, 0); labels.Children.Add(t); }
        grid.Children.Add(labels); Grid.SetColumn(control, 1); grid.Children.Add(control); AutomationProperties.SetName(control, title); return grid;
    }
    public static CheckBox Toggle(bool value, Action<bool> changed)
    {
        var c = new CheckBox { IsChecked = value }; Motion.ToggleFeedback(c); c.Checked += (_, _) => changed(true); c.Unchecked += (_, _) => changed(false); return c;
    }
    public static ComboBox Choice(IEnumerable<string> choices, string selected, Action<string> changed, bool translate = true)
    {
        var c = new ComboBox { ItemsSource = choices.ToArray(), SelectedItem = selected, MinWidth = 150 };
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding { Converter = new ChoiceLabelConverter() });
        if (translate) c.ItemTemplate = new DataTemplate { VisualTree = label };
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is string v) changed(v); }; return c;
    }
    public static void ExcludePopup(Visual visual, Window? owningWindow = null)
    {
        var settings = (Application.Current as App)?.Controller?.Settings;
        Window? owner = owningWindow ?? PopupOwner(visual);
        bool exclude = settings == null || (owner != null ? AppCapturePrivacy.ShouldHidePopup(owner, settings) : settings.HideControlsFromCapture);
        if (PresentationSource.FromVisual(visual) is HwndSource source && !NativeWindowService.TryExcludeHandle(source.Handle, exclude, out var error))
            (Application.Current as App)?.Controller?.Report(error ?? "Could not hide a control popup from capture.");
    }
    internal static Window? PopupOwner(DependencyObject? element)
    {
        // Popup visuals live in a separate HWND; follow their placement/container chain.
        var visited = new HashSet<DependencyObject>();
        while (element != null && visited.Add(element))
        {
            if (element is ContextMenu menu) { element = menu.PlacementTarget; continue; }
            if (element is ToolTip tip) { element = tip.PlacementTarget; continue; }
            if (element is Popup popup) { element = popup.PlacementTarget ?? popup.TemplatedParent; continue; }
            if (Window.GetWindow(element) is Window owner) return owner;
            element = ItemsControl.ItemsControlFromItemContainer(element) ??
                (element is FrameworkElement framework ? framework.Parent ?? framework.TemplatedParent : null) ??
                (element is Visual ? VisualTreeHelper.GetParent(element) : null);
        }
        return null;
    }
    public static void Tip(FrameworkElement element, string label)
    {
        element.ToolTip = new ToolTip { Content = label };
    }
    private sealed class ChoiceLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => L.T(value as string);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
