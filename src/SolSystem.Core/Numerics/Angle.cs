using System.Numerics;

namespace SolSystem.Core.Numerics;

/// <summary>
/// Angles as Q32.32 turns: a full revolution is exactly 2^32 raw units, so
/// wrapping is a 32-bit addition and costs nothing.
/// </summary>
/// <remarks>
/// Turns rather than radians because radians are irrational — pi cannot be written
/// down in any fixed-point format, and every angle would carry that error. With
/// turns, <c>Sin(0) == 0</c> and <c>Cos(0) == One</c> hold exactly, and a quarter
/// turn is exactly <c>0x4000_0000</c>. Degrees, radians and turns all convert at
/// the boundary; only turns live inside.
/// </remarks>
internal static class Angle
{
    /// <summary>A quarter turn, in raw units. Exactly representable.</summary>
    internal const uint QuarterTurn = 0x4000_0000;

    /// <summary>A half turn, in raw units. Exactly representable.</summary>
    internal const uint HalfTurn = 0x8000_0000;

    /// <summary>
    /// Table entries per quadrant. Raising this is the only lever on sine accuracy;
    /// see the interpolation error note on <see cref="Sin"/>.
    /// </summary>
    internal const int TableBits = 12;

    internal const int TableSize = 1 << TableBits;

    /// <summary>Fractional bits remaining in the angle after the table index is taken.</summary>
    internal const int RemainderBits = 32 - TableBits - 2;

    /// <summary>
    /// Sine of the first quadrant, at <c>TableSize + 1</c> uniform steps over
    /// [0, pi/2]. Entries are computed independently from <see cref="Math.Sin"/>
    /// rather than accumulated, so the table carries no compounding error of its own.
    /// </summary>
    internal static readonly long[] SinTable = BuildSinTable();

    /// <summary>
    /// Arctangent of the first octant, at <c>TableSize + 1</c> uniform steps over
    /// [0, 1], in raw turn units. Same table size and same interpolation argument as
    /// the sine table.
    /// </summary>
    private static readonly long[] AtanTable = BuildAtanTable();

    /// <summary>
    /// Fractional bits below the arctangent table's index.
    /// </summary>
    /// <remarks>
    /// This is <em>not</em> <see cref="RemainderBits"/>. Sin's input is a 32-bit turn
    /// with two quadrant bits above the table, leaving 30 bits of angle, so its index
    /// is 12 bits and its remainder 18. An arctangent ratio is a Q32.32 value in
    /// [0, 1] with no quadrant bits above it: 32 bits of fraction, 12 for the index,
    /// leaving 20. Reusing the sine's shift here made the index run to 16383 against
    /// a 4098-entry table and read past the end.
    /// </remarks>
    private const int AtanRemainderBits = 32 - TableBits;

    private static long[] BuildAtanTable()
    {
        var table = new long[TableSize + 2];
        for (int i = 0; i <= TableSize; i++)
        {
            double turn = Math.Atan((double)i / TableSize) / (2.0 * Math.PI);
            table[i] = (long)Math.Round(turn * Fix64.OneRaw);
        }

        // The endpoint is atan(1) = pi/4, which is an EIGHTH of a turn, not a quarter.
        // Writing a quarter here scaled every angle by two, and the error is invisible
        // on the axes — it only appears away from them.
        long eighthTurn = QuarterTurn / 2;

        table[0] = 0;
        table[TableSize] = eighthTurn;
        table[TableSize + 1] = eighthTurn;
        return table;
    }

    private static long[] BuildSinTable()
    {
        // TableSize + 2 entries: index + 1 must be safe when the index lands exactly
        // on the last entry, which happens for angles precisely on a quadrant
        // boundary.
        var table = new long[TableSize + 2];
        for (int i = 0; i <= TableSize; i++)
        {
            double radians = (Math.PI / 2.0) * i / TableSize;
            table[i] = (long)Math.Round(Math.Sin(radians) * Fix64.OneRaw);
        }

        // Pin the endpoints exactly. The rounded values may or may not already be
        // exact, and these two are the cases callers will assert on.
        table[0] = 0;
        table[TableSize] = Fix64.OneRaw;
        table[TableSize + 1] = Fix64.OneRaw;
        return table;
    }

