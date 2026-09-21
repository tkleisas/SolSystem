using SolSystem.Core.Local;
using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;
using Xunit;

namespace SolSystem.Core.Tests;

/// <summary>
/// Attitude and docking: the first thing in the project whose success is not a number but a
/// manoeuvre.
/// </summary>
/// <remarks>
/// The tests here drive the ship through a real approach and assert that it arrives, which
/// is the closest a test can get to answering the Phase 0 gate — "is flying this ship fun" —
/// without a human at the controls.
/// </remarks>
public class DockingTests
{
    internal const double TickSeconds = 1.0 / 120.0;

    private static Fix128 F(double value) => Fix128.FromDouble(value);
    private static Fix128Vec V(double x, double y, double z) => new(F(x), F(y), F(z));
    private static readonly Fix128 Pi = Fix128.FromDouble(Math.PI);

    /// <summary>A crewed ship at a position and velocity, nose along <paramref name="nose"/>.</summary>
    private static Ship MakeShip(Fix128Vec position, Fix128Vec velocity, Fix128Vec nose)
    {
        // The nose direction arrives as a rotation vector: for a rotation about z, the angle
        // is the arctangent of the y component over the x component.
        Fix128 angle = Fix128.FromDouble(Math.Atan2(nose.Y.ToDouble(), nose.X.ToDouble()));
        var attitude = new Attitude(V(0, 0, angle.ToDouble()), Fix128Vec.Zero);

        // The propellant load is solved, not chosen: 90 t dry and 98 kN at exactly 0.1 g — the
        // bottom of the crewed band the design sets — means 9.9322 t of it. A round 10 t would
        // put the ship at 0.099 g and quietly break that invariant in every test that flies it,
        // which is exactly the kind of drift the mass assertions exist to catch.
        return new Ship(
            position,
            velocity,
            F(90.0),
            F(9.9322),
            Engine.Crewed(F(98.0), F(900.0)),
            attitude);
    }

    /// <summary>
    /// Angular velocity that swings the ship's attitude until its nose points along
    /// <paramref name="to"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The command is the difference between the attitude the ship has and the attitude it
    /// wants, divided by a time constant, which <see cref="Attitude.ClampAngularVelocity"/> then
    /// holds inside what a crew can take. This is the same shape as the throttle: ask for the
    /// whole error, let the limiter decide the rate.
    /// </para>
    /// <para>
    /// It is written this way because the obvious alternative — take the angle between the nose
    /// and the target, and rotate about their cross product — has a degenerate case that is not
    /// merely awkward but wrong, and it is the single most common manoeuvre in docking. Two
    /// anti-parallel vectors have a zero cross product, so a ship ordered to reverse is told not
    /// to turn and coasts helplessly past its target. Any perpendicular axis turns the nose
    /// through the same angle, but the fold in <see cref="Attitude.Step"/> — which rewrites a
    /// rotation vector past pi back to pi — punishes the wrong choice by turning the reversal
    /// into a full circle back to where it started. Working from the attitude itself has no
    /// anti-parallel case to special-case at all: a half turn is just the rotation vector that
    /// points the other way, and the subtraction does the rest.
    /// </para>
    /// </remarks>
    private static Fix128Vec TurnTowards(Attitude attitude, Fix128Vec to)
    {
        Fix128Vec wanted = AttitudeFor(to);
        Fix128Vec error = wanted - attitude.RotationVector;

        // A half-turn's worth of error asks for the ship's full turn rate; anything less asks
        // for proportionally less, so the nose settles rather than hunting.
        return error * (Attitude.CrewedMaxTurnRate / HalfTurn);
    }

    /// <summary>
    /// A rotation vector that points the nose along <paramref name="direction"/>, taking the
    /// shortest way from straight ahead.
    /// </summary>
    /// <remarks>
    /// Built from the forward vector's angle in the xy plane, which is all the docking corridor
    /// needs: the corridor runs along x and the ship is expected to arrive in the plane of the
    /// axis. Rodrigues' formula takes it from there, so the vector need not be small.
    /// </remarks>
    private static Fix128Vec AttitudeFor(Fix128Vec direction)
    {
        Fix128Vec unit = direction.Normalized();
        if (unit.IsZero)
        {
            return Fix128Vec.Zero;
        }

        double x = unit.X.ToDouble();
        double y = unit.Y.ToDouble();
        double z = unit.Z.ToDouble();

        return V(0, 0, Math.Atan2(y, x));
    }

