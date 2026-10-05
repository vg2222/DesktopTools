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
    internal static async Task<string> TranslateAsync(string text, string direction, CancellationToken token = default, string? packRoot = null)
    {
        string[] route = TranslationPacks.Route(direction);
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException(L.T("Enter text to translate."));
        if (text.Length > MaxCharacters) throw new ArgumentException(L.T("Select a shorter passage (up to 4,000 characters)."));
        await gate.WaitAsync(token);
        try { return await Task.Run(() =>
        {
            string result = text;
            foreach (string pair in route)
            {
                string directory = TranslationPacks.DirectoryFor(pair, packRoot) ?? throw new FileNotFoundException(L.T("Download the offline language pack first."));
                Verify(directory, pair, token);
                // OPUS-MT is sentence-trained. Preserve separators in each stage of a pivot translation.
                var parts = Regex.Split(result, @"(?<=[.!?。！？])(\s+)|(\r?\n+)");
                if (parts.Count(p => !string.IsNullOrWhiteSpace(p)) > 32) throw new ArgumentException(L.T("Translate up to 32 sentences at a time."));
                var builder = new StringBuilder();
                using var stage = new TranslationStage(directory);
                foreach (string part in parts) { token.ThrowIfCancellationRequested(); builder.Append(string.IsNullOrWhiteSpace(part) ? part : stage.Translate(part, token)); }
                result = builder.ToString();
            }
            return result;
        }, token); }
        finally { gate.Release(); }
    }
    private static void Verify(string directory, string direction, CancellationToken token)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Assets", "Translation");
        IEnumerable<Asset> assets = string.Equals(directory, Path.Combine(root, direction), StringComparison.OrdinalIgnoreCase)
            ? JsonSerializer.Deserialize<Asset[]>(File.ReadAllText(Path.Combine(root, "manifest.json")))!
                .Where(asset => asset.File.StartsWith(direction + "/", StringComparison.Ordinal)).Select(asset => new Asset(Path.GetFileName(asset.File), asset.Sha256))
            : TranslationPacks.Find(direction).Files.Select(asset => new Asset(asset.File, asset.Sha256));
        foreach (var asset in assets)
        {
            token.ThrowIfCancellationRequested(); using var file = File.OpenRead(Path.Combine(directory, asset.File));
            if (!Convert.ToHexString(SHA256.HashData(file)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                // Make a damaged downloaded pack available for re-download in Text tools.
                TranslationPacks.MarkDamaged(directory);
                throw new InvalidDataException(L.T("Translation models are damaged. Extract a fresh DesktopTools package."));
            }
        }
    }
    // One route stage owns its sessions and tokenization data. Decoder outputs and cancellation
    // remain sentence-local; disposing the stage also releases a partially initialized session pair.
    private sealed class TranslationStage : IDisposable
    {
        private readonly string directory;
        private readonly int heads, headSize;
        private readonly Dictionary<string, int> vocabulary;
        private readonly Dictionary<int, string> reverse;
        private readonly SentencePieceTokenizer tokenizer;
        private InferenceSession? encoder, decoder;

        internal TranslationStage(string directory)
        {
            this.directory = directory;
            using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "config.json")));
            heads = config.RootElement.GetProperty("decoder_attention_heads").GetInt32();
            headSize = config.RootElement.GetProperty("d_model").GetInt32() / heads;
            vocabulary = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path.Combine(directory, "vocab.json")))!;
            reverse = vocabulary.ToDictionary(p => p.Value, p => p.Key);
            using var spm = File.OpenRead(Path.Combine(directory, "source.spm"));
            tokenizer = SentencePieceTokenizer.Create(spm, false, false);
        }

        internal string Translate(string text, CancellationToken token)
        {
            long[] ids = tokenizer.EncodeToTokens(text, out _).Select(t => (long)vocabulary.GetValueOrDefault(t.Value, vocabulary["<unk>"])).Append(0L).ToArray();
            if (ids.Length > MaxTokens) throw new ArgumentException(L.T("This passage has too many tokens. Translate a shorter selection."));
            if (encoder == null)
            {
                using var options = new SessionOptions { IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4), InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL, LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR };
                options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
                encoder = new InferenceSession(Path.Combine(directory, "encoder_model_quantized.onnx"), options);
                token.ThrowIfCancellationRequested();
                decoder = new InferenceSession(Path.Combine(directory, "decoder_model_merged_quantized.onnx"), options);
            }
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
                    foreach (string name in decoder!.InputMetadata.Keys.Where(n => n.StartsWith("past_key_values.", StringComparison.Ordinal)))
                    {
                        string outputName = name.Replace("past_key_values.", "present.", StringComparison.Ordinal);
                        Tensor<float> cache = step == 0 ? new DenseTensor<float>(Array.Empty<float>(), new[] { 1, heads, 0, headSize }) :
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

        public void Dispose()
        {
            try { decoder?.Dispose(); }
            finally { encoder?.Dispose(); }
        }
    }
}
