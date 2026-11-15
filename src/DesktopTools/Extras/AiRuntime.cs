using System.IO;
using System.Security.Cryptography;
using DesktopTools.Localization;
using Microsoft.ML.OnnxRuntime;

namespace DesktopTools.Extras;

/// <summary>
/// Owns the ONNX Runtime session for optional models. One model is loaded at a time and calls are serialized. A session is kept for a
/// short idle period so repeated work does not reload it, then released by a low-frequency timer; Quit releases it at once.
/// Models run on the processor, or on the graphics card through Windows' DirectML when chosen and available; a session that cannot be
/// created on the graphics card is created on the processor instead.
/// </summary>
internal static class AiRuntime
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    private static readonly object timerLock = new();
    private static InferenceSession[]? sessions;
    private static string[] loadedPaths = [];
    private static DateTime lastUse;
    private static Timer? idleTimer;
    private static volatile bool releaseRequested;

    internal static TimeSpan IdleRelease { get; set; } = TimeSpan.FromSeconds(60);
    internal static Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;
    private static string preferredDevice = AiGpu.Processor;
    /// <summary>The device chosen in settings ("Processor" or "GraphicsCard"). Changing it releases loaded sessions so the next run uses it.</summary>
    internal static string PreferredDevice
    {
        get => preferredDevice;
        set
        {
            string device = value == AiGpu.GraphicsCard ? AiGpu.GraphicsCard : AiGpu.Processor;
            if (device == preferredDevice) return;
            preferredDevice = device; GraphicsCardFailed = false; ReleaseAll();
        }
    }
    /// <summary>The device the loaded sessions run on, or the one the next load will use.</summary>
    internal static string Device { get; private set; } = AiGpu.Processor;
    /// <summary>True after a session could not be created on the graphics card and the processor was used instead.</summary>
    internal static bool GraphicsCardFailed { get; private set; }
    /// <summary>Called once when the graphics card could not be used and models fell back to the processor.</summary>
    internal static Action<string>? GraphicsCardFallback { get; set; }
    /// <summary>The first loaded model file, or null.</summary>
    internal static string? LoadedModelPath => loadedPaths.Length > 0 ? loadedPaths[0] : null;
    /// <summary>Every model file of the loaded set (one file for most features; the OCR pack loads its detector and a recognizer together).</summary>
    internal static IReadOnlyList<string> LoadedModelPaths => loadedPaths;

    /// <summary>Loads (or reuses) the session for this file after verifying its SHA-256. One model is loaded at a time; callers are serialized.</summary>
    internal static Task<T> RunAsync<T>(string modelPath, string sha256, Func<InferenceSession, T> use, CancellationToken token) =>
        RunAsync([(modelPath, sha256)], loaded => use(loaded[0]), token);

    /// <summary>
    /// Loads (or reuses) one session per file, as one unit, after verifying every SHA-256. One set is loaded at a time and replaces any other;
    /// callers are serialized. A file that fails its check throws <see cref="InvalidDataException"/> whose <see cref="DamagedPath"/> names it, and nothing stays loaded.
    /// </summary>
    internal static Task<T> RunAsync<T>(IReadOnlyList<(string Path, string Sha256)> models, Func<IReadOnlyList<InferenceSession>, T> use, CancellationToken token) =>
        RunAsync(models, use, token, prepare: null);

    /// <summary>
    /// As above; each file whose index has a step in <paramref name="prepare"/> is read once, its SHA-256 checked on those bytes, and its session
    /// is created from the bytes that step returns. Used to drop unneeded outputs of a model in memory; the file on disk is never changed.
    /// Other files are verified and loaded from disk as usual, so a large file is not held in memory twice.
    /// <paramref name="lowMemory"/> turns off ONNX Runtime's memory arena and memory pattern for a newly loaded set, so a large model returns
    /// its working memory after each run instead of keeping its peak. With <paramref name="wait"/>, a caller that cannot get its turn within
    /// that time (another model is busy, for example a long translation) gets <see cref="AiRuntimeBusyException"/> instead of waiting.
    /// </summary>
    internal static async Task<T> RunAsync<T>(IReadOnlyList<(string Path, string Sha256)> models, Func<IReadOnlyList<InferenceSession>, T> use, CancellationToken token,
        IReadOnlyDictionary<int, Func<byte[], byte[]>>? prepare, bool lowMemory = false, TimeSpan? wait = null)
    {
        if (models.Count == 0) throw new ArgumentException("No model files given.", nameof(models));
        string[] paths = models.Select(model => Path.GetFullPath(model.Path)).ToArray();
        if (wait is { } limit) { if (!await gate.WaitAsync(limit, token)) throw new AiRuntimeBusyException(); }
        else await gate.WaitAsync(token);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if (sessions == null || !paths.SequenceEqual(loadedPaths, StringComparer.OrdinalIgnoreCase))
                {
                    ReleaseLocked();
                    for (int i = 0; i < paths.Length; i++)
                        if (prepare == null || !prepare.ContainsKey(i))
                            try { Verify(paths[i], models[i].Sha256, token); }
                            catch (Exception e) when (e is not OperationCanceledException) { NoteFailedPath(e, paths[i]); throw; }
                    bool gpu = preferredDevice == AiGpu.GraphicsCard && !GraphicsCardFailed && models.Any(model => GpuVerified(model.Sha256)) && AiGpu.EnsureLoaded();
                    InferenceSession[] loaded;
                    try { loaded = CreateSessions(paths, models, prepare, lowMemory, gpu, token); }
                    catch (Exception e) when (gpu && e is not OperationCanceledException && e is not InvalidDataException && e is not FileNotFoundException)
                    {
                        // The graphics card could not take this model (driver, memory or an unsupported operator): use the processor from now on.
                        GraphicsCardFailed = true; gpu = false;
                        GraphicsCardFallback?.Invoke(e.Message);
                        loaded = CreateSessions(paths, models, prepare, lowMemory, gpu: false, token);
                    }
                    sessions = loaded; loadedPaths = paths; Device = gpu ? AiGpu.GraphicsCard : AiGpu.Processor;
                    StartTimer();
                }
                lastUse = Clock();
                try { return use(sessions); }
                catch (OnnxRuntimeException e) when (Device == AiGpu.GraphicsCard)
                {
                    // A model that loaded on the graphics card but failed while running is run again on the processor.
                    GraphicsCardFailed = true; GraphicsCardFallback?.Invoke(e.Message);
                    ReleaseLocked();
                    sessions = CreateSessions(paths, models, prepare, lowMemory, gpu: false, token); loadedPaths = paths; Device = AiGpu.Processor;
                    StartTimer();
                    return use(sessions);
                }
                finally { lastUse = Clock(); }
            }, token);
        }
        finally
        {
            if (releaseRequested) ReleaseLocked();
            gate.Release();
        }
    }

    /// <summary>
    /// Model files measured to give the same results on the graphics card and to run faster there (docs/ai-models.md, task 6): the OCR text finder
    /// and the speech encoders. Other models stay on the processor even when the graphics card is chosen: the translation model crashed the
    /// process on DirectML, the speech decoder produced wrong text without an error, and the OCR recognizer was slower.
    /// </summary>
    private static readonly HashSet<string> GpuVerifiedModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "a431985659dc921974177a95adcfbb90fd9e51989a5e04d70d0b75f597b6e61d", // ocr-text-detector inference.onnx
        "f0bd7927234639c6e1f293cef18a210cee4e4aea93e200ebbe48e1d7acf6fdb1", // speech-fast encoder_model.onnx
        "31a05a14d514440e43746fdaaa8d4e8102c9543e53c5ae1111910af142041406", // speech-accurate encoder_model.onnx
    };
    internal static bool GpuVerified(string sha256) => GpuVerifiedModels.Contains(sha256);

    private static InferenceSession[] CreateSessions(string[] paths, IReadOnlyList<(string Path, string Sha256)> models,
        IReadOnlyDictionary<int, Func<byte[], byte[]>>? prepare, bool lowMemory, bool gpu, CancellationToken token)
    {
        var created = new List<InferenceSession>();
        try
        {
            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                using var options = Options(lowMemory, gpu && GpuVerified(models[i].Sha256));
                try
                {
                    if (prepare != null && prepare.TryGetValue(i, out var step))
                        created.Add(new InferenceSession(step(ReadVerified(path, models[i].Sha256, token)), options));
                    else created.Add(new InferenceSession(path, options));
                }
                catch (Exception e) when (e is not OperationCanceledException) { NoteFailedPath(e, path); throw; }
                token.ThrowIfCancellationRequested();
            }
            return created.ToArray();
        }
        catch { foreach (var item in created) item.Dispose(); throw; }
    }

    private const string DamagedPathKey = "DesktopTools.ModelPath";
    /// <summary>The full path of the model file that failed its SHA-256 check, when <paramref name="error"/> is that failure.</summary>
    internal static string? DamagedPath(Exception error) => error is InvalidDataException && error.Data[DamagedPathKey] is string path ? path : null;
    private const string FailedPathKey = "DesktopTools.FailedModelPath";
    /// <summary>The full path of the model file whose check or loading threw <paramref name="error"/>; null for errors while a loaded model runs.</summary>
    internal static string? FailedPath(Exception error) => error.Data[FailedPathKey] as string;
    private static void NoteFailedPath(Exception error, string path) { try { error.Data[FailedPathKey] = path; } catch (Exception) { } }

    /// <summary>Releases the session when it has not been used for <see cref="IdleRelease"/>. Never waits for a running call.</summary>
    internal static void ReleaseIdle()
    {
        if (!gate.Wait(0)) return;
        try { if (sessions != null && Clock() - lastUse >= IdleRelease) ReleaseLocked(); }
        finally { gate.Release(); }
    }

    /// <summary>Releases the session now, or as soon as a running call finishes.</summary>
    internal static void ReleaseAll()
    {
        if (gate.Wait(TimeSpan.FromSeconds(2)))
        {
            try { ReleaseLocked(); }
            finally { gate.Release(); }
        }
        else releaseRequested = true;
    }

    private static void ReleaseLocked()
    {
        releaseRequested = false;
        var old = sessions; sessions = null; loadedPaths = [];
        if (old != null) { foreach (var item in old) item.Dispose(); DesktopTools.Native.MemoryTrimmer.Request(); }
        lock (timerLock) { idleTimer?.Dispose(); idleTimer = null; }
    }

    private static void StartTimer()
    {
        lock (timerLock)
        {
            if (idleTimer != null) return;
            var period = TimeSpan.FromSeconds(Math.Clamp(IdleRelease.TotalSeconds / 4, 5, 30));
            idleTimer = new Timer(_ => ReleaseIdle(), null, period, period);
        }
    }

    private static void Verify(string path, string sha256, CancellationToken token)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(L.T("A model file is missing. Download the model again on the AI models page."), path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1 << 20]; int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
        if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.T("A model file is damaged. Download the model again on the AI models page.")) { Data = { [DamagedPathKey] = path } };
    }

    private static byte[] ReadVerified(string path, string sha256, CancellationToken token)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(L.T("A model file is missing. Download the model again on the AI models page."), path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        if (stream.Length > int.MaxValue) throw new InvalidDataException("The model file is too large to load.");
        var bytes = new byte[stream.Length];
        int offset = 0, read;
        while (offset < bytes.Length && (read = stream.Read(bytes, offset, Math.Min(1 << 20, bytes.Length - offset))) > 0) { token.ThrowIfCancellationRequested(); offset += read; }
        if (offset != bytes.Length || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.T("A model file is damaged. Download the model again on the AI models page.")) { Data = { [DamagedPathKey] = path } };
        return bytes;
    }

    private static SessionOptions Options(bool lowMemory, bool gpu = false)
    {
        var options = new SessionOptions { IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4), InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
        if (lowMemory) { options.EnableCpuMemArena = false; options.EnableMemoryPattern = false; }
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        if (gpu)
        {
            // DirectML needs sequential execution without memory patterns; operators it lacks still run on the processor.
            options.EnableMemoryPattern = false;
            try { options.AppendExecutionProvider_DML(0); }
            catch (Exception) { options.Dispose(); throw; }
        }
        return options;
    }
}

/// <summary>Another model held the runtime longer than the caller was willing to wait.</summary>
internal sealed class AiRuntimeBusyException() : InvalidOperationException("Another model is busy.");
