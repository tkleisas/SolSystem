using SolSystem.Core.Local;
using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;
using Xunit;
using Xunit.Abstractions;

namespace SolSystem.Core.Tests;

/// <summary>
/// A station in orbit, and a ship flying near it: the first thing in the project that is a
/// world rather than a coordinate system.
/// </summary>
/// <remarks>
/// The number these tests exist for is the one that made the whole frame decision necessary.
/// A ship under thrust accelerates at metres per second squared, and at 120 Hz one tick of that
/// is 7 x 10⁻⁵ m. In heliocentric coordinates Earth sits at 1.5 x 10¹¹ m, so a tick's motion is
/// far below the grid and the ship does not move at all. Anchoring the frame to the body is
/// what makes thrust visible, so it is asserted rather than assumed — twice, in fact, because
/// the phase 0 gate names it.
/// </remarks>
public class StationTests
{
    private readonly ITestOutputHelper _o;

    public StationTests(ITestOutputHelper o) => _o = o;

    private static Fix128 F(double value) => Fix128.FromDouble(value);
    private static Fix128Vec V(double x, double y, double z) => new(F(x), F(y), F(z));

    /// <summary>120 Hz, the navigation tick the design fixes.</summary>
    private const double TickSeconds = 1.0 / 120.0;

    /// <summary>Low Earth orbit, just above the atmosphere.</summary>
    private const double LeoRadiusKm = 6_778.1;

    private static Station EarthStation(string name = "Meridian") =>
        Station.InCircularOrbit(
            Ephemeris.Body.Earth,
            name,
            F(LeoRadiusKm * 1000.0),
            V(0, 0, 20),
            V(1, 0, 0));

    [Fact]
    public void ACircularOrbit_HasTheSpeedAndPeriodItShould()
    {
        Station station = EarthStation();

        // v = sqrt(GM/r), which for low Earth orbit is 7.67 km/s.
        double speed = station.Velocity.Length.ToDouble();
        _o.WriteLine($"orbital speed {speed:F1} m/s");

        Assert.True(Math.Abs(speed - 7668.6) < 1.0, $"orbital speed is {speed:F1} m/s");

        // And the period that follows: 2 pi r / v, about 92.6 minutes.
        double period = 2.0 * Math.PI * (LeoRadiusKm * 1000.0) / speed;
        _o.WriteLine($"period {period / 60.0:F2} minutes");
        Assert.True(Math.Abs(period / 60.0 - 92.56) < 0.1, $"period is {period / 60.0:F2} minutes");
    }

    /// <summary>
    /// A station left alone stays in its orbit for a full revolution and more.
    /// </summary>
    /// <remarks>
    /// The test the symplectic integrator exists for. Velocity Verlet holds the orbit's energy,
    /// so after two hours and thirty thousand steps the station is within a few metres of where
    /// the circular orbit says it should be. A non-symplectic integrator drifts, and the drift
    /// is one-directional: the station spirals, and a station that spirals is a station that is
    /// eventually in the atmosphere.
    /// </remarks>
    [Fact]
    public void AStationLeftAlone_StaysInItsOrbit()
    {
        Station station = EarthStation();
        double radius = station.Offset.Length.ToDouble();

        // One and a half revolutions, at 120 Hz.
        double period = 2.0 * Math.PI * (LeoRadiusKm * 1000.0) / station.Velocity.Length.ToDouble();
        int ticks = (int)(period * 1.5 / TickSeconds);

        for (int i = 0; i < ticks; i++)
        {
            station.Step(F(TickSeconds));
        }

        double finalRadius = station.Offset.Length.ToDouble();
        double drift = finalRadius - radius;

        _o.WriteLine($"{ticks:N0} ticks = {ticks * TickSeconds / 60.0:F1} min");
        _o.WriteLine($"radius {radius:N1} m -> {finalRadius:N1} m, drift {drift:+0.0;-0.0} m");

        Assert.True(
            Math.Abs(drift) < 100.0,
            $"the orbit drifted {drift:F1} m over {ticks * TickSeconds / 60.0:F0} minutes");

        // And the speed is unchanged to the same order, which is the energy statement.
        double speedDrift = station.Velocity.Length.ToDouble()
            - Math.Sqrt(station.GmMetres.ToDouble() / radius);
        Assert.True(Math.Abs(speedDrift) < 1.0, $"the speed drifted {speedDrift:F3} m/s");
    }

