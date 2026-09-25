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
    public static void ModalEntrance(FrameworkElement element)
    {
        if (!Enabled) return;
        element.RenderTransformOrigin = new Point(.5, .5);
        var scale = new ScaleTransform(1, 1); element.RenderTransform = scale;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { FillBehavior = FillBehavior.Stop });
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            scale.BeginAnimation(property, new DoubleAnimation(.97, 1, TimeSpan.FromMilliseconds(230)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }
    public static void Reveal(FrameworkElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (!Enabled || !element.IsLoaded) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.86, 1, TimeSpan.FromMilliseconds(130)) { FillBehavior = FillBehavior.Stop });
    }
    public static void Transition(FrameworkElement element)
    {
        bool interrupted = element.HasAnimatedProperties ||
            element.RenderTransform is TranslateTransform active && active.HasAnimatedProperties;
        double opacity = interrupted ? element.Opacity : .86;
        double startY = interrupted && element.RenderTransform is TranslateTransform previous ? previous.Y : 3;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        var translation = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = translation; translation.BeginAnimation(TranslateTransform.YProperty, null);
        translation.Y = 0;
        if (!Enabled) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(opacity, 1, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop });
        translation.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(startY, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
    public static void PageTransition(FrameworkElement element) => PageTransition(element, Enabled && element.IsLoaded);

    internal static void PageTransition(FrameworkElement element, bool animate)
    {
        var translation = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        // Completed WPF clocks can still report animated properties. Every new page
        // must start from a fresh entrance, including after the first transition.
        const double startOpacity = .62;
        const double startX = 14;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        translation.BeginAnimation(TranslateTransform.XProperty, null);
        translation.BeginAnimation(TranslateTransform.YProperty, null);
        element.RenderTransform = translation;
        element.Opacity = 1;
        translation.X = 0;
        translation.Y = 0;
        if (!animate) return;

        var duration = TimeSpan.FromMilliseconds(165);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
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
        WindowEntrance(window);
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
        window.ContentRendered += (_, _) => { if (appeared) return; appeared = true; if (window.Content is FrameworkElement content) Transition(content); lastTick = clock.Elapsed; lifetime.Reset(dismissDuration, activitySource()); timer.Start(); };
        window.Closed += (_, _) => { closed = true; timer.Stop(); };
        return extended =>
        {
            if (closed) return;
            DesktopTools.Presentation.WindowDismissal.Cancel(window);
            dismissDuration = extended;
            if (window.Content is FrameworkElement content) { content.BeginAnimation(UIElement.OpacityProperty, null); content.Opacity = 1; }
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
                new DoubleAnimation(current, destination, TimeSpan.FromMilliseconds(180))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }
        Move(false);
        toggle.Loaded += (_, _) => Move(false);
        toggle.Checked += (_, _) => Move(true); toggle.Unchecked += (_, _) => Move(true);
    }
}
