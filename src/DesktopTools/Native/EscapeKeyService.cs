using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DesktopTools.Native;

/// <summary>Escape belongs to the active tool only, even when its canvas is click-through.</summary>
internal sealed class EscapeKeyService : IDisposable
{
    private readonly HwndSource source = new(new HwndSourceParameters("DesktopTools.Escape") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 0, Height = 0 });
    private bool enabled;
    public event Action? Pressed;
    public EscapeKeyService() => source.AddHook(Proc);
    public bool SetEnabled(bool value)
    {
        if (value == enabled) return true;
        if (value && !NativeMethods.RegisterHotKey(source.Handle, 1, 0x4000, 0x1B)) return false;
        if (!value) NativeMethods.UnregisterHotKey(source.Handle, 1);
        enabled = value; return true;
    }
    private IntPtr Proc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x312) { handled = true; Pressed?.Invoke(); }
        return IntPtr.Zero;
    }
    public void Dispose() { SetEnabled(false); source.RemoveHook(Proc); source.Dispose(); }
}
