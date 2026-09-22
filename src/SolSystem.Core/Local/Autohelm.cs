using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;

namespace SolSystem.Core.Local;

/// <summary>
/// The flight computer: flies a chosen trajectory without the pilot's hands on the controls.
/// </summary>
/// <remarks>
/// <para>
/// This is not a second physics model and it does not pretend to plan. The plan comes from
/// <see cref="FlightPlan"/>, which quotes a time and a fuel cost; what this does is fly it — point
/// the ship, burn, turn over at the midpoint, and shut down on arrival.
/// </para>
/// <para>
/// <b>What it flies is a torch crossing and nothing else.</b> A ballistic transfer would mean
/// matching a launch window and timing two impulses, which is a different and much longer piece of
/// work; the honest thing is to say so rather than to fly a ballistic plan badly. Selecting a
/// ballistic option hands the ship a heading and leaves the pilot to the window.
/// </para>
/// <para>
/// <b>The reversal is the whole difficulty.</b> A torch crossing is accelerate–flip–decelerate, and
/// the flip takes thirty seconds during which the engine points the wrong way and the ship coasts.
/// The computer therefore has to start braking early enough to pay for its own turn — the same
/// problem the docking law has, at a scale of millions of kilometres instead of hundreds of metres.
/// Braking from <c>v</c> needs <c>v²/2a + v·t_turn</c>, and at the speeds a torch reaches that second
/// term is enormous: at a peak of 52 km/s a thirty-second turn is 1 560 km of coasting.
/// </para>
/// </remarks>
internal struct Autohelm
{
    /// <summary>What the computer is doing, for the display.</summary>
    internal enum Phase
    {
        /// <summary>Nothing selected.</summary>
        Idle,

        /// <summary>Pointing at the destination before lighting the engine.</summary>
        Aligning,

        /// <summary>Burning towards the destination.</summary>
        Accelerating,

        /// <summary>Coming about, engine off.</summary>
        Reversing,

        /// <summary>Burning to shed speed.</summary>
        Decelerating,

        /// <summary>Arrived and holding.</summary>
        Arrived,
    }

    /// <summary>Where the destination is, heliocentric, in kilometres.</summary>
    internal Fix128Vec Target;

    /// <summary>The phase the computer is in.</summary>
    internal Phase Stage;

    /// <summary>How close counts as arrived, in kilometres.</summary>
    internal Fix128 ArrivalRadius;

    /// <summary>Throttle to use, 0 to 1. The economy option is this at a quarter.</summary>
    internal Fix128 Throttle;

    /// <summary>Seconds spent in the current run, for the display.</summary>
    internal Fix128 ElapsedSeconds;

    /// <summary>
    /// How many seconds the helm needs to come about.
    /// </summary>
    /// <remarks>
    /// A reversal is half a turn, so it is <b>pi</b> over the maximum turn rate — thirty seconds at
    /// six degrees a second. Not two pi, which is a full circle and would have the computer budgeting
    /// a minute for a manoeuvre that takes half of one. Precomputed once: it is a constant wearing
    /// a property's clothes, and evaluating it per tick was a conversion per tick.
    /// </remarks>
    internal static readonly Fix128 ReversalSeconds =
        Fix128.FromDouble(Math.PI) / Attitude.CrewedMaxTurnRate;

    /// <summary>Starts a crossing to a target at a given throttle.</summary>
    internal static Autohelm To(Fix128Vec target, Fix128 arrivalRadius, Fix128 throttle) => new()
    {
        Target = target,
        ArrivalRadius = arrivalRadius,
        Throttle = throttle,
        Stage = Phase.Aligning,
    };

    /// <summary>Whether the computer has the controls.</summary>
    internal readonly bool Engaged => Stage != Phase.Idle;

    /// <summary>
    /// How far the ship still has to slow down, in metres — the local frame's own unit.
    /// </summary>
    /// <remarks>
    /// The distance a reversal-and-brake actually needs, which is the thing the computer has to get
    /// right: <c>v²/2a</c> to stop, plus <c>v·t_turn</c> for the coast during the turn. Ignoring the
    /// second term is how a torch ship arrives at a planet still doing fifty kilometres a second.
    /// </remarks>
    internal static Fix128 BrakingDistance(Fix128 speed, Fix128 acceleration, Fix128 turnSeconds)
    {
        if (acceleration <= Fix128.Zero)
        {
            return Fix128.Zero;
        }

        return (speed * speed / (acceleration * Fix128.FromWhole(2)))
            + (speed * turnSeconds);
    }

