using SolSystem.Core.Numerics;

namespace SolSystem.Core.Local;

/// <summary>What the pilot is asking the ship to do this tick.</summary>
internal readonly struct Command
{
    /// <summary>Thrust direction, in the local frame. Normalised by <see cref="Ship.Step"/>.</summary>
    internal readonly Fix128Vec ThrustDirection;

    /// <summary>Throttle, 0 to 1. Values above 1 are clamped.</summary>
    internal readonly Fix128 Throttle;

    /// <summary>
    /// Requested angular velocity, radians per second about each axis.
    /// </summary>
    /// <remarks>
    /// Clamped by the hull's turn-rate ceiling. Turning costs almost no energy — a hundred
    /// tonne hull needs 10² J to spin up — so this is limited by what a crew can work
    /// through, not by propellant.
    /// </remarks>
    internal readonly Fix128Vec AngularVelocity;

    internal Command(Fix128Vec thrustDirection, Fix128 throttle, Fix128Vec angularVelocity)
    {
        ThrustDirection = thrustDirection;
        Throttle = throttle;
        AngularVelocity = angularVelocity;
    }

    /// <summary>Engines off.</summary>
    internal static readonly Command Coast = new(Fix128Vec.Zero, Fix128.Zero, Fix128Vec.Zero);

    /// <summary>Any direction with the given throttle.</summary>
    internal static Command WithThrottle(Fix128 throttle) =>
        new(new Fix128Vec(Fix128.One, Fix128.Zero, Fix128.Zero), throttle, Fix128Vec.Zero);
}

/// <summary>
/// A ship in the local frame: metres, metres per second, tonnes, all in Q64.64.
/// </summary>
/// <remarks>
/// The width is Q64.64 rather than Q32.32 because the local frame needs to hold both a
/// position out to millions of kilometres and an acceleration of a few m/s² with enough
/// resolution left to integrate it at 120 Hz. See <see cref="GravitySource.AccelerationAt"/>
/// for the measurements behind that.
/// </remarks>
/// <remarks>
/// <para>
/// Propellant is finite and the mass falls as it burns, so acceleration rises through a
/// burn. That coupling is the whole reason to model mass at all: it is what makes a long
/// burn behave differently from a short one, and it is what gives the design's delta-v
/// currency something real to be a currency of.
/// </para>
/// <para>
/// <b>Delta-v is a derived quantity, not a stored one.</b> It is
/// <c>vₑ · ln(mass / dryMass)</c> and is recomputed on demand, so it can never disagree
/// with the propellant actually in the tanks.
/// </para>
/// </remarks>
internal struct Ship
{
    /// <summary>Position in the local frame, metres.</summary>
    internal Fix128Vec Position;

    /// <summary>Velocity, metres per second.</summary>
    internal Fix128Vec Velocity;

    /// <summary>Current mass including propellant, tonnes.</summary>
    internal Fix128 Mass;

    /// <summary>Propellant remaining, tonnes.</summary>
    internal Fix128 Propellant;

    /// <summary>The engine.</summary>
    internal Engine Engine;

    /// <summary>
    /// Orientation and rotation.
    /// </summary>
    /// <remarks>
    /// The main engine pushes along the nose, so <b>thrust follows attitude</b>: to brake
    /// the ship must turn around, and while turned it cannot correct laterally. That is the
    /// whole difficulty of docking, and it is why attitude is simulated rather than assumed.
    /// </remarks>
    internal Attitude Attitude;

    internal Ship(Fix128Vec position, Fix128Vec velocity, Fix128 dryMass, Fix128 propellant, Engine engine)
        : this(position, velocity, dryMass, propellant, engine,
               new Attitude(Fix128Vec.Zero, Fix128Vec.Zero))
    {
    }

    internal Ship(
        Fix128Vec position,
        Fix128Vec velocity,
        Fix128 dryMass,
        Fix128 propellant,
        Engine engine,
        Attitude attitude)
    {
        Position = position;
        Velocity = velocity;
        Propellant = propellant;
        Mass = dryMass + propellant;
        Engine = engine;
        Attitude = attitude;
    }

    /// <summary>Mass with the tanks empty, tonnes.</summary>
    internal readonly Fix128 DryMass => Mass - Propellant;

    /// <summary>Propellant as a fraction of the current mass, 0 to 1.</summary>
    internal readonly Fix128 PropellantFraction => Mass == Fix128.Zero
        ? Fix128.Zero
        : Propellant / Mass;

    /// <summary>
    /// Delta-v remaining, in m/s: <c>vₑ · ln(mass / dryMass)</c>.
    /// </summary>
    /// <remarks>
    /// The rocket equation, and the number every manoeuvre in the game is priced in. Zero
    /// when the tanks are dry, because the logarithm of one is zero.
    /// </remarks>
    internal readonly Fix128 DeltaVRemaining
    {
        get
        {
            Fix128 dryMass = DryMass;
            if (Propellant <= Fix128.Zero || dryMass <= Fix128.Zero)
            {
                return Fix128.Zero;
            }

            return Engine.ExhaustVelocityMetresPerSecond * Fix128.Log(Mass / dryMass);
        }
    }

