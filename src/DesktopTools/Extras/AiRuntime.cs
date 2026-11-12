using System.IO;
using System.Security.Cryptography;
using DesktopTools.Localization;
using Microsoft.ML.OnnxRuntime;

namespace DesktopTools.Extras;

/// <summary>
/// Owns the ONNX Runtime session for optional models. One model is loaded at a time and calls are serialized. A session is kept for a
/// short idle period so repeated work does not reload it, then released by a low-frequency timer; Quit releases it at once.
/// This update runs on the processor only (spec section 10).
/// </summary>
internal static class AiRuntime
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    private static readonly object timerLock = new();
    private static InferenceSession? session;
    private static string? loadedPath;
    private static DateTime lastUse;
    private static Timer? idleTimer;
    private static volatile bool releaseRequested;

    internal static TimeSpan IdleRelease { get; set; } = TimeSpan.FromSeconds(60);
    internal static Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;
    /// <summary>The device sessions are created on. The AiDevice setting has one value, "Processor", in this update.</summary>
    internal static string Device => "Processor";
    internal static string? LoadedModelPath => loadedPath;

    /// <summary>Loads (or reuses) the session for this file after verifying its SHA-256. One model is loaded at a time; callers are serialized.</summary>
    internal static async Task<T> RunAsync<T>(string modelPath, string sha256, Func<InferenceSession, T> use, CancellationToken token)
    {
        string path = Path.GetFullPath(modelPath);
        await gate.WaitAsync(token);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if (session == null || !string.Equals(loadedPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    ReleaseLocked();
                    Verify(path, sha256, token);
                    using var options = Options();
                    var created = new InferenceSession(path, options);
                    if (token.IsCancellationRequested) { created.Dispose(); token.ThrowIfCancellationRequested(); }
                    session = created; loadedPath = path;
                    StartTimer();
                }
                lastUse = Clock();
                try { return use(session); }
                finally { lastUse = Clock(); }
            }, token);
        }
        finally
        {
            if (releaseRequested) ReleaseLocked();
            gate.Release();
        }
    }

    /// <summary>Releases the session when it has not been used for <see cref="IdleRelease"/>. Never waits for a running call.</summary>
    internal static void ReleaseIdle()
    {
        if (!gate.Wait(0)) return;
        try { if (session != null && Clock() - lastUse >= IdleRelease) ReleaseLocked(); }
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
        var old = session; session = null; loadedPath = null;
        old?.Dispose();
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
            throw new InvalidDataException(L.T("A model file is damaged. Download the model again on the AI models page."));
    }

    private static SessionOptions Options()
    {
        var options = new SessionOptions { IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4), InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        return options;
    }
}
