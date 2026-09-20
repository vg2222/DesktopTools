using DesktopTools.Localization;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DesktopTools.Native;
using DesktopTools.UI;

namespace DesktopTools.Extras;

/// <summary>Independent, opt-in presentation aids. No hooks or polling survive StopAll.</summary>
public sealed class PresentationHudService : IDisposable
{
    private readonly Action<string> _report;
    private readonly Func<bool> _excludeControls;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly HookProc _mouseCallback, _keyboardCallback;
    private readonly List<Window> _rings = [];
    private readonly Dictionary<Window, DispatcherTimer> _ringTimers = [];
    private nint _mouseHook, _keyboardHook;
    private Window? _keys, _timerWindow;
    private CountdownWindow? _countdown;
    private ScreenRulerWindow? _ruler;
    private readonly List<Window> _blackouts = [];
    private bool _blackoutSharingOnly;
    public bool BlackoutSharingOnly
    {
        get => _blackoutSharingOnly;
        set
        {
            Verify(); if (_blackoutSharingOnly == value) return;
            _blackoutSharingOnly = value;
            // An explicit new activation is required after changing the mode.
            StopBlackout(); Changed?.Invoke();
        }
    }
    private TextBlock? _keyText;
    private DispatcherTimer? _keyExpiry, _timerTick;
    private DispatcherTimer? _keyFadeExpiry;
    private readonly Stopwatch _stopwatch = new();
    private int _pendingClicks, _clickGeneration, _keyGeneration;
    private bool _keyPending, _disposed;
    private string? _pendingShortcut;
    private int _shortcutAnimationGeneration;
    private double _countdownMinutes = 5, _rulerLength = 600;
    public double ShortcutDisplaySeconds { get; set; } = 1.6;
    public bool ShortcutAnimations { get; set; } = true;
    public string ClickColor { get; set; } = "#63D0FF";
    public double ClickSize { get; set; } = 64;
    public double ClickDurationMilliseconds { get; set; } = 450;
    public double CountdownMinutes
    {
        get => _countdownMinutes;
        set { Verify(); double minutes = Number(value, 1, 120, 5); if (_countdownMinutes == minutes) return; _countdownMinutes = minutes; if (_countdown != null) _countdown.Minutes = minutes; }
    }
    public double RulerLength
    {
        get => _rulerLength;
        set { Verify(); double length = Number(value, 100, 1600, 600); if (_rulerLength == length) return; _rulerLength = length; if (_ruler != null) _ruler.Length = length; }
    }
    public Func<string, bool> IsAvailable { get; set; } = _ => true;
    public void ApplyAvailability()
    {
        Verify();
        if (IsClickIndicatorsEnabled && !IsAvailable("aid-Click indicators")) ToggleClickIndicators();
        if (IsKeystrokesEnabled && !IsAvailable("aid-Shortcut display")) ToggleKeystrokes();
        if (IsTimerVisible && !IsAvailable("aid-Stopwatch")) ToggleTimer();
        if (IsCountdownVisible && !IsAvailable("aid-Countdown")) ToggleCountdown();
        if (IsRulerVisible && !IsAvailable("aid-Screen ruler")) ToggleRuler();
        if (IsBlackoutVisible && !IsAvailable("aid-Screen blackout")) ToggleBlackout();
    }
    public event Action? Changed;
    public bool IsClickIndicatorsEnabled => _mouseHook != 0;
    public bool IsKeystrokesEnabled => _keyboardHook != 0;
    public bool IsTimerVisible => _timerWindow != null;
    public bool IsCountdownVisible => _countdown != null;
    public bool IsRulerVisible => _ruler != null;
    public bool IsBlackoutVisible => _blackouts.Count != 0;

