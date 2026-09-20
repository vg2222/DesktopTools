using DesktopTools.Localization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopTools.UI;

namespace DesktopTools.Extras;

public sealed class CountdownWindow : Window
{
    private readonly Stopwatch clock = new();
    private readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly TextBlock time = new() { FontFamily = new FontFamily("Consolas"), FontSize = 42, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Button start;
    private readonly CountdownRing progress = new() { Width = 80, Height = 80, Margin = new Thickness(0, 0, 20, 0) };
    private TimeSpan duration = TimeSpan.FromMinutes(5);
    public TimeSpan Remaining => TimeSpan.FromTicks(Math.Max(0, duration.Ticks - clock.Elapsed.Ticks));
    public bool IsRunning => clock.IsRunning;
    public double Minutes { get => duration.TotalMinutes; set { duration = TimeSpan.FromMinutes(double.IsFinite(value) ? Math.Clamp(value, 1, 120) : 5); Reset(); } }

    public CountdownWindow()
    {
        Title = L.T("DesktopTools Countdown"); Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false;
        Motion.WindowEntrance(this);
        var layout = new DockPanel { LastChildFill = false };
        var close = UtilityWindowChrome.CaptionButton("Close", L.T("Close"), Close); DockPanel.SetDock(close, Dock.Right); layout.Children.Add(close);
        var ring = new Grid(); ring.Children.Add(progress); var clockIcon = Ui.Icon("Timer", 28); clockIcon.Margin = new Thickness(0, 0, 20, 0); clockIcon.VerticalAlignment = VerticalAlignment.Center; clockIcon.HorizontalAlignment = HorizontalAlignment.Center; ring.Children.Add(clockIcon); layout.Children.Add(ring);
        var clockPanel = new StackPanel { Width = 126, Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
        time.FontSize = 34; time.SetResourceReference(TextBlock.FontFamilyProperty, "BodyFont"); time.FontWeight = FontWeights.SemiBold; time.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        time.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
        clockPanel.Children.Add(time);
        var minutes = new ComboBox { Width = 125, Margin = new Thickness(0, 6, 0, 0), ToolTip = L.T("Countdown minutes") };
        foreach (double value in new[] { 1d, 3, 5, 10, 15, 25, 30, 45, 60, 90, 120 }) minutes.Items.Add(new ComboBoxItem { Content = L.F($"{value:0} minutes"), Tag = value });
        minutes.SelectedIndex = 2; minutes.SelectionChanged += (_, _) => { if (minutes.SelectedItem is ComboBoxItem { Tag: double value }) Minutes = value; };
        Loaded += (_, _) => { if (!minutes.Items.Cast<ComboBoxItem>().Any(i => (double)i.Tag == Minutes)) minutes.Items.Add(new ComboBoxItem { Content = L.F($"{Minutes:0} minutes"), Tag = Minutes }); minutes.SelectedItem = minutes.Items.Cast<ComboBoxItem>().First(i => (double)i.Tag == Minutes); };
        System.Windows.Automation.AutomationProperties.SetName(minutes, L.T("Countdown minutes")); clockPanel.Children.Add(minutes); layout.Children.Add(clockPanel);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        start = Ui.IconButton("Play", L.T("Start"), ToggleRunning); start.Width = start.Height = 48; start.SetResourceReference(Control.BackgroundProperty, "Accent");
        DesignTokens.SetButtonRadius(start, new CornerRadius(24)); controls.Children.Add(start); var reset = Ui.IconButton("RotateLeft", L.T("Reset"), Reset); reset.Width = reset.Height = 44; reset.Margin = new Thickness(10, 0, 10, 0); DesignTokens.SetButtonRadius(reset, new CornerRadius(22)); controls.Children.Add(reset); layout.Children.Add(controls);
        var card = Ui.Card(layout, 18); card.CornerRadius = new CornerRadius(58); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); card.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); Content = card;
        tick.Tick += OnTick;
        Closed += (_, _) => { tick.Stop(); tick.Tick -= OnTick; clock.Reset(); };
        Update();
    }
    public void ToggleRunning()
    {
        if (clock.IsRunning) { clock.Stop(); tick.Stop(); }
        else { if (Remaining == TimeSpan.Zero) clock.Reset(); clock.Start(); tick.Start(); }
        Update();
    }
    public void Reset() { clock.Reset(); tick.Stop(); Update(); }
    private void OnTick(object? sender, EventArgs e)
    {
        if (Remaining == TimeSpan.Zero) { clock.Stop(); tick.Stop(); }
        Update();
    }
    private void Update()
    {
        var remaining = TimeSpan.FromSeconds(Math.Ceiling(Remaining.TotalSeconds));
        time.Text = remaining.TotalHours >= 1 ? remaining.ToString(@"h\:mm\:ss") : remaining.ToString(@"mm\:ss");
        start.Content = Ui.Icon(IsRunning ? "Pause" : "Play", 22); Ui.Tip(start, IsRunning ? L.T("Pause") : L.T("Start"));
        System.Windows.Automation.AutomationProperties.SetName(start, IsRunning ? L.T("Pause") : L.T("Start"));
        progress.Fraction = Remaining.TotalSeconds / Math.Max(1, duration.TotalSeconds); progress.InvalidateVisual();
    }
    private sealed class CountdownRing : FrameworkElement
    {
        internal double Fraction { get; set; } = 1;
        protected override void OnRender(DrawingContext dc)
        {
            var center = new Point(40, 40); const double radius = 34;
            dc.DrawEllipse(null, new Pen(Ui.Brush("Stroke"), 6), center, radius, radius);
            double fraction = Math.Clamp(Fraction, 0, 1); if (fraction <= 0) return;
            var pen = new Pen(Ui.Brush("Accent"), 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (fraction >= .9999) { dc.DrawEllipse(null, pen, center, radius, radius); return; }
            double angle = fraction * Math.PI * 2 - Math.PI / 2;
            var path = new StreamGeometry(); using (var drawing = path.Open()) { drawing.BeginFigure(new Point(40, 6), false, false); drawing.ArcTo(new Point(40 + radius * Math.Cos(angle), 40 + radius * Math.Sin(angle)), new Size(radius, radius), 0, fraction > .5, SweepDirection.Clockwise, true, false); }
            dc.DrawGeometry(null, pen, path);
        }
    }
}
