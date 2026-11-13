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

            // A set of models (the OCR pack's detector and recognizer) loads and is reused as one unit.
            string root = Path.GetFullPath("ai-runtime-set-" + Guid.NewGuid().ToString("N"));
            string packFolder = Path.Combine(root, "set-pack");
            Directory.CreateDirectory(packFolder);
            string one = Path.Combine(packFolder, "one.onnx"), two = Path.Combine(packFolder, "two.onnx");
            File.Copy(model, one); File.Copy(model, two);
            try
            {
                var set = new[] { (one, U2NetPHash), (two, U2NetPHash) };
                var sessions = await AiRuntime.RunAsync(set, loaded => loaded.ToArray(), CancellationToken.None);
                Check(sessions.Length == 2 && !ReferenceEquals(sessions[0], sessions[1]), "Set did not load two sessions");
                Check(AiRuntime.LoadedModelPaths.SequenceEqual([one, two]), "Loaded paths: " + string.Join(", ", AiRuntime.LoadedModelPaths));
                var again = await AiRuntime.RunAsync(set, loaded => loaded.ToArray(), CancellationToken.None);
                Check(ReferenceEquals(again[0], sessions[0]) && ReferenceEquals(again[1], sessions[1]), "Same set was not reused");
                // One wrong hash in a set: the damaged file is named and nothing stays loaded.
                AiRuntime.ReleaseAll();
                var damaged = await Fails(AiRuntime.RunAsync(new[] { (one, U2NetPHash), (two, new string('0', 64)) }, _ => 0, CancellationToken.None));
                Check(damaged is InvalidDataException && AiRuntime.DamagedPath(damaged) == two && AiRuntime.LoadedModelPaths.Count == 0, "Damaged file in a set: " + damaged);
                // Removing a pack whose folder holds a loaded file releases the set first, so the folder can be deleted.
                await AiRuntime.RunAsync(set, _ => 0, CancellationToken.None);
                var pack = new DesktopTools.Core.ModelPack("set-pack", "n", "p", "f", "pub", "a", "https://huggingface.co/x/y", "r1", "mit", "MIT.txt", "a", "t", ["l"],
                    [new("one.onnx", "https://huggingface.co/x/y/resolve/r1/one.onnx", U2NetPHash, new FileInfo(one).Length)]);
                ModelPacks.Remove(pack, new DesktopTools.Core.ModelConsentStore(root), root);
                Check(AiRuntime.LoadedModelPaths.Count == 0 && !Directory.Exists(packFolder), "Remove did not release the loaded set");
            }
            finally { AiRuntime.ReleaseAll(); if (Directory.Exists(root)) Directory.Delete(root, true); }

            // A prepare step runs once per load on the verified bytes; here it keeps only the first output of a real model.
            AiRuntime.ReleaseAll();
            int prepared = 0; string? firstOutput = null;
            byte[] KeepFirstStep(byte[] bytes)
            {
                prepared++; firstOutput = DesktopTools.Core.OnnxModelEdit.OutputNames(bytes)[0];
                return DesktopTools.Core.OnnxModelEdit.KeepOutputs(bytes, [firstOutput]);
            }
            var KeepFirst = new System.Collections.Generic.Dictionary<int, Func<byte[], byte[]>> { [0] = KeepFirstStep };
            int originalOutputs;
            using (var probe = new InferenceSession(model)) originalOutputs = probe.OutputMetadata.Count;
            var trimmed = await AiRuntime.RunAsync([(model, U2NetPHash)], loaded => loaded[0], CancellationToken.None, KeepFirst);
            Check(prepared == 1 && originalOutputs > 1 && trimmed.OutputMetadata.Keys.SequenceEqual([firstOutput!]), $"Prepared outputs: {string.Join(", ", trimmed.OutputMetadata.Keys)} of {originalOutputs}");
            var trimmedAgain = await AiRuntime.RunAsync([(model, U2NetPHash)], loaded => loaded[0], CancellationToken.None, KeepFirst);
            Check(prepared == 1 && ReferenceEquals(trimmed, trimmedAgain), "A loaded set was prepared again");
            AiRuntime.ReleaseAll();
            var damagedPrepared = await Fails(AiRuntime.RunAsync([(model, new string('0', 64))], _ => 0, CancellationToken.None, KeepFirst));
            Check(damagedPrepared is InvalidDataException && AiRuntime.DamagedPath(damagedPrepared) == Path.GetFullPath(model) && prepared == 1 && AiRuntime.LoadedModelPath == null,
                "A damaged file reached prepare: " + damagedPrepared);
            var failedPrepare = await Fails(AiRuntime.RunAsync<int>([(model, U2NetPHash)], _ => 0, CancellationToken.None, new System.Collections.Generic.Dictionary<int, Func<byte[], byte[]>> { [0] = _ => throw new InvalidDataException("bad graph") }));
            Check(failedPrepare is InvalidDataException && AiRuntime.FailedPath(failedPrepare) == Path.GetFullPath(model) && AiRuntime.DamagedPath(failedPrepare) == null && AiRuntime.LoadedModelPath == null,
                "A failing prepare step: " + failedPrepare);

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
