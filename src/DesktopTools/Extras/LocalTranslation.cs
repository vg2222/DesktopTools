using DesktopTools.Localization;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace DesktopTools.Extras;

internal static class LocalTranslation
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    internal const int MaxCharacters = 4000, MaxTokens = 256;
    private sealed record Asset(string File, string Sha256);
    internal static async Task<string> TranslateAsync(string text, string direction, CancellationToken token = default)
    {
        if (direction is not "en-ru" and not "ru-en") throw new ArgumentException("Unsupported translation direction.");
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException(L.T("Enter text to translate."));
        if (text.Length > MaxCharacters) throw new ArgumentException(L.T("Select a shorter passage (up to 4,000 characters)."));
        await gate.WaitAsync(token);
        try { return await Task.Run(() =>
        {
            // OPUS-MT is sentence-trained. Preserve separators and avoid losing short opening sentences.
            var parts = Regex.Split(text, @"(?<=[.!?。！？])(\s+)|(\r?\n+)");
            if (parts.Count(p => !string.IsNullOrWhiteSpace(p)) > 32) throw new ArgumentException(L.T("Translate up to 32 sentences at a time."));
            var result = new StringBuilder();
            foreach (string part in parts) { token.ThrowIfCancellationRequested(); result.Append(string.IsNullOrWhiteSpace(part) ? part : Translate(part, direction, token)); }
            return result.ToString();
        }, token); }
        finally { gate.Release(); }
    }
    private static string Translate(string text, string direction, CancellationToken token)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Assets", "Translation");
        string directory = Path.Combine(root, direction);
        if (!File.Exists(Path.Combine(root, "manifest.json"))) throw new FileNotFoundException(L.T("Translation models are missing. Extract the complete DesktopTools package."));
        var assets = JsonSerializer.Deserialize<Asset[]>(File.ReadAllText(Path.Combine(root, "manifest.json")))!;
        foreach (var asset in assets.Where(a => a.File.StartsWith(direction + "/", StringComparison.Ordinal)))
        {
            token.ThrowIfCancellationRequested(); using var file = File.OpenRead(Path.Combine(root, asset.File));
            if (!Convert.ToHexString(SHA256.HashData(file)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(L.T("Translation models are damaged. Extract a fresh DesktopTools package."));
        }
        var vocabulary = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path.Combine(directory, "vocab.json")))!;
        var reverse = vocabulary.ToDictionary(p => p.Value, p => p.Key);
        using var spm = File.OpenRead(Path.Combine(directory, "source.spm"));
        var tokenizer = SentencePieceTokenizer.Create(spm, false, false);
        long[] ids = tokenizer.EncodeToTokens(text, out _).Select(t => (long)vocabulary.GetValueOrDefault(t.Value, vocabulary["<unk>"])).Append(0L).ToArray();
        if (ids.Length > MaxTokens) throw new ArgumentException(L.T("This passage has too many tokens. Translate a shorter selection."));
        using var options = new SessionOptions { IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4), InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        using var encoder = new InferenceSession(Path.Combine(directory, "encoder_model_quantized.onnx"), options);
        token.ThrowIfCancellationRequested();
        using var decoder = new InferenceSession(Path.Combine(directory, "decoder_model_merged_quantized.onnx"), options);
        using var run = new RunOptions(); using var registration = token.Register(() => run.Terminate = true);
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue>? first = null, previous = null;
        try
        {
            token.ThrowIfCancellationRequested();
            var attention = new DenseTensor<long>(Enumerable.Repeat(1L, ids.Length).ToArray(), new[] { 1, ids.Length });
            using var encoded = encoder.Run(new[] { NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, new[] { 1, ids.Length })), NamedOnnxValue.CreateFromTensor("attention_mask", attention) }, encoder.OutputMetadata.Keys.ToArray(), run);
            var hidden = encoded.First().AsTensor<float>();
            int next = vocabulary["<pad>"]; var generated = new List<int>();
            for (int step = 0; step < MaxTokens; step++)
            {
                token.ThrowIfCancellationRequested();
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(new long[] { next }, new[] { 1, 1 })),
                    NamedOnnxValue.CreateFromTensor("encoder_attention_mask", attention),
                    NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hidden),
                    NamedOnnxValue.CreateFromTensor("use_cache_branch", new DenseTensor<bool>(new[] { step > 0 }, new[] { 1 }))
                };
                foreach (string name in decoder.InputMetadata.Keys.Where(n => n.StartsWith("past_key_values.", StringComparison.Ordinal)))
                {
                    string outputName = name.Replace("past_key_values.", "present.", StringComparison.Ordinal);
                    Tensor<float> cache = step == 0 ? new DenseTensor<float>(Array.Empty<float>(), new[] { 1, 8, 0, 64 }) :
                        (name.Contains(".encoder.", StringComparison.Ordinal) ? first! : previous!).First(v => v.Name == outputName).AsTensor<float>();
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, cache));
                }
                var result = decoder.Run(inputs, decoder.OutputMetadata.Keys.ToArray(), run);
                if (previous != first) previous?.Dispose(); previous = result; first ??= result;
                var logits = result.First(v => v.Name == "logits").AsTensor<float>();
                float best = float.NegativeInfinity; next = 0;
                for (int id = 0; id < vocabulary.Count; id++)
                {
                    if (id == vocabulary["<pad>"]) continue;
                    float score = logits[0, 0, id]; if (score > best) { best = score; next = id; }
                }
                if (!float.IsFinite(best)) throw new InvalidDataException(L.T("The translation model returned an invalid result."));
                if (next == 0) return string.Concat(generated.Select(id => reverse[id])).Replace("▁", " ").Trim();
                generated.Add(next);
            }
            throw new InvalidOperationException(L.T("Translation reached its length limit. Try a shorter passage."));
        }
        catch (OnnxRuntimeException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        finally { if (previous != first) previous?.Dispose(); first?.Dispose(); }
    }
}
