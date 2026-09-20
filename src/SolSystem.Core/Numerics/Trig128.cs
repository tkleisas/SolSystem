namespace SolSystem.Core.Numerics;

/// <summary>
/// Sine and cosine at Q64.64 precision, on angles given as Q64.64 turns.
/// </summary>
/// <remarks>
/// <para>
/// A second trigonometry module, alongside <see cref="Angle"/>, because the two frames
/// need different resolution rather than the same function at two scales.
/// <see cref="Angle"/> works on a 32-bit turn: one part in 2³² of a revolution, about
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
/// The table is Q64.64 and <b>separate from <see cref="Angle"/>'s</b>. Reusing the 32-bit
/// table would silently return values 2³² times too small: the entries carry an implicit
/// scale, and the two modules' scales differ.
/// </para>
/// </remarks>
internal static class Trig128
{
    /// <summary>Table entries per quadrant. The single accuracy lever.</summary>
    private const int TableBits = 12;

    private const int TableSize = 1 << TableBits;

    /// <summary>Bits of the turn below the two quadrant bits.</summary>
    private const int WithinBits = 62;

    /// <summary>Bits of `within` used as the interpolation fraction, at the top.</summary>
    private const int FractionBits = 50;

    /// <summary>Bits of `within` used as the table index, below the fraction.</summary>
    private const int TableFractionBits = WithinBits - FractionBits;

    /// <summary>Left shift that scales the raw fraction bits to Q64.64.</summary>
    private const int FractionShift = 64 - FractionBits;

    /// <summary>
    /// Scale of the table entries: 2^63, i.e. Q0.63.
    /// </summary>
    /// <remarks>
    /// NOT 2^64. A sine lies in [0, 1], and 1.0 is not representable in Q64.64 — the
    /// largest value is 1 - 2^-64. Storing entries at sin × 2^64 and then pinning the
    /// endpoint to 2^63 mixes two scales, which is what made sine report 0.5 at a quarter
    /// turn. At 2^63 the endpoints 0 and 1 are both exact, at the cost of one bit of
    /// resolution that the result does not need.
    /// </remarks>
    private const int TableScale = 63;

    /// <summary>Mask selecting the raw fraction bits out of `within`.</summary>
    private const ulong FractionMask = (1UL << FractionBits) - 1;

    /// <summary>
    /// Sine over the first quadrant, at <c>TableSize + 2</c> uniform steps across
    /// [0, pi/2], in Q64.64. Entries are computed independently so the table carries no
    /// compounding error of its own.
    /// </summary>
    private static readonly ulong[] SinTable = BuildSinTable();

    private static ulong[] BuildSinTable()
    {
        var table = new ulong[TableSize + 2];
        for (int i = 0; i <= TableSize; i++)
        {
            double radians = Math.PI / 2.0 * i / TableSize;
            table[i] = (ulong)Math.Round(Math.Sin(radians) * 9223372036854775808.0);
        }

        // Pin the endpoints, which callers assert on. Both are exact at this scale.
        table[0] = 0;
        table[TableSize] = 1UL << TableScale;
        table[TableSize + 1] = 1UL << TableScale;
        return table;
    }

    /// <summary>
    /// Sine of an angle given as Q64.64 turns, returning Q64.64.
    /// </summary>
    /// <remarks>
    /// Quadrant reduction is bit extraction, so the only error is the linear
    /// interpolation between table entries: of order <c>(pi/2/4096)²/8</c>, about
    /// 1.8 × 10⁻⁸ in the result. That is a hundred times the Q64.64 grid but far inside
    /// the 10⁻⁶ radian the propagator needs, and halving the table step quarters it.
    /// </remarks>
    internal static Fix128 SinTurn(Fix128 turn)
    {
        // The magitude's top two bits are the quadrant; the remaining 62 bits are the
        // position within it. Shifting the low word LEFT would drop the top bits and wrap,
        // so the quadrant is taken first and masked away.
        int quadrant = (int)(turn.Magnitude >> WithinBits) & 3;
        ulong within = (ulong)turn.Magnitude & ((1UL << WithinBits) - 1);

        // Take the index and the fraction from the top of `within`, in that order. Both
        // are needed, and the fraction is the wider field so that the turn's resolution is
        // not thrown away: only the last (WithinBits - FractionBits - TableFractionBits)
        // bits are dropped.
        // The index is a POSITION, not an entry number: it may equal TableSize, which lands
        // exactly on the endpoint with a zero fraction. Clamping it to TableSize - 1 moves
        // that sample an entire entry down and costs 7e-8 of accuracy at every quadrant
        // boundary — the one place a caller is most likely to check.
        int index = (int)(within >> FractionBits);
        Int128 fraction = (Int128)(within & FractionMask) << FractionShift;

        // Quadrants 1 and 3 measure back from the quarter turn: mirror the index and
        // complement the fraction. Mirroring the index alone moves the sample a whole
        // table entry; complementing the raw remainder instead of the scaled fraction
        // makes the complement half of what it should be, which is not visible on the
        // cardinal angles where the fraction is zero.
        bool mirrored = quadrant == 1 || quadrant == 3;
        if (mirrored)
        {
            index = TableSize - 1 - index;
            fraction = (Int128.One << 64) - fraction;
        }

        if (index < 0)
        {
            index = 0;
        }

        if (index > TableSize)
        {
            index = TableSize;
        }

        ulong a = SinTable[index];
        ulong b = SinTable[index + 1];

        // In a mirrored quadrant the fraction measures how far BACK towards entry `index`
        // the sample lies, from entry `index + 1`, so the same step is subtracted rather
        // than added.
        // The table holds Q0.63; the caller works in Q64.64.
        Int128 value = Interpolate(a, b, fraction) << (64 - TableScale);

        // The magnitude is 128 bits wide, so the narrowing cast to ulong must NOT be used
        // here: `(ulong)(Int128)2^64` is 0, which reports sine as exactly zero at the
        // quarter turn.
        bool negative = quadrant >= 2;
        UInt128 magnitude = (UInt128)(value < 0 ? -value : value);

        return Fix128.FromRaw(magnitude, negative || turn.Negative);
    }

