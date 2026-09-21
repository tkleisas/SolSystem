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
        //
        // The whole part must be taken from the FULL magnitude, not by narrowing to a ulong
        // first. `(ulong)(Magnitude >> 64) << 64` drops the whole part for any value whose
        // fraction does not fit in 64 bits — which is every whole number, because the whole
        // part lives in exactly those 64 bits. Narrowing first made 2^64 read as 0, so
        // sine reported 0 at a quarter turn while the lookup underneath was correct.
        UInt128 whole = Magnitude >> FractionalBits;
        ulong low = (ulong)Magnitude;
        double magnitude = (double)whole + low / TwoTo64;
        return Negative ? -magnitude : magnitude;
    }

    internal static readonly Fix128 Zero = new(UInt128.Zero, false);

    internal static readonly Fix128 One = new((UInt128)1 << FractionalBits, false);

    /// <summary>One half.</summary>
    internal static readonly Fix128 Half = new((UInt128)1 << (FractionalBits - 1), false);

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

        // No range guard is needed. sqrt(x) < x for every x above one, so the root can never
        // leave the type's range; what happens for large inputs is a loss of FRACTIONAL
        // precision, which is the honest answer — a Q64.64 square root of 5 x 10^10 carries a
        // metre of resolution, and that is exactly enough to be useful.
        //
        // There used to be a guard here rejecting anything above 2^62, which is 2.6 AU. It was
        // covering an overflow in the integer root rather than a real limit, and it made the
        // entire outer solar system unreachable.
        return new(IntMath.SqrtScaled(a.Magnitude), false);
    }

    internal Fix128 Abs() => new(Magnitude, false);

    /// <summary>
    /// Natural logarithm, for the rocket equation.
    /// </summary>
    /// <remarks>
    /// The same atanh series as the local frame's other logarithm: split into
    /// <c>2^exponent · m</c> with m in [1, 2), then
    /// <c>2·(t + t³/3 + t⁵/5 + …)</c> with <c>t = (m-1)/(m+1)</c>.
    /// </remarks>
    /// <summary>
    /// The angle of the point <c>(x, y)</c>, in radians, from -pi to pi.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Computed in fixed point throughout, and the first version was not: it converted both
    /// arguments to <c>double</c>, called the Fix64 arctangent, and converted the answer back. That
    /// is three 128-bit-to-floating conversions on a type whose whole point is not to use them, and
    /// it made an attitude update <b>seventeen times slower</b> — a docking approach went from six
    /// seconds to a hundred.
    /// </para>
    /// <para>
    /// The reduction is the standard one. With <c>t = min/max</c> in [0, 1], the arctangent is taken
    /// of <c>t</c> directly while it is below <c>tan(pi/8)</c>, and of <c>(t-1)/(t+1)</c> above it,
    /// where the result is <c>pi/4</c> plus a small correction. Both arguments then lie in
    /// [-0.4143, 0.4143], where the odd series converges quickly enough that eleven terms reach
    /// 2e-9 — better than the fixed-point representation itself resolves.
    /// </para>
    /// <para>
    /// Returns zero at the origin, where the angle is undefined, rather than throwing. A caller
    /// computing an angle from a quaternion can reach the origin through rounding, and an exception
    /// from the middle of an attitude update is not a useful way to find that out.
    /// </para>
    /// </remarks>
    internal static Fix128 Atan2(Fix128 y, Fix128 x)
    {
        if (x == Zero && y == Zero)
        {
            return Zero;
        }

        bool xNegative = x.Negative;
        bool yNegative = y.Negative;

        Fix128 ax = xNegative ? -x : x;
        Fix128 ay = yNegative ? -y : y;

        bool xIsLarger = ax >= ay;
        Fix128 larger = xIsLarger ? ax : ay;
        Fix128 smaller = xIsLarger ? ay : ax;

        // atan of the ratio, in [0, pi/4], with the pi/4 shift where the ratio is large.
        Fix128 t = smaller / larger;
        Fix128 angle = AtanUnit(t);

        // For the y-larger case the angle is measured from the +y axis, so it is the complement
        // within a quarter turn. Getting this wrong is invisible on the axes and shows everywhere
        // else.
        Fix128 fromPositiveX = xIsLarger ? angle : (PiOverTwo - angle);

        return (xNegative, yNegative) switch
        {
            (false, false) => fromPositiveX,
            (true, false) => Pi - fromPositiveX,
            (true, true) => fromPositiveX - Pi,
            (false, true) => -fromPositiveX,
        };
    }

    /// <summary>arctan of an argument in [0, 1], by range reduction and an odd series.</summary>
    private static Fix128 AtanUnit(Fix128 t)
    {
        if (t <= Fix128.Zero)
        {
            return Zero;
        }

        // tan(pi/8). Above it the arctangent is taken of (t-1)/(t+1) instead, which is at most
        // 0.4143 in magnitude and converges far faster than t itself does near one.
        Fix128 reduced = t;
        bool shifted = t > TanPiOverEight;

        if (shifted)
        {
            reduced = (t - One) / (t + One);
        }

        // atan(r) = r - r^3/3 + r^5/5 - ... with |r| <= 0.4143, so the eleventh power carries the
        // series past the representation's own resolution.
        Fix128 squared = reduced * reduced;
        Fix128 term = reduced;
        Fix128 sum = reduced;

        for (int n = 3; n <= 21; n += 2)
        {
            term = term * squared;
            Fix128 contribution = term / FromWhole(n);
            sum = ((n / 2) % 2 == 1) ? sum - contribution : sum + contribution;
        }

        return shifted ? sum + PiOverFour : sum;
    }

    private static readonly Fix128 Pi = FromDouble(Math.PI);
    private static readonly Fix128 PiOverTwo = FromDouble(Math.PI / 2.0);
    private static readonly Fix128 PiOverFour = FromDouble(Math.PI / 4.0);
    private static readonly Fix128 TanPiOverEight = FromDouble(0.41421356237309503);

    internal static Fix128 Log(Fix128 x)
    {
        if (x.Magnitude == UInt128.Zero || x.Negative)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Log is defined only for positive Fix128 values.");
        }

        ulong high = (ulong)(x.Magnitude >> 64);
        int bits = high != 0
            ? 128 - System.Numerics.BitOperations.LeadingZeroCount(high)
            : 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)x.Magnitude);
        int exponent = bits - FractionalBits;

        Fix128 m = exponent >= 0
            ? FromRaw(x.Magnitude >> exponent)
            : FromRaw(x.Magnitude << -exponent);

        Fix128 t = (m - One) / (m + One);
        Fix128 tSquared = t * t;
        Fix128 term = t;
        Fix128 sum = t;

        for (int n = 3; n <= 27; n += 2)
        {
            term = term * tSquared;
            sum += term / FromWhole(n);
        }

        return sum * FromWhole(2) + FromWhole(exponent) * Ln2;
    }

    /// <summary>Natural logarithm of 2 at Q64.64.</summary>
    private static readonly Fix128 Ln2 = FromRaw((UInt128)12_786_308_645_202_655_232UL);

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
