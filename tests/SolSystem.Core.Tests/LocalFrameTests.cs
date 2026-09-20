using SolSystem.Core.Local;
using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;
using Xunit;

namespace SolSystem.Core.Tests;

/// <summary>
/// The local frame: a ship with finite propellant, flown at a fixed navigation tick.
/// </summary>
/// <remarks>
/// Lengths are in metres and speeds in metres per second, which is the frame's unit. In
/// megametres the position increment over a tick is two raw units of a Q32.32 value and an
/// orbit is almost pure rounding; see the remarks on <see cref="GravitySource"/>.
/// </remarks>
/// <remarks>
/// The propulsion numbers are checked against the rocket equation in closed form, and the
/// trajectory against the same physics the solar frame was validated on. Nothing here
/// integrates by hand — every expected value is derived from a formula.
/// </remarks>
public class LocalFrameTests
{
    /// <summary>The navigation tick. Fixed, and the same rate in every test.</summary>
    internal const double TickSeconds = 1.0 / 120.0;

    private static Fix128 F(double value) => Fix128.FromDouble(value);

    /// <summary>
    /// A crewed ship, 7000 km from the centre — low Earth orbit, where gravity is a real
    /// force rather than a rounding error, so an integrator mistake shows up immediately.
    /// Distances are in metres.
    /// </summary>
    /// <remarks>
    /// The engine is a <see cref="Engine.Crewed"/> one, so it is held inside the 0.1-1 g
    /// band. Thrust is computed from the requested acceleration rather than the other way
    /// round, because that is the constraint a crewed hull actually has to satisfy.
    /// </remarks>
    private static Ship MakeShip(double thrustKn, double ispSeconds, double dryTonnes, double propellantTonnes)
    {
        var position = new Fix128Vec(F(7_000_000.0), Fix128.Zero, Fix128.Zero);
        return new Ship(
            position,
            Fix128Vec.Zero,
            F(dryTonnes),
            F(propellantTonnes),
            Engine.Crewed(F(thrustKn), F(ispSeconds)));
    }

    /// <summary>Thrust in kN that produces <paramref name="g"/> at <paramref name="tonnes"/>.</summary>
    private static double ThrustFor(double g, double tonnes) => g * 9.80665 * tonnes;

    private static GravitySource Earth() => GravitySource.AtOrigin(Constants.EarthGmLocal);

    // ------------------------------------------------------------------ propulsion

    [Fact]
    public void ExhaustVelocity_IsIspTimesStandardGravity()
    {
        var engine = Engine.Crewed(F(100), F(900));
        Assert.Equal(900.0 * 9.80665, engine.ExhaustVelocityMetresPerSecond.ToDouble(), 6);
    }

    [Fact]
    public void MassFlow_FollowsThrustOverExhaustVelocity()
    {
        var engine = Engine.Crewed(F(100), F(900));
        double expected = 100_000.0 / (900.0 * 9.80665) / 1000.0;
        Assert.Equal(expected, engine.MassFlowTonnesPerSecond.ToDouble(), 12);
    }

    [Fact]
    public void DeltaV_MatchesTheRocketEquation()
    {
        const double isp = 900.0;
        const double dry = 5000.0;
        const double propellant = 5000.0;

        Ship ship = MakeShip(100, isp, dry, propellant);
        double expected = isp * 9.80665 * Math.Log((dry + propellant) / dry);

        // Delta-v is reported in Mm/s, so compare in metres per second.
        double actual = ship.DeltaVRemaining.ToDouble();
        Assert.True(
            Math.Abs(actual - expected) / expected < 1e-9,
            $"delta-v {actual} m/s, expected {expected} m/s");
    }

