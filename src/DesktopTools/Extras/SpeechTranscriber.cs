using System.Security.Cryptography;
using DesktopTools.Core;
using DesktopTools.Localization;
using DesktopTools.Native;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DesktopTools.Extras;

internal enum TranscriptModelSize { Fast, Accurate }

internal sealed record TranscriptProgress(TimeSpan Processed, TimeSpan Total);

/// <summary>Timed lines in file time, the language used, the audio length read, and how many windows were skipped as silence or no speech.</summary>
internal sealed record TranscriptResult(IReadOnlyList<TranscriptLine> Lines, string Language, TimeSpan Duration, int SkippedWindows);

/// <summary>A transcript model pack is not downloaded (<see cref="Damaged"/> false) or failed its checks (true).</summary>
internal sealed class ModelPackUnavailableException(ModelPack pack, string message, bool damaged) : Exception(message)
{
    public ModelPack Pack { get; } = pack;
    public bool Damaged { get; } = damaged;
}

/// <summary>
/// Turns a recording's speech into timed lines with an optional Whisper pack (spec section 6). Audio is decoded in a stream, one 30-second
/// window at a time; each window is a separate <see cref="AiRuntime"/> call, so other features can use the model gate between windows.
/// The encoder is loaded with only its last_hidden_state output (an in-memory edit; the downloaded file is never changed).
/// </summary>
internal static class SpeechTranscriber
{
    private const int MinimumWindowSamples = WhisperDecoding.SampleRate / 10;
    private const int HeadSize = 64;

    internal static string PackId(TranscriptModelSize size) => size == TranscriptModelSize.Accurate ? "speech-accurate" : "speech-fast";

    internal static ModelPack? Pack(TranscriptModelSize size) => ModelPacks.Catalog.FirstOrDefault(pack => pack.Id == PackId(size));

    internal static ModelPackState StateOf(TranscriptModelSize size) => Pack(size) is { } pack ? ModelPacks.StateOf(pack) : ModelPackState.Missing;

    internal static bool IsInstalled(TranscriptModelSize size) => StateOf(size) == ModelPackState.Installed;

    internal static async Task<TranscriptResult> TranscribeAsync(string mediaPath, TranscriptModelSize size, string? language,
        IProgress<TranscriptProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var pack = Pack(size) ?? throw new InvalidOperationException("The transcript model is not in this version's catalog.");
        string folder = ModelPacks.DirectoryFor(pack) ?? throw Unavailable(pack);
        var encoder = PackFile(pack, "encoder_model.onnx"); var decoder = PackFile(pack, "decoder_model_merged.onnx");
        var (config, vocabulary) = await Task.Run(() => ReadSettings(pack, folder), token);
        if (language != null && !config.Languages.ContainsKey(language)) language = null;
        (string Path, string Sha256)[] models = [(Path.Combine(folder, encoder.File), encoder.Sha256), (Path.Combine(folder, decoder.File), decoder.Sha256)];
        var prepare = new Dictionary<int, Func<byte[], byte[]>> { [0] = bytes => OnnxModelEdit.KeepOutputs(bytes, ["last_hidden_state"]) };

        using var audio = await Task.Run(() => AudioDecoder.Open(mediaPath), token);
        var buffer = new float[WhisperDecoding.WindowSamples];
        int count = 0; long bufferStart = 0; bool ended = false;
        var lines = new List<TranscriptLine>(); int skipped = 0;
        TimeSpan Time(long samples) => TimeSpan.FromTicks(samples * TimeSpan.TicksPerSecond / WhisperDecoding.SampleRate);
        TimeSpan Total(long processed) => audio.Duration > Time(processed) ? audio.Duration : Time(processed);

        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!ended && count < buffer.Length)
            {
                int filled = count;
                count = await Task.Run(() =>
                {
                    int total = filled, read;
                    while (total < buffer.Length && (read = audio.Read(buffer.AsSpan(total))) > 0) total += read;
                    return total;
                }, token);
                ended = count < buffer.Length;
            }
            if (count < MinimumWindowSamples) break;

