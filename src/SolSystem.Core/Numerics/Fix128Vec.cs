namespace SolSystem.Core.Numerics;

/// <summary>
/// A 3-vector of Q64.64 fixed-point kilometres, or kilometres per second, or
/// kilometres per second squared, according to context.
/// </summary>
/// <remarks>
/// The solar frame's vector type, and the local frame's too: metres and kilometres are
/// magnitudes are small enough for a Q32.32 value and the arithmetic is narrower.
/// </remarks>
internal readonly struct Fix128Vec : IEquatable<Fix128Vec>
{
    internal readonly Fix128 X;
    internal readonly Fix128 Y;
    internal readonly Fix128 Z;

    internal Fix128Vec(Fix128 x, Fix128 y, Fix128 z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    internal static readonly Fix128Vec Zero = new(Fix128.Zero, Fix128.Zero, Fix128.Zero);

    public static Fix128Vec operator +(Fix128Vec a, Fix128Vec b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Fix128Vec operator -(Fix128Vec a, Fix128Vec b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Fix128Vec operator -(Fix128Vec a) => new(-a.X, -a.Y, -a.Z);

    public static Fix128Vec operator *(Fix128Vec a, Fix128 s) => new(a.X * s, a.Y * s, a.Z * s);

    public static Fix128Vec operator *(Fix128 s, Fix128Vec a) => a * s;

    /// <summary>Sum of the squared components, with no scaling and no guard.</summary>
    /// <remarks>
    /// A Q64.64 multiply halves the headroom, so a component past 2^63.5 — 22 AU in
    /// kilometres — overflows this. Use <see cref="Length"/> for anything that might be that
    /// large; Neptune at 30 AU is, and the failure is silent.
    /// </remarks>
    internal Fix128 LengthSquared => X * X + Y * Y + Z * Z;

    /// <summary>
    /// The magnitude, computed without forming the sum of squares in the vector's own scale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each component is scaled down by a shared binary exponent before squaring, so every
    /// square is at most one, and the root is scaled back up afterwards. The shared exponent is
    /// the largest any component needs, which keeps the ratios between components exact — they
    /// are all divided by the same power of two.
    /// </para>
    /// <para>
    /// This is the same shape as the old Q32.32 vector's, arrived at twice: a fixed-point square
    /// is the thing that runs out of range first, and the solar frame is exactly where it
    /// happens. Neptune's components are 4.5 x 10^9 km and their squares reach 2 x 10^19,
    /// past the 1.8 x 10^19 the type can hold, so a vector 30 AU long reports itself as 8.3.
    /// </para>
    /// <para>
    /// Scaling by an exponent rather than by a computed factor matters at the far end: a
    /// factor that brings 30 AU down to one is 2^-33, and squaring it to restore the scale
    /// underflows at 2^-66. Shifting does not.
    /// </para>
    /// </remarks>
    internal Fix128 Length
    {
        get
        {
            if (IsZero)
            {
                return Fix128.Zero;
            }

            int shift = Math.Max(Exponent(X), Math.Max(Exponent(Y), Exponent(Z)));

            Fix128 sx = Scale(X, -shift);
            Fix128 sy = Scale(Y, -shift);
            Fix128 sz = Scale(Z, -shift);

            // Every component is below one by construction, so the sum of squares is at most
            // three and the root at most two.
            Fix128 sum = sx * sx + sy * sy + sz * sz;
            return Shift(Fix128.Sqrt(sum), shift);
        }
    }

    /// <summary>The component's binary exponent, as a power of two. Zero for a zero value.</summary>
    private static int Exponent(Fix128 value)
    {
        if (value.IsZero)
        {
            return 0;
        }

        UInt128 magnitude = value.Magnitude;
        ulong high = (ulong)(magnitude >> 64);
        int bits = high != 0
            ? 128 - System.Numerics.BitOperations.LeadingZeroCount(high)
            : 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)magnitude);

        return bits - 64;
    }

    /// <summary>Multiplies a value by a power of two, exactly, without an intermediate factor.</summary>
    private static Fix128 Scale(Fix128 value, int shift)
    {
        if (shift == 0 || value.IsZero)
        {
            return value;
        }

        return shift > 0
            ? Fix128.FromRaw(value.Magnitude << shift, value.Negative)
            : Fix128.FromRaw(value.Magnitude >> -shift, value.Negative);
    }

    /// <summary>Multiplies by two to the <paramref name="shift"/>, for the result coming back up.</summary>
    private static Fix128 Shift(Fix128 value, int shift) => Scale(value, shift);

    internal bool IsZero => X == Fix128.Zero && Y == Fix128.Zero && Z == Fix128.Zero;

    public bool Equals(Fix128Vec other) => X == other.X && Y == other.Y && Z == other.Z;

    public override bool Equals(object? obj) => obj is Fix128Vec other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    public static bool operator ==(Fix128Vec a, Fix128Vec b) => a.Equals(b);

    public static bool operator !=(Fix128Vec a, Fix128Vec b) => !a.Equals(b);

    /// <summary>Unit vector in the same direction.</summary>
    internal Fix128Vec Normalized()
    {
        Fix128 length = Length;
        if (length == Fix128.Zero)
        {
            throw new InvalidOperationException("Cannot normalise a zero vector.");
        }

        return new Fix128Vec(X / length, Y / length, Z / length);
    }

    public override string ToString() => $"({X}, {Y}, {Z})";
}
