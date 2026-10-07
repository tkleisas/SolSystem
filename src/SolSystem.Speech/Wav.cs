namespace SolSystem.Speech;

/// <summary>Which sample encoding a WAV is written in.</summary>
internal enum WavEncoding
{
    /// <summary>16-bit PCM, clipped and rounded. The playback path's own encoding.</summary>
    Pcm16,

    /// <summary>32-bit IEEE float, WAVE_FORMAT_IEEE_FLOAT — the codec's native output, and
    /// the speech cache's, so a cached round-trip is byte-exact rather than re-quantised.</summary>
    Float32,
}

/// <summary>
/// Writes a PCM WAV: the codec's own rate and channel count, in either encoding.
/// </summary>
internal static class Wav
{
    internal static void Write(string path, ReadOnlySpan<float> interleaved, int samplesPerChannel, int sampleRate, int channels)
        => Write(path, interleaved, samplesPerChannel, sampleRate, channels, WavEncoding.Pcm16);

    internal static void Write(
        string path,
        ReadOnlySpan<float> interleaved,
        int samplesPerChannel,
        int sampleRate,
        int channels,
        WavEncoding encoding)
    {
        int bytesPerSample = encoding == WavEncoding.Pcm16 ? 2 : 4;
        int formatTag = encoding == WavEncoding.Pcm16 ? 1 : 3;   // PCM vs IEEE float
        int totalSamples = samplesPerChannel * channels;
        int dataBytes = totalSamples * bytesPerSample;
        byte[] bytes = new byte[44 + dataBytes];

        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 0x46464952);     // RIFF
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), 36 + dataBytes);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), 0x45564157);     // WAVE
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), 0x20746D66);    // fmt
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), 16);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20, 2), (ushort)formatTag);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22, 2), (ushort)channels);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24, 4), sampleRate);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28, 4), sampleRate * channels * bytesPerSample);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32, 2), (ushort)(channels * bytesPerSample));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(34, 2), (ushort)(bytesPerSample * 8));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36, 4), 0x61746164);    // data
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40, 4), dataBytes);

        if (encoding == WavEncoding.Pcm16)
        {
            for (int index = 0; index < totalSamples; index++)
            {
                float clipped = Math.Clamp(interleaved[index], -1.0f, 1.0f);
                short pcm = (short)MathF.Round(clipped * 32767.0f);
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(44 + (index * 2), 2), pcm);
            }
        }
        else
        {
            for (int index = 0; index < totalSamples; index++)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(
                    bytes.AsSpan(44 + (index * 4), 4), interleaved[index]);
            }
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, bytes);
    }
}