    public static uint FromRaw(uint raw) => raw;

    public static uint ToRaw(uint turn) => turn;

    /// <summary>Wraps a raw turn count into a <see cref="Fix64"/> value in [0, 1).</summary>
    public static Fix64 RawToUnit(uint turn) => Fix64.FromRaw((long)(turn & 0xFFFF_FFFF) << Fix64.FractionalBits);

    /// <summary>
    /// Sine of an angle given in turns.
    /// </summary>
    /// <remarks>
    /// Quadrant reduction is bit extraction and a negation, so the only error is
    /// the interpolation between table entries: of order
    /// <c>(pi/2 / 4096)^2 / 8</c>, about 4.6e-8 in raw units of the output — around
    /// a hundred times the 2^-32 step of the input angle, and far below anything
    /// the simulation can act on. Halving the table step quarters this error.
    /// </remarks>
    public static Fix64 Sin(uint turn)
    {
        int quadrant = (int)(turn >> 30);
        uint within = turn & (QuarterTurn - 1);
        int index = (int)(within >> RemainderBits);
        long remainder = within & ((1u << RemainderBits) - 1);

        // `fraction` is the position between entry `index` and entry `index + 1`, in
        // Q32.32, so it ranges over [0, 1] INCLUSIVE.
        long fraction = remainder << (Fix64.FractionalBits - RemainderBits);

        // In quadrants 1 and 3 the sine is measured back from the quarter turn:
        // sin(pi - a) = sin(a). The sample therefore moves to the mirror position
        // `TableSize - 1 - index` with fraction `1 - fraction`.
        //
        // Getting this right is fiddly in a way that does not show up at the cardinal
        // angles, where index lands on an entry and the fraction is zero. Three separate
        // mistakes here each cost 3.8e-4 of sine error, none of them visible at a
        // quadrant boundary:
        //   - mirroring the index without complementing the fraction;
        //   - `TableSize - index` instead of `TableSize - 1 - index`, which is one entry
        //     out;
        //   - complementing the 18-bit remainder instead of the 32-bit fraction, which
        //     can never reach exactly 1.
        bool mirrored = quadrant == 1 || quadrant == 3;
        if (mirrored)
        {
            index = (TableSize - 1) - index;
            fraction = Fix64.OneRaw - fraction;
        }

        long a = SinTable[index];
        long b = index + 1 < SinTable.Length ? SinTable[index + 1] : SinTable[index];

        // The interpolation walks forwards in every quadrant: the mirrored angle still
        // increases with `within`. Only the index and the fraction are complemented.
        long value = a + (long)(((Int128)(b - a) * fraction) >> Fix64.FractionalBits);

        return Fix64.FromRaw(quadrant >= 2 ? -value : value);
    }

    /// <summary>Cosine of an angle given in turns. A sine with the angle advanced a quarter turn.</summary>
    public static Fix64 Cos(uint turn) => Sin(unchecked(turn + QuarterTurn));