    public PresentationHudService(Action<string> report, Func<bool>? excludeControls = null)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _excludeControls = excludeControls ?? (() => false);
        _mouseCallback = MouseHook; _keyboardCallback = KeyboardHook;
    }
    public void ToggleClickIndicators()
    {
        Verify();
        if (!IsClickIndicatorsEnabled && !IsAvailable("aid-Click indicators")) return;
        if (IsClickIndicatorsEnabled) StopClicks();
        else _mouseHook = Install(14, _mouseCallback);
        Changed?.Invoke();
    }
    public void ToggleKeystrokes()
    {
        Verify();
        if (!IsKeystrokesEnabled && !IsAvailable("aid-Shortcut display")) return;
        if (IsKeystrokesEnabled) StopKeys();
        else _keyboardHook = Install(13, _keyboardCallback);
        Changed?.Invoke();
    }
    private nint Install(int kind, HookProc callback)
    {
        nint hook = SetWindowsHookEx(kind, callback, GetModuleHandle(null), 0);
        if (hook == 0) _report(L.T("Could not enable presentation input: ") + new Win32Exception(Marshal.GetLastWin32Error()).Message);
        return hook;
    }
    private nint MouseHook(int code, nint message, nint data)
    {
        // Copy only click coordinates; do not create windows or call application code in the hook.
        try
        {
            if (code >= 0 && IsClickIndicatorsEnabled && message is 0x201 or 0x204 or 0x207 && _pendingClicks < 8)
            {
                var input = Marshal.PtrToStructure<MouseInput>(data);
                int generation = _clickGeneration;
                bool right = message == 0x204;
                _pendingClicks++;
                _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    _pendingClicks--;
                    if (generation != _clickGeneration || !IsClickIndicatorsEnabled || _disposed) return;
                    try { ShowRing(input.Point, right); }
                    catch (Exception ex) { _report(L.T("Click indicator unavailable: ") + ex.Message); }
                }));
            }
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
        return CallNextHookEx(0, code, message, data);
    }
    private nint KeyboardHook(int code, nint message, nint data)
    {
        try
        {
            if (code >= 0 && IsKeystrokesEnabled && message is 0x100 or 0x104)
            {
                uint key = unchecked((uint)Marshal.ReadInt32(data));
                // Never use ToUnicode, read focused controls, record text, or retain a key history.
                string? shortcut = PresentationShortcutFormatter.Format(key, Down(0x11), Down(0x12), Down(0x10), Down(0x5B) || Down(0x5C), Down(0xA5));
                if (shortcut != null)
                {
                    _pendingShortcut = shortcut;
                    if (!_keyPending)
                    {
                        _keyPending = true;
                        int generation = _keyGeneration;
                        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                        {
                            _keyPending = false;
                            if (generation != _keyGeneration || !IsKeystrokesEnabled || _disposed) return;
                            string? label = _pendingShortcut; _pendingShortcut = null;
                            if (label == null) return;
                            try { ShowShortcut(label); }
                            catch (Exception ex) { _report(L.T("Shortcut display unavailable: ") + ex.Message); }
                        }));
                    }
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
        return CallNextHookEx(0, code, message, data);
    }
    private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    private static double Number(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private Window Shell(string title, double width, double height, bool interactive = false)
    {
        var window = new Window { Title = "DesktopTools " + title, Width = width, Height = height,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true,
            Background = Brushes.Transparent, ShowInTaskbar = false, ShowActivated = false,
            Topmost = true, Focusable = interactive, Tag = interactive ? "Stopwatch" : AppCapturePrivacy.PresentationSurface };
        window.SourceInitialized += (_, _) => NativeWindowService.ConfigureOverlay(window, !interactive);
        if (interactive) Motion.WindowEntrance(window);
        return window;
    }
    private static Border Card(UIElement content)
    {
        var card = new Border { Child = content, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(16) };
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        card.SetResourceReference(Border.BorderBrushProperty, "Stroke"); return card;
    }
    private static void Place(Window window, MonitorInfo monitor, double physicalX, double physicalY)
    {
        if (!NativeMethods.SetWindowPos(new WindowInteropHelper(window).Handle, new nint(-1),
            (int)Math.Round(physicalX), (int)Math.Round(physicalY),
            (int)Math.Round((window.ActualWidth > 0 ? window.ActualWidth : window.Width) * monitor.ScaleX), (int)Math.Round((window.ActualHeight > 0 ? window.ActualHeight : window.Height) * monitor.ScaleY), 0x10))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private void ShowRing(NativeMethods.Point point, bool right)
    {
        while (_rings.Count >= 8) CloseRing(_rings[0]);
        var monitor = MonitorService.GetAll().FirstOrDefault(m => m.Bounds.Contains(new Point(point.X, point.Y))) ?? MonitorService.GetCurrent();
        double size = Number(ClickSize, 24, 160, 64);
        var duration = TimeSpan.FromMilliseconds(Number(ClickDurationMilliseconds, 100, 2000, 450));
        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(ClickColor); }
        catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException) { color = Color.FromRgb(99, 208, 255); }
        var window = Shell(L.T("Click"), size, size);
        var ring = new Ellipse { Margin = new Thickness(size * .14), StrokeThickness = right ? 4 : 3,
            Stroke = new SolidColorBrush(color), IsHitTestVisible = false };
        window.Content = ring;
        var expiry = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = duration };
        expiry.Tick += (_, _) => CloseRing(window);
        _rings.Add(window); _ringTimers[window] = expiry;
        try
        {
            window.Show(); Place(window, monitor, point.X - size / 2 * monitor.ScaleX, point.Y - size / 2 * monitor.ScaleY);
            ring.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, duration));
            if (!Motion.Enabled) ring.BeginAnimation(UIElement.OpacityProperty, null);
            expiry.Start();
        }
        catch { CloseRing(window); throw; }
    }
    private void CloseRing(Window window)
    {
        if (_ringTimers.Remove(window, out var timer)) timer.Stop();
        _rings.Remove(window); window.Close();
    }
    private void ShowShortcut(string label)
    {
        if (_keys == null)
        {
            _keys = Shell(L.T("Shortcuts"), 400, 74);
            _keyText = new TextBlock { Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _keys.Content = Card(_keyText);
            _keyText.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            _keyExpiry = new DispatcherTimer(DispatcherPriority.Background, _dispatcher);
            _keyExpiry.Tick += (_, _) => HideShortcut();
        }
        bool replacing = _keys.IsVisible;
        bool changed = _keyText!.Text != label;
        _keyText!.Text = label;
        var monitor = MonitorService.GetCurrent();
        _keys.Show();
        Place(_keys, monitor, monitor.WorkingArea.Left + (monitor.WorkingArea.Width - _keys.Width * monitor.ScaleX) / 2,
            monitor.WorkingArea.Bottom - (_keys.Height + 40) * monitor.ScaleY);
        RevealShortcut(replacing && changed);
        _keyExpiry!.Stop(); _keyExpiry.Interval = TimeSpan.FromSeconds(Number(ShortcutDisplaySeconds, .3, 10, 1.6)); _keyExpiry.Start();
    }
    private void RevealShortcut(bool replacing)
    {
        _keyFadeExpiry?.Stop(); _keyFadeExpiry = null;
        _shortcutAnimationGeneration++;
        if (_keys?.Content is not FrameworkElement content) return;
        content.BeginAnimation(UIElement.OpacityProperty, null); content.Opacity = 1;
        var offset = content.RenderTransform as TranslateTransform ?? new TranslateTransform(); content.RenderTransform = offset;
        offset.BeginAnimation(TranslateTransform.YProperty, null); offset.Y = 0;
        if (!ShortcutAnimations || !Motion.Enabled) return;
        var duration = TimeSpan.FromMilliseconds(replacing ? 120 : 180);
        content.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(replacing ? .45 : 0, 1, duration) { FillBehavior = FillBehavior.Stop });
        offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(replacing ? 3 : 7, 0, duration)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
    }
    private void HideShortcut()
    {
        _keyExpiry?.Stop();
        if (_keys == null) return;
        int generation = ++_shortcutAnimationGeneration;
        void Finish()
        {
            if (generation != _shortcutAnimationGeneration) return;
            _keyFadeExpiry?.Stop(); _keyFadeExpiry = null;
            if (_keys?.Content is FrameworkElement content)
            {
                content.BeginAnimation(UIElement.OpacityProperty, null);
                if (content.RenderTransform is TranslateTransform offset) offset.BeginAnimation(TranslateTransform.YProperty, null);
            }
            _keys?.Hide(); if (_keyText != null) _keyText.Text = "";
        }
        if (!ShortcutAnimations || !Motion.Enabled || _keys.Content is not FrameworkElement card) { Finish(); return; }
        var fade = new DoubleAnimation(card.Opacity, 0, TimeSpan.FromMilliseconds(140)) { FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) => Finish();
        card.BeginAnimation(UIElement.OpacityProperty, fade);
        // Occluded WPF windows may not receive composition frames. Lifetime must
        // not depend solely on a rendered animation completing.
        _keyFadeExpiry = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromMilliseconds(160) };
        _keyFadeExpiry.Tick += (_, _) => Finish(); _keyFadeExpiry.Start();
    }
    public void ToggleTimer()
    {
        Verify();
        if (!IsTimerVisible && !IsAvailable("aid-Stopwatch")) return;
        if (_timerWindow != null) { _timerWindow.Close(); return; }
        var window = Shell(L.T("Timer"), 294, 132, true);
        window.SizeToContent = SizeToContent.Height;
        var stack = new StackPanel();
        var time = new TextBlock { Text = "00:00", FontFamily = new FontFamily("Consolas"), FontSize = 34,
            Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        time.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        var start = Ui.Button(L.T("Start"), () => { });
        void Update() { var elapsed = _stopwatch.Elapsed; time.Text = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"mm\:ss"); }
        _timerTick = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromMilliseconds(200) };
        _timerTick.Tick += (_, _) => Update();
        start.Click += (_, _) =>
        {
            if (_stopwatch.IsRunning) { _stopwatch.Stop(); _timerTick?.Stop(); start.Content = L.T("Start"); }
            else { _stopwatch.Start(); _timerTick?.Start(); start.Content = L.T("Pause"); }
            Update();
        };
        var reset = Ui.Button(L.T("Reset"), () => { _stopwatch.Reset(); _timerTick?.Stop(); start.Content = L.T("Start"); Update(); });
        var close = Ui.Button(L.T("Close"), () => window.Close());
        foreach (var button in new[] { start, reset, close }) { button.Margin = new Thickness(3, 0, 3, 0); button.Padding = new Thickness(10, 7, 10, 7); controls.Children.Add(button); }
        stack.Children.Add(time); stack.Children.Add(controls); window.Content = Card(stack);
        time.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) window.DragMove(); };
        window.Closed += (_, _) =>
        {
            _timerTick?.Stop(); _timerTick = null; _stopwatch.Reset(); _timerWindow = null; Changed?.Invoke();
        };
        _timerWindow = window;
        try
        {
            var monitor = MonitorService.GetCurrent(); window.Show();
            Place(window, monitor, monitor.WorkingArea.Right - (window.Width + 24) * monitor.ScaleX, monitor.WorkingArea.Top + 24 * monitor.ScaleY);
            // Only the interactive timer is a control; rings and shortcut labels remain capturable.
            if (!NativeWindowService.TryExcludeFromCapture(window, _excludeControls(), out string? error) && error != null) _report(error);
        }
        catch { window.Close(); throw; }
        Changed?.Invoke();
    }
    public void ToggleCountdown()
    {
        Verify();
        if (!IsCountdownVisible && !IsAvailable("aid-Countdown")) return;
        if (_countdown != null) { _countdown.Close(); return; }
        var window = new CountdownWindow { Minutes = CountdownMinutes };
        _countdown = window;
        window.Closed += (_, _) => { _countdown = null; Changed?.Invoke(); };
        ShowControl(window);
    }
    public void ToggleRuler()
    {
        Verify();
        if (!IsRulerVisible && !IsAvailable("aid-Screen ruler")) return;
        if (_ruler != null) { _ruler.Close(); return; }
        var window = new ScreenRulerWindow { Length = RulerLength };
        _ruler = window;
        window.Closed += (_, _) => { _ruler = null; Changed?.Invoke(); };
        ShowControl(window);
    }
    private void ShowControl(Window window)
    {
        try
        {
            var monitor = MonitorService.GetCurrent();
            window.Show();
            Place(window, monitor, monitor.WorkingArea.Left + 32 * monitor.ScaleX, monitor.WorkingArea.Top + 32 * monitor.ScaleY);
            if (!NativeWindowService.TryExcludeFromCapture(window, _excludeControls(), out string? error) && error != null) _report(error);
        }
        catch { window.Close(); throw; }
        Changed?.Invoke();
    }
    public void ToggleBlackout()
    {
        Verify();
        if (!IsBlackoutVisible && !IsAvailable("aid-Screen blackout")) return;
        if (IsBlackoutVisible) { StopBlackout(); Changed?.Invoke(); return; }
        try
        {
            foreach (var monitor in MonitorService.GetAll())
            {
                // Opaque black window; no transparency or dimming alpha is involved.
                var window = new Window { Title = L.T("DesktopTools Blackout"), Tag = AppCapturePrivacy.BlackoutSurface, WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = BlackoutSharingOnly ? Brushes.Transparent : Brushes.Black, ShowInTaskbar = false,
                    ShowActivated = false, Focusable = false, Topmost = true,
                    Width = monitor.Bounds.Width / monitor.ScaleX, Height = monitor.Bounds.Height / monitor.ScaleY };
                window.SourceInitialized += (_, _) =>
                {
                    NativeWindowService.ConfigureOverlay(window, true);
                    if (BlackoutSharingOnly) NativeWindowService.ApplySharingBlackout(window);
                };
                window.Closed += (_, _) => { _blackouts.Remove(window); Changed?.Invoke(); };
                _blackouts.Add(window); window.Show(); MonitorService.PlaceWindow(window, monitor);
            }
            if (BlackoutSharingOnly) _report(L.T("Warning: your screen may still be visible in screen-sharing apps. Check the viewer's output before using this mode. Press Escape to stop."));
        }
        catch { StopBlackout(); throw; }
        Changed?.Invoke();
    }
    private void StopBlackout() { foreach (var window in _blackouts.ToArray()) window.Close(); }
    private void StopClicks()
    {
        if (_mouseHook != 0) { UnhookWindowsHookEx(_mouseHook); _mouseHook = 0; }
        _clickGeneration++;
        foreach (var window in _rings.ToArray()) CloseRing(window);
    }
    private void StopKeys()
    {
        if (_keyboardHook != 0) { UnhookWindowsHookEx(_keyboardHook); _keyboardHook = 0; }
        _keyGeneration++; _shortcutAnimationGeneration++; _pendingShortcut = null;
        if (_keys?.Content is FrameworkElement content)
        {
            content.BeginAnimation(UIElement.OpacityProperty, null);
            if (content.RenderTransform is TranslateTransform offset) offset.BeginAnimation(TranslateTransform.YProperty, null);
        }
        _keyExpiry?.Stop(); _keyExpiry = null; _keyFadeExpiry?.Stop(); _keyFadeExpiry = null; _keys?.Close(); _keys = null; _keyText = null;
    }
    public void StopAll()
    {
        _dispatcher.VerifyAccess();
        // Stopping the HUD must release every window before returning, even when
        // the normal close path would animate its dismissal.
        using (DesktopTools.Presentation.WindowDismissal.Suppress())
        {
            StopClicks(); StopKeys(); _timerWindow?.Close(); _countdown?.Close(); _ruler?.Close(); StopBlackout();
        }
        Changed?.Invoke();
    }
    public void Dispose() { if (_disposed) return; StopAll(); _disposed = true; Changed = null; }
    private void Verify() { _dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this); }

    private delegate nint HookProc(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public NativeMethods.Point Point; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int kind, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? module);
}