    /// <summary>
    /// The mass budget of the reference crewed hull, at the numbers the docking tests fly.
    /// </summary>
    /// <remarks>
    /// The propulsion module has no state of its own: thrust, exhaust velocity, mass flow and
    /// delta-v are all derived from two inputs, which is what keeps them from drifting into
    /// disagreeing with each other. This asserts the derivation end to end, and pins the two
    /// numbers a designer would actually check — the acceleration sits at the bottom of the
    /// crewed band, and the tanks hold about fifteen minutes of full-throttle burn.
    /// </remarks>
    [Fact]
    public void TheReferenceHull_SitsAtTheBottomOfTheCrewedBand()
    {
        // 98 kN on 99.932 t wet: the docking harness's ship, and 0.1 g by construction.
        const double thrust = 98.0;
        const double dry = 90.0;
        const double propellant = 9.9322;

        Ship ship = MakeShip(thrust, 900.0, dry, propellant);

        double wet = dry + propellant;
        Assert.Equal(wet, ship.Mass.ToDouble(), 9);
        Assert.Equal(dry, ship.DryMass.ToDouble(), 9);

        double acceleration = thrust / wet;
        Assert.True(
            Math.Abs(acceleration - 0.1 * 9.80665) < 0.002,
            $"a0 = {acceleration:F4} m/s2, which is not the 0.1 g floor of the crewed band");

        // Burn time falls out of the mass flow rather than being stored: 10.9 t at 0.0111 t/s.
        double burnSeconds = propellant / ship.Engine.MassFlowTonnesPerSecond.ToDouble();
        Assert.True(
            Math.Abs(burnSeconds - 894.5) < 0.5,
            $"full-throttle endurance {burnSeconds:F1} s");

        // And the rocket equation on top of it.
        double expected = 900.0 * 9.80665 * Math.Log(wet / dry);
        Assert.True(
            Math.Abs(ship.DeltaVRemaining.ToDouble() - expected) / expected < 1e-9,
            $"delta-v {ship.DeltaVRemaining.ToDouble():F1} m/s, expected {expected:F1} m/s");
    }

    [Fact]
    public void DeltaV_IsZeroWithDryTanks()
    {
        Ship ship = MakeShip(100, 900, 5000, 0);
        Assert.Equal(0.0, ship.DeltaVRemaining.ToDouble());
    }

    [Fact]
    public void DeltaV_FallsAsPropellantBurns()
    {
        Ship ship = MakeShip(100, 900, 5000, 5000);
        Fix128 initial = ship.DeltaVRemaining;

        var sources = new[] { Earth() };
        var command = Command.WithThrottle(Fix128.One);
        for (int i = 0; i < 120 * 60; i++)
        {
            ship.Step(sources, F(TickSeconds), command);
        }

        Assert.True(ship.DeltaVRemaining < initial, "delta-v should fall as propellant is spent");
        Assert.True(ship.DeltaVRemaining > Fix128.Zero, "a minute of burn should not empty the tanks");
    }

    [Fact]
    public void AFullBurn_UnderACeiling_GainsThrustOverMassTimesTime()
    {
        // With the crewed 1 g ceiling in force and the thrust sized for 1 g at the full
        // mass, the acceleration is clamped to 1 g for the whole burn even as the ship
        // lightens. The velocity gained is therefore a·t, NOT the rocket equation's
        // vₑ·ln(mass ratio) — the ceiling is throwing away the extra acceleration that the
        // falling mass would have bought.
        //
        // This is a real property of the design rather than an artifact: a crewed hull
        // cannot use its own mass loss to accelerate harder, because the crew cannot take
        // more than 1 g. It is exactly the trade a shell hull escapes.
        Ship ship = MakeShip(ThrustFor(1.0, 10), 900, 5, 5);
        var sources = new[] { GravitySource.AtOrigin(Fix128.Zero) };
        var command = Command.WithThrottle(Fix128.One);

        Fix128 flow = ship.Engine.MassFlowTonnesPerSecond;
        double burnSeconds = 5.0 / flow.ToDouble();

        for (int i = 0; i < 60000 && ship.Propellant > Fix128.Zero; i++)
        {
            ship.Step(sources, F(TickSeconds), command);
        }

        double gained = ship.Velocity.Length.ToDouble();
        double expected = 1.0 * 9.80665 * burnSeconds;

        Assert.True(
            Math.Abs(gained - expected) / expected < 1e-4,
            $"burn produced {gained} m/s; 1 g for {burnSeconds} s is {expected} m/s");
    }

