using SolSystem.Core.Numerics;

namespace SolSystem.Core.Local;

/// <summary>
/// The approach to a docking port: the law that flies a ship down a corridor and stops it.
/// </summary>
/// <remarks>
/// <para>
/// This is the hard case in the whole project so far, and it is hard for one reason: the
/// main engine fires along the nose, so slowing down means turning round first, and a crewed
/// hull turns at six degrees a second. <b>A reversal is half a minute.</b> For all of that
/// half minute the engine cannot help, so the turn has to be paid for in distance, in
/// advance, from a decision taken before it is needed.
/// </para>
/// <para>
/// The shape that falls out is three phases, and the boundaries between them are distances
/// rather than times so they cannot drift apart as the mass changes under the burn:
/// </para>
/// <list type="number">
/// <item><b>Closing.</b> Point at the port and burn. The ship is fast and the corridor is
/// long.</item>
/// <item><b>Braking.</b> Come about and burn retrograde. The decision to enter this phase is
/// taken when the distance left equals the distance needed to stop <em>plus</em> the distance
/// that will be covered while turning round, and it is <em>latched</em>: a trigger re-tested
/// every tick re-arms itself as the ship slows, which saws the nose back and forth and never
/// arrives.</item>
/// <item><b>Terminal.</b> The last few metres at a creep, inside the envelope the capture
/// latches can hold.</item>
/// </list>
/// <para>
/// The law was written four times as a private method in a probe before it worked, and every
/// one of those attempts taught a rule that is recorded at the line it applies to. What
/// follows is the fifth, and the first that is a type rather than a script.
/// </para>
/// </remarks>
internal struct Approach
{
    /// <summary>Which part of the manoeuvre the ship is in.</summary>
    internal enum Stage
    {
        /// <summary>Burning toward the port.</summary>
        Closing,

        /// <summary>Coming about, then burning to shed speed.</summary>
        Braking,

        /// <summary>Creeping the last metres into the capture envelope.</summary>
        Terminal,
    }

    /// <summary>Where in the manoeuvre the ship is.</summary>
    internal Stage Phase { get; private set; }

    /// <summary>
    /// Fraction of the drive a lateral correction may use.
    /// </summary>
    /// <remarks>
    /// Small, and the reason is not tidiness. The throttle is gated on the nose pointing at
    /// the commanded direction, so a correction big enough to swing the nose more than about
    /// twenty-five degrees off the corridor shuts the engine down entirely — and then the ship
    /// coasts, holding its attitude, arriving never. A tenth of the drive keeps the total
    /// command within about six degrees of the corridor axis.
    /// </remarks>
    private const double LateralShare = 0.10;

    /// <summary>Speed below which braking is considered finished, in m/s.</summary>
    private const double BrakeComplete = 0.05;

    /// <summary>Speed the terminal phase holds, in m/s.</summary>
    private const double CreepSpeed = 0.05;

    /// <summary>Fastest the closing phase will ask for, in m/s.</summary>
    private const double ClosingSpeedCap = 10.0;

    /// <summary>Closing speed per metre of corridor still to run.</summary>
    private const double ApproachGain = 0.04;

    /// <summary>How hard the closing phase chases its target speed.</summary>
    private const double ClosingGain = 0.5;

    /// <summary>Range inside which the terminal creep begins, in metres.</summary>
    private const double TerminalRange = 30.0;

    /// <summary>Alignment above which the engine is allowed to fire.</summary>
    private const double Firing = 0.90;

    /// <summary>
    /// The brake trigger, which is not a stopping distance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things make the real braking distance longer than <c>v²/2a</c>, and the first
    /// version of this used neither and then a flat multiple of three, which is worse than
    /// either.
    /// </para>
    /// <list type="number">
    /// <item><b>The reversal.</b> The ship covers <c>v·t_turn</c> while coming about, because
    /// for the whole of the turn the nose is not where it needs to be and the gate holds the
    /// engine shut.</item>
    /// <item><b>The gate ramp.</b> Even once the nose starts to come round, the throttle only
    /// reaches full when the alignment does. Over a full reversal the alignment passes through
    /// every value, and the mean of <c>max(0, cos θ)</c> across that sweep is 1/π — call it a
    /// third. So the ship loses another <c>v·t_turn/3</c> of effective braking to the ramp.</item>
    /// </list>
    /// <para>
    /// Folding both in gives <c>v²/2a + 1.33·v·t_turn</c>. The version that used a flat
    /// multiple of three braked at 1 544 m with 5.97 m/s on the clock, spent its whole thirty
    /// seconds of reversal coasting, arrived at the end of the burn still doing 3.3 m/s, and
    /// then latched into a 0.05 m/s creep nine hundred metres short of the station — where it
    /// would have taken six hours to arrive, and never did.
    /// </para>
    /// </remarks>
    private const double GateRampFactor = 1.0 + (1.0 / 3.0);

