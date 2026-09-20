using DesktopTools.Localization;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace DesktopTools.Native;

public static class NativeWindowService
{
    internal static Func<Window, bool?>? CapturePrivacyResolver { get; set; }
    public static nint GetForegroundWindowHandle() => NativeMethods.GetForegroundWindow();

    public static void ShowForeground(Window window)
    {
        DesktopTools.Presentation.WindowDismissal.Cancel(window);
        window.ShowActivated = true;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        if (!window.IsVisible) window.Show();
        window.Activate();
        var handle = new WindowInteropHelper(window).Handle;
        // Raise only this app-owned window, without changing its persistent topmost preference.
        NativeMethods.SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002);
        RestoreForeground(handle);
        window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (window.IsVisible && window.WindowState != WindowState.Minimized) window.Activate();
        }));
    }

    public static bool RestoreForeground(nint handle) => handle != IntPtr.Zero &&
        NativeMethods.IsWindow(handle) && NativeMethods.SetForegroundWindow(handle);

    public static void ConfigureOverlay(Window window, bool clickThrough)
    {
        window.ShowInTaskbar = false;
        window.Topmost = true;
        SetClickThrough(window, clickThrough);
    }

    public static void SetClickThrough(Window window, bool clickThrough)
    {
        window.Dispatcher.VerifyAccess();
        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
        Marshal.SetLastPInvokeError(0);
        long style = NativeMethods.GetWindowLongPtr(hwnd, -20).ToInt64();
        int readError = Marshal.GetLastWin32Error();
        if (style == 0 && readError != 0) throw new Win32Exception(readError);
        const long transparent = 0x20, noActivate = 0x08000000, toolWindow = 0x80;
        style |= toolWindow;
        style = clickThrough ? style | transparent | noActivate : style & ~(transparent | noActivate);
        Marshal.SetLastPInvokeError(0);
        IntPtr previous = NativeMethods.SetWindowLongPtr(hwnd, -20, new IntPtr(style));
        int writeError = Marshal.GetLastWin32Error();
        if (previous == IntPtr.Zero && writeError != 0) throw new Win32Exception(writeError);
        if (!NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020))
            throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("Could not update overlay input mode."));
        window.IsHitTestVisible = !clickThrough;
    }

    public static bool TryExcludeFromCapture(Window window, bool exclude, out string? error)
    {
        exclude = CapturePrivacyResolver?.Invoke(window) ?? exclude;
        window.Dispatcher.VerifyAccess();
        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
        return TryExcludeHandle(hwnd, exclude, out error);
    }

    // WDA_MONITOR replaces this app-owned window with blank content in supported captures.
    // Unlike WDA_EXCLUDEFROMCAPTURE, it must not reveal the windows underneath.
    public static void ApplySharingBlackout(Window window)
    {
        window.Dispatcher.VerifyAccess();
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (!NativeMethods.SetWindowDisplayAffinity(hwnd, 1))
            throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("Windows could not activate sharing blackout. Your screen may still be visible."));
    }

    /// <summary>Requests affinity for an app-owned top-level popup or tooltip handle.</summary>
    public static bool TryExcludeHandle(nint hwnd, bool exclude, out string? error)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        { error = L.T("The drawing control does not have a valid native window yet."); return false; }
        bool success = NativeMethods.SetWindowDisplayAffinity(hwnd, exclude ? 0x11u : 0u);
        error = success ? null : L.F($"Windows could not update capture exclusion: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        return success;
    }

    /// <summary>Optional decoration for opaque controls only. Never apply to an annotation canvas.</summary>
    public static void ApplyBackdrop(Window window, bool dark, bool transparency)
    {
        window.Dispatcher.VerifyAccess();
        if (window.AllowsTransparency) return;
        IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
        int darkValue = dark ? 1 : 0;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, 20, ref darkValue, sizeof(int));
        int corners = 2; // DWMWCP_ROUND; DWM still controls maximized window corners.
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, 33, ref corners, sizeof(int));
        RoundedWindowRegion.Attach(window);
        HwndTarget? target = HwndSource.FromHwnd(hwnd)?.CompositionTarget;
        bool supported = transparency && !SystemParameters.HighContrast && SystemTransparencyEnabled() &&
            OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) && target != null &&
            NativeMethods.DwmIsCompositionEnabled(out bool composing) >= 0 && composing;
        if (supported)
        {
            int acrylic = 3; // DWMSBT_TRANSIENTWINDOW: Desktop Acrylic on Windows 11.
            var glass = new NativeMethods.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            if (NativeMethods.DwmSetWindowAttribute(hwnd, 38, ref acrylic, sizeof(int)) >= 0 &&
                NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref glass) >= 0)
            {
                target!.BackgroundColor = Colors.Transparent;
                window.Background = Brushes.Transparent;
                return;
            }
            Debug.WriteLine("DesktopTools: optional system backdrop unavailable; using solid surface.");
        }
        int none = 1;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, 38, ref none, sizeof(int));
        var frame = new NativeMethods.Margins();
        _ = NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref frame);
        Color fallback = SystemParameters.HighContrast ? SystemColors.WindowColor :
            (window.TryFindResource("Surface") as SolidColorBrush)?.Color ?? (dark ? Color.FromRgb(18, 19, 21) : Color.FromRgb(242, 244, 248));
        fallback.A = 255;
        if (target != null) target.BackgroundColor = fallback;
        window.Background = new SolidColorBrush(fallback);
    }

    private static bool SystemTransparencyEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        { return false; }
    }

    public static void SynchronizeDesktop()
    {
        int result = NativeMethods.DwmFlush();
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }
}
