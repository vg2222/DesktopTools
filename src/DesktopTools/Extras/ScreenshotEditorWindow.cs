using DesktopTools.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Core;
using DesktopTools.Native;
using DesktopTools.UI;

namespace DesktopTools.Extras;

public sealed class ScreenshotEditorWindow : Window
{
    private readonly ScreenshotEditDocument _document;
    private readonly BitmapSource _image;
    private readonly Action<BitmapSource> _onExport;
    private readonly Action<string> _report;
    private readonly Canvas _surface;
    private readonly EditCanvas _drawing;
    private readonly Button _undo, _redo;
    private readonly Button _colorButton;
    private readonly Dictionary<string, Button> _tools = [];
    private readonly TextBlock _status;
    private readonly List<Point> _points = [];
    private string _tool = "Pen";
    private Color _color = Colors.Red;
    private double _thickness = 4, _opacity = 1;
    private Annotation? _movingOriginal;
    private readonly HashSet<Guid> _erasing = [];
    private bool _gesture;
    private AnnotationTextEditor? _editor;
    private Point _textOrigin;

    public ScreenshotEditorWindow(BitmapSource image, Action<BitmapSource> onExport, Action<string> report, bool applyToImage = false, string? editorLayout = null)
    {
        editorLayout ??= "B";
        _image = image; _document = new(image); _onExport = onExport; _report = report;
        Title = L.T("Edit screenshot · DesktopTools"); Width = 1060; Height = 760; MinWidth = 640; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Surface"); SetResourceReference(ForegroundProperty, "Text");
        WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent; ResizeMode = ResizeMode.CanResizeWithGrip;
        MinWidth = 860; MinHeight = 500;
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = UtilityWindowChrome.Header(this, "DesktopTools — " + L.T("Edit screenshot"), Close, L.T("Close"), 13); layout.Children.Add(header);
        var work = new Grid(); work.ColumnDefinitions.Add(new ColumnDefinition()); work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) }); Grid.SetRow(work, 2); layout.Children.Add(work);
        _surface = new Canvas { Width = image.PixelWidth, Height = image.PixelHeight, Background = Brushes.Transparent, ClipToBounds = true, Cursor = Cursors.Cross };
        _drawing = new EditCanvas(image, _document) { Width = image.PixelWidth, Height = image.PixelHeight, IsHitTestVisible = false }; _surface.Children.Add(_drawing);
        var backdrop = new Border { Child = new Viewbox { Child = _surface, Stretch = Stretch.Uniform }, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 12, 0) };
        backdrop.SetResourceReference(Border.BackgroundProperty, "Card"); backdrop.SetResourceReference(Border.BorderBrushProperty, "Stroke"); work.Children.Add(backdrop);
        var properties = new StackPanel(); properties.Children.Add(Ui.Text(L.T("Properties"), 13, true));
        var colorLabel = Ui.Text(L.T("Color"), 12); colorLabel.Margin = new Thickness(0, 18, 0, 10); properties.Children.Add(colorLabel);
        var colors = new WrapPanel();
        foreach (var color in new[] { Colors.DodgerBlue, Colors.Crimson, Colors.Coral, Colors.Gold, Colors.LightGreen, Colors.MediumOrchid })
        {
            var swatch = Ui.Button("", () => { CommitText(); _color = color; Update(); }); swatch.Width = swatch.Height = swatch.MinHeight = 25; swatch.Padding = new Thickness(3); swatch.Margin = new Thickness(0, 0, 5, 6); swatch.Background = new SolidColorBrush(color); DesignTokens.SetButtonRadius(swatch, new CornerRadius(13));
            Ui.Tip(swatch, color.ToString()); System.Windows.Automation.AutomationProperties.SetName(swatch, color.ToString()); colors.Children.Add(swatch);
        }
        properties.Children.Add(colors);
        _colorButton = Ui.Button("", () => { CommitText(); new ColorPickerWindow(_color.ToString(), value => { _color = (Color)ColorConverter.ConvertFromString(value); Update(); return true; }) { Owner = this }.ShowDialog(); }); properties.Children.Add(_colorButton);
        System.Windows.Automation.AutomationProperties.SetName(_colorButton, L.T("Annotation color"));
        var strokeLabel = Ui.Text(L.T("Stroke thickness"), 12); strokeLabel.Margin = new Thickness(0, 18, 0, 8); properties.Children.Add(strokeLabel);
        var widthChoice = new Slider { Minimum = 1, Maximum = 32, Value = _thickness, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 4 };
        System.Windows.Automation.AutomationProperties.SetName(widthChoice, L.T("Stroke thickness")); widthChoice.ValueChanged += (_, _) => _thickness = widthChoice.Value; properties.Children.Add(widthChoice);
        var opacityLabel = Ui.Text(L.T("Opacity"), 12); opacityLabel.Margin = new Thickness(0, 18, 0, 8); properties.Children.Add(opacityLabel);
        var opacity = new Slider { Minimum = .1, Maximum = 1, Value = 1 }; System.Windows.Automation.AutomationProperties.SetName(opacity, L.T("Annotation opacity")); opacity.ValueChanged += (_, _) => _opacity = opacity.Value; properties.Children.Add(opacity);
        _status = Ui.Text("", 11, muted: true); _status.Margin = new Thickness(0, 18, 0, 0); properties.Children.Add(_status);
        var propertyCard = Ui.Card(properties, 14); propertyCard.VerticalAlignment = VerticalAlignment.Top; propertyCard.Margin = new Thickness(0); Grid.SetColumn(propertyCard, 1); work.Children.Add(propertyCard);
        var tools = new WrapPanel();
        void SelectTool(string tool) { CommitText(); CancelGesture(); _tool = tool; Update(); }
        foreach (var tool in new[] { "Select", "Pen", "Highlighter", "Arrow", "Rectangle", "Ellipse", "Text", "Eraser", "Crop" })
        {
            var button = Ui.IconButton(tool, L.T(tool), () => SelectTool(tool)); button.Width = button.Height = button.MinHeight = 32; button.Padding = new Thickness(7); button.Margin = new Thickness(1); _tools.Add(tool, button); tools.Children.Add(button);
        }
        var more = Ui.IconButton("More", L.T("More tools"), () => { }); more.Width = more.Height = more.MinHeight = 32;
        var menu = new ContextMenu();
        foreach (var tool in new[] { "Line", "Number", "Redaction", "Eyedropper" }) { var item = new MenuItem { Header = L.T(tool == "Redaction" ? "Cover" : tool), Icon = Ui.Icon(tool, 16) }; item.Click += (_, _) => SelectTool(tool); menu.Items.Add(item); }
        menu.Items.Add(new Separator());
        var clear = new MenuItem { Header = L.T("Clear"), Icon = Ui.Icon("Clear", 16) }; clear.Click += (_, _) => { CommitText(); CancelGesture(); _document.Clear(); Update(); }; menu.Items.Add(clear);
        var resetCrop = new MenuItem { Header = L.T("Reset crop"), Icon = Ui.Icon("Crop", 16) }; resetCrop.Click += (_, _) => { CommitText(); CancelGesture(); _document.SetCrop(new(0, 0, image.PixelWidth, image.PixelHeight)); Update(); }; menu.Items.Add(resetCrop);
        var ocr = new MenuItem { Header = L.T("Extract text"), Icon = Ui.Icon("ScanText", 16) }; ocr.Click += (_, _) => ExtractText(); menu.Items.Add(ocr);
        more.ContextMenu = menu; more.Click += (_, _) => { menu.PlacementTarget = more; menu.IsOpen = true; }; tools.Children.Add(more); Closed += (_, _) => menu.IsOpen = false;
        _undo = Ui.IconButton("Undo", L.T("Undo"), Undo); _redo = Ui.IconButton("Redo", L.T("Redo"), Redo); _undo.Width = _redo.Width = 32; tools.Children.Add(_undo); tools.Children.Add(_redo);
        var toolCard = Ui.Card(tools, 6); toolCard.Margin = new Thickness(0, 0, 12, 0); toolCard.VerticalAlignment = VerticalAlignment.Center;
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
        if (applyToImage) { var apply = Ui.Button(L.T("Apply to image"), () => Export("Apply"), true); apply.Content = Ui.IconLabel("Check", L.T("Apply to image"), primary: true); actions.Children.Add(apply); }
        else
        {
            var copy = Ui.Button(L.T("Copy screenshot"), () => Export("Copy")); copy.Content = Ui.IconLabel("Copy", L.T("Copy image")); actions.Children.Add(copy);
            var save = Ui.Button(L.T("Save PNG"), () => Export("Save"), true); save.Content = Ui.IconLabel("Save", L.T("Save PNG"), primary: true); actions.Children.Add(save);
        }
        if (editorLayout == "A") { Grid.SetRow(toolCard, 1); toolCard.Margin = new Thickness(0, 0, 0, 12); layout.Children.Add(toolCard); }
        else footer.Children.Add(toolCard);
        Grid.SetRow(footer, 3); layout.Children.Add(footer);
        var guide = FeatureTourButton.Create(this, "screenshot-editor", () => new GuidedTour.Step[]
        {
            new(() => toolCard, "Annotation toolbar", "Choose a drawing tool, selection, eraser or crop. More tools are available in the menu."),
            new(() => backdrop, "Image canvas", "Draw on the image. Select moves an annotation; Undo restores the previous change."),
            new(() => propertyCard, "Properties", "Choose color, thickness and opacity for your annotations."),
            new(() => actions, "Export image", applyToImage ? "Apply returns the edited image to the image editor without saving a file." : "Copy sends the result to the clipboard. Save creates a PNG file.")
        }); DockPanel.SetDock(guide, Dock.Right); header.Children.Insert(1, guide);
        var shell = Ui.Card(layout, 14); shell.Margin = new Thickness(0); shell.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); shell.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); Content = shell;
        Loaded += (_, _) => Motion.Reveal(layout);
        _surface.MouseLeftButtonDown += Begin; _surface.MouseMove += Move; _surface.MouseLeftButtonUp += Finish;
        _surface.LostMouseCapture += LostCapture; PreviewKeyDown += OnKey; Closed += Cleanup;
        Update();
    }
    private Point Position(MouseEventArgs e) { var p = e.GetPosition(_surface); return new(Math.Clamp(p.X, 0, _image.PixelWidth), Math.Clamp(p.Y, 0, _image.PixelHeight)); }
    private Annotation Current() => new() { Kind = _tool == "Crop" ? AnnotationKind.Rectangle : Enum.Parse<AnnotationKind>(_tool), Points = _points.ToArray(), Color = _color, Thickness = _thickness, Opacity = _opacity };
    private void Begin(object sender, MouseButtonEventArgs e)
    {
        if (_editor != null && _editor.IsMouseOver) return;
        CommitText(); CancelGesture(); var point = Position(e);
        if (_tool is "Select" or "Eraser")
        {
            var hit = _document.Items.LastOrDefault(a => AnnotationRenderer.Hit(a, point));
            _drawing.SelectedId = _tool == "Select" ? hit?.Id : null;
            if (hit != null)
            {
                if (_tool == "Eraser") { _erasing.Add(hit.Id); _drawing.HiddenIds = _erasing; }
                else { _movingOriginal = hit; _drawing.Pending = hit; }
                _points.Add(point); _gesture = true; _surface.CaptureMouse();
            }
            _drawing.InvalidateVisual(); e.Handled = true; return;
        }
        if (_tool == "Number")
        {
            int next = _document.Items.Where(a => a.Kind == AnnotationKind.Number).Select(a => a.Number).DefaultIfEmpty(0).Max() + 1;
            _document.Add(new Annotation { Kind = AnnotationKind.Number, Points = [point], Number = next, Color = _color, FontSize = 24 });
            Update(); e.Handled = true; return;
        }
        if (_tool == "Eyedropper")
        {
            int x = Math.Min((int)point.X, _image.PixelWidth - 1), y = Math.Min((int)point.Y, _image.PixelHeight - 1);
            var crop = _document.Crop;
            if (x >= crop.X && y >= crop.Y && x < crop.X + crop.Width && y < crop.Y + crop.Height)
            {
                try { _color = ScreenshotPixel.Read(_document.Export(), x - crop.X, y - crop.Y); Update(); }
                catch (Exception ex) { _report(L.T("Could not sample this pixel: ") + ex.Message); }
            }
            e.Handled = true; return;
        }
        if (_tool == "Text")
        {
            OpenText(point); e.Handled = true; return;
        }
        _points.Add(point); _gesture = true; _surface.CaptureMouse(); _drawing.Pending = Current(); _drawing.InvalidateVisual(); e.Handled = true;
    }
    private void Move(object sender, MouseEventArgs e)
    {
        if (!_gesture) return;
        var point = Position(e);
        if (_tool == "Select" && _movingOriginal != null)
        {
            var bounds = AnnotationRenderer.Bounds(_movingOriginal); var delta = point - _points[0];
            delta.X = Math.Clamp(delta.X, -bounds.Left, Math.Max(-bounds.Left, _image.PixelWidth - bounds.Right));
            delta.Y = Math.Clamp(delta.Y, -bounds.Top, Math.Max(-bounds.Top, _image.PixelHeight - bounds.Bottom));
            _drawing.Pending = AnnotationTransform.Move(_movingOriginal, delta); _drawing.InvalidateVisual(); e.Handled = true; return;
        }
        if (_tool == "Eraser")
        {
            var hit = _document.Items.LastOrDefault(a => !_erasing.Contains(a.Id) && AnnotationRenderer.Hit(a, point)); if (hit != null) _erasing.Add(hit.Id);
            _drawing.InvalidateVisual(); e.Handled = true; return;
        }
        if (_tool is "Pen" or "Highlighter") { if ((point - _points[^1]).Length > .5) _points.Add(point); }
        else if (_points.Count == 1) _points.Add(point); else _points[1] = point;
        _drawing.Pending = Current(); _drawing.InvalidateVisual(); e.Handled = true;
    }
    private void Finish(object sender, MouseButtonEventArgs e)
    {
        if (!_gesture) return; Move(sender, e);
        if (_tool == "Select") { if (_drawing.Pending != null && _movingOriginal != null && !_drawing.Pending.Points.SequenceEqual(_movingOriginal.Points)) _document.Replace(_drawing.Pending); }
        else if (_tool == "Eraser") _document.Remove(_erasing);
        else if (_tool == "Crop")
        {
            var rect = new Rect(_points[0], _points[^1]);
            int x = (int)Math.Floor(rect.X), y = (int)Math.Floor(rect.Y);
            int width = (int)Math.Ceiling(rect.Right) - x, height = (int)Math.Ceiling(rect.Bottom) - y;
            if (width > 0 && height > 0) _document.SetCrop(new(x, y, width, height));
        }
        else if (_tool is "Pen" or "Highlighter" || _points.Count > 1) _document.Add(Current());
        CancelGesture(); Update(); e.Handled = true;
    }
    private void OpenText(Point point)
    {
        _editor = new AnnotationTextEditor(point, new Size(_image.PixelWidth, _image.PixelHeight), _color, 24); _textOrigin = _editor.ExportOrigin;
        _editor.CommitRequested += CommitText; _editor.CancelRequested += () => { if (_editor != null) { _surface.Children.Remove(_editor); _editor = null; } };
        _surface.Children.Add(_editor); _editor.Focus();
    }
    private void CommitText()
    {
        if (_editor == null) return;
        if (!string.IsNullOrWhiteSpace(_editor.Text)) _document.Add(new Annotation { Kind = AnnotationKind.Text, Points = [_textOrigin], Text = _editor.Text, TextWidth = _editor.ExportWidth, FontFamily = _editor.FontFamily.Source, FontSize = _editor.FontSize, Bold = _editor.FontWeight == FontWeights.Bold, Italic = _editor.FontStyle == FontStyles.Italic, Color = ((SolidColorBrush)_editor.Foreground).Color, Opacity = _opacity });
        _surface.Children.Remove(_editor); _editor = null; Update();
    }
    private void CancelGesture() { _gesture = false; _movingOriginal = null; _erasing.Clear(); _drawing.HiddenIds = null; _points.Clear(); _drawing.Pending = null; if (_surface.IsMouseCaptured) _surface.ReleaseMouseCapture(); _drawing.InvalidateVisual(); }
    private void LostCapture(object sender, MouseEventArgs e) { if (_gesture) CancelGesture(); }
    private void Undo() { CommitText(); CancelGesture(); _document.Undo(); Update(); }
    private void Redo() { CommitText(); CancelGesture(); _document.Redo(); Update(); }
    private void Update()
    {
        _undo.IsEnabled = _document.CanUndo; _redo.IsEnabled = _document.CanRedo;
        var colorContent = new StackPanel { Orientation = Orientation.Horizontal };
        colorContent.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(_color), BorderBrush = Ui.Brush("Stroke"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0) });
        colorContent.Children.Add(Ui.Text(_color.ToString(), 12)); _colorButton.Content = colorContent;
        foreach (var (tool, button) in _tools) button.SetResourceReference(BackgroundProperty, tool == _tool ? "Selected" : "Field");
        var hint = _tool switch { "Crop" => L.T("Drag to select the area to keep."), "Text" => L.T("Click to add text. Ctrl+Enter to finish."), "Number" => L.T("Click to add the next numbered step."), "Eyedropper" => L.T("Click inside the crop to choose its pixel color."), "Redaction" => L.T("Drag an opaque cover over private details."), _ => L.T("Drag on the image to annotate.") };
        _status.Text = L.F($"{_document.Crop.Width} × {_document.Crop.Height} pixels   •   {hint}");
        _drawing.InvalidateVisual();
    }
    private void ExtractText()
    {
        CommitText(); CancelGesture();
        try { new OcrTextWindow(_document.Export(), _report) { Owner = this }.ShowDialog(); }
        catch (Exception ex) { _report(L.T("Could not extract text: ") + ex.Message); }
    }
    private void Export(string action)
    {
        CommitText(); CancelGesture();
        try
        {
            var result = _document.Export();
            if (action == "Save") ImageOutput.Save(this, result, _report);
            else _onExport(result);
            if (action == "Apply") { Close(); return; }
        }
        catch (Exception ex) { _report(L.T("Could not export the screenshot: ") + ex.Message); }
    }
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_editor != null) { _surface.Children.Remove(_editor); _editor = null; }
            else if (_gesture) CancelGesture(); else Close(); e.Handled = true;
        }
        else if (_editor != null) { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { CommitText(); e.Handled = true; } }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Z or Key.Y) { if (e.Key == Key.Z) Undo(); else Redo(); e.Handled = true; }
    }
    private void Cleanup(object? sender, EventArgs args)
    {
        CancelGesture(); _surface.MouseLeftButtonDown -= Begin; _surface.MouseMove -= Move; _surface.MouseLeftButtonUp -= Finish; _surface.LostMouseCapture -= LostCapture;
        PreviewKeyDown -= OnKey; Closed -= Cleanup; _surface.Children.Clear(); _editor = null; Content = null;
    }
    private sealed class EditCanvas(BitmapSource image, ScreenshotEditDocument document) : FrameworkElement
    {
        public Annotation? Pending { get; set; }
        public Guid? SelectedId { get; set; }
        public HashSet<Guid>? HiddenIds { get; set; }
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            AnnotationRenderer.Draw(dc, document.Items.Where(a => a.Kind != AnnotationKind.Redaction && a.Id != Pending?.Id && HiddenIds?.Contains(a.Id) != true));
            if (Pending != null && Pending.Kind != AnnotationKind.Redaction) AnnotationRenderer.Draw(dc, [Pending]);
            foreach (var cover in document.Items.Where(a => a.Kind == AnnotationKind.Redaction && a.Id != Pending?.Id && HiddenIds?.Contains(a.Id) != true).Concat(Pending?.Kind == AnnotationKind.Redaction ? new[] { Pending } : Array.Empty<Annotation>()))
                if (cover.Points.Count > 1) dc.DrawRectangle(Brushes.Black, null, new Rect(cover.Points[0], cover.Points[^1]));
            var selected = Pending?.Id == SelectedId ? Pending : document.Items.FirstOrDefault(a => a.Id == SelectedId);
            if (selected != null) dc.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 1.5) { DashStyle = DashStyles.Dash }, AnnotationRenderer.Bounds(selected));
            var crop = document.Crop;
            var bounds = new Rect(crop.X, crop.Y, crop.Width, crop.Height);
            var mask = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(RenderSize)), new RectangleGeometry(bounds));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, mask);
            if (crop.Width != image.PixelWidth || crop.Height != image.PixelHeight) dc.DrawRectangle(null, new Pen(Brushes.White, 1), bounds);
        }
    }
}
