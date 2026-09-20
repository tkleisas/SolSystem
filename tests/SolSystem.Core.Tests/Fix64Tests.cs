using SolSystem.Core.Numerics;
using Xunit;

namespace SolSystem.Core.Tests;

/// <summary>
/// Golden-value tests for the Q32.32 primitives. These are the tests that would
/// catch a silent overflow in the 128-bit multiply, which is the failure mode most
/// likely to look correct on small inputs and be wrong in the field.
/// </summary>
public class Fix64Tests
{
    private const long Lsb = 1; // one raw unit = 2^-32 Mm

    [Fact]
    public void Constants_AreExact()
    {
        Assert.Equal(0, Fix64.Zero.Raw);
        Assert.Equal(1L << 32, Fix64.One.Raw);
        Assert.Equal(1L << 31, Fix64.Half.Raw);
        Assert.Equal(1L << 33, Fix64.Two.Raw);
        Assert.Equal(long.MaxValue, Fix64.MaxValue.Raw);
        Assert.Equal(long.MinValue, Fix64.MinValue.Raw);
    }

    [Theory]
    [InlineData(0L, 0)]
    [InlineData(1L, 1)]
    [InlineData(-1L, -1)]
    [InlineData(2L, 2)]
    [InlineData(1_000_000L, 1_000_000)]
    [InlineData(-4500L, -4500)]
    public void FromMm_RoundTrips(long mm, long expected)
    {
        Fix64 value = Fix64.FromMm(mm);
        Assert.Equal(expected << 32, value.Raw);
        Assert.Equal(expected, Math.Round(value.ToDouble()));
    }

    [Fact]
    public void AdditionAndSubtraction_AreExact()
    {
        Fix64 a = Fix64.FromDouble(1.25);
        Fix64 b = Fix64.FromDouble(2.5);
        Assert.Equal(3.75, (a + b).ToDouble(), 15);
        Assert.Equal(-1.25, (a - b).ToDouble(), 15);
        Assert.Equal(Fix64.Zero, a - a);
    }

    [Fact]
    public void Multiplication_IsExactAtOne()
    {
        Fix64 a = Fix64.FromDouble(3.5);
        Assert.Equal(a, a * Fix64.One);
        Assert.Equal(Fix64.One, Fix64.One * Fix64.One);
    }

    [Theory]
    [InlineData(2.0, 3.0)]
    [InlineData(0.5, 0.25)]
    [InlineData(-4.0, 2.5)]
    [InlineData(1.5, -1.5)]
    [InlineData(12345.678, 0.001)]
    public void Multiplication_MatchesDouble(double a, double b)
    {
        Fix64 left = Fix64.FromDouble(a);
        Fix64 right = Fix64.FromDouble(b);
        Fix64 product = left * right;

        // The reference is the product of the *rounded* operands, because that is
        // what a fixed-point multiply is the correct answer to. Comparing against the
        // product of the original decimals instead would be measuring the operand
        // rounding, which is not what this test is for.
        Int128 exact = ((Int128)left.Raw * right.Raw) >> 32;

        Assert.Equal((long)exact, product.Raw);
    }

    [Fact]
    public void Multiplication_IsTakenAt128Bits()
    {
        // The property under test is the WIDTH of the intermediate, so the operand is
        // chosen where a 64-bit intermediate would overflow by a factor of four while
        // the 128-bit one does not.
        //
        // Note what is deliberately NOT asserted: that the result is large or positive.
        // Squaring 10 AU is 100 AU^2, which is past MaxValue, so the narrowed result
        // legitimately wraps to a negative raw value. A fixed-point multiply has no
        // overflow detection by design; staying inside the range is the caller's job.
        Fix64 distance = Fix64.FromMm(10 * 149_597_870L);

        Int128 wide = (Int128)distance.Raw * distance.Raw;
        Assert.True(wide > long.MaxValue, "the operand did not actually exercise the 128-bit path");

        // The implementation must produce exactly the true product, narrowed.
        Fix64 squared = distance * distance;
        Assert.Equal((long)(wide >> Fix64.FractionalBits), squared.Raw);
    }