    /// <summary>Seconds the hull takes to turn a half turn, from its own rate limit.</summary>
    private static Fix128 TurnSeconds(Fix128 maxTurnRate) =>
        maxTurnRate == Fix128.Zero
            ? Fix128.Zero
            : Fix128.FromDouble(Math.PI) / maxTurnRate;

    /// <summary>
    /// The command for this tick.
    /// </summary>
    /// <param name="ship">The ship, read only. It is a struct, so pass it by value.</param>
    /// <param name="port">The port being approached.</param>
    /// <param name="frameGravity">
    /// Gravitational acceleration acting on the ship in this frame, if any. In a station's own
    /// frame there is none — both are falling together, and the residual tidal terms over a
    /// two-kilometre approach are millimetres. Pass the real figure when flying in a frame
    /// where the pull is not cancelled.
    /// </param>
    internal Command Next(in Ship ship, DockingPort port, Fix128Vec frameGravity)
    {
        // The corridor direction: a ship reaches the port by travelling against its axis.
        Fix128Vec inward = -port.Axis;

        Fix128Vec offset = ship.Position - port.Position;
        double range = offset.Length.ToDouble();
        double closing = Dot(ship.Velocity, inward).ToDouble();

        // What the drive can do, which moves as the tanks empty.
        double accel = ship.Engine.ThrustKilonewtons.ToDouble() / ship.Mass.ToDouble();
        accel = Math.Min(accel, ship.Engine.MaxAccelerationInMetresPerSecondSquared.ToDouble());

        if (accel <= 0.0)
        {
            return Command.Coast;
        }

        double maxTurnRate = Attitude.CrewedMaxTurnRate.ToDouble();
        double turnSeconds = maxTurnRate <= 0.0 ? 0.0 : Math.PI / maxTurnRate;

        // The physics that make this a manoeuvre rather than a translation: the reversal and
        // the gate ramp, both paid for in distance before the decision is taken. See
        // GateRampFactor.
        double stopping = closing * closing / (2.0 * accel);
        double turnPenalty = closing * turnSeconds * GateRampFactor;

        // The latch. Once committed, committed: see the class remarks.
        //
        // And it requires the ship to actually be moving toward the port. At rest the two
        // distances are both zero, so `range <= 0` is false at any real standoff and the
        // condition looks safe — but the margin multiplies the stopping distance, and a ship
        // that has just arrived at zero closing speed still satisfies `range <= 0 * margin`.
        // It then latches into braking with nothing to brake, commands a reversal, loses its
        // alignment gate, and never moves again. Requiring way on is what makes the decision
        // mean what it says.
        if (Phase == Stage.Closing
            && closing > BrakeComplete
            && range <= stopping + turnPenalty)
        {
            Phase = Stage.Braking;
        }
        else if (Phase == Stage.Braking && closing <= BrakeComplete)
        {
            // Only terminal if there is nothing left to travel. Reaching a low closing speed a
            // kilometre out is not an arrival, and treating it as one is how the first version
            // parked the ship nine hundred metres from the port for six hours. Far out, the
            // right answer is to close the distance again.
            Phase = range <= TerminalRange ? Stage.Terminal : Stage.Closing;
        }

        // The acceleration the ship needs along the corridor, in its own frame.
        double along;
        if (Phase == Stage.Closing)
        {
            // Toward a target speed, not at full thrust. "Closing" used to mean "burn", which
            // is right on the way in and badly wrong on the way back: after a long brake the
            // ship arrives at the port slowly, the latch releases into Closing, Closing
            // commands full thrust toward a port a hundred metres away, and the ship
            // re-accelerates into a second approach it did not need. The target climbs with
            // the distance left so the law is continuous at both ends.
            double desired = Math.Max(CreepSpeed, Math.Min(ClosingSpeedCap, range * ApproachGain));
            along = Math.Clamp((desired - closing) * ClosingGain, -accel, accel);
        }
        else if (Phase == Stage.Braking)
        {
            along = -accel;
        }
        else
        {
            along = Math.Clamp((CreepSpeed - closing) * 0.5, -accel * 0.25, accel * 0.25);
        }

        // A little of the lateral error, or the ship drifts off the centreline. Bounded by
        // LateralShare so it cannot take the nose off the corridor and shut the throttle.
        Fix128Vec lateral = offset - port.Axis * Dot(offset, port.Axis);
        Fix128Vec sideways = Fix128Vec.Zero;
        if (!lateral.IsZero)
        {
            Fix128Vec lateralDirection = lateral.Normalized();
            double lateralSpeed = Dot(ship.Velocity, lateralDirection).ToDouble();
            double lateralAccel = Math.Clamp(
                -lateralSpeed * 0.25, -accel * LateralShare, accel * LateralShare);
            sideways = lateralDirection * Fix128.FromDouble(lateralAccel);
        }

        // The commanded acceleration, then the engine direction that produces it. The gravity
        // term is what makes this a rendezvous rather than a collision in any frame where the
        // pull is not cancelled: the engine has to supply `wanted - g`, not `wanted`.
        Fix128Vec wanted = inward * Fix128.FromDouble(along) + sideways - frameGravity;
        if (wanted.IsZero)
        {
            return new Command(inward, Fix128.Zero, Fix128Vec.Zero);
        }

        Fix128Vec direction = wanted.Normalized();
        Fix128Vec turn = TurnTowards(ship.Attitude, direction);
        double alignment = Dot(ship.Attitude.Forward, direction).ToDouble();

        double needed = wanted.Length.ToDouble();
        Fix128 throttle = alignment > Firing
            ? Fix128.FromDouble(Math.Clamp(needed / accel, 0.0, 1.0))
            : Fix128.Zero;

        return new Command(direction, throttle, turn);
    }

