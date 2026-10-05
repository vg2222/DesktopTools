using DesktopTools.Core;
using DesktopTools.Localization;
using DesktopTools.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace DesktopTools.Extras;

public sealed partial class TextSelectionView
{
    /// <summary>
    /// Draws the picture, a dimming layer with a hole under every recognized word, an outline per word and the selection, and turns pointer input into
    /// selection changes. All hit testing is done in image pixels; <see cref="GetMatrix"/> maps image pixels to this element's DIPs (fit, zoom, pan).
    /// </summary>
    private sealed class Surface : FrameworkElement
    {
        public static readonly DependencyProperty RevealProperty = DependencyProperty.Register(nameof(Reveal), typeof(double), typeof(Surface),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public double Reveal { get => (double)GetValue(RevealProperty); set => SetValue(RevealProperty, value); }

        private readonly BitmapSource image;
        private TextSelectionModel? model;
        private Dictionary<int, TextLink> links = [];
        private int hoverWord = -1;
        private double zoom = 1;
        private Vector pan;
        private bool dragging, panning;
        private Point panStart; private Vector panOrigin;
        private Point downPoint; private int downWord = -1;
        private TextBox? editor; private int editing = -1;

        public Surface(BitmapSource image)
        {
            this.image = image; Focusable = true; ClipToBounds = true; FocusVisualStyle = null; SnapsToDevicePixels = false;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        }

        public Canvas? Host { get; set; }               // holds the in-place editor
        public event Action? SelectionChanged;
        public event Action<string, TextCopyKind>? CopyRequested;
        public event Action? TranslateRequested;
        public event Action<string>? Notice;

        public void SetModel(TextSelectionModel? value, IReadOnlyList<TextLink> found)
        {
            CloseEditor(false);
            model = value; hoverWord = -1;
            links = []; foreach (var link in found) for (int i = link.FirstWord; i <= link.LastWord; i++) links[i] = link;
            BeginAnimation(RevealProperty, null);
            if (model == null || !Motion.Enabled) Reveal = model == null ? 0 : 1;
            else BeginAnimation(RevealProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            InvalidateVisual(); SelectionChanged?.Invoke();
        }

        public Matrix GetMatrix()
        {
            const double margin = 16;
            double fit = Math.Max(.01, Math.Min((ActualWidth - 2 * margin) / image.PixelWidth, (ActualHeight - 2 * margin) / image.PixelHeight));
            double scale = fit * zoom;
            double cx = ActualWidth / 2 + pan.X, cy = ActualHeight / 2 + pan.Y;
            return new Matrix(scale, 0, 0, scale, cx - image.PixelWidth * scale / 2, cy - image.PixelHeight * scale / 2);
        }
        public Point ImageToSurface(Point p) => GetMatrix().Transform(p);
        public Point SurfaceToImage(Point p) { var m = GetMatrix(); m.Invert(); return m.Transform(p); }

        public void ResetView() { zoom = 1; pan = new Vector(); InvalidateVisual(); }

        private static Color AccentColor() => Ui.Brush("Accent") is SolidColorBrush brush ? brush.Color : Colors.DodgerBlue;
        private static readonly Color LinkColor = Color.FromRgb(0x4D, 0xB4, 0xFF);

        private double WaveOpacity(double y)
        {
            if (Reveal >= 1) return 1;
            return Math.Clamp((Reveal * 1.5 - y / Math.Max(1, image.PixelHeight)) * 4, 0, 1);
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));   // makes the whole area hit-testable
            var matrix = GetMatrix();
            dc.PushTransform(new MatrixTransform(matrix));
            var full = new Rect(0, 0, image.PixelWidth, image.PixelHeight);
            dc.DrawImage(image, full);
            if (model != null && model.Count > 0)
            {
                const double pad = 2, radius = 3;
                var accent = AccentColor();
                var holes = new GeometryGroup { FillRule = FillRule.Nonzero };
                foreach (var word in model.Words) { var r = word.Bounds; r.Inflate(pad, pad); holes.Children.Add(new RectangleGeometry(r, radius, radius)); }
                var dim = Geometry.Combine(new RectangleGeometry(full), holes, GeometryCombineMode.Exclude, null);
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)(140 * Reveal), 0, 0, 0)), null, dim);
                double thin = 1 / Math.Max(.05, matrix.M11);
                var selection = new SolidColorBrush(Color.FromArgb(95, accent.R, accent.G, accent.B));
                var hover = new SolidColorBrush(Color.FromArgb(45, accent.R, accent.G, accent.B));
                for (int i = 0; i < model.Count; i++)
                {
                    var r = model.Words[i].Bounds; r.Inflate(pad, pad);
                    byte alpha = (byte)(210 * WaveOpacity(r.Top));
                    bool isLink = links.ContainsKey(i);
                    var edge = isLink ? LinkColor : accent;
                    var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, edge.R, edge.G, edge.B)), thin * (model.IsEdited(i) ? 2 : 1));
                    if (model.IsSelected(i)) dc.DrawRoundedRectangle(selection, new Pen(new SolidColorBrush(Color.FromArgb(255, accent.R, accent.G, accent.B)), thin), r, radius, radius);
                    else dc.DrawRoundedRectangle(i == hoverWord ? hover : null, pen, r, radius, radius);
                    if (isLink && alpha > 0)
                        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(alpha, LinkColor.R, LinkColor.G, LinkColor.B)), thin * 1.6), new Point(r.Left + 1, r.Bottom + thin * 2), new Point(r.Right - 1, r.Bottom + thin * 2));
                }
            }
            dc.Pop();
        }

        // ---- input -------------------------------------------------------------------------------------------------------------------------------

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Focus(); if (editor != null) CloseEditor(true);
            if (model == null || model.Count == 0) return;
            var point = SurfaceToImage(e.GetPosition(this)); downPoint = point;
            if (Keyboard.IsKeyDown(Key.Space)) { StartPan(e.GetPosition(this)); CaptureMouse(); e.Handled = true; return; }
            int? word = model.WordAt(point); downWord = word ?? -1;
            if (Keyboard.Modifiers == ModifierKeys.Control && word is int linked && links.TryGetValue(linked, out var link)) { OpenLink(link); e.Handled = true; return; }
            var target = word ?? model.NearestWord(point);
            if (e.ClickCount >= 3 && word is int line) model.SelectLine(line);
            else if (e.ClickCount == 2 && word is int one) model.SelectWord(one);
            else if (word is int exact) { model.Begin(exact); dragging = true; CaptureMouse(); }
            else { model.Clear(); if (target is int near) { model.Begin(near); dragging = true; CaptureMouse(); } }
            InvalidateVisual(); SelectionChanged?.Invoke(); e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (model == null) return;
            var position = e.GetPosition(this);
            if (panning) { pan = panOrigin + (position - panStart); InvalidateVisual(); return; }
            var point = SurfaceToImage(position);
            if (dragging)
            {
                if ((point - downPoint).Length > 2 / Math.Max(.05, GetMatrix().M11) && model.NearestWord(point) is int target) { model.Extend(target); InvalidateVisual(); SelectionChanged?.Invoke(); }
                return;
            }
            int? hit = model.WordAt(point); int next = hit ?? -1;
            if (next != hoverWord) { hoverWord = next; InvalidateVisual(); }
            Cursor = next < 0 ? Cursors.Arrow : Keyboard.Modifiers == ModifierKeys.Control && links.ContainsKey(next) ? Cursors.Hand : Cursors.IBeam;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (panning) { panning = false; ReleaseMouseCapture(); return; }
            if (!dragging) return;
            dragging = false; ReleaseMouseCapture();
            // A plain click on empty space clears the selection; a click on a word leaves that word selected.
            if (model != null && downWord < 0 && (SurfaceToImage(e.GetPosition(this)) - downPoint).Length <= 2) { model.Clear(); InvalidateVisual(); SelectionChanged?.Invoke(); }
        }

        protected override void OnMouseLeave(MouseEventArgs e) { if (hoverWord >= 0) { hoverWord = -1; InvalidateVisual(); } }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle) { StartPan(e.GetPosition(this)); CaptureMouse(); e.Handled = true; }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && panning) { panning = false; ReleaseMouseCapture(); e.Handled = true; }
            base.OnMouseUp(e);
        }
        private void StartPan(Point from) { panning = true; panStart = from; panOrigin = pan; }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                var anchor = e.GetPosition(this); var imagePoint = SurfaceToImage(anchor);
                zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15), .25, 8);
                var moved = ImageToSurface(imagePoint); pan += anchor - moved;
                InvalidateVisual(); e.Handled = true; return;
            }
            if (zoom > 1.001) { pan += new Vector(0, e.Delta / 2.0); InvalidateVisual(); e.Handled = true; }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (model == null || editor != null) return;
            bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
            if (ctrl && e.Key == Key.A) { model.SelectAll(); InvalidateVisual(); SelectionChanged?.Invoke(); e.Handled = true; }
            else if (ctrl && e.Key == Key.C) { CopyRequested?.Invoke(model.HasSelection ? model.GetText(true) : model.GetText(false), model.HasSelection ? TextCopyKind.Selection : TextCopyKind.All); e.Handled = true; }
            else if (e.Key == Key.Escape && model.HasSelection) { model.Clear(); InvalidateVisual(); SelectionChanged?.Invoke(); e.Handled = true; }
            else if (e.Key == Key.F2) { BeginFix(model.HasSelection ? model.Range.Start : hoverWord); e.Handled = true; }
            else if (ctrl && e.Key == Key.D0) { ResetView(); e.Handled = true; }
        }

        protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
        {
            if (model == null || model.Count == 0) return;
            var point = SurfaceToImage(e.GetPosition(this)); int? word = model.WordAt(point);
            if (word is int w && !model.IsSelected(w)) { model.SelectWord(w); InvalidateVisual(); SelectionChanged?.Invoke(); }
            var menu = new ContextMenu();
            void Item(string header, string icon, Action action, bool enabled = true)
            {
                var item = new MenuItem { Header = L.T(header), Icon = Ui.Icon(icon, 16), IsEnabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item);
            }
            Item("Copy selection", "Copy", () => CopyRequested?.Invoke(model.GetText(true), TextCopyKind.Selection), model.HasSelection);
            Item("Copy all", "Copy", () => CopyRequested?.Invoke(model.GetText(false), TextCopyKind.All));
            if (word is int hit)
            {
                if (links.TryGetValue(hit, out var link)) { menu.Items.Add(new Separator()); Item("Open link", "Search", () => OpenLink(link)); Item("Copy link", "Copy", () => CopyRequested?.Invoke(link.Target.StartsWith("mailto:") || link.Target.StartsWith("tel:") ? link.Target[(link.Target.IndexOf(':') + 1)..] : link.Target, TextCopyKind.Link)); }
                menu.Items.Add(new Separator()); Item("Fix text", "Pen", () => BeginFix(hit));
            }
            if (TranslateRequested != null) { menu.Items.Add(new Separator()); Item("Translate", "Translate", () => TranslateRequested?.Invoke()); }
            menu.PlacementTarget = this; menu.IsOpen = true; e.Handled = true;
        }

        private void OpenLink(TextLink link)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link.Target) { UseShellExecute = true }); }
            catch (Exception ex) { Notice?.Invoke(L.T("Could not open the link: ") + ex.Message); }
        }

        // ---- fixing a word --------------------------------------------------------------------------------------------------------------------

        public void BeginFix(int word)
        {
            if (model == null || word < 0 || word >= model.Count || Host == null) return;
            CloseEditor(true);
            var bounds = model.Words[word].Bounds; var matrix = GetMatrix();
            var topLeft = matrix.Transform(bounds.TopLeft);
            double height = Math.Clamp(bounds.Height * matrix.M11, 22, 56);
            editing = word;
            editor = new TextBox { Text = model.TextOf(word), MinWidth = Math.Max(90, bounds.Width * matrix.M11 + 24), Height = height, FontSize = Math.Clamp(bounds.Height * matrix.M11 * .8, 12, 30),
                Padding = new Thickness(4, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(editor, L.T("Enter the corrected text"));
            Canvas.SetLeft(editor, topLeft.X - 4); Canvas.SetTop(editor, topLeft.Y - (height - bounds.Height * matrix.M11) / 2);
            editor.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CloseEditor(true); e.Handled = true; } else if (e.Key == Key.Escape) { CloseEditor(false); e.Handled = true; } };
            editor.LostKeyboardFocus += (_, _) => CloseEditor(true);
            Host.Children.Add(editor); editor.Focus(); editor.SelectAll();
        }

        public void CloseEditor(bool commit)
        {
            var box = editor; if (box == null) return;
            editor = null; int word = editing; editing = -1;
            Host?.Children.Remove(box);
            if (commit && model != null && word >= 0 && word < model.Count) { model.SetOverride(word, box.Text); SelectionChanged?.Invoke(); }
            InvalidateVisual(); Focus();
        }

        public RenderTargetBitmap Render()
        {
            var target = new RenderTargetBitmap(Math.Max(1, (int)ActualWidth), Math.Max(1, (int)ActualHeight), 96, 96, PixelFormats.Pbgra32);
            target.Render(this); return target;
        }
    }
}