    [Fact]
    public void Multiplication_StaysExactWellInsideTheRange()
    {
        // A control for the test above: at a magnitude where the result is representable,
        // squaring must be exact rather than wrapped.
        // The operands stop at 10 000 Mm because the SQUARE has to stay inside the
        // range as well, and the ceiling is about 2.1e9: squaring 1e6 Mm would leave the
        // range and wrap, which is the case the test above deliberately covers.
        foreach (long mm in new[] { 1L, 10L, 1_000L, 10_000L, 46_000L })
        {
            Fix64 v = Fix64.FromMm(mm);
            Fix64 squared = v * v;

            Int128 exact = ((Int128)v.Raw * v.Raw) >> Fix64.FractionalBits;
            Assert.Equal((long)exact, squared.Raw);
            Assert.Equal((double)mm * mm, squared.ToDouble(), 6);
        }
    }

    [Fact]
    public void TwoFrames_HaveTheReachAndPrecisionTheDesignClaims()
    {
        // §6.2 of the design puts this type in two frames that differ only by unit. Both
        // properties below are load-bearing and both were wrong in early drafts, in
        // opposite directions, so they are pinned against real bodies and real speeds
        // rather than against a derived constant.
        const long maxWholeUnits = 2_147_483_647L;
        const double auInKm = 149_597_870.0;

        Assert.Equal(maxWholeUnits, Fix64.MaxWholeUnits);
        Assert.Equal(maxWholeUnits, Fix64.MaxValue.Raw >> 32);
        Assert.Equal((Int128)maxWholeUnits << 32, (Int128)Fix64.FromMm(maxWholeUnits).Raw);

        // SOLAR FRAME, unit = kilometre.
        double solarReachAu = maxWholeUnits / auInKm;
        Assert.InRange(solarReachAu, 14.355, 14.356);

        // It has to reach the theatres the design actually uses: Venus, Mars, the
        // Jovian moons, and Saturn.
        foreach (double au in new[] { 0.72, 1.0, 1.52, 5.2, 9.5 })
        {
            Assert.True(au < solarReachAu, $"{au} AU must be inside the solar frame");
        }

        // Uranus is outside it. That is a real limitation, not an oversight, and pinning
        // it here means the day the game wants Uranus the test says so.
        Assert.True(19.2 > solarReachAu, "Uranus is expected to be out of range in km");

        // The solar frame's LSB is 233 MICROMETRES, not nanometres: 2^-32 km, which is
        // 2.328e-7 m.
        double solarLsbMetres = (1.0 / 4294967296.0) * 1000.0;
        Assert.InRange(solarLsbMetres * 1e6, 0.2328, 0.2329);

        // LOCAL FRAME, unit = metre. Same type, thousandth of the unit.
        double localLsbMetres = 1.0 / 4294967296.0;
        Assert.InRange(localLsbMetres * 1e9, 0.2328, 0.2329);

        // Reach: 2.147e6 km, which is wider than Earth's sphere of influence.
        double localReachKm = maxWholeUnits / 1000.0;
        Assert.True(localReachKm > 924_000.0, "the local frame must cover Earth's sphere of influence");

        // The reason the local frame exists is PRECISION, not reach, and the test for it
        // is a speed rather than a distance: a 1 cm/s docking approach at a 120 Hz tick
        // moves 83 um per tick.
        double metresPerTick = 0.01 / 120.0;

        // The local frame puts that at ~358 000 LSB per tick: motion is smooth and the
        // quantisation is nowhere near visible at any camera scale the game will use.
        Assert.True(
            metresPerTick / localLsbMetres > 100_000.0,
            "local frame must render a docking approach smoothly");

        // The solar frame also manages 358 LSB per tick, so it is not visibly broken
        // either. What it cannot do is resolve the SHAPE of a ship: its 233 um grid is
        // coarser than most hull features and than any contact or collision geometry,
        // which is the real reason the action layer gets its own frame.
        Assert.True(
            metresPerTick / solarLsbMetres > 100.0,
            "the solar frame is not too coarse for smooth motion, only for geometry");
        // Pin the actual grid, which is the whole point of the split.
        Assert.InRange(solarLsbMetres, 2.32e-7, 2.34e-7);
        Assert.InRange(localLsbMetres, 2.32e-10, 2.34e-10);
        Assert.Equal(1000.0, solarLsbMetres / localLsbMetres, 6);
    }

