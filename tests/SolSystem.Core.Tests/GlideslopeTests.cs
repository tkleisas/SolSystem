using SolSystem.Core.Local;
using SolSystem.Core.Numerics;
using Xunit;

namespace SolSystem.Core.Tests;

/// <summary>
/// The glideslope as a control law: the line, its clamps, its duration, and the
/// feasibility check a corridor and a drive have to pass together.
/// </summary>
public class GlideslopeTests
{
    private static Fix128 F(double v) => Fix128.FromDouble(v);

    [Fact]
    public void TheProfile_IsAStraightLineInTheRangeRatePlane()
    {
        // Hablani's identity: v(r) = v_T + (v0 - v_T)·r/r0, exactly. A law that is not a
        // line is not this law, however close the endpoints are.
        var profile = Glideslope.For(F(2_000.0), F(8.0), F(0.1));

        foreach (double t in new[] { 0.0, 0.13, 0.5, 0.77, 1.0 })
        {
            Fix128 at = F(2_000.0 * t);
            double expected = 0.1 + (8.0 - 0.1) * t;
            Assert.Equal(expected, profile.RateAt(at).ToDouble(), 12);
        }
    }

    [Fact]
    public void TheLine_IsClampedAtBothEnds()
    {
        var profile = Glideslope.For(F(2_000.0), F(8.0), F(0.1));

        // Beyond the corridor start the ship gets the initial rate, not an extrapolation
        // past it; past contact it gets the contact rate, not a command to retreat.
        Assert.Equal(8.0, profile.RateAt(F(9_999.0)).ToDouble(), 12);
        Assert.Equal(0.1, profile.RateAt(F(-50.0)).ToDouble(), 12);
    }

    [Fact]
    public void TheDuration_IsTheSlopeInverted()
    {
        // The line's duration is r0 / (v0 - v_T), and it is also the 1/lambda of the
        // equivalent exponential: fly the line end to end and the time falls out of the
        // slope alone.
        var profile = Glideslope.For(F(2_000.0), F(8.0), F(0.1));

        Assert.Equal(2_000.0 / 7.9, profile.DurationSeconds!.Value.ToDouble(), 9);
    }

    [Fact]
    public void AConstantRateProfile_HasNoDuration()
    {
        // v0 = v_T is legal — a corridor flown at one rate — and it never arrives. There
        // is no infinity in Q64.64, so the answer is "no answer", and a display that
        // wants one prints "never".
        var profile = Glideslope.For(F(2_000.0), F(0.1), F(0.1));

        Assert.Null(profile.DurationSeconds);
        Assert.Contains("never", profile.ToString(), System.StringComparison.Ordinal);
    }

    [Fact]
    public void Feasibility_IsJudgedAtTheFarEndWhereTheShipIsFastest()
    {
        // a_required = (v0 - v_T)·v0/r0: 7.9·8/2000 = 0.0316 m/s2. The check is the
        // boundary itself, so it is asserted AT the boundary as well as on either side.
        var profile = Glideslope.For(F(2_000.0), F(8.0), F(0.1));
        double required = 7.9 * 8.0 / 2_000.0;

        Assert.True(profile.Feasible(F(required)));
        Assert.True(profile.Feasible(F(required * 2.0)));
        Assert.False(profile.Feasible(F(required / 2.0)));
        Assert.False(profile.Feasible(Fix128.Zero));
    }
}
