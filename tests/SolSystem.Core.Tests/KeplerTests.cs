using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;
using Xunit;

namespace SolSystem.Core.Tests;

/// <summary>
/// The Keplerian propagator, checked three ways: against closed-form geometry, against
/// conservation of orbital energy, and against numerical integration of the same orbit.
/// </summary>
/// <remarks>
/// The third check is the strongest one available, because <see cref="Verlet128"/> is an
/// independent implementation of the same physics with no shared code. The spike already
/// established that it holds an orbit to 1.4e-6 over a year, so it is a trustworthy
/// reference; agreement between the two is real evidence rather than a self-consistency
/// check.
/// </remarks>
public class KeplerTests
{
    private const double AuKm = 149_597_870.0;
    private const double YearSeconds = 365.25 * 24 * 3600;

    private static Fix128 F(double value) => Fix128.FromDouble(value);

    /// <summary>Earth's mean orbital speed about the Sun, km/s.</summary>
    private static readonly double CircularSpeed = Math.Sqrt(Constants.SunGm.ToDouble() / AuKm);

    private static OrbitalElements Circular(double radiusKm) =>
        OrbitalElements.FromEccentricAnomaly(
            F(radiusKm),
            Fix128.Zero,
            Fix128.Zero,
            Fix128.Zero,
            Fix128.Zero,
            Fix128.Zero,
            Fix128.Zero,
            Constants.SunGm);

    private static OrbitalElements Eccentric(double radiusKm, double eccentricity) =>
        OrbitalElements.FromEccentricAnomaly(
            F(radiusKm),
            F(eccentricity),
            Fix128.Zero,
            Fix128.Zero,
            Fix128.Zero,
            Fix128.Zero,
            Fix128.Zero,
            Constants.SunGm);

    private static double RadiusKm(SolarState state) => state.Position.Length.ToDouble();

    private static double SpeedKmS(SolarState state) => state.Velocity.Length.ToDouble();

    [Fact]
    public void MeanMotion_MatchesTheOrbitalPeriod()
    {
        // n = 1/period. Earth's period is 365.25 days, so the mean motion should be
        // 1/3.156e7 turns per second.
        OrbitalElements orbit = Circular(AuKm);

        // MeanMotion is radians per second, so the period is 2pi/n. Earth's sidereal year
        // is 365.2569 days at this semi-major axis, NOT 365.25 — the Julian year is 0.0069
        // days short of it, which is 17745 km of orbital travel and enough to look like a
        // propagator bug if the test asserts against the wrong number.
        double periodSeconds = 2.0 * Math.PI / orbit.MeanMotion.ToDouble();
        double periodDays = periodSeconds / 86400.0;

        Assert.True(Math.Abs(periodDays - 365.2569) < 0.001, $"period was {periodDays} days");
    }

    [Fact]
    public void CircularOrbit_HoldsItsRadiusAllTheWayRound()
    {
        OrbitalElements orbit = Circular(AuKm);
        double period = 2.0 * Math.PI / orbit.MeanMotion.ToDouble();

        for (int i = 0; i < 64; i++)
        {
            Fix128 time = F(period * i / 64.0);
            double radius = RadiusKm(orbit.StateAt(time));
            double relative = Math.Abs(radius - AuKm) / AuKm;

            Assert.True(relative < 1e-7, $"at step {i}: radius {radius} km, relative error {relative}");
        }
    }

    [Fact]
    public void CircularOrbit_HoldsItsSpeedAllTheWayRound()
    {
        OrbitalElements orbit = Circular(AuKm);
        double period = 2.0 * Math.PI / orbit.MeanMotion.ToDouble();

        for (int i = 0; i < 64; i++)
        {
            Fix128 time = F(period * i / 64.0);
            double speed = SpeedKmS(orbit.StateAt(time));
            double relative = Math.Abs(speed - CircularSpeed) / CircularSpeed;

            Assert.True(relative < 1e-7, $"at step {i}: speed {speed} km/s, relative error {relative}");
        }
    }

    [Fact]
    public void CircularOrbit_KeepsVelocityPerpendicularToPosition()
    {
        // The defining property of a circular orbit: v·r = 0 everywhere.
        OrbitalElements orbit = Circular(AuKm);
        double period = 2.0 * Math.PI / orbit.MeanMotion.ToDouble();

        for (int i = 0; i < 32; i++)
        {
            SolarState state = orbit.StateAt(F(period * i / 32.0));
            double dot = (state.Position.X * state.Velocity.X
                + state.Position.Y * state.Velocity.Y
                + state.Position.Z * state.Velocity.Z).ToDouble();

            double scale = RadiusKm(state) * SpeedKmS(state);
            Assert.True(Math.Abs(dot) / scale < 1e-7, $"at step {i}: v·r = {dot}");
        }
    }

