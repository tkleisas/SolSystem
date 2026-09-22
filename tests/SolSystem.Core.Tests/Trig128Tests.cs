using SolSystem.Core.Numerics;
using Xunit;

namespace SolSystem.Core.Tests;

/// <summary>
/// The Q64.64 trigonometry the propagator runs on, checked against
/// <see cref="Math"/> across the whole circle.
/// </summary>
public class Trig128Tests
{
    private static double ToRadians(Fix128 turns) => turns.ToDouble() * 2.0 * Math.PI;

    [Fact]
    public void CardinalAngles_AreExact()
    {
        Assert.Equal(0, Trig128.SinTurn(Fix128.Zero).ToDouble());
        Assert.Equal(1.0, Trig128.SinTurn(Fix128.FromDouble(0.25)).ToDouble());
        Assert.Equal(0, Trig128.SinTurn(Fix128.FromDouble(0.5)).ToDouble());
        Assert.Equal(-1.0, Trig128.SinTurn(Fix128.FromDouble(0.75)).ToDouble());

        Assert.Equal(1.0, Trig128.CosTurn(Fix128.Zero).ToDouble());
        Assert.Equal(0, Trig128.CosTurn(Fix128.FromDouble(0.25)).ToDouble());
        Assert.Equal(-1.0, Trig128.CosTurn(Fix128.FromDouble(0.5)).ToDouble());
    }

    [Fact]
    public void Sine_MatchesDoubleAcrossTheCircle()
    {
        // A large prime step so samples do not land on table entries and hide an
        // interpolation bug.
        for (int i = 0; i < 4000; i++)
        {
            double turns = i * 0.0002500007;
            Fix128 angle = Fix128.FromDouble(turns);
            double expected = Math.Sin(ToRadians(angle));
            double actual = Trig128.SinTurn(angle).ToDouble();

            Assert.True(Math.Abs(expected - actual) < 1e-7, $"turns {turns}: {expected} vs {actual}");
        }
    }

    [Fact]
    public void Cosine_MatchesDoubleAcrossTheCircle()
    {
        for (int i = 0; i < 4000; i++)
        {
            double turns = i * 0.0002500007;
            Fix128 angle = Fix128.FromDouble(turns);
            double expected = Math.Cos(ToRadians(angle));
            double actual = Trig128.CosTurn(angle).ToDouble();

            Assert.True(Math.Abs(expected - actual) < 1e-7, $"turns {turns}: {expected} vs {actual}");
        }
    }

    [Fact]
    public void PythagoreanIdentity_HoldsWithinInterpolationError()
    {
        for (int i = 0; i < 2000; i++)
        {
            Fix128 angle = Fix128.FromDouble(i * 0.0005000013);
            Fix128 s = Trig128.SinTurn(angle);
            Fix128 c = Trig128.CosTurn(angle);
            double magnitude = (s * s + c * c).ToDouble();

            Assert.True(Math.Abs(magnitude - 1.0) < 1e-7, $"turns {i}: |(sin,cos)|² = {magnitude}");
        }
    }

    [Fact]
    public void NegativeAngles_AreOddSymmetric()
    {
        for (int i = 1; i < 500; i++)
        {
            Fix128 angle = Fix128.FromDouble(i * 0.001);
            double positive = Trig128.SinTurn(angle).ToDouble();
            double negative = Trig128.SinTurn(-angle).ToDouble();

            Assert.True(Math.Abs(positive + negative) < 1e-8, $"{angle}: {positive} vs {negative}");
        }
    }

    [Fact]
    public void NegativeAngles_MatchDoubleInEveryQuadrant()
    {
        // One sample per quadrant, each negated: 0.1, 0.3, 0.6, 0.85 turns. The sign of
        // sin(-x) flips between the front and back halves of the circle, and an OR where
        // the sign logic wanted an XOR is invisible until the back half is sampled —
        // which the symmetry test above never reaches.
        foreach (double turns in new[] { 0.1, 0.3, 0.6, 0.85 })
        {
            Fix128 angle = Fix128.FromDouble(-turns);
            double radians = turns * -2.0 * Math.PI;

            double sine = Trig128.SinTurn(angle).ToDouble();
            Assert.True(Math.Abs(sine - Math.Sin(radians)) < 1e-7,
                $"sin({-turns} turns): {sine}, expected {Math.Sin(radians)}");

            double cosine = Trig128.CosTurn(angle).ToDouble();
            Assert.True(Math.Abs(cosine - Math.Cos(radians)) < 1e-7,
                $"cos({-turns} turns): {cosine}, expected {Math.Cos(radians)}");
        }
    }

    [Fact]
    public void FullRevolutions_DoNotChangeTheResult()
    {
        for (int i = 0; i < 200; i++)
        {
            double turns = i * 0.0031;
            Fix128 bare = Fix128.FromDouble(turns);
            Fix128 wrapped = Fix128.FromDouble(turns + 3.0);

            double difference = Math.Abs(
                Trig128.SinTurn(bare).ToDouble() - Trig128.SinTurn(wrapped).ToDouble());

            Assert.True(difference < 1e-8, $"{turns}: {difference}");
        }
    }

    [Fact]
    public void RadianConversions_RoundTrip()
    {
        foreach (double radians in new[] { 0.0, 0.5, 1.0, 3.14159, -2.5, 6.28 })
        {
            Fix128 turns = Trig128.TurnsFromRadians(Fix128.FromDouble(radians));
            double back = Trig128.RadiansFromTurns(turns).ToDouble();
            Assert.True(Math.Abs(back - radians) < 1e-15, $"{radians} -> {back}");
        }
    }

    [Fact]
    public void SinRadians_MatchesDouble()
    {
        foreach (double radians in new[] { 0.0, 0.3, 1.0, 1.5707963, 2.5, 3.0, -1.2 })
        {
            double actual = Trig128.SinRadians(Fix128.FromDouble(radians)).ToDouble();
            Assert.True(Math.Abs(actual - Math.Sin(radians)) < 1e-7, $"{radians}: {actual}");
        }
    }
}
