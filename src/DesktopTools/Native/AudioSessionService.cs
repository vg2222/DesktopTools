using DesktopTools.Localization;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopTools.Native;

public sealed record AudioApplication(string Key, string Name, double Volume, bool Muted, int SessionCount)
{
    public uint ProcessId { get; init; }
}

/// <summary>Controls sessions on the current default multimedia render endpoint. COM objects never escape an operation.</summary>
public sealed partial class AudioSessionService : IAudioControlsService
{
    private bool disposed;
    public IReadOnlyList<AudioApplication> ReadApplications()
    {
        var sessions = new List<AudioApplication>();
        Visit((control, volume, key, name, pid) =>
        {
            Check(volume.GetMasterVolume(out var level)); Check(volume.GetMute(out var muted));
            sessions.Add(new(key, name, level * 100, muted, 1) { ProcessId = pid });
        }, tolerateEndedSessions: true);
        return sessions.GroupBy(s => s.Key).Select(g => new AudioApplication(g.Key, g.First().Name,
            g.Average(s => s.Volume), g.All(s => s.Muted), g.Count()) { ProcessId = g.First().ProcessId }).OrderBy(s => s.Name).ToArray();
    }

    public void SetVolume(string key, double percent)
    {
        if (!double.IsFinite(percent)) throw new ArgumentOutOfRangeException(nameof(percent));
        Apply(key, volume => { var context = Guid.Empty; Check(volume.SetMasterVolume((float)(Math.Clamp(percent, 0, 100) / 100), ref context)); });
    }
    public void SetMuted(string key, bool muted) => Apply(key, volume => { var context = Guid.Empty; Check(volume.SetMute(muted, ref context)); });
    private void Apply(string key, Action<ISimpleAudioVolume> action)
    {
        bool found = false;
        Visit((control, volume, sessionKey, name, _) => { if (sessionKey == key) { action(volume); found = true; } });
        if (!found) throw new InvalidOperationException(L.T("This application's audio session ended. Refresh the list."));
    }
    private void Visit(Action<IAudioSessionControl2, ISimpleAudioVolume, string, string, uint> action, bool tolerateEndedSessions = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        IMMDeviceEnumerator? devices = null; IMMDevice? device = null; object? managerObject = null; IAudioSessionEnumerator? sessions = null;
        try
        {
            devices = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            int endpointResult = devices.GetDefaultAudioEndpoint(0, 1, out device);
            // An unplugged/default-less output is an empty mixer, not a polling error.
            // Writes still fail so a stale control cannot appear to have succeeded.
            if (tolerateEndedSessions && endpointResult == unchecked((int)0x80070490)) return;
            Check(endpointResult);
            Check(device.GetId(out string deviceId));
            var iid = typeof(IAudioSessionManager2).GUID;
            Check(device.Activate(ref iid, 23, IntPtr.Zero, out managerObject));
            Check(((IAudioSessionManager2)managerObject).GetSessionEnumerator(out sessions));
            Check(sessions.GetCount(out int count));
            for (int i = 0; i < count; i++)
            {
                object? session = null;
                try
                {
                    Check(sessions.GetSession(i, out session));
                    var control = (IAudioSessionControl2)session;
                    Check(control.GetState(out int state));
                    if (state == 2) continue; // Expired; include idle sessions so paused apps can be adjusted.
                    Check(control.GetProcessId(out uint pid));
                    Check(control.GetSessionInstanceIdentifier(out string identifier));
                    bool system = control.IsSystemSoundsSession() == 0;
                    // A stale row must never write to the same process on a different output.
                    string applicationKey = system ? "system" : pid == 0 ? identifier : $"process:{pid}";
                    string key = $"{deviceId.Length}:{deviceId}:{applicationKey}";
                    string name = system ? L.T("System sounds") : ResolveName(control, pid);
                    action(control, (ISimpleAudioVolume)session, key, name, system ? 0 : pid);
                }
                catch (COMException) when (tolerateEndedSessions) { /* A process may exit during enumeration. */ }
                finally { Release(session); }
            }
        }
        finally { Release(sessions); Release(managerObject); Release(device); Release(devices); }
    }
    private static string ResolveName(IAudioSessionControl2 control, uint pid)
    {
        if (control.GetDisplayName(out string name) >= 0 && !string.IsNullOrWhiteSpace(name) && !name.StartsWith('@')) return name;
        try { using var process = Process.GetProcessById(checked((int)pid)); return process.ProcessName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or OverflowException) { return L.F($"Application ({pid})"); }
    }
    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    public void Dispose() => disposed = true;

    // Method order follows mmdeviceapi.h / audiopolicy.h. Unused slots retain their native positions.
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint mode, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr id, uint flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr id, uint flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object control);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string identifier);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string identifier);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
