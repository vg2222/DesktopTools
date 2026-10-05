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

public sealed partial class ScreenshotEditorView : UserControl
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
    private Action<bool>? _showPrivacyTab;
    private Color _color = Colors.Red;
    private double _thickness = 4, _opacity = 1;
    private Annotation? _movingOriginal;
    private readonly HashSet<Guid> _erasing = [];
    private bool _gesture;
    private AnnotationTextEditor? _editor;
    private Point _textOrigin;
    private readonly bool _hosted;
    private Grid _work = null!; private DockPanel _footer = null!; private GridSplitter _divider = null!;
    private TextSelectionView? _textView;
    /// <summary>Raised when the embedded text view asks to translate text; leave unsubscribed to hide the Translate button.</summary>
    public event Action<string>? TranslateRequested;
    internal FrameworkElement ToolCard { get; private set; } = null!;
    internal FrameworkElement PropertyCard { get; private set; } = null!;
    internal FrameworkElement Backdrop { get; private set; } = null!;
    internal FrameworkElement Actions { get; private set; } = null!;
    public event Action? CloseRequested;
    /// <summary>The picture with every annotation and cover applied; the source image is untouched.</summary>
    public BitmapSource RenderResult() => _document.Export();
    public bool HasEdits => _document.Items.Count > 0 || _document.Crop.Width != _image.PixelWidth || _document.Crop.Height != _image.PixelHeight;
    internal ScreenshotEditDocument Document => _document;

    public ScreenshotEditorView(BitmapSource image, Action<BitmapSource> onExport, Action<string> report, bool applyToImage = false, string? editorLayout = null,
        string? applyLabel = null, bool offerOriginal = false, AutoRedactOptions? autoRedact = null, bool reviewBeforeOutput = false,
        Func<BitmapSource, ScreenshotExportAction, Task<bool>>? exportAsync = null, bool hosted = false)
    {
        editorLayout ??= "A"; // tools above the image, actions below: the order people read an editor in
        _image = image; _document = new(image); _onExport = onExport; _report = report;
        SetResourceReference(ForegroundProperty, "Text"); _hosted = hosted;
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var work = new Grid(); work.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 250 }); work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) }); work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300), MinWidth = 240 }); Grid.SetRow(work, 1); layout.Children.Add(work); _work = work;
        var divider = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext, Background = Brushes.Transparent };
        Grid.SetColumn(divider, 1); work.Children.Add(divider); _divider = divider;
        _surface = new Canvas { Width = image.PixelWidth, Height = image.PixelHeight, Background = Brushes.Transparent, ClipToBounds = true, Cursor = Cursors.Cross };
        _originalLayer = new Image { Source = image, Width = image.PixelWidth, Height = image.PixelHeight, Stretch = Stretch.Fill, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        _surface.Children.Add(_originalLayer);
        _drawing = new EditCanvas(image, _document) { Width = image.PixelWidth, Height = image.PixelHeight, IsHitTestVisible = false }; _surface.Children.Add(_drawing);
        var backdrop = new Border { Child = new Viewbox { Child = BuildStyleFrame(_surface, image.PixelWidth, image.PixelHeight), Stretch = Stretch.Uniform }, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 12, 0) };
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
        _colorButton = Ui.Button("", () => { CommitText(); new ColorPickerWindow(_color.ToString(), value => { _color = (Color)ColorConverter.ConvertFromString(value); Update(); return true; }) { Owner = Window.GetWindow(this) }.ShowDialog(); }); properties.Children.Add(_colorButton);
        System.Windows.Automation.AutomationProperties.SetName(_colorButton, L.T("Annotation color"));
        var widthChoice = new Slider { Minimum = 1, Maximum = 32, Value = _thickness, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 4 };
        widthChoice.ValueChanged += (_, _) => _thickness = widthChoice.Value;
        var strokeField = Ui.SliderField(L.T("Stroke thickness"), widthChoice, v => $"{v:0} px"); strokeField.Margin = new Thickness(0, 18, 0, 0); properties.Children.Add(strokeField);
        var opacity = new Slider { Minimum = .1, Maximum = 1, Value = 1 }; opacity.ValueChanged += (_, _) => _opacity = opacity.Value;
        var opacityField = Ui.SliderField(L.T("Opacity"), opacity, v => $"{v * 100:0}%"); opacityField.Margin = new Thickness(0, 14, 0, 0); properties.Children.Add(opacityField);
        System.Windows.Automation.AutomationProperties.SetName(opacity, L.T("Annotation opacity"));
        _status = Ui.Text("", 11, muted: true); _status.Margin = new Thickness(0, 18, 0, 0); properties.Children.Add(_status);
        var propertyCard = Ui.Card(new ScrollViewer { Content = properties, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 14); propertyCard.Margin = new Thickness(0); Grid.SetColumn(propertyCard, 2); work.Children.Add(propertyCard); PropertyCard = propertyCard; Backdrop = backdrop;
        var tools = new WrapPanel();
        void SelectTool(string tool) { if (_showingOriginal) ToggleOriginal(); CommitText(); CancelGesture(); _tool = tool; _showPrivacyTab?.Invoke(tool == "Redaction"); Update(); }
        foreach (var tool in new[] { "Select", "Pen", "Highlighter", "Arrow", "Rectangle", "Ellipse", "Text", "Redaction", "Eraser", "Crop" })
        {
            var button = Ui.IconButton(tool, L.T(tool == "Redaction" ? "Cover" : tool), () => SelectTool(tool)); button.Width = button.Height = button.MinHeight = 32; button.Padding = new Thickness(7); button.Margin = new Thickness(1); _tools.Add(tool, button); tools.Children.Add(button);
        }
        var more = Ui.IconButton("More", L.T("More tools"), () => { }); more.Width = more.Height = more.MinHeight = 32;
        var menu = new ContextMenu();
        foreach (var tool in new[] { "Line", "Number", "Eyedropper" }) { var item = new MenuItem { Header = L.T(tool == "Redaction" ? "Cover" : tool), Icon = Ui.Icon(tool, 16) }; item.Click += (_, _) => SelectTool(tool); menu.Items.Add(item); }
        menu.Items.Add(new Separator());
        var clear = new MenuItem { Header = L.T("Clear"), Icon = Ui.Icon("Clear", 16) }; clear.Click += (_, _) => { CommitText(); CancelGesture(); _document.Clear(); Update(); }; menu.Items.Add(clear);
        var resetCrop = new MenuItem { Header = L.T("Reset crop"), Icon = Ui.Icon("Crop", 16) }; resetCrop.Click += (_, _) => { CommitText(); CancelGesture(); _document.SetCrop(new(0, 0, image.PixelWidth, image.PixelHeight)); Update(); }; menu.Items.Add(resetCrop);
        var ocr = new MenuItem { Header = L.T("Extract text"), Icon = Ui.Icon("ScanText", 16) }; ocr.Click += (_, _) => ExtractText(); menu.Items.Add(ocr);
        more.ContextMenu = menu; more.Click += (_, _) => { menu.PlacementTarget = more; menu.IsOpen = true; }; tools.Children.Add(more); Unloaded += (_, _) => menu.IsOpen = false;
        tools.Children.Add(ToolbarDivider());
        _undo = Ui.IconButton("Undo", L.T("Undo"), Undo); _redo = Ui.IconButton("Redo", L.T("Redo"), Redo); foreach (var b in new[] { _undo, _redo }) { b.Width = b.Height = b.MinHeight = 32; b.Padding = new Thickness(7); b.Margin = new Thickness(1); } tools.Children.Add(_undo); tools.Children.Add(_redo);
        tools.Children.Add(ToolbarDivider());
        _originalButton = Ui.IconButton("Image", L.T("Show original"), ToggleOriginal); _originalButton.Width = _originalButton.Height = _originalButton.MinHeight = 32; _originalButton.Padding = new Thickness(7); _originalButton.Margin = new Thickness(1); tools.Children.Add(_originalButton);
        var toolCard = Ui.Card(tools, 6); toolCard.Margin = new Thickness(0, 0, 12, 0); toolCard.VerticalAlignment = VerticalAlignment.Center;
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
        if (applyToImage)
        {
            if (offerOriginal)
            {
                var original = Ui.Button(L.T("Add original"), () => { _onExport(_image); CloseRequested?.Invoke(); });
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
        ToolCard = toolCard; Actions = actions;
        if (editorLayout == "A") { Grid.SetRow(toolCard, 0); toolCard.Margin = new Thickness(0, 0, 0, 12); layout.Children.Add(toolCard); }
        else footer.Children.Add(toolCard);
        Grid.SetRow(footer, 2); layout.Children.Add(footer); _footer = footer;
        // Hosted in Image tools the host owns saving and the single Original/Edited button, so the footer and this editor's own copy stay hidden.
        if (_hosted) { footer.Visibility = Visibility.Collapsed; _originalButton.Visibility = Visibility.Collapsed; }
        Content = layout;
        InitializeRedaction(properties, tools, actions, autoRedact, reviewBeforeOutput, exportAsync);
        Loaded += (_, _) => Motion.Reveal(layout);
        _surface.MouseLeftButtonDown += Begin; _surface.MouseMove += Move; _surface.MouseLeftButtonUp += Finish;
        _surface.LostMouseCapture += LostCapture; PreviewKeyDown += OnKey;
        Update();
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
        Ui.Tip(_originalButton, L.T(_showingOriginal ? "Show edited" : "Show original"));
        // The icon shows what the next click does: the picture while editing, the pen while looking at the original.
        _originalButton.Content = Ui.Icon(_showingOriginal ? "Draw" : "Image");
        if (_showingOriginal) _originalButton.SetResourceReference(BackgroundProperty, "Selected"); else _originalButton.Background = Brushes.Transparent;
        System.Windows.Automation.AutomationProperties.SetName(_originalButton, L.T(_showingOriginal ? "Show edited" : "Show original"));
        Update();
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
    private void ExtractText() => ShowTextMode();
    internal bool InTextMode => _textView != null;

    /// <summary>Swaps the canvas and panels for the text view of what the picture currently shows (annotations included; the source stays untouched).</summary>
    internal void ShowTextMode()
    {
        if (_textView != null || _reviewClosed) return;
        CommitText(); CancelGesture(); if (_showingOriginal) ToggleOriginal();
        try
        {
            _textView = new TextSelectionView(_document.Export(), _report, TranslateRequested == null ? null : text => TranslateRequested?.Invoke(text), ShowEditingMode);
            Grid.SetColumnSpan(_textView, 3); _work.Children.Add(_textView);
            SetEditingVisible(false);
            _ = _textView.RecognizeAsync();
        }
        catch (Exception ex) { _report(L.T("Could not extract text: ") + ex.Message); ShowEditingMode(); }
    }

    internal void ShowEditingMode()
    {
        var view = _textView; if (view == null) return;
        _textView = null; view.Cancel(); _work.Children.Remove(view);
        SetEditingVisible(true); Update();
    }

    private void SetEditingVisible(bool visible)
    {
        var state = visible ? Visibility.Visible : Visibility.Collapsed;
        Backdrop.Visibility = PropertyCard.Visibility = ToolCard.Visibility = _divider.Visibility = state;
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
            if (action != "Apply") result = ScreenshotBeautifier.Apply(result, _beautify); // frame, background and shadow only for finished copies
            bool success;
            if (_exportAsync != null) success = await _exportAsync(result, Enum.Parse<ScreenshotExportAction>(action));
            else if (action == "Save") success = ImageOutput.Save(Window.GetWindow(this)!, result, _report);
            else { _onExport(result); success = true; }
            if (success && (action == "Apply" || _reviewBeforeOutput)) { CloseRequested?.Invoke(); return; }
        }
        catch (Exception ex) { _report(L.T("Could not export the screenshot: ") + ex.Message); }
        finally { _exporting = false; if (!_reviewClosed) RefreshReviewControls(); }
    }
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_textView != null)
        {
            // The text view owns the keyboard: Esc clears its selection first, then leaves it; undo/redo belong to the hidden editor.
            if (e.Key == Key.Escape && _textView.Model?.HasSelection != true) { ShowEditingMode(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Z or Key.Y) e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            if (_editor != null) { _surface.Children.Remove(_editor); _editor = null; }
            else if (_gesture) CancelGesture(); else CloseRequested?.Invoke(); e.Handled = true;
        }
        else if (_editor != null) { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { CommitText(); e.Handled = true; } }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Z or Key.Y) { if (e.Key == Key.Z) Undo(); else Redo(); e.Handled = true; }
    }
    /// <summary>Releases timers, tokens and handlers; the hosting window calls it when it closes.</summary>
    internal void Dispose()
    {
        _textView?.Cancel(); _textView = null;
        CloseRedactionReview();
        CancelGesture(); _surface.MouseLeftButtonDown -= Begin; _surface.MouseMove -= Move; _surface.MouseLeftButtonUp -= Finish; _surface.LostMouseCapture -= LostCapture;
        PreviewKeyDown -= OnKey; _surface.Children.Clear(); _editor = null;
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
        // While a stroke is dragged only the stroke changes. The picture underneath is rendered once per gesture and cached, and the
        // pending annotation is drawn over it as vector graphics; re-compositing the whole bitmap on every mouse move made drawing lag.
        private BitmapSource? _backdrop;
        private long _backdropRevision = -1;
        private Guid? _backdropExcluded;
        private int _backdropHidden = -1;
        protected override void OnRender(DrawingContext dc)
        {
            var full = new Rect(0, 0, image.PixelWidth, image.PixelHeight);
            var pending = Pending;
            bool vector = pending != null && (pending.Kind != AnnotationKind.Redaction || pending.RedactionStyle == RedactionStyle.Solid);
            if (vector)
            {
                Guid? excluded = document.Items.Any(a => a.Id == pending!.Id) ? pending!.Id : null;
                int hidden = HiddenIds?.Count ?? 0;
                if (_backdrop == null || _backdropRevision != document.Revision || _backdropExcluded != excluded || _backdropHidden != hidden)
                { _backdrop = document.RenderWithout(excluded, HiddenIds); _backdropRevision = document.Revision; _backdropExcluded = excluded; _backdropHidden = hidden; }
                dc.DrawImage(_backdrop, full);
                if (pending!.Kind == AnnotationKind.Redaction && pending.Points.Count >= 2) dc.DrawRectangle(Brushes.Black, null, new Rect(pending.Points[0], pending.Points[^1]));
                else AnnotationRenderer.Draw(dc, [pending]);
            }
            else dc.DrawImage(document.RenderPreview(pending, HiddenIds), full);
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
