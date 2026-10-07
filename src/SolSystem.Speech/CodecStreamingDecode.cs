using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace SolSystem.Speech;

/// <summary>
/// The audio tokenizer's streaming decoder: audio codes in, 48 kHz stereo out, one batch of
/// frames at a time, with its own state carried between calls.
/// </summary>
/// <remarks>
/// The decoder is a causal transformer with attention caches, exported with its state as
/// plain input/output tensors: integer offset counters, float32 key/value caches, and a
/// positions tensor initialised to −1. The shapes and names come from the codec meta's
/// <c>streaming_decode</c> section, so a re-export needs no change here. Feeding it frames
/// advances the state; <see cref="Reset"/> rewinds it for the next utterance.
/// </remarks>
internal sealed class CodecStreamingDecode
{
    private readonly InferenceSession _session;
    private readonly int _numQuantizers;

    private readonly List<(string Input, string Output, int[] Shape)> _transformerStates = [];
    private readonly List<AttentionCacheSpec> _attentionCaches = [];
    private Dictionary<string, DenseTensor<float>> _floatState = new(StringComparer.Ordinal);
    private Dictionary<string, DenseTensor<int>> _intState = new(StringComparer.Ordinal);

    public CodecStreamingDecode(MossModel model, int numQuantizers)
    {
        _session = model.Session("codec_decode_step");
        _numQuantizers = numQuantizers;

        JsonElement streaming = model.CodecMeta.GetProperty("streaming_decode");
        foreach (JsonElement spec in streaming.GetProperty("transformer_offsets").EnumerateArray())
        {
            _transformerStates.Add((
                Input: Text(spec, "input_name"),
                Output: Text(spec, "output_name"),
                Shape: Ints(spec, "shape")));
        }

        foreach (JsonElement spec in streaming.GetProperty("attention_caches").EnumerateArray())
        {
            _attentionCaches.Add(new AttentionCacheSpec(
                OffsetInput: Text(spec, "offset_input_name"),
                OffsetOutput: Text(spec, "offset_output_name"),
                KeysInput: Text(spec, "cached_keys_input_name"),
                KeysOutput: Text(spec, "cached_keys_output_name"),
                ValuesInput: Text(spec, "cached_values_input_name"),
                ValuesOutput: Text(spec, "cached_values_output_name"),
                PositionsInput: Text(spec, "cached_positions_input_name"),
                PositionsOutput: Text(spec, "cached_positions_output_name"),
                OffsetShape: Ints(spec, "offset_shape"),
                CacheShape: Ints(spec, "cache_shape"),
                PositionsShape: Ints(spec, "positions_shape")));
        }
    }

    private static string Text(JsonElement spec, string name) => spec.GetProperty(name).GetString()!;

    private static int[] Ints(JsonElement spec, string name) => [.. spec.GetProperty(name).EnumerateArray().Select(x => x.GetInt32())];

    /// <summary>Winds the decoder back to its initial state, ready for a new utterance.</summary>
    public void Reset()
    {
        _floatState = new Dictionary<string, DenseTensor<float>>(StringComparer.Ordinal);
        _intState = new Dictionary<string, DenseTensor<int>>(StringComparer.Ordinal);

        foreach ((string input, _, int[] shape) in _transformerStates)
        {
            // Shapes come from the codec meta, as the reference reads them; the session's
            // own metadata reports dynamic axes as −1, which is not a shape to build.
            _intState[input] = new DenseTensor<int>(shape);
        }

        foreach (AttentionCacheSpec cache in _attentionCaches)
        {
            _intState[cache.OffsetInput] = new DenseTensor<int>(cache.OffsetShape);
            _floatState[cache.KeysInput] = new DenseTensor<float>(cache.CacheShape);
            _floatState[cache.ValuesInput] = new DenseTensor<float>(cache.CacheShape);

            // Positions start at −1: nothing has been written yet, which is not zero.
            var positions = new DenseTensor<int>(cache.PositionsShape);
            positions.Buffer.Span.Fill(-1);
            _intState[cache.PositionsInput] = positions;
        }
    }

