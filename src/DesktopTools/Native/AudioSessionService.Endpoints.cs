using System.Runtime.InteropServices;
using DesktopTools.Localization;

namespace DesktopTools.Native;

public sealed record AudioEndpoint(string Id, string Name, double Volume, bool Muted);
public sealed record AudioOutputDevice(string Id, string Name)
{
    public override string ToString() => Name;
}
public interface IAudioControlsService : IDisposable
{
    IReadOnlyList<AudioApplication> ReadApplications();
    AudioEndpoint? ReadEndpoint(bool microphone);
    IReadOnlyList<AudioOutputDevice> ReadOutputDevices();
    bool CanSelectOutput { get; }
    void SelectOutput(string id);
    void SetVolume(string key, double percent);
    void SetMuted(string key, bool muted);
    void SetEndpointVolume(string id, double percent);
    void SetEndpointMuted(string id, bool muted);
}

public sealed partial class AudioSessionService
{
    public AudioEndpoint? ReadEndpoint(bool microphone)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        IMMDeviceEnumerator? devices = null; IMMDevice? device = null; object? volumeObject = null;
        try
        {
            devices = CreateEnumerator();
            int result = devices.GetDefaultAudioEndpoint(microphone ? 1 : 0, 1, out device);
            if (result == unchecked((int)0x80070490)) return null; // No active default endpoint.
            Check(result); Check(device.GetId(out string id));
            var iid = typeof(IAudioEndpointVolume).GUID;
            Check(device.Activate(ref iid, 23, IntPtr.Zero, out volumeObject));
            var volume = (IAudioEndpointVolume)volumeObject;
            Check(volume.GetMasterVolumeLevelScalar(out float level)); Check(volume.GetMute(out bool muted));
            return new(id, FriendlyName(device, microphone), level * 100, muted);
        }
        finally { Release(volumeObject); Release(device); Release(devices); }
    }
    public void SetEndpointVolume(string id, double percent)
    {
        if (!double.IsFinite(percent)) throw new ArgumentOutOfRangeException(nameof(percent));
        WithEndpoint(id, volume => { var context = Guid.Empty; Check(volume.SetMasterVolumeLevelScalar((float)(Math.Clamp(percent, 0, 100) / 100), ref context)); });
    }
    public void SetEndpointMuted(string id, bool muted) => WithEndpoint(id, volume => { var context = Guid.Empty; Check(volume.SetMute(muted, ref context)); });
    private void WithEndpoint(string id, Action<IAudioEndpointVolume> action)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        IMMDeviceEnumerator? devices = null; IMMDevice? device = null; object? volumeObject = null;
        try
        {
            devices = CreateEnumerator(); Check(devices.GetDevice(id, out device)); Check(device.GetState(out uint state));
            if (state != 1) throw new InvalidOperationException(L.T("Audio device disconnected. Refresh and try again."));
            var iid = typeof(IAudioEndpointVolume).GUID; Check(device.Activate(ref iid, 23, IntPtr.Zero, out volumeObject));
            action((IAudioEndpointVolume)volumeObject);
        }
        finally { Release(volumeObject); Release(device); Release(devices); }
    }
    private static IMMDeviceEnumerator CreateEnumerator() => (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
    private static string FriendlyName(IMMDevice device, bool microphone)
    {
        IPropertyStore? properties = null;
        try
        {
            Check(device.OpenPropertyStore(0, out properties));
            var key = new PropertyKey { Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
            Check(properties.GetValue(ref key, out var value));
            try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "" : L.T(microphone ? "Microphone" : "Output device"); }
            finally { PropVariantClear(ref value); }
        }
        finally { Release(properties); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropertyValue { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropertyValue value);
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropertyValue value);
    }
    // Vtable order verified against Windows SDK 10.0.26100.0 endpointvolume.h.
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
