using System.Runtime.InteropServices;

namespace DesktopTools.Native;

/// <summary>Bounded local diagnostics. Never reads window titles, text or pixels.</summary>
internal static class WindowOrderTrace
{
    internal sealed record Entry(DateTime Time, string Caller, long Handle, uint ProcessId, long Owner,
        bool BeforeTopmost, bool AfterTopmost, long InsertAfter, uint Flags, bool Success, int Error);
    private static readonly Queue<Entry> entries = new();
    internal static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("DESKTOPTOOLS_WINDOW_TRACE") == "1";
    internal static Entry[] Snapshot() { lock (entries) return entries.ToArray(); }
    internal static bool Topmost(nint handle) => (NativeMethods.GetWindowLongPtr(handle, -20).ToInt64() & 8) != 0;
    internal static void Record(nint handle, nint after, uint flags, string caller, bool before, bool success, int error)
    {
        GetWindowThreadProcessId(handle, out uint processId);
        var entry = new Entry(DateTime.UtcNow, caller, handle.ToInt64(), processId, GetWindow(handle, 4).ToInt64(),
            before, Topmost(handle), after.ToInt64(), flags, success, error);
        lock (entries)
        {
            if (entries.Count == 256) entries.Dequeue();
            entries.Enqueue(entry);
        }
    }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint handle, uint command);
}
