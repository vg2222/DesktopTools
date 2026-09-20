using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using DesktopTools.Native;
using DesktopTools.UI;

internal static class GuidePlacementChecks
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static async Task RunAsync()
    {
        var monitor = MonitorService.GetCurrent(true); var work = monitor.WorkingArea;
        var button = new Button { Content = "Guide target", Width = 120, Height = 40 };
        var owner = new Window { Width = 440, Height = 300, ShowActivated = false, WindowStyle = WindowStyle.None,
            Content = new AdornerDecorator { Child = new Grid { Children = { button } } } };
        try
        {
            owner.Show();
            foreach (bool right in new[] { false, true })
            foreach (bool bottom in new[] { false, true })
            {
                owner.Left = (right ? work.Right - owner.Width * monitor.ScaleX : work.Left) / monitor.ScaleX;
                owner.Top = (bottom ? work.Bottom - owner.Height * monitor.ScaleY : work.Top) / monitor.ScaleY;
                button.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                button.VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
                owner.UpdateLayout(); await Task.Delay(50);
                using var tour = new GuidedTour(owner, [new(() => button, "Guide target", "A hint beside its control.")], 0, (_, _) => true);
                await Task.Delay(100);
                var popup = (Popup)typeof(GuidedTour).GetField("popup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tour)!;
                var child = (FrameworkElement)popup.Child;
                var hint = new Rect(child.PointToScreen(new Point()), child.PointToScreen(new Point(child.ActualWidth, child.ActualHeight)));
                var target = new Rect(button.PointToScreen(new Point()), button.PointToScreen(new Point(button.ActualWidth, button.ActualHeight)));
                Check(!hint.IntersectsWith(target), "Hint covered the target near a screen corner");
                var tolerance = work; tolerance.Inflate(2, 2); Check(tolerance.Contains(hint), "Hint extended outside the work area");
            }
            // Negative origins and target-relative coordinates at 100/150/200% without changing OS settings.
            foreach (double scale in new[] { 1d, 1.5, 2 })
            {
                var area = new Rect(-1500 / scale, -900 / scale, 1600 / scale, 1000 / scale);
                var hint = new Size(302, 170); var control = new Size(60, 36);
                var placements = GuidedTour.HintPlacements(hint, control, area);
                Check(placements.Length > 0 && area.Contains(new Rect(placements[0].Point, hint)) && !new Rect(new Point(), control).IntersectsWith(new Rect(placements[0].Point, hint)), "DPI/negative-origin placement is invalid");
            }
        }
        finally { owner.Close(); }
    }
}