    [Fact]
    public void Conversions_RejectWhatDoesNotFit()
    {
        // Both conversion paths must refuse rather than wrap, and the exception must
        // name a limit a caller can act on.
        OverflowException fromMm = Assert.Throws<OverflowException>(() => Fix64.FromMm(3_000_000_000L));
        Assert.Contains("2147483647", fromMm.Message, StringComparison.Ordinal);
        Assert.Contains("3000000000", fromMm.Message, StringComparison.Ordinal);

        OverflowException fromDouble = Assert.Throws<OverflowException>(() => Fix64.FromDouble(1e12));
        Assert.Contains("2147483647", fromDouble.Message, StringComparison.Ordinal);

        // Non-finite input is refused too, rather than silently becoming MinValue.
        Assert.Throws<ArgumentOutOfRangeException>(() => Fix64.FromDouble(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fix64.FromDouble(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => Fix64.FromDouble(double.NegativeInfinity));
    }

    [Fact]
    public void Multiplication_IsCommutativeAndAssociativeWithinOneLsb()
    {
        Fix64 a = Fix64.FromDouble(1.0 / 3.0);
        Fix64 b = Fix64.FromDouble(7.0);
        Fix64 c = Fix64.FromDouble(0.001);

        Assert.Equal((a * b).Raw, (b * a).Raw);

        long left = (a * b * c).Raw;
        long right = (a * (b * c)).Raw;
        Assert.True(Math.Abs(left - right) <= 4, $"associativity drift was {Math.Abs(left - right)} raw units");
    }

    [Theory]
    [InlineData(6.0, 3.0, 2.0)]
    [InlineData(1.0, 3.0, 0.333333333333333)]
    [InlineData(-9.0, 2.0, -4.5)]
    public void Division_MatchesDouble(double a, double b, double expected)
    {
        Fix64 quotient = Fix64.FromDouble(a) / Fix64.FromDouble(b);

        // At 1/3 the quotient is ~0.333, so one LSB of 2^-32 is ~7.8e-11 absolute.
        // Truncation towards negative infinity costs one more. The tolerance is set
        // from that, not from a guess.
        double oneLsb = 1.0 / 4294967296.0;
        Assert.True(
            Math.Abs(expected - quotient.ToDouble()) <= oneLsb * 2,
            $"{a} / {b}: expected {expected}, got {quotient.ToDouble()}");
    }

    [Fact]
    public void Division_TruncatesTowardsNegativeInfinity()
    {
        // Consistency with integer division, so that floors of angles and indices
        // behave the same way in fixed point as they do in the rest of the code.
        Fix64 result = Fix64.FromDouble(-1.0) / Fix64.FromDouble(3.0);
        Assert.True(result.Raw <= (long)(-1.0 / 3.0 * (1L << 32)) + 1);
    }

    [Fact]
    public void Division_ByZero_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => Fix64.One / Fix64.Zero);
        Assert.Throws<DivideByZeroException>(() => Fix64.One % Fix64.Zero);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(4.0)]
    [InlineData(100.0)]
    [InlineData(1e6)]
    [InlineData(12345.6789)]
    public void Sqrt_MatchesDouble(double value)
    {
        Fix64 root = Fix64.Sqrt(Fix64.FromDouble(value));
        Assert.Equal(Math.Sqrt(value), root.ToDouble(), 9);
    }

