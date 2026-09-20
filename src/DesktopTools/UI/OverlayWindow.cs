using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopTools.Core;
using DesktopTools.Native;

namespace DesktopTools.UI;

internal sealed class DrawingSurface(AnnotationDocument document) : FrameworkElement
{
    public Annotation? Preview { get; set; }
    public Guid? SelectedId { get; set; }
    public bool Replacing { get; set; }
    public Rect SelectedBounds => (Preview != null && Replacing ? Preview : document.Items.FirstOrDefault(a => a.Id == SelectedId)) is { } item ? AnnotationRenderer.Bounds(item) : Rect.Empty;
    public static Point[] Handles(Rect bounds) => bounds.IsEmpty ? [] : [bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft];
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        AnnotationRenderer.Draw(dc, document.Items.Select(a => Replacing && Preview?.Id == a.Id ? Preview : a));
        if (Preview != null && !Replacing) AnnotationRenderer.Draw(dc, [Preview]);
        var bounds = SelectedBounds;
        if (!bounds.IsEmpty)
        {
            var pen = new Pen(Brushes.DodgerBlue, 1) { DashStyle = DashStyles.Dash };
            dc.DrawRectangle(null, pen, bounds);
            foreach (var p in Handles(bounds)) dc.DrawRectangle(Brushes.White, new Pen(Brushes.DodgerBlue, 1), new Rect(p - new Vector(4, 4), new Size(8, 8)));
        }
    }
}

