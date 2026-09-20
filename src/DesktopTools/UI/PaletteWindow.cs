using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopTools.Native;

namespace DesktopTools.UI;

internal sealed class PaletteWindow : Window
{
    private readonly AppController controller;
    private readonly MonitorInfo monitor;
    private readonly Dictionary<string, Button> tools = [];
    private readonly Button undo;
    private readonly Button redo;
    private readonly Button swatch;
    private readonly TextBlock thickness;
    private readonly Button interact;
    public PaletteWindow(AppController controller, MonitorInfo monitor)
    {
        this.controller = controller; this.monitor = monitor;
        Title = L.T("Drawing tools"); WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true; SizeToContent = SizeToContent.WidthAndHeight;
        AllowsTransparency = true; Background = Brushes.Transparent;
        double availableWidth = Math.Max(220, monitor.WorkingArea.Width / monitor.ScaleX - 44);
        var layout = new StackPanel { MaxWidth = availableWidth }; var top = new WrapPanel { MaxWidth = availableWidth }; var bottom = new WrapPanel { MaxWidth = availableWidth, Margin = new Thickness(0, 8, 0, 0) };
        var handle = Ui.Text("⋮⋮", 21, muted: true); handle.Cursor = Cursors.SizeAll; handle.Width = 28; Ui.Tip(handle, L.T("Drag drawing tools"));
        handle.MouseLeftButtonDown += (_, e) => { if (e.ButtonState != MouseButtonState.Pressed) return; DragMove(); ClampPosition(); controller.UpdateSettings(s => { s.PaletteX = Left - monitor.Bounds.X / monitor.ScaleX; s.PaletteY = Top - monitor.Bounds.Y / monitor.ScaleY; }); };
        top.Children.Add(handle);
        var select = Ui.Button(L.T("Select"), () => controller.SetTool("Select")); Ui.Tip(select, L.T("Select, move or resize (V)")); tools["Select"] = select; top.Children.Add(select);
        foreach (var (name, key) in new[] { ("Pen", "P"), ("Highlighter", "H"), ("Arrow", "A"), ("Line", "L"), ("Rectangle", "R"), ("Ellipse", "O"), ("Text", "T"), ("Number", "N"), ("Eraser", "E") })
        {
            var b = Ui.IconButton(name, L.F($"{L.T(name)} ({key})"), () => controller.SetTool(name)); tools[name] = b; top.Children.Add(b);
        }
        swatch = Ui.Button("", controller.PickColor); swatch.Width = 36; swatch.Padding = new Thickness(8); Ui.Tip(swatch, L.T("Custom color")); System.Windows.Automation.AutomationProperties.SetName(swatch, L.T("Custom color")); top.Children.Add(swatch);
        thickness = Ui.Text(L.F($"{3} px"), 12); var thicknessButton = Ui.Button("", () => OpenOptions()); thicknessButton.Content = thickness; Ui.Tip(thicknessButton, L.T("Thickness, opacity and colors")); top.Children.Add(thicknessButton);
        undo = Ui.IconButton("Undo", L.T("Undo (Ctrl+Z)"), controller.Document.Undo); redo = Ui.IconButton("Redo", L.T("Redo (Ctrl+Y)"), controller.Document.Redo); top.Children.Add(undo); top.Children.Add(redo);
        bottom.Children.Add(Ui.IconButton("Capture", L.T("Capture region"), () => _ = controller.CaptureAsync()));
        bottom.Children.Add(Ui.IconButton("Clear", L.T("Clear all · undoable"), controller.Document.Clear));
        bottom.Children.Add(Ui.Button(L.T("Duplicate"), controller.DuplicateSelected));
        interact = Ui.Button(L.T("Interact"), () => controller.Interact()); bottom.Children.Add(interact); bottom.Children.Add(Ui.Button(L.T("Switch monitor"), controller.ChooseMonitor));
        bottom.Children.Add(Ui.Button(L.T("Hide palette"), controller.TogglePalette)); bottom.Children.Add(Ui.IconButton("Close", L.T("Hide annotations (Esc)"), () => controller.HideAnnotations(animateControls: true)));
        layout.Children.Add(top); layout.Children.Add(bottom); var card = Ui.Card(layout, 10); card.Margin = new Thickness(0); card.CornerRadius = new CornerRadius(16); card.SetResourceReference(Border.BackgroundProperty, "Surface"); Content = card;
        SourceInitialized += (_, _) => ApplyExclusion();
        Loaded += (_, _) =>
        {
            Left = monitor.Bounds.X / monitor.ScaleX + (controller.Settings.PaletteX ?? 28); Top = monitor.Bounds.Y / monitor.ScaleY + (controller.Settings.PaletteY ?? 28); ClampPosition();
        };
        AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((sender, e) => { if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) { controller.CancelActiveTool(); e.Handled = true; } else controller.ForwardKey(sender, e); }), true);
        Deactivated += (_, _) => controller.CheckDrawingFocus();
        controller.Document.Changed += Refresh; Closed += (_, _) => controller.Document.Changed -= Refresh;
    }
    private void ClampPosition()
    {
        var r = monitor.WorkingArea; double minX = r.Left / monitor.ScaleX, minY = r.Top / monitor.ScaleY;
        Left = Math.Clamp(Left, minX, Math.Max(minX, r.Right / monitor.ScaleX - ActualWidth)); Top = Math.Clamp(Top, minY, Math.Max(minY, r.Bottom / monitor.ScaleY - ActualHeight));
    }
    public void ApplyExclusion()
    {
        NativeWindowService.ApplyBackdrop(this, controller.Dark, controller.Settings.Transparency);
        if (!NativeWindowService.TryExcludeFromCapture(this, AppCapturePrivacy.ShouldHideFeature("Drawing palette", controller.Settings), out var error)) controller.Report(error ?? L.T("Controls capture exclusion is unavailable. Use Hide palette."));
    }
    public void Refresh()
    {
        foreach (var (name, button) in tools) { if (name == controller.Tool) button.SetResourceReference(BackgroundProperty, "Hover"); else button.Background = Brushes.Transparent; Ui.Tip(button, L.F($"{L.T(name)} ({controller.Settings.DrawingShortcuts[name]})")); }
        Ui.Tip(undo, L.F($"Undo ({controller.Settings.DrawingShortcuts["Undo"]})")); Ui.Tip(redo, L.F($"Redo ({controller.Settings.DrawingShortcuts["Redo"]})"));
        undo.IsEnabled = controller.Document.CanUndo; redo.IsEnabled = controller.Document.CanRedo;
        swatch.Content = new Border { Width = 19, Height = 19, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(controller.Settings.Color)) };
        thickness.Text = L.F($"{controller.Settings.Thickness.ToString("0.#")} px");
        interact.Content = L.F($"Interact · {controller.Settings.DrawingShortcuts["Interact"]}");

    }
    private void OpenOptions()
    {
        var panel = new StackPanel();
        var colors = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (string value in new[] { "#FF2870FF", "#FFFF4545", "#FFFFCF32", "#FF37C978", "#FFFFFFFF", "#FF161719" })
        {
            var b = Ui.Button("", () => controller.UpdateSettings(s => s.Color = value)); b.Content = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)) }; Ui.Tip(b, value); colors.Children.Add(b);
        }
        panel.Children.Add(colors);
        var width = new Slider { Minimum = 1, Maximum = 24, Value = controller.Settings.Thickness, Width = 160, TickFrequency = 1, IsSnapToTickEnabled = true }; width.PreviewMouseLeftButtonUp += (_, _) => controller.UpdateSettings(s => s.Thickness = width.Value); width.KeyUp += (_, _) => controller.UpdateSettings(s => s.Thickness = width.Value);
        panel.Children.Add(Ui.Row(L.T("Thickness"), null, width));
        var arrowhead = new Slider { Minimum = .5, Maximum = 3, Value = controller.Settings.ArrowHeadSize, Width = 160, TickFrequency = .25, IsSnapToTickEnabled = true };
        arrowhead.PreviewMouseLeftButtonUp += (_, _) => controller.UpdateSettings(s => s.ArrowHeadSize = arrowhead.Value); arrowhead.KeyUp += (_, _) => controller.UpdateSettings(s => s.ArrowHeadSize = arrowhead.Value);
        panel.Children.Add(Ui.Row(L.T("Arrowhead size"), L.T("Small to large"), arrowhead));
        var opacity = new Slider { Minimum = .1, Maximum = 1, Value = controller.Settings.Opacity, Width = 160 }; opacity.PreviewMouseLeftButtonUp += (_, _) => controller.UpdateSettings(s => s.Opacity = opacity.Value); opacity.KeyUp += (_, _) => controller.UpdateSettings(s => s.Opacity = opacity.Value);
        panel.Children.Add(Ui.Row(L.T("Opacity"), null, opacity));
        panel.Children.Add(Ui.Row(L.T("Filled shapes"), L.T("Rectangles and ellipses"), Ui.Toggle(controller.Settings.FillShapes, v => controller.UpdateSettings(s => s.FillShapes = v))));
        panel.Children.Add(Ui.Row(L.T("Shape snapping"), L.T("Hold Shift while drawing"), Ui.Toggle(controller.Settings.ShapeSnapping, v => controller.UpdateSettings(s => s.ShapeSnapping = v))));
        var window = new Window { Title = L.T("Drawing options"), Content = Ui.Card(panel), SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Topmost = true };
        window.SourceInitialized += (_, _) => NativeWindowService.TryExcludeFromCapture(window, AppCapturePrivacy.ShouldHideFeature("Drawing palette", controller.Settings), out _);
        panel.Children.Add(Ui.Button(L.T("Done"), window.Close)); controller.ShowControlDialog(window);
    }
}