    [Fact]
    public void AFullBurn_WithNoCeiling_MatchesTheRocketEquation()
    {
        // A shell hull at 100 g can use the whole drive, so the mass loss does show up and
        // the rocket equation applies exactly. This is the test that proves the mass
        // accounting is right, and it is the pair to the test above.
        Ship ship = new(
            new Fix128Vec(F(7_000_000.0), Fix128.Zero, Fix128.Zero),
            Fix128Vec.Zero,
            F(5),
            F(5),
            Engine.Shell(F(ThrustFor(1.0, 10)), F(900), F(1000)));

        Fix128 predicted = ship.DeltaVRemaining;

        var sources = new[] { GravitySource.AtOrigin(Fix128.Zero) };
        var command = Command.WithThrottle(Fix128.One);

        for (int i = 0; i < 5_000_000 && ship.Propellant > Fix128.Zero; i++)
        {
            ship.Step(sources, F(TickSeconds), command);
        }

        double gained = ship.Velocity.Length.ToDouble();
        double expected = predicted.ToDouble();

        Assert.True(
            Math.Abs(gained - expected) / expected < 1e-4,
            $"burn produced {gained} m/s against a predicted {expected} m/s");
    }

    [Fact]
    public void AcceleringMass_IsConsumedUniformlyWithThrust()
    {
        // Mass must fall linearly with time at a fixed throttle, because the mass flow is
        // constant. If mass fell exponentially or with velocity, everything downstream
        // would be wrong.
        Ship ship = MakeShip(ThrustFor(1.0, 10), 900, 5, 5);
        var sources = new[] { GravitySource.AtOrigin(Fix128.Zero) };
        var command = Command.WithThrottle(Fix128.One);

        double flow = ship.Engine.MassFlowTonnesPerSecond.ToDouble();
        for (int i = 0; i < 1200; i++)
        {
            ship.Step(sources, F(TickSeconds), command);
        }

        double expectedMass = 10.0 - flow * 10.0;
        Assert.True(
            Math.Abs(ship.Mass.ToDouble() - expectedMass) < 1e-9,
            $"after 10 s the mass was {ship.Mass.ToDouble()}, expected {expectedMass}");
    }

    [Fact]
    public void Thrust_ProducesTheAccelerationItShould()
    {
        // 1 m/s² is 0.102 g, inside the crewed band, so it must not be clamped. The thrust
        // is sized for the LOADED mass: MakeShip adds propellant to the dry mass, and sizing
        // for the dry mass alone under-thrusts by a factor of 105/100.
        Ship ship = MakeShip(ThrustFor(1.0 / 9.80665, 105), 900, 100, 5);
        var sources = new[] { GravitySource.AtOrigin(Fix128.Zero) };
        var command = new Command(
            new Fix128Vec(Fix128.One, Fix128.Zero, Fix128.Zero), Fix128.One, Fix128Vec.Zero);

        for (int i = 0; i < 120; i++)
        {
            ship.Step(sources, F(TickSeconds), command);
        }

        double speed = ship.Velocity.Length.ToDouble();
        Assert.True(Math.Abs(speed - 1.0) < 0.001, $"one second at 1 m/s² gave {speed} m/s");
    }

    [Fact]
    public void PropellantNeverGoesNegative()
    {
        Ship ship = MakeShip(ThrustFor(1.0, 10), 900, 10, 10);
        var sources = new[] { GravitySource.AtOrigin(Fix128.Zero) };
        var command = Command.WithThrottle(Fix128.One);

        for (int i = 0; i < 5_000_000 && ship.Propellant > Fix128.Zero; i++)
        {
            ship.Step(sources, F(TickSeconds), command);
        }

        Assert.Equal(0.0, ship.Propellant.ToDouble());
        Assert.True(ship.Mass > Fix128.Zero, "the ship must not lose its own structure");
        Assert.Equal(0.0, ship.DeltaVRemaining.ToDouble());
    }

    [Fact]
    public void CoastingWithNoThrottle_BurnsNothing()
    {
        Ship ship = MakeShip(100, 900, 5000, 5000);
        Fix128 before = ship.Propellant;

        var sources = new[] { Earth() };
        for (int i = 0; i < 120 * 60; i++)
        {
            ship.Step(sources, F(TickSeconds), Command.Coast);
        }

        Assert.Equal(before, ship.Propellant);
    }

