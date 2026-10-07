using System.Buffers;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace SolSystem.Speech;

/// <summary>
/// One synthesis: text in, audio frames out, streaming to a PCM sink as they decode.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline is a faithful port of the reference ONNX orchestrator. A request is a block
/// of 17-wide rows — one text token and sixteen audio-code slots per row — prefilled at once;
/// a one-layer local model then proposes each 16-token audio frame, either inside a fused
/// sampling graph (given uniform draws) or channel by channel through a cached-step graph
/// (argmax); after each frame the global model advances one row through its KV cache. The
/// frames land in the codec's streaming decoder, which emits 48 kHz stereo for every frame
/// it is fed — 3 840 samples a frame, 12.5 frames a second.
/// </para>
/// <para>
/// <b>Determinism.</b> Greedy mode touches no random numbers anywhere, so a fixed-seed
/// regression can pin its token stream exactly. The sampled mode's randomness lives in the
/// uniform draws the fused sampling graph consumes; the reference implementation draws them
/// from numpy's PCG64, and this port draws them from its own seeded generator — the audio
/// for a given seed is therefore a property of THIS port, not of the Python one, which is
/// the right way round for a game that must sound the same every run.
/// </para>
/// </remarks>
internal sealed class MossSynthesizer : IDisposable
{
    private readonly MossModel _model;
    private readonly SentencePieceModel _tokenizer;

    private readonly int _nVq;
    private readonly int _rowWidth;
    private readonly int _audioPad;
    private readonly int _audioStart;
    private readonly int _audioEnd;
    private readonly int _userSlot;
    private readonly int _assistantSlot;

    private readonly int _localLayers;
    private readonly int _localHeads;
    private readonly int _localHeadDim;
    private readonly int _audioCodebookSize;

    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly int _numQuantizers;

    /// <summary>Samples of audio per generated frame, at the codec's rate.</summary>
    internal int SamplesPerFrame { get; private init; }

    internal int SampleRate => _sampleRate;

    internal MossSynthesizer(MossModel model, SentencePieceModel tokenizer, SpeechCache? cache = null)
    {
        _model = model;
        _tokenizer = tokenizer;
        _cache = cache;

        _nVq = model.TokenId("n_vq");
        _rowWidth = _nVq + 1;
        _audioPad = model.TokenId("audio_pad_token_id");
        _audioStart = model.TokenId("audio_start_token_id");
        _audioEnd = model.TokenId("audio_end_token_id");
        _userSlot = model.TokenId("audio_user_slot_token_id");
        _assistantSlot = model.TokenId("audio_assistant_slot_token_id");

        _localLayers = model.ModelConfig.GetProperty("local_layers").GetInt32();
        _localHeads = model.ModelConfig.GetProperty("local_heads").GetInt32();
        _localHeadDim = model.ModelConfig.GetProperty("local_head_dim").GetInt32();
        _audioCodebookSize = model.ModelConfig.GetProperty("audio_codebook_sizes")[0].GetInt32();

        _sampleRate = model.CodecConfig.GetProperty("sample_rate").GetInt32();
        _channels = model.CodecConfig.GetProperty("channels").GetInt32();
        _numQuantizers = model.CodecConfig.GetProperty("num_quantizers").GetInt32();

        // The codec's downsample_rate is the SAMPLES OF AUDIO ONE FRAME CARRIES (48 000 / 12.5
        // Hz = 3 840), not a divisor of the rate. Dividing instead produced a "frame" of
        // twelve samples: the cached plays were chopped into thousands of callbacks, and the
        // playback lead counted frames of six milliseconds where the design said two seconds.
        SamplesPerFrame = model.CodecConfig.GetProperty("downsample_rate").GetInt32();

        _codecStream = new CodecStreamingDecode(model, _numQuantizers);
    }

    private readonly SpeechCache? _cache;

    // ------------------------------------------------------------------ request rows

    private int[][] BuildTextRows(IEnumerable<int> tokenIds)
    {
        var rows = new List<int[]>();
        foreach (int tokenId in tokenIds)
        {
            var row = new int[_rowWidth];
            Array.Fill(row, _audioPad);
            row[0] = tokenId;
            rows.Add(row);
        }

        return [.. rows];
    }