    /// <summary>Decodes a batch of frame rows and returns interleaved stereo audio.</summary>
    public (float[] Audio, int SamplesPerChannel) RunFrames(IReadOnlyList<int[]> frameRows)
    {
        if (frameRows.Count == 0)
        {
            return ([], 0);
        }

        var codes = new DenseTensor<int>([1, frameRows.Count, _numQuantizers]);
        for (int frame = 0; frame < frameRows.Count; frame++)
        {
            for (int quantizer = 0; quantizer < _numQuantizers; quantizer++)
            {
                codes[0, frame, quantizer] = quantizer < frameRows[frame].Length
                    ? frameRows[frame][quantizer]
                    : 0;
            }
        }

        var feeds = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("audio_codes", codes),
            NamedOnnxValue.CreateFromTensor(
                "audio_code_lengths", new DenseTensor<int>(new[] { frameRows.Count }, [1])),
        };
        foreach ((string input, _, _) in _transformerStates)
        {
            feeds.Add(NamedOnnxValue.CreateFromTensor(input, _intState[input]));
        }

        foreach (AttentionCacheSpec cache in _attentionCaches)
        {
            feeds.Add(NamedOnnxValue.CreateFromTensor(cache.OffsetInput, _intState[cache.OffsetInput]));
            feeds.Add(NamedOnnxValue.CreateFromTensor(cache.KeysInput, _floatState[cache.KeysInput]));
            feeds.Add(NamedOnnxValue.CreateFromTensor(cache.ValuesInput, _floatState[cache.ValuesInput]));
            feeds.Add(NamedOnnxValue.CreateFromTensor(cache.PositionsInput, _intState[cache.PositionsInput]));
        }

        // The decoder's state is cloned per call, deliberately: the graphs rearrange output
        // memory through ORT's arena, and a state tensor that aliases a live input behaves
        // like a decoder whose history is being rewritten — the audio drifted. The clone is
        // the honest price of feeding an exported graph that takes its own output back.
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _session.Run(feeds);

        foreach ((string input, string output, _) in _transformerStates)
        {
            _intState[input] = (DenseTensor<int>)AsTensor<int>(outputs, output).Clone();
        }

        foreach (AttentionCacheSpec cache in _attentionCaches)
        {
            _intState[cache.OffsetInput] = (DenseTensor<int>)AsTensor<int>(outputs, cache.OffsetOutput).Clone();
            _floatState[cache.KeysInput] = (DenseTensor<float>)AsTensor<float>(outputs, cache.KeysOutput).Clone();
            _floatState[cache.ValuesInput] = (DenseTensor<float>)AsTensor<float>(outputs, cache.ValuesOutput).Clone();
            _intState[cache.PositionsInput] = (DenseTensor<int>)AsTensor<int>(outputs, cache.PositionsOutput).Clone();
        }

        DenseTensor<float> audio = (DenseTensor<float>)AsTensor<float>(outputs, "audio");
        Tensor<int> audioLengths = AsTensor<int>(outputs, "audio_lengths");
        int audioLength = audioLengths[0];
        int channels = audio.Dimensions[1];
        int total = audio.Dimensions[2];
        int length = Math.Min(Math.Max(audioLength, 0), total);

        // Channel-major in the tensor, interleaved out, so a sink can hand the buffer to a
        // playback instance without a repack.
        var interleaved = new float[length * channels];
        for (int sample = 0; sample < length; sample++)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                interleaved[sample * channels + channel] = audio[0, channel, sample];
            }
        }

        return (interleaved, length);
    }

    private sealed record AttentionCacheSpec(
        string OffsetInput,
        string OffsetOutput,
        string KeysInput,
        string KeysOutput,
        string ValuesInput,
        string ValuesOutput,
        string PositionsInput,
        string PositionsOutput,
        int[] OffsetShape,
        int[] CacheShape,
        int[] PositionsShape);

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
}
