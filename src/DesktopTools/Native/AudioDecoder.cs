using System.Runtime.InteropServices;

namespace DesktopTools.Native;

/// <summary>The file has no audio stream that Windows can decode.</summary>
internal sealed class NoAudioTrackException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Streams the first audio track of a media file as 16 kHz mono float samples through the Windows Media Foundation source reader, which
/// decodes and resamples. Memory stays bounded: one decoded sample is held at a time. Reads stop after <see cref="MaxDuration"/>.
/// </summary>
internal sealed class AudioDecoder : IDisposable
{
    internal const int SampleRate = 16000;
    /// <summary>The video editor's input cap.</summary>
    internal static readonly TimeSpan MaxDuration = TimeSpan.FromHours(6);
    private const long MaxSamples = 6L * 3600 * SampleRate;

    [DllImport("mfplat.dll")] private static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll")] private static extern int MFShutdown();
    [DllImport("mfplat.dll")] private static extern int MFCreateMediaType(out IntPtr type);
    [DllImport("mfplat.dll")] private static extern int MFCreateAttributes(out IntPtr attributes, uint size);
    [DllImport("mfreadwrite.dll")] private static extern int MFCreateSourceReaderFromURL([MarshalAs(UnmanagedType.LPWStr)] string url, IntPtr attributes, out IntPtr reader);

