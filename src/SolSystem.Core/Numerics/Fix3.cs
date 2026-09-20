namespace SolSystem.Core.Numerics;

/// <summary>
/// A 3-vector of fixed-point megametres, or megametres per second, or megametres
/// per second squared. The unit is the caller's business; the arithmetic is the same.
/// </summary>
/// <remarks>
/// Deliberately not a generic or SIMD type. Hardware vector lanes have their own
/// rounding behaviour, and a simulation whose results depend on which machine ran
/// it has given up the only reason it uses fixed point.
/// </remarks>
internal readonly struct Fix3 : IEquatable<Fix3>
{
    internal readonly Fix64 X;
    internal readonly Fix64 Y;
    internal readonly Fix64 Z;

    internal Fix3(Fix64 x, Fix64 y, Fix64 z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    internal static readonly Fix3 Zero = new(Fix64.Zero, Fix64.Zero, Fix64.Zero);

    public static Fix3 operator +(Fix3 a, Fix3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Fix3 operator -(Fix3 a, Fix3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Fix3 operator -(Fix3 a) => new(-a.X, -a.Y, -a.Z);

    public static Fix3 operator *(Fix3 a, Fix64 s) => new(a.X * s, a.Y * s, a.Z * s);

    public static Fix3 operator *(Fix64 s, Fix3 a) => a * s;

    /// <summary>
    /// The sum of the squares. Overflows if any component exceeds about 46 341 in value.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="Length"/> for anything whose magnitude is not known to be small.
    /// This is retained for callers that genuinely want the square — a squared distance
    /// compared against another squared distance, say — where the extra precision of not
    /// taking a root matters.
    /// </remarks>
    public Fix64 LengthSquared => X * X + Y * Y + Z * Z;

    /// <summary>
    /// The magnitude, computed without forming the sum of squares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A Q32.32 value cannot hold a square larger than 2.147 × 10⁹, so
    /// <c>sqrt(x² + y² + z²)</c> overflows for any component past about 46 341 — 46 km,
    /// which is nothing. A low Earth orbit at 7 000 km is a hundred and fifty times over
    /// the line, and the failure is silent: the sum wraps negative and every derived
    /// quantity, including gravity, is nonsense.
    /// </para>
    /// <para>
    /// Each component is instead scaled down by its own binary exponent before squaring, so
    /// every square is at most 1, and the result is scaled back up. Only the exponent
    /// arithmetic needs care.
    /// </para>
    /// </remarks>
    internal Fix64 Length
    {
        get
        {
            if (X.IsZero && Y.IsZero && Z.IsZero)
            {
                return Fix64.Zero;
            }

            // The largest exponent any component needs, as a power of two.
            int shift = Math.Max(Exponent(X), Math.Max(Exponent(Y), Exponent(Z)));

            Fix64 sx = Scale(X, -shift);
            Fix64 sy = Scale(Y, -shift);
            Fix64 sz = Scale(Z, -shift);

            // Every component is now below 1 by construction, so the sum of squares cannot
            // exceed 3 and the root cannot exceed 2.
            Fix64 sum = sx * sx + sy * sy + sz * sz;
            return Scale(Fix64.Sqrt(sum), shift);
        }
    }

    /// <summary>The component's binary exponent, as a power of two. Zero for a zero value.</summary>
    private static int Exponent(Fix64 value)
    {
        if (value.IsZero)
        {
            return 0;
        }

        int bits = 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)value.Raw);
        return bits - Fix64.FractionalBits;
    }

    /// <summary>Multiplies by 2^<paramref name="shift"/>, saturating rather than wrapping.</summary>
    private static Fix64 Scale(Fix64 value, int shift)
    {
        if (shift == 0 || value.IsZero)
        {
            return value;
        }

        return shift > 0
            ? Fix64.FromRaw(value.Raw << Math.Min(shift, 31))
            : Fix64.FromRaw(value.Raw >> Math.Min(-shift, 63));
    }

    /// <summary>Unit vector in the same direction. Throws at the origin.</summary>
    internal Fix3 Normalized()
    {
        Fix64 length = Length;
        if (length == Fix64.Zero)
        {
            throw new InvalidOperationException("Cannot normalise a zero vector.");
        }

        return new Fix3(X / length, Y / length, Z / length);
    }

    public bool Equals(Fix3 other) => X == other.X && Y == other.Y && Z == other.Z;

    public override bool Equals(object? obj) => obj is Fix3 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    public static bool operator ==(Fix3 a, Fix3 b) => a.Equals(b);

    public static bool operator !=(Fix3 a, Fix3 b) => !a.Equals(b);

    internal (double X, double Y, double Z) ToDoubles() => (X.ToDouble(), Y.ToDouble(), Z.ToDouble());

    public override string ToString() => $"({X}, {Y}, {Z})";
}
