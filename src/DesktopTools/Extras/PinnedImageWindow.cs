using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation;
using DesktopTools.Native;
using DesktopTools.UI;

namespace DesktopTools.Extras;

public sealed class PinnedImageWindow : Window
{
    private readonly Image _image;
    private readonly Border _toolbar;
    private readonly Thumb _resize;
    private readonly double _ratio;
    private BitmapSource? _source;
    private readonly Button _compact;
    private readonly ContextMenu _menu;
    private readonly Action<string> _report;

    public PinnedImageWindow(BitmapSource image, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(image);
        _source = image; _report = report; _ratio = (double)image.PixelWidth / image.PixelHeight;
        Title = L.T("Pinned image · DesktopTools"); Topmost = true; ShowInTaskbar = false;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; BorderThickness = new Thickness(0); ShowActivated = false;
        Motion.WindowEntrance(this);
        var monitor = MonitorService.GetCurrent();
        double maxWidth = monitor.WorkingArea.Width / monitor.ScaleX * .85, maxHeight = monitor.WorkingArea.Height / monitor.ScaleY * .85;
        double width = Math.Min(image.PixelWidth / monitor.ScaleX, Math.Min(maxWidth, maxHeight * _ratio));
        Width = Math.Max(1, width); Height = Width / _ratio;
        Left = monitor.WorkingArea.X / monitor.ScaleX + (monitor.WorkingArea.Width / monitor.ScaleX - Width) / 2;
        Top = monitor.WorkingArea.Y / monitor.ScaleY + (monitor.WorkingArea.Height / monitor.ScaleY - Height) / 2;
        var layout = new Grid { Background = Brushes.Transparent, ClipToBounds = true };
        _image = new Image { Source = image, Stretch = Stretch.Fill, Cursor = Cursors.SizeAll };
        AutomationProperties.SetName(_image, L.T("Pinned screenshot. Drag to move. Ctrl+C copies, Ctrl+S saves, Escape closes."));
        _image.MouseLeftButtonDown += DragImage; layout.Children.Add(_image);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        actions.Children.Add(Ui.Button(L.T("Copy"), () => Copy()));
        actions.Children.Add(Ui.Button(L.T("Save"), () => Save()));
        actions.Children.Add(Ui.Button(L.T("Close"), Close));
        var opacity = new Slider { Minimum = .15, Maximum = 1, Value = 1, Width = 72, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) };
        AutomationProperties.SetName(opacity, L.T("Screenshot opacity")); opacity.ValueChanged += (_, _) => _image.Opacity = opacity.Value;
        actions.Children.Add(opacity);
        _toolbar = new Border { Child = actions, Padding = new Thickness(6), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6), Visibility = Visibility.Collapsed };
        _toolbar.SetResourceReference(Border.BackgroundProperty, "Card"); _toolbar.SetResourceReference(Border.BorderBrushProperty, "Stroke");
        layout.Children.Add(_toolbar);
        _menu = new ContextMenu();
        _menu.SetResourceReference(Control.BackgroundProperty, "Card"); _menu.SetResourceReference(Control.ForegroundProperty, "Text"); _menu.SetResourceReference(Control.BorderBrushProperty, "Stroke");
        void MenuAction(string label, Action action)
        {
            var item = new MenuItem { Header = label, Padding = new Thickness(12, 7, 12, 7) };
            item.SetResourceReference(Control.BackgroundProperty, "Card"); item.SetResourceReference(Control.ForegroundProperty, "Text");
            item.Click += (_, _) => action(); _menu.Items.Add(item);
        }
        MenuAction(L.T("Copy image    Ctrl+C"), Copy); MenuAction(L.T("Save PNG    Ctrl+S"), Save);
        MenuAction(L.T("Opacity 100%"), () => opacity.Value = 1); MenuAction(L.T("Opacity 75%"), () => opacity.Value = .75); MenuAction(L.T("Opacity 50%"), () => opacity.Value = .5);
        MenuAction(L.T("Enlarge    Ctrl++"), () => ScaleImage(1.1)); MenuAction(L.T("Shrink    Ctrl+-"), () => ScaleImage(1 / 1.1)); MenuAction(L.T("Close    Esc"), Close);
        _menu.Opened += MenuOpened; ContextMenu = _menu;
        _compact = Ui.IconButton("More", L.T("Pinned image actions"), OpenMenu); _compact.Width = 28; _compact.Height = 28; _compact.MinWidth = 0; _compact.MinHeight = 0;
        _compact.Padding = new Thickness(0); _compact.Margin = new Thickness(2); _compact.HorizontalAlignment = HorizontalAlignment.Right; _compact.VerticalAlignment = VerticalAlignment.Top; _compact.Visibility = Visibility.Collapsed;
        _compact.SetResourceReference(Control.BackgroundProperty, "Card"); AutomationProperties.SetName(_compact, L.T("Pinned image actions")); layout.Children.Add(_compact);
        var template = new ControlTemplate(typeof(Thumb));
        var mark = new FrameworkElementFactory(typeof(Border)); mark.SetValue(Border.BackgroundProperty, Brushes.White); mark.SetValue(Border.BorderBrushProperty, Brushes.DimGray); mark.SetValue(Border.BorderThicknessProperty, new Thickness(1)); mark.SetValue(Border.CornerRadiusProperty, new CornerRadius(3)); template.VisualTree = mark;
        _resize = new Thumb { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, Template = template, Visibility = Visibility.Collapsed, Margin = new Thickness(2) };
        AutomationProperties.SetName(_resize, L.T("Resize screenshot, preserving proportions")); _resize.DragDelta += ResizeImage; layout.Children.Add(_resize);
        Content = layout;
        SizeChanged += Resized; MouseEnter += Hover; MouseLeave += Hover; IsKeyboardFocusWithinChanged += FocusChanged; PreviewKeyDown += Keys;
        Closing += (_, _) => _menu.IsOpen = false;
        Closed += Cleanup;
    }
    private void DragImage(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) { DragMove(); e.Handled = true; }
    }
    private void ResizeImage(object sender, DragDeltaEventArgs e)
    {
        double change = Math.Abs(e.HorizontalChange) >= Math.Abs(e.VerticalChange * _ratio) ? e.HorizontalChange : e.VerticalChange * _ratio;
        double width = Math.Clamp(Width + change, Math.Min(10000, Math.Max(24, 24 * _ratio)), 10000);
        Width = width; Height = width / _ratio;
    }
    private void Hover(object sender, MouseEventArgs e) => UpdateControls();
    private void FocusChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateControls();
    private void Resized(object sender, SizeChangedEventArgs e) => UpdateControls();
    private void MenuOpened(object sender, RoutedEventArgs e) => Ui.ExcludePopup(_menu);
    private void OpenMenu() { _menu.PlacementTarget = _compact; _menu.Placement = PlacementMode.Bottom; _menu.IsOpen = true; }
    private void Copy() { if (_source != null) ImageOutput.Copy(_source, _report); }
    private void Save() { if (_source != null) ImageOutput.Save(this, _source, _report); }
    private void ScaleImage(double scale) { Width = Math.Clamp(Width * scale, Math.Min(10000, Math.Max(24, 24 * _ratio)), 10000); Height = Width / _ratio; }
    private void UpdateControls()
    {
        var visible = IsMouseOver || IsKeyboardFocusWithin;
        bool full = ActualWidth >= 320 && ActualHeight >= 90;
        _toolbar.Visibility = visible && full ? Visibility.Visible : Visibility.Collapsed;
        _compact.Visibility = visible && !full && ActualWidth >= 32 && ActualHeight >= 32 ? Visibility.Visible : Visibility.Collapsed;
        _resize.Visibility = visible && ActualWidth >= 32 && ActualHeight >= 32 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Keys(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift)) { _menu.PlacementTarget = this; _menu.Placement = PlacementMode.MousePoint; _menu.IsOpen = true; e.Handled = true; }
        else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C) { Copy(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { Save(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Add or Key.Subtract or Key.OemPlus or Key.OemMinus)
        {
            Width = Math.Clamp(Width * (e.Key is Key.Add or Key.OemPlus ? 1.1 : 1 / 1.1), Math.Min(10000, Math.Max(24, 24 * _ratio)), 10000); Height = Width / _ratio; e.Handled = true;
        }
    }
    private void Cleanup(object? sender, EventArgs e)
    {
        _image.MouseLeftButtonDown -= DragImage; _resize.DragDelta -= ResizeImage; MouseEnter -= Hover; MouseLeave -= Hover;
        SizeChanged -= Resized; _menu.IsOpen = false; _menu.Opened -= MenuOpened; _menu.Items.Clear(); ContextMenu = null;
        IsKeyboardFocusWithinChanged -= FocusChanged; PreviewKeyDown -= Keys; Closed -= Cleanup; _image.Source = null; _source = null; Content = null;
    }
}