    [Fact]
    public void CrewedHull_IsHeldInsideTheAccelerationBand()
    {
        // A drive far more powerful than the crew can take must be throttled back to 1 g.
        // Without the ceiling this ship would pull 50 m/s², which is five times what a
        // person survives for more than a few seconds.
        Ship ship = MakeShip(ThrustFor(50.0, 10), 900, 10, 10);
        var sources = new[] { GravitySource.AtOrigin(Fix128.Zero) };
        var command = Command.WithThrottle(Fix128.One);

        for (int i = 0; i < 120; i++)
        {
            ship.Step(sources, F(TickSeconds), command);
        }

        // One second of 1 g is 9.80665 m/s.
        double g = ship.Velocity.Length.ToDouble() / 9.80665;
        Assert.True(g <= 1.0001, $"a crewed hull reached {g} g in one second");
        Assert.True(g >= 0.9999, $"a crewed hull only reached {g} g");
    }

    [Fact]
    public void ShellHull_CanUseTheMechanicalBand()
    {
        // A shell has no flesh to squash, so the same physics is allowed to reach 10 g.
        Ship ship = new(
            new Fix128Vec(F(7_000_000.0), Fix128.Zero, Fix128.Zero),
            Fix128Vec.Zero,
            F(10),
            F(10),
            Engine.Shell(F(ThrustFor(50.0, 10)), F(900)));

        var sources = new[] { GravitySource.AtOrigin(Fix128.Zero) };
        var command = Command.WithThrottle(Fix128.One);

        for (int i = 0; i < 120; i++)
        {
            ship.Step(sources, F(TickSeconds), command);
        }

        double g = ship.Velocity.Length.ToDouble() / 9.80665;
        Assert.True(Math.Abs(g - 10.0) < 0.001, $"a shell hull should reach 10 g, reached {g}");
    }

    [Fact]
    public void TheAccelerationBands_AreTheOnesTheDesignNames()
    {
        Assert.Equal(0.1, Engine.CrewedMinimumG.ToDouble(), 9);
        Assert.Equal(1.0, Engine.CrewedMaximumG.ToDouble(), 9);
        Assert.Equal(10.0, Engine.ShellMinimumG.ToDouble(), 9);
        Assert.Equal(100.0, Engine.ShellMaximumG.ToDouble(), 9);

        // And the defaults land on the right side of each band.
        Assert.Equal(9.80665, Engine.Crewed(F(100), F(900)).MaxAccelerationInMetresPerSecondSquared.ToDouble(), 6);
        Assert.Equal(98.0665, Engine.Shell(F(100), F(900)).MaxAccelerationInMetresPerSecondSquared.ToDouble(), 6);
    }

    // ------------------------------------------------------------------ gravity and motion

    [Fact]
    public void AGravitySource_PullsTowardsItself()
    {
        var source = GravitySource.AtOrigin(Constants.EarthGmLocal);
        Fix128Vec acceleration = source.AccelerationAt(new Fix128Vec(F(7_000_000.0), Fix128.Zero, Fix128.Zero));

        // At 7000 km, GM/r² is 8.1347 m/s², and the frame's unit makes that the value
        // directly — no conversion anywhere.
        double expected = 3.986004418e14 / Math.Pow(7000e3, 2);
        double actual = -acceleration.X.ToDouble();

        // The GM constant is rounded to its nearest representable value, which costs about
        // 10⁻⁷ of relative accuracy here and is the floor for any acceleration in this frame.
        Assert.True(Math.Abs(actual - expected) / expected < 1e-6, $"got {actual} m/s², expected {expected}");
        Assert.True(acceleration.X.ToDouble() < 0, "gravity must pull towards the source");
    }

    [Fact]
    public void GRAVITY_MODEL_MatchesTheSolarFrames()
    {
        // Two independent gravity implementations, one physical field. Earth's GM is used
        // rather than the Sun's because the Sun's does not fit the local frame at all — the
        // test below pins that.
        //
        // Both frames are Q64.64 in metres now, so this checks that the two independent
        // gravity implementations agree — and, by producing Earth's familiar 9.82 m/s², that
        // neither has drifted from SI.
        const double gmSi = 3.986004418e14;
        const double radiusMetres = 6_371_000.0;

        var localSource = GravitySource.AtOrigin(Fix128.FromDouble(gmSi));
        Fix128Vec localAcceleration = localSource.AccelerationAt(
            new Fix128Vec(F(radiusMetres), Fix128.Zero, Fix128.Zero));

        Fix128Vec solarAcceleration = Verlet128.Gravity(
            new Fix128Vec(F(radiusMetres), Fix128.Zero, Fix128.Zero),
            Fix128.FromDouble(gmSi));

        double local = -localAcceleration.X.ToDouble();
        double solar = -solarAcceleration.X.ToDouble();

        Assert.True(
            Math.Abs(local - solar) / solar < 1e-6,
            $"local frame gave {local} m/s², solar frame gave {solar} m/s²");
        Assert.True(Math.Abs(local - 9.82) < 0.01, $"Earth's surface gravity came out as {local} m/s²");
    }

