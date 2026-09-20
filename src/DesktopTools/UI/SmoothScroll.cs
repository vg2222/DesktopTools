using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Animation;

namespace DesktopTools.UI;

internal static class SmoothScroll
{
    private static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false));
    private static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached("Target", typeof(double), typeof(SmoothScroll), new PropertyMetadata(0d));
    private static readonly DependencyProperty ActiveAnimationProperty = DependencyProperty.RegisterAttached("ActiveAnimation", typeof(DoubleAnimation), typeof(SmoothScroll));
    private static readonly DependencyProperty AnimatedOffsetProperty = DependencyProperty.RegisterAttached(
        "AnimatedOffset", typeof(double), typeof(SmoothScroll), new PropertyMetadata(0d, (element, args) =>
        {
            if (element is ScrollViewer viewer && args.NewValue is double offset) viewer.ScrollToVerticalOffset(offset);
        }));

    internal static void Enable(ScrollViewer viewer)
    {
        if ((bool)viewer.GetValue(EnabledProperty)) return;
        viewer.SetValue(EnabledProperty, true);
        viewer.PreviewMouseWheel += (_, args) =>
        {
            if (args.Handled || viewer.ScrollableHeight <= 0) return;
            for (DependencyObject? part = args.OriginalSource as DependencyObject; part != null && part != viewer; part = part is Visual or Visual3D ? VisualTreeHelper.GetParent(part) : LogicalTreeHelper.GetParent(part))
                if (part is ScrollViewer) return;
            double current = viewer.VerticalOffset;
            double pending = viewer.GetValue(ActiveAnimationProperty) != null ? (double)viewer.GetValue(TargetProperty) : current;
            // Accumulate rapid wheel ticks; use the current position immediately on direction reversal.
            if ((pending - current) * args.Delta > 0) pending = current;
            double distance = SystemParameters.WheelScrollLines < 0 ? viewer.ViewportHeight : Math.Max(1, SystemParameters.WheelScrollLines) * 32;
            double target = Math.Clamp(pending - args.Delta / 120d * distance, 0, viewer.ScrollableHeight);
            if (Math.Abs(current - target) < .5) return;
            args.Handled = true;
            Stop(viewer);
            viewer.SetValue(TargetProperty, target);
            if (!Motion.Enabled) { viewer.SetValue(AnimatedOffsetProperty, target); viewer.ScrollToVerticalOffset(target); return; }
            viewer.SetValue(AnimatedOffsetProperty, current);
            var animation = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
            };
            animation.Completed += (_, _) =>
            {
                if (!ReferenceEquals(viewer.GetValue(ActiveAnimationProperty), animation)) return;
                viewer.ClearValue(ActiveAnimationProperty);
                // FillBehavior.Stop leaves its clock attached; remove it so later wheel input starts
                // from the actual offset after navigation, keyboard input or scrollbar dragging.
                viewer.BeginAnimation(AnimatedOffsetProperty, null);
            };
            viewer.SetValue(ActiveAnimationProperty, animation);
            viewer.BeginAnimation(AnimatedOffsetProperty, animation);
            // Keep the final position after FillBehavior.Stop restores the base value.
            viewer.SetValue(AnimatedOffsetProperty, target);
        };
        viewer.PreviewMouseDown += (_, _) => Stop(viewer);
        viewer.PreviewKeyDown += (_, args) =>
        {
            if (args.Key is Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Up or Key.Down) Stop(viewer);
        };
        viewer.Unloaded += (_, _) => Stop(viewer);
    }

    internal static void ScrollToOffset(ScrollViewer viewer, double offset)
    {
        Stop(viewer);
        viewer.ScrollToVerticalOffset(offset);
    }

    private static void Stop(ScrollViewer viewer)
    {
        if (viewer.GetValue(ActiveAnimationProperty) == null) return;
        double current = viewer.VerticalOffset;
        viewer.ClearValue(ActiveAnimationProperty);
        viewer.SetValue(AnimatedOffsetProperty, current);
        viewer.BeginAnimation(AnimatedOffsetProperty, null);
    }
}