    /// <summary>
    /// Advances the ship by one navigation tick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Velocity Verlet with a thrust term, evaluated in the order
    /// <c>(p, a₀) → p′ → (p′, a₁) → v′</c>. Holding the thrust vector fixed across the tick
    /// is exact rather than an approximation, because a constant acceleration is exactly
    /// what the Verlet position update assumes.
    /// </para>
    /// <para>
    /// The acceleration is computed at the <b>mid-tick</b> mass, since propellant is
    /// consumed uniformly. At a 120 Hz tick a full-throttle burn consumes about 10⁻⁴ of the
    /// ship's mass, so the difference from either endpoint is far below the frame's
    /// resolution — but taking the midpoint costs nothing and is the defensible choice.
    /// </para>
    /// </remarks>
    internal void Step(
        ReadOnlySpan<GravitySource> sources,
        Fix128 dt,
        in Command command)
    {
        Fix128 throttle = Clamp01(command.Throttle);
        Fix128 flow = Engine.MassFlowTonnesPerSecond * throttle;

        // Burn for this tick. Propellant is clamped at zero and the flow with it, so a tank
        // that runs dry mid-tick stops producing thrust at the right instant rather than
        // going negative.
        Fix128 consumed = flow * dt;
        if (consumed > Propellant)
        {
            consumed = Propellant;
        }

        Propellant -= consumed;
        Mass -= consumed;

        // Attitude first, so this tick's thrust uses this tick's nose direction.
        Attitude.AngularVelocity = Attitude.ClampAngularVelocity(
            command.AngularVelocity, Attitude.CrewedMaxTurnRate);
        Attitude.Step(dt);

        // The main engine fires along the NOSE. A command direction is therefore interpreted
        // as "point here and burn", which is what a pilot actually does: the throttle and the
        // helm are one control. The lateral component is discarded by the dot product, so
        // commanding a direction the ship is not yet facing simply gives no thrust.
        Fix128Vec nose = Attitude.Forward;

        Fix128Vec thrustAcceleration = Fix128Vec.Zero;
        if (consumed > Fix128.Zero
            && Mass > Fix128.Zero
            && !command.ThrustDirection.Equals(Fix128Vec.Zero))
        {
            Fix128Vec desired = command.ThrustDirection.Normalized();

            // Only the component along the nose produces thrust. Commanding a direction the
            // ship is not yet facing gives a reduced burn rather than a turn, which is what
            // makes the helm and the throttle one control instead of two.
            Fix128 alignment = Dot(nose, desired);
            if (alignment > Fix128.Zero)
            {
                // kN / t is exactly m/s², which is the frame's unit — no conversion at all.
                Fix128 accelerationMetres = Engine.ThrustKilonewtons / Mass;

                // The hull's ceiling, not the engine's: a drive capable of more than the
                // structure or the crew can take is throttled back to what the hull allows.
                // This keeps a crewed ship inside 0.1-1 g and lets a shell, which has no
                // flesh to squash, use the drive's full 10-100 g.
                Fix128 ceilingMetres = Engine.MaxAccelerationInMetresPerSecondSquared;
                if (accelerationMetres > ceilingMetres)
                {
                    accelerationMetres = ceilingMetres;
                }

                thrustAcceleration = nose * (accelerationMetres * alignment);
            }
        }

        Fix128Vec acceleration = Gravity(sources, Position) + thrustAcceleration;

        Fix128 halfDt = dt * Fix128.Half;
        Fix128Vec positionDelta = Velocity * dt + acceleration * (halfDt * dt);
        Position += positionDelta;

        // Velocity Verlet needs the gravity at the NEW position. Skipping this and reusing
        // the old value turns the integrator into a non-symplectic one, which spirals an
        // orbit in or out over a long run.
        Fix128Vec newAcceleration = Gravity(sources, Position) + thrustAcceleration;
        Velocity += (acceleration + newAcceleration) * halfDt;
    }

    /// <summary>Unit vector in the same direction as <paramref name="v"/>.</summary>
    private static Fix128 Dot(Fix128Vec a, Fix128Vec b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>Total gravitational acceleration from every source, at a position.</summary>
    private static Fix128Vec Gravity(ReadOnlySpan<GravitySource> sources, Fix128Vec position)
    {
        Fix128Vec total = Fix128Vec.Zero;
        for (int i = 0; i < sources.Length; i++)
        {
            total += sources[i].AccelerationAt(position);
        }

        return total;
    }

    /// <summary>Tonnes of propellant a burn of <paramref name="seconds"/> would consume at full throttle.</summary>
    internal readonly Fix128 PropellantFor(double seconds) =>
        Engine.MassFlowTonnesPerSecond * Fix128.FromDouble(seconds);

    private static Fix128 Clamp01(Fix128 value)
    {
        if (value <= Fix128.Zero)
        {
            return Fix128.Zero;
        }

        return value >= Fix128.One ? Fix128.One : value;
    }

    /// <summary>
    /// Kilonewtons per tonne, as an acceleration in m/s². Exactly 1.
    /// </summary>
    /// <remarks>
    /// A kilonewton per tonne is 1000 N / 1000 kg = 1 m/s², so this conversion is the
    /// identity and the m/s² to Mm/s² conversion happens once, below. Applying
    /// <see cref="Units.MetresToMegametres"/> here as well scaled every acceleration by
    /// 10⁻⁶ twice, which made a 50 m/s² engine produce nothing measurable.
    /// </remarks>
    private static readonly Fix128 KilonewtonsPerTonneToMetresPerSecondSquared = Fix128.One;
}
