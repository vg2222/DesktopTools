using DesktopTools.Localization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopTools.Core;
using DesktopTools.UI;

namespace DesktopTools.Extras;
internal sealed class TeleprompterWindow : Window
{
    private readonly TextBox editor;
    private readonly TextBlock script;
    private readonly ScrollViewer scroll;
    private readonly Button play;
    private readonly Func<Action<AppSettings>, bool> save;
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly Stopwatch elapsed = new();
    private double lastTime, speed;
    private bool running, dirty, closed, presentation;
    private readonly StackPanel outline = new();
    private readonly ColumnDefinition outlineColumn = new() { Width = new GridLength(205) };
    private readonly Button mode;
    private double studioWidth = 820, studioHeight = 620, studioLeft, studioTop;
    internal bool IsPresentation => presentation;
    internal bool IsRunning => running;
    public TeleprompterWindow(AppSettings settings, Func<Action<AppSettings>, bool> save)
    {
        this.save = save; speed = settings.TeleprompterSpeed;
        Title = L.T("Teleprompter"); Width = studioWidth; Height = studioHeight; MinWidth = 640; MinHeight = 360; Topmost = settings.TeleprompterTopmost;
        WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent; ResizeMode = ResizeMode.CanResizeWithGrip; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel(); var header = UtilityWindowChrome.Header(this, L.T("Teleprompter"), Close, L.T("Close teleprompter"), 18); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var pin = UtilityWindowChrome.CaptionButton("Pin", L.T("Always on top"), () => { if (save(s => s.TeleprompterTopmost = !Topmost)) Topmost = !Topmost; }); DockPanel.SetDock(pin, Dock.Right); header.Children.Insert(1, pin);
        editor = new TextBox { Text = settings.TeleprompterText, FontSize = settings.TeleprompterFontSize, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, MaxLength = 50000, Padding = new Thickness(18), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        script = Ui.Text(settings.TeleprompterText, settings.TeleprompterFontSize); script.Padding = new Thickness(24, 24, 24, 160); script.VerticalAlignment = VerticalAlignment.Top;
        scroll = new ScrollViewer { Content = script, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Visibility = Visibility.Collapsed };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        play = Ui.IconButton("Play", L.T("Start"), TogglePlayback); play.Width = play.Height = 42; play.SetResourceReference(Control.BackgroundProperty, "Accent"); DesignTokens.SetButtonRadius(play, new CornerRadius(21)); toolbar.Children.Add(play);
        var restart = Ui.IconButton("Stop", L.T("Restart"), () => { Pause(); scroll.ScrollToTop(); }); restart.Margin = new Thickness(8, 0, 14, 0); toolbar.Children.Add(restart);
        StackPanel Group(string label, FrameworkElement control)
        {
            var group = new StackPanel { Margin = new Thickness(10, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
            group.Children.Add(Ui.Text(L.T(label), 10, muted: true)); group.Children.Add(control); return group;
        }
        var velocity = new ComboBox { Width = 86, Margin = new Thickness(6, 0, 6, 0), IsEditable = false };
        foreach (double value in new[] { 10d, 20, 40, 60, 90, 120, 180 }.Append(speed).Distinct().OrderBy(v => v)) velocity.Items.Add(value);
        velocity.SelectedItem = speed; velocity.ToolTip = L.T("Pixels per second"); System.Windows.Automation.AutomationProperties.SetName(velocity, L.T("Speed"));
        velocity.SelectionChanged += (_, _) => { if (velocity.SelectedItem is double value && save(s => s.TeleprompterSpeed = value)) speed = value; }; toolbar.Children.Add(Group("Speed", velocity));
        var font = new ComboBox { Width = 72, Margin = new Thickness(0, 0, 6, 0) };
        foreach (double value in new[] { 18d, 24, 32, 40, 48, 64 }.Append(script.FontSize).Distinct().OrderBy(v => v)) font.Items.Add(value);
        font.SelectedItem = script.FontSize; font.ToolTip = L.T("Text size"); System.Windows.Automation.AutomationProperties.SetName(font, L.T("Text size"));
        font.SelectionChanged += (_, _) => { if (font.SelectedItem is double value && save(s => s.TeleprompterFontSize = value)) script.FontSize = editor.FontSize = value; }; toolbar.Children.Add(Group("Text size", font));
        bool mirrored = false;
        toolbar.Children.Add(Ui.IconButton("FlipHorizontal", L.T("Mirror"), () => { mirrored = !mirrored; script.RenderTransformOrigin = new Point(.5, .5); script.RenderTransform = new ScaleTransform(mirrored ? -1 : 1, 1); }));
        mode = Ui.IconButton("Maximize", L.T("Presentation mode"), () => SetPresentation(!presentation)); DockPanel.SetDock(mode, Dock.Right); header.Children.Insert(1, mode);
        var guide = FeatureTourButton.Create(this, "teleprompter", () => new GuidedTour.Step[]
        {
            new(() => editor.IsVisible ? editor : scroll, "Script", "Write your script here. Your text stays on this device."),
            new(() => velocity, "Speed", "Choose a comfortable reading speed before pressing Play."),
            new(() => font, "Text size", "Adjust text size to your viewing distance."),
            new(() => mode, "Presentation mode", "Switch between the studio and the compact reading window without losing your place.")
        }, settings, save); DockPanel.SetDock(guide, Dock.Right); header.Children.Insert(1, guide);
        AddHandler(FeatureTourButton.SetupAppliedEvent, new RoutedEventHandler((_, _) => { velocity.SelectedItem = settings.TeleprompterSpeed; font.SelectedItem = settings.TeleprompterFontSize; Topmost = settings.TeleprompterTopmost; }));
        var edit = Ui.IconButton("Text", L.T("Edit text"), () => { Pause(); if (presentation) SetPresentation(false); scroll.Visibility = Visibility.Collapsed; editor.Visibility = Visibility.Visible; editor.Focus(); });
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(edit, Dock.Right); footer.Children.Add(edit); footer.Children.Add(toolbar);
        var footerCard = Ui.Card(footer, 10); footerCard.Margin = new Thickness(0, 12, 0, 0); DockPanel.SetDock(footerCard, Dock.Bottom); root.Children.Add(footerCard);
        var content = new Grid(); content.ColumnDefinitions.Add(outlineColumn); content.ColumnDefinitions.Add(new ColumnDefinition());
        var sections = new DockPanel { Margin = new Thickness(0, 8, 14, 0) };
        var label = Ui.Text(L.T("Script sections"), 13, true); label.Margin = new Thickness(0, 0, 0, 10); DockPanel.SetDock(label, Dock.Top); sections.Children.Add(label);
        sections.Children.Add(new ScrollViewer { Content = outline, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); content.Children.Add(sections);
        var reading = new Grid(); reading.Children.Add(scroll); reading.Children.Add(editor); Grid.SetColumn(reading, 1); content.Children.Add(reading); root.Children.Add(content);
        var card = Ui.Card(root, 18); card.Margin = new Thickness(0); card.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); card.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); Content = card;
        System.Windows.Automation.AutomationProperties.SetName(editor, L.T("Teleprompter script"));
        UpdateOutline();
        editor.TextChanged += (_, _) => { dirty = true; debounce.Stop(); debounce.Start(); };
        debounce.Tick += (_, _) => { Flush(); UpdateOutline(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Pause(); e.Handled = true; }
            else if (!editor.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Space) { TogglePlayback(); e.Handled = true; }
            else if (!editor.IsKeyboardFocusWithin && e.Key == Key.Home) { Pause(); scroll.ScrollToTop(); e.Handled = true; }
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) Pause(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Pause(); };
        Closing += (_, e) => { Pause(); Flush(); if (dirty) e.Cancel = true; };
        Closed += (_, _) => { closed = true; debounce.Stop(); }; Motion.WindowEntrance(this);
    }
    internal void SetPresentation(bool value)
    {
        if (presentation == value) return;
        Flush(); if (dirty) return;
        presentation = value;
        if (value)
        {
            studioWidth = Width; studioHeight = Height; studioLeft = Left; studioTop = Top;
            outlineColumn.Width = new GridLength(0); Height = 420;
            script.Text = editor.Text; editor.Visibility = Visibility.Collapsed; scroll.Visibility = Visibility.Visible;
        }
        else
        {
            outlineColumn.Width = new GridLength(205); Width = studioWidth; Height = studioHeight; Left = studioLeft; Top = studioTop;
        }
        Ui.Tip(mode, L.T(value ? "Studio mode" : "Presentation mode")); System.Windows.Automation.AutomationProperties.SetName(mode, L.T(value ? "Studio mode" : "Presentation mode"));
    }
    private void UpdateOutline()
    {
        outline.Children.Clear(); int offset = 0, index = 0;
        foreach (string line in editor.Text.Split('\n'))
        {
            int start = offset; offset += line.Length + 1;
            if (string.IsNullOrWhiteSpace(line)) continue;
            string heading = line.Trim();
            var text = Ui.Text($"{++index}. " + heading[..Math.Min(72, heading.Length)], 12); text.MaxHeight = 60;
            var section = Ui.Button("", () => { Pause(); if (editor.Visibility == Visibility.Visible) { editor.Focus(); editor.Select(Math.Min(start, editor.Text.Length), 0); editor.ScrollToLine(editor.GetLineIndexFromCharacterIndex(Math.Min(start, editor.Text.Length))); } else scroll.ScrollToVerticalOffset(scroll.ScrollableHeight * start / Math.Max(1, editor.Text.Length)); });
            section.Content = text; section.Margin = new Thickness(0, 0, 0, 6); section.HorizontalContentAlignment = HorizontalAlignment.Stretch; outline.Children.Add(section);
            if (index >= 100) break;
        }
        if (index == 0) outline.Children.Add(Ui.Text(L.T("Write your script to see its sections here."), 12, muted: true));
    }
    private void Flush() { debounce.Stop(); if (!dirty) return; dirty = false; if (!save(s => s.TeleprompterText = editor.Text)) dirty = true; }
    public void TogglePlayback()
    {
        if (closed) return;
        if (!IsVisible) Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (running) { Pause(); return; }
        if (string.IsNullOrWhiteSpace(editor.Text)) { editor.Focus(); return; }
        Flush(); script.Text = editor.Text; editor.Visibility = Visibility.Collapsed; scroll.Visibility = Visibility.Visible; scroll.Focus(); scroll.UpdateLayout();
        if (scroll.VerticalOffset >= scroll.ScrollableHeight) scroll.ScrollToTop();
        running = true; SetPlayIcon(); elapsed.Restart(); lastTime = 0; CompositionTarget.Rendering += Tick;
    }
    private void Tick(object? sender, EventArgs args)
    {
        double now = elapsed.Elapsed.TotalSeconds, dt = Math.Min(.1, now-lastTime); lastTime = now;
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset + dt * speed);
        if (scroll.VerticalOffset >= scroll.ScrollableHeight) Pause();
    }
    private void SetPlayIcon() { play.Content = Ui.Icon(running ? "Pause" : "Play"); Ui.Tip(play, L.T(running ? "Pause" : "Start")); System.Windows.Automation.AutomationProperties.SetName(play, L.T(running ? "Pause" : "Start")); }
    internal void Pause() { CompositionTarget.Rendering -= Tick; elapsed.Stop(); running = false; SetPlayIcon(); }
}