    private int[][] BuildAudioPrefixRows(IReadOnlyList<List<int>> promptCodes, int slotTokenId)
    {
        var rows = new List<int[]>(promptCodes.Count);
        foreach (List<int> codeRow in promptCodes)
        {
            var row = new int[_rowWidth];
            Array.Fill(row, _audioPad);
            row[0] = slotTokenId;
            for (int index = 0; index < Math.Min(codeRow.Count, _nVq); index++)
            {
                row[index + 1] = codeRow[index];
            }

            rows.Add(row);
        }

        return [.. rows];
    }

    private (int[][] InputIds, int[][] AttentionMask) BuildRequestRows(
        IReadOnlyList<List<int>> promptCodes,
        IReadOnlyList<int> textTokenIds)
    {
        JsonElement templates = _model.PromptTemplates;

        var prefixIds = new List<int>(templates.GetProperty("user_prompt_prefix_token_ids").GetArrayLength() + 1);
        prefixIds.AddRange(templates.GetProperty("user_prompt_prefix_token_ids").EnumerateArray().Select(x => x.GetInt32()));
        prefixIds.Add(_audioStart);

        var suffixIds = new List<int> { _audioEnd };
        suffixIds.AddRange(templates.GetProperty("user_prompt_after_reference_token_ids").EnumerateArray().Select(x => x.GetInt32()));
        suffixIds.AddRange(textTokenIds);
        suffixIds.AddRange(templates.GetProperty("assistant_prompt_prefix_token_ids").EnumerateArray().Select(x => x.GetInt32()));
        suffixIds.Add(_audioStart);

        var rows = new List<int[]>();
        rows.AddRange(BuildTextRows(prefixIds));
        rows.AddRange(BuildAudioPrefixRows(promptCodes, _userSlot));
        rows.AddRange(BuildTextRows(suffixIds));

        var mask = new int[1][];
        mask[0] = new int[rows.Count];
        Array.Fill(mask[0], 1);

        return ([.. rows], mask);
    }

    // ------------------------------------------------------------------ generation

    internal delegate void AudioSink(ReadOnlySpan<float> stereoInterleaved, int samplesPerChannel);