    /// <summary>
    /// Arctangent of the ratio y/x as the angle of the point (x, y), in turns.
    /// </summary>
    /// <remarks>
    /// <paramref name="x"/> and <paramref name="y"/> are ordinary fixed-point
    /// values (megametres, typically), not turns. Scaling is taken out before the
    /// division so that <c>y/x</c> lands in [0, 1] with full precision, which is
    /// what keeps an angle stable when the inputs are small.
    /// </remarks>
    public static uint Atan2(Fix64 y, Fix64 x)
    {
        if (x.Raw == 0 && y.Raw == 0)
        {
            throw new ArgumentException("Atan2 is undefined at the origin.", nameof(y));
        }

        bool xNegative = x.Raw < 0;
        bool yNegative = y.Raw < 0;

        // Absolute values as unsigned. Negating first and casting afterwards would be
        // wrong in two ways: -long.MinValue is undefined, and the cast itself would
        // reinterpret the bit pattern rather than take a magnitude.
        ulong ax = xNegative ? (ulong)(-x.Raw) : (ulong)x.Raw;
        ulong ay = yNegative ? (ulong)(-y.Raw) : (ulong)y.Raw;

        // The octant is decided by comparing magnitudes, and the arc tangent is always
        // taken of the smaller over the larger, so its argument lies in [0, 1] where
        // the table covers it exactly. Reflecting afterwards is what keeps the mapping
        // right in the second and fourth quadrants, where the bare ratio is ambiguous.
        ulong larger = Math.Max(ax, ay);
        ulong smaller = Math.Min(ax, ay);
        bool xIsLarger = ax >= ay;

        uint alpha = AtanUnitRatio(DivideToUnit(smaller, larger));

        // alpha is atan(smaller/larger). For the x-larger case that is already the
        // angle from the +x axis; for the y-larger case it is measured from the +y
        // axis instead, so it is the complement within a quarter turn. Getting this
        // wrong is invisible on the axes and only shows up away from them.
        uint fromPositiveX = xIsLarger ? alpha : unchecked(QuarterTurn - alpha);

        return (xNegative, yNegative) switch
        {
            (false, false) => fromPositiveX,
            (true, false) => unchecked(HalfTurn - fromPositiveX),
            (true, true) => unchecked(HalfTurn + fromPositiveX),
            (false, true) => unchecked(0u - fromPositiveX),
        };
    }

    /// <summary>
    /// <paramref name="numerator"/> / <paramref name="denominator"/> as a Q32.32
    /// value, both given as unsigned magnitudes with the numerator the smaller, so
    /// the result lies in [0, 1].
    /// </summary>
    /// <remarks>
    /// The result is returned at 64 bits rather than 32 because a Q32.32 value of
    /// exactly 1.0 is 2^32, which does not fit in a <see cref="uint"/>. The numerator
    /// is shifted up as far as it will go before the divide, so the quotient uses the
    /// width of the 128-bit intermediate instead of losing its high bits.
    /// </remarks>
    private static ulong DivideToUnit(ulong numerator, ulong denominator)
    {
        if (numerator == 0)
        {
            return 0;
        }

        if (numerator == denominator)
        {
            return Fix64.OneRaw;
        }

        int headroom = BitOperations.LeadingZeroCount(numerator);
        UInt128 dividend = (UInt128)numerator << (Fix64.FractionalBits + headroom);
        return (ulong)((dividend / denominator) >> headroom);
    }

    /// <summary>Arctangent of a Q32.32 value in [0, 1], returned in raw turn units.</summary>
    /// <remarks>
    /// A table rather than a series. The Taylor series for arctangent converges
    /// unusably slowly near t = 1 — fourteen terms still leave about two degrees of
    /// error, because it is the alternating harmonic series at the boundary — so it
    /// is the wrong tool here despite being the textbook answer.
    /// <para>
    /// Linear interpolation over 4096 entries: the step is 2^-12, and |atan''| is
    /// bounded by 0.65 on [0, 1], so the error is at most
    /// <c>0.65 * (2^-12)^2 / 8</c>, about 4.8e-9 radians, or 1e-9 of a turn. That is
    /// well inside the fixed-point resolution of the inputs.
    /// </para>
    /// </remarks>
    private static uint AtanUnitRatio(ulong ratio)
    {
        if (ratio == 0)
        {
            return 0;
        }

        long eighthTurn = QuarterTurn / 2;

        if (ratio >= (ulong)Fix64.OneRaw)
        {
            return (uint)eighthTurn;
        }

        int index = (int)(ratio >> AtanRemainderBits);
        if (index >= TableSize)
        {
            return (uint)eighthTurn;
        }

        long remainder = (long)(ratio & ((1UL << AtanRemainderBits) - 1));

        long a = AtanTable[index];
        long b = AtanTable[index + 1];
        long value = a + (long)(((Int128)(b - a) * remainder) >> AtanRemainderBits);

        return (uint)value;
    }
}