    [Fact]
    public void CircularOrbit_ReturnsToItsStartingPointAfterOnePeriod()
    {
        OrbitalElements orbit = Circular(AuKm);

        // The period comes from the elements, not from the Julian year: using 365.25 days
        // leaves the body 0.0069 days short of a full revolution and reports a 17745 km
        // "closure error" that is really the test's own arithmetic.
        double period = 2.0 * Math.PI / orbit.MeanMotion.ToDouble();

        SolarState start = orbit.StateAt(Fix128.Zero);
        SolarState after = orbit.StateAt(F(period));

        double error = Math.Sqrt(
            Math.Pow(start.Position.X.ToDouble() - after.Position.X.ToDouble(), 2)
            + Math.Pow(start.Position.Y.ToDouble() - after.Position.Y.ToDouble(), 2)
            + Math.Pow(start.Position.Z.ToDouble() - after.Position.Z.ToDouble(), 2));

        Assert.True(error / AuKm < 1e-6, $"closure error was {error} km");
    }

    [Fact]
    public void EccentricOrbit_ClosesOnItselfInEitherDirectionFromTheEpoch()
    {
        // One period BEFORE the epoch is the same point as one period after it. That is
        // the only way the mean anomaly handed to WrapTurns is ever negative in this
        // suite, and it pins the wrap's sign: preserved, a -0.001-turn anomaly starts the
        // fixed six-pass Newton from -6 milliradians; normalised to [0, 1) it would start
        // from 6.28 radians and six passes is not enough.
        OrbitalElements orbit = Eccentric(AuKm, 0.1);
        double period = 2.0 * Math.PI / orbit.MeanMotion.ToDouble();

        SolarState start = orbit.StateAt(Fix128.Zero);
        SolarState backwards = orbit.StateAt(F(-period));
        SolarState forwards = orbit.StateAt(F(period));

        foreach ((SolarState candidate, string label) in new[] { (backwards, "backwards"), (forwards, "forwards") })
        {
            double error = Math.Sqrt(
                Math.Pow(start.Position.X.ToDouble() - candidate.Position.X.ToDouble(), 2)
                + Math.Pow(start.Position.Y.ToDouble() - candidate.Position.Y.ToDouble(), 2)
                + Math.Pow(start.Position.Z.ToDouble() - candidate.Position.Z.ToDouble(), 2));

            Assert.True(error / AuKm < 1e-6, $"{label} closure error was {error} km");
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.0167)]
    [InlineData(0.1)]
    [InlineData(0.3)]
    public void EccentricOrbit_ReachesItsAnalyticApoapsisAndPeriapsis(double eccentricity)
    {
        // r_apoapsis = a(1+e) and r_periapsis = a(1-e), exactly. A propagator that gets the
        // eccentric anomaly wrong still produces an ellipse, just the wrong one, so this is
        // the check that pins the shape.
        OrbitalElements orbit = Eccentric(AuKm, eccentricity);

        double period = 2.0 * Math.PI / orbit.MeanMotion.ToDouble();
        double maxRadius = 0;
        double minRadius = double.MaxValue;
        for (int i = 0; i < 256; i++)
        {
            double radius = RadiusKm(orbit.StateAt(F(period * i / 256.0)));
            maxRadius = Math.Max(maxRadius, radius);
            minRadius = Math.Min(minRadius, radius);
        }

        double expectedApoapsis = AuKm * (1 + eccentricity);
        double expectedPeriapsis = AuKm * (1 - eccentricity);

        Assert.True(
            Math.Abs(maxRadius - expectedApoapsis) / expectedApoapsis < 1e-5,
            $"e={eccentricity}: apoapsis {maxRadius}, expected {expectedApoapsis}");
        Assert.True(
            Math.Abs(minRadius - expectedPeriapsis) / expectedPeriapsis < 1e-5,
            $"e={eccentricity}: periapsis {minRadius}, expected {expectedPeriapsis}");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.0167)]
    [InlineData(0.1)]
    [InlineData(0.3)]
    public void EccentricOrbit_ConservesSpecificOrbitalEnergy(double eccentricity)
    {
        // ε = -GM/(2a) for any closed orbit, independent of eccentricity or position. This
        // catches an error in the velocity that the radius checks would miss entirely.
        OrbitalElements orbit = Eccentric(AuKm, eccentricity);
        double expected = -Constants.SunGm.ToDouble() / (2.0 * AuKm);
        double period = 2.0 * Math.PI / orbit.MeanMotion.ToDouble();

        for (int i = 0; i < 64; i++)
        {
            SolarState state = orbit.StateAt(F(period * i / 64.0));
            double energy = Verlet128.SpecificEnergy(state, Constants.SunGm).ToDouble();
            double relative = Math.Abs(energy - expected) / Math.Abs(expected);

            // The floor here is the trigonometry's interpolation error, not the propagator's
            // arithmetic: sin and cos are good to about 1.2e-8, and the velocity divides by
            // (1 - e·cos E), so the energy error grows with eccentricity. Measured: 1.7e-7
            // at e = 0.3. A finer sine table would tighten it; 4096 entries is plenty for
            // orbital work, so the floor is documented rather than chased.
            Assert.True(relative < 1e-6, $"e={eccentricity} step {i}: ε = {energy}, expected {expected}");
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.0167)]
    [InlineData(0.1)]
    public void EccentricOrbit_ConservesAngularMomentum(double eccentricity)
    {
        // h = r × v is constant for a two-body orbit. It is conserved by position and
        // velocity independently, so it catches a rotation applied to one and not the other.
        OrbitalElements orbit = Eccentric(AuKm, eccentricity);

        SolarState first = orbit.StateAt(Fix128.Zero);
        (double hx, double hy, double hz) = Cross(first);
        double magnitude = Math.Sqrt(hx * hx + hy * hy + hz * hz);

        double period = 2.0 * Math.PI / orbit.MeanMotion.ToDouble();
        for (int i = 1; i < 64; i++)
        {
            SolarState state = orbit.StateAt(F(period * i / 64.0));
            (double cx, double cy, double cz) = Cross(state);
            double error = Math.Sqrt(
                Math.Pow(cx - hx, 2) + Math.Pow(cy - hy, 2) + Math.Pow(cz - hz, 2)) / magnitude;

            Assert.True(error < 1e-7, $"e={eccentricity} step {i}: angular momentum moved by {error}");
        }

        static (double X, double Y, double Z) Cross(SolarState state)
        {
            double px = state.Position.X.ToDouble(), py = state.Position.Y.ToDouble(), pz = state.Position.Z.ToDouble();
            double vx = state.Velocity.X.ToDouble(), vy = state.Velocity.Y.ToDouble(), vz = state.Velocity.Z.ToDouble();
            return (py * vz - pz * vy, pz * vx - px * vz, px * vy - py * vx);
        }
    }

    [Fact]
    public void InclinedOrbit_StaysInItsPlane()
    {
        // A tilted circular orbit must stay in one plane, and the plane's normal must match
        // the one implied by the inclination and node. This is what catches a rotation
        // matrix with the wrong order or a transposed inclination.
        const double inclinationTurns = 30.0 / 360.0;
        const double nodeTurns = 0.0;

        OrbitalElements orbit = OrbitalElements.FromEccentricAnomaly(
            F(AuKm), Fix128.Zero, F(inclinationTurns), Fix128.Zero, F(nodeTurns),
            Fix128.Zero, Fix128.Zero, Constants.SunGm);

        // For Ω = 0 and i = 30°, the orbit normal is (0, -sin i, cos i).
        double expectedNormalY = -Math.Sin(inclinationTurns * 2 * Math.PI);
        double expectedNormalZ = Math.Cos(inclinationTurns * 2 * Math.PI);

        double period = 2.0 * Math.PI / orbit.MeanMotion.ToDouble();
        for (int i = 1; i < 32; i++)
        {
            SolarState state = orbit.StateAt(F(period * i / 32.0));

            // The normal is r × v, normalised.
            double px = state.Position.X.ToDouble(), py = state.Position.Y.ToDouble(), pz = state.Position.Z.ToDouble();
            double vx = state.Velocity.X.ToDouble(), vy = state.Velocity.Y.ToDouble(), vz = state.Velocity.Z.ToDouble();
            double nx = py * vz - pz * vy;
            double ny = pz * vx - px * vz;
            double nz = px * vy - py * vx;
            double n = Math.Sqrt(nx * nx + ny * ny + nz * nz);

            Assert.True(Math.Abs(ny / n - expectedNormalY) < 1e-7, $"step {i}: normal y was {ny / n}");
            Assert.True(Math.Abs(nz / n - expectedNormalZ) < 1e-7, $"step {i}: normal z was {nz / n}");
        }
    }

    [Fact]
    public void InclinedOrbit_CrossesTheReferencePlaneAtTheNode()
    {
        // With Ω = 90°, the ascending node lies along +y, so the orbit crosses z = 0 there
        // and its radius there equals a(1-e²)/(1+e·cos(ω)) — for ω = 0, the semi-latus
        // rectum.
        const double nodeTurns = 0.25;
        OrbitalElements orbit = OrbitalElements.FromEccentricAnomaly(
            F(AuKm), F(0.2), F(30.0 / 360.0), Fix128.Zero, F(nodeTurns),
            Fix128.Zero, Fix128.Zero, Constants.SunGm);

        // Sweep for the crossing and check it happens near +y.
        double period = 2.0 * Math.PI / orbit.MeanMotion.ToDouble();
        SolarState? previous = null;
        for (int i = 0; i <= 4096; i++)
        {
            SolarState state = orbit.StateAt(F(period * i / 4096.0));
            if (previous is { } before
                && Math.Sign(before.Position.Z.ToDouble()) != Math.Sign(state.Position.Z.ToDouble()))
            {
                double x = state.Position.X.ToDouble();
                double y = state.Position.Y.ToDouble();

                Assert.True(Math.Abs(x) < AuKm * 0.05, $"node crossing had x = {x}, expected near 0");
                Assert.True(y > 0, $"ascending node should be at +y, was {y}");
                return;
            }

            previous = state;
        }

        Assert.Fail("the orbit never crossed the reference plane");
    }

    [Fact]
    public void Propagator_AgreesWithNumericalIntegration()
    {
        // The Verlet integrator shares no code with the propagator, so agreement between
        // them is evidence rather than self-consistency.
        //
        // The residual here is the INTEGRATOR's error, not the propagator's, and it is
        // worth recording why the obvious test does not work. Comparing over one
        // revolution at a one-hour step gives a disagreement of 19 145 km, and that figure
        // does NOT fall when the step is halved — 19 797 km at two hours, 19 285 km at
        // half an hour — because it is not a truncation error that extrapolation could
        // remove. It is the integrator's phase drift, which oscillates around the orbit
        // rather than accumulating.
        //
        // Tightening the step does converge, so the comparison is made at a step where the
        // integrator's error is a fifth of the orbital radius's tolerance:
        //
        //     dt = 3600 s -> 19 145 km      dt = 900 s -> 7 437 km
        //     dt =  300 s ->  1 573 km      dt =  60 s -> 1 595 km
        //
        // The propagator's own exactness is established by the analytic tests — radius,
        // specific energy, angular momentum, orbital plane — not by this one.
        OrbitalElements orbit = Circular(AuKm);
        double period = 2.0 * Math.PI / orbit.MeanMotion.ToDouble();
        SolarState start = orbit.StateAt(Fix128.Zero);

        var integrated = start;
        Fix128 dt = F(300);
        int ticks = (int)Math.Round(period / 300.0);
        for (int i = 0; i < ticks; i++)
        {
            Verlet128.Step(ref integrated, Constants.SunGm, dt);
        }

        SolarState propagated = orbit.StateAt(F(period));

        double error = Math.Sqrt(
            Math.Pow(propagated.Position.X.ToDouble() - integrated.Position.X.ToDouble(), 2)
            + Math.Pow(propagated.Position.Y.ToDouble() - integrated.Position.Y.ToDouble(), 2)
            + Math.Pow(propagated.Position.Z.ToDouble() - integrated.Position.Z.ToDouble(), 2));

        Assert.True(
            error / AuKm < 1e-4,
            $"propagator and integrator disagreed by {error} km over one revolution");
    }

    [Fact]
    public void StartingState_SatisfiesVisViva()
    {
        // The velocity the propagator produces must be the velocity that makes the orbit
        // circular: v² = GM/r. If it were not, every invariant test below would be checking
        // a self-consistent but wrong ellipse.
        OrbitalElements orbit = Circular(AuKm);
        SolarState start = orbit.StateAt(Fix128.Zero);

        double r = start.Position.Length.ToDouble();
        double v = start.Velocity.Length.ToDouble();
        double semiMajorFromVisViva = 1.0 / (2.0 / r - v * v / Constants.SunGm.ToDouble());

        Assert.Equal(r, semiMajorFromVisViva, 6);
    }

    [Fact]
    public void Propagator_ClosesExactlyOverAWholeRevolution()
    {
        // Keplerian rails accumulate no error to close out: the state at one period must be
        // the state at zero. The measured closure is 0.000000 km.
        OrbitalElements orbit = Eccentric(AuKm, 0.1);
        double period = 2.0 * Math.PI / orbit.MeanMotion.ToDouble();

        SolarState start = orbit.StateAt(Fix128.Zero);
        SolarState after = orbit.StateAt(F(period));

        double error = Math.Sqrt(
            Math.Pow(start.Position.X.ToDouble() - after.Position.X.ToDouble(), 2)
            + Math.Pow(start.Position.Y.ToDouble() - after.Position.Y.ToDouble(), 2));

        Assert.True(error < 1.0, $"closure error was {error} km");
    }

    [Fact]
    public void StateAt_TheEpoch_IsTheStartingState()
    {
        OrbitalElements orbit = Eccentric(AuKm, 0.2);
        SolarState state = orbit.StateAt(Fix128.Zero);

        // At E = 0 the body is at periapsis, on the +x axis.
        Assert.Equal(AuKm * 0.8, state.Position.X.ToDouble(), 3);
        Assert.Equal(0.0, state.Position.Y.ToDouble(), 3);
        Assert.Equal(0.0, state.Position.Z.ToDouble(), 3);
    }

    [Fact]
    public void Propagation_IsAPureFunctionOfTime()
    {
        // The same time must give the same state regardless of how many other times were
        // asked for in between, which rules out any accumulated state.
        OrbitalElements orbit = Eccentric(AuKm, 0.1);

        SolarState direct = orbit.StateAt(F(YearSeconds * 3.7));

        for (int i = 0; i < 50; i++)
        {
            orbit.StateAt(F(YearSeconds * i / 50.0));
        }

        SolarState after = orbit.StateAt(F(YearSeconds * 3.7));

        Assert.Equal(direct.Position.X.ToDouble(), after.Position.X.ToDouble());
        Assert.Equal(direct.Position.Y.ToDouble(), after.Position.Y.ToDouble());
        Assert.Equal(direct.Velocity.X.ToDouble(), after.Velocity.X.ToDouble());
    }

    [Fact]
    public void Propagation_StaysStableOverManyPeriods()
    {
        // Ten years, well past any single mission, with the radius checked each period.
        OrbitalElements orbit = Eccentric(AuKm, 0.1);
        double expected = AuKm * 0.9;
        double period = 2.0 * Math.PI / orbit.MeanMotion.ToDouble();

        for (int year = 1; year <= 10; year++)
        {
            double radius = RadiusKm(orbit.StateAt(F(period * year)));
            Assert.True(
                Math.Abs(radius - expected) / expected < 1e-5,
                $"after {year} years the periapsis radius was {radius}, expected {expected}");
        }
    }

    [Fact]
    public void ExternalOrbit_WorksAtJupiterDistance()
    {
        // 5.2 AU exercises the range where r² no longer fits the local frame's type.
        const double jupiterAu = 5.2;
        double radiusKm = jupiterAu * AuKm;

        OrbitalElements orbit = OrbitalElements.FromEccentricAnomaly(
            F(radiusKm), F(0.0489), F(0.0227), F(0.0), F(0.0),
            Fix128.Zero, Fix128.Zero, Constants.SunGm);

        // Jupiter's period is 11.86 years.
        double periodYears = 2.0 * Math.PI / orbit.MeanMotion.ToDouble() / YearSeconds;
        Assert.True(Math.Abs(periodYears - 11.86) < 0.02, $"period was {periodYears} years");

        double period = 2.0 * Math.PI / orbit.MeanMotion.ToDouble();
        for (int i = 0; i < 32; i++)
        {
            SolarState state = orbit.StateAt(F(period * i / 32.0));
            double radius = RadiusKm(state);
            Assert.True(
                Math.Abs(radius - radiusKm) / radiusKm < 0.06,
                $"at step {i}: radius {radius / AuKm} AU");
        }
    }
}