    /// <summary>
    /// Synthesizes one chunk of text and streams decoded audio to the sink as it is produced.
    /// </summary>
    internal void SynthesizeChunk(
        string text,
        IReadOnlyList<List<int>> promptCodes,
        SynthesisSettings settings,
        AudioSink? onAudio)
    {
        List<int> textTokenIds = [.. _tokenizer.Encode(text)];
        (int[][] inputIds, int[][] mask) = BuildRequestRows(promptCodes, textTokenIds);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        (float[] globalHidden, Dictionary<string, DenseTensor<float>> past) = RunPrefill(inputIds, mask);
        double prefillMs = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        long decodeCalls = 0;
        double decodeMs = 0.0;
        double codecMs = 0.0;
        double localMs = 0.0;
        int pastValidLength = mask[0].Sum();

        var generatedFrames = new List<int[]>();
        var previousTokensByChannel = new List<int>[_nVq];
        var previousTokenSetsByChannel = new HashSet<int>[_nVq];
        for (int channel = 0; channel < _nVq; channel++)
        {
            previousTokensByChannel[channel] = [];
            previousTokenSetsByChannel[channel] = [];
        }

        // THE CODEC DECODE IS PIPELINED OFF THE FRAME LOOP. The three graphs a frame runs
        // are dispatch-bound (~2 000 nodes at ~10 µs each), not compute-bound, so the codec
        // — whose batches carry the biggest state — runs on its own thread and is fed as
        // frames are generated. The frame loop never waits on audio; the sink is called
        // from the codec thread. State stays sequential and in order; only the wall time
        // overlaps.
        _codecStream.Reset();

        var codecQueue = new System.Collections.Concurrent.ConcurrentQueue<int[]>();
        Exception? codecFailure = null;
        int drained = 0;
        var codecThread = new Thread(() =>
        {
            int emittedSamples = 0;
            try
            {
                var batch = new List<int[]>();
                while (true)
                {
                    while (codecQueue.TryDequeue(out int[]? frame))
                    {
                        batch.Add(frame);
                    }

                    if (batch.Count > 0)
                    {
                        double before = clock.Elapsed.TotalMilliseconds;
                        (float[] audio, int audioLength) = _codecStream.RunFrames(batch);
                        codecMs += clock.Elapsed.TotalMilliseconds - before;
                        batch.Clear();
                        if (audioLength > 0)
                        {
                            emittedSamples += audioLength;
                            onAudio?.Invoke(audio, audioLength);
                        }

                        continue;
                    }

                    System.Threading.SpinWait.SpinUntil(
                        () => !codecQueue.IsEmpty || System.Threading.Volatile.Read(ref drained) == 1);
                    if (System.Threading.Volatile.Read(ref drained) == 1)
                    {
                        return;
                    }
                }
            }
            catch (Exception exception)
            {
                codecFailure = exception;
            }
        })
        {
            Name = "narrator-codec",
            IsBackground = true,
        };
        codecThread.Start();

        for (int step = 0; step < settings.MaxNewFrames; step++)
        {
            SynthesisRuns++;
            int[] frame;
            if (settings.Greedy)
            {
                if (!GenerateFrameCachedStep(
                        globalHidden, previousTokensByChannel, previousTokenSetsByChannel, out frame))
                {
                    break;
                }
            }
            else
            {
                double localBefore = clock.Elapsed.TotalMilliseconds;
                (bool shouldContinue, int[] sampled) = GenerateFrameFixedSampled(
                    globalHidden, previousTokenSetsByChannel, settings.CreateRandom());
                localMs += clock.Elapsed.TotalMilliseconds - localBefore;
                if (!shouldContinue)
                {
                    break;
                }

                frame = sampled;
                for (int channel = 0; channel < _nVq; channel++)
                {
                    previousTokensByChannel[channel].Add(frame[channel]);
                    previousTokenSetsByChannel[channel].Add(frame[channel]);
                }
            }

            generatedFrames.Add(frame);
            codecQueue.Enqueue(frame);

            // One decode step advances the global model past this frame.
            var nextRow = new DenseTensor<int>([1, 1, _rowWidth]);
            for (int slot = 0; slot < _rowWidth; slot++)
            {
                nextRow[0, 0, slot] = slot == 0 ? _assistantSlot : frame[slot - 1];
            }

            var feeds = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", nextRow),
                NamedOnnxValue.CreateFromTensor(
                    "past_valid_lengths", new DenseTensor<int>(new[] { pastValidLength }, [1])),
            };
            foreach (string name in _model.DecodeInputNames().Skip(2))
            {
                feeds.Add(NamedOnnxValue.CreateFromTensor(name, past[name]));
            }

            // The KV tensors are cloned, deliberately: ORT's arena hands a later output back
            // the same memory a live input occupies, and a past tensor that aliases the
            // present behaves like a model with a corrupted history — the measured symptom
            // was speech that never ends. The clone is the honest price of feeding an
            // exported graph that takes its own output back as input.
            double decodeBefore = clock.Elapsed.TotalMilliseconds;
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
                _model.Session("decode").Run(feeds);
            decodeMs += clock.Elapsed.TotalMilliseconds - decodeBefore;
            decodeCalls++;
            globalHidden = ExtractLastHidden(AsTensor<float>(outputs, "global_hidden"));
            foreach (string outputName in _model.DecodeOutputNames().Skip(1))
            {
                string pastName = "past_" + outputName["present_".Length..];
                past[pastName] = (DenseTensor<float>)AsTensor<float>(outputs, outputName).Clone();
            }

            pastValidLength += 1;
        }

        // Flush: release the codec thread, wait for the last batch, and surface its failure.
        System.Threading.Volatile.Write(ref drained, 1);
        codecThread.Join();
        if (codecFailure is not null)
        {
            throw codecFailure;
        }

