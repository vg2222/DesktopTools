using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using DesktopTools.Localization;
using DesktopTools.Native;
using DesktopTools.UI;

namespace DesktopTools.Extras;

internal sealed class RecordingHudWindow : Window
{
    private readonly TextBlock time = Ui.Text("00:00", 21, true);
    private readonly Ellipse indicator = new() { Width = 14, Height = 14, Fill = Brushes.Coral, Margin = new Thickness(0, 0, 14, 0) };
    private readonly Button pause, stop;
    private readonly TextBlock sourceName;
    private readonly string sourceLabel;
    private bool completed;
    private readonly ColumnDefinition timeColumn;
    private (bool Paused, bool Stopping, bool Suspended, bool Starting)? displayedState;

    internal RecordingHudWindow(string source, Action togglePause, Action requestStop, bool excludeFromCapture, MonitorInfo? display = null)
    {
        Title = L.T("Screen recorder"); Width = 470; Height = 78; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false; WindowStartupLocation = WindowStartupLocation.Manual;
        var monitor = display ?? MonitorService.GetCurrent();
        var initial = BottomPlacement(monitor, new Size(Width, Height));
        Left = initial.Left / monitor.ScaleX; Top = initial.Top / monitor.ScaleY;
        Loaded += (_, _) =>
        {
            var bounds = BottomPlacement(monitor, new Size(ActualWidth, ActualHeight));
            NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, new nint(-1),
                (int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height, 0x10);
        };
        var row = new Grid(); foreach (var width in new[] { 28d, 91, 1, double.NaN, 48, 48 }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(width) ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
        timeColumn = row.ColumnDefinitions[1]; time.TextWrapping = TextWrapping.NoWrap;
        void Add(UIElement child, int column) { Grid.SetColumn(child, column); row.Children.Add(child); }
        Add(indicator, 0); Add(time, 1);
        var divider = new Border { Width = 1, Height = 28 }; divider.SetResourceReference(Border.BackgroundProperty, "Stroke"); Add(divider, 2);
        sourceLabel = source; var name = sourceName = Ui.Text(source, 12); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; name.ToolTip = source;
        var label = new DockPanel { Margin = new Thickness(14, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center }; var icon = Ui.Icon("Monitor", 20); icon.Margin = new Thickness(0, 0, 9, 0); label.Children.Add(icon); label.Children.Add(name); Add(label, 3);
        pause = Ui.IconButton("Pause", L.T("Pause"), togglePause); stop = Ui.IconButton("Stop", L.T("Stop"), requestStop);
        foreach (var button in new[] { pause, stop }) { button.Width = button.Height = 42; button.Margin = new Thickness(3, 0, 3, 0); button.VerticalAlignment = VerticalAlignment.Center; DesignTokens.SetButtonRadius(button, new CornerRadius(21)); }
        pause.SetResourceReference(Control.BackgroundProperty, "Card"); stop.Background = new SolidColorBrush(Color.FromRgb(222, 53, 65)); ((Shape)stop.Content).Fill = Brushes.White;
        Add(pause, 4); Add(stop, 5);
        var card = Ui.Card(row, 16); card.Margin = new Thickness(0); card.CornerRadius = new CornerRadius(39); card.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); card.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); Content = card;
        card.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
        SourceInitialized += (_, _) => NativeWindowService.TryExcludeFromCapture(this, excludeFromCapture, out _);
        Closing += (_, e) => { if (!completed) { e.Cancel = true; requestStop(); } };
        Motion.WindowEntrance(this);
    }
    internal void Update(TimeSpan elapsed, bool paused, bool stopping, bool sourceSuspended = false, bool starting = false)
    {
        string value = RecordingTime.Format(elapsed, compact: true);
        bool timeChanged = time.Text != value, stateChanged = displayedState != (paused, stopping, sourceSuspended, starting);
        if (!timeChanged && !stateChanged) return;
        time.Text = value;
        timeColumn.Width = new GridLength(elapsed.TotalHours >= 1 ? 112 + Math.Max(0, value.Length - 8) * 12 : 91);
        string label = L.T(stopping ? "Finishing" : starting ? "Starting recording" : paused ? "Paused" : "Recording");
        AutomationProperties.SetName(time, label + " " + value);
        if (!stateChanged) return;
        displayedState = (paused, stopping, sourceSuspended, starting);
        indicator.Fill = stopping || paused || starting ? Brushes.Goldenrod : Brushes.Coral; Ui.Tip(time, label);
        pause.Content = Ui.Icon(paused ? "Play" : "Pause"); string action = L.T(paused ? "Resume" : "Pause"); Ui.Tip(pause, action); AutomationProperties.SetName(pause, action); pause.IsEnabled = stop.IsEnabled = !stopping;
        if (sourceSuspended || starting) pause.IsEnabled = false;
        sourceName.Text = sourceSuspended ? L.T("Source paused") : sourceLabel; sourceName.ToolTip = sourceSuspended ? L.T("Source window is minimized or hidden. Recording is paused.") : sourceLabel;
    }
    internal void Finish() { completed = true; Close(); }
    internal static Rect BottomPlacement(MonitorInfo monitor, Size size)
    {
        var area = monitor.WorkingArea;
        double width = Math.Min(size.Width * monitor.ScaleX, area.Width);
        double height = Math.Min(size.Height * monitor.ScaleY, area.Height);
        return new Rect(area.Left + (area.Width - width) / 2,
            Math.Max(area.Top, area.Bottom - height - 20 * monitor.ScaleY), width, height);
    }
}