    private static readonly Fix128 HalfTurn = Fix128.FromDouble(Math.PI);

    // ------------------------------------------------------------------ attitude

    [Fact]
    public void ARotationVector_TurnsTheNose()
    {
        // A quarter turn about z takes the nose from +x to +y.
        var attitude = new Attitude(V(0, 0, Math.PI / 2), Fix128Vec.Zero);
        Fix128Vec forward = attitude.Forward;

        Assert.Equal(0.0, forward.X.ToDouble(), 9);
        Assert.Equal(1.0, forward.Y.ToDouble(), 9);
    }

    [Fact]
    public void Rotation_PreservesLength()
    {
        var attitude = new Attitude(V(0.3, -0.4, 0.9), Fix128Vec.Zero);
        Fix128Vec forward = attitude.Forward;

        Assert.Equal(1.0, forward.Length.ToDouble(), 9);
    }

    [Fact]
    public void TurnRate_IsClampedToWhatACrewCanTake()
    {
        // One second of 6 degrees per second is 6 degrees of rotation.
        var attitude = new Attitude(Fix128Vec.Zero, V(0, 0, 6.0 * Math.PI / 180.0));
        for (int i = 0; i < 120; i++)
        {
            attitude.Step(F(TickSeconds));
        }

        double angle = attitude.RotationVector.Length.ToDouble();
        Assert.Equal(6.0 * Math.PI / 180.0, angle, 6);
    }

    [Fact]
    public void AnOverFastTurn_IsClampedRatherThanObeyed()
    {
        var attitude = new Attitude(Fix128Vec.Zero, V(0, 0, 100.0));
        attitude.AngularVelocity = Attitude.ClampAngularVelocity(
            attitude.AngularVelocity, Attitude.CrewedMaxTurnRate);

        Assert.True(
            attitude.AngularVelocity.Length <= Attitude.CrewedMaxTurnRate,
            "a hundred radians per second is not something a crew can work through");
    }

    // ------------------------------------------------------------------ docking conditions

    [Fact]
    public void APerfectApproach_Docks()
    {
        // The corridor runs along +x away from the port, so the ship sits at +x, closes by
        // travelling in -x, and arrives nose-first pointing -x. Inside the contact range, not
        // merely inside the capture range: a ship two metres out is within every tolerance and is
        // on the doorstep.
        Ship ship = MakeShip(V(0.2, 0, 0), V(-0.1, 0, 0), V(-1, 0, 0));
        DockingReport report = Docking.Evaluate(ship, DockingPort.AtOrigin, Fix128Vec.Zero);

        Assert.True(report.Contact, $"{report.Reason} | r={report.Range.ToDouble():F4} "
            + $"lat={report.LateralOffset.ToDouble():F4} close={report.ClosingSpeed.ToDouble():F4} "
            + $"mis={report.Misalignment.ToDouble() * 180 / Math.PI:F2} deg");
        Assert.True(report.LateralOffset.ToDouble() < 1e-9);

        // Positive means approaching. The sign was inverted here for a long time, and the
        // whole docking law was built on top of it before anybody noticed: a guidance law that
        // brakes correctly, arrives in the envelope, and is then told by the envelope that it
        // is leaving.
        Assert.Equal(0.1, report.ClosingSpeed.ToDouble(), 9);
    }

    [Fact]
    public void BeingTooFarAway_IsNotADocking()
    {
        Ship ship = MakeShip(V(50, 0, 0), V(-0.1, 0, 0), V(-1, 0, 0));
        DockingReport report = Docking.Evaluate(ship, DockingPort.AtOrigin, Fix128Vec.Zero);

        Assert.False(report.Contact);
        Assert.Equal("out of range", report.Reason);
    }

