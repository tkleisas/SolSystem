namespace SolSystem.Core.Numerics;

/// <summary>
/// Integer square root and the 128-bit helpers <see cref="Fix128"/> needs.
/// </summary>
/// <remarks>
/// Everything here is exact integer arithmetic, so it produces identical results
/// on every platform. That property is the whole reason the simulation is not
/// allowed to use <see cref="float"/> or <see cref="double"/>.
/// </remarks>
internal static class IntMath
{
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

        // The Q64.64 square root of x is floor(sqrt(x) * 2^64), and with x = n / 2^64 that is
        // the integer root of D = n * 2^64. D needs 193 bits, which is why this cannot be
        // written as "root of n, then shift up": the intermediate overflows UInt128 for any
        // input above 2^62, and 2^62 kilometres is 2.6 AU — inside the solar system and
        // outside the useful range, since Jupiter alone is at 5.2.
        //
        // So the root is built one bit at a time by the bit-pair method, reading D's bits
        // where they live rather than materialising them. Bit i of D is zero for i < 64 and
        // for i >= 192, and is bit i-64 of n in between; the pairs below walk k from the top
        // of the 97-bit root down to zero.
        UInt128 remaining = UInt128.Zero;
        UInt128 root = UInt128.Zero;

        for (int k = 96; k >= 0; k--)
        {
            ulong pair = 0;
            for (int j = 0; j < 2; j++)
            {
                int source = 2 * k + j - 64;
                if (source >= 0 && source <= 127 && ((n >> source) & UInt128.One) != UInt128.Zero)
                {
                    pair |= 1UL << j;
                }
            }

            remaining = (remaining << 2) | pair;

            UInt128 trial = (root << 2) | UInt128.One;
            if (remaining >= trial)
            {
                remaining -= trial;
                root = (root << 1) | UInt128.One;
            }
            else
            {
                root <<= 1;
            }
        }

        return root;
    }

}
