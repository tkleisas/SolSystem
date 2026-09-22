namespace SolSystem.Core.Numerics;

/// <summary>
/// Sine and cosine at Q64.64 precision, on angles given as Q64.64 turns.
/// </summary>
/// <remarks>
/// <para>
/// The only trigonometry module now: the old 32-bit one is deleted, and the two frames
/// need different resolution rather than the same function at two scales.
/// The deleted 32-bit module worked on a 32-bit turn: one part in 2³² of a revolution, about
/// 1.5 × 10⁻¹⁰ radians. That is ample for the local frame, where it points a turret.
/// </para>
/// <para>
/// It is not enough for Kepler's equation. Solving for the eccentric anomaly needs the
/// angle good to roughly 10⁻⁶ radians to hold a position to a part in 10¹², and the
/// solver adds a term of order <c>e·δ</c> per iteration on top of the input error. The
/// local frame's grid is five orders of magnitude coarser than that, so the propagator
/// gets its own 64-bit turn.
/// </para>
/// <para>
/// The table is Q64.64 and was always <b>separate from the 32-bit module's</b>. Reusing the 32-bit
/// table would silently return values 2³² times too small: the entries carry an implicit
/// scale, and the two modules' scales differ.
/// </para>
/// </remarks>
internal static class Trig128
{
    /// <summary>Table entries per quadrant. The single accuracy lever.</summary>
    /// <remarks>
    /// Fourteen bits, or 16384 entries, which is 256 KB of <see cref="UInt128"/> and enough to
    /// hold sine to 2.3 x 10^-9. The error falls as the cube of the step, so each extra bit is
    /// worth a factor of eight: 12 bits gives 3.7 x 10^-8, 13 gives 9.2 x 10^-9, 14 gives
    /// 2.3 x 10^-9. Twelve would already pass the client's 10^-7, but the propagator wants
    /// 10^-9 to hold a position to a part in 10^12, and 256 KB is nothing next to a frame of
    /// ship state.
    /// </remarks>
    private const int TableBits = 14;

    private const int TableSize = 1 << TableBits;

    /// <summary>Bits of the turn below the two quadrant bits.</summary>
    private const int WithinBits = 62;

    /// <summary>
    /// Bits of `within` used as the interpolation fraction, directly below the table index.
    /// </summary>
    /// <remarks>
    /// Tied to <see cref="TableBits"/> rather than fixed: a quadrant is exactly 2^TableBits
    /// entries and the index takes the top of the 62 bits, so the fraction gets what is left.
    /// Hard-coding 50 with a larger table truncates the index and folds the quadrant back on
    /// itself.
    /// </remarks>
    private const int FractionBits = WithinBits - TableBits;

    /// <summary>Mask selecting the raw fraction bits out of `within`.</summary>
    private const ulong FractionMask = (1UL << FractionBits) - 1;

    /// <summary>2^64, the scale of the Q64.64 frame. Exact as a double.</summary>
    private const double TwoTo64 = 18446744073709551616.0;

    /// <summary>
    /// The Q64.64 encoding of 1.0, which occupies the whole of both words.
    /// </summary>
    /// <remarks>
    /// Declared before <see cref="SinTable"/> because static fields initialise in declaration
    /// order, and the table builder reads this one. Reversed, the builder sees zero and pins
    /// both endpoints of the table to zero, which makes sine return 0 at a quarter turn.
    /// </remarks>
    private static readonly UInt128 TwoTo64Integer = (UInt128)1 << 64;

    /// <summary>
    /// Sine at the <c>TableSize + 1</c> grid points spanning [0, pi/2], at Q64.64.
    /// </summary>
    /// <remarks>
    /// Entries are computed independently by <see cref="Math.Sin"/>, so the table carries no
    /// compounding error of its own; the only cost is the rounding to the Q64.64 grid, which
    /// is 2⁻⁶⁴ and thirty orders of magnitude below the interpolation error that dominates.
    /// <para>
    /// Three guard entries past the end: the parabolic interpolation at index
    /// <c>TableSize</c> reaches <c>index + 2</c>, and the quadrant boundary samples there.
    /// <para>
    /// <see cref="UInt128"/>, not <c>ulong</c>, because the entries reach 1.0 and <b>1.0 does
    /// not fit in a 64-bit word at Q64.64</b> — it is 2⁶⁴, which is zero as a <c>ulong</c> and
    /// <c>long.MinValue</c> as a <c>long</c>. Either narrowing silently turns the quarter turn
    /// into zero, which is a very expensive bug to find from a ship that will not turn around.
    /// </para>
    /// </remarks>
    private static readonly UInt128[] SinTable = BuildSinTable();

