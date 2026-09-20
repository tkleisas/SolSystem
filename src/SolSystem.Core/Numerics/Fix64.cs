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

    private Fix64(long raw) => Raw = raw;

    /// <summary>Wraps a raw 2^-32 unit count. The only way to build a value without scaling.</summary>
    internal static Fix64 FromRaw(long raw) => new(raw);

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
    /// Converts metres to megametres exactly, at 2^-32 Mm resolution (which is
    /// finer than a nanometre, so no metre value is ever lost this way).
    /// </summary>
    internal static Fix64 FromMetres(long metres) => FromRaw(metres * (OneRaw / 1_000_000));

    // ---------------------------------------------------------------- constants

    internal static readonly Fix64 Zero = new(0);
    internal static readonly Fix64 One = new(OneRaw);
    internal static readonly Fix64 Half = new(OneRaw >> 1);
    internal static readonly Fix64 Two = new(OneRaw << 1);
    internal static readonly Fix64 MaxValue = new(MaxRaw);
    internal static readonly Fix64 MinValue = new(MinRaw);

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