            int advance, leadingSilence;
            var window = buffer.AsMemory(0, count);
            if (WhisperDecoding.RmsDbfs(window.Span) < WhisperDecoding.SilenceDbfs) { skipped++; advance = count; }
            else if ((leadingSilence = WhisperDecoding.LeadingSilenceSamples(window.Span)) > 0) advance = leadingSilence; // start the window near the first sound
            else
            {
                float[] mel = await Task.Run(() => WhisperDecoding.LogMel(window.Span), token);
                string? requested = language;
                WindowResult result;
                try
                {
                    result = await AiRuntime.RunAsync(models, sessions => Decode(sessions[0], sessions[1], mel, config, requested, token), token, prepare);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or OnnxRuntimeException)
                {
                    string? failed = AiRuntime.FailedPath(e) ?? (e as FileNotFoundException)?.FileName;
                    if (failed == null || !failed.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw;
                    // Only a checksum mismatch marks the pack damaged; a sharing violation or a failed allocation is reported as it is.
                    if (AiRuntime.DamagedPath(e) != null) { ModelPacks.MarkDamaged(pack); throw Unavailable(pack); }
                    if (e is FileNotFoundException or DirectoryNotFoundException || ModelPacks.StateOf(pack) == ModelPackState.Missing) throw Unavailable(pack);
                    throw;
                }
                if (WhisperDecoding.IsNoSpeech(result.NoSpeechProbability)) { skipped++; advance = count; }
                else
                {
                    // The language is detected again on each window until one holds speech, then kept for the rest of the file.
                    language ??= result.Language;
                    var (windowLines, seek) = WhisperDecoding.Segments(result.Tokens, bufferStart / (double)WhisperDecoding.SampleRate, count, config, vocabulary.Decode);
                    lines.AddRange(windowLines);
                    advance = Math.Clamp(seek, 1, count);
                }
            }
            bufferStart += advance;
            if (advance >= count) count = 0;
            else { Array.Copy(buffer, advance, buffer, 0, count - advance); count -= advance; }
            progress?.Report(new TranscriptProgress(Time(bufferStart), Total(bufferStart)));
            if (ended && count == 0) break;
        }
        long length = bufferStart + count;
        return new TranscriptResult(lines, language ?? "", Time(length), skipped);
    }

    private sealed record WindowResult(IReadOnlyList<int> Tokens, float NoSpeechProbability, string Language);

    /// <summary>Runs the encoder once and the decoder greedily over one window. Detects the language first when none is given.</summary>
    private static WindowResult Decode(InferenceSession encoder, InferenceSession decoder, float[] mel, WhisperConfig config, string? language, CancellationToken token)
    {
        float[] hidden; int[] hiddenShape;
        using (var outputs = encoder.Run([NamedOnnxValue.CreateFromTensor("input_features", new DenseTensor<float>(mel, [1, WhisperDecoding.MelBins, WhisperDecoding.Frames]))]))
        {
            var tensor = outputs.First().AsTensor<float>();
            hidden = tensor.ToArray(); hiddenShape = tensor.Dimensions.ToArray();
        }
        if (hiddenShape.Length != 3) throw new InvalidDataException("The speech encoder returned an unexpected shape.");
        var states = new DenseTensor<float>(hidden, hiddenShape);
        int layers = decoder.InputMetadata.Keys.Count(name => name.EndsWith(".decoder.key", StringComparison.Ordinal));
        int heads = decoder.InputMetadata["past_key_values.0.decoder.key"].Dimensions[1];

        Dictionary<string, DenseTensor<float>> EmptyPast()
        {
            var past = new Dictionary<string, DenseTensor<float>>();
            for (int layer = 0; layer < layers; layer++)
                foreach (string kind in new[] { "decoder", "encoder" })
                    foreach (string part in new[] { "key", "value" })
                        past[$"past_key_values.{layer}.{kind}.{part}"] = new DenseTensor<float>([1, heads, 0, HeadSize]);
            return past;
        }
        // One step: returns logits at the last position (and at position 0 for the first step), and updates the cache in place.
        (float[] Last, float[] First) Step(int[] ids, Dictionary<string, DenseTensor<float>> past, bool first)
        {
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(ids.Select(id => (long)id).ToArray(), [1, ids.Length])),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", states),
                NamedOnnxValue.CreateFromTensor("use_cache_branch", new DenseTensor<bool>(new[] { !first }, [1])),
            };
            foreach (var (name, value) in past) inputs.Add(NamedOnnxValue.CreateFromTensor(name, value));
            using var results = decoder.Run(inputs);
            var logits = results.First(result => result.Name == "logits").AsTensor<float>();
            int positions = logits.Dimensions[1], vocabulary = logits.Dimensions[2];
            float[] all = logits.ToArray();
            float[] last = all.AsSpan((positions - 1) * vocabulary, vocabulary).ToArray();
            float[] firstPosition = first ? all.AsSpan(0, vocabulary).ToArray() : [];
            foreach (var output in results.Where(result => result.Name.StartsWith("present.", StringComparison.Ordinal)))
            {
                string name = "past_key_values." + output.Name["present.".Length..];
                if (!first && name.Contains(".encoder.", StringComparison.Ordinal)) continue; // the cross-attention cache is fixed after step one
                var tensor = output.AsTensor<float>();
                past[name] = new DenseTensor<float>(tensor.ToArray(), tensor.Dimensions.ToArray());
            }
            return (last, firstPosition);
        }

        if (language == null || !config.Languages.ContainsKey(language))
        {
            token.ThrowIfCancellationRequested();
            var (detect, _) = Step([config.StartOfTranscript], EmptyPast(), first: true);
            language = WhisperDecoding.BestLanguage(detect, config);
        }

        var cache = EmptyPast();
        var tokens = new List<int>();
        float noSpeech = 0;
        bool firstStep = true;
        while (tokens.Count < WhisperDecoding.MaxTokensPerWindow)
        {
            token.ThrowIfCancellationRequested();
            int[] ids = firstStep ? [config.StartOfTranscript, config.Languages[language], config.Transcribe] : [tokens[^1]];
            var (logits, firstLogits) = Step(ids, cache, firstStep);
            if (firstStep) noSpeech = Probability(firstLogits, config.NoSpeech);
            WhisperDecoding.ApplyRules(logits, tokens, firstStep, config);
            firstStep = false;
            int best = 0;
            for (int i = 1; i < logits.Length; i++) if (logits[i] > logits[best]) best = i;
            if (best == config.EndOfText || float.IsNegativeInfinity(logits[best])) break;
            tokens.Add(best);
        }
        return new WindowResult(tokens, noSpeech, language);
    }

    private static float Probability(float[] logits, int id)
    {
        if (id < 0 || id >= logits.Length) return 0;
        float max = logits.Max(); double sum = 0;
        foreach (float value in logits) sum += Math.Exp(value - max);
        return (float)(Math.Exp(logits[id] - max) / sum);
    }

    private static ModelPackFile PackFile(ModelPack pack, string name) =>
        pack.Files.FirstOrDefault(file => file.File == name) ?? throw new InvalidDataException($"The transcript model lacks {name}.");

    private static (WhisperConfig Config, WhisperVocabulary Vocabulary) ReadSettings(ModelPack pack, string folder)
    {
        try
        {
            byte[] Verified(string name)
            {
                var file = PackFile(pack, name);
                byte[] bytes = File.ReadAllBytes(Path.Combine(folder, file.File));
                if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(name + " failed its checksum.");
                return bytes;
            }
            var config = WhisperDecoding.ParseConfig(System.Text.Encoding.UTF8.GetString(Verified("generation_config.json")));
            using var vocab = new MemoryStream(Verified("vocab.json"));
            return (config, WhisperVocabulary.Load(vocab));
        }
        catch (InvalidDataException) { ModelPacks.MarkDamaged(pack); throw Unavailable(pack); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { throw Unavailable(pack); }
    }

    private static ModelPackUnavailableException Unavailable(ModelPack pack)
    {
        bool damaged = ModelPacks.StateOf(pack) == ModelPackState.Damaged;
        return new ModelPackUnavailableException(pack,
            damaged ? L.F($"{L.T(pack.Name)} is damaged. Download it again.") : L.F($"{L.T(pack.Name)} is not downloaded."), damaged);
    }
}