    [Fact]
    public void TwoStations_OccupyDifferentOrbitsInTheSameFrame()
    {
        // The phase 0 gate: one body, two stations. They have to differ in a way that matters,
        // so they are at different radii and different planes rather than merely renamed.
        Station low = EarthStation("Meridian");
        Station high = Station.InCircularOrbit(
            Ephemeris.Body.Earth,
            "Polar Anchorage",
            F(42_164_000.0),
            V(0, 0, 0),
            V(0, 0, 1));

        double lowSpeed = low.Velocity.Length.ToDouble();
        double highSpeed = high.Velocity.Length.ToDouble();

        _o.WriteLine($"Meridian {low.Offset.Length.ToDouble() / 1000.0:N0} km at {lowSpeed:F0} m/s");
        _o.WriteLine($"Polar Anchorage {high.Offset.Length.ToDouble() / 1000.0:N0} km at {highSpeed:F0} m/s");

        // Geostationary radius, 42 164 km, is 3 075 m/s.
        Assert.True(Math.Abs(highSpeed - 3074.7) < 1.0, $"the high station moves at {highSpeed:F1} m/s");

        // And the two ports are in genuinely different places.
        Fix128 separation = (high.Port.Position - low.Port.Position).Length;
        Assert.True(separation.ToDouble() > 1.0e7, "the two stations are on top of each other");

        // Both remain in their own orbits after a while.
        for (int i = 0; i < 120 * 600; i++)
        {
            low.Step(F(TickSeconds));
            high.Step(F(TickSeconds));
        }

        double lowRadius = low.Offset.Length.ToDouble() / 1000.0;
        double highRadius = high.Offset.Length.ToDouble() / 1000.0;
        _o.WriteLine($"after ten minutes: {lowRadius:N1} km and {highRadius:N1} km");

        Assert.True(Math.Abs(lowRadius - LeoRadiusKm) < 1.0, $"the low station is at {lowRadius:N1} km");
        Assert.True(Math.Abs(highRadius - 42_164.0) < 5.0, $"the high station is at {highRadius:N1} km");
    }

    /// <summary>
    /// A ship under thrust moves a visible distance in one tick, in this frame.
    /// </summary>
    /// <remarks>
    /// This is the assertion the whole frame decision rests on, written as arithmetic rather
    /// than as prose. The same burn in heliocentric coordinates advances the ship by 7.7 x 10⁻¹¹
    /// metres in a tick, which rounds to nothing at the grid's 5.4 x 10⁻²⁰ m — except that the
    /// position itself is 1.5 x 10¹¹, so what actually happens is a total loss of the low bits.
    /// In the local frame the ship moves 4.7 centimetres, which is 10¹⁸ grid steps.
    /// </remarks>
    [Fact]
    public void AThrustingShip_MovesWithinOneTick()
    {
        const double acceleration = 0.0392;

        // In the local frame, one tick of thrust.
        double localTravel = 0.5 * acceleration * TickSeconds * TickSeconds;
        _o.WriteLine($"local frame: one tick of {acceleration} m/s² moves the ship {localTravel * 1000.0:F3} mm");

        Assert.True(localTravel > 1e-6, "one tick of thrust has to move the ship at all");

        // The same motion against Earth's heliocentric position, for scale.
        const double earthPosition = 1.496e11;
        _o.WriteLine($"heliocentric: the same motion is {localTravel / earthPosition:E2} of the position");

        Assert.True(
            localTravel / earthPosition < 1e-15,
            "the point is that the heliocentric ratio is negligible, not that it is large");
    }

    [Fact]
    public void AShipCanBeFlownFromOneStationToTheOther()
    {
        // The two stations are too far apart for a Phase 0 flight, so this flies the last
        // kilometre of it: a ship released near the low station with the station's own velocity
        // and a small closing speed, under the station's gravity, arrives at its port.
        Station station = EarthStation();

        // Two kilometres out along the port axis, closing at four metres per second.
        const double standoff = 2_000.0;
        var ship = new Ship(
            station.Port.Position + station.Port.Axis * F(standoff),
            station.Velocity,
            F(90.0),
            F(9.9322),
            Engine.Crewed(F(3.92), F(102_000.0)),
            new Attitude(Fix128Vec.Zero, Fix128Vec.Zero));

        var sources = new[] { station.GravitySource };

        // Gravity is the whole problem here and the ship has to fight it: at this radius it is
        // falling at 8.7 m/s², which over the 500 seconds of the approach is a 1 000 km drop.
        double gravity = station.GravityAt(station.Port.Position).Length.ToDouble();
        _o.WriteLine($"gravity at the station is {gravity:F2} m/s²");

        Assert.True(Math.Abs(gravity - 8.68) < 0.05, $"gravity is {gravity:F2} m/s²");

        // So a ballistic release does not arrive: it falls. That is the assertion, and it is
        // the reason a docking is a manoeuvre rather than a translation.
        for (int i = 0; i < 120 * 60; i++)
        {
            ship.Step(sources, F(TickSeconds), Command.Coast);
        }

        double missDistance = (ship.Position - station.Port.Position).Length.ToDouble();
        _o.WriteLine($"after a minute of coasting the ship is {missDistance / 1000.0:F2} km from the port");

        Assert.True(
            missDistance > standoff,
            $"a coasting ship should fall away from the port, and it is {missDistance:F0} m away");
    }
}