    private static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid AudioMajorType = new("73647561-0000-0010-8000-00AA00389B71"), FloatSubtype = new("00000003-0000-0010-8000-00AA00389B71");
    private static readonly Guid Channels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a"), SamplesPerSecond = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    private static readonly Guid BitsPerSample = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669"), BlockAlignment = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    private static readonly Guid AverageBytesPerSecond = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    // Lets the source reader insert the audio resampler for the requested rate and channel count.
    private static readonly Guid AdvancedVideoProcessing = new("0f81da2c-b537-4672-a8b2-a681b17307a3");
    private static readonly Guid DurationKey = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a");
    private const uint FirstAudioStream = 0xFFFFFFFD, AllStreams = 0xFFFFFFFE, MediaSource = 0xFFFFFFFF, EndOfStream = 2;
    private const uint MfVersion = 0x20070;

    // COM vtable slots used below (IUnknown 0-2; IMFAttributes::SetUINT32 21, SetGUID 24; IMFSourceReader 3-12; IMFSample::ConvertToContiguousBuffer 41; IMFMediaBuffer Lock 3, Unlock 4).
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReleaseFn(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetUInt32Fn(IntPtr self, ref Guid key, uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetGuidFn(IntPtr self, ref Guid key, ref Guid value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetStreamSelectionFn(IntPtr self, uint stream, int selected);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetCurrentMediaTypeFn(IntPtr self, uint stream, IntPtr reserved, IntPtr type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ReadSampleFn(IntPtr self, uint stream, uint control, out uint actualStream, out uint flags, out long timestamp, out IntPtr sample);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetPresentationAttributeFn(IntPtr self, uint stream, ref Guid key, IntPtr variant);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ConvertToContiguousBufferFn(IntPtr self, out IntPtr buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int LockFn(IntPtr self, out IntPtr data, out uint maxLength, out uint currentLength);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int UnlockFn(IntPtr self);

    private static T Method<T>(IntPtr self, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self), slot * IntPtr.Size));
    private static void Check(int hr, string what) { if (hr < 0) throw new COMException(what, hr); }
    private static void Release(ref IntPtr pointer) { if (pointer != IntPtr.Zero) { Method<ReleaseFn>(pointer, 2)(pointer); pointer = IntPtr.Zero; } }

    private IntPtr reader;
    private readonly ReadSampleFn readSample;
    private float[] pending = [];
    private int pendingOffset, pendingCount;
    private long delivered;
    // Samples placed on the timeline so far (decoded plus inserted silence), and silence still to deliver before the pending samples.
    private long placed, silence;
    private bool ended, started, disposed;

    /// <summary>The container's stated length, or zero when it does not say.</summary>
    internal TimeSpan Duration { get; }

    private AudioDecoder(IntPtr reader, TimeSpan duration)
    {
        this.reader = reader; Duration = duration; started = true;
        readSample = Method<ReadSampleFn>(reader, 9);
    }

    internal static AudioDecoder Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException("The media file could not be found.", full);
        Check(MFStartup(MfVersion, 0), "MFStartup");
        IntPtr attributes = 0, reader = 0, type = 0;
        bool handedOver = false;
        try
        {
            Check(MFCreateAttributes(out attributes, 1), "MFCreateAttributes");
            var key = AdvancedVideoProcessing;
            Check(Method<SetUInt32Fn>(attributes, 21)(attributes, ref key, 1), "SetUINT32");
            int hr = MFCreateSourceReaderFromURL(full, attributes, out reader);
            if (hr < 0) throw new NoAudioTrackException("Windows could not read this file.", new COMException("MFCreateSourceReaderFromURL", hr));
            var select = Method<SetStreamSelectionFn>(reader, 4);
            Check(select(reader, AllStreams, 0), "SetStreamSelection");
            if (select(reader, FirstAudioStream, 1) < 0) throw new NoAudioTrackException("The file has no audio track.");
            TimeSpan duration = TimeSpan.Zero;
            IntPtr variant = Marshal.AllocCoTaskMem(24);
            try
            {
                for (int i = 0; i < 24; i++) Marshal.WriteByte(variant, i, 0);
                var durationKey = DurationKey;
                if (Method<GetPresentationAttributeFn>(reader, 12)(reader, MediaSource, ref durationKey, variant) >= 0)
                    duration = TimeSpan.FromTicks(Math.Max(0, Marshal.ReadInt64(variant, 8)));
            }
            finally { Marshal.FreeCoTaskMem(variant); }
            Check(MFCreateMediaType(out type), "MFCreateMediaType");
            var setGuid = Method<SetGuidFn>(type, 24); var setUInt = Method<SetUInt32Fn>(type, 21);
            Guid major = MajorType, audio = AudioMajorType, subtype = Subtype, floatFormat = FloatSubtype;
            Check(setGuid(type, ref major, ref audio), "SetGUID major");
            Check(setGuid(type, ref subtype, ref floatFormat), "SetGUID subtype");
            foreach (var (name, value) in new[] { (Channels, 1u), (SamplesPerSecond, (uint)SampleRate), (BitsPerSample, 32u), (BlockAlignment, 4u), (AverageBytesPerSecond, (uint)SampleRate * 4) })
            { var item = name; Check(setUInt(type, ref item, value), "SetUINT32"); }
            hr = Method<SetCurrentMediaTypeFn>(reader, 7)(reader, FirstAudioStream, IntPtr.Zero, type);
            if (hr < 0) throw new NoAudioTrackException("Windows cannot decode this file's audio.", new COMException("SetCurrentMediaType", hr));
            var decoder = new AudioDecoder(reader, duration);
            handedOver = true;
            return decoder;
        }
        finally
        {
            Release(ref type); Release(ref attributes);
            if (!handedOver) { Release(ref reader); MFShutdown(); }
        }
    }

    /// <summary>Copies the next decoded samples into <paramref name="destination"/>. Returns 0 at the end of the audio.</summary>
    internal int Read(Span<float> destination)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        int written = 0;
        while (written < destination.Length)
        {
            if (silence > 0)
            {
                int quiet = (int)Math.Min(Math.Min(silence, destination.Length - written), MaxSamples - delivered);
                if (quiet <= 0) { ended = true; silence = 0; pendingCount = 0; break; }
                destination.Slice(written, quiet).Clear();
                silence -= quiet; written += quiet; delivered += quiet;
                continue;
            }
            if (pendingCount == 0 && !Fill()) break;
            int count = (int)Math.Min(Math.Min(pendingCount, destination.Length - written), MaxSamples - delivered);
            if (count <= 0) { ended = true; pendingCount = 0; break; }
            pending.AsSpan(pendingOffset, count).CopyTo(destination[written..]);
            pendingOffset += count; pendingCount -= count; written += count; delivered += count;
        }
        return written;
    }

    private bool Fill()
    {
        while (!ended)
        {
            Check(readSample(reader, FirstAudioStream, 0, out _, out uint flags, out long timestamp, out IntPtr sample), "ReadSample");
            try
            {
                if (sample != IntPtr.Zero && CopySample(sample))
                {
                    // Audio that starts late or has a gap keeps its place in time: the gap becomes silence instead of pulling later speech earlier.
                    long expected = timestamp > 0 ? timestamp / (10_000_000 / SampleRate) : placed;
                    if (expected - placed > SampleRate / 50) { silence = expected - placed; placed = expected; }
                    placed += pendingCount;
                    return true;
                }
            }
            finally { Release(ref sample); }
            if ((flags & EndOfStream) != 0) ended = true;
        }
        return false;
    }

    private bool CopySample(IntPtr sample)
    {
        IntPtr buffer = 0;
        Check(Method<ConvertToContiguousBufferFn>(sample, 41)(sample, out buffer), "ConvertToContiguousBuffer");
        try
        {
            Check(Method<LockFn>(buffer, 3)(buffer, out IntPtr data, out _, out uint length), "Lock");
            try
            {
                int count = (int)(length / 4);
                if (count == 0) return false;
                if (pending.Length < count) pending = new float[count];
                Marshal.Copy(data, pending, 0, count);
                pendingOffset = 0; pendingCount = count;
                return true;
            }
            finally { Method<UnlockFn>(buffer, 4)(buffer); }
        }
        finally { Release(ref buffer); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Release(ref reader);
        if (started) { started = false; MFShutdown(); }
        pending = [];
    }
}
