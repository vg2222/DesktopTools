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
        try { await LocalTranslation.TranslateAsync(new string('a', 4001), "en-ru"); throw new Exception("Length limit ignored"); } catch (ArgumentException) { }
        try { await LocalTranslation.TranslateAsync("test", "bad"); throw new Exception("Invalid language accepted"); } catch (ArgumentException) { }
        var paragraphs = await LocalTranslation.TranslateAsync("Open the window.\nThe computer is on the table.", "en-ru");
        if (!paragraphs.Contains('\n') || !paragraphs.Contains("столе")) throw new Exception("Paragraphs lost or gate stranded");
    }
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
