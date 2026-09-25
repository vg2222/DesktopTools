using DesktopTools.Localization;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DesktopTools.Native;

/// <summary>Must be constructed and used on the application's dispatcher thread.</summary>
public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource source;
    private Dictionary<HotkeyGesture, int> registrations = new();
    private Dictionary<int, string> actions = new();
    private Dictionary<string, string> bindings = new();
    private bool disposed;
    public event Action<string>? Pressed;
    public IReadOnlyDictionary<string, string> RegisteredHotkeys => new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(bindings);

    public HotkeyService()
    {
        source = new HwndSource(new HwndSourceParameters("DesktopTools.Hotkeys") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) });
        source.AddHook(WindowProc);
    }

    public bool DispatchSuspended { get; set; }

    /// <summary>Register each requested shortcut independently during startup or a retry.</summary>
    public IReadOnlyDictionary<string, string> RegisterAvailable(IReadOnlyDictionary<string, string> requestedBindings)
    {
        source.Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        var unavailable = new Dictionary<string, string>();
        var next = new Dictionary<HotkeyGesture, int>();
        var nextActions = new Dictionary<int, string>();
        var nextBindings = new Dictionary<string, string>();
        foreach (var pair in requestedBindings)
        {
            if (!HotkeyGesture.TryParse(pair.Value, out var gesture, out string? error))
            {
                unavailable[pair.Key] = error ?? "Invalid shortcut";
                continue;
            }
            if (next.ContainsKey(gesture))
            {
                unavailable[pair.Key] = "Another DesktopTools action uses this shortcut.";
                continue;
            }
            int id;
            if (!registrations.TryGetValue(gesture, out id))
            {
                id = 1;
                while (registrations.ContainsValue(id) || next.ContainsValue(id)) id++;
                if (id > 0xBFFF)
                {
                    unavailable[pair.Key] = "Windows has no available hotkey IDs.";
                    continue;
                }
                if (!NativeMethods.RegisterHotKey(source.Handle, id, gesture.Modifiers | 0x4000, gesture.VirtualKey))
                {
                    unavailable[pair.Key] = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                    continue;
                }
            }
            next.Add(gesture, id);
            nextActions.Add(id, pair.Key);
            nextBindings.Add(pair.Key, pair.Value);
        }
        foreach (var pair in registrations)
            if (!next.ContainsKey(pair.Key)) NativeMethods.UnregisterHotKey(source.Handle, pair.Value);
        registrations = next;
        actions = nextActions;
        bindings = nextBindings;
        return unavailable;
    }

    public bool TryValidate(IReadOnlyDictionary<string, string> replacement, out string? error)
    {
        source.Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        error = null;
        var requested = new Dictionary<HotkeyGesture, string>();
        foreach (var pair in replacement)
        {
            if (!HotkeyGesture.TryParse(pair.Value, out var gesture, out error)) return false;
            if (!requested.TryAdd(gesture, pair.Key)) { error = L.F($"{pair.Key} and {requested[gesture]} use the same shortcut."); return false; }
        }
        var temporary = new List<int>();
        try
        {
            foreach (var pair in requested)
            {
                if (registrations.ContainsKey(pair.Key)) continue;
                int id = 1;
                while (registrations.ContainsValue(id) || temporary.Contains(id)) id++;
                if (id > 0xBFFF || !NativeMethods.RegisterHotKey(source.Handle, id, pair.Key.Modifiers | 0x4000, pair.Key.VirtualKey))
                { error = L.F($"{pair.Value}: shortcut is unavailable. {new Win32Exception(Marshal.GetLastWin32Error()).Message} Your previous shortcuts are unchanged."); return false; }
                temporary.Add(id);
            }
            return true;
        }
        finally { foreach (int id in temporary) NativeMethods.UnregisterHotKey(source.Handle, id); }
    }

    public bool TryReplace(IReadOnlyDictionary<string, string> replacement, out string? error)
    {
        source.Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        error = null;
        var requested = new Dictionary<HotkeyGesture, string>();
        foreach (var pair in replacement)
        {
            if (!HotkeyGesture.TryParse(pair.Value, out var gesture, out error)) { error = L.F($"{pair.Key}: {error}"); return false; }
            if (!requested.TryAdd(gesture, pair.Key)) { error = L.F($"{pair.Key} and {requested[gesture]} use the same shortcut."); return false; }
        }
        var additions = new Dictionary<HotkeyGesture, int>();
        foreach (var pair in requested)
        {
            if (registrations.ContainsKey(pair.Key)) continue;
            int id = 1;
            while (registrations.ContainsValue(id) || additions.ContainsValue(id)) id++;
            if (id > 0xBFFF || !NativeMethods.RegisterHotKey(source.Handle, id, pair.Key.Modifiers | 0x4000, pair.Key.VirtualKey))
            {
                int nativeError = Marshal.GetLastWin32Error();
                foreach (int addedId in additions.Values) NativeMethods.UnregisterHotKey(source.Handle, addedId);
                error = L.F($"{pair.Value}: shortcut is unavailable. {new Win32Exception(nativeError).Message} Your previous shortcuts are unchanged.");
                return false;
            }
            additions.Add(pair.Key, id);
        }
        var next = new Dictionary<HotkeyGesture, int>();
        var nextActions = new Dictionary<int, string>();
        foreach (var pair in requested)
        {
            int id = registrations.TryGetValue(pair.Key, out int existing) ? existing : additions[pair.Key];
            next.Add(pair.Key, id);
            nextActions.Add(id, pair.Value);
        }
        foreach (var pair in registrations)
            if (!next.ContainsKey(pair.Key)) NativeMethods.UnregisterHotKey(source.Handle, pair.Value);
        registrations = next;
        actions = nextActions;
        bindings = replacement.ToDictionary(p => p.Key, p => p.Value);
        return true;
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && actions.TryGetValue(wParam.ToInt32(), out string? action))
        { handled = true; if (!DispatchSuspended) Pressed?.Invoke(action); }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (disposed) return;
        source.Dispatcher.VerifyAccess();
        disposed = true;
        foreach (int id in registrations.Values) NativeMethods.UnregisterHotKey(source.Handle, id);
        registrations.Clear(); actions.Clear(); bindings.Clear();
        source.RemoveHook(WindowProc);
        source.Dispose();
    }
}