        double frameLoopMs = clock.Elapsed.TotalMilliseconds;
        LastStageReport = (prefillMs, frameLoopMs, decodeMs, codecMs, localMs);
        _lastChunkFrames = generatedFrames.Count;
        _codecStream.Reset();
    }

    /// <summary>
    /// Synthesizes a whole text: normalised, chunked to the token budget, with the
    /// reference pauses between chunks, streamed chunk by chunk. A prompt path clones a
    /// voice; a name picks a built-in preset. The prompt is encoded once per session and
    /// cached — a voice does not re-tune mid-run. When a cache is present, a line already
    /// rendered is played from its WAV and no synthesis runs; a line not in the cache is
    /// rendered, written to disk and mapped in.
    /// </summary>
    internal void Synthesize(
        string text,
        SynthesisSettings settings,
        AudioSink? onAudio)
    {
        IReadOnlyList<List<int>> promptCodes = PromptCodes(settings);
        string prepared = TtsNormalizer.Normalize(text);

        if (_cache is not null)
        {
            string key = SpeechCache.Key(_sampleRate, _channels, VoiceSource(settings, promptCodes),
                settings.Seed, settings.Greedy, prepared);
            if (_cache.TryGet(key, _sampleRate, _channels, out byte[] cachedPcm, out _))
            {
                SinkCachedPcm(cachedPcm, onAudio);
                return;
            }

            var rendered = new System.Collections.Generic.List<float>();
            void CapturingSink(ReadOnlySpan<float> chunk, int samplesPerChannel)
            {
                rendered.AddRange(chunk[..(samplesPerChannel * _channels)]);
                onAudio?.Invoke(chunk, samplesPerChannel);
            }

            int frames = 0;
            foreach (string chunk in ChunkText(prepared, settings.ChunkTokenBudget))
            {
                frames += SynthesizeChunkCounting(chunk, promptCodes, settings, CapturingSink);
            }

            _cache.Put(key, text, prepared, settings.Voice, VoiceSource(settings, promptCodes),
                settings.Seed, settings.Greedy, _sampleRate, _channels, frames, [.. rendered]);
            return;
        }

        foreach (string chunk in ChunkText(prepared, settings.ChunkTokenBudget))
        {
            SynthesizeChunk(chunk, promptCodes, settings, onAudio);
        }
    }

    /// <summary>A run's frame total, for the cache row.</summary>
    private int SynthesizeChunkCounting(string chunk, IReadOnlyList<List<int>> promptCodes,
        SynthesisSettings settings, AudioSink? onAudio)
    {
        int before = _lastChunkFrames;
        SynthesizeChunk(chunk, promptCodes, settings, onAudio);
        return _lastChunkFrames - before;
    }

    private int _lastChunkFrames;

    /// <summary>How many audio frames were actually synthesized since the runtime was created —
    /// zero while every line played from cache.</summary>
    internal long SynthesisRuns { get; private set; }

    /// <summary>
    /// Feeds a cached line to the sink in single-frame chunks, so a cached play takes the
    /// same path through the sink's pacing as a rendered one. The cache's WAVs are IEEE
    /// float32, so the bytes go straight over: no requantisation on the way back.
    /// </summary>
    private void SinkCachedPcm(byte[] floatBytes, AudioSink? onAudio)
    {
        if (onAudio is null)
        {
            return;
        }

        int samples = floatBytes.Length / (4 * _channels);
        var floats = new float[samples * _channels];
        for (int index = 0; index < floats.Length; index++)
        {
            floats[index] = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(
                floatBytes.AsSpan(index * 4, 4));
        }

        int frames = samples / SamplesPerFrame;
        for (int frame = 0; frame < frames; frame++)
        {
            int offset = frame * SamplesPerFrame * _channels;
            onAudio(floats.AsSpan(offset, SamplesPerFrame * _channels), SamplesPerFrame);
        }
    }

    /// <summary>
    /// The voice's identity as the cache keys it: the preset's name, or the hash of a
    /// clone's codes — so a re-cut prompt is a different voice even at the same path.
    /// </summary>
    private static string VoiceSource(SynthesisSettings settings, IReadOnlyList<List<int>> promptCodes)
    {
        if (settings.PromptAudioPath is null)
        {
            return $"preset:{settings.Voice}";
        }

        var bytes = new List<byte>(promptCodes.Count * _staticQuantizerEstimate);
        foreach (List<int> row in promptCodes)
        {
            foreach (int code in row)
            {
                bytes.Add((byte)(code & 0xFF));
                bytes.Add((byte)((code >> 8) & 0xFF));
            }
        }

        return "codes:" + Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData([.. bytes]));
    }

    private const int _staticQuantizerEstimate = 16;

    private IReadOnlyList<List<int>> PromptCodes(SynthesisSettings settings)
    {
        if (settings.PromptAudioPath is not string path)
        {
            return _model.PromptCodesFor(settings.Voice);
        }

        // A clone's cost is paid once per recording: the sidecar beside the prompt holds
        // the codes, validated against the recording's hash, so the encoder graph runs
        // only when the recording changed since they were made.
        string sidecar = path + ".codes";
        byte[] sourceHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
        if (PromptCodesFile.TryLoad(sidecar, sourceHash, _sampleRate, _channels, _numQuantizers,
                out List<List<int>>? loaded))
        {
            return loaded;
        }

        List<List<int>> encoded = EncodeReferenceAudio(path);
        PromptCodesFile.Save(sidecar, path, _sampleRate, _channels, _numQuantizers, encoded);
        return encoded;
    }

    /// <summary>
    /// The audio tokenizer's encoder: a reference recording in, its codes out — the codes a
    /// cloned voice is carried by.
    /// </summary>
    internal List<List<int>> EncodeReferenceAudio(string path)
    {
        (float[] waveform, int samples) = AudioPrompt.Prepare(path, _sampleRate, _channels);
        var tensor = new DenseTensor<float>(waveform, [1, _channels, samples]);
        var feeds = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("waveform", tensor),
            NamedOnnxValue.CreateFromTensor(
                "input_lengths", new DenseTensor<int>(new[] { samples }, [1])),
        };

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _model.Session("codec_encode").Run(feeds);
        DenseTensor<int> codes = (DenseTensor<int>)AsTensor<int>(outputs, "audio_codes");
        int codeLength = AsTensor<int>(outputs, "audio_code_lengths")[0];

        var rows = new List<List<int>>(codeLength);
        for (int frame = 0; frame < codeLength; frame++)
        {
            var row = new List<int>(_numQuantizers);
            for (int quantizer = 0; quantizer < _numQuantizers; quantizer++)
            {
                row.Add(codes[0, frame, quantizer]);
            }

            rows.Add(row);
        }

        return [.. rows];
    }

    /// <summary>Decodes already-generated frames in one full pass. For the regression tests.</summary>
    internal (float[] Audio, int SamplesPerChannel) DecodeFull(IReadOnlyList<int[]> frames)
    {
        int[][][] codes = [frames.Select(f => f.Take(_numQuantizers).ToArray()).ToArray()];
        var flat = new DenseTensor<int>([1, frames.Count, _numQuantizers]);
        for (int frame = 0; frame < frames.Count; frame++)
        {
            for (int quantizer = 0; quantizer < _numQuantizers; quantizer++)
            {
                flat[0, frame, quantizer] = codes[0][frame][quantizer];
            }
        }

        var lengths = new DenseTensor<int>(new[] { frames.Count }, [1]);
        var feeds = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("audio_codes", flat),
            NamedOnnxValue.CreateFromTensor("audio_code_lengths", lengths),
        };

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _model.Session("codec_decode").Run(feeds);
        DenseTensor<float> audio = (DenseTensor<float>)AsTensor<float>(outputs, "audio");
        int audioLength = AsTensor<int>(outputs, "audio_lengths")[0];
        return (audio.Buffer.ToArray(), audioLength);
    }

    /// <summary>How long each stage of the last chunk has spent, for the bench mode.</summary>
    internal (double PrefillMs, double FrameLoopMs, double DecodeMs, double CodecMs, double LocalMs)
        LastStageReport { get; private set; }

    // ------------------------------------------------------------------ the graphs

    private (float[] Hidden, Dictionary<string, DenseTensor<float>> Past) RunPrefill(
        int[][] inputIds,
        int[][] attentionMask)
    {
        int rows = inputIds.Length;
        var ids = new DenseTensor<int>([1, rows, _rowWidth]);
        for (int row = 0; row < rows; row++)
        {
            for (int slot = 0; slot < _rowWidth; slot++)
            {
                ids[0, row, slot] = inputIds[row][slot];
            }
        }

        var mask = new DenseTensor<int>([1, rows]);
        for (int row = 0; row < rows; row++)
        {
            mask[0, row] = attentionMask[0][row];
        }

        var feeds = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", ids),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask),
        };

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _model.Session("prefill").Run(feeds);
        DenseTensor<float> hidden = (DenseTensor<float>)AsTensor<float>(outputs, "global_hidden");
        int hiddenDim = hidden.Dimensions[^1];
        var last = new float[hiddenDim];
        int lastRow = hidden.Dimensions[1] - 1;
        for (int index = 0; index < hiddenDim; index++)
        {
            last[index] = hidden[0, lastRow, index];
        }

        var past = new Dictionary<string, DenseTensor<float>>(StringComparer.Ordinal);
        foreach (string outputName in _model.PrefillOutputNames().Skip(1))
        {
            string pastName = "past_" + outputName["present_".Length..];
            past[pastName] = (DenseTensor<float>)AsTensor<float>(outputs, outputName).Clone();
        }

        return (last, past);
    }

    /// <summary>The fused sampling graph: one call per frame, uniforms in, tokens out.</summary>
    private (bool ShouldContinue, int[] Frame) GenerateFrameFixedSampled(
        float[] globalHidden,
        IReadOnlyList<HashSet<int>> previousTokenSetsByChannel,
        Random random)
    {
        var mask = new DenseTensor<int>([1, _nVq, _audioCodebookSize]);
        for (int channel = 0; channel < _nVq; channel++)
        {
            foreach (int tokenId in previousTokenSetsByChannel[channel])
            {
                if (tokenId >= 0 && tokenId < _audioCodebookSize)
                {
                    mask[0, channel, tokenId] = 1;
                }
            }
        }

        float[] assistantU = [ClampUniform(random.NextDouble())];
        var audioU = new DenseTensor<float>([1, _nVq]);
        for (int channel = 0; channel < _nVq; channel++)
        {
            audioU[0, channel] = ClampUniform(random.NextDouble());
        }

        var feeds = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("global_hidden", new DenseTensor<float>(globalHidden, [1, globalHidden.Length])),
            NamedOnnxValue.CreateFromTensor("repetition_seen_mask", mask),
            NamedOnnxValue.CreateFromTensor("assistant_random_u", new DenseTensor<float>(assistantU, [1])),
            NamedOnnxValue.CreateFromTensor("audio_random_u", audioU),
        };

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _model
            .Session("local_fixed_sampled_frame")
            .Run(feeds);
        int[] frame = [.. ((DenseTensor<int>)AsTensor<int>(outputs, "frame_token_ids")).Buffer.ToArray()];
        bool shouldContinue = AsTensor<int>(outputs, "should_continue")[0] != 0;
        return (shouldContinue, frame);
    }

    /// <summary>
    /// The channel-by-channel path, which is also the greedy path: the local model runs a
    /// cached step per channel and the host takes the argmax. No random numbers anywhere.
    /// </summary>
    private bool GenerateFrameCachedStep(
        float[] globalHidden,
        IReadOnlyList<List<int>> previousTokensByChannel,
        IReadOnlyList<HashSet<int>> previousTokenSetsByChannel,
        out int[] frame)
    {
        frame = new int[_nVq];
        Dictionary<string, DenseTensor<float>> localPast = EmptyLocalPast();
        int localPastValidLength = 0;

        // Step 0: seed the local cache from the global hidden, then read the assistant gate.
        (float[] textLogits, _, localPast) = RunLocalCachedStep(
            globalHidden, textTokenId: 0, audioTokenId: 0, channelIndex: 0, stepType: 0,
            localPastValidLength, localPast);
        localPastValidLength += 1;

        int nextTextToken = SampleAssistantTextToken(textLogits, previousTokenSetsByChannel.Count > 0
            ? []
            : []);
        if (nextTextToken != _assistantSlot)
        {
            return false;
        }

        // Step 1: the assistant slot is in; the first audio channel's logits are live.
        (_, DenseTensor<float> audioLogits, localPast) = RunLocalCachedStep(
            globalHidden, textTokenId: nextTextToken, audioTokenId: 0, channelIndex: 0, stepType: 1,
            localPastValidLength, localPast);
        localPastValidLength += 1;

        int previousToken = SampleAudioToken(audioLogits, 0, previousTokensByChannel, previousTokenSetsByChannel);
        frame[0] = previousToken;
        previousTokensByChannel[0].Add(previousToken);
        previousTokenSetsByChannel[0].Add(previousToken);

        for (int channel = 1; channel < _nVq; channel++)
        {
            (_, audioLogits, localPast) = RunLocalCachedStep(
                globalHidden, textTokenId: 0, audioTokenId: previousToken, channelIndex: channel - 1,
                stepType: 2, localPastValidLength, localPast);
            localPastValidLength += 1;

            previousToken = SampleAudioToken(audioLogits, channel, previousTokensByChannel, previousTokenSetsByChannel);
            frame[channel] = previousToken;
            previousTokensByChannel[channel].Add(previousToken);
            previousTokenSetsByChannel[channel].Add(previousToken);
        }

        return true;
    }

    private (float[] TextLogits, DenseTensor<float> AudioLogits, Dictionary<string, DenseTensor<float>> NextPast)
        RunLocalCachedStep(
            float[] globalHidden,
            int textTokenId,
            int audioTokenId,
            int channelIndex,
            int stepType,
            int localPastValidLength,
            Dictionary<string, DenseTensor<float>> localPast)
    {
        var feeds = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("global_hidden", new DenseTensor<float>(globalHidden, [1, globalHidden.Length])),
            NamedOnnxValue.CreateFromTensor("text_token_id", new DenseTensor<int>(new[] { textTokenId }, [1])),
            NamedOnnxValue.CreateFromTensor("audio_token_id", new DenseTensor<int>(new[] { audioTokenId }, [1])),
            NamedOnnxValue.CreateFromTensor("channel_index", new DenseTensor<int>(new[] { channelIndex }, [1])),
            NamedOnnxValue.CreateFromTensor("step_type", new DenseTensor<int>(new[] { stepType }, [1])),
            NamedOnnxValue.CreateFromTensor("past_valid_lengths", new DenseTensor<int>(new[] { localPastValidLength }, [1])),
        };
        foreach ((string name, DenseTensor<float> tensor) in localPast)
        {
            feeds.Add(NamedOnnxValue.CreateFromTensor(name, tensor));
        }

        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _model
            .Session("local_cached_step")
            .Run(feeds);

        float[] textLogits = [.. ((DenseTensor<float>)AsTensor<float>(outputs, "text_logits")).Buffer.ToArray()];
        DenseTensor<float> audioLogits = (DenseTensor<float>)AsTensor<float>(outputs, "audio_logits").Clone();

        var nextPast = new Dictionary<string, DenseTensor<float>>(StringComparer.Ordinal);
        foreach (string outputName in _model.LocalCachedOutputNames().Skip(2))
        {
            string pastName = "local_past_" + outputName["local_present_".Length..];
            nextPast[pastName] = (DenseTensor<float>)AsTensor<float>(outputs, outputName).Clone();
        }

        return (textLogits, audioLogits, nextPast);
    }

    private Dictionary<string, DenseTensor<float>> EmptyLocalPast()
    {
        var result = new Dictionary<string, DenseTensor<float>>(StringComparer.Ordinal);
        for (int layer = 0; layer < _localLayers; layer++)
        {
            result[$"local_past_key_{layer}"] = new DenseTensor<float>([1, 0, _localHeads, _localHeadDim]);
            result[$"local_past_value_{layer}"] = new DenseTensor<float>([1, 0, _localHeads, _localHeadDim]);
        }

        return result;
    }

    /// <summary>Greedy gate between the assistant slot and the end of audio.</summary>
    private int SampleAssistantTextToken(float[] textLogits, HashSet<int> _) =>
        textLogits[_assistantSlot] >= textLogits[_audioEnd] ? _assistantSlot : _audioEnd;

    private int SampleAudioToken(
        DenseTensor<float> audioLogits,
        int channel,
        IReadOnlyList<List<int>> previousTokensByChannel,
        IReadOnlyList<HashSet<int>> previousTokenSetsByChannel)
    {
        // The logits tensor is flat across channels; slice this channel's span.
        int perChannel = audioLogits.Dimensions[^1];
        float[] channelLogits = new float[perChannel];
        int start = channel * perChannel;
        audioLogits.Buffer.Span.Slice(start, perChannel).CopyTo(channelLogits);

        // Argmax with the repetition penalty the reference applies on the greedy path.
        float best = float.NegativeInfinity;
        int bestIndex = 0;
        bool penalize = previousTokenSetsByChannel[channel].Count > 0;
        for (int index = 0; index < perChannel; index++)
        {
            float score = channelLogits[index];
            if (penalize && previousTokenSetsByChannel[channel].Contains(index))
            {
                score = score < 0 ? score * 1.2f : score / 1.2f;
            }

            if (score > best)
            {
                best = score;
                bestIndex = index;
            }
        }

        return bestIndex;
    }

    /// <summary>
    private static float ClampUniform(double value) =>
        (float)Math.Clamp(value, 0.0, 0.99999994);

    private static Tensor<T> AsTensor<T>(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs, string name)
    {
        foreach (DisposableNamedOnnxValue value in outputs)
        {
            if (value.Name == name && value.Value is Tensor<T> tensor)
            {
                return tensor;
            }
        }

        throw new InvalidOperationException($"the graph produced no '{name}' tensor");
    }

    private static float[] ExtractLastHidden(Tensor<float> hidden)
    {
        int rows = hidden.Dimensions[1];
        int width = hidden.Dimensions[2];
        var result = new float[width];
        int last = rows - 1;
        for (int index = 0; index < width; index++)
        {
            result[index] = hidden[0, last, index];
        }

        return result;
    }

    // ------------------------------------------------------------------ chunking

    /// <summary>
    /// Splits text into synthesis chunks by sentence, then clause, then a binary search on a
    /// token budget — the reference's voice-clone chunking, minus the parts its callers
    /// disable. Narration lines are authored, so the budget is generous and the splits rare.
    /// </summary>
    internal List<string> ChunkText(string text, int maxTokens)
    {
        string prepared = PrepareTextForChunking(text);
        if (_tokenizer.Encode(prepared).Length <= maxTokens)
        {
            return [prepared];
        }

        var sentences = SplitByPunctuation(prepared, ".!?；;");
        var chunks = new List<string>();
        foreach (string sentence in sentences)
        {
            if (_tokenizer.Encode(sentence).Length <= maxTokens)
            {
                chunks.Add(sentence);
                continue;
            }

            foreach (string clause in SplitByPunctuation(sentence, ",，、;：:"))
            {
                chunks.AddRange(SplitByTokenBudget(clause, maxTokens));
            }
        }

        return chunks;
    }

    private List<string> SplitByTokenBudget(string text, int maxTokens)
    {
        string remaining = text.Trim();
        var pieces = new List<string>();
        const string boundaryChars = ",;: .!?";
        while (remaining.Length > 0)
        {
            if (_tokenizer.Encode(remaining).Length <= maxTokens)
            {
                pieces.Add(remaining);
                break;
            }

            int low = 1;
            int high = remaining.Length;
            int bestLength = 1;
            while (low <= high)
            {
                int middle = (low + high) / 2;
                string candidate = remaining[..middle].Trim();
                if (candidate.Length == 0 || _tokenizer.Encode(candidate).Length <= maxTokens)
                {
                    bestLength = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            int cut = bestLength;
            for (int index = Math.Min(bestLength, remaining.Length) - 1; index > Math.Max(-1, bestLength - 25); index--)
            {
                if (boundaryChars.Contains(remaining[index]))
                {
                    cut = index + 1;
                    break;
                }
            }

            pieces.Add(remaining[..cut].Trim());
            remaining = remaining[cut..].Trim();
        }

        return pieces;
    }

    /// <summary>The reference's prompt preparation: trimmed, spaced, sentence-ended, short lines padded.</summary>
    internal static string PrepareTextForChunking(string text)
    {
        string prepared = text.Trim();
        if (prepared.Length == 0)
        {
            throw new ArgumentException("Narration text cannot be empty.", nameof(text));
        }

        prepared = prepared.Replace("\r", " ").Replace("\n", " ");
        while (prepared.Contains("  "))
        {
            prepared = prepared.Replace("  ", " ");
        }

        if (char.IsAsciiLetterOrDigit(prepared[^1]))
        {
            prepared += ".";
        }

        if (prepared.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 5)
        {
            prepared = "        " + prepared;
        }

        return prepared;
    }

    private static List<string> SplitByPunctuation(string text, string punctuation)
    {
        var sentences = new List<string>();
        var current = new System.Text.StringBuilder();
        int index = 0;
        while (index < text.Length)
        {
            char character = text[index];
            current.Append(character);
            if (punctuation.Contains(character))
            {
                string sentence = current.ToString().Trim();
                if (sentence.Length > 0)
                {
                    sentences.Add(sentence);
                }

                current.Clear();
                index++;
                while (index < text.Length && text[index] == ' ')
                {
                    index++;
                }

                continue;
            }

            index++;
        }

        string tail = current.ToString().Trim();
        if (tail.Length > 0)
        {
            sentences.Add(tail);
        }

        return sentences;
    }

    private readonly CodecStreamingDecode _codecStream;

    public void Dispose()
    {
        // The codec's session is owned by the model; only the local state dies here.
        _codecStream.Reset();
    }
}

/// <summary>The knobs a synthesis run is set by.</summary>
internal sealed class SynthesisSettings
{
    internal required string Voice { get; init; } = "Adam";

    /// <summary>
    /// A reference recording to clone, which overrides the built-in voice when set.
    /// </summary>
    internal string? PromptAudioPath { get; init; }

    /// <summary>True takes the argmax path everywhere; the narration defaults to sampled.</summary>
    internal bool Greedy { get; init; }

    /// <summary>The seed of THIS port's uniform draws. Same seed, same audio, every run.</summary>
    internal int Seed { get; init; } = 1234;

    internal int MaxNewFrames { get; init; } = 375;

    internal int ChunkTokenBudget { get; init; } = 75;

    /// <summary>A fresh generator for one chunk: same seed, same sequence, every run.</summary>
    internal Random CreateRandom() => new(Seed);
}
