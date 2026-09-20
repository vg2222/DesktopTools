using DesktopTools.Localization;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopTools.Native;

/// <summary>Temporarily changes foreground windows' topmost state and restores changes on exit.</summary>
public sealed class WindowPinService : IDisposable
{
    private sealed record WindowIdentity(nint Handle, uint ProcessId, long StartTime);
    private sealed record Change(WindowIdentity Identity, bool OriginalTopmost);
    private readonly Dictionary<nint, Change> changes = new();
    private readonly object gate = new();
    private bool disposed;

    public bool ToggleForeground() => ToggleWindow(NativeMethods.GetForegroundWindow());

    internal bool ToggleWindow(nint handle)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (handle == 0 || !NativeMethods.IsWindow(handle) || !IsWindowVisible(handle) ||
                handle == GetDesktopWindow() || handle == GetShellWindow())
                throw new InvalidOperationException(L.T("Select a visible application window and try Pin window again."));
            var className = new StringBuilder(256);
            GetClassName(handle, className, className.Capacity);
            if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                throw new InvalidOperationException(L.T("The desktop and taskbar cannot be pinned. Select an application window."));
            WindowIdentity identity = ReadIdentity(handle);
            if (identity.ProcessId == (uint)Environment.ProcessId || identity.ProcessId <= 4)
                throw new InvalidOperationException(L.T("Select another application's window before using Pin window."));

            if (changes.TryGetValue(handle, out Change? existing) && existing.Identity != identity)
            {
                changes.Remove(handle);
                existing = null;
            }
            bool original = IsTopmost(handle);
            bool pinned = !original;
            // Record before mutation so a failed postcondition check can still be restored.
            Change change = existing ?? new Change(identity, original);
            changes[handle] = change;
            if (!Matches(identity))
            {
                changes.Remove(handle);
                throw new InvalidOperationException(L.T("The selected window closed or changed. Select the window again."));
            }
            SetTopmost(handle, pinned);
            if (pinned == change.OriginalTopmost) changes.Remove(handle);
            return pinned;
        }
    }

    public void RestoreAll()
    {
        lock (gate)
        {
            var failures = new List<Exception>();
            foreach (Change change in changes.Values.ToArray())
            {
                if (!NativeMethods.IsWindow(change.Identity.Handle))
                { changes.Remove(change.Identity.Handle); continue; }
                try
                {
                    // Never touch a replacement process that inherited an HWND or PID.
                    if (Matches(change.Identity) && IsTopmost(change.Identity.Handle) != change.OriginalTopmost)
                        SetTopmost(change.Identity.Handle, change.OriginalTopmost);
                    changes.Remove(change.Identity.Handle);
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
                { failures.Add(ex); }
            }
            if (failures.Count != 0)
                throw new AggregateException(L.T("Some windows could not be restored. Keep those applications open and try restoring again."), failures);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            try { RestoreAll(); }
            catch (AggregateException ex) { Debug.WriteLine($"DesktopTools window restoration: {ex}"); }
            disposed = true;
        }
    }

    private static WindowIdentity ReadIdentity(nint handle)
    {
        GetWindowThreadProcessId(handle, out uint pid);
        if (pid == 0) throw new InvalidOperationException(L.T("The selected window is no longer available. Select it again."));
        try
        {
            using Process process = Process.GetProcessById(checked((int)pid));
            return new WindowIdentity(handle, pid, process.StartTime.ToUniversalTime().Ticks);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
        { throw new InvalidOperationException(L.T("Windows could not identify this application safely. Select a different window and try again."), ex); }
    }

    private static bool Matches(WindowIdentity identity) => NativeMethods.IsWindow(identity.Handle) &&
        ReadIdentity(identity.Handle) == identity;

    private static bool IsTopmost(nint handle)
    {
        Marshal.SetLastPInvokeError(0);
        long style = NativeMethods.GetWindowLongPtr(handle, -20).ToInt64();
        int error = Marshal.GetLastWin32Error();
        if (style == 0 && error != 0)
            throw new Win32Exception(error, L.T("Windows could not read this window's pin state. Select the window and try again."));
        return (style & 0x8) != 0;
    }

    private static void SetTopmost(nint handle, bool topmost)
    {
        // No movement, resizing, activation, or owner-window reordering.
        const uint flags = 0x0001 | 0x0002 | 0x0010 | 0x0200;
        if (!NativeMethods.SetWindowPos(handle, topmost ? new nint(-1) : new nint(-2), 0, 0, 0, 0, flags))
            throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("Windows could not change this window's pin state. Select the application window and try again."));
        if (IsTopmost(handle) != topmost)
            throw new InvalidOperationException(L.T("The application did not retain the requested pin state. Try a different application window."));
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint handle, StringBuilder name, int count);
}
