using System.Buffers.Binary;

namespace SolSystem.Speech;

/// <summary>
/// A voice cloned from a reference recording: the recording in, its audio codes out.
/// </summary>
/// <remarks>
/// <para>
/// Voice cloning needs the reference audio as codes, and the codes come from the audio
/// tokenizer's encoder graph. The reference implementation loads through torchaudio and
/// resamples; this port reads PCM WAV directly and resamples by linear interpolation —
/// the encoder is a tokenizer of a speech signal, and a prompt clip at 48 kHz mono or
/// stereo is what it wants to see. Mono is doubled to the codec's channel count; a
/// non-48 kHz file is resampled to the codec's own rate first.
/// </para>
/// </remarks>
internal static class AudioPrompt
{
    /// <summary>
    /// Reads a PCM WAV and returns it as the codec wants its waveform: mono or stereo
    /// float32 in [-1, 1), at the given rate.
    /// </summary>
    internal static (float[] Interleaved, int Channels, int SampleRate) ReadWav(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 44 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x46464952)
        {
            throw new InvalidDataException($"{path} is not a RIFF/WAVE file.");
        }

        int cursor = 12;
        ushort channels = 0, bits = 0, format = 0;
        int sampleRate = 0;
        byte[]? samples = null;

        while (cursor + 8 <= data.Length)
        {
            uint chunkId = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(cursor, 4));
            int chunkLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(cursor + 4, 4));
            int body = cursor + 8;
            if (chunkId == 0x20746D66) // fmt
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(body, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(body + 2, 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(body + 4, 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(body + 14, 2));
            }
            else if (chunkId == 0x61746164) // data
            {
                samples = data[body..Math.Min(body + chunkLength, data.Length)];
            }

            cursor = body + chunkLength + (chunkLength & 1);
        }

        if (samples is null || format != 1 || bits != 16)
        {
            throw new InvalidDataException(
                $"{path}: this port reads 16-bit PCM WAV; re-export the prompt clip as PCM first.");
        }

        int frames = samples.Length / (channels * 2);
        var interleaved = new float[frames * channels];
        for (int index = 0; index < interleaved.Length; index++)
        {
            interleaved[index] = BinaryPrimitives.ReadInt16LittleEndian(samples.AsSpan(index * 2, 2)) / 32768.0f;
        }

        return (interleaved, channels, sampleRate);
    }

    /// <summary>
    /// Brings a prompt recording to the codec's shape: resampled to its rate, converted to
    /// its channel count, as a [1, channels, samples] float tensor payload.
    /// </summary>
    internal static (float[] Waveform, int Samples) Prepare(
        string path, int targetRate, int targetChannels)
    {
        (float[] interleaved, int channels, int rate) = ReadWav(path);
        int frames = interleaved.Length / channels;

        // Resample first, on the source layout, then convert channels.
        if (rate != targetRate)
        {
            int outFrames = (int)Math.Round(frames * (double)targetRate / rate);
            var resampled = new float[outFrames * channels];
            for (int channel = 0; channel < channels; channel++)
            {
                for (int frame = 0; frame < outFrames; frame++)
                {
                    double source = frame * (double)rate / targetRate;
                    int left = (int)source;
                    int right = Math.Min(left + 1, frames - 1);
                    double fraction = source - left;
                    resampled[frame * channels + channel] = (float)
                        (interleaved[left * channels + channel] * (1.0 - fraction)
                         + interleaved[right * channels + channel] * fraction);
                }
            }

            // Channel count survives a rate change untouched.
            (interleaved, frames) = (resampled, outFrames);
        }

        if (channels == targetChannels)
        {
            // Already the right shape.
        }
        else if (channels == 1 && targetChannels > 1)
        {
            var widened = new float[frames * targetChannels];
            for (int frame = 0; frame < frames; frame++)
            {
                for (int channel = 0; channel < targetChannels; channel++)
                {
                    widened[frame * targetChannels + channel] = interleaved[frame];
                }
            }

            (interleaved, channels) = (widened, targetChannels);
        }
        else if (channels > 1 && targetChannels == 1)
        {
            var narrowed = new float[frames];
            for (int frame = 0; frame < frames; frame++)
            {
                double sum = 0.0;
                for (int channel = 0; channel < channels; channel++)
                {
                    sum += interleaved[frame * channels + channel];
                }

                narrowed[frame] = (float)(sum / channels);
            }

            (interleaved, channels) = (narrowed, 1);
        }
        else
        {
            throw new NotSupportedException(
                $"unsupported channel conversion {channels} -> {targetChannels}");
        }

        // The encoder sees [1, channels, frames]: channel-major.
        var waveform = new float[frames * channels];
        for (int channel = 0; channel < channels; channel++)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                waveform[channel * frames + frame] = interleaved[frame * channels + channel];
            }
        }

        return (waveform, frames);
    }
}
