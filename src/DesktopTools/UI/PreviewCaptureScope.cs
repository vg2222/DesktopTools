using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DesktopTools.Native;

namespace DesktopTools.UI;

// Exclude preview controls from capture without changing their visibility or foreground state.
internal sealed class PreviewCaptureScope : IDisposable
{
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
    private readonly List<(nint Handle, uint Affinity)> windows = [];
    internal PreviewCaptureScope()
    {
        try
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (!window.IsVisible || !AppCapturePrivacy.IsControlWindow(window)) continue;
                var handle = new WindowInteropHelper(window).Handle;
                if (!GetWindowDisplayAffinity(handle, out uint affinity))
                    throw new InvalidOperationException(Localization.L.T("Preview is unavailable while DesktopTools controls cannot be excluded from capture."));
                if (affinity == 0x11) continue;
                if (!NativeWindowService.TryExcludeHandle(handle, true, out _))
                    throw new InvalidOperationException(Localization.L.T("Preview is unavailable while DesktopTools controls cannot be excluded from capture."));
                windows.Add((handle, affinity));
            }
            NativeWindowService.SynchronizeDesktop();
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        var retry = new List<(nint Handle, uint Affinity)>();
        foreach (var item in windows)
            if (NativeMethods.IsWindow(item.Handle) && !NativeMethods.SetWindowDisplayAffinity(item.Handle, item.Affinity)) retry.Add(item);
        if (retry.Count != 0)
        {
            NativeWindowService.SynchronizeDesktop();
            foreach (var (handle, affinity) in retry)
                if (NativeMethods.IsWindow(handle)) NativeMethods.SetWindowDisplayAffinity(handle, affinity);
        }
        windows.Clear();
    }
}