    [Fact]
    public void ArrivingOffAxis_IsACollisionNotADocking()
    {
        // Close and slow, but 1.5 m to one side: inside capture range, outside the corridor.
        Ship ship = MakeShip(V(0.5, 1.5, 0), V(-0.1, 0, 0), V(-1, 0, 0));
        DockingReport report = Docking.Evaluate(ship, DockingPort.AtOrigin, Fix128Vec.Zero);

        Assert.False(report.Contact);
        Assert.Equal("off the port axis", report.Reason);
    }

    [Fact]
    public void ArrivingTooFast_IsNotADocking()
    {
        Ship ship = MakeShip(V(1.5, 0, 0), V(-5.0, 0, 0), V(-1, 0, 0));
        DockingReport report = Docking.Evaluate(ship, DockingPort.AtOrigin, Fix128Vec.Zero);

        Assert.False(report.Contact);
        Assert.Equal("closing too fast", report.Reason);
    }

    [Fact]
    public void ArrivingSideways_IsNotADocking()
    {
        // Position and speed are perfect, but the ship is broadside.
        Ship ship = MakeShip(V(1.5, 0, 0), V(-0.1, 0, 0), V(0, 1, 0));
        DockingReport report = Docking.Evaluate(ship, DockingPort.AtOrigin, Fix128Vec.Zero);

        Assert.False(report.Contact);
        Assert.Equal("not aligned with the port", report.Reason);
    }

    [Fact]
    public void MovingAway_IsNotADocking()
    {
        // The one place the sign of the corridor matters most: a ship at +x travelling +x is
        // leaving, and reading the port's axis as its outward facing would call this a docking.
        Ship ship = MakeShip(V(1.5, 0, 0), V(0.1, 0, 0), V(1, 0, 0));
        DockingReport report = Docking.Evaluate(ship, DockingPort.AtOrigin, Fix128Vec.Zero);

        Assert.False(report.Contact);
        Assert.Equal("moving away", report.Reason);

        // And the sign itself, so a future inversion is caught here rather than by a law that
        // mysteriously cannot arrive.
        Assert.True(report.ClosingSpeed.ToDouble() < 0.0,
            $"retreating should read negative, got {report.ClosingSpeed.ToDouble()}");
    }

    [Fact]
    public void TheMisalignmentAngle_IsMeasuredCorrectly()
    {
        Ship aligned = MakeShip(V(1.5, 0, 0), V(-0.1, 0, 0), V(-1, 0, 0));
        Ship reversed = MakeShip(V(1.5, 0, 0), V(-0.1, 0, 0), V(1, 0, 0));
        Ship sideways = MakeShip(V(1.5, 0, 0), V(-0.1, 0, 0), V(0, 1, 0));

        Assert.True(Docking.Evaluate(aligned, DockingPort.AtOrigin, Fix128Vec.Zero)
            .Misalignment.ToDouble() < 1e-9);

        Assert.Equal(Math.PI, Docking.Evaluate(reversed, DockingPort.AtOrigin, Fix128Vec.Zero)
            .Misalignment.ToDouble(), 6);

        Assert.Equal(Math.PI / 2, Docking.Evaluate(sideways, DockingPort.AtOrigin, Fix128Vec.Zero)
            .Misalignment.ToDouble(), 6);
    }

    // ------------------------------------------------------------------ flying it