    /// <summary>
    /// Flies one tick.
    /// </summary>
    /// <param name="ship">The ship being flown.</param>
    /// <param name="sources">Gravity acting on it.</param>
    /// <param name="seconds">The tick.</param>
    internal void Step(ref Ship ship, ReadOnlySpan<GravitySource> sources, Fix128 seconds)
    {
        if (!Engaged)
        {
            return;
        }

        ElapsedSeconds += seconds;

        Fix128Vec toTarget = Target - ship.Position;
        Fix128 range = toTarget.Length;
        Fix128 speed = ship.Velocity.Length;

        Fix128 acceleration = ship.Engine.MaxAccelerationInMetresPerSecondSquared * Throttle;
        if (acceleration <= Fix128.Zero)
        {
            // Planning authority, not command authority: the phases below budget braking
            // distances, and the engine's ceiling is the figure to budget against. The corner
            // it guards is an engagement at throttle zero, which no caller makes — the client
            // engages at full or at the economy quarter — and without it the braking
            // distance is exactly zero, which wedges the phase machine in Accelerating
            // forever. With it the machine progresses. The burn gate still commands the
            // engagement's own throttle, so such a ship would coast through its target with
            // the engine dark; the fallback keeps the plan moving, it does not make the
            // engagement sane.
            acceleration = ship.Engine.MaxAccelerationInMetresPerSecondSquared;
        }

        switch (Stage)
        {
            case Phase.Aligning:
                // Point at the destination, and do not light the engine until the nose is there. A
                // burn commanded while the ship is still coming about goes mostly sideways, which is
                // the same mistake the docking approach made and spends fuel to no purpose.
                if (IsAligned(ship, toTarget, AlignTolerance))
                {
                    Stage = Phase.Accelerating;
                }

                break;

            case Phase.Accelerating:
                // Turn over when the remaining distance is what stopping needs. The check is against
                // the BRAKING distance rather than against half the range, because a crossing at
                // constant thrust would otherwise turn over at the midpoint and then arrive at
                // exactly the speed the second half could not shed.
                if (range <= BrakingDistance(speed, acceleration, ReversalSeconds))
                {
                    Stage = Phase.Reversing;
                }

                break;

            case Phase.Reversing:
                if (IsAligned(ship, -toTarget, AlignTolerance))
                {
                    Stage = Phase.Decelerating;
                }

                break;

            case Phase.Decelerating:
                if (speed <= acceleration * seconds * Fix128.FromWhole(4) || range <= ArrivalRadius)
                {
                    Stage = Phase.Arrived;
                }

                break;

            case Phase.Arrived:
                return;
        }

        // Which way to point, and whether to burn at all.
        Fix128Vec wanted = Stage switch
        {
            Phase.Aligning or Phase.Accelerating => toTarget,
            Phase.Reversing or Phase.Decelerating => Fix128Vec.Zero - toTarget,
            _ => Fix128Vec.Zero,
        };

        Fix128Vec turn = SteerTowards(ship.Attitude, wanted);

        bool burning = Stage is Phase.Accelerating or Phase.Decelerating;

        // The engine fires along the nose, so a burn is only useful once the nose is roughly there.
        // The gate follows how much is being asked for, the same way the docking law's does.
        Fix128 throttle = Fix128.Zero;
        if (burning && IsAligned(ship, wanted, BurnTolerance))
        {
            throttle = Throttle;
        }

        var command = new Command(wanted, throttle, turn);
        ship.Step(sources, seconds, command);
    }

    /// <summary>Whether the nose is within a tolerance of a direction.</summary>
    private static bool IsAligned(in Ship ship, Fix128Vec direction, Fix128 tolerance)
    {
        if (direction.IsZero)
        {
            return true;
        }

        Fix128Vec unit = direction.Normalized();
        Fix128Vec nose = ship.Attitude.Forward;

        Fix128 dot = (nose.X * unit.X) + (nose.Y * unit.Y) + (nose.Z * unit.Z);
        return dot >= (Fix128.One - tolerance);
    }

