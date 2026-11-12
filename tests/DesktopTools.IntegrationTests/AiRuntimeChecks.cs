using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopTools.Extras;
using Microsoft.ML.OnnxRuntime;

internal static class AiRuntimeChecks
{
    private const string U2NetPHash = "309C8469258DDA742793DCE0EBEA8E6DD393174F89934733ECC8B14C76F4DDD8";

    internal static async Task RunAsync()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        async Task<Exception?> Fails(Task task) { try { await task; return null; } catch (Exception e) { return e; } }
        string model = Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "u2netp.onnx");
        string copy = Path.GetFullPath("ai-runtime-copy-" + Guid.NewGuid().ToString("N") + ".onnx");
        File.Copy(model, copy);
        var now = new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
        var previousClock = AiRuntime.Clock; var previousIdle = AiRuntime.IdleRelease;
        try
        {
            Check(AiRuntime.IdleRelease == TimeSpan.FromSeconds(60), "Default idle release is not 60 seconds");
            Check(AiRuntime.Device == "Processor", "Only the processor is supported in this update");
            AiRuntime.Clock = () => now;
            AiRuntime.ReleaseAll();
            AiRuntime.ReleaseAll(); // safe when nothing is loaded
            Check(AiRuntime.LoadedModelPath == null, "Something loaded before the first call");

            // First call loads; a second within the idle period reuses the same session.
            InferenceSession first = await AiRuntime.RunAsync(model, U2NetPHash, session => session, CancellationToken.None);
            Check(AiRuntime.LoadedModelPath == Path.GetFullPath(model), "First call did not load the model");
            Check(first.InputMetadata.Count > 0, "Loaded session is not usable");
            now += TimeSpan.FromSeconds(30);
            AiRuntime.ReleaseIdle();
            InferenceSession second = await AiRuntime.RunAsync(model, U2NetPHash.ToLowerInvariant(), session => session, CancellationToken.None);
            Check(ReferenceEquals(first, second), "Second call within the idle period created a new session");

            // Idle period passed: released.
            now += TimeSpan.FromSeconds(59);
            AiRuntime.ReleaseIdle();
            Check(AiRuntime.LoadedModelPath != null, "Released before the idle period passed");
            now += TimeSpan.FromSeconds(2);
            AiRuntime.ReleaseIdle();
            Check(AiRuntime.LoadedModelPath == null, "Idle session was not released");

            // A wrong checksum throws and loads nothing.
            var wrong = await Fails(AiRuntime.RunAsync(model, new string('0', 64), _ => 0, CancellationToken.None));
            Check(wrong is InvalidDataException && AiRuntime.LoadedModelPath == null, "Wrong checksum was not refused: " + wrong);
            var missing = await Fails(AiRuntime.RunAsync(copy + ".missing", U2NetPHash, _ => 0, CancellationToken.None));
            Check(missing is FileNotFoundException && AiRuntime.LoadedModelPath == null, "Missing file was not refused: " + missing);

            // A second model path replaces the first (one model at a time).
            await AiRuntime.RunAsync(model, U2NetPHash, _ => 0, CancellationToken.None);
            InferenceSession replaced = await AiRuntime.RunAsync(copy, U2NetPHash, session => session, CancellationToken.None);
            Check(AiRuntime.LoadedModelPath == copy && !ReferenceEquals(replaced, first), "Second model did not replace the first");

            // A cancelled token throws without leaving a session loaded.
            AiRuntime.ReleaseAll();
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                var error = await Fails(AiRuntime.RunAsync(model, U2NetPHash, _ => 0, cancelled.Token));
                Check(error is OperationCanceledException && AiRuntime.LoadedModelPath == null, "Cancelled call loaded a session: " + error);
            }

            // Two overlapping calls run one after the other.
            int running = 0, maximum = 0;
            var release = new TaskCompletionSource();
            int Use(InferenceSession _)
            {
                int now = Interlocked.Increment(ref running); maximum = Math.Max(maximum, now);
                release.Task.Wait(TimeSpan.FromSeconds(10));
                Interlocked.Decrement(ref running); return now;
            }
            var a = AiRuntime.RunAsync(model, U2NetPHash, Use, CancellationToken.None);
            await Task.Delay(200);
            var b = AiRuntime.RunAsync(model, U2NetPHash, Use, CancellationToken.None);
            await Task.Delay(200);
            Check(!b.IsCompleted && running == 1, "Second call did not wait for the first");
            release.SetResult();
            await Task.WhenAll(a, b);
            Check(maximum == 1, "Calls overlapped");

            // A failure inside the caller's work does not poison the runtime.
            var thrown = await Fails(AiRuntime.RunAsync<int>(model, U2NetPHash, _ => throw new InvalidOperationException("caller"), CancellationToken.None));
            Check(thrown is InvalidOperationException, "Caller exception was not passed through");
            Check(await AiRuntime.RunAsync(model, U2NetPHash, _ => 7, CancellationToken.None) == 7, "Runtime unusable after a caller exception");

            AiRuntime.ReleaseAll();
            Check(AiRuntime.LoadedModelPath == null, "ReleaseAll left a session");
            File.Delete(copy); // nothing holds the file after release
        }
        finally
        {
            AiRuntime.ReleaseAll(); AiRuntime.Clock = previousClock; AiRuntime.IdleRelease = previousIdle;
            if (File.Exists(copy)) File.Delete(copy);
        }
    }
}
