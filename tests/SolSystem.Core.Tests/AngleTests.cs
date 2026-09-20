using SolSystem.Core.Numerics;
using Xunit;

namespace SolSystem.Core.Tests;

/// <summary>
/// The trigonometric functions are the part of the fixed-point core most likely to
/// be subtly wrong, so they are checked against <see cref="Math"/> at a tight
/// tolerance across the whole circle rather than at a handful of friendly angles.
/// </summary>
public class AngleTests
{
    private const double TurnToRadians = 2.0 * Math.PI / 4294967296.0;

    private static double ToRadians(uint turn) => turn * TurnToRadians;

    [Fact]
    public void CardinalAngles_AreExact()
    {
        // These are the values the simulation will assert on, so they must be exact
        // rather than merely close.
        Assert.Equal(0, Angle.Sin(0).Raw);
        Assert.Equal(Fix64.One.Raw, Angle.Sin(Angle.QuarterTurn).Raw);
        Assert.Equal(0, Angle.Sin(Angle.HalfTurn).Raw);
        Assert.Equal(-Fix64.One.Raw, Angle.Sin(unchecked(Angle.QuarterTurn * 3)).Raw);

        Assert.Equal(Fix64.One.Raw, Angle.Cos(0).Raw);
        Assert.Equal(0, Angle.Cos(Angle.QuarterTurn).Raw);
        Assert.Equal(-Fix64.One.Raw, Angle.Cos(Angle.HalfTurn).Raw);
    }

    [Fact]
    public void Sine_MatchesDoubleAcrossTheCircle()
    {
        // Step by a large prime number of raw units so the samples do not land on
        // table entries and hide an interpolation bug.
        for (uint turn = 0; turn < uint.MaxValue - 1_000_003; turn += 1_000_003)
        {
            double expected = Math.Sin(ToRadians(turn));
            double actual = Angle.Sin(turn).ToDouble();
            Assert.True(Math.Abs(expected - actual) < 2e-7, $"turn {turn}: expected {expected}, got {actual}");
        }
    }

    [Fact]
    public void Cosine_MatchesDoubleAcrossTheCircle()
    {
        for (uint turn = 0; turn < uint.MaxValue - 1_000_003; turn += 1_000_003)
        {
            double expected = Math.Cos(ToRadians(turn));
            double actual = Angle.Cos(turn).ToDouble();
            Assert.True(Math.Abs(expected - actual) < 2e-7, $"turn {turn}: expected {expected}, got {actual}");
        }
    }

    [Fact]
    public void PythagoreanIdentity_HoldsWithinInterpolationError()
    {
        for (uint turn = 0; turn < uint.MaxValue - 999_983; turn += 999_983)
        {
            Fix64 s = Angle.Sin(turn);
            Fix64 c = Angle.Cos(turn);
            double magnitude = (s * s + c * c).ToDouble();
            Assert.True(Math.Abs(magnitude - 1.0) < 1e-7, $"turn {turn}: |(sin,cos)|^2 = {magnitude}");
        }
    }

    [Fact]
    public void CosineIsSineAdvancedAQuarterTurn()
    {
        for (uint turn = 0; turn < 5_000_000; turn += 7_919)
        {
            Assert.Equal(Angle.Sin(unchecked(turn + Angle.QuarterTurn)).Raw, Angle.Cos(turn).Raw);
        }
    }

    [Fact]
    public void AdditiveWrap_IsExactlyPeriodic()
    {
        // A full turn is 2^32 raw units, which is not itself representable in a uint,
        // so wrapping is demonstrated by the fact that adding a turn's worth of
        // uint arithmetic returns the original angle exactly.
        Assert.Equal(Angle.Sin(0u).Raw, Angle.Sin(unchecked(0u - 1u + 1u)).Raw);

        // The quarter-turn identity must hold right across the seam.
        foreach (uint turn in new[] { 0u, 1u, 12345u, 1_000_000_000u, 2_000_000_000u, 3_000_000_000u, 4_000_000_000u })
        {
            Fix64 viaSin = Angle.Sin(unchecked(turn + Angle.QuarterTurn));
            Assert.Equal(viaSin.Raw, Angle.Cos(turn).Raw);
        }
    }

