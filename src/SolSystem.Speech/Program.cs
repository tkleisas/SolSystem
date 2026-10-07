using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace SolSystem.Speech;

/// <summary>
/// The speech tool's own entry point: a self-test against the manifest's reference ids,
/// a synthesis CLI, and a listenable WAV out the other end.
/// </summary>
/// <remarks>
/// <para>
/// The model bundle lives under <c>artifacts/moss-tts/</c> (never in git; fetched once).
/// <c>dotnet run --project src/SolSystem.Speech -- --help</c> for the surface.
/// </para>
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine(Usage);
                return 0;
            }

            string modelRoot = FindModelDir(args);

            if (args.Contains("--self-test"))
            {
                return SelfTest(modelRoot);
            }

            if (args.Contains("--bench"))
            {
                return Bench(modelRoot, args);
            }

            if (args.Contains("--voices"))
            {
                return ListVoices(modelRoot);
            }

            if (args.Contains("--tokenizer"))
            {
                return InspectTokenizer(modelRoot, args);
            }

            int textIndex = Array.IndexOf(args, "--text");
            if (textIndex < 0 || textIndex + 1 >= args.Length)
            {
                Console.Error.WriteLine("no --text given; nothing to say");
                return 2;
            }

            return Synthesize(args, modelRoot, text: args[textIndex + 1]);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"speech failed: {exception.Message}");
            if (args.Contains("--verbose"))
            {
                Console.Error.WriteLine(exception);
            }

            return 3;
        }
    }

    private static string TakeValue(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null!;
    }

    private static string FindModelDir(string[] args)
    {
        string? given = TakeValue(args, "--model-dir");
        if (given is not null)
        {
            return given;
        }

        // Walk up from the executable and the working directory for artifacts/moss-tts.
        string? found = MossModel.FindDeployment();
        if (found is not null)
        {
            return found;
        }

        throw new DirectoryNotFoundException(
            "the MOSS-TTS-Nano ONNX bundle was not found under artifacts/moss-tts. "
            + "Fetch it once with tools/tts/fetch_models.sh, or pass --model-dir.");
    }

    private static int InspectTokenizer(string modelRoot, string[] args)
    {
        SentencePieceModel tokenizer = SentencePieceModel.Load(Path.Combine(
            modelRoot, MossModel.TtsDirectoryName, "tokenizer.model"));
        Console.WriteLine($"vocabulary: {tokenizer.VocabularySize} pieces");
        Console.WriteLine("  first pieces, as bytes:");
        foreach (string piece in tokenizer.FirstPieces(6))
        {
            Console.WriteLine($"    [{string.Join(' ', System.Text.Encoding.UTF8.GetBytes(piece).Select(b => $"{b:X2}"))}] = \"{piece}\"");
        }
        string text = TakeValue(args, "--text") ?? "Hello.";
        int[] ids = tokenizer.Encode(text);
        Console.WriteLine($"  {text} -> {ids.Length} tokens: {string.Join(' ', ids.Take(40))}");
        foreach (string probe in new[] { "▁estab", "l", "ished", "▁e", "st", "ab", "li", "sh", "ed", "▁established" })
        {
            int id = tokenizer.IdOf(probe);
            Console.WriteLine($"    {probe!} id={id} score={(id >= 0 ? tokenizer.ScoreOf(id) : 0f)}");
        }
        return 0;    }

    private static int ListVoices(string modelRoot)
    {
        using MossModel model = MossModel.Load(modelRoot);
        Console.WriteLine("built-in voices:");
        foreach (string name in model.VoiceNames())
        {
            Console.WriteLine($"  {name}");
        }

        return 0;
    }

    private static int Synthesize(string[] args, string modelRoot, string text)
    {
            string outPath = TakeValue(args, "--out") ?? "generated_audio/speech_output.wav";
            string voice = TakeValue(args, "--voice") ?? "Adam";
            string? prompt = TakeValue(args, "--prompt");
            bool greedy = args.Contains("--greedy");
            int seed = int.TryParse(TakeValue(args, "--seed"), out int parsed) ? parsed : 1234;
            int threads = int.TryParse(TakeValue(args, "--threads"), out int parsedThreads) ? parsedThreads : 4;

        using MossModel model = MossModel.Load(modelRoot, threads);
        SentencePieceModel tokenizer = SentencePieceModel.Load(Path.Combine(
            modelRoot, MossModel.TtsDirectoryName, "tokenizer.model"));
        using SpeechCache? cache = SpeechCache.Open(
            TakeValue(args, "--cache-db") ?? SpeechCache.DefaultDatabasePath());
        using MossSynthesizer synthesizer = new(model, tokenizer, cache);
        var settings = new SynthesisSettings
        {
            Voice = voice, PromptAudioPath = prompt, Greedy = greedy, Seed = seed,
        };

        var pcm = new System.Collections.Generic.List<float>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        synthesizer.Synthesize(text, settings, (chunk, samplesPerChannel) =>
        {
            pcm.AddRange(chunk[..(samplesPerChannel * 2)]);
            double generated = samplesPerChannel / (double)synthesizer.SampleRate;
            Console.WriteLine($"  decoded {samplesPerChannel,6} samples ({generated:F2} s) at "
                + $"{clock.Elapsed.TotalMilliseconds,7:F0} ms");
        });
        clock.Stop();

        (double prefillMs, double frameLoopMs, double decodeMs, double codecMs, double localMs) =
            synthesizer.LastStageReport;
        Console.WriteLine($"  stages: prefill {prefillMs:F0} ms, frame loop {frameLoopMs:F0} ms "
            + $"(global decode {decodeMs:F0}, codec {codecMs:F0}, local frame {localMs:F0})");

        float[] interleaved = [.. pcm];
        int frames = interleaved.Length / 2;
        Wav.Write(outPath, interleaved, frames, synthesizer.SampleRate, 2);
        Console.WriteLine($"wrote {outPath}: {frames / (double)synthesizer.SampleRate:F2} s of audio "
            + $"in {clock.Elapsed.TotalSeconds:F2} s");

        string hash = Convert.ToHexStringLower(SHA256.HashData(
            interleaved.SelectMany(BitConverter.GetBytes).ToArray()));
        Console.WriteLine($"  pcm hash (float32): {hash[..32]}");
        return 0;
    }

    /// <summary>
    /// Runs the smallest graph repeatedly, with ORT profiling on, so a flat per-call cost
    /// names its owner in the profile rather than staying a mystery.
    /// </summary>
    private static int Bench(string modelRoot, string[] args)
    {
        int threads = int.TryParse(TakeValue(args, "--threads"), out int parsed) ? parsed : 3;
        string profilePrefix = TakeValue(args, "--profile") ?? "generated_audio/ort_bench";

        string ttsDir = Path.Combine(modelRoot, MossModel.TtsDirectoryName);
        string localPath = Path.Combine(ttsDir, "moss_tts_local_fixed_sampled_frame.onnx");

        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = threads,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        };

        // The benches kept suggesting a per-call constant far above the kernels' own time;
        // the threadpool's spin-wait between tiny nodes is the usual suspect.
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        if (args.Contains("--profile"))
        {
            // ORT's per-node profile logging roughly doubles the run's cost, so it is only
            // on when asked for, and the number printed with it is not comparable.
            options.EnableProfiling = true;
        }
        using InferenceSession local = new(localPath, options);

        var hidden = new DenseTensor<float>(new float[768], [1, 768]);
        var mask = new DenseTensor<int>([1, 16, 1024]);
        var u1 = new DenseTensor<float>(new float[1], [1]);
        var uAudio = new DenseTensor<float>(new float[16], [1, 16]);
        var feeds = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("global_hidden", hidden),
            NamedOnnxValue.CreateFromTensor("repetition_seen_mask", mask),
            NamedOnnxValue.CreateFromTensor("assistant_random_u", u1),
            NamedOnnxValue.CreateFromTensor("audio_random_u", uAudio),
        };

        // The first call pays for the plan, the arena and the memory pattern.
        using (local.Run(feeds)) { }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        const int localRuns = 100;
        for (int i = 0; i < localRuns; i++)
        {
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = local.Run(feeds);
        }

        clock.Stop();
        string profileJson = local.EndProfiling();
        string profilePath = $"{profilePrefix}.json";
        File.WriteAllText(profilePath, profileJson);
        Console.WriteLine($"local frame graph: {clock.Elapsed.TotalMilliseconds / localRuns:F2} ms per call "
            + $"({localRuns} runs, fixed shapes, profile: {profilePath})");

        return 0;
    }

    private static int SelfTest(string modelRoot)
    {
        Console.WriteLine("self-test");

        // 1. The tokenizer, against the manifest's own reference ids.
        string ttsDir = Path.Combine(modelRoot, MossModel.TtsDirectoryName);
        var manifest = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(ttsDir, "browser_poc_manifest.json"))).RootElement;
        SentencePieceModel tokenizer = SentencePieceModel.Load(Path.Combine(ttsDir, "tokenizer.model"));
        Console.WriteLine($"  vocabulary: {tokenizer.VocabularySize} pieces");

        int failures = 0;
        foreach (var sample in manifest.GetProperty("text_samples").EnumerateArray())
        {
            string text = sample.GetProperty("text").GetString()!;
            int[] expected = [.. sample.GetProperty("text_token_ids").EnumerateArray().Select(x => x.GetInt32())];
            int[] actual = tokenizer.Encode(text);
            bool same = expected.SequenceEqual(actual);
            Console.WriteLine($"  {sample.GetProperty("id").GetString()}: {actual.Length} tokens, "
                + $"{(same ? "ids match" : "IDS DIFFER")}");
            if (!same)
            {
                failures++;
                Console.WriteLine($"    expected {string.Join(' ', expected.Take(20))}...");
                Console.WriteLine($"    actual   {string.Join(' ', actual.Take(20))}...");
            }
        }

        // 2. The normaliser, against the reference's own ASCII cases.
        var cases = new (string Name, string Input, string Expected)[]
        {
            ("english_spaces", "This   is   a   test.", "This is a test."),
            ("underscore_plain_1", "foo_bar", "foo bar。"),
            ("underscore_protected_mention", "关注@foo_bar", "关注 @foo_bar。"),
            ("flow_arrow_chain", "请求接入 -> 身份判定 -> 域服务处理", "请求接入，身份判定，域服务处理。"),
        };

        int normalizerFailures = 0;
        foreach (var (name, input, expected) in cases)
        {
            if (expected is null)
            {
                continue;
            }

            string actual = TtsNormalizer.Normalize(input);
            bool ok = actual == expected;
            Console.WriteLine($"  normaliser {name}: {(ok ? "match" : $"DIFFER -> {actual}")}");
            if (!ok)
            {
                normalizerFailures++;
            }
        }

        failures += normalizerFailures;

        // 2. The model loads and the graphs report their wire format.
        using MossModel model = MossModel.Load(modelRoot);
        Console.WriteLine("  sessions: 8 graphs loaded");
        Console.WriteLine($"  decode takes {_modelDecodeInputs(model)} inputs; "
            + $"prefill yields {_modelPrefillOutputs(model)} KV tensors");
        foreach (var (name, meta) in model.Session("decode").InputMetadata)
        {
            if (name.StartsWith("past_key_0") || name.StartsWith("past_value_0") || name == "input_ids")
            {
                Console.WriteLine($"    {name}: [{string.Join(", ", meta.Dimensions)}] {meta.ElementType.Name}");
            }
        }

        // 3. A short greedy synthesis: deterministic, no RNG, and hashable.
        using var synthesizer = new MossSynthesizer(model, tokenizer);
        var settings = new SynthesisSettings { Voice = "Adam", Greedy = true, MaxNewFrames = 24 };
        var pcm = new List<float>();
        synthesizer.Synthesize("Meridian.", settings, (chunk, samplesPerChannel) =>
            pcm.AddRange(chunk[..(samplesPerChannel * 2)]));

        byte[] bytes = pcm.SelectMany(BitConverter.GetBytes).ToArray();
        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Console.WriteLine($"  greedy PCM hash: {hash}");
        // 4. The voice cache, end to end: render, then play from file with no synthesis.
        string cacheDb = Path.Combine("generated_audio", "selftest-voice.db");
        using (SpeechCache selfCache = SpeechCache.Open(cacheDb)!)
        using (MossSynthesizer cached = new(model, tokenizer, selfCache))
        {
            var cacheSettings = new SynthesisSettings { Voice = "Adam", Greedy = true, MaxNewFrames = 16 };
            cached.Synthesize("The corridor is yours.", cacheSettings, null);
            long runsAfterFirst = cached.SynthesisRuns;
            float[]? firstPcm = null;
            cached.Synthesize("The corridor is yours.", cacheSettings, (chunk, samplesPerChannel) =>
                firstPcm = [.. chunk[..(samplesPerChannel * 2)].ToArray()]);

            bool cachedPlay = cached.SynthesisRuns == runsAfterFirst;
            Console.WriteLine($"  cache: second play synthesized nothing: {cachedPlay}, "
                + $"{firstPcm?.Length ?? 0} floats");
            if (!cachedPlay)
            {
                failures++;
            }
        }

        Console.WriteLine(failures == 0 ? "self-test passed" : "self-test FAILED");
        return failures == 0 ? 0 : 1;
    }

    private static int _modelDecodeInputs(MossModel model) => model.DecodeInputNames().Length;

    private static int _modelPrefillOutputs(MossModel model) => model.PrefillOutputNames().Length;

    private const string Usage = """
        SolSystem.Speech — the MOSS-TTS-Nano ONNX runtime, in C#

          --self-test            tokenizer against manifest ids, then a greedy synth hash
          --voices               list the built-in voice presets
          --text "..."           synthesize this text
          --voice <name>         built-in voice (default Adam)
          --prompt <file.wav>    clone a voice from this 16-bit PCM recording
          --out <file.wav>       where to write (default generated_audio/speech_output.wav)
          --seed <n>             seed of the port's own sampling (default 1234)
          --greedy               argmax everywhere: no sampling, for regression checks
          --threads <n>          onnxruntime intra-op threads (default 4)
          --model-dir <dir>      where the bundle lives (default: artifacts/moss-tts, found upward)

        The bundle is ~770 MB and is fetched once:
          tools/tts/fetch_models.sh
        """;
}