    /// <summary>
    /// The last few metres, flown in: the ship closes on the port, spends propellant doing it,
    /// and is captured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the terminal phase of an approach, and it is the part worth asserting now. The
    /// whole sequence from a kilometre out — close, come about, burn the speed off, creep — is a
    /// guidance problem rather than a physics one, and the guidance law is still being designed;
    /// what is decided is the ship, its attitude, its mass flow, and the capture envelope, and
    /// this exercises all four together.
    /// </para>
    /// <para>
    /// The reversal that dominates a long approach is asserted separately, in
    /// <see cref="ARotationVector_TurnsTheNose"/> and the turn-rate tests: a crewed hull needs
    /// half a minute to come about, which at flight speed is a hundred metres or more of
    /// coasting before the engine points the right way. That cost is the reason docking is a
    /// manoeuvre rather than a steering problem, and it belongs in the design rather than in a
    /// test that would be measuring a controller nobody has written yet.
    /// </para>
    /// </remarks>
    [Fact]
    public void ClosingTheLastMetres_Docks()
    {
        // Drifting in slower than the corridor asks for, so the pilot has to open the throttle
        // to reach the closing speed — a ship that already happens to be at the right speed is
        // captured without the engine doing anything, and the propellant assertion would then be
        // measuring the setup rather than the manoeuvre.
        Ship ship = MakeShip(V(8.0, 0, 0), V(-0.1, 0, 0), V(-1, 0, 0));
        DockingPort port = DockingPort.AtOrigin;
        var sources = new[] { new GravitySource(V(-10_000, 0, 0), Fix128.Zero) };

        Fix128 start = ship.Propellant;
        double closest = double.MaxValue;
        bool docked = false;

        for (int tick = 0; tick < 120 * 60 && !docked; tick++)
        {
            // Point the nose down the corridor and hold a gentle closing speed. The attitude is
            // commanded whether or not it needs to change, so the test exercises the helm.
            Fix128Vec inward = -port.Axis;
            Fix128Vec turn = TurnTowards(ship.Attitude, inward);
            double alignment = Dot(ship.Attitude.Forward, inward).ToDouble();
            Fix128 throttle = alignment > 0.999
                ? Fix128.FromDouble(Math.Clamp((0.3 - Dot(ship.Velocity, inward).ToDouble()) / 0.05, 0.0, 1.0))
                : Fix128.Zero;

            ship.Step(sources, F(TickSeconds), new Command(inward, throttle, turn));

            DockingReport report = Docking.Evaluate(ship, port, Fix128Vec.Zero);
            closest = Math.Min(closest, report.Range.ToDouble());
            docked = report.Docked;
        }

        Assert.True(
            docked,
            $"the ship never docked; closest approach was {closest:F3} m at {ship.Velocity.Length.ToDouble():F3} m/s");
        Assert.True(start > ship.Propellant, "a manoeuvre that changes velocity has to cost propellant");
    }

    [Fact]
    public void AFullThrottleBurn_SpendsPropellantAtTheMassFlowRate()
    {
        Ship ship = MakeShip(V(1000, 0, 0), Fix128Vec.Zero, V(1, 0, 0));
        var sources = new[] { GravitySource.AtOrigin(Fix128.Zero) };
        var burn = new Command(V(1, 0, 0), Fix128.One, Fix128Vec.Zero);

        Fix128 start = ship.Propellant;
        const int ticks = 120;
        for (int i = 0; i < ticks; i++)
        {
            ship.Step(sources, F(TickSeconds), burn);
        }

        double expected = ship.Engine.MassFlowTonnesPerSecond.ToDouble() * ticks * TickSeconds;
        double actual = (start - ship.Propellant).ToDouble();
        Assert.True(
            Math.Abs(actual - expected) / expected < 1e-9,
            $"burned {actual:F6} t in {ticks} ticks, mass flow says {expected:F6} t");
    }

    [Fact]
    public void Coasting_BurnsNothing()
    {
        Ship ship = MakeShip(V(1000, 0, 0), V(1, 0, 0), V(1, 0, 0));
        var sources = new[] { GravitySource.AtOrigin(Fix128.Zero) };

        Fix128 start = ship.Propellant;
        for (int i = 0; i < 1200; i++)
        {
            ship.Step(sources, F(TickSeconds), Command.Coast);
        }

        Assert.Equal(start.ToDouble(), ship.Propellant.ToDouble(), 12);
    }