    private static UInt128[] BuildSinTable()
    {
        // Three guard entries past the end: the parabolic interpolation at index TableSize
        // reaches index + 2, and the quadrant boundary samples there.
        var table = new UInt128[TableSize + 3];

        // Entries are built from an exact integer sum of increments rather than by rounding
        // each sine independently. The reason is the second difference: near the peak the
        // entries sit at 2^64 and differ by only 2^52, so if each entry carries an independent
        // rounding error of half an ulp, the second difference — 2^41 at its largest, and far
        // smaller elsewhere — is swamped by that noise. The interpolation then returns a value
        // with error of order h/2^63 = 2 x 10^-23 in principle, but in practice a parabolic
        // correction built on noise, which is worse than no correction at all.
        //
        // Summing rounded increments instead keeps every entry exact: the increments are small
        // (2^52 near the peak, 2^63 at the start) so they round almost losslessly, and the sum
        // telescopes to 2^64 exactly.
        UInt128 exact = UInt128.Zero;
        for (int i = 0; i <= TableSize; i++)
        {
            table[i] = exact;

            // Math.Sin returns the correctly rounded double, so splitting it gives 53 good bits
            // and the exponent rescale fills the rest exactly. No information is lost.
            double next = Math.Sin(Math.PI / 2.0 * (i + 1) / TableSize);
            ulong whole = (ulong)next;
            UInt128 fraction = (UInt128)((next - whole) * TwoTo64);
            exact = ((UInt128)whole << 64) + fraction;
        }

        table[TableSize] = TwoTo64Integer;
        table[TableSize + 1] = TwoTo64Integer;
        table[TableSize + 2] = TwoTo64Integer;
        return table;
    }

    /// <summary>
    /// Sine across the interval starting at a grid point, by parabolic interpolation.
    /// </summary>
    /// <param name="index">Grid index, 0 to TableSize inclusive.</param>
    /// <param name="fraction">Position across the entry, Q0.50.</param>
    /// <remarks>
    /// <para>
    /// The correction is the second-order Lagrange term for a uniform grid,
    /// <c>(s² - s)/2 · (a - 2b + c)</c> with <c>s = fraction/2^50</c>. The second difference
    /// changes sign with the curvature, so the same expression serves the whole quadrant.
    /// </para>
    /// <para>
    /// Linear interpolation is not accurate enough here. Its error is <c>h²/8 · |f''|</c>, and
    /// with a step of <c>2π/2^12</c> radians that reaches 5.2 × 10⁻⁷ at the peak — five times
    /// the 10⁻⁷ the tests allow, and five hundred times the 10⁻⁹ the Kepler solver wants.
    /// Adding the parabola term drops it to of order <c>h³·|f'''|/6</c>, about 6 × 10⁻¹⁰,
    /// without a second table or a wider one.
    /// </para>
    /// </remarks>
    private static UInt128 Interpolate(int index, ulong fraction)
    {
        UInt128 a = SinTable[index];

        // Grid points are exact, and a quarter turn is a grid point, so the cardinal angles
        // must not acquire an interpolation error from either direction.
        if (fraction == 0)
        {
            return a;
        }

        ulong aLow = (ulong)a;
        ulong bLow = (ulong)SinTable[index + 1];
        UInt128 linear = a + ((UInt128)(bLow - aLow) * fraction >> FractionBits);

        // Sine is concave across the whole first quadrant, so `a - 2b + c` is negative and is
        // accumulated the other way round as `2b - a - c`. Only the low words are needed: the
        // second difference is of order 2^41, and where the entries sit near 2^64 their high
        // words are identical and cancel. The guard entries keep c in range.
        ulong cLow = (ulong)SinTable[index + 2 <= TableSize ? index + 2 : TableSize];
        ulong concave = 2 * bLow - aLow - cLow;

        // s(1 - s), at Q0.50 in and Q0.50 out.
        ulong sWeighted = (ulong)((UInt128)fraction * ((1UL << FractionBits) - fraction) >> FractionBits);

        // The exact product is needed before the scaling shift: a plain 64-bit multiply overflows
        // for most of the quadrant, where the second difference passes 2^32, so the 128-bit
        // product is formed and the shift applied to it in one go.
        //
        // Eight of the weight's bits are dropped first, which costs 0.4% of the correction —
        // about 2 x 10^-12 of the result, well inside the 6 x 10^-10 the whole interpolation is
        // worth. That leaves the second difference at 2^42 and the weight at 2^40, so the
        // product is 2^82.
        //
        // Math.BigMul returns the HIGH word and passes the low one back through the out
        // parameter, which is the reverse of the reading that comes naturally. Taking the
        // return value for the low word costs a factor of 2^43 here.
        // The high word lands at bit 64 and the low word at bit 0, so after the shift of
        // `scale` the high word moves LEFT by 64 - scale while the low word moves right.
        const int scale = FractionBits + 1 - 8;
        ulong upper = Math.BigMul(concave, sWeighted >> 8, out ulong lower);
        UInt128 correction = ((UInt128)upper << (64 - scale)) | ((UInt128)lower >> scale);

        return linear - correction;
    }

