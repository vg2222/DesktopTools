using System.Runtime.InteropServices;

namespace DesktopTools.Native;

internal static class UserActivity
{
    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size; public uint Tick; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput input);
    internal static uint? LastInputTick()
    {
        var input = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        return GetLastInputInfo(ref input) ? input.Tick : null;
    }
    [StructLayout(LayoutKind.Sequential)] private struct FlashInfo { public uint Size; public nint Window; public uint Flags; public uint Count; public uint Timeout; }
    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FlashInfo info);
    internal static void Flash(nint window)
    {
        var info = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = window, Flags = 2 | 12, Count = uint.MaxValue };
        FlashWindowEx(ref info);
    }
}