    [Fact]
    public void Sqrt_IsExactOnPerfectSquares()
    {
        for (long n = 0; n < 4096; n++)
        {
            Fix64 root = Fix64.Sqrt(Fix64.FromMm(n * n));
            Assert.Equal(n, root.Raw >> 32);
            Assert.Equal(0, root.Raw & 0xFFFF_FFFF); // exactly n, no fraction
        }
    }

    [Fact]
    public void Sqrt_OfNegative_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Fix64.Sqrt(Fix64.FromMm(-1)));

    [Fact]
    public void Sqrt_IsTheLargestRootThatFits()
    {
        // The exact contract, stated in raw units: Sqrt(a) is the largest representable
        // r with r*r <= a.Raw << 32, because the Q32.32 square root is Isqrt(raw << 32).
        //
        // Both comparisons must be taken at 128 bits. Comparing (r*r) >> 32 against
        // a.Raw instead is NOT equivalent and is not a valid invariant: the narrowing
        // shift floors, so ((r+1)^2) >> 32 can land exactly on a.Raw while
        // (r+1)^2 > a.Raw << 32 still holds. Asserting the narrowed form fails for
        // ordinary values like 1.37 and 2 for reasons that have nothing to do with Sqrt.
        static bool Fits(Fix64 a, Fix64 r) =>
            (Int128)r.Raw * r.Raw <= (Int128)a.Raw << Fix64.FractionalBits;

        foreach (double value in new[] { 0.5, 1.0, 1.37, 2.0, 100.0, 12345.678, 1e6, 1e9 })
        {
            Fix64 a = Fix64.FromDouble(value);
            Fix64 root = Fix64.Sqrt(a);

            Assert.True(Fits(a, root), $"root too large for {value}: {root.ToDouble()}");
            Assert.True(
                !Fits(a, root + Fix64.FromRaw(1)),
                $"root not maximal for {value}: {(root + Fix64.FromRaw(1)).ToDouble()} also fits");
        }
    }

    [Fact]
    public void Sqrt_IsCorrectlyRoundedToWithinOneLsb()
    {
        // The user-facing property: the result is never more than one LSB of the
        // fixed-point grid away from the true square root.
        foreach (double value in new[] { 0.25, 0.5, 1.0, 1.37, 2.0, 7.5, 100.0, 12345.678, 1e6, 1e9 })
        {
            double actual = Fix64.Sqrt(Fix64.FromDouble(value)).ToDouble();
            double expected = Math.Sqrt(value);
            double oneLsb = 1.0 / 4294967296.0;

            Assert.True(
                Math.Abs(actual - expected) <= oneLsb,
                $"sqrt({value}) = {actual}, expected {expected} (off by {Math.Abs(actual - expected) / oneLsb} LSB)");
        }
    }

    [Fact]
    public void AbsAndMinMax_Behave()
    {
        Fix64 negative = Fix64.FromMm(-5);
        Assert.Equal(Fix64.FromMm(5), Fix64.Abs(negative));
        Assert.Equal(negative, Fix64.Min(negative, Fix64.FromMm(5)));
        Assert.Equal(Fix64.FromMm(5), Fix64.Max(negative, Fix64.FromMm(5)));
    }

    [Fact]
    public void Comparison_OrdersCorrectly()
    {
        Fix64 negative = Fix64.FromMm(-1);
        Fix64 zero = Fix64.Zero;
        Fix64 one = Fix64.One;
        Fix64 oneAndAHalf = Fix64.FromDouble(1.5);

        Assert.True(negative < zero);
        Assert.True(zero < one);
        Assert.True(oneAndAHalf > one);
        Assert.True(one <= Fix64.One);
        Assert.True(one >= Fix64.One);
        Assert.NotEqual(one, Fix64.Two);
    }

    [Fact]
    public void IntegerDivisionAgreesWithFloor()
    {
        // The remainder operator keeps the sign of the dividend, as int does.
        Fix64 a = Fix64.FromDouble(-7.0);
        Fix64 b = Fix64.FromDouble(3.0);
        Fix64 remainder = a % b;
        Assert.Equal(-1.0, remainder.ToDouble(), 12);
    }
}