    private static Fix128 Dot(Fix128Vec a, Fix128Vec b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    /// <summary>
    /// A ship can turn a half turn and finish it.
    /// </summary>
    /// <remarks>
    /// The clamp in <c>Attitude.Step</c> folds a rotation vector past pi back to pi so the
    /// axis-angle pair stays unique. It used to fold only strictly past, which makes pi a
    /// fixed point: a ship that has come exactly half way round and is still being told to
    /// keep going wants 2pi on the next step, the fold rewrites that as pi, and the ship
    /// tumbles on the spot forever. The nose sits at exactly -x with the rotation command
    /// still lit and never moves again — which is a perfectly silent failure, and it cost a
    /// great deal of time to find from a pilot that simply never arrived.
    /// </remarks>
    [Fact]
    public void AHalfTurn_Completes()
    {
        var attitude = new Attitude(Fix128Vec.Zero, Fix128Vec.Zero);

        // Command the shortest turn that ends at half a turn from the start.
        var commanded = new Fix128Vec(Fix128.Zero, Fix128.Zero, Pi);
        double previous = 0.0;

        for (int tick = 0; tick < 120 * 60; tick++)
        {
            attitude.AngularVelocity = Attitude.ClampAngularVelocity(
                commanded, Attitude.CrewedMaxTurnRate);
            attitude.Step(F(TickSeconds));

            double angle = attitude.RotationVector.Length.ToDouble();
            Assert.True(angle >= previous - 1e-9, $"the rotation went backwards at tick {tick}");
            previous = angle;

            if (tick > 120 && attitude.Forward.X.ToDouble() < -0.99)
            {
                // It got there. The nose is along -x and the rotation is at the limit, which
                // is the state that used to be terminal.
                Assert.True(angle > 3.0, $"arrived with the rotation at {angle:F6}");
                return;
            }
        }

        Assert.Fail($"the ship never completed a half turn; nose at {attitude.Forward.X.ToDouble():F6}, "
            + $"rotation {attitude.RotationVector.Length.ToDouble():F6}");
    }
    [Fact]
    public void ARotationIsComposed_NotAdded()
    {
        // The bug this exists for: `RotationVector += omega*dt` is exact only to first order, and the
        // error is the Baker-Campbell-Hausdorff commutator term, of order |delta|*|v|. It is
        // negligible while the accumulated rotation is small -- which is why it survived every
        // docking test, because a ship on final approach barely rotates -- and it is catastrophic at
        // pi, which is exactly where a ship that has turned to face its port is sitting.
        //
        // At |v| = pi and a full-rate tick of six degrees, the error is nine degrees, about an axis
        // with nothing to do with the one commanded. Rolling the ship about its own nose moved the
        // nose instead.
        //
        // THE NOSE CANNOT SHOW THIS. At a rotation of exactly pi about any axis perpendicular to it,
        // +x goes to -x whatever the axis is, so the nose sits still under both the right answer and
        // the wrong one. The DECK is the vector that carries the information, and it is the one the
        // first version of this test did not look at.
        var attitude = new Attitude(
            new Fix128Vec(Fix128.Zero, Fix128.Zero, F(Math.PI)), Fix128Vec.Zero);

        double step = 6.0 * Math.PI / 180.0;

        // Command a rotation about world x for one second at that rate. The ship's nose is along
        // -x, so this is a rotation about its own long axis: a ROLL.
        attitude.AngularVelocity = new Fix128Vec(F(step), Fix128.Zero, Fix128.Zero);
        attitude.Step(Fix128.One);

        Fix128Vec nose = attitude.Forward;
        Fix128Vec deck = attitude.Rotate(new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.One));

        // The nose is unmoved, because a roll does not move the nose. Under addition it would have
        // swung 2*step/pi towards +x, which is a quarter of the commanded angle about an axis at
        // right angles to the one asked for.
        Assert.True(Math.Abs(nose.X.ToDouble() + 1.0) < 1e-6,
            $"a roll must leave the nose alone; it is at ({nose.X.ToDouble():F6},"
            + $"{nose.Y.ToDouble():F6},{nose.Z.ToDouble():F6})");

        // The deck carries the roll: it tips towards -y by sin(step).
        double composed = Math.Sin(step);
        Assert.True(Math.Abs(deck.Y.ToDouble() + composed) < 1e-6,
            $"the deck should tip {composed:F6} towards -y and it is at {deck.Y.ToDouble():F6}");

        // And the rotation is still a rotation.
        Assert.Equal(1.0, nose.Length.ToDouble(), 9);
        Assert.Equal(1.0, deck.Length.ToDouble(), 9);
    }

}

/// <summary>Test-only helpers on <see cref="Ship"/>.</summary>
internal static class ShipTestExtensions
{
    internal static double RangeToOrigin(this Ship ship) => ship.Position.Length.ToDouble();

}
