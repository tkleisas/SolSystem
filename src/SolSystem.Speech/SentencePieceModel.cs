using System.Buffers.Binary;
using System.Text;

namespace SolSystem.Speech;

/// <summary>
/// A SentencePiece model: the pieces, their merge priorities, and a BPE encoder.
/// </summary>
/// <remarks>
/// <para>
/// The model file is a SentencePiece <c>ModelProto</c>: a repeated <c>pieces</c> field of
/// <c>{string piece = 1; float score = 2; enum type = 3}</c> records, in id order. The trainer
/// spec is absent from the exported copy, so the algorithm is decided against the manifest's
/// two reference samples rather than assumed: a Viterbi over the scores reproduces the
/// Chinese sample and diverges on the English one, while the BPE merge — repeatedly take the
/// adjacent pair whose <em>concatenation is a piece</em>, highest merged-piece score first —
/// reproduces both exactly. That is the encoder this class implements.
/// </para>
/// <para>
/// A first version of the merge kept a table keyed on the pair that best decomposed each
/// piece by summed score; a piece reachable through a different pair then lost its merge
/// and the segmentation went fine-grained. Looking the concatenation up directly has no
/// such gap. Characters outside the vocabulary fall back one id per UTF-8 byte when the
/// 256 byte pieces are present, and to the unknown token otherwise.
/// </para>
/// </remarks>
internal sealed class SentencePieceModel
{
    private const int UnknownId = 0;
    private const char SentencePieceSpace = '▁'; // U+2581, lower one eighth block

    private readonly string[] _pieces;
    private readonly float[] _scores;
    private readonly byte[] _types;
    private readonly Dictionary<string, int> _pieceIndex;

    private SentencePieceModel(string[] pieces, float[] scores, byte[] types)
    {
        _pieces = pieces;
        _scores = scores;
        _types = types;
        _pieceIndex = new Dictionary<string, int>(pieces.Length, StringComparer.Ordinal);
        for (int index = 0; index < pieces.Length; index++)
        {
            _pieceIndex.TryAdd(pieces[index], index);
        }
    }

    internal int VocabularySize => _pieces.Length;

    /// <summary>A few pieces, for the diagnostic mode.</summary>
    internal string[] FirstPieces(int count) =>
        [.. Enumerable.Range(0, Math.Min(count, _pieces.Length)).Select(index => _pieces[index])];

    /// <summary>The type byte of a piece, for the diagnostic mode.</summary>
    internal byte TypeOf(int id) => _types[id];

    internal int IdOf(string piece) => _pieceIndex.TryGetValue(piece, out int id) ? id : UnknownId;

    /// <summary>A piece's score, for the diagnostic mode.</summary>
    internal float ScoreOf(int id) => _scores[id];

    /// <summary>
    /// Encodes text the BPE way: repeatedly merge the adjacent pair whose concatenation is
    /// a piece, highest merged-piece score first; byte fallback for what remains.
    /// </summary>
    internal int[] Encode(string text)
    {
        string normalized = Normalize(text);
        var symbols = new List<string>(normalized.Length);
        foreach (char c in normalized)
        {
            symbols.Add(c.ToString());
        }

        while (symbols.Count > 1)
        {
            int bestIndex = -1;
            float bestScore = float.NegativeInfinity;
            for (int index = 0; index < symbols.Count - 1; index++)
            {
                if (_pieceIndex.TryGetValue(symbols[index] + symbols[index + 1], out int merged)
                    && _scores[merged] > bestScore)
                {
                    bestScore = _scores[merged];
                    bestIndex = index;
                }
            }

            if (bestIndex < 0)
            {
                break;
            }

            symbols[bestIndex] = symbols[bestIndex] + symbols[bestIndex + 1];
            symbols.RemoveAt(bestIndex + 1);
        }

        var ids = new List<int>(symbols.Count);
        foreach (string symbol in symbols)
        {
            if (_pieceIndex.TryGetValue(symbol, out int id))
            {
                ids.Add(id);
                continue;
            }

            // One id per UTF-8 byte of what did not segment, when byte pieces exist.
            foreach (byte b in Encoding.UTF8.GetBytes(symbol))
            {
                ids.Add(_pieceIndex.TryGetValue($"<0x{b:X2}>", out int byteId)
                    ? byteId
                    : UnknownId);
            }
        }

        return [.. ids];
    }

