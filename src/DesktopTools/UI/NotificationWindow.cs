using DesktopTools.Localization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using DesktopTools.Native;
using DesktopTools.Core;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DesktopTools.UI;

public enum NotificationKind { Info, Warning, Error }

/// <summary>A transient app notification with no system chrome or activation on display.</summary>
public sealed class NotificationWindow : Window
{
    public NotificationKind Kind { get; private set; }
    private readonly string noticeStyle;
    private readonly BitmapSource? noticeImage;
    private (string Label, Action Run)[]? noticeActions;
    private readonly Action<TimeSpan> resetDismissal;
    private bool noticeRendered;
    private bool resizeRunning;
    private bool placementQueued;
    private int resizeGeneration;
    private double pendingHeight = double.NaN;
    private double? previousProgress;
    public bool HoldOpen { get; set; }
    internal Action? ClickAction { get; set; }
    internal double AutoDismissSeconds { get; private set; }
    public NotificationWindow(string message, NotificationKind kind, Window? owner = null, string style = "Capsule",
        BitmapSource? image = null, (string Label, Action Run)[]? actions = null, double seconds = 6)
    {
        Kind = kind;
        noticeStyle = style; noticeImage = image; noticeActions = actions;
        Title = "DesktopTools " + L.T(kind.ToString()); Width = 400; SizeToContent = SizeToContent.Height;
        Tag = "Notifications";
        if (image != null) Width = style == "Preview" ? 460 : 410;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
        ShowActivated = false; ShowInTaskbar = false; Focusable = false;

        Content = NotificationSurface.Create(message, kind, style, Close, image, actions);
        PreviewMouseLeftButtonUp += (_, e) =>
        {
            for (DependencyObject? item = e.OriginalSource as DependencyObject; item != null;
                 item = item is Visual ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item))
                if (item is ButtonBase) return;
            ActivateBody();
        };
        AutomationProperties.SetName(this, L.T(kind.ToString()) + ": " + message);
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            long style = NativeMethods.GetWindowLongPtr(handle, -20).ToInt64();
            NativeMethods.SetWindowLongPtr(handle, -20, new nint(style | 0x08000000 | 0x80));
            NativeWindowService.TryExcludeFromCapture(this, true, out _);
        };
        Loaded += (_, _) => PositionAll();
        ContentRendered += (_, _) => noticeRendered = true;
        Closed += (_, _) => PositionAll();
        SizeChanged += (_, _) => { if (resizeRunning) QueuePositionAll(); };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(PositionAll));
        resetDismissal = Motion.AutoDismiss(this, TimeSpan.FromSeconds(double.IsFinite(seconds) ? Math.Clamp(seconds, 3, 30) : 6), () => HoldOpen,
            hoverSource: IsCursorWithinWindow);
    }

    internal void ActivateBody() => ClickAction?.Invoke();

    private bool IsCursorWithinWindow()
    {
        if (!IsVisible || !IsLoaded) return false;
        nint handle = new WindowInteropHelper(this).Handle;
        return handle != 0 && NativeMethods.GetCursorPos(out var cursor) && NativeMethods.GetWindowRect(handle, out var bounds) &&
            cursor.X >= bounds.Left && cursor.X < bounds.Right && cursor.Y >= bounds.Top && cursor.Y < bounds.Bottom;
    }

    public void UpdateMessage(string message, double? progress = null, NotificationKind? kind = null, double? seconds = null, (string Label, Action Run)[]? actions = null)
    {
        double oldHeight = ActualHeight;
        bool animateResize = noticeRendered && IsVisible && Motion.Enabled && double.IsFinite(oldHeight) && oldHeight > 0;
        if (animateResize && SizeToContent != SizeToContent.Manual)
        {
            SizeToContent = SizeToContent.Manual;
            Height = oldHeight;
        }
        bool animateTint = kind.HasValue && kind != Kind;
        if (kind.HasValue) Kind = kind.Value;
        if (actions != null) noticeActions = actions;
        var surface = NotificationSurface.Create(message, Kind, noticeStyle, Close, noticeImage, noticeActions, progress, previousProgress);
        previousProgress = progress;
        if (!noticeRendered) Motion.PrepareNoticeEntrance(surface);
        Content = surface; AutomationProperties.SetName(this, L.T(Kind.ToString()) + ": " + message);
        if (Kind is NotificationKind.Error or NotificationKind.Warning && !SystemParameters.HighContrast)
        {
            var brush = new SolidColorBrush((TryFindResource("Surface") as SolidColorBrush)?.Color ?? Colors.White);
            surface.Background = brush;
            var basis = brush.Color;
            var accent = Kind == NotificationKind.Error ? Color.FromRgb(190, 55, 67) : Color.FromRgb(190, 116, 30);
            var tintColor = Color.FromArgb(basis.A, (byte)(basis.R * .8 + accent.R * .2), (byte)(basis.G * .8 + accent.G * .2), (byte)(basis.B * .8 + accent.B * .2));
            if (Motion.Enabled && animateTint) brush.BeginAnimation(SolidColorBrush.ColorProperty, new System.Windows.Media.Animation.ColorAnimation(tintColor, TimeSpan.FromMilliseconds(800)));
            else brush.Color = tintColor;
        }
        if (seconds.HasValue) resetDismissal(TimeSpan.FromSeconds(Math.Clamp(seconds.Value, 3, 120)));
        if (animateResize) AnimateUpdatedHeight(surface, oldHeight);
        else
        {
            if (resizeRunning) { resizeGeneration++; resizeRunning = false; BeginAnimation(HeightProperty, null); SizeToContent = SizeToContent.Height; }
            QueuePositionAll();
        }
    }

    private void AnimateUpdatedHeight(FrameworkElement surface, double oldHeight)
    {
        surface.Measure(new Size(Width, double.PositiveInfinity));
        double target = surface.DesiredSize.Height;
        if (!double.IsFinite(target) || target <= 0) { SizeToContent = SizeToContent.Height; QueuePositionAll(); return; }
        if (resizeRunning && Math.Abs(target - pendingHeight) < 1) { QueuePositionAll(); return; }
        if (Math.Abs(target - oldHeight) < 1)
        {
            resizeGeneration++; resizeRunning = false;
            BeginAnimation(HeightProperty, null); Height = target; SizeToContent = SizeToContent.Height;
            QueuePositionAll(); return;
        }
        int generation = ++resizeGeneration;
        resizeRunning = true; pendingHeight = target;
        Height = target;
        var animation = new DoubleAnimation(oldHeight, target, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            if (generation != resizeGeneration || !IsLoaded) return;
            resizeRunning = false;
            BeginAnimation(HeightProperty, null);
            Height = target;
            SizeToContent = SizeToContent.Height;
            QueuePositionAll();
        };
        BeginAnimation(HeightProperty, animation);
        QueuePositionAll();
    }

    private void QueuePositionAll()
    {
        if (placementQueued) return;
        placementQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            placementQueued = false;
            if (IsVisible) PositionAll();
        }), DispatcherPriority.Render);
    }

    private static void PositionAll()
    {
        var monitor = MonitorService.GetCurrent(primary: true);
        double gap = 10 * monitor.ScaleX;
        var notices = Application.Current.Windows.OfType<NotificationWindow>().Where(w => w.IsVisible && w.IsLoaded).ToArray();
        var placed = new List<Rect>();
        foreach (var notice in notices)
        {
            var bounds = NoticePlacement.Calculate(monitor.WorkingArea, new Size(notice.Width, notice.ActualHeight), monitor.ScaleX, monitor.ScaleY);
            var xCandidates = new HashSet<double> { bounds.X, monitor.WorkingArea.Left, monitor.WorkingArea.Right - bounds.Width };
            foreach (var occupied in placed)
            {
                xCandidates.Add(occupied.Right + gap);
                xCandidates.Add(occupied.Left - bounds.Width - gap);
            }
            Rect? available = null;
            foreach (double x in xCandidates.Where(x => x >= monitor.WorkingArea.Left && x + bounds.Width <= monitor.WorkingArea.Right)
                .OrderBy(x => Math.Abs(x + bounds.Width / 2 - (monitor.WorkingArea.Left + monitor.WorkingArea.Width / 2))))
            {
                var candidate = new Rect(x, bounds.Y, bounds.Width, bounds.Height);
                while (true)
                {
                    var collisions = placed.Where(candidate.IntersectsWith).ToArray();
                    if (collisions.Length == 0) { available = candidate; break; }
                    candidate.Y = collisions.Max(rect => rect.Bottom) + gap;
                    if (candidate.Bottom > monitor.WorkingArea.Bottom) break;
                }
                if (available.HasValue) break;
            }
            if (!available.HasValue)
            {
                // A full monitor cannot show another notice without overlap. Prefer the newer message.
                notices.FirstOrDefault(window => window != notice && window.IsVisible)?.Close();
                return;
            }
            var target = available.Value; placed.Add(target);
            NativeMethods.SetWindowPos(new WindowInteropHelper(notice).Handle, new nint(-1), (int)target.X, (int)target.Y,
                (int)target.Width, (int)target.Height, 0x10 | 0x0200);
        }
    }
}