internal sealed class OverlayWindow : Window
{
    private readonly AppController controller;
    private readonly DrawingSurface surface;
    private readonly Canvas textLayer = new();
    private AnnotationTextEditor? editor;
    private Point textOrigin;
    private readonly List<Point> points = [];
    private readonly Image frozenImage = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private bool gesture;
    private Annotation? editOriginal;
    private Point editStart;
    private Rect editBounds;
    private int resizeHandle = -1;
    private bool editMoved;
    public MonitorInfo Monitor { get; }
    public bool HasPending => gesture || editor != null;
    public OverlayWindow(AppController controller, MonitorInfo monitor)
    {
        this.controller = controller; Monitor = monitor;
        // A zero-alpha layered window is transparent to native hit testing, regardless of WPF hit testing.
        // One alpha unit keeps the live desktop visually unchanged while Draw owns pointer input.
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)); ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize;
        Left = monitor.Bounds.Left / monitor.ScaleX; Top = monitor.Bounds.Top / monitor.ScaleY; Width = monitor.Bounds.Width / monitor.ScaleX; Height = monitor.Bounds.Height / monitor.ScaleY;
        surface = new DrawingSurface(controller.Document) { Focusable = true };
        var grid = new Grid(); grid.Children.Add(frozenImage); grid.Children.Add(surface); grid.Children.Add(textLayer); Content = grid;
        SourceInitialized += (_, _) => { NativeWindowService.ConfigureOverlay(this, false); MonitorService.PlaceWindow(this, monitor); };
        controller.Document.Changed += DocumentChanged; Closed += (_, _) => controller.Document.Changed -= DocumentChanged;
        surface.MouseLeftButtonDown += Begin; surface.MouseMove += Move; surface.MouseLeftButtonUp += End;
        PreviewKeyDown += KeyPressed;
        Deactivated += (_, _) => controller.CheckDrawingFocus();
        surface.LostMouseCapture += (_, _) => { if (gesture) CancelPending(); };
    }
    private void DocumentChanged()
    {
        if (surface.SelectedId is { } id && !controller.Document.Items.Any(a => a.Id == id)) surface.SelectedId = null;
        surface.InvalidateVisual();
    }
    public void SetFrozen(BitmapSource? bitmap) { frozenImage.Source = bitmap; }
    internal void ShowControlDialog(Window dialog) => controller.ShowControlDialog(dialog);
    public void Mode(bool drawing)
    {
        if (!drawing) { CommitText(); CancelPending(); surface.SelectedId = null; surface.InvalidateVisual(); }
        NativeWindowService.SetClickThrough(this, !drawing); surface.IsHitTestVisible = drawing; textLayer.IsHitTestVisible = drawing;
        if (drawing) { Activate(); surface.Focus(); }
    }
    private Annotation MakeAnnotation() => new()
    {
        Kind = controller.Tool == "Eraser" ? AnnotationKind.Pen : Enum.Parse<AnnotationKind>(controller.Tool), Points = points.ToArray(),
        Color = (Color)ColorConverter.ConvertFromString(controller.Settings.Color), Thickness = controller.Settings.Thickness * (controller.Tool == "Highlighter" ? 4 : 1),
        Opacity = controller.Tool == "Highlighter" ? Math.Min(.35, controller.Settings.Opacity) : controller.Settings.Opacity, FontSize = controller.Settings.FontSize, Filled = controller.Settings.FillShapes, ArrowHeadSize = controller.Settings.ArrowHeadSize
    };
    private void Begin(object sender, MouseButtonEventArgs e)
    {
        if (controller.State != OverlayState.Draw) return;
        CommitText(); var point = e.GetPosition(surface);
        if (controller.Tool == "Select")
        {
            var selected = controller.Document.Items.FirstOrDefault(a => a.Id == surface.SelectedId);
            var handles = DrawingSurface.Handles(surface.SelectedBounds);
            resizeHandle = Array.FindIndex(handles, p => (p - point).Length <= 9);
            var hit = resizeHandle >= 0 ? selected : controller.Document.Items.Reverse().FirstOrDefault(a => AnnotationRenderer.Hit(a, point));
            surface.SelectedId = hit?.Id;
            if (hit != null)
            {
                editOriginal = hit; editStart = point; editBounds = AnnotationRenderer.Bounds(hit); editMoved = false;
                gesture = true; surface.Replacing = true; surface.Preview = hit; surface.CaptureMouse();
            }
            surface.InvalidateVisual(); e.Handled = true; return;
        }
        surface.SelectedId = null;
        if (controller.Tool == "Text") { OpenText(point); e.Handled = true; return; }
        if (controller.Tool == "Number")
        {
            points.Clear(); points.Add(point);
            int next = controller.Document.Items.Where(a => a.Kind == AnnotationKind.Number).Select(a => a.Number).DefaultIfEmpty(0).Max() + 1;
            controller.Document.Add(MakeAnnotation() with { Number = next }); e.Handled = true; return;
        }
        if (controller.Tool == "Eraser") { var hit = controller.Document.Items.Reverse().FirstOrDefault(a => AnnotationRenderer.Hit(a, point)); if (hit != null) controller.Document.Remove(hit.Id); return; }
        points.Clear(); points.Add(point); gesture = true; surface.CaptureMouse(); surface.Preview = MakeAnnotation(); surface.InvalidateVisual(); e.Handled = true;
    }
    private void Move(object sender, MouseEventArgs e)
    {
        if (!gesture) return;
        var point = e.GetPosition(surface); point = new Point(Math.Clamp(point.X, 0, ActualWidth), Math.Clamp(point.Y, 0, ActualHeight));
        if (editOriginal != null)
        {
            editMoved = (point - editStart).Length > .5;
            if (resizeHandle < 0) surface.Preview = AnnotationTransform.Move(editOriginal, point - editStart);
            else
            {
                var anchor = DrawingSurface.Handles(editBounds)[(resizeHandle + 2) % 4];
                // Keep the corner on its original side of the fixed opposite corner.
                point.X = resizeHandle is 0 or 3 ? Math.Min(point.X, anchor.X - 4) : Math.Max(point.X, anchor.X + 4);
                point.Y = resizeHandle is 0 or 1 ? Math.Min(point.Y, anchor.Y - 4) : Math.Max(point.Y, anchor.Y + 4);
                surface.Preview = AnnotationTransform.Resize(editOriginal, editBounds, new Rect(anchor, point));
            }
            surface.InvalidateVisual(); return;
        }
        if (controller.Settings.ShapeSnapping && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && controller.Tool is "Line" or "Arrow" or "Rectangle" or "Ellipse")
            point = DrawingGeometry.Constrain(points[0], point, Enum.Parse<AnnotationKind>(controller.Tool));
        if (controller.Tool is "Pen" or "Highlighter") { if ((point - points[^1]).Length < .7) return; points.Add(point); }
        else { if (points.Count == 1) points.Add(point); else points[1] = point; }
        surface.Preview = MakeAnnotation(); surface.InvalidateVisual();
    }
    private void End(object sender, MouseButtonEventArgs e)
    {
        if (!gesture) return; Move(sender, e);
        if (editOriginal != null)
        {
            var edited = surface.Preview; var changed = editMoved;
            CancelPending();
            if (changed && edited != null) controller.Document.Replace(edited);
            e.Handled = true; return;
        }
        var annotation = MakeAnnotation(); gesture = false; surface.ReleaseMouseCapture(); surface.Preview = null; controller.Document.Add(annotation); e.Handled = true;
    }
    private void OpenText(Point point)
    {
        editor = new AnnotationTextEditor(point, new Size(ActualWidth, ActualHeight), (Color)ColorConverter.ConvertFromString(controller.Settings.Color), controller.Settings.FontSize); textOrigin = editor.ExportOrigin;
        editor.CommitRequested += CommitText; editor.CancelRequested += () => CancelPending();
        textLayer.Children.Add(editor); editor.Focus();
    }
    public void CommitText()
    {
        if (editor == null) return;
        if (!string.IsNullOrWhiteSpace(editor.Text)) controller.Document.Add(new Annotation { Kind = AnnotationKind.Text, Points = new[] { textOrigin }, Text = editor.Text, TextWidth = editor.ExportWidth, FontFamily = editor.FontFamily.Source, FontSize = editor.FontSize, Bold = editor.FontWeight == FontWeights.Bold, Italic = editor.FontStyle == FontStyles.Italic, Color = ((SolidColorBrush)editor.Foreground).Color, Opacity = controller.Settings.Opacity });
        textLayer.Children.Clear(); editor = null;
    }
    public bool CancelPending()
    {
        if (editor != null) { textLayer.Children.Clear(); editor = null; return true; }
        if (!gesture) return false; gesture = false; editOriginal = null; surface.Replacing = false; points.Clear(); surface.Preview = null; surface.ReleaseMouseCapture(); surface.InvalidateVisual(); return true;
    }
    public void DuplicateSelected()
    {
        CancelPending();
        var selected = controller.Document.Items.FirstOrDefault(a => a.Id == surface.SelectedId);
        if (selected == null) return;
        var duplicate = AnnotationTransform.Duplicate(selected);
        controller.Document.Add(duplicate); surface.SelectedId = duplicate.Id; surface.InvalidateVisual();
    }
    public bool DeleteSelected()
    {
        CancelPending();
        if (surface.SelectedId is not { } id) return false;
        controller.Document.Remove(id); return true;
    }
    internal void KeyPressed(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (key == Key.Escape && modifiers == ModifierKeys.None) { if (!CancelPending()) controller.HideAnnotations(); e.Handled = true; return; }
        var action = DrawingBindings.Find(controller.Settings.DrawingShortcuts, key, modifiers);
        if (editor != null) { if (action == "FinishText") { CommitText(); e.Handled = true; } return; }
        if (action == null) return;
        switch (action)
        {
            case "Undo": CancelPending(); controller.Document.Undo(); break;
            case "Redo": CancelPending(); controller.Document.Redo(); break;
            case "Duplicate": DuplicateSelected(); break;
            case "Delete": if (!DeleteSelected()) controller.Document.Clear(); break;
            case "Interact": controller.Interact(); break;
            case "FinishText": return;
            default: controller.SetTool(action); break;
        }
        e.Handled = true;
    }
}
