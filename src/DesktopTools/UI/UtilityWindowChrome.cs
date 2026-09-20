using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using DesktopTools.Native;

namespace DesktopTools.UI;

internal static class UtilityWindowChrome
{
    // Standard tool windows can use DWM acrylic. Ink, selection and floating HUD surfaces
    // keep their layered windows and must not opt into this chrome.
    internal static void EnableBackdrop(Window window)
    {
        Motion.WindowEntrance(window);
        window.WindowStyle = WindowStyle.None; window.AllowsTransparency = false;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 52, ResizeBorderThickness = window.ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize ? new Thickness(0) : new Thickness(6, 0, 6, 6),
            CornerRadius = new CornerRadius(8), GlassFrameThickness = new Thickness(-1), UseAeroCaptionButtons = false
        });
        window.SourceInitialized += InitializeBackdrop;
        window.Loaded += LoadedBackdrop;
        window.SizeChanged += (_, _) => ClipContent(window);
        window.StateChanged += (_, _) => ClipContent(window);
    }
    private static void InitializeBackdrop(object? sender, EventArgs args)
    {
        var window = (Window)sender!; window.SourceInitialized -= InitializeBackdrop; ApplyBackdrop(window);
    }
    private static void LoadedBackdrop(object sender, RoutedEventArgs args)
    {
        var window = (Window)sender; window.Loaded -= LoadedBackdrop; ApplyBackdrop(window); ClipContent(window);
    }
    internal static void ClipContent(Window window)
    {
        if (window.Content is not FrameworkElement content || content.ActualWidth <= 0 || content.ActualHeight <= 0) return;
        double radius = window.WindowState == WindowState.Maximized ? 0 : 8;
        if (content is Border border) border.CornerRadius = new CornerRadius(radius);
        content.Clip = new RectangleGeometry(new Rect(0, 0, content.ActualWidth, content.ActualHeight), radius, radius);
    }
    private static void ApplyBackdrop(Window window)
    {
        bool transparency = (Application.Current as App)?.Controller?.Settings.Transparency ?? window.TryFindResource("GlassSurface") is LinearGradientBrush;
        bool dark = window.TryFindResource("Surface") is SolidColorBrush surface && surface.Color.R < 128;
        NativeWindowService.ApplyBackdrop(window, dark, transparency);
    }

    internal static Border DialogCard(Window window, FrameworkElement header, UIElement body, FrameworkElement actions, double padding)
    {
        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top); layout.Children.Add(header);
        DockPanel.SetDock(actions, Dock.Bottom); layout.Children.Add(actions);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SmoothScroll.Enable(scroll); layout.Children.Add(scroll);
        var card = Ui.Card(layout, padding); card.Margin = new Thickness(0);
        card.SetBinding(FrameworkElement.MaxWidthProperty, new Binding(nameof(Window.MaxWidth)) { Source = window });
        card.SetBinding(FrameworkElement.MaxHeightProperty, new Binding(nameof(Window.MaxHeight)) { Source = window });
        card.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); card.SetResourceReference(Border.BorderBrushProperty, "GlassRim");
        double maxWidth = window.MaxWidth, maxHeight = window.MaxHeight, minWidth = window.MinWidth, minHeight = window.MinHeight;
        MonitorInfo? previous = null; bool fitting = false, closed = false;
        void Fit(Window target, bool reposition)
        {
            if (closed || fitting) return;
            fitting = true;
            try
            {
                var monitor = MonitorService.GetForWindow(target);
                if (monitor == previous && !reposition) return;
                previous = monitor;
                double width = Math.Min(maxWidth, Math.Max(1, monitor.WorkingArea.Width / monitor.ScaleX - 24));
                double height = Math.Min(maxHeight, Math.Max(1, monitor.WorkingArea.Height / monitor.ScaleY - 24));
                window.MinWidth = Math.Min(minWidth, width); window.MinHeight = Math.Min(minHeight, height);
                window.MaxWidth = width; window.MaxHeight = height;
                if (!window.IsLoaded) return;
                window.UpdateLayout();
                var handle = new WindowInteropHelper(window).Handle;
                if (!NativeMethods.GetWindowRect(handle, out var bounds)) return;
                int x = (int)Math.Clamp(bounds.Left, monitor.WorkingArea.Left, Math.Max(monitor.WorkingArea.Left, monitor.WorkingArea.Right - (bounds.Right - bounds.Left)));
                int y = (int)Math.Clamp(bounds.Top, monitor.WorkingArea.Top, Math.Max(monitor.WorkingArea.Top, monitor.WorkingArea.Bottom - (bounds.Bottom - bounds.Top)));
                if (x != bounds.Left || y != bounds.Top)
                    NativeMethods.SetWindowPos(handle, 0, x, y, 0, 0, 0x01 | 0x04 | 0x10 | 0x0200);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            { System.Diagnostics.Debug.WriteLine("DesktopTools dialog placement: " + ex); }
            finally { fitting = false; }
        }
        void Initialized(object? sender, EventArgs args) => Fit(window.Owner ?? window, false);
        void Loaded(object sender, RoutedEventArgs args) => Fit(window, true);
        void Moved(object? sender, EventArgs args) { if (window.IsLoaded) Fit(window, false); }
        void DpiChanged(object sender, DpiChangedEventArgs args) => window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => Fit(window, true)));
        void Closed(object? sender, EventArgs args)
        {
            closed = true; window.SourceInitialized -= Initialized; window.Loaded -= Loaded; window.LocationChanged -= Moved; window.DpiChanged -= DpiChanged; window.Closed -= Closed;
        }
        window.SourceInitialized += Initialized; window.Loaded += Loaded; window.LocationChanged += Moved; window.DpiChanged += DpiChanged; window.Closed += Closed;
        return card;
    }

    public static DockPanel Header(Window window, string text, Action closeAction, string closeLabel, double titleSize = 22)
    {
        if (window.AllowsTransparency && !GetHasTopDrag(window))
        {
            window.SetValue(HasTopDragProperty, true);
            window.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (e.GetPosition(window).Y > 48 || e.LeftButton != MouseButtonState.Pressed) return;
                for (DependencyObject? node = e.OriginalSource as DependencyObject; node != null;
                    node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                    if (node is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase or ComboBox or Slider) return;
                e.Handled = true;
                window.DragMove();
            };
        }
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var close = CaptionButton("Close", closeLabel, closeAction);
        close.Margin = new Thickness(0);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var dragArea = new Border
        {
            Background = Brushes.Transparent, MinHeight = DesignTokens.ControlHeight, Cursor = Cursors.Arrow,
            Child = Ui.Text(text, titleSize, true)
        };
        dragArea.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            e.Handled = true;
            if (window.IsVisible && Mouse.LeftButton == MouseButtonState.Pressed) window.DragMove();
        };
        header.Children.Add(dragArea);
        return header;
    }

    internal static Button CaptionButton(string icon, string label, Action action)
    {
        var button = Ui.IconButton(icon, label, action);
        button.Width = DesignTokens.CaptionButtonWidth;
        button.Height = button.MinHeight = DesignTokens.CaptionButtonHeight;
        button.Padding = new Thickness(9);
        button.Content = Ui.Icon(icon, 14);
        button.VerticalAlignment = VerticalAlignment.Center;
        return button;
    }
    private static readonly DependencyProperty HasTopDragProperty = DependencyProperty.RegisterAttached("HasTopDrag", typeof(bool), typeof(UtilityWindowChrome), new PropertyMetadata(false));
    private static bool GetHasTopDrag(Window window) => (bool)window.GetValue(HasTopDragProperty);
}