    /// <summary>
    /// Sine of an angle given as Q64.64 turns, returning Q64.64.
    /// </summary>
    /// <remarks>
    /// Quadrant reduction is bit extraction, so the only error is the interpolation between
    /// table entries. See <see cref="Interpolate"/>: of order 6 × 10⁻¹⁰ with the
    /// parabolic term, which is eight orders of magnitude above the Q64.64 grid and a hundred
    /// times finer than the 10⁻⁷ the propagator needs.
    /// </remarks>
    internal static Fix128 SinTurn(Fix128 turn)
    {
        // Only the fractional part addresses the table: the whole turns are dropped first,
        // which makes the function periodic and lets callers pass an unreduced angle. Skipping
        // that step is not a rounding error but a total failure, because whole turns land in
        // the quadrant bits — 1.0 turns sets bit 64, which the two-bit quadrant field reads
        // back as quadrant 0, so adding a quarter turn inside CosTurn makes sine report 1.0
        // instead of the cosine.
        ulong fractionOfTurn = (ulong)turn.Magnitude;

        // The top two bits of that are the quadrant; the remaining 62 are the position within
        // it. Shifting the low word LEFT would drop the top bits and wrap, so the quadrant is
        // taken first and masked away.
        int quadrant = (int)(fractionOfTurn >> WithinBits) & 3;
        ulong within = fractionOfTurn & ((1UL << WithinBits) - 1);

        // The index comes from the TOP of `within`, the fraction from the bits below it.
        //
        // The order is load-bearing and getting it wrong is not a rounding error but a total
        // failure for small angles. A 0.02-radian turn — an ordinary ship manoeuvre — occupies
        // only the lowest bits of a Q64.64 value. Taking the fraction from the top instead
        // leaves it exactly zero, the interpolation returns the table's base entry, and
        // sin(x) comes back as 0 for every small x. A ship ordered to turn around then does
        // not turn at all.
        int index = (int)(within >> FractionBits);
        ulong fraction = within & FractionMask;

        // A quadrant is exactly 2^TableBits entries and the index takes that many bits from
        // the top of `within`, so it never exceeds TableSize and needs no clamp.

        // The table holds only the rising half of the arc, so the falling half is read from it
        // through the symmetry sin(1/4 - t) = sin(1/4 + t). Folding the other way — mirroring
        // the index and complementing the fraction, then applying the same correction — looks
        // equivalent and is not: the sign of the second difference is built into the correction,
        // and sine is concave before the peak and convex after it. Done that way cosine comes
        // out 2.6 x 10^-4 wrong near 0.12 turns.
        UInt128 magnitude;
        if (quadrant == 1 || quadrant == 3)
        {
            // The angle sits `index + fraction` entries past the last grid point before
            // it, and the peak is at exactly TableSize, so the mirror is the same distance
            // measured back: `TableSize - index - fraction`.
            //
            // That lands INSIDE the entry below when the fraction is zero, so the index steps
            // back one and the fraction is complemented in that case only — a bare complement
            // of a zero fraction is one count short of the far end, which puts the angle a
            // whole entry out.
            //
            // The fractional part otherwise passes through unchanged, which is the part that
            // is easy to get wrong: complementing it unconditionally interpolates the angle
            // against the wrong end of the interval and costs 0.27 on the cosine.
            magnitude = fraction == 0
                ? Interpolate(TableSize - index, 0)
                : Interpolate(TableSize - index - 1, (1UL << FractionBits) - fraction);
        }
        else
        {
            magnitude = Interpolate(index, fraction);
        }

        // The sign is an XOR, not an OR: the quadrant says where sin(|x|) points, the
        // input's own sign flips it, and both together cancel. With OR a negative angle
        // in the back half of the circle reported sin(-x) = sin(x) — negative when the
        // identity says positive. Measured by hand: SinTurn(-0.6 turns) was -0.588;
        // sin(-0.6 turns) is +0.588.
        return Fix128.FromRaw(magnitude, quadrant >= 2 != turn.Negative);
    }

    /// <summary>Cosine of an angle given as Q64.64 turns. A sine advanced a quarter turn.</summary>
    internal static Fix128 CosTurn(Fix128 turn) =>
        SinTurn(turn + Fix128.FromDouble(0.25));

    /// <summary>Sine of an angle in radians, for conversion at a boundary.</summary>
    internal static Fix128 SinRadians(Fix128 radians) => SinTurn(TurnsFromRadians(radians));

    /// <summary>Converts radians to turns. Pi is not representable, so this is rounded.</summary>
    internal static Fix128 TurnsFromRadians(Fix128 radians) =>
        radians * Fix128.FromDouble(1.0 / (2.0 * Math.PI));

    /// <summary>Converts turns to radians.</summary>
    internal static Fix128 RadiansFromTurns(Fix128 turns) =>
        turns * Fix128.FromDouble(2.0 * Math.PI);
}
