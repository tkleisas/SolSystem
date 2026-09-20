using System.Globalization;

namespace SolSystem.Core.Numerics;

/// <summary>
/// A signed Q64.64 fixed-point number: 64 bits of integer, 64 bits of fraction.
/// </summary>
/// <remarks>
/// <para>
/// This type exists because <see cref="Fix64"/> cannot do the solar frame. Gravity needs
/// <c>r²</c>; in a Q32.32 <c>long</c> the largest square that fits is about 2.1 × 10⁹, so
/// <c>r</c> is capped near 46 000 km — one seventh of an Earth radius. Saturn is
/// 1.4 × 10⁹ km away, so its <c>r²</c> overflows by nine orders of magnitude.
/// </para>
/// <para>
/// No choice of unit rescues that. The solar frame needs both ends of the range at once:
/// a heliocentric coordinate out to billions of kilometres (31 bits) and a thrust
/// acceleration down to nanometres per second squared (60 bits of fraction). That is more
/// than 64 bits of dynamic range by construction, so the type has to be wider.
/// </para>
/// <para>
/// The magnitude is stored as a single unsigned <see cref="UInt128"/> with a separate
/// sign. An earlier version stored the bits in a signed <see cref="Int128"/> and split
/// them with shifts and casts; that is wrong whenever the low word's top bit is set,
/// because the shift sign-extends, and <c>&lt;&lt; 64</c> on a signed value silently drops
/// the top half. Keeping an unsigned magnitude makes every operation here expressible
/// without a sign-extension trap.
/// </para>
/// <para>
/// The unit is the kilometre, matching the design's solar frame. <see cref="Fix64"/>
/// remains the local frame's type.
/// </para>
/// </remarks>
internal readonly struct Fix128 : IEquatable<Fix128>, IComparable<Fix128>
{
    internal const int FractionalBits = 64;

    private const double TwoTo64 = 18446744073709551616.0;

    /// <summary>Unsigned magnitude, already scaled by 2^64.</summary>
    internal readonly UInt128 Magnitude;

    /// <summary>True when the value is negative. Zero is never negative.</summary>
    internal readonly bool Negative;

    /// <summary>True when the magnitude is zero, whatever the sign bit says.</summary>
    internal bool IsZero => Magnitude == UInt128.Zero;

    private Fix128(UInt128 magnitude, bool negative)
    {
        Magnitude = magnitude;
        Negative = negative && magnitude != UInt128.Zero;
    }

    /// <summary>Wraps an already-scaled magnitude.</summary>
    internal static Fix128 FromRaw(UInt128 magnitude, bool negative = false) => new(magnitude, negative);

    internal static Fix128 FromWhole(long whole)
    {
        bool negative = whole < 0;
        ulong magnitude = negative ? (ulong)(-whole) : (ulong)whole;
        return new((UInt128)magnitude << FractionalBits, negative);
    }

    internal static Fix128 FromDouble(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Not a finite value.");
        }

        bool negative = value < 0;
        double magnitude = Math.Abs(value);

        double whole = Math.Floor(magnitude);
        if (whole >= TwoTo64)
        {
            throw new OverflowException($"{value} is outside the representable range of Fix128.");
        }

        ulong wholeWord = (ulong)whole;
        double fraction = magnitude - whole;
        ulong low = (ulong)Math.Round(fraction * TwoTo64);

        UInt128 scaled = ((UInt128)wholeWord << FractionalBits) + low;
        return new(scaled, negative);
    }

    internal double ToDouble()
    {
        // The value is Magnitude / 2^64, so the high word is the integer part as-is and
        // only the low word is scaled. Multiplying the high word by 2^64 instead scales
        // everything by 2^128.
        ulong high = (ulong)(Magnitude >> FractionalBits);
        ulong low = (ulong)Magnitude;
        double magnitude = high + low / TwoTo64;
        return Negative ? -magnitude : magnitude;
    }

    internal static readonly Fix128 Zero = new(UInt128.Zero, false);

    internal static readonly Fix128 One = new((UInt128)1 << FractionalBits, false);

    // ---------------------------------------------------------------- arithmetic

    public static Fix128 operator +(Fix128 a, Fix128 b)
    {
        if (a.Negative == b.Negative)
        {
            return new(a.Magnitude + b.Magnitude, a.Negative);
        }

        // Opposite signs: the result takes the sign of the larger magnitude.
        if (a.Magnitude >= b.Magnitude)
        {
            return new(a.Magnitude - b.Magnitude, a.Negative);
        }

        return new(b.Magnitude - a.Magnitude, b.Negative);
    }

    public static Fix128 operator -(Fix128 a, Fix128 b) => a + new Fix128(b.Magnitude, !b.Negative);

    public static Fix128 operator -(Fix128 a) => new(a.Magnitude, !a.Negative);

    /// <summary>
    /// Fixed-point multiply. <c>(a * b) &gt;&gt; 64</c> is the top 128 bits of the 256-bit
    /// product, written in a form whose intermediates all fit:
    /// <c>(a * (b &gt;&gt; 64)) + ((a * (b &amp; mask)) &gt;&gt; 64)</c>.
    /// </summary>
    public static Fix128 operator *(Fix128 a, Fix128 b)
    {
        return new(IntMath.MultiplyHigh128(a.Magnitude, b.Magnitude), a.Negative ^ b.Negative);
    }

    public static Fix128 operator /(Fix128 a, Fix128 b)
    {
        if (b.Magnitude == UInt128.Zero)
        {
            throw new DivideByZeroException("Fix128 division by zero.");
        }

        // (a << 64) / b, exact and without materialising the shifted numerator.
        UInt128 quotient = IntMath.ShiftedDivide(a.Magnitude, b.Magnitude);
        return new(quotient, a.Negative ^ b.Negative);
    }

    /// <summary>
    /// Square root, exact to the last representable bit.
    /// </summary>
    /// <remarks>
    /// The input is a plain quantity whose square root is wanted, so the result is the
    /// Q64.64 root of <c>Magnitude</c> — computed as the integer square root of
    /// <c>Magnitude &lt;&lt; 64</c>, which <see cref="IntMath.SqrtScaled"/> does without ever
    /// forming that product.
    /// </remarks>
    public static Fix128 Sqrt(Fix128 a)
    {
        if (a.Negative)
        {
            throw new ArgumentOutOfRangeException(nameof(a), "Sqrt is undefined for negative Fix128 values.");
        }

        if (a.Magnitude == UInt128.Zero)
        {
            return Zero;
        }

        if ((ulong)(a.Magnitude >> 64) >= 1UL << 62)
        {
            throw new OverflowException(
                "Fix128.Sqrt input is too large: its square root would leave the type's range.");
        }

        return new(IntMath.SqrtScaled(a.Magnitude), false);
    }

    internal Fix128 Abs() => new(Magnitude, false);

    // ---------------------------------------------------------------- comparison

    public bool Equals(Fix128 other) => Magnitude == other.Magnitude && Negative == other.Negative;

    public override bool Equals(object? obj) => obj is Fix128 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Magnitude, Negative);

    public int CompareTo(Fix128 other)
    {
        if (Negative != other.Negative)
        {
            return Negative ? -1 : 1;
        }

        int magnitude = Magnitude.CompareTo(other.Magnitude);
        return Negative ? -magnitude : magnitude;
    }

    public static bool operator ==(Fix128 a, Fix128 b) => a.Equals(b);

    public static bool operator !=(Fix128 a, Fix128 b) => !a.Equals(b);

    public static bool operator <(Fix128 a, Fix128 b) => a.CompareTo(b) < 0;

    public static bool operator >(Fix128 a, Fix128 b) => a.CompareTo(b) > 0;

    public static bool operator <=(Fix128 a, Fix128 b) => a.CompareTo(b) <= 0;

    public static bool operator >=(Fix128 a, Fix128 b) => a.CompareTo(b) >= 0;

    public override string ToString() => ToDouble().ToString("0.###############", CultureInfo.InvariantCulture);
}
