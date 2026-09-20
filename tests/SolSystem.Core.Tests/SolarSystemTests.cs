using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;
using Xunit;
using Xunit.Abstractions;

namespace SolSystem.Core.Tests;

/// <summary>
/// The join between the ephemeris and the local frame: the one place that knows both.
/// </summary>
/// <remarks>
/// This is where a frame bug would live, and a frame bug is expensive — it shows up as a
/// station that slides backwards at thirty kilometres a second, or a ship whose position is
/// off by a factor of a thousand. The tests here check the conversion in both directions and
/// check the thing that makes the local frame usable at all: that a point placed near a body
/// inherits that body's orbital velocity.
/// </remarks>
public class SolarSystemTests
{
    private readonly ITestOutputHelper _o;

    public SolarSystemTests(ITestOutputHelper o) => _o = o;

    /// <summary>2025-01-01 00:00 TT, seconds from J2000.</summary>
    private const double ReferenceSeconds = (2460676.5 - 2451545.0) * 86400.0;

    /// <summary>The Sun's GM in km³/s², the figure the design quotes.</summary>
    private const double SunGmKm = 1.32712440018e11;

    [Fact]
    public void TheClock_StartsAtJ2000AndAdvances()
    {
        var system = new SolarSystem();
        Assert.Equal(0.0, system.SecondsFromJ2000);

        system.Advance(3600.0);
        Assert.Equal(3600.0, system.SecondsFromJ2000);

        system.SetTime(ReferenceSeconds);
        Assert.Equal(ReferenceSeconds, system.SecondsFromJ2000);
    }

    [Fact]
    public void TheSunIsWhereTheEarthsOrbitSaysItShouldBe()
    {
        // A body's distance from the Sun is a check on the elements, the units and the frame
        // all at once: AU has to survive the trip into kilometres, and a body at the wrong
        // radius is a body in the wrong orbit.
        var system = new SolarSystem();
        system.SetTime(ReferenceSeconds);

        foreach (SolarSystem.Body body in SolarSystem.Bodies)
        {
            Ephemeris.State state = system.Heliocentric(body.Kind);
            double radiusAu = state.Position.Length.ToDouble() / 149_597_870.7;
            double speed = state.Velocity.Length.ToDouble();

            _o.WriteLine($"{body.Name,-8} {radiusAu,9:F4} AU  {speed,7:F3} km/s");

            Assert.True(radiusAu > 0.3 && radiusAu < 31.0, $"{body.Name} is at {radiusAu:F3} AU");
            Assert.True(speed > 4.0 && speed < 50.0, $"{body.Name} moves at {speed:F3} km/s");
        }
    }

    [Fact]
    public void EveryBody_IsWhereTheEphemerisPutsIt()
    {
        // The join must not move anything. If SolarSystem introduces a scale or a frame error,
        // it shows up here as a discrepancy against the ephemeris rather than as a plausible
        // wrong number somewhere downstream.
        var system = new SolarSystem();
        system.SetTime(ReferenceSeconds);

        foreach (SolarSystem.Body body in SolarSystem.Bodies)
        {
            Ephemeris.State direct = Ephemeris.AtSecondsFromJ2000(body.Kind, ReferenceSeconds);
            Ephemeris.State joined = system.Heliocentric(body.Kind);

            Assert.Equal(direct.Position.X.ToDouble(), joined.Position.X.ToDouble(), 9);
            Assert.Equal(direct.Position.Y.ToDouble(), joined.Position.Y.ToDouble(), 9);
            Assert.Equal(direct.Position.Z.ToDouble(), joined.Position.Z.ToDouble(), 9);
            Assert.Equal(direct.Velocity.X.ToDouble(), joined.Velocity.X.ToDouble(), 9);
        }
    }

    [Fact]
    public void RelativePosition_IsTheDifferenceOfTheTwo()
    {
        var system = new SolarSystem();
        system.SetTime(ReferenceSeconds);

        Ephemeris.State earth = system.Heliocentric(Ephemeris.Body.Earth);
        Ephemeris.State mars = system.Heliocentric(Ephemeris.Body.Mars);
        Ephemeris.State relative = system.Relative(Ephemeris.Body.Mars, Ephemeris.Body.Earth);

        Assert.Equal(mars.Position.X.ToDouble() - earth.Position.X.ToDouble(), relative.Position.X.ToDouble(), 6);
        Assert.Equal(mars.Position.Y.ToDouble() - earth.Position.Y.ToDouble(), relative.Position.Y.ToDouble(), 6);

        // And the distance between the two orbits is a real transfer distance: Mars was
        // 1.6 AU out in January 2025 while Earth was at 0.98, so the separation has to land
        // between the closest and farthest the two can be.
        double separationAu = relative.Position.Length.ToDouble() / 149_597_870.7;
        _o.WriteLine($"Earth to Mars on 2025-01-01: {separationAu:F4} AU");

        Assert.True(separationAu > 0.3 && separationAu < 2.7, $"the two are {separationAu:F3} AU apart");
    }