    /// <summary>
    /// SentencePiece's default normalisation: NFKC, whitespace folded into <c>▁</c>,
    /// dummy prefix, runs collapsed.
    /// </summary>
    private static string Normalize(string text)
    {
        string nfkc = text.Normalize(NormalizationForm.FormKC);

        var builder = new StringBuilder(nfkc.Length + 1);
        builder.Append(SentencePieceSpace);
        foreach (char c in nfkc)
        {
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
            {
                builder.Append(SentencePieceSpace);
            }
            else if (!char.IsControl(c))
            {
                builder.Append(c);
            }
        }

        // Collapse runs of the space marker and drop the trailing one, keeping the dummy prefix.
        string joined = builder.ToString();
        var collapsed = new StringBuilder(joined.Length);
        bool previousWasSpace = false;
        for (int index = 0; index < joined.Length; index++)
        {
            bool isSpace = joined[index] == SentencePieceSpace;
            if (isSpace && previousWasSpace)
            {
                continue;
            }

            collapsed.Append(joined[index]);
            previousWasSpace = isSpace;
        }

        string result = collapsed.ToString().TrimEnd(SentencePieceSpace);
        return result.Length == 0 ? SentencePieceSpace.ToString() : result;
    }

    // -------------------------------------------------------------- proto parsing

    private const byte PieceNormal = 1;
    private const byte PieceUnknown = 2;
    private const byte PieceControl = 3;
    private const byte PieceUserDefined = 4;
    private const byte PieceByte = 6;

    internal static SentencePieceModel Load(string path)
    {
        byte[] data = File.ReadAllBytes(path);

        var pieces = new List<string>();
        var scores = new List<float>();
        var types = new List<byte>();

        int cursor = 0;
        while (cursor < data.Length)
        {
            (int field, WireType wire, int next) = ReadTag(data, cursor);
            if (field == 1 && wire == WireType.LengthDelimited)
            {
                (string piece, float score, byte type) = ReadPiece(data, next);
                pieces.Add(piece);
                scores.Add(score);
                types.Add(type);
                cursor = SkipRecord(data, next);
                continue;
            }

            // trainer_spec, normalizer_spec and the rest are skipped whole.
            if (wire == WireType.LengthDelimited)
            {
                cursor = SkipRecord(data, next);
            }
            else
            {
                cursor = next;
            }
        }

        return new SentencePieceModel([.. pieces], [.. scores], [.. types]);
    }

    private enum WireType
    {
        Varint = 0,
        Fixed64 = 1,
        LengthDelimited = 2,
        Fixed32 = 5,
    }

    private static (int Field, WireType Wire, int Next) ReadTag(byte[] data, int cursor)
    {
        (ulong value, int next) = ReadVarint(data, cursor);
        return ((int)(value >> 3), (WireType)(value & 7), next);
    }

    private static (ulong Value, int Next) ReadVarint(byte[] data, int cursor)
    {
        ulong value = 0;
        int shift = 0;
        while (cursor < data.Length)
        {
            byte b = data[cursor++];
            value |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80)
            {
                break;
            }

            shift += 7;
        }

        return (value, cursor);
    }

    private static (string Piece, float Score, byte Type) ReadPiece(byte[] data, int cursor)
    {
        (int length, int end) = ReadLength(data, cursor);
        int body = end - length;
        string piece = "";
        float score = 0.0f;
        byte type = PieceNormal;

        while (body < end)
        {
            (int field, WireType wire, int next) = ReadTag(data, body);
            switch (field, wire)
            {
                case (1, WireType.LengthDelimited):
                    // ReadLength's second value is the END of the field's body; the text
                    // begins exactly textLength bytes before it.
                    (int textLength, int textEnd) = ReadLength(data, next);
                    piece = Encoding.UTF8.GetString(data, textEnd - textLength, textLength);
                    body = textEnd;
                    break;
                case (2, WireType.Fixed32):
                    score = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(next, 4));
                    body = next + 4;
                    break;
                case (3, WireType.Varint):
                    (ulong value, int after) = ReadVarint(data, next);
                    type = (byte)value;
                    body = after;
                    break;
                default:
                    body = wire == WireType.LengthDelimited ? SkipRecord(data, next) : next;
                    break;
            }
        }

        return (piece, score, type);
    }

    private static (int Length, int End) ReadLength(byte[] data, int cursor)
    {
        (ulong value, int next) = ReadVarint(data, cursor);
        return ((int)value, next + (int)value);
    }

    private static int SkipRecord(byte[] data, int bodyStart)
    {
        (_, int end) = ReadLength(data, bodyStart);
        return end;
    }
}
