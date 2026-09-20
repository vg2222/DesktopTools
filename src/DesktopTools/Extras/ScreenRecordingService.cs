using ScreenRecorderLib;
using DesktopTools.Native;
using DesktopTools.Localization;
using System.Windows;

namespace DesktopTools.Extras;

internal sealed class ScreenRecordingService : IDisposable, IAsyncDisposable
{
    private Recorder? recorder;
    private Task? disposal;
    private readonly object recorderGate = new();
    private TaskCompletionSource<string>? completion;
    private string? destination, staging;
    private bool stopping;
    private bool userPaused, sourceSuspended;
    private readonly object clockGate = new();
    private readonly System.Diagnostics.Stopwatch elapsed = new();
    private string status = "Idle";
    internal TimeSpan Elapsed { get { lock (clockGate) return elapsed.Elapsed; } }
    internal string Status { get { lock (clockGate) return status; } }
    internal string? StopReason { get; private set; }
    internal bool SourceSuspended { get { lock (recorderGate) return sourceSuspended; } }
    private CancellationTokenSource? startupRefresh;
    internal bool Active => recorder != null || disposal is { IsCompleted: false };
    internal event Action<string>? StatusChanged;
    internal Task<string> Start(MonitorInfo monitor, Rect? region, string path, bool microphone, bool systemAudio, int framesPerSecond = 30, string quality = "Balanced", bool hardwareAcceleration = true)
    {
        var source = new DisplayRecordingSource(monitor.Id) { RecorderApi = RecorderApi.WindowsGraphicsCapture, IsCursorCaptureEnabled = true, IsBorderRequired = true };
        if (region is Rect area)
        {
            if (area.IsEmpty || area.X < 0 || area.Y < 0 || area.Width < 2 || area.Height < 2 || area.Right > monitor.Bounds.Width || area.Bottom > monitor.Bounds.Height)
                throw new ArgumentOutOfRangeException(nameof(region));
            source.SourceRect = new ScreenRect(area.X, area.Y, area.Width, area.Height);
        }
        return StartSource(source, path, microphone, systemAudio, framesPerSecond, quality, hardwareAcceleration);
    }
    internal Task<string> StartSource(RecordingSourceBase source, string path, bool microphone = false, bool systemAudio = false, int framesPerSecond = 30, string quality = "Balanced", bool hardwareAcceleration = true)
    {
        if (Active) throw new InvalidOperationException("Recording already active.");
        disposal = null;
        if (framesPerSecond is not (24 or 30 or 60 or 90 or 120 or 144)) throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        int encoderQuality = QualityValue(quality);
        ValidateAudioSources(microphone, systemAudio);
        stopping = userPaused = sourceSuspended = false;
        StopReason = null;
        lock (clockGate) { elapsed.Reset(); status = "Starting recording"; }
        destination = Path.GetFullPath(path);
        if (File.Exists(destination)) throw new IOException(L.T("Choose a new filename. Existing recordings are not overwritten."));
        staging = Path.Combine(Path.GetDirectoryName(destination)!, "." + Path.GetFileNameWithoutExtension(destination) + "." + Guid.NewGuid().ToString("N") + ".partial.mp4");
        var sources = new List<AudioSourceBase>();
        if (microphone) sources.Add(CaptureAudioSource.Default);
        if (systemAudio) sources.Add(LoopbackAudioSource.Default);
        var options = new RecorderOptions
        {
            SourceOptions = new SourceOptions { RecordingSources = new List<RecordingSourceBase> { source } },
            AudioOptions = new AudioOptions { IsAudioEnabled = sources.Count > 0, AudioSources = sources },
            // A static desktop may not deliver another capture frame for seconds.
            // Keep media time advancing so a still screen produces a usable clip.
            VideoEncoderOptions = new VideoEncoderOptions { Framerate = framesPerSecond, Quality = encoderQuality, IsFixedFramerate = true, IsHardwareEncodingEnabled = hardwareAcceleration, Encoder = new H264VideoEncoder { BitrateMode = H264BitrateControlMode.Quality }, IsFragmentedMp4Enabled = false },
            OutputOptions = new OutputOptions { RecorderMode = RecorderMode.Video }
        };
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var currentCompletion = completion; string currentStaging = staging, currentDestination = destination;
        RecordingWindowInfo? refreshSource = source is WindowRecordingSource selectedWindow ? RecordingWindows.Identify(selectedWindow.Handle) : null;
        recorder = Recorder.CreateRecorder(options);
        recorder.OnRecordingComplete += (_, _) =>
        {
            StopStartupRefresh();
            if (!stopping && refreshSource != null && !RecordingWindows.IsSameWindow(refreshSource))
                StopReason = "Source window closed. Recording stopped.";
            lock (clockGate) elapsed.Stop();
            _ = FinalizeAsync(currentStaging, currentDestination, currentCompletion);
        };
        recorder.OnRecordingFailed += (_, e) => { StopStartupRefresh(); lock (clockGate) elapsed.Stop(); currentCompletion.TrySetException(new IOException(e.Error + "\n" + L.T("Temporary recording: ") + currentStaging)); };
        recorder.OnStatusChanged += (_, e) =>
        {
            string next = e.Status.ToString();
            lock (clockGate) { status = next; if (next == "Recording" && !stopping) elapsed.Start(); else elapsed.Stop(); }
            StatusChanged?.Invoke(next);
        };
        try
        {
            CancellationToken refreshToken = default;
            if (refreshSource != null)
            {
                startupRefresh = new CancellationTokenSource();
                refreshToken = startupRefresh.Token;
            }
            recorder.Record(staging);
            if (refreshSource != null && !completion.Task.IsCompleted) _ = WatchWindowAsync(refreshSource, refreshToken);
            return completion.Task;
        }
        catch
        {
            // Native teardown can wait for capture callbacks. Keep the caller's UI
            // pumping even when Record throws before returning the completion task.
            _ = DisposeAsync().AsTask().ContinueWith(task => System.Diagnostics.Debug.WriteLine(task.Exception),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }
    internal static void ValidateAudioSources(bool microphone, bool systemAudio)
    {
        if (!microphone && !systemAudio) return;
        using var devices = new AudioSessionService();
        if (microphone && devices.ReadEndpoint(true) == null)
            throw new InvalidOperationException(L.T("No microphone is available. Connect an input device or turn off microphone recording."));
        if (systemAudio && devices.ReadEndpoint(false) == null)
            throw new InvalidOperationException(L.T("No audio output is available. Connect an output device or turn off system audio recording."));
    }
    internal void Pause() { lock (recorderGate) { userPaused = true; if (!stopping && !sourceSuspended) recorder?.Pause(); } }
    internal static int QualityValue(string quality) => quality switch { "Economy" => 50, "Balanced" => 70, "High" => 90, _ => throw new ArgumentOutOfRangeException(nameof(quality)) };
    internal void Resume() { lock (recorderGate) { userPaused = false; if (!stopping && !sourceSuspended) recorder?.Resume(); } }
    internal void Stop(string? reason = null)
    {
        StopStartupRefresh();
        lock (recorderGate)
        {
            if (recorder == null || stopping) return;
            StopReason = reason;
            stopping = true;
            lock (clockGate) elapsed.Stop();
            try { recorder.Stop(); }
            catch (Exception ex) { completion?.TrySetException(ex); }
        }
    }
    private static async Task FinalizeAsync(string staging, string destination, TaskCompletionSource<string> completion)
    {
        try { await FinalizeFileAsync(staging, destination); completion.TrySetResult(destination); }
        catch (Exception ex) { completion.TrySetException(new IOException(L.T("Recording could not be finalized. Temporary file: ") + staging, ex)); }
    }
    internal static async Task FinalizeFileAsync(string staging, string destination)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(staging, destination, false); return; }
            catch (IOException ex) when (attempt < 20 && (ex.HResult & 0xffff) is 32 or 33) { await Task.Delay(50).ConfigureAwait(false); }
        }
    }
    private async Task WatchWindowAsync(RecordingWindowInfo source, CancellationToken token)
    {
        // ScreenRecorderLib 7.0.1 discards the first window frame when sizing its pool.
        // A static GDI window may produce no second frame. Request ordinary asynchronous
        // paints during startup only; preserve focus, position, visibility and contents.
        try
        {
            int refreshes = 0;
            while (true)
            {
                await Task.Delay(100, token).ConfigureAwait(false);
                if (!RecordingWindows.IsSameWindow(source)) { Stop("Source window closed. Recording stopped."); return; }
                bool unavailable = !RecordingWindows.Available(source);
                lock (recorderGate)
                {
                    if (!stopping && recorder != null && unavailable != sourceSuspended)
                    {
                        sourceSuspended = unavailable;
                        if (!userPaused) { if (unavailable) recorder.Pause(); else recorder.Resume(); }
                        StatusChanged?.Invoke(unavailable ? "Source window is minimized or hidden. Recording is paused." : userPaused ? "Paused" : "Recording");
                        if (!unavailable) refreshes = 0;
                    }
                }
                if (refreshes++ < 15) RecordingWindows.RequestFrame(source);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { completion?.TrySetException(ex); Stop(); }
    }
    private void StopStartupRefresh() { var refresh = Interlocked.Exchange(ref startupRefresh, null); if (refresh != null) { refresh.Cancel(); refresh.Dispose(); } }
    public ValueTask DisposeAsync()
    {
        StopStartupRefresh();
        lock (recorderGate)
        {
            if (disposal != null) return new ValueTask(disposal);
            var current = recorder; recorder = null; stopping = true;
            lock (clockGate) elapsed.Stop();
            // Do not hold recorderGate during native joins: source callbacks may need
            // that gate, while WGC teardown may also require a pumping window thread.
            disposal = current == null ? Task.CompletedTask : Task.Run(current.Dispose);
            return new ValueTask(disposal);
        }
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
