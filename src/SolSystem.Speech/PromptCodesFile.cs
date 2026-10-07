using System.Security.Cryptography;

namespace SolSystem.Speech;

/// <summary>
/// A voice prompt's encoded codes, saved beside the recording that produced them.
/// </summary>
/// <remarks>
/// <para>
/// Cloning a voice runs the tokenizer's encoder over the recording — a few seconds of CPU
/// per session. The result is small and deterministic (the encoder is a plain graph, no
/// sampling): a few kilobytes of codebook ids. Saving them beside the recording makes the
/// clone a load instead of a synthesis, and the file is small enough to commit like a
/// baked texture.
/// </para>
/// <para>
/// The file is <b>validated, not trusted</b>: a magic and a format version first, then the
/// SHA-256 of the recording it was made from — a stale sidecar from a re-cut prompt is
/// refused and re-encoded rather than spoken in the wrong voice.
/// </para>
/// </remarks>
internal static class PromptCodesFile
{
    // "MOSSCODE" magic, then version, then the codec's identity, then the source hash.
    private static readonly byte[] Magic = [0x4D, 0x4F, 0x53, 0x53, 0x43, 0x4F, 0x44, 0x45];
    private const int Version = 1;

    internal static bool TryLoad(
        string path,
        ReadOnlySpan<byte> sourceHash,
        int codecSampleRate,
        int codecChannels,
        int codecQuantizers,
        out List<List<int>> codes)
    {
        codes = [];

        try
        {
            byte[] data = File.ReadAllBytes(path);
            using var reader = new BinaryReader(new MemoryStream(data));

            if (data.Length < Magic.Length + 4 || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            {
                return false;
            }

            if (reader.ReadUInt32() != Version
                || reader.ReadInt32() != codecSampleRate
                || reader.ReadInt32() != codecChannels
                || reader.ReadInt32() != codecQuantizers
                || !reader.ReadBytes(32).AsSpan().SequenceEqual(sourceHash))
            {
                return false;
            }

            int frames = reader.ReadInt32();
            if (frames < 0 || frames > 100_000)
            {
                return false;
            }

            var loaded = new List<List<int>>(frames);
            for (int frame = 0; frame < frames; frame++)
            {
                var row = new List<int>(codecQuantizers);
                for (int quantizer = 0; quantizer < codecQuantizers; quantizer++)
                {
                    row.Add(reader.ReadUInt16());
                }

                loaded.Add(row);
            }

            codes = loaded;
            return true;
        }
        catch (Exception)
        {
            // A missing, truncated or unreadable sidecar is simply "not cached"; the caller
            // encodes and re-saves.
            return false;
        }
    }

    internal static void Save(
        string path,
        string sourcePath,
        int codecSampleRate,
        int codecChannels,
        int codecQuantizers,
        IReadOnlyList<List<int>> codes)
    {
        byte[] sourceHash = SHA256.HashData(File.ReadAllBytes(sourcePath));

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Magic);
        writer.Write((uint)Version);
        writer.Write(codecSampleRate);
        writer.Write(codecChannels);
        writer.Write(codecQuantizers);
        writer.Write(sourceHash);
        writer.Write(codes.Count);
        foreach (List<int> row in codes)
        {
            for (int quantizer = 0; quantizer < codecQuantizers; quantizer++)
            {
                ushort code = quantizer < row.Count
                    ? checked((ushort)row[quantizer])
                    : (ushort)0;
                writer.Write(code);
            }
        }

        writer.Flush();
        File.WriteAllBytes(path, stream.ToArray());
    }
}
