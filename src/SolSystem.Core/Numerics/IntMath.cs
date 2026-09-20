namespace SolSystem.Core.Numerics;

/// <summary>
/// Integer square root and the 128-bit helpers the fixed-point type needs.
/// </summary>
/// <remarks>
/// Everything here is exact integer arithmetic, so it produces identical results
/// on every platform. That property is the whole reason the simulation is not
/// allowed to use <see cref="float"/> or <see cref="double"/>.
/// </remarks>
internal static class IntMath
{
    /// <summary>
    /// Floor of the square root of a non-negative 64-bit integer, by bit-pair
    /// extraction. Exact: the result r satisfies r*r &lt;= n &lt; (r+1)*(r+1).
    /// </summary>
    /// <remarks>
    /// A seedless bit-pair algorithm rather than Newton's method. Newton is fewer
    /// iterations on paper, but it needs a correct initial guess and a correction
    /// pass, and getting those subtle is how a square root ends up wrong once in
    /// every few billion calls. This version has no seed and no correction step.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="n"/> is negative.</exception>
    internal static ulong Isqrt(ulong n)
    {
        ulong result = 0;

        // Start at the largest power of four that is <= n. Taken from the bit length
        // rather than a fixed 2^62: a fixed ceiling silently returns 0 for any input
        // larger than it, which is exactly the range Fix64.Sqrt operates in.
        int bitLength = 64 - System.Numerics.BitOperations.LeadingZeroCount(n);
        ulong bit = bitLength <= 0 ? 0UL : 1UL << ((bitLength - 1) & ~1);

        while (bit != 0)
        {
            if (n >= result + bit)
            {
                n -= result + bit;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }

            bit >>= 2;
        }

        return result;
    }

    /// <summary>Floor of the square root of a non-negative value.</summary>
    internal static ulong Isqrt(long n) => n < 0
        ? throw new ArgumentOutOfRangeException(nameof(n), n, "Isqrt is undefined for negative values.")
        : Isqrt((ulong)n);

    /// <summary>
    /// Floor of the square root of a non-negative 128-bit integer.
    /// </summary>
    /// <remarks>
    /// Needed because a Q32.32 square root shifts its input left by 32 bits, which
    /// takes an 80-bit value to 112 bits. Computing that in 64 bits silently
    /// truncates for every value at or above 1.0 — the failure mode is a square root
    /// of zero for large inputs and nonsense for others.
    /// </remarks>
    internal static UInt128 Isqrt(UInt128 n)
    {
        UInt128 result = UInt128.Zero;

        // BitOperations has no UInt128 overload, so the leading-zero count is taken
        // from whichever half holds the value.
        ulong high = (ulong)(n >> 64);
        ulong low = (ulong)n;
        int bitLength = high != 0
            ? 128 - System.Numerics.BitOperations.LeadingZeroCount(high)
            : 64 - System.Numerics.BitOperations.LeadingZeroCount(low);

        UInt128 bit = bitLength <= 0
            ? UInt128.Zero
            : UInt128.One << ((bitLength - 1) & ~1);

        while (bit != UInt128.Zero)
        {
            if (n >= result + bit)
            {
                n -= result + bit;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }

            bit >>= 2;
        }

        return result;
    }

    /// <summary>
    /// <c>(numerator &lt;&lt; 64) / denominator</c> for values below 2^127, without ever
    /// forming the shifted numerator.
    /// </summary>
    /// <remarks>
    /// Shifting first is not possible: <c>numerator &lt;&lt; 64</c> overflows
    /// <see cref="UInt128"/> for any numerator at or above 1.0 in a Q64.64 value, and
    /// C# truncates silently. The quotient is instead assembled from an exact 2-limb
    /// long division: each step keeps its remainder below the divisor, so the running
    /// value stays under 2^127 and the top limb comes out zero for in-range inputs.
    /// </remarks>
    /// <summary>
    /// The top 128 bits of the product of two 128-bit values, which is what a fixed-point
    /// multiply wants: the true product shifted down by 64 bits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Writing <c>a·b</c> as <c>ah·bh·2^128 + cross·2^64 + lowLow</c>, the shifted result
    /// keeps <c>ah·bh·2^64</c>, all of <c>cross</c>, and the carry out of <c>lowLow</c>.
    /// The partial products must be ADDED together with the cross terms carrying, not
    /// assembled from selected 64-bit halves.
    /// </para>
    /// <para>
    /// Note carefully which half moves where: <c>lowLow</c> is shifted DOWN by 64 and
    /// <c>ah·bh</c> is shifted UP by 64. Getting that pair the wrong way round discards
    /// the entire integer part of the result and keeps only its low word, which still
    /// looks plausible for values near one.
    /// </para>
    /// </remarks>
    internal static UInt128 MultiplyHigh128(UInt128 a, UInt128 b)
    {
        ulong al = (ulong)a, ah = (ulong)(a >> 64);
        ulong bl = (ulong)b, bh = (ulong)(b >> 64);

        UInt128 lowLow = (UInt128)al * bl;
        UInt128 cross = (UInt128)al * bh + (UInt128)ah * bl;
        UInt128 top = (UInt128)ah * bh;

        return (lowLow >> 64) + cross + (top << 64);
    }