    /// <summary>Cosine of an angle given as Q64.64 turns. A sine advanced a quarter turn.</summary>
    internal static Fix128 CosTurn(Fix128 turn) =>
        SinTurn(turn + Fix128.FromDouble(0.25));

    /// <summary>
    /// Reduces a Q64.64 turn into [0, 1), the form <see cref="SinTurn"/> expects.
    /// </summary>
    internal static Fix128 Normalize(Fix128 turn)
    {
        if (turn.IsZero)
        {
            return Fix128.Zero;
        }

        ulong fraction = (ulong)turn.Magnitude;
        return Fix128.FromRaw(fraction, turn.Negative);
    }

    /// <summary>Sine of an angle in radians, for conversion at a boundary.</summary>
    internal static Fix128 SinRadians(Fix128 radians) => SinTurn(TurnsFromRadians(radians));

    /// <summary>Converts radians to turns. Pi is not representable, so this is rounded.</summary>
    internal static Fix128 TurnsFromRadians(Fix128 radians) =>
        radians * Fix128.FromDouble(1.0 / (2.0 * Math.PI));

    /// <summary>Converts turns to radians.</summary>
    internal static Fix128 RadiansFromTurns(Fix128 turns) =>
        turns * Fix128.FromDouble(2.0 * Math.PI);

    /// <summary>
    /// Linear interpolation between two adjacent table entries, without ever forming the
    /// product of the step and the fraction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The obvious form — <c>low + ((high - low) * fraction) &gt;&gt; 64</c> — overflows
    /// <see cref="Int128"/>. The step reaches ±2⁶³ and the fraction reaches 2⁶⁴, so the
    /// product reaches 2¹²⁷ and the result wraps silently into a plausible-looking wrong
    /// answer. It read as sine being exactly 0.5 at a quarter turn.
    /// </para>
    /// <para>
    /// This form never multiplies them. The step is split into high and low halves:
    /// <c>step*fraction = stepHigh*2⁶⁴*fraction + stepLow*fraction</c>. The first term is
    /// folded in as a shift of the high word alone, and the second is a 64×64 product whose
    /// own shift discards its overflow. Both partial results stay inside 128 bits because
    /// the step's high word is a small integer.
    /// </para>
    /// <para>
    /// The fraction always measures forward from <c>low</c> towards <c>high</c>, in both
    /// mirrored and unmirrored quadrants — it is the table position that is mirrored, not
    /// the direction of travel. Treating a mirrored fraction as running backwards is a
    /// separate mistake that lands exactly on the wrong endpoint.
    /// </para>
    /// </remarks>
    private static Int128 Interpolate(ulong low, ulong high, Int128 fraction)
    {
        if (fraction == 0)
        {
            return low;
        }

        if (fraction == Int128.One << 64)
        {
            return high;
        }

        Int128 step = (Int128)high - low;
        Int128 stepHigh = step >> TableScale;
        Int128 stepLow = (Int128)(ulong)step & ((Int128.One << TableScale) - 1);

        // step*fraction = stepHigh*2^63*fraction + stepLow*fraction, with the high term
        // folded back in as a shift of the high word alone.
        return low + (stepHigh << TableScale) + ((stepLow * fraction) >> 64);
    }
}