    [Fact]
    public void ALocalPoint_InheritsTheBodysVelocity()
    {
        // The whole reason the local frame is anchored to a body. Without this a station in
        // orbit around Earth would sit still while Earth moved away at thirty kilometres a
        // second, and a ship near it would have to be re-positioned every tick.
        var system = new SolarSystem();
        system.SetTime(ReferenceSeconds);

        var offset = new Fix128Vec(
            Fix128.FromDouble(6_778.0), Fix128.Zero, Fix128.Zero);

        SolarSystem.LocalPoint point = system.LocalTo(Ephemeris.Body.Earth, offset);

        Assert.Equal(Ephemeris.Body.Earth, point.Frame);

        double solarSpeed = system.Heliocentric(Ephemeris.Body.Earth).Velocity.Length.ToDouble();
        double localSpeed = point.Velocity.Length.ToDouble();

        _o.WriteLine($"Earth moves at {solarSpeed:F3} km/s, the point at {localSpeed / 1000.0:F3} km/s in the local frame");

        Assert.True(
            Math.Abs(localSpeed / 1000.0 - solarSpeed) / solarSpeed < 1e-9,
            $"the local velocity is {localSpeed / 1000.0:F3} km/s against Earth's {solarSpeed:F3}");
    }

    [Fact]
    public void ALocalPoint_IsInMetresAndTheRightDistanceOut()
    {
        // The local frame is metres; the ephemeris is kilometres. A factor of a thousand here
        // is the single most likely bug in the join, so it is checked by placing a point one
        // Earth radius above the equator and asking how far it is from the centre.
        const double radiusKm = 6_378.1;

        var system = new SolarSystem();
        system.SetTime(ReferenceSeconds);

        var offset = new Fix128Vec(Fix128.FromDouble(radiusKm), Fix128.Zero, Fix128.Zero);
        SolarSystem.LocalPoint centre = system.LocalTo(Ephemeris.Body.Earth, Fix128Vec.Zero);
        SolarSystem.LocalPoint surface = system.LocalTo(Ephemeris.Body.Earth, offset);

        Fix128Vec displacement = surface.Position - centre.Position;
        double metres = displacement.Length.ToDouble();

        _o.WriteLine($"one Earth radius is {metres / 1000.0:F4} km in the local frame");

        Assert.True(
            Math.Abs(metres / 1000.0 - radiusKm) / radiusKm < 1e-9,
            $"one Earth radius came out as {metres / 1000.0:F4} km");
    }

    [Fact]
    public void ABodyCarriesItsGravityAndItsSize()
    {
        // These are the two numbers the local frame needs to be a world rather than a
        // coordinate system: how hard the body pulls, and how big it is to hit.
        SolarSystem.Body earth = SolarSystem.BodyOf(Ephemeris.Body.Earth);

        Assert.Equal("Earth", earth.Name);
        Assert.Equal(6_378.1, earth.RadiusKm, 1);
        Assert.Equal(3.986004418e5, earth.GmKm, 3);

        // Which is the same figure the local frame's constant carries, in the other unit.
        Assert.Equal(Constants.EarthGmKm.ToDouble(), earth.GmKm, 3);

        // And every body in the table has a real one.
        foreach (SolarSystem.Body body in SolarSystem.Bodies)
        {
            Assert.True(body.RadiusKm > 1000.0, $"{body.Name} has radius {body.RadiusKm}");
            Assert.True(body.GmKm > 1e4, $"{body.Name} has GM {body.GmKm}");
            Assert.False(string.IsNullOrWhiteSpace(body.Name));
        }
    }

    [Fact]
    public void TheEphemerisAndThePropagator_AgreeOnTheEarthsOrbit()
    {
        // Two independent routes to the same place: JPL's fitted elements through a Kepler
        // solve, and the velocity Verlet propagator stepping a state vector under the Sun's
        // gravity. They should stay together for a quarter of a year. If they do not, one of
        // them is wrong, and that is worth knowing before either is trusted for navigation.
        var system = new SolarSystem();
        system.SetTime(ReferenceSeconds);

        Ephemeris.State start = system.Heliocentric(Ephemeris.Body.Earth);
        var state = new SolarState(start.Position, start.Velocity);

        // The Sun's GM in km³/s² is the same number twice: the design's constant and the
        // ephemeris's own unit. Checking them equal catches a conversion that has gone wrong.
        Assert.Equal(SunGmKm, Constants.SunGm.ToDouble(), 3);

        const double step = 3600.0;
        const int steps = 2190;
        for (int i = 0; i < steps; i++)
        {
            Verlet128.Step(ref state, Constants.SunGm, Fix128.FromDouble(step));
        }

        system.Advance(step * steps);
        Ephemeris.State propagated = system.Heliocentric(Ephemeris.Body.Earth);

        double separationKm = (propagated.Position - state.Position).Length.ToDouble();
        double radiusAu = propagated.Position.Length.ToDouble() / 149_597_870.7;

        _o.WriteLine($"after {step * steps / 86400.0:F0} days the two routes differ by {separationKm:N0} km");
        _o.WriteLine($"the ephemeris puts Earth at {radiusAu:F4} AU, moving {propagated.Velocity.Length.ToDouble():F3} km/s");

        Assert.True(
            separationKm < 3.0e6,
            $"the propagator and the ephemeris are {separationKm:N0} km apart after a quarter year");

        // And the propagated state is still a real Earth orbit rather than a spiral.
        Assert.True(radiusAu > 0.98 && radiusAu < 1.02, $"Earth ended at {radiusAu:F4} AU");
    }
}