    /// <summary>
    /// <c>floor((numerator &lt;&lt; 64) / denominator)</c>, for a numerator below 2^126.
    /// </summary>
    /// <remarks>
    /// The shift is never materialised, because it needs up to 190 bits and
    /// <see cref="UInt128"/> shifts truncate silently — the single mistake behind most of
    /// the bugs this type produced. The quotient's 64 bits are instead produced from the
    /// top, with the running remainder held below the divisor so it always fits.
    /// </remarks>
    internal static UInt128 ShiftedDivide(UInt128 numerator, UInt128 denominator)
    {
        if (denominator == UInt128.Zero)
        {
            throw new DivideByZeroException();
        }

        const ulong scaleLimit = 0xFFFF_FFFF_FFFF_FFFFUL; // 2^64 - 1
        if (numerator / denominator > scaleLimit)
        {
            throw new OverflowException(
                "ShiftedDivide quotient does not fit in 128 bits; the result leaves the type's range.");
        }

        // Split the shifted quotient into its whole and fractional halves:
        //   (n << 64) / d = (n / d) << 64 + ((n % d) << 64) / d
        // The first term is exact integer division; the second is a division of a value
        // below d*2^64, so its 64-bit quotient is produced bit by bit with a running
        // remainder that stays below d. Neither half ever forms n << 64, which needs up to
        // 190 bits and would truncate silently in a UInt128.
        UInt128 whole = numerator / denominator;
        UInt128 remainder = numerator % denominator;

        UInt128 fraction = UInt128.Zero;
        for (int bit = 63; bit >= 0; bit--)
        {
            // remainder < denominator, so doubling it stays inside 128 bits.
            remainder <<= 1;
            if (remainder >= denominator)
            {
                remainder -= denominator;
                fraction |= UInt128.One << bit;
            }
        }

        // The bit dropped by the final doubling can still close the gap by one.
        if (remainder >= denominator)
        {
            fraction += UInt128.One;
        }

        return (whole << 64) + fraction;
    }

    /// <summary>
    /// Floor of the square root of a non-negative 128-bit integer, by bit-pair
    /// extraction. Seedless and exact.
    /// </summary>
    internal static UInt128 Sqrt128(UInt128 n)
    {
        if (n == UInt128.Zero)
        {
            return UInt128.Zero;
        }

        ulong high = (ulong)(n >> 64);
        int bits = high != 0
            ? 128 - System.Numerics.BitOperations.LeadingZeroCount(high)
            : 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)n);

        // The largest power of four not exceeding n.
        UInt128 root = UInt128.Zero;
        UInt128 bit = UInt128.One << (((bits - 1) / 2) * 2);

        while (bit != UInt128.Zero)
        {
            if (n >= root + bit)
            {
                n -= root + bit;
                root = (root >> 1) + bit;
            }
            else
            {
                root >>= 1;
            }

            bit >>= 2;
        }

        return root;
    }

    /// <summary>
    /// Floor of the square root of <c>n &lt;&lt; 64</c> for <c>n</c> below 2^126, so the
    /// result is the Q64.64 square root of <c>n</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>n &lt;&lt; 64</c> needs up to 190 bits and is never formed. Instead the root is
    /// split into its known part and a correction, using the integer square root of
    /// <c>n</c> and the exact step between successive squares:
    /// </para>
    /// <code>
    ///   root      = isqrt(n) &lt;&lt; 32
    ///   remaining = (n - isqrt(n)^2) &lt;&lt; 64
    ///   step      = 2*root + 1
    ///   correction = max c such that  remaining &gt;= c*step + c*c
    /// </code>
    /// <para>
    /// The first estimate <c>remaining / step</c> is high or exact, because
    /// <c>c*step + c^2 &gt; c*step</c>. One exact test then brings it down. Everything
    /// here fits in 128 bits: <c>step</c> is below 2^97, the correction below 2^32, and
    /// <c>c*step + c*c</c> below 2^129 only for c at its maximum, which the division
    /// bound already excludes.
    /// </para>
    /// <para>
    /// This went through several wrong forms before landing here, all of which
    /// under-estimated the root by a large factor rather than by one: treating the
    /// correction as a single bit, and dividing by an unscaled step. The identity
    /// <c>sqrt(n &lt;&lt; 64) == sqrt(n) &lt;&lt; 32</c> holds only for perfect squares, and
    /// the gap is what the correction is for.
    /// </para>
    /// </remarks>
    internal static UInt128 SqrtScaled(UInt128 n)
    {
        if (n == UInt128.Zero)
        {
            return UInt128.Zero;
        }

        UInt128 baseRoot = Sqrt128(n);
        UInt128 remainder = n - (baseRoot * baseRoot);

        UInt128 root = baseRoot << (FractionalScale / 2);
        UInt128 step = (root << 1) + UInt128.One;
        UInt128 remaining = remainder << FractionalScale;

        // First estimate, then one exact correction downwards.
        UInt128 correction = remaining / step;
        while (correction > UInt128.Zero
            && remaining < correction * step + correction * correction)
        {
            correction--;
        }

        return root + correction;
    }

    /// <summary>The scale used by <see cref="SqrtScaled"/>, matching the Q64.64 type.</summary>
    private const int FractionalScale = 64;

    /// <summary>
    /// Signed 128-bit product of two 64-bit integers.
    /// </summary>
    /// <remarks>
    /// This is the operation that makes or breaks a Q32.32 type. Multiplying two
    /// fixed-point values overflows a 64-bit intermediate for almost any realistic
    /// magnitude, so the product must be taken at full width before it is shifted
    /// back down.
    /// </remarks>
    internal static Int128 Mul128(long a, long b) => (Int128)a * b;

    /// <summary>
    /// Arithmetic right shift of a 128-bit value by 32 — the narrowing half of a
    /// fixed-point multiply. Arithmetic shift rounds towards negative infinity,
    /// which keeps <c>Fix64</c> division and multiplication floors consistent.
    /// </summary>
    internal static long NarrowMul(Int128 product) => (long)(product >> Fix64.FractionalBits);
}
