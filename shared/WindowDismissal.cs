using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace DesktopTools.Presentation;

/// <summary>Uses native transitions for opaque windows and a short scale/fade for layered surfaces.</summary>
internal static class WindowDismissal
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(WindowDismissal), new PropertyMetadata(null));
    private static int suppressed;
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(100);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    internal static void Attach(Window window, Func<bool> enabled, UIElement? embeddedSurface = null)
    {
        if (window.GetValue(StateProperty) is State) return;
        window.SetValue(StateProperty, new State(window, enabled, embeddedSurface));
    }

    internal static void Hide(Window window, Func<bool> enabled)
    {
        Attach(window, enabled);
        ((State)window.GetValue(StateProperty)).Hide();
    }

    // Showing a reusable tray window again must cancel an old pending hide.
    internal static void Cancel(Window window) => (window.GetValue(StateProperty) as State)?.Cancel();
    internal static bool IsDismissing(Window window) => (window.GetValue(StateProperty) as State)?.Running == true;
    internal static int NativeTransitionRevision(Window window) => (window.GetValue(StateProperty) as State)?.NativeTransitionRevision ?? 0;
    internal static IDisposable Suppress()
    {
        suppressed++;
        return new Scope();
    }
    private sealed class Scope : IDisposable
    {
        private bool disposed;
        public void Dispose() { if (disposed) return; disposed = true; suppressed--; }
    }

    private sealed class State
    {
        private readonly Window window;
        private readonly Func<bool> enabled;
        private readonly UIElement visual;
        private UIElement? input;
        private FrameworkElement? scaleTarget;
        private Transform? originalTransform;
        private Point originalOrigin;
        private bool hitTestVisible;
        private bool committing, closed, hiding;
        private double opacity;
        private int generation;
        internal bool Running { get; private set; }
        internal int NativeTransitionRevision { get; private set; }

        internal State(Window window, Func<bool> enabled, UIElement? embeddedSurface)
        {
            this.window = window; this.enabled = enabled; visual = embeddedSurface ?? window;
            window.Closing += Closing;
            window.Closed += Closed;
            window.PreviewKeyDown += KeyDown;
            window.IsVisibleChanged += VisibilityChanged;
            if (UsesNativeTransition) NativeTransition();
            // Embedded dialogs have their input visual in the owner's HWND.
            if (embeddedSurface is not null) embeddedSurface.PreviewKeyDown += KeyDown;
        }
        private bool CanAnimate => suppressed == 0 && enabled() && visual.IsVisible
            && !window.Dispatcher.HasShutdownStarted && window.WindowState != WindowState.Minimized;
        // WPF opacity on an opaque HWND reveals its acrylic/composition backplate.
        // Let DWM animate the complete native surface instead of fading only WPF pixels.
        private bool UsesNativeTransition => visual == window && !window.AllowsTransparency;
        private void NativeTransition()
        {
            nint handle = new WindowInteropHelper(window).Handle;
            if (handle == 0) return;
            if (WindowChrome.GetWindowChrome(window) != null)
            {
                // Retain the native caption style for DWM transitions. WindowChrome
                // still owns non-client rendering, so no system title bar is added.
                long style = GetWindowLongPtr(handle, -16).ToInt64();
                const long caption = 0x00C00000;
                if ((style & caption) != caption)
                {
                    _ = SetWindowLongPtr(handle, -16, new nint(style | caption));
                    _ = SetWindowPos(handle, 0, 0, 0, 0, 0, 0x0037 /* frame changed, no move/size/z-order/activation */);
                }
            }
            int disabled = suppressed == 0 && enabled() ? 0 : 1;
            if (DwmSetWindowAttribute(handle, 3 /* DWMWA_TRANSITIONS_FORCEDISABLED */, ref disabled, sizeof(int)) >= 0)
                NativeTransitionRevision++;
        }
        private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (window.IsVisible && UsesNativeTransition) NativeTransition();
        }

        private void Closing(object? sender, CancelEventArgs e)
        {
            if (committing || closed) return;
            // Recorder finalization, unsaved data and installer operations can veto closing.
            if (e.Cancel) { if (Running && !hiding) Cancel(); return; }
            if (UsesNativeTransition) { NativeTransition(); return; }
            if (!CanAnimate) { Cancel(); return; }
            bool? dialogResult = window.DialogResult;
            e.Cancel = true;
            if (Running) return;
            Begin(() =>
            {
                committing = true;
                try
                {
                    if (dialogResult.HasValue) window.DialogResult = dialogResult;
                    else window.Close();
                }
                finally
                {
                    committing = false;
                    // A later handler can still veto the final close; keep the window usable.
                    if (!closed) Cancel();
                }
            });
        }
        internal void Hide()
        {
            if (closed) return;
            if (UsesNativeTransition) { NativeTransition(); window.Hide(); return; }
            if (!CanAnimate) { Cancel(); window.Hide(); return; }
            if (Running) return;
            hiding = true;
            Begin(() => { window.Hide(); Cancel(); });
        }
        private void Begin(Action complete)
        {
            Running = true;
            int version = ++generation;
            opacity = visual.Opacity;
            input = visual == window ? window.Content as UIElement : visual;
            if (input is not null) { hitTestVisible = input.IsHitTestVisible; input.SetCurrentValue(UIElement.IsHitTestVisibleProperty, false); }
            scaleTarget = visual == window ? window.Content as FrameworkElement
                : visual is Panel { Children.Count: 1 } panel ? panel.Children[0] as FrameworkElement : visual as FrameworkElement;
            if (scaleTarget != null)
            {
                originalTransform = scaleTarget.RenderTransform;
                originalOrigin = scaleTarget.RenderTransformOrigin;
                var scale = new ScaleTransform(1, 1);
                var transform = new TransformGroup(); transform.Children.Add(originalTransform); transform.Children.Add(scale);
                scaleTarget.SetCurrentValue(UIElement.RenderTransformProperty, transform);
                scaleTarget.SetCurrentValue(UIElement.RenderTransformOriginProperty, new Point(.5, .5));
                foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
                    scale.BeginAnimation(property, new DoubleAnimation(1, .96, Duration)
                    { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            }
            var fade = new DoubleAnimation(opacity, 0, Duration)
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            fade.Completed += (_, _) => { if (!closed && version == generation) complete(); };
            visual.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        }
        internal void Cancel()
        {
            if (!Running) return;
            generation++; Running = hiding = false;
            visual.BeginAnimation(UIElement.OpacityProperty, null);
            if (scaleTarget != null)
            {
                scaleTarget.SetCurrentValue(UIElement.RenderTransformProperty, originalTransform);
                scaleTarget.SetCurrentValue(UIElement.RenderTransformOriginProperty, originalOrigin);
                scaleTarget = null; originalTransform = null;
            }
            if (input is not null) input.SetCurrentValue(UIElement.IsHitTestVisibleProperty, hitTestVisible);
            input = null;
        }
        private void KeyDown(object sender, KeyEventArgs e) { if (Running) e.Handled = true; }
        private void Closed(object? sender, EventArgs e)
        {
            // Never restore a closing surface: native destruction and modal removal can
            // still be pending, and restoring opacity produces a bright/gray final frame.
            closed = true; generation++; Running = hiding = false;
            input = null; scaleTarget = null; originalTransform = null;
            window.Closing -= Closing; window.Closed -= Closed; window.PreviewKeyDown -= KeyDown;
            window.IsVisibleChanged -= VisibilityChanged;
            if (visual != window) visual.PreviewKeyDown -= KeyDown;
            window.ClearValue(StateProperty);
        }
    }
}
