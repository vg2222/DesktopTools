using System.Runtime.InteropServices;
using DesktopTools.Localization;

namespace DesktopTools.Native;

public sealed partial class AudioSessionService
{
    private bool? outputSelectionAvailable;
    public bool CanSelectOutput
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (outputSelectionAvailable is bool available) return available;
            object? policy = null;
            try { policy = CreatePolicy(); return (outputSelectionAvailable = policy is IOutputPolicy).Value; }
            catch (Exception ex) when (ex is COMException or InvalidCastException) { return (outputSelectionAvailable = false).Value; }
            finally { Release(policy); }
        }
    }
    public IReadOnlyList<AudioOutputDevice> ReadOutputDevices()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        IMMDeviceEnumerator? enumerator = null; IMMDeviceCollection? collection = null;
        var outputs = new List<AudioOutputDevice>();
        try
        {
            enumerator = CreateEnumerator(); Check(enumerator.EnumAudioEndpoints(0, 1, out collection));
            Check(collection.GetCount(out uint count));
            for (uint i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                try { Check(collection.Item(i, out device)); Check(device.GetId(out string id)); outputs.Add(new(id, FriendlyName(device, false))); }
                catch (COMException) { /* A device can disappear during enumeration. */ }
                finally { Release(device); }
            }
            return outputs.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        finally { Release(collection); Release(enumerator); }
    }
    public void SelectOutput(string id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ReadOutputDevices().Any(device => device.Id == id)) throw new InvalidOperationException(L.T("Audio device disconnected. Refresh and try again."));
        object? instance = null; IMMDeviceEnumerator? enumerator = null;
        try
        {
            instance = CreatePolicy(); var policy = (IOutputPolicy)instance;
            enumerator = CreateEnumerator(); string? console = DefaultId(enumerator, 0);
            Check(policy.SetDefaultEndpoint(id, 0));
            try { Check(policy.SetDefaultEndpoint(id, 1)); }
            catch
            {
                // Undo only our first change; do not replace a concurrent external selection.
                if (console != null && DefaultId(enumerator, 0) == id) Check(policy.SetDefaultEndpoint(console, 0));
                throw;
            }
        }
        finally { Release(instance); Release(enumerator); }
    }
    private static string? DefaultId(IMMDeviceEnumerator enumerator, int role)
    {
        IMMDevice? device = null;
        try
        {
            int result = enumerator.GetDefaultAudioEndpoint(0, role, out device);
            if (result == unchecked((int)0x80070490)) return null;
            Check(result); Check(device.GetId(out string id)); return id;
        }
        finally { Release(device); }
    }
    private static object CreatePolicy() => Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"))!)!;

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    // Windows' private policy ABI, capability-probed before enabling the selector.
    // SetDefaultEndpoint is the eleventh method after IUnknown. Earlier slots are never called.
    // ABI references are documented in docs/audio-controls.md; this is not a public SDK contract.
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOutputPolicy
    {
        void Reserved01(); void Reserved02(); void Reserved03(); void Reserved04(); void Reserved05();
        void Reserved06(); void Reserved07(); void Reserved08(); void Reserved09(); void Reserved10();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }
}
