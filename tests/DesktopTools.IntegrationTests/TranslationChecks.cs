using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;
using DesktopTools.Extras;
using System.Threading;

internal static class TranslationChecks
{
    public static async Task RunAsync()
    {
        string russian = await LocalTranslation.TranslateAsync("Hello world! This is a test.", "en-ru");
        string english = await LocalTranslation.TranslateAsync("Привет, мир! Это тест.", "ru-en");
        File.WriteAllText("translation-results.txt", russian + "\n" + english);
        foreach (string text in new[] { "Hello world!", "Open the window.", "The computer is on the table.", "Please save this file before closing the application." }) File.AppendAllText("translation-results.txt", "\n" + text + " => " + await LocalTranslation.TranslateAsync(text, "en-ru"));
        if (!russian.Contains("тест", StringComparison.OrdinalIgnoreCase) || !english.Contains("test", StringComparison.OrdinalIgnoreCase)) throw new Exception("Reference translation failed: " + russian + " / " + english);
        if (!russian.Contains("мир", StringComparison.OrdinalIgnoreCase)) throw new Exception("Opening sentence was lost");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await LocalTranslation.TranslateAsync("Hello", "en-ru", canceled.Token); throw new Exception("Pre-cancel ignored"); } catch (OperationCanceledException) { }
        using var running = new CancellationTokenSource();
        var request = LocalTranslation.TranslateAsync("Please translate this passage about a computer, a window and an application.", "en-ru", running.Token);
        await Task.Delay(100); running.Cancel();
        try { await request; throw new Exception("In-flight cancel ignored"); } catch (OperationCanceledException) { }
        try { await LocalTranslation.TranslateAsync(new string('a', 20001), "en-ru"); throw new Exception("Length limit ignored"); } catch (ArgumentException) { }
        try { await LocalTranslation.TranslateAsync("test", "bad"); throw new Exception("Invalid language accepted"); } catch (ArgumentException) { }
        var paragraphs = await LocalTranslation.TranslateAsync("Open the window.\nThe computer is on the table.", "en-ru");
        if (!paragraphs.Contains('\n') || !paragraphs.Contains("столе")) throw new Exception("Paragraphs lost or gate stranded");
        await LongInputAsync();
    }
    // Spec 9: sessions stay loaded through AiRuntime, progress is reported, and input up to 20,000 characters is accepted.
    private static async Task LongInputAsync()
    {
        AiRuntime.ReleaseAll();
        string fixture = "Hello world! Open the window. The computer is on the table.";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await LocalTranslation.TranslateAsync("Hello world!", "en-ru");
        double cold = watch.Elapsed.TotalMilliseconds;
        if (!AiRuntime.LoadedModelPaths.Any(p => p.EndsWith(Path.Combine("en-ru", "decoder_model_merged_quantized.onnx"), StringComparison.OrdinalIgnoreCase)))
            throw new Exception("The en-ru decoder is not kept loaded by AiRuntime after a request");
        watch.Restart();
        await LocalTranslation.TranslateAsync("Hello world!", "en-ru");
        double warm = watch.Elapsed.TotalMilliseconds;
        Console.WriteLine($"Translation timing: short sentence cold {cold:F0} ms, warm {warm:F0} ms");
        if (warm >= cold * 0.5) throw new Exception($"Second request did not reuse the sessions ({cold:F0} ms then {warm:F0} ms)");
        watch.Restart();
        await LocalTranslation.TranslateAsync(fixture, "en-ru");
        Console.WriteLine($"Translation timing: 3-sentence fixture warm {watch.Elapsed.TotalMilliseconds:F0} ms");

        string sentences40 = string.Join(" ", Enumerable.Range(1, 40).Select(i => "Open the window."));
        string forty = await LocalTranslation.TranslateAsync(sentences40, "en-ru");
        if (!forty.Contains("окно", StringComparison.OrdinalIgnoreCase)) throw new Exception("40 sentences were not translated: " + forty[..Math.Min(80, forty.Length)]);

        string block = "The computer is on the table. ";
        string twoThousand = string.Concat(Enumerable.Repeat(block, 2000 / block.Length));
        watch.Restart();
        await LocalTranslation.TranslateAsync(twoThousand, "en-ru");
        Console.WriteLine($"Translation timing: {twoThousand.Length}-character text {watch.Elapsed.TotalMilliseconds:F0} ms");

        int limit = LocalTranslation.MaxCharacters;
        if (limit != 20000) throw new Exception("The character limit is not 20,000");
        string longest = string.Concat(Enumerable.Repeat(block, 20000 / block.Length)).PadRight(20000, ' ');
        var values = new System.Collections.Generic.List<double>();
        var progress = new SyncProgress(values.Add);
        watch.Restart();
        string translated = await LocalTranslation.TranslateAsync(longest, "en-ru", default, null, progress);
        Console.WriteLine($"Translation timing: 20,000-character text {watch.Elapsed.TotalMilliseconds:F0} ms, {values.Count} progress reports");
        if (string.IsNullOrWhiteSpace(translated)) throw new Exception("20,000 characters gave no translation");
        if (values.Count < 2 || values[^1] != 1.0) throw new Exception("Progress did not end at 1");
        for (int i = 1; i < values.Count; i++) if (values[i] < values[i - 1]) throw new Exception("Progress went backwards");
        try { await LocalTranslation.TranslateAsync(longest + "a", "en-ru"); throw new Exception("20,001 characters accepted"); } catch (ArgumentException) { }

        using (var running = new CancellationTokenSource())
        {
            var pending = LocalTranslation.TranslateAsync(longest, "en-ru", running.Token);
            await Task.Delay(300); running.Cancel(); watch.Restart();
            try { await pending; throw new Exception("Cancel of a long request ignored"); } catch (OperationCanceledException) { }
            if (watch.Elapsed > TimeSpan.FromSeconds(2)) throw new Exception($"Cancel took {watch.Elapsed.TotalMilliseconds:F0} ms");
        }
        AiRuntime.ReleaseAll();
        if (AiRuntime.LoadedModelPaths.Count != 0) throw new Exception("ReleaseAll left translation sessions loaded");
        if (!(await LocalTranslation.TranslateAsync("Hello world!", "en-ru")).Contains("мир")) throw new Exception("Translation failed after ReleaseAll");
        AiRuntime.ReleaseAll();
    }

    // Progress<T> posts to the thread pool; this one records synchronously so the order is the order reported.
    private sealed class SyncProgress(Action<double> report) : IProgress<double> { public void Report(double value) { lock (this) report(value); } }
    public static Task MetadataAsync()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Assets", "Translation", "en-ru");
        using var log = File.CreateText("translation-metadata.txt");
        foreach (string model in new[] { "encoder_model_quantized.onnx", "decoder_model_merged_quantized.onnx" })
        {
            using var options = new SessionOptions { IntraOpNumThreads = 2, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
            using var session = new InferenceSession(Path.Combine(root, model), options);
            log.WriteLine(model);
            foreach (var pair in session.InputMetadata) log.WriteLine("IN " + pair.Key + " " + pair.Value.ElementType + " " + string.Join(",", pair.Value.Dimensions));
            foreach (var pair in session.OutputMetadata) log.WriteLine("OUT " + pair.Key + " " + pair.Value.ElementType + " " + string.Join(",", pair.Value.Dimensions));
        }
        using var stream = File.OpenRead(Path.Combine(root, "source.spm"));
        var tokenizer = SentencePieceTokenizer.Create(stream, false, false);
        var tokens = tokenizer.EncodeToTokens("Hello world!", out _);
        log.WriteLine(string.Join(" / ", tokens.Select(t => t.Value)));
        return Task.CompletedTask;
    }
}
