using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DesktopTools.Core;
using DesktopTools.Localization;
using DesktopTools.Native;

namespace DesktopTools.UI;

/// <summary>Anchored hints over real controls. A tour never invokes the target action.</summary>
internal sealed class GuidedTour : IDisposable
{
    internal sealed record Step(Func<FrameworkElement?> Target, string Title, string Description);
    private readonly Window owner;
    private readonly IReadOnlyList<Step> steps;
    private readonly Func<SetupStatus, int, bool> save;
    private readonly Popup popup = new() { AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Custom };
    private FrameworkElement? target;
    private AdornerLayer? layer;
    private Highlight? highlight;
    private int index;
    private bool disposed;
    internal bool IsOpen => popup.IsOpen;

    internal GuidedTour(Window owner, IReadOnlyList<Step> steps, int firstStep, Func<SetupStatus, int, bool> save)
    {
        if (steps.Count == 0) throw new ArgumentException("A tour requires at least one target.", nameof(steps));
        this.owner = owner; this.steps = steps; this.save = save;
        owner.Closed += OwnerClosed; owner.LocationChanged += Reposition; owner.SizeChanged += Reposition;
        owner.DpiChanged += Reposition;
        popup.CustomPopupPlacementCallback = PlaceHint;
        owner.PreviewKeyDown += OnKey; owner.IsVisibleChanged += OwnerVisibilityChanged;
        popup.Opened += (_, _) => { if (popup.Child is Visual visual) Ui.ExcludePopup(visual, owner); };
        if (!Show(Math.Clamp(firstStep, 0, steps.Count - 1))) Dispose();
    }
    internal bool Show(int step)
    {
        if (disposed || step < 0 || step >= steps.Count) return false;
        var next = steps[step].Target();
        if (next == null || !next.IsVisible || Window.GetWindow(next) != owner) return false;
        var nextLayer = AdornerLayer.GetAdornerLayer(next);
        if (nextLayer == null || !save(SetupStatus.InProgress, step)) return false;
        ClearTarget(); index = step; target = next; layer = nextLayer;
        target.BringIntoView(); target.LayoutUpdated += Reposition; target.Unloaded += TargetUnloaded;
        highlight = new Highlight(target); layer.Add(highlight);
        var content = new StackPanel { Width = 270 };
        content.Children.Add(Ui.Text($"{index + 1} / {steps.Count}", 12, muted: true));
        content.Children.Add(Ui.Text(L.T(steps[index].Title), 16, true));
        var description = Ui.Text(L.T(steps[index].Description), 13); description.Margin = new Thickness(0, 8, 0, 12); content.Children.Add(description);
        var buttons = new WrapPanel();
        var back = Ui.Button(L.T("Back"), () => Show(index - 1)); back.IsEnabled = index > 0;
        buttons.Children.Add(back); buttons.Children.Add(Ui.Button(L.T("Skip"), Skip));
        buttons.Children.Add(Ui.Button(L.T(index == steps.Count - 1 ? "Done" : "Next"), Next, true)); content.Children.Add(buttons);
        var card = Ui.Card(content, 16); card.Margin = new Thickness(0); card.PreviewKeyDown += OnKey;
        popup.Child = card; popup.PlacementTarget = target; popup.IsOpen = true;
        Motion.Transition(card); return true;
    }
    internal void Next()
    {
        if (disposed) return;
        if (index < steps.Count - 1) { Show(index + 1); return; }
        if (save(SetupStatus.Completed, index)) Dispose();
    }
    internal void Skip() { if (!disposed && save(SetupStatus.Skipped, index)) Dispose(); }
    private CustomPopupPlacement[] PlaceHint(Size popupSize, Size targetSize, Point offset)
    {
        if (target == null || PresentationSource.FromVisual(target) == null) return [];
        var center = target.PointToScreen(new Point(targetSize.Width / 2, targetSize.Height / 2));
        var monitors = MonitorService.GetAll();
        var monitor = monitors.FirstOrDefault(m => m.Bounds.Contains(center)) ?? monitors.OrderBy(m => Distance(m.Bounds, center)).First();
        // Popup placement is in the target's DIP coordinates; monitor bounds are physical pixels.
        var work = new Rect(target.PointFromScreen(monitor.WorkingArea.TopLeft), target.PointFromScreen(monitor.WorkingArea.BottomRight));
        return HintPlacements(popupSize, targetSize, work);
    }
    private static double Distance(Rect bounds, Point point) =>
        (point - new Point(Math.Clamp(point.X, bounds.Left, bounds.Right), Math.Clamp(point.Y, bounds.Top, bounds.Bottom))).LengthSquared;

    internal static CustomPopupPlacement[] HintPlacements(Size hint, Size control, Rect work)
    {
        const double gap = 12;
        double x = Math.Clamp(0, work.Left, Math.Max(work.Left, work.Right - hint.Width));
        double y = Math.Clamp(0, work.Top, Math.Max(work.Top, work.Bottom - hint.Height));
        Point[] candidates = [new(control.Width + gap, y), new(-hint.Width - gap, y), new(x, control.Height + gap), new(x, -hint.Height - gap)];
        // Shift only along the selected side. Clamping both axes can cover the highlighted control.
        var fitting = candidates.Where(point => work.Contains(new Rect(point, hint))).ToArray();
        return (fitting.Length > 0 ? fitting : candidates)
            .Select(point => new CustomPopupPlacement(point, PopupPrimaryAxis.None)).ToArray();
    }
    private void Reposition(object? sender, EventArgs args)
    {
        if (!popup.IsOpen || target == null || !target.IsLoaded || PresentationSource.FromVisual(target) == null) return;
        // WPF recomputes monitor-edge placement when an offset changes.
        var position = target.PointToScreen(new Point());
        var size = target.RenderSize;
        var dpi = VisualTreeHelper.GetDpi(target);
        if (position == lastPosition && size == lastSize && dpi.Equals(lastDpi)) return;
        lastPosition = position; lastSize = size; lastDpi = dpi;
        popup.HorizontalOffset = .01; popup.HorizontalOffset = 0; highlight?.InvalidateVisual();
    }
    private Point lastPosition;
    private Size lastSize;
    private DpiScale lastDpi;
    private void OnKey(object sender, KeyEventArgs args) { if (args.Key == Key.Escape) { args.Handled = true; Dispose(); } }
    private void OwnerClosed(object? sender, EventArgs args) => Dispose();
    private void OwnerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) { if (!owner.IsVisible) Dispose(); }
    private void TargetUnloaded(object sender, RoutedEventArgs args) => Dispose();
    private void ClearTarget()
    {
        if (target != null) { target.LayoutUpdated -= Reposition; target.Unloaded -= TargetUnloaded; }
        if (highlight != null) layer?.Remove(highlight);
        highlight = null; layer = null; target = null;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        ClearTarget(); popup.IsOpen = false; popup.Child = null; popup.PlacementTarget = null;
        popup.CustomPopupPlacementCallback = null;
        owner.Closed -= OwnerClosed; owner.LocationChanged -= Reposition; owner.SizeChanged -= Reposition;
        owner.DpiChanged -= Reposition;
        owner.PreviewKeyDown -= OnKey; owner.IsVisibleChanged -= OwnerVisibilityChanged;
    }
    private sealed class Highlight : Adorner
    {
        internal Highlight(UIElement target) : base(target) { IsHitTestVisible = false; }
        protected override void OnRender(DrawingContext drawing)
        {
            drawing.DrawRoundedRectangle(null, new Pen(Ui.Brush("Accent"), 2), new Rect(AdornedElement.RenderSize), 8, 8);
        }
    }
}
