using System.Security.Cryptography;
using SolSystem.Core.Numerics;

namespace SolSystem.Core.Determinism;

/// <summary>
/// Builds the world-hash digest shared by the hash instruments — the probe's
/// <c>hash</c> command, the controller's hash endpoint, anything that wants to ask
/// "are these two worlds the same world?".
/// </summary>
/// <remarks>
/// <para>
/// Everything the simulation believes is folded into one value, and two runs of the
/// same inputs have to produce the same one. If they do not, something is reading a
/// clock, a hash seed, an iteration order or a float, and it will do the same thing in
/// a player's game where it is far harder to see.
/// </para>
/// <para>
/// The hash is taken over the <b>raw fixed-point words</b>, not over printed decimals.
/// Formatting rounds; rounding hides a one-bit drift, and a one-bit drift is exactly
/// the thing a determinism check is for.
/// </para>
/// </remarks>
internal sealed class WorldHashBuilder
{
    private readonly List<byte> _bytes = [];

    /// <summary>
    /// A raw 128-bit fixed-point magnitude, byte for byte.
    /// </summary>
    /// <remarks>
    /// Hashed directly rather than through <c>double</c>. A double carries 53 bits of
    /// mantissa, so the cast drops the low eleven bits of a Q64.64 word — the bits a
    /// one-bit drift lives in — and the type carries no sign at all, so <c>x</c> and
    /// <c>-x</c> hashed identically. There is no <c>BitConverter</c> overload for
    /// <see cref="UInt128"/>, so the two 64-bit halves go in explicitly.
    /// </remarks>
    public void Add(Fix128 value)
    {
        Add(value.Magnitude);
        Add(value.Negative);
    }

    public void Add(Fix128Vec value)
    {
        Add(value.X);
        Add(value.Y);
        Add(value.Z);
    }

    public void Add(double value) => _bytes.AddRange(BitConverter.GetBytes(value));

    public void Add(long value) => _bytes.AddRange(BitConverter.GetBytes(value));

    public void Add(bool value) => _bytes.Add(value ? (byte)1 : (byte)0);

    /// <summary>
    /// A raw 128-bit word without the sign bit folded in — the halves of an unsigned
    /// value such as a fixed-point magnitude.
    /// </summary>
    public void Add(UInt128 value)
    {
        _bytes.AddRange(BitConverter.GetBytes((ulong)(value & ulong.MaxValue)));
        _bytes.AddRange(BitConverter.GetBytes((ulong)(value >> 64)));
    }

    /// <summary>
    /// The digest: SHA-256 over everything appended so far, lowercase hexadecimal.
    /// </summary>
    public string Digest() => Convert.ToHexString(SHA256.HashData(_bytes.ToArray())).ToLowerInvariant();
}
