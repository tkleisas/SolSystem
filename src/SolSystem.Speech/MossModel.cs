using System.Text.Json;
using Microsoft.ML.OnnxRuntime;

namespace SolSystem.Speech;

/// <summary>
/// The MOSS-TTS-Nano ONNX deployment: manifest, graph metas, and the inference sessions.
/// </summary>
/// <remarks>
/// <para>
/// The export is a browser-POC bundle: a manifest naming the graphs, a TTS meta carrying the
/// model config and the KV-cache wire format, a codec meta carrying the tokenizer's shapes,
/// and the graphs themselves with their weights in <c>.data</c> companions. Everything the
/// orchestrator needs is read from these files rather than hard-coded here, so a re-export
/// with a different layer count or cache shape needs no change in this port.
/// </para>
/// <para>
/// Shapes and ids are stated in the metas, not assumed: <c>n_vq</c> quantizers per frame at
/// 12.5 Hz, a 17-wide row (one text token + one code per quantizer), and the special ids
/// (pad 3, audio_start 6, audio_end 7, slots 8 and 9, audio_pad 1024) that the request rows
/// are assembled from.
/// </para>
/// </remarks>
internal sealed class MossModel : IDisposable
{
    public const string TtsDirectoryName = "MOSS-TTS-Nano-100M-ONNX";
    public const string CodecDirectoryName = "MOSS-Audio-Tokenizer-Nano-ONNX";

    private readonly Dictionary<string, InferenceSession> _sessions;
    private readonly JsonElement _manifest;
    private readonly JsonElement _ttsMeta;
    private readonly JsonElement _codecMeta;

    private MossModel(
        JsonElement manifest,
        JsonElement ttsMeta,
        JsonElement codecMeta,
        Dictionary<string, InferenceSession> sessions)
    {
        _manifest = manifest;
        _ttsMeta = ttsMeta;
        _codecMeta = codecMeta;
        _sessions = sessions;
    }

    /// <summary>Loads the bundle and creates the sessions. Eager: minutes of inference follow.</summary>
    /// <remarks>
    /// The local graphs are small and thread-barrier dominated: two threads is measurably
    /// better for them than more. The global decode and the codec do real work and take
    /// the full count.
    /// </remarks>
    internal static MossModel Load(string modelRoot, int threadCount = 4, int localThreadCount = 2)
    {
        string ttsDir = Path.Combine(modelRoot, TtsDirectoryName);
        string codecDir = Path.Combine(modelRoot, CodecDirectoryName);

        JsonElement manifest = LoadElement(Path.Combine(ttsDir, "browser_poc_manifest.json"));
        JsonElement ttsMeta = LoadElement(Path.Combine(ttsDir, "tts_browser_onnx_meta.json"));
        JsonElement codecMeta = LoadElement(Path.Combine(codecDir, "codec_browser_onnx_meta.json"));

        var sessions = new Dictionary<string, InferenceSession>(StringComparer.Ordinal)
        {
            ["prefill"] = Session(Path.Combine(ttsDir, GraphFile(ttsMeta, "prefill")), threadCount),
            ["decode"] = Session(Path.Combine(ttsDir, GraphFile(ttsMeta, "decode_step")), threadCount),
            ["local_decoder"] = Session(
                Path.Combine(ttsDir, GraphFile(ttsMeta, "local_decoder")), localThreadCount),
            ["local_cached_step"] = Session(
                Path.Combine(ttsDir, GraphFile(ttsMeta, "local_cached_step")), localThreadCount),
            ["local_fixed_sampled_frame"] = Session(
                Path.Combine(ttsDir, GraphFile(ttsMeta, "local_fixed_sampled_frame")), localThreadCount),
            ["codec_encode"] = Session(
                Path.Combine(codecDir, GraphFile(codecMeta, "encode")), threadCount),
            ["codec_decode"] = Session(
                Path.Combine(codecDir, GraphFile(codecMeta, "decode_full")), threadCount),
            ["codec_decode_step"] = Session(
                Path.Combine(codecDir, GraphFile(codecMeta, "decode_step")), threadCount),
        };

        return new MossModel(manifest, ttsMeta, codecMeta, sessions);
    }

    private static string GraphFile(JsonElement meta, string key) =>
        meta.GetProperty("files").GetProperty(key).GetString()
        ?? throw new InvalidOperationException($"the meta does not name the '{key}' graph");

    private static JsonElement LoadElement(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Where the deployment lives, by walking upward for <c>artifacts/moss-tts</c>: the
    /// one place this is answered, which both the tool and the client call.
    /// </summary>
    internal static string? FindDeployment()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            DirectoryInfo? directory = new(start);
            while (directory is not null)
            {
                string candidate = Path.Combine(directory.FullName, "artifacts", "moss-tts");
                if (Directory.Exists(Path.Combine(candidate, TtsDirectoryName)))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static InferenceSession Session(string path, int threadCount)    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = threadCount,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        };

        return new InferenceSession(path, options);
    }

    internal InferenceSession Session(string key) => _sessions[key];

    internal JsonElement Manifest => _manifest;
    internal JsonElement TtsConfig => _manifest.GetProperty("tts_config");
    internal JsonElement GenerationDefaults => _manifest.GetProperty("generation_defaults");
    internal JsonElement PromptTemplates => _manifest.GetProperty("prompt_templates");
    internal JsonElement ModelConfig => _ttsMeta.GetProperty("model_config");
    internal JsonElement CodecConfig => _codecMeta.GetProperty("codec_config");
    internal JsonElement CodecMeta => _codecMeta;

    /// <summary>Token ids on the manifest's tts_config, read as ints.</summary>
    internal int TokenId(string name) => TtsConfig.GetProperty(name).GetInt32();

    /// <summary>A built-in voice's prompt codes, by name, from the manifest.</summary>
    internal List<List<int>> PromptCodesFor(string voice)
    {
        foreach (JsonElement row in _manifest.GetProperty("builtin_voices").EnumerateArray())
        {
            if (string.Equals(row.GetProperty("voice").GetString(), voice, StringComparison.Ordinal))
            {
                return [.. row.GetProperty("prompt_audio_codes").EnumerateArray()
                    .Select(channel => channel.EnumerateArray().Select(x => x.GetInt32()).ToList())];
            }
        }

        throw new ArgumentException($"Built-in voice not found: {voice}", nameof(voice));
    }

    internal string[] VoiceNames() => [.. _manifest.GetProperty("builtin_voices").EnumerateArray()
        .Select(row => row.GetProperty("voice").GetString() ?? "")];

    /// <summary>How many KV tensors the global model carries, and their names as wired.</summary>
    internal string[] PrefillOutputNames() => StringList(_ttsMeta, "prefill_output_names");

    internal string[] DecodeInputNames() => StringList(_ttsMeta, "decode_input_names");

    internal string[] DecodeOutputNames() => StringList(_ttsMeta, "decode_output_names");

    internal string[] LocalCachedOutputNames() => StringList(_ttsMeta, "local_cached_output_names");

    private static string[] StringList(JsonElement meta, string key) => [.. meta
        .GetProperty("onnx")
        .GetProperty(key)
        .EnumerateArray()
        .Select(item => item.GetString() ?? "")];

    public void Dispose()
    {
        foreach (InferenceSession session in _sessions.Values)
        {
            session.Dispose();
        }
    }
}