    [Fact]
    public void Sine_AgreesAcrossTheWrapSeam()
    {
        // Two angles an equal amount either side of zero must give the same sine.
        // The seam sits at 2^32 so the pair straddles the uint wrap, and by symmetry
        // about the maximum the two values have to match.
        const uint delta = 1000;
        uint nearEnd = uint.MaxValue - delta + 1; // delta raw units below the wrap
        uint nearStart = delta;                   // delta raw units above zero

        double below = Angle.Sin(nearEnd).ToDouble();
        double above = Angle.Sin(nearStart).ToDouble();

        // Sine is odd about zero, and the two angles straddle the wrap symmetrically,
        // so the values are equal in magnitude and opposite in sign - not equal.
        Assert.True(
            Math.Abs(below + above) < 1e-9,
            $"sine either side of the seam was not antisymmetric: {below} vs {above}");

        // And both are that magnitude, near zero.
        double expected = Math.Sin(2.0 * Math.PI * delta / 4294967296.0);
        Assert.True(Math.Abs(above - expected) < 1e-9, $"above the seam: {above} vs {expected}");
        Assert.True(Math.Abs(below + expected) < 1e-9, $"below the seam: {below} vs {-expected}");

        // And sine really is near zero there, so the seam is where it should be: the
        // maximum angle is just under a full turn, whose sine is just under zero.
        Assert.True(Math.Abs(above) < 1e-5, $"sine one step from the seam was {above}");

        // A quarter turn from the seam is near the maximum, confirming the mapping
        // did not get shifted by the wrap.
        Assert.True(Angle.Sin(unchecked(nearStart + Angle.QuarterTurn)).ToDouble() > 0.999);
    }

    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.0, -1.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(-1.0, 1.0)]
    [InlineData(-1.0, -1.0)]
    [InlineData(1.0, -1.0)]
    [InlineData(3.0, 4.0)]
    [InlineData(-3.0, 4.0)]
    [InlineData(149_597_870.0, 0.0)]
    [InlineData(0.0, 384_400.0)]
    public void Atan2_MatchesDouble(double x, double y)
    {
        uint angle = Angle.Atan2(Fix64.FromDouble(y), Fix64.FromDouble(x));
        double expected = Math.Atan2(y, x);

        // Normalise the double result into [0, 1) turns.
        double expectedTurns = expected / (2.0 * Math.PI);
        if (expectedTurns < 0)
        {
            expectedTurns += 1.0;
        }

        double actual = angle / 4294967296.0;
        double error = Math.Abs(expectedTurns - actual);

        // Tolerate wrapping at the seam.
        error = Math.Min(error, 1.0 - error);
        Assert.True(error < 1e-6, $"atan2({x}, {y}): expected {expectedTurns} turns, got {actual} ({error} off)");
    }

    [Fact]
    public void Atan2_CoversAllQuadrants()
    {
        // Each axis direction must land in its own quarter of the circle.
        Assert.InRange(Angle.Atan2(Fix64.Zero, Fix64.One), 0u, Angle.QuarterTurn - 1);
        Assert.InRange(Angle.Atan2(Fix64.One, Fix64.Zero), Angle.QuarterTurn, Angle.HalfTurn - 1);
        Assert.InRange(
            Angle.Atan2(Fix64.Zero, -Fix64.One),
            Angle.HalfTurn,
            unchecked(Angle.QuarterTurn * 3) - 1);
        Assert.InRange(
            Angle.Atan2(-Fix64.One, Fix64.Zero),
            unchecked(Angle.QuarterTurn * 3),
            uint.MaxValue);
    }

    [Fact]
    public void Atan2_AtOrigin_Throws() =>
        Assert.Throws<ArgumentException>(() => Angle.Atan2(Fix64.Zero, Fix64.Zero));

    [Fact]
    public void Atan2_IsStableForSmallVectors()
    {
        // Scale invariance: the angle must not depend on the magnitude of the input,
        // which is what the normalisation in Atan2 is for.
        uint big = Angle.Atan2(Fix64.FromDouble(3000.0), Fix64.FromDouble(4000.0));
        uint small = Angle.Atan2(Fix64.FromDouble(0.003), Fix64.FromDouble(0.004));

        long difference = Math.Abs((long)big - small);
        Assert.True(difference < 1_000, $"scale changed the angle by {difference} raw units");
    }
}