    [Fact]
    public void LocalFrame_ReachIsBoundedByTheSunsGMInSI()
    {
        // A Q64.64 value tops out at 9.2 × 10¹⁸, and the Sun's GM in SI is 1.3 × 10²⁰ — so
        // the local frame cannot hold a solar gravitational parameter at all. That is not a
        // defect to work around: it is what "local" means. Heliocentric work belongs to the
        // solar frame, in kilometres, where the same constant is 1.3 × 10¹¹.
        const double sunGmSi = 1.32712440018e20;

        Assert.Throws<OverflowException>(() => Fix128.FromDouble(sunGmSi));

        // Earth's does fit, with room to spare.
        Fix128 earthGm = Fix128.FromDouble(3.986004418e14);
        Assert.True(earthGm.ToDouble() > 0);
    }

    [Fact]
    public void ACircularOrbit_HoldsItsRadius()
    {
        // Put the ship in a circular orbit and let it run a full revolution at the
        // navigation tick. The radius must not drift, which is the local frame's equivalent
        // of the solar frame's orbit tests.
        const double radiusKm = 7000.0;
        double radiusMetres = radiusKm * 1000.0;

        double gmSi = 3.986004418e14;
        double circularSpeed = Math.Sqrt(gmSi / radiusMetres);

        var ship = new Ship(
            new Fix128Vec(F(radiusMetres), Fix128.Zero, Fix128.Zero),
            new Fix128Vec(Fix128.Zero, F(circularSpeed), Fix128.Zero),
            F(10_000),
            F(0),
            Engine.Crewed(F(100), F(900)));

        var sources = new[] { Earth() };
        double period = 2.0 * Math.PI * Math.Sqrt(Math.Pow(radiusMetres, 3) / gmSi);
        int ticks = (int)Math.Round(period / TickSeconds);

        double minRadius = double.MaxValue;
        double maxRadius = 0;
        for (int i = 0; i < ticks; i++)
        {
            ship.Step(sources, F(TickSeconds), Command.Coast);
            double r = ship.Position.Length.ToDouble();
            minRadius = Math.Min(minRadius, r);
            maxRadius = Math.Max(maxRadius, r);
        }

        double drift = (maxRadius - minRadius) / radiusMetres;
        Assert.True(drift < 1e-5, $"radius wandered by {drift:G4} of itself over one revolution");
    }

    [Fact]
    public void TheTick_IsTheOnlyTimeStep()
    {
        // Advancing by N ticks must equal advancing by dt*N once, to within the integrator's
        // own truncation. This pins the tick as a real fixed step rather than a hint.
        var sources = new[] { Earth() };

        Ship once = MakeShip(0, 900, 10, 0);
        Ship lumped = MakeShip(0, 900, 10, 0);

        const int ticks = 600;
        for (int i = 0; i < ticks; i++)
        {
            once.Step(sources, F(TickSeconds), Command.Coast);
        }

        lumped.Step(sources, F(TickSeconds * ticks), Command.Coast);

        // One five-second step is far coarser than 600 fine ones, so they must differ: the
        // coarse step holds gravity fixed across five seconds while it in fact turns through
        // a measurable angle. The measured difference is about 5e-4 Mm over a 7000 km orbit,
        // which is the truncation error of the coarse step and nothing else.
        double difference = (once.Position - lumped.Position).Length.ToDouble();
        Assert.True(difference > 0, "the two step sizes should not agree exactly");
        Assert.True(difference < 1e3, $"a lumped step differed by {difference} m");

        // And the fine integration is the one closer to the truth: at this radius the true
        // motion is curved, so the coarse step, which assumes a straight chord, overshoots.
        Assert.True(
            once.Position.Length.ToDouble() < 7_000_000.0,
            "a ship with no thrust must fall, not rise");
    }
}
