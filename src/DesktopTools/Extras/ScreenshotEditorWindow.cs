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

public sealed partial class ScreenshotEditorWindow : Window
{
    private readonly ScreenshotEditDocument _document;
    private readonly BitmapSource _image;
    private readonly Action<BitmapSource> _onExport;
    private readonly Action<string> _report;
    private readonly Canvas _surface;
    private readonly EditCanvas _drawing;
    private readonly Button _undo, _redo;
    private readonly Button _originalButton;
    private readonly Image _originalLayer;
    private bool _showingOriginal;
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

    public ScreenshotEditorWindow(BitmapSource image, Action<BitmapSource> onExport, Action<string> report, bool applyToImage = false, string? editorLayout = null,
        string? applyLabel = null, bool offerOriginal = false, AutoRedactOptions? autoRedact = null, bool reviewBeforeOutput = false,
        Func<BitmapSource, ScreenshotExportAction, Task<bool>>? exportAsync = null)
    {
        editorLayout ??= "B";
        _image = image; _document = new(image); _onExport = onExport; _report = report;
        Title = L.T(applyLabel == null ? "Edit screenshot · DesktopTools" : "Edit guide image"); Width = 1060; Height = 760; MinWidth = 640; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Surface"); SetResourceReference(ForegroundProperty, "Text");
        WindowStyle = WindowStyle.None; UtilityWindowChrome.EnableBackdrop(this); Background = Brushes.Transparent; ResizeMode = ResizeMode.CanResizeWithGrip;
        MinWidth = 860; MinHeight = 500;
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = UtilityWindowChrome.Header(this, "DesktopTools — " + L.T(applyLabel == null ? "Edit screenshot" : "Edit guide image"), Close, L.T("Close"), 13, allowMinimize: true); layout.Children.Add(header);
        var work = new Grid(); work.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 250 }); work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) }); work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300), MinWidth = 240 }); Grid.SetRow(work, 2); layout.Children.Add(work);
        var divider = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext, Background = Brushes.Transparent };
        Grid.SetColumn(divider, 1); work.Children.Add(divider);
        _surface = new Canvas { Width = image.PixelWidth, Height = image.PixelHeight, Background = Brushes.Transparent, ClipToBounds = true, Cursor = Cursors.Cross };
        _originalLayer = new Image { Source = image, Width = image.PixelWidth, Height = image.PixelHeight, Stretch = Stretch.Fill, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        _surface.Children.Add(_originalLayer);
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
        var widthChoice = new Slider { Minimum = 1, Maximum = 32, Value = _thickness, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 4 };
        widthChoice.ValueChanged += (_, _) => _thickness = widthChoice.Value;
        var strokeField = Ui.SliderField(L.T("Stroke thickness"), widthChoice, v => $"{v:0} px"); strokeField.Margin = new Thickness(0, 18, 0, 0); properties.Children.Add(strokeField);
        var opacity = new Slider { Minimum = .1, Maximum = 1, Value = 1 }; opacity.ValueChanged += (_, _) => _opacity = opacity.Value;
        var opacityField = Ui.SliderField(L.T("Opacity"), opacity, v => $"{v * 100:0}%"); opacityField.Margin = new Thickness(0, 14, 0, 0); properties.Children.Add(opacityField);
        System.Windows.Automation.AutomationProperties.SetName(opacity, L.T("Annotation opacity"));
        _status = Ui.Text("", 11, muted: true); _status.Margin = new Thickness(0, 18, 0, 0); properties.Children.Add(_status);
        var propertyCard = Ui.Card(new ScrollViewer { Content = properties, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 14); propertyCard.Margin = new Thickness(0); Grid.SetColumn(propertyCard, 2); work.Children.Add(propertyCard);
        var tools = new WrapPanel();
        void SelectTool(string tool) { if (_showingOriginal) ToggleOriginal(); CommitText(); CancelGesture(); _tool = tool; Update(); }
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
        tools.Children.Add(ToolbarDivider());
        _undo = Ui.IconButton("Undo", L.T("Undo"), Undo); _redo = Ui.IconButton("Redo", L.T("Redo"), Redo); _undo.Width = _redo.Width = 32; tools.Children.Add(_undo); tools.Children.Add(_redo);
        tools.Children.Add(ToolbarDivider());
        _originalButton = Ui.IconButton("Image", L.T("Show original"), ToggleOriginal); _originalButton.Width = 32; tools.Children.Add(_originalButton);
        var toolCard = Ui.Card(tools, 6); toolCard.Margin = new Thickness(0, 0, 12, 0); toolCard.VerticalAlignment = VerticalAlignment.Center;
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
        if (applyToImage)
        {
            if (offerOriginal)
            {
                var original = Ui.Button(L.T("Add original"), () => { _onExport(_image); Close(); });
                original.Content = Ui.IconLabel("Image", L.T("Add original")); actions.Children.Add(original);
            }
            string label = applyLabel == null ? L.T("Apply to image") : L.T(applyLabel);
            var apply = Ui.Button(label, () => Export("Apply"), true); apply.Content = Ui.IconLabel("Check", label, primary: true); actions.Children.Add(apply);
        }
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
            new(() => actions, "Export image", applyLabel != null ? "Use the edited copy in the guide; the source image stays unchanged." : applyToImage ? "Apply returns the edited image to the image editor without saving a file." : "Copy sends the result to the clipboard. Save creates a PNG file.")
        }); DockPanel.SetDock(guide, Dock.Right); header.Children.Insert(header.Children.Count - 1, guide);
        var shell = Ui.Card(layout, 14); shell.Margin = new Thickness(0); shell.SetResourceReference(Border.BackgroundProperty, "GlassSurface"); shell.SetResourceReference(Border.BorderBrushProperty, "GlassRim"); Content = shell;
        Loaded += InitializeEditorSize;
        InitializeRedaction(properties, tools, actions, autoRedact, reviewBeforeOutput, exportAsync);
        Loaded += (_, _) => Motion.Reveal(layout);
        _surface.MouseLeftButtonDown += Begin; _surface.MouseMove += Move; _surface.MouseLeftButtonUp += Finish;
        _surface.LostMouseCapture += LostCapture; PreviewKeyDown += OnKey; Closed += Cleanup;
        Update();
    }
    private void InitializeEditorSize(object sender, RoutedEventArgs args)
    {
        Loaded -= InitializeEditorSize;
        var monitor = MonitorService.GetForWindow(this);
        var area = monitor.WorkingArea;
        int width = Math.Max(1, (int)Math.Round(area.Width * .75)), height = Math.Max(1, (int)Math.Round(area.Height * .75));
        MinWidth = Math.Min(860, width / monitor.ScaleX); MinHeight = Math.Min(500, height / monitor.ScaleY);
        Width = width / monitor.ScaleX; Height = height / monitor.ScaleY;
        // Work area and HWND placement use physical pixels, including monitors
        // with negative origins; WPF dimensions above remain monitor-local DIPs.
        NativeMethods.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(this).Handle, IntPtr.Zero,
            (int)Math.Round(area.Left + (area.Width - width) / 2), (int)Math.Round(area.Top + (area.Height - height) / 2),
            width, height, 0x0004 | 0x0010);
    }
    private static Border ToolbarDivider()
    {
        var divider = new Border { Width = 1, Height = 22, Margin = new Thickness(7, 5, 7, 5), VerticalAlignment = VerticalAlignment.Center };
        divider.SetResourceReference(Border.BackgroundProperty, "Divider"); return divider;
    }
    private Point Position(MouseEventArgs e) { var p = e.GetPosition(_surface); return new(Math.Clamp(p.X, 0, _image.PixelWidth), Math.Clamp(p.Y, 0, _image.PixelHeight)); }
    private Annotation Current() => new() { Kind = _tool == "Crop" ? AnnotationKind.Rectangle : Enum.Parse<AnnotationKind>(_tool), Points = _points.ToArray(), Color = _color, Thickness = _thickness, Opacity = _opacity, RedactionStyle = _redactionStyle };
    private void Begin(object sender, MouseButtonEventArgs e)
    {
        if (_showingOriginal) { e.Handled = true; return; }
        if (_editor != null && _editor.IsMouseOver) return;
        CommitText(); CancelGesture(); var point = Position(e);
        if (TryBeginRedactionResize(point)) { e.Handled = true; return; }
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
        if (ResizeRedaction(point)) { e.Handled = true; return; }
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
    private void CancelGesture() { _gesture = false; _movingOriginal = null; _resizeHandle = -1; _erasing.Clear(); _drawing.HiddenIds = null; _points.Clear(); _drawing.Pending = null; if (_surface.IsMouseCaptured) _surface.ReleaseMouseCapture(); _drawing.InvalidateVisual(); }
    private void LostCapture(object sender, MouseEventArgs e) { if (_gesture) CancelGesture(); }
    private void Undo() { CommitText(); CancelGesture(); _document.Undo(); Update(); }
    private void Redo() { CommitText(); CancelGesture(); _document.Redo(); Update(); }
    private void ToggleOriginal()
    {
        CommitText(); CancelGesture(); _showingOriginal = !_showingOriginal;
        _originalLayer.Visibility = _showingOriginal ? Visibility.Visible : Visibility.Collapsed;
        _drawing.Visibility = _showingOriginal ? Visibility.Collapsed : Visibility.Visible;
        Ui.Tip(_originalButton, L.T(_showingOriginal ? "Show edited" : "Show original")); Update();
    }
    private void Update()
    {
        _undo.IsEnabled = _document.CanUndo; _redo.IsEnabled = _document.CanRedo;
        var colorContent = new StackPanel { Orientation = Orientation.Horizontal };
        colorContent.Children.Add(new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(_color), BorderBrush = Ui.Brush("Stroke"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0) });
        colorContent.Children.Add(Ui.Text(_color.A == 255 ? $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}" : _color.ToString(), 12)); _colorButton.Content = colorContent;
        foreach (var (tool, button) in _tools) button.SetResourceReference(BackgroundProperty, tool == _tool ? "Selected" : "Field");
        var hint = _tool switch { "Crop" => L.T("Drag an area to keep. The crop applies on release; Undo restores it."), "Text" => L.T("Click to add text. Ctrl+Enter to finish."), "Number" => L.T("Click to add the next numbered step."), "Eyedropper" => L.T("Click inside the crop to choose its pixel color."), "Redaction" => L.T("Drag an opaque cover over private details."), _ => L.T("Drag on the image to annotate.") };
        _status.Text = _showingOriginal ? L.T("Viewing original. Edits are still in your working copy.")
            : L.F($"{_document.Crop.Width} × {_document.Crop.Height} pixels   •   {hint}");
        _drawing.InvalidateVisual();
        UpdateRedactionReview();
    }
    private void ExtractText()
    {
        CommitText(); CancelGesture();
        try { new OcrTextWindow(_document.Export(), _report) { Owner = this }.ShowDialog(); }
        catch (Exception ex) { _report(L.T("Could not extract text: ") + ex.Message); }
    }
    private async void Export(string action)
    {
        if (_showingOriginal) ToggleOriginal();
        CommitText(); CancelGesture();
        UpdateRedactionReview();
        if (!_reviewState.CanExport || _exporting) return;
        _exporting = true; RefreshReviewControls();
        try
        {
            var result = _document.Export();
            bool success;
            if (_exportAsync != null) success = await _exportAsync(result, Enum.Parse<ScreenshotExportAction>(action));
            else if (action == "Save") success = ImageOutput.Save(this, result, _report);
            else { _onExport(result); success = true; }
            if (success && (action == "Apply" || _reviewBeforeOutput)) { Close(); return; }
        }
        catch (Exception ex) { _report(L.T("Could not export the screenshot: ") + ex.Message); }
        finally { _exporting = false; if (!_reviewClosed) RefreshReviewControls(); }
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
        CloseRedactionReview();
        CancelGesture(); _surface.MouseLeftButtonDown -= Begin; _surface.MouseMove -= Move; _surface.MouseLeftButtonUp -= Finish; _surface.LostMouseCapture -= LostCapture;
        PreviewKeyDown -= OnKey; Closed -= Cleanup; _surface.Children.Clear(); _editor = null; Content = null;
    }
    private sealed class EditCanvas(BitmapSource image, ScreenshotEditDocument document) : FrameworkElement
    {
        public Annotation? Pending { get; set; }
        public Guid? SelectedId { get; set; }
        public HashSet<Guid>? HiddenIds { get; set; }
        public IReadOnlyList<SensitiveFinding> Suggestions { get; set; } = [];
        public Guid? SelectedFinding { get; set; }
        internal double HandleRadius
        {
            get { var window = Window.GetWindow(this); double scale = window == null ? 1 : TransformToAncestor(window).TransformBounds(new Rect(0, 0, 1, 1)).Width; return 5 / Math.Max(.01, scale); }
        }
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawImage(document.RenderPreview(Pending, HiddenIds), new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            var accent = (TryFindResource("Accent") as Brush) ?? (TryFindResource("Selected") as Brush) ?? SystemColors.HighlightBrush;
            int number = 0;
            foreach (var finding in Suggestions)
            {
                dc.DrawRectangle(null, new Pen(accent, finding.Id == SelectedFinding ? HandleRadius / 2 : HandleRadius / 4) { DashStyle = DashStyles.Dash }, new Rect(finding.Bounds.X, finding.Bounds.Y, finding.Bounds.Width, finding.Bounds.Height));
                var label = new FormattedText((++number).ToString(System.Globalization.CultureInfo.CurrentCulture), System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, new Typeface((FontFamily)FindResource("BodyFont"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), HandleRadius * 2.4,
                    (Brush)FindResource("AccentText"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
                var badge = new Rect(finding.Bounds.X, Math.Max(0, finding.Bounds.Y - label.Height - HandleRadius), label.Width + HandleRadius * 2, label.Height + HandleRadius);
                dc.DrawRoundedRectangle(accent, null, badge, HandleRadius / 2, HandleRadius / 2);
                dc.DrawText(label, new Point(badge.X + HandleRadius, badge.Y + HandleRadius / 2));
            }
            var selected = Pending?.Id == SelectedId ? Pending : document.Items.FirstOrDefault(a => a.Id == SelectedId);
            if (selected != null) dc.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 1.5) { DashStyle = DashStyles.Dash }, AnnotationRenderer.Bounds(selected));
            if (selected?.Kind == AnnotationKind.Redaction && selected.Points.Count >= 2)
                foreach (var point in RedactionHandles(new Rect(selected.Points[0], selected.Points[^1]))) dc.DrawRectangle(accent, new Pen(SystemColors.WindowBrush, HandleRadius / 5), new Rect(point.X - HandleRadius, point.Y - HandleRadius, HandleRadius * 2, HandleRadius * 2));
            var crop = document.Crop;
            var bounds = new Rect(crop.X, crop.Y, crop.Width, crop.Height);
            var mask = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(RenderSize)), new RectangleGeometry(bounds));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, mask);
            if (crop.Width != image.PixelWidth || crop.Height != image.PixelHeight) dc.DrawRectangle(null, new Pen(Brushes.White, 1), bounds);
        }
    }
}