    /// <summary>
    /// Angular velocity that swings the ship's nose onto <paramref name="direction"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Worked from the attitude rather than from the angle between the nose and the target, and
    /// that is not a style choice. Two anti-parallel vectors have a zero cross product, so the
    /// obvious formulation tells a ship ordered to reverse <em>not to turn</em> — and a
    /// reversal is the most common manoeuvre in docking, because braking means turning round.
    /// Working from the attitude has no degenerate case: a half turn is just the rotation
    /// vector that points the other way.
    /// </para>
    /// <para>
    /// The whole error is asked for and then clamped by the hull's own rate limit, which is
    /// where the limit belongs. Scaling it down here instead looks equivalent and is a trap:
    /// a half turn scaled by a gain is a slow turn, the alignment gate stays shut for the whole
    /// of it, and the engine never lights at all.
    /// </para>
    /// <para>
    /// This only handles rotation in the plane of the corridor, which is the plane the design
    /// keeps its stations and ships in. A target off that plane needs the general
    /// axis-angle form and is not written.
    /// </para>
    /// </remarks>
    internal static Fix128Vec TurnTowards(Attitude attitude, Fix128Vec direction)
    {
        Fix128Vec unit = direction.Normalized();
        if (unit.IsZero)
        {
            return Fix128Vec.Zero;
        }

        double wanted = Math.Atan2(unit.Y.ToDouble(), unit.X.ToDouble());
        double error = wanted - attitude.RotationVector.Z.ToDouble();

        while (error > Math.PI)
        {
            error -= 2.0 * Math.PI;
        }

        while (error <= -Math.PI)
        {
            error += 2.0 * Math.PI;
        }

        // The whole error over a tenth of a second, so the clamp is the only limiter.
        return new Fix128Vec(
            Fix128.Zero, Fix128.Zero, Fix128.FromDouble(error / 0.1));
    }

    private static Fix128 Dot(Fix128Vec a, Fix128Vec b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