    /// <summary>
    /// A turn command that brings the nose onto a direction, without overshooting it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Proportional only, with the rate damped: the same shape as the docking helm, because the
    /// problem is the same one and the actuator is the same rate-limited one. An integrator here
    /// would wind up during the long coast of a reversal and then hold the ship off the axis.
    /// </para>
    /// <para>
    /// <b>A reversal has to be handled separately and this is the case the whole autopilot turns
    /// on.</b> A torch crossing is accelerate–flip–decelerate, and the flip is a half turn — so
    /// <c>cross(nose, wanted)</c> is very nearly zero at the moment it is needed most, and exactly
    /// zero when the ship is aimed straight at its target. Returning a zero command there is a ship
    /// that never turns round: it coasts to the destination nose-first at full speed, which is what
    /// the first version did, arriving at three point nine kilometres a second and calling it an
    /// arrival. The docking helm has the same guard for the same reason.
    /// </para>
    /// </remarks>
    private static Fix128Vec SteerTowards(in Attitude attitude, Fix128Vec direction)
    {
        if (direction.IsZero)
        {
            return Fix128Vec.Zero;
        }

        Fix128Vec wanted = direction.Normalized();
        Fix128Vec nose = attitude.Forward;

        Fix128Vec axis = Fix128Vec.Cross(nose, wanted);
        Fix128 alignment = (nose.X * wanted.X) + (nose.Y * wanted.Y) + (nose.Z * wanted.Z);

        if (axis.Length < Tiny)
        {
            if (alignment > Fix128.Zero)
            {
                return Fix128Vec.Zero;
            }

            // Exactly antiparallel: there is no axis the cross product can give, so one is chosen.
            // Any perpendicular will do, and the deck is the one that keeps the ship the right way
            // up through the turn.
            Fix128Vec deck = attitude.Rotate(new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.One));
            axis = Fix128Vec.Cross(deck, nose);

            if (axis.Length < Tiny)
            {
                // The nose is along the deck, which a ship's never is, but a hull at exactly that
                // attitude would otherwise be un-turnable. Any axis at all is better than none.
                axis = Fix128Vec.Cross(new Fix128Vec(Fix128.Zero, Fix128.One, Fix128.Zero), nose);
            }
        }

        // Scale by the angle still to go, so the command dies away as the nose arrives rather than
        // holding full rate until it is on top of the target and then hunting. The dot of two
        // normalized vectors can round a hair past ±1, which the arccosine refuses — so it is
        // clamped first, cheaply, rather than trusted.
        //
        // Error below Tiny cannot reach this line. The cross axis is sin(error) for unit vectors,
        // so a vanishing error arrives at the degeneracy branch above with a positive alignment
        // and leaves through its zero command. The first version carried an override that
        // substituted a half turn for a vanishing error — a nose dead on target ordered to spin
        // — and it was unreachable for exactly that reason; it is removed rather than explained.
        Fix128 error = Fix128.Acos(Fix128.Clamp(alignment, -Fix128.One, Fix128.One));

        Fix128Vec command = axis.Normalized() * error;

        // Damping, against the rate the ship already has.
        return command - (attitude.AngularVelocity * SteerDamping);
    }

    /// <summary>Sizes below which a vector or an angle is noise for steering purposes.</summary>
    private static readonly Fix128 Tiny = Fix128.FromDouble(1e-6);

    /// <summary>The helm's damping on the ship's existing rate.</summary>
    private static readonly Fix128 SteerDamping = Fix128.FromDouble(0.4);

    /// <summary>Dot-product tolerance for "the nose is there" in the alignment phases.</summary>
    private static readonly Fix128 AlignTolerance = Fix128.FromDouble(0.02);

    /// <summary>Dot-product tolerance for lighting the engine at all.</summary>
    private static readonly Fix128 BurnTolerance = Fix128.FromDouble(0.15);

    /// <summary>A one-word description, for the heads-up display.</summary>
    internal readonly string Describe() => Stage switch
    {
        Phase.Aligning => "ALIGNING",
        Phase.Accelerating => "BURNING",
        Phase.Reversing => "REVERSING",
        Phase.Decelerating => "BRAKING",
        Phase.Arrived => "ARRIVED",
        _ => "MANUAL",
    };
}
