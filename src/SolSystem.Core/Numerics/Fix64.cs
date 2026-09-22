using System.Globalization;

namespace SolSystem.Core.Numerics;

/// <summary>
/// A signed Q32.32 fixed-point number: 32 bits of integer, 32 bits of fraction.
/// </summary>
/// <remarks>
/// <para>
/// The simulation's unit is the <b>megametre</b> (Mm), so the plain
/// <see cref="long"/> inside this type is a count of 2^-32 Mm. That gives a range
/// of ±2.147e9 Mm (about ±14 300 AU) with a resolution of 2^-32 Mm, which is
/// 233 nanometres. The whole solar system fits in one type, and no origin shifting
/// or rescaling is ever needed to keep a planet and a docking port in the same
/// coordinate space.
/// </para>
/// <para>
/// Every operation is exact integer arithmetic over <see cref="long"/> and
/// <see cref="Int128"/>, so two machines running the same code produce
/// bit-identical results. Multiplication must not be written as
/// <c>(a * b) >> 32</c> — that overflows silently for any realistic magnitude.
/// </para>
/// <para>
/// This type is <c>internal</c> while the numerics are being proven out; it is
/// expected to become public once the spike has settled the design.
/// </para>
/// </remarks>
internal readonly struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>
{
    /// <summary>Number of fractional bits. The other 32 are the integer part.</summary>
    internal const int FractionalBits = 32;

    /// <summary>2^32 — one whole unit.</summary>
    internal const long OneRaw = 1L << FractionalBits;

    /// <summary>The largest representable value, in raw units (integer part 0x7FFFFFFF).</summary>
    internal const long MaxRaw = long.MaxValue;

    /// <summary>The smallest representable value, in raw units.</summary>
    internal const long MinRaw = long.MinValue;

    /// <summary>
    /// The largest whole-unit magnitude, 2^31 - 1.
    /// </summary>
    /// <remarks>
    /// Worth naming explicitly, because <c>MaxValue.ToDouble()</c> rounds to
    /// 2147483648 — one more than the true value — so an error message or a test that
    /// quotes that number is quoting something the type cannot represent.
    /// </remarks>
    internal const long MaxWholeUnits = long.MaxValue >> FractionalBits;

    internal readonly long Raw;

    /// <summary>True when the value is exactly zero.</summary>
    internal bool IsZero => Raw == 0;

    private Fix64(long raw) => Raw = raw;

    /// <summary>Wraps a raw 2^-32 unit count. The only way to build a value without scaling.</summary>
    internal static Fix64 FromRaw(long raw) => new(raw);

    /// <summary>
    /// Converts a physical value in the frame's own unit.
    /// </summary>
    /// <remarks>
    /// Use this for measured constants, and <see cref="FromRaw"/> only when a raw bit
    /// pattern is genuinely what is meant. <c>FromRaw(1_711_975_862)</c> is the value
    /// 0.3986, not 3.986 × 10⁻⁴: the raw count is the value <b>times</b> 2³², and reaching
    /// for the raw constructor with a physical number in hand gets the scale wrong by
    /// 2³² in one direction or by a factor of ten in the other.
    /// </remarks>
    internal static Fix64 FromValue(double value) => FromDouble(value);

    /// <summary>Converts a whole number of megametres.</summary>
    internal static Fix64 FromMm(long mm)
    {
        Int128 raw = (Int128)mm << FractionalBits;
        if (raw > MaxRaw || raw < MinRaw)
        {
            throw new OverflowException(
                $"{mm} whole units is outside the representable range of Fix64 "
                + $"(maximum magnitude is {MaxWholeUnits} whole units).");
        }

        return new Fix64((long)raw);
    }

    /// <summary>
    /// Converts metres to megametres, rounding to the nearest representable value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The nearest representable value is exact in the only sense the type can mean: no
    /// metre value is lost below the type's own grid, because 2^-32 Mm is 233
    /// nanometres. The earlier form evaluated <c>OneRaw / 1_000_000</c> as integers
    /// first — 4 294 rather than 4 294.967296, a 225 ppm error on every metre converted —
    /// and multiplied in <see cref="long"/>, which wraps past about 2.1 × 10¹⁵ metres.
    /// The multiply is taken at <see cref="Int128"/> and the result range-checked.
    /// </para>
    /// </remarks>
    internal static Fix64 FromMetres(long metres)
    {
        Int128 scaled = (Int128)metres * OneRaw;

        // Round to nearest, half away from zero: integer division truncates toward
        // zero, so the round is the divisor's half added with the value's own sign.
        Int128 raw = scaled >= 0
            ? (scaled + 500_000) / 1_000_000
            : -((-scaled + 500_000) / 1_000_000);

        if (raw > MaxRaw || raw < MinRaw)
        {
            throw new OverflowException(
                $"{metres} metres is outside the representable range of Fix64 "
                + $"(maximum magnitude is {MaxWholeUnits} whole units).");
        }

        return new Fix64((long)raw);
    }

    // ---------------------------------------------------------------- constants

    internal static readonly Fix64 Zero = new(0);
    internal static readonly Fix64 One = new(OneRaw);
    internal static readonly Fix64 Half = new(OneRaw >> 1);
    internal static readonly Fix64 Two = new(OneRaw << 1);
    internal static readonly Fix64 MaxValue = new(MaxRaw);
    internal static readonly Fix64 MinValue = new(MinRaw);

    /// <summary>Natural logarithm of 2, at the width the logarithm's tail needs.</summary>
    private static readonly Fix64 Ln2 = FromRaw(2_977_044_472);

    // ---------------------------------------------------------------- arithmetic

    public static Fix64 operator +(Fix64 a, Fix64 b) => new(a.Raw + b.Raw);

    public static Fix64 operator -(Fix64 a, Fix64 b) => new(a.Raw - b.Raw);

    public static Fix64 operator -(Fix64 a) => new(-a.Raw);

    /// <summary>Fixed-point multiply, exact in the intermediate at 128 bits.</summary>
    public static Fix64 operator *(Fix64 a, Fix64 b) => new(IntMath.NarrowMul(IntMath.Mul128(a.Raw, b.Raw)));

    /// <summary>
    /// Fixed-point divide, truncating towards negative infinity to match
    /// <see cref="int"/> division.
    /// </summary>
    /// <remarks>
    /// The numerator is scaled up by 32 bits first, so this overflows when
    /// <c>|a|</c> approaches <see cref="MaxValue"/> and <c>|b|</c> is small. The
    /// safe envelope is |a| &lt; 2^30 whole units. A checked variant is a planned
    /// addition; until then, callers must keep dividends away from the extremes.
    /// </remarks>
    public static Fix64 operator /(Fix64 a, Fix64 b)
    {
        if (b.Raw == 0)
        {
            throw new DivideByZeroException("Fix64 division by zero.");
        }

        Int128 numerator = (Int128)a.Raw << FractionalBits;
        return new((long)(numerator / b.Raw));
    }

    public static Fix64 operator %(Fix64 a, Fix64 b)
    {
        if (b.Raw == 0)
        {
            throw new DivideByZeroException("Fix64 remainder by zero.");
        }

        return new(a.Raw % b.Raw);
    }

    // ---------------------------------------------------------------- comparison

    public bool Equals(Fix64 other) => Raw == other.Raw;

    public override bool Equals(object? obj) => obj is Fix64 other && Equals(other);

    public override int GetHashCode() => Raw.GetHashCode();

    public int CompareTo(Fix64 other) => Raw.CompareTo(other.Raw);

    public static bool operator ==(Fix64 a, Fix64 b) => a.Raw == b.Raw;

    public static bool operator !=(Fix64 a, Fix64 b) => a.Raw != b.Raw;

    public static bool operator <(Fix64 a, Fix64 b) => a.Raw < b.Raw;

    public static bool operator >(Fix64 a, Fix64 b) => a.Raw > b.Raw;

    public static bool operator <=(Fix64 a, Fix64 b) => a.Raw <= b.Raw;

    public static bool operator >=(Fix64 a, Fix64 b) => a.Raw >= b.Raw;

    // ---------------------------------------------------------------- functions

    public static Fix64 Abs(Fix64 a) => new(a.Raw >= 0 ? a.Raw : -a.Raw);

    /// <summary>
    /// Square root, exact to the last representable bit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The result is <c>Isqrt(raw &lt;&lt; 32)</c>, which is the largest r with
    /// <c>r*r &lt;= value</c> in raw units — the exactly correctly rounded
    /// fixed-point square root for a non-negative input.
    /// </para>
    /// <para>
    /// The shift must be taken at 128 bits. In 64 bits it truncates for every input
    /// at or above 1.0 and returns a square root of zero.
    /// </para>
    /// </remarks>
    public static Fix64 Sqrt(Fix64 a)
    {
        if (a.Raw < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(a), "Sqrt is undefined for negative Fix64 values.");
        }

        if (a.Raw == 0)
        {
            return Zero;
        }

        UInt128 root = IntMath.Isqrt((UInt128)(ulong)a.Raw << FractionalBits);
        return new((long)root);
    }

    public static Fix64 Min(Fix64 a, Fix64 b) => a.Raw < b.Raw ? a : b;

    public static Fix64 Max(Fix64 a, Fix64 b) => a.Raw > b.Raw ? a : b;

    // ---------------------------------------------------------------- conversion

    /// <summary>
    /// Natural logarithm.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Needed for the rocket equation: the delta-v a ship has left is
    /// <c>Isp·g₀·ln(mass / dryMass)</c>. There is no way to express that without a
    /// logarithm, and the design prices everything in delta-v, so this is load-bearing
    /// rather than a convenience.
    /// </para>
    /// <para>
    /// Computed by splitting the value into <c>2^k · m</c> with <c>m</c> in [1, 2), then
    /// <c>ln(m)</c> from the atanh series
    /// <c>2·(t + t³/3 + t⁵/5 + …)</c> with <c>t = (m-1)/(m+1)</c> ≤ 1/3. That converges
    /// quickly at this width and needs no table.
    /// </para>
    /// </remarks>
    internal static Fix64 Log(Fix64 x)
    {
        if (x.Raw <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Log is defined only for positive Fix64 values.");
        }

        // Split x into 2^exponent · m with m in [1, 2).
        //
        // `k` is the index of raw's top bit, which is the value's binary exponent PLUS
        // FractionalBits — raw is x scaled by 2^32. Using k as the exponent directly
        // multiplies the result by 2^32, so ln(1) comes out as 32·ln2 rather than 0 and
        // every logarithm is wrong by that constant.
        int k = 63 - System.Numerics.BitOperations.LeadingZeroCount((ulong)x.Raw);
        int exponent = k - FractionalBits;
        Fix64 m = exponent >= 0
            ? FromRaw(x.Raw >> exponent)
            : FromRaw(x.Raw << -exponent);

        Fix64 t = (m - One) / (m + One);
        Fix64 tSquared = t * t;
        Fix64 term = t;
        Fix64 sum = t;

        // Fourteen odd terms is ample: each carries another factor of 1/9. The divisors
        // must be the INTEGERS 3, 5, 7 …, which is FromDouble(n) — NOT FromMm(n), which
        // scales by 2^32 and would divide each term by n·2^32.
        for (int n = 3; n <= 27; n += 2)
        {
            term = term * tSquared;
            sum += term / FromDouble(n);
        }

        return sum * Two + FromDouble(exponent) * Ln2;
    }

    /// <summary>
    /// Rounds a <see cref="double"/> to the nearest representable value. For test
    /// and diagnostic setup only — a simulation that reaches for this has stopped
    /// being reproducible.
    /// </summary>
    internal static Fix64 FromDouble(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Not a finite value.");
        }

        Int128 raw = (Int128)Math.Round(value * OneRaw, MidpointRounding.ToEven);
        if (raw > MaxRaw || raw < MinRaw)
        {
            // Report the magnitude in whole units as well: "4487936100" on its own is
            // the raw count, and a caller who sees that number will think the limit is
            // four billion when it is really about two point one billion.
            throw new OverflowException(
                $"Value {value} is outside the representable range of Fix64 "
                + $"(maximum magnitude is {MaxWholeUnits} whole units).");
        }

        return new Fix64((long)raw);
    }

    /// <summary>
    /// Converts to <see cref="double"/>. Lossy by construction, and permitted only
    /// at the render and diagnostic boundary — never inside the simulation.
    /// </summary>
    internal double ToDouble() => Raw / (double)OneRaw;

    public override string ToString() =>
        ToDouble().ToString("0.###############", CultureInfo.InvariantCulture);
}
