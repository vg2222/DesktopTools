using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DesktopTools.UI;

/// <summary>Short, input-independent transitions. Never used on ink or capture surfaces.</summary>
public static class Motion
{
    public static readonly DependencyProperty FeedbackProperty = DependencyProperty.RegisterAttached(
        "Feedback", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnFeedbackChanged));
    private static readonly DependencyProperty FeedbackAttachedProperty = DependencyProperty.RegisterAttached(
        "FeedbackAttached", typeof(bool), typeof(Motion), new PropertyMetadata(false));
    private static readonly DependencyProperty EntranceAttachedProperty = DependencyProperty.RegisterAttached(
        "EntranceAttached", typeof(bool), typeof(Motion), new PropertyMetadata(false));
    public static bool GetFeedback(DependencyObject target) => (bool)target.GetValue(FeedbackProperty);
    public static void SetFeedback(DependencyObject target, bool value) => target.SetValue(FeedbackProperty, value);
    private static void OnFeedbackChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        if (target is Button button) ButtonFeedback(button);
        else if (target is CheckBox toggle) ToggleFeedback(toggle);
    }
    public static bool AppEnabled { get; set; } = true;
    public static bool Enabled => AppEnabled && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
    /// <summary>The house easing: fast start, long soft landing. Quartic ease-out reads as smoother than the default cubic.</summary>
    internal static readonly IEasingFunction Out = Frozen(new PowerEase { Power = 4, EasingMode = EasingMode.EaseOut });
    /// <summary>Settles with a very small overshoot, for things that should feel like they land (toggles, selection marks, dialogs).</summary>
    internal static readonly IEasingFunction Spring = Frozen(new BackEase { Amplitude = .45, EasingMode = EasingMode.EaseOut });
    /// <summary>Accelerates away, for things leaving the screen.</summary>
    internal static readonly IEasingFunction In = Frozen(new PowerEase { Power = 3, EasingMode = EasingMode.EaseIn });
    private static T Frozen<T>(T easing) where T : Freezable { easing.Freeze(); return easing; }
    public static void ModalEntrance(FrameworkElement element)
    {
        if (!Enabled) return;
        element.RenderTransformOrigin = new Point(.5, .5);
        var scale = new ScaleTransform(1, 1); element.RenderTransform = scale;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = Out, FillBehavior = FillBehavior.Stop });
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            scale.BeginAnimation(property, new DoubleAnimation(.94, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = Spring, FillBehavior = FillBehavior.Stop });
    }
    /// <summary>
    /// A selection mark that moves with the selection instead of jumping: the fill fades, the accent bar grows from its middle
    /// with a small overshoot (and shrinks quickly when deselected).
    /// </summary>
    internal static void Select(UIElement fill, FrameworkElement bar, bool on, bool animate)
    {
        var scale = bar.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 0);
        bar.RenderTransformOrigin = new Point(.5, .5); bar.RenderTransform = scale;
        double fromFill = fill.Opacity, fromBar = scale.ScaleY, toFill = on ? 1 : 0, toBar = on ? 1 : 0;
        fill.BeginAnimation(UIElement.OpacityProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        fill.Opacity = toFill; scale.ScaleY = toBar;
        if (!animate || !Enabled) return;
        fill.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(fromFill, toFill, TimeSpan.FromMilliseconds(on ? 240 : 170)) { EasingFunction = Out, FillBehavior = FillBehavior.Stop });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(fromBar, toBar, TimeSpan.FromMilliseconds(on ? 360 : 150)) { EasingFunction = on ? Spring : In, FillBehavior = FillBehavior.Stop });
    }
    public static void Reveal(FrameworkElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (!Enabled || !element.IsLoaded) return;
        // A window settles into place: it fades up while rising a few pixels. Nothing is ever fully transparent, so a
        // window captured mid-entrance still shows its content.
        var ease = Out;
        var duration = TimeSpan.FromMilliseconds(320);
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.6, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        if (element.RenderTransform is null or MatrixTransform { Matrix.IsIdentity: true } or TranslateTransform)
        {
            var rise = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
            element.RenderTransform = rise; rise.BeginAnimation(TranslateTransform.YProperty, null); rise.Y = 0;
            rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        }
    }
    /// <summary>Menus and drop-down lists unfold from their anchor instead of appearing at once.</summary>
    public static void PopupEntrance(FrameworkElement? surface)
    {
        if (surface == null || !Enabled) return;
        var ease = Out;
        var duration = TimeSpan.FromMilliseconds(190);
        surface.RenderTransformOrigin = new Point(.5, 0);
        var group = new TransformGroup(); var scale = new ScaleTransform(1, 1); var slide = new TranslateTransform();
        group.Children.Add(scale); group.Children.Add(slide); surface.RenderTransform = group;
        surface.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.98, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.94, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }
    public static void Transition(FrameworkElement element)
    {
        bool interrupted = element.HasAnimatedProperties ||
            element.RenderTransform is TranslateTransform active && active.HasAnimatedProperties;
        double opacity = interrupted ? element.Opacity : .76;
        double startY = interrupted && element.RenderTransform is TranslateTransform previous ? previous.Y : 6;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        var translation = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = translation; translation.BeginAnimation(TranslateTransform.YProperty, null);
        translation.Y = 0;
        if (!Enabled) return;
        var duration = TimeSpan.FromMilliseconds(240);
        var ease = Out;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(opacity, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        translation.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(startY, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }
    internal static void PrepareNoticeEntrance(FrameworkElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = Enabled ? 0 : 1;
        element.RenderTransform = new TranslateTransform(0, Enabled ? 8 : 0);
    }

    private static void CompleteNoticeEntrance(FrameworkElement element)
    {
        var translation = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = translation;
        element.Opacity = 1;
        translation.Y = 0;
        if (!Enabled) return;
        var duration = TimeSpan.FromMilliseconds(300);
        var ease = Out;
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        translation.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }
    public static void PageTransition(FrameworkElement element) => PageTransition(element, Enabled && element.IsLoaded);

    internal static void PageTransition(FrameworkElement element, bool animate)
    {
        var translation = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        // Completed WPF clocks can still report animated properties. Every new page
        // must start from a fresh entrance, including after the first transition.
        const double startOpacity = .5;
        const double startX = 22;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        translation.BeginAnimation(TranslateTransform.XProperty, null);
        translation.BeginAnimation(TranslateTransform.YProperty, null);
        element.RenderTransform = translation;
        element.Opacity = 1;
        translation.X = 0;
        translation.Y = 0;
        if (!animate) return;

        var duration = TimeSpan.FromMilliseconds(280);
        var ease = Out;
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(startOpacity, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        translation.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(startX, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }
    public static void WindowEntrance(Window window, bool animateClose = true)
    {
        if ((bool)window.GetValue(EntranceAttachedProperty)) return;
        window.SetValue(EntranceAttachedProperty, true);
        window.Loaded += (_, _) =>
        {
            // Attach after constructor closing guards (saving, recording, etc.).
            if (animateClose) DesktopTools.Presentation.WindowDismissal.Attach(window, () => Enabled);
            if (window.Content is FrameworkElement content) Reveal(content);
        };
    }
    private static TranslateTransform Offset(FrameworkElement element)
    {
        if (element.RenderTransform is not TranslateTransform offset) element.RenderTransform = offset = new TranslateTransform();
        return offset;
    }
    /// <summary>Raises tiles a couple of pixels under the pointer. Not for ink or capture surfaces.</summary>
    public static void HoverLift(FrameworkElement element, double lift = 2)
    {
        var offset = Offset(element);
        void MoveTo(double y)
        {
            double from = offset.Y;
            offset.BeginAnimation(TranslateTransform.YProperty, null); offset.Y = y;
            if (Enabled) offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from, y, TimeSpan.FromMilliseconds(200))
                { EasingFunction = Out, FillBehavior = FillBehavior.Stop });
        }
        element.MouseEnter += (_, _) => { if (element.IsEnabled) MoveTo(-lift); };
        element.MouseLeave += (_, _) => MoveTo(0);
    }
    /// <summary>Fades and slides a set of elements in one after another; does nothing when motion is off.</summary>
    public static void Stagger(IEnumerable<FrameworkElement> items, int stepMilliseconds = 35, double distance = 10)
    {
        if (!Enabled) return;
        int index = 0;
        var ease = Out;
        foreach (var item in items)
        {
            var offset = Offset(item);
            var delay = TimeSpan.FromMilliseconds(Math.Min(index++, 12) * stepMilliseconds);
            var duration = TimeSpan.FromMilliseconds(380);
            item.Opacity = 0; offset.Y = distance;
            var fade = new DoubleAnimation(0, 1, duration) { BeginTime = delay, EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
            fade.Completed += (_, _) => { item.BeginAnimation(UIElement.OpacityProperty, null); item.Opacity = 1; };
            var slide = new DoubleAnimation(distance, 0, duration) { BeginTime = delay, EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
            slide.Completed += (_, _) => { offset.BeginAnimation(TranslateTransform.YProperty, null); offset.Y = 0; };
            item.BeginAnimation(UIElement.OpacityProperty, fade); offset.BeginAnimation(TranslateTransform.YProperty, slide);
        }
    }
    public static void ButtonFeedback(Button button)
    {
        if ((bool)button.GetValue(FeedbackAttachedProperty)) return;
        button.SetValue(FeedbackAttachedProperty, true);
        void Respond()
        {
            if (button.Template?.FindName("Feedback", button) is not Border feedback) return;
            double target = !button.IsEnabled ? 0 : button.IsPressed ? .11 : button.IsMouseOver ? .045 : 0;
            double current = feedback.Opacity;
            feedback.BeginAnimation(UIElement.OpacityProperty, null); feedback.Opacity = target;
            if (Enabled) feedback.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(button.IsPressed ? 55 : 110)) { FillBehavior = FillBehavior.Stop });
            // Small press-in so clicks feel physical; large content (preview tiles, cards) is left alone.
            if (button.Template.FindName("PressScale", button) is ScaleTransform press && button.ActualWidth <= 420 && button.ActualHeight <= 120)
            {
                double scale = button.IsEnabled && button.IsPressed ? .965 : 1;
                press.BeginAnimation(ScaleTransform.ScaleXProperty, null); press.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                double from = press.ScaleX; press.ScaleX = press.ScaleY = scale;
                if (Enabled) foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
                    press.BeginAnimation(property, new DoubleAnimation(from, scale, TimeSpan.FromMilliseconds(button.IsPressed ? 70 : 160)) { EasingFunction = Out, FillBehavior = FillBehavior.Stop });
            }
        }
        button.MouseEnter += (_, _) => Respond(); button.MouseLeave += (_, _) => Respond();
        button.IsEnabledChanged += (_, _) => Respond();
        // IsPressed is set by ButtonBase after preview events, including keyboard activation.
        button.PreviewMouseLeftButtonDown += (_, _) => button.Dispatcher.BeginInvoke(Respond, DispatcherPriority.Input);
        button.PreviewMouseLeftButtonUp += (_, _) => button.Dispatcher.BeginInvoke(Respond, DispatcherPriority.Input);
        button.KeyDown += (_, _) => button.Dispatcher.BeginInvoke(Respond, DispatcherPriority.Input);
        button.KeyUp += (_, _) => button.Dispatcher.BeginInvoke(Respond, DispatcherPriority.Input);
        button.LostMouseCapture += (_, _) => Respond();
    }
    public static Action<TimeSpan> AutoDismiss(Window window, TimeSpan duration, Func<bool>? held = null, Func<uint?>? activitySource = null, Func<bool>? hoverSource = null)
    {
        // Pause while hidden for capture or while the user is interacting with an action.
        var dismissDuration = duration;
        activitySource ??= DesktopTools.Native.UserActivity.LastInputTick;
        if (window.Content is FrameworkElement initialContent) PrepareNoticeEntrance(initialContent);
        window.Loaded += (_, _) => DesktopTools.Presentation.WindowDismissal.Attach(window, () => Enabled);
        var lifetime = new DesktopTools.Core.NotificationLifetime(dismissDuration, activitySource());
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var lastTick = clock.Elapsed;
        bool appeared = false;
        bool closed = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            var now = clock.Elapsed; var elapsed = now - lastTick; lastTick = now;
            if (!lifetime.Advance(elapsed, activitySource(),
                !window.IsVisible || window.IsMouseOver || window.IsKeyboardFocusWithin || hoverSource?.Invoke() == true || held?.Invoke() == true)) return;
            timer.Stop();
            window.Close();
        };
        // WPF can raise ContentRendered after Closed for a window closed before its first frame; a timer
        // started then never stops and keeps the closed window and its screenshot alive.
        window.ContentRendered += (_, _) => { if (appeared || closed) return; appeared = true; if (window.Content is FrameworkElement content) CompleteNoticeEntrance(content); lastTick = clock.Elapsed; lifetime.Reset(dismissDuration, activitySource()); timer.Start(); };
        window.Closed += (_, _) => { closed = true; timer.Stop(); };
        return extended =>
        {
            if (closed) return;
            DesktopTools.Presentation.WindowDismissal.Cancel(window);
            dismissDuration = extended;
            if (appeared && window.Content is FrameworkElement content) { content.BeginAnimation(UIElement.OpacityProperty, null); content.Opacity = 1; }
            lifetime.Reset(extended, activitySource()); lastTick = clock.Elapsed; timer.Start();
        };
    }
    public static readonly DependencyProperty TogglePositionProperty = DependencyProperty.RegisterAttached(
        "TogglePosition", typeof(double), typeof(Motion), new PropertyMetadata(0d));
    public static double GetTogglePosition(DependencyObject element) => (double)element.GetValue(TogglePositionProperty);
    public static void SetTogglePosition(DependencyObject element, double value) => element.SetValue(TogglePositionProperty, value);
    public static void ToggleFeedback(CheckBox toggle)
    {
        if ((bool)toggle.GetValue(FeedbackAttachedProperty)) return;
        toggle.SetValue(FeedbackAttachedProperty, true);
        void Move(bool animate)
        {
            double destination = toggle.IsChecked == true ? 20 : 0;
            double current = GetTogglePosition(toggle);
            toggle.BeginAnimation(TogglePositionProperty, null);
            SetTogglePosition(toggle, destination);
            if (animate && Enabled) toggle.BeginAnimation(TogglePositionProperty,
                new DoubleAnimation(current, destination, TimeSpan.FromMilliseconds(300))
                { EasingFunction = Spring, FillBehavior = FillBehavior.Stop });
        }
        Move(false);
        toggle.Loaded += (_, _) => Move(false);
        toggle.Checked += (_, _) => Move(true); toggle.Unchecked += (_, _) => Move(true);
    }
}
