using System.Runtime.InteropServices;

namespace DesktopTools.Native;

public sealed partial class AudioSessionService
{
    /// <summary>Returns endpoint activity, not a guarantee that a recording contains audio.</summary>
    public double ReadPeakLevel(bool microphone)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        IMMDeviceEnumerator? devices = null; IMMDevice? device = null; object? meterObject = null;
        try
        {
            devices = CreateEnumerator();
            int result = devices.GetDefaultAudioEndpoint(microphone ? 1 : 0, 1, out device);
            if (result == unchecked((int)0x80070490)) return 0;
            Check(result);
            var iid = typeof(IAudioMeterInformation).GUID;
            Check(device.Activate(ref iid, 23, IntPtr.Zero, out meterObject));
            Check(((IAudioMeterInformation)meterObject).GetPeakValue(out float peak));
            return Math.Clamp(peak, 0, 1);
        }
        finally { Release(meterObject); Release(device); Release(devices); }
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
        [PreserveSig] int GetMeteringChannelCount(out uint count);
        [PreserveSig] int GetChannelsPeakValues(uint count, IntPtr peaks);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
    }
}
