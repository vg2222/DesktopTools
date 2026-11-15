using DesktopTools.Core;
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
    internal const int MaxCharacters = 20000, MaxTokens = 256;
    /// <summary>Test and measurement seam: decodes with this many beams instead of the model's <c>num_beams</c>.</summary>
    internal static int? BeamsOverride { get; set; }
    // Beam 4 measured better than greedy in both bundled directions at about 1.5 times the time (docs/text-tools.md); wider beams were not measured.
    private const int MaxBeams = 4;
    private const string EncoderFile = "encoder_model_quantized.onnx", DecoderFile = "decoder_model_merged_quantized.onnx";
    private sealed record Asset(string File, string Sha256);

    /// <summary>
    /// Translates sentence by sentence. The encoder and decoder of every route stage load as one <see cref="AiRuntime"/> set (two files direct,
    /// four through English), so repeated requests reuse them until the idle release. <paramref name="progress"/> receives the fraction of
    /// sentence parts done across all stages, ending at 1.
    /// </summary>
    internal static async Task<string> TranslateAsync(string text, string direction, CancellationToken token = default, string? packRoot = null, IProgress<double>? progress = null)
    {
        string[] route = TranslationPacks.Route(direction);
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException(L.T("Enter text to translate."));
        if (text.Length > MaxCharacters) throw new ArgumentException(L.T("Select a shorter passage (up to 20,000 characters)."));
        token.ThrowIfCancellationRequested();
        var directories = new string[route.Length];
        var models = new List<(string Path, string Sha256)>();
        for (int i = 0; i < route.Length; i++)
        {
            directories[i] = TranslationPacks.DirectoryFor(route[i], packRoot) ?? throw new FileNotFoundException(L.T("Download the offline language pack first."));
            var onnx = Verify(directories[i], route[i], token);
            models.Add((Path.Combine(directories[i], EncoderFile), onnx[EncoderFile]));
            models.Add((Path.Combine(directories[i], DecoderFile), onnx[DecoderFile]));
        }
        try
        {
            return await AiRuntime.RunAsync(models, sessions =>
            {
                string result = text;
                for (int stageIndex = 0; stageIndex < route.Length; stageIndex++)
                {
                    // OPUS-MT is sentence-trained. Preserve separators in each stage of a pivot translation.
                    var parts = Regex.Split(result, @"(?<=[.!?。！？])(\s+)|(\r?\n+)");
                    int total = parts.Count(p => !string.IsNullOrWhiteSpace(p)), done = 0;
                    var builder = new StringBuilder();
                    var stage = new TranslationStage(directories[stageIndex], sessions[2 * stageIndex], sessions[2 * stageIndex + 1]);
                    foreach (string part in parts)
                    {
                        token.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(part)) { builder.Append(part); continue; }
                        builder.Append(stage.Translate(part, token));
                        progress?.Report(Math.Min(1.0, (stageIndex + (double)++done / total) / route.Length));
                    }
                    result = builder.ToString();
                }
                progress?.Report(1.0);
                return result;
            }, token);
        }
        catch (InvalidDataException error) when (AiRuntime.DamagedPath(error) is string damaged)
        {
            // Make a damaged downloaded pack available for re-download in Text tools.
            string folder = Path.GetDirectoryName(damaged)!;
            if (!IsBundled(folder, Path.GetFileName(folder))) TranslationPacks.MarkDamaged(folder);
            throw new InvalidDataException(L.T("Translation models are damaged. Extract a fresh DesktopTools package."), error);
        }
    }

    private static bool IsBundled(string directory, string direction) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), Path.Combine(AppContext.BaseDirectory, "Assets", "Translation", direction), StringComparison.OrdinalIgnoreCase);

    /// <summary>Checks the small files of a stage now and returns the SHA-256 of its model files, which <see cref="AiRuntime"/> checks at load.</summary>
    private static Dictionary<string, string> Verify(string directory, string direction, CancellationToken token)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "Assets", "Translation");
        bool bundled = IsBundled(directory, direction);
        IEnumerable<Asset> assets = bundled
            ? JsonSerializer.Deserialize<Asset[]>(File.ReadAllText(Path.Combine(root, "manifest.json")))!
                .Where(asset => asset.File.StartsWith(direction + "/", StringComparison.Ordinal)).Select(asset => new Asset(Path.GetFileName(asset.File), asset.Sha256))
            : TranslationPacks.Find(direction).Files.Select(asset => new Asset(asset.File, asset.Sha256));
        var onnx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            token.ThrowIfCancellationRequested();
            if (asset.File.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)) { onnx[asset.File] = asset.Sha256; continue; }
            bool intact;
            try { using var file = File.OpenRead(Path.Combine(directory, asset.File)); intact = Convert.ToHexString(SHA256.HashData(file)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase); }
            catch (FileNotFoundException) { intact = false; }
            if (!intact)
            {
                if (!bundled) TranslationPacks.MarkDamaged(directory);
                throw new InvalidDataException(L.T("Translation models are damaged. Extract a fresh DesktopTools package."));
            }
        }
        if (!onnx.ContainsKey(EncoderFile) || !onnx.ContainsKey(DecoderFile)) throw new InvalidDataException(L.T("Translation models are damaged. Extract a fresh DesktopTools package."));
        return onnx;
    }

    // One route stage holds the tokenization data; its sessions belong to AiRuntime. Decoder outputs and cancellation remain sentence-local.
    private sealed class TranslationStage
    {
        private readonly int heads, headSize, beams;
        private readonly double lengthPenalty;
        private readonly Dictionary<string, int> vocabulary;
        private readonly Dictionary<int, string> reverse;
        private readonly SentencePieceTokenizer tokenizer;
        private readonly InferenceSession encoder, decoder;

        internal TranslationStage(string directory, InferenceSession encoder, InferenceSession decoder)
        {
            this.encoder = encoder; this.decoder = decoder;
            using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "config.json")));
            heads = config.RootElement.GetProperty("decoder_attention_heads").GetInt32();
            headSize = config.RootElement.GetProperty("d_model").GetInt32() / heads;
            beams = BeamsOverride ?? (config.RootElement.TryGetProperty("num_beams", out var b) && b.TryGetInt32(out int n) ? Math.Clamp(n, 1, MaxBeams) : 1);
            lengthPenalty = config.RootElement.TryGetProperty("length_penalty", out var p) && p.TryGetDouble(out double v) ? v : 1.0;
            vocabulary = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path.Combine(directory, "vocab.json")))!;
            reverse = vocabulary.ToDictionary(p => p.Value, p => p.Key);
            using var spm = File.OpenRead(Path.Combine(directory, "source.spm"));
            tokenizer = SentencePieceTokenizer.Create(spm, false, false);
        }

        internal string Translate(string text, CancellationToken token)
        {
            long[] ids = tokenizer.EncodeToTokens(text, out _).Select(t => (long)vocabulary.GetValueOrDefault(t.Value, vocabulary["<unk>"])).Append(0L).ToArray();
            if (ids.Length > MaxTokens) throw new ArgumentException(L.T("This passage has too many tokens. Translate a shorter selection."));
            using var run = new RunOptions(); using var registration = token.Register(() => run.Terminate = true);
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue>? first = null, previous = null;
            try
            {
                token.ThrowIfCancellationRequested();
                var attention = new DenseTensor<long>(Enumerable.Repeat(1L, ids.Length).ToArray(), new[] { 1, ids.Length });
                using var encoded = encoder.Run(new[] { NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids, new[] { 1, ids.Length })), NamedOnnxValue.CreateFromTensor("attention_mask", attention) }, encoder.OutputMetadata.Keys.ToArray(), run);
                var hidden = encoded.First().AsTensor<float>();
                int pad = vocabulary["<pad>"], vocab = vocabulary.Count;
                // End of sentence is token 0 (</s>); the decoder starts from the pad token, which is never generated.
                var search = new TranslationBeamSearch(beams, 0, MaxTokens, lengthPenalty);
                long[] next = [pad]; IReadOnlyList<int> parents = [0];
                // Beams share the encoder output and its cross-attention cache; tiles are made once per row count.
                var tiled = new Dictionary<(string Name, int Rows), Tensor<float>>();
                var tiledMask = new Dictionary<int, Tensor<long>>();
                Tensor<float> Tile(string name, Tensor<float> source, int rows) =>
                    rows == 1 ? source : tiled.TryGetValue((name, rows), out var t) ? t : tiled[(name, rows)] = SelectRows(source, new int[rows]);
                for (int step = 0; !search.Done; step++)
                {
                    token.ThrowIfCancellationRequested();
                    int rows = next.Length;
                    var inputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(next, new[] { rows, 1 })),
                        NamedOnnxValue.CreateFromTensor("encoder_attention_mask", rows == 1 ? attention : tiledMask.TryGetValue(rows, out var mask) ? mask
                            : tiledMask[rows] = new DenseTensor<long>(Enumerable.Repeat(1L, rows * ids.Length).ToArray(), new[] { rows, ids.Length })),
                        NamedOnnxValue.CreateFromTensor("encoder_hidden_states", Tile("hidden", hidden, rows)),
                        NamedOnnxValue.CreateFromTensor("use_cache_branch", new DenseTensor<bool>(new[] { step > 0 }, new[] { 1 }))
                    };
                    bool identity = parents.Count == 1 && parents[0] == 0;
                    foreach (string name in decoder.InputMetadata.Keys.Where(n => n.StartsWith("past_key_values.", StringComparison.Ordinal)))
                    {
                        string outputName = name.Replace("past_key_values.", "present.", StringComparison.Ordinal);
                        Tensor<float> cache;
                        if (step == 0) cache = new DenseTensor<float>(Array.Empty<float>(), new[] { 1, heads, 0, headSize });
                        else if (name.Contains(".encoder.", StringComparison.Ordinal)) cache = Tile(name, first!.First(v => v.Name == outputName).AsTensor<float>(), rows);
                        else
                        {
                            // Each beam continues the self-attention cache of the row it extends.
                            var own = previous!.First(v => v.Name == outputName).AsTensor<float>();
                            cache = identity ? own : SelectRows(own, parents);
                        }
                        inputs.Add(NamedOnnxValue.CreateFromTensor(name, cache));
                    }
                    var result = decoder.Run(inputs, decoder.OutputMetadata.Keys.ToArray(), run);
                    if (previous != first) previous?.Dispose(); previous = result; first ??= result;
                    var logits = result.First(v => v.Name == "logits").AsTensor<float>();
                    var scores = new float[rows * vocab];
                    for (int r = 0; r < rows; r++) for (int id = 0; id < vocab; id++) scores[r * vocab + id] = logits[r, 0, id];
                    try { parents = search.Step(scores, vocab, pad); }
                    catch (InvalidDataException) { throw new InvalidDataException(L.T("The translation model returned an invalid result.")); }
                    next = search.NextTokens.Select(id => (long)id).ToArray();
                }
                if (!search.HasResult) throw new InvalidOperationException(L.T("Translation reached its length limit. Try a shorter passage."));
                return string.Concat(search.Best.Select(id => reverse[id])).Replace("▁", " ").Trim();
            }
            catch (OnnxRuntimeException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
            finally { if (previous != first) previous?.Dispose(); first?.Dispose(); }
        }

        // Copies the given rows (first dimension) of a tensor, in order; the source may be native memory owned by ONNX Runtime.
        private static DenseTensor<float> SelectRows(Tensor<float> source, IReadOnlyList<int> rows)
        {
            int[] dimensions = source.Dimensions.ToArray();
            int rowSize = (int)(source.Length / dimensions[0]);
            ReadOnlySpan<float> data = source is DenseTensor<float> dense ? dense.Buffer.Span : source.ToArray();
            var copy = new float[rows.Count * rowSize];
            for (int i = 0; i < rows.Count; i++) data.Slice(rows[i] * rowSize, rowSize).CopyTo(copy.AsSpan(i * rowSize));
            dimensions[0] = rows.Count;
            return new DenseTensor<float>(copy, dimensions);
        }
    }
}
