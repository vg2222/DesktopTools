using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace DesktopTools.Native;

/// <summary>
/// Gives memory back to Windows after heavy, short-lived work (editors, text recognition, AI models) has ended.
/// Requests are debounced, so a burst of closing windows causes one compacting collection a few seconds later.
/// </summary>
public static class MemoryTrimmer
{
    private static readonly object gate = new();
    private static Timer? timer;
    internal static TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(4);
    /// <summary>Number of completed trims; used by tests and the memory probe.</summary>
    public static int Trims { get; private set; }

    /// <summary>Schedules a trim; safe to call from any thread.</summary>
    public static void Request()
    {
        lock (gate)
        {
            timer ??= new Timer(_ => TrimNow(), null, Timeout.Infinite, Timeout.Infinite);
            timer.Change(Delay, Timeout.InfiniteTimeSpan);
        }
    }

    public static void TrimNow()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            // Native allocations freed by finalizers (bitmaps, model sessions) leave pages in the working set; hand them back.
            using var process = Process.GetCurrentProcess();
            SetProcessWorkingSetSizeEx(process.Handle, -1, -1, 0);
            Trims++;
        }
        catch (Exception) { }
    }

    public static void Shutdown()
    {
        lock (gate) { timer?.Dispose(); timer = null; }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSizeEx(IntPtr process, nint minimum, nint maximum, uint flags);
}
