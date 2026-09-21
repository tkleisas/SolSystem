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

        /// <summary>Inside contact range: killing the residual rate and settling on the latches.</summary>
        Hold,
    }

    /// <summary>Where in the manoeuvre the ship is.</summary>
    internal Stage Phase { get; private set; }

    /// <summary>Whether the corridor axis has been captured from the port yet.</summary>
    private bool _haveAxis;

    /// <summary>
    /// The direction the ship travels to reach the port, fixed for the whole approach.
    /// </summary>
    /// <remarks>
    /// Taken from the port's axis on the first tick and then held. The live bearing — the
    /// normalised offset to the port — looks like the more correct choice and is not: inside the
    /// last few metres a lateral error of a few centimetres swings it through tens of degrees,
    /// so the law chases a direction that is mostly numerical noise. Three traces of the final
    /// approach show the nose at −0.93, then +0.98, then −0.94 within seconds, with the range
    /// wandering between 2 m and 17 m and the throttle slamming with it. A corridor is a fixed
    /// direction and that is exactly what makes it flyable.
    /// </remarks>
    private Fix128Vec _axis;

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

    /// <summary>Helm gain: radians of commanded rate per radian of pointing error.</summary>
    private const double AttitudeGain = 2.0;

    /// <summary>
    /// Helm damping: how much of the current rate is subtracted from the command.
    /// </summary>
    /// <remarks>
    /// Critical damping is <c>2·sqrt(gain)</c>, which for a gain of 2 is 2.83; a little under
    /// that leaves the turn brisk without overshooting.
    /// </remarks>
    private const double AttitudeDamping = 2.4;

    /// <summary>How hard the closing phase chases its target speed.</summary>
    private const double ClosingGain = 0.5;

    /// <summary>Range inside which the terminal creep begins, in metres.</summary>
    private const double TerminalRange = 20.0;

    /// <summary>
    /// Range inside which the ship stops flying a profile and starts settling, in metres.
    /// </summary>
    /// <remarks>
    /// The last phase, and it exists because every law before it oscillates. Chasing a speed
    /// proportional to the distance left cannot stop on a mark: the ship crosses the capture
    /// envelope at a few centimetres a second, the desired speed falls below what it is doing,
    /// the law brakes, it drifts back out, and it repeats — the *right* speed and the *right*
    /// range never coincide for long enough to be caught. The tests saw it park at 0.527 m with
    /// a closing speed of zero, and at 0.182 m the test that flies two kilometres was still
    /// going after four hundred thousand ticks.
    /// </remarks>
    private const double HoldRange = 0.25;

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
        // The corridor direction: a ship reaches the port by travelling against its axis, and
        // the axis is captured once and held.
        if (!_haveAxis)
        {
            _axis = port.Axis;
            _haveAxis = true;
        }

        Fix128Vec inward = -_axis;

        Fix128Vec offset = ship.Position - port.Position;
        double range = offset.Length.ToDouble();

        // Closing speed is measured toward the port, not along the fixed corridor axis.
        //
        // The difference only shows up after the ship has gone past, and then it is the whole
        // story. Against the fixed axis, a ship that has crossed the port and is retreating
        // still reads a *positive* closing speed, because it is still moving the same way — so
        // a law that homes on range is handed a rate of the wrong sign, drives it to the cap,
        // and runs away at ten metres a second. That is exactly what happened the first time
        // the terminal phase was given position feedback. Against the live bearing, the sign
        // flips the instant the ship passes, and the same law turns round and comes back.
        // Signed travel along the corridor: positive means closing on the port, negative means
        // past it and moving away. Along a fixed axis this is a genuine signed quantity, which
        // is what lets the same law both approach and recover from an overshoot.
        double closing = Dot(ship.Velocity, inward).ToDouble();
        double alongCorridor = Dot(offset, inward).ToDouble();

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
        else if (Phase == Stage.Terminal && range <= HoldRange)
        {
            Phase = Stage.Hold;
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
            // Proportional on the speed error, NOT "burn at full thrust until the target is
            // reached". The difference is the whole of this bug: a ship that has overshot reads
            // a negative rate error, and a law that clamps a *positive* acceleration into the
            // allowed range keeps the sign and accelerates away at full thrust. It reached ten
            // metres a second, drifting outward, with "closing" and "target" both printing ten —
            // which looks like perfect station-keeping and is a ship leaving at two kilometres a
            // minute.
            double desired = Math.Max(CreepSpeed, Math.Min(ClosingSpeedCap, range * ApproachGain));
            along = Math.Clamp((desired - closing) * ClosingGain, -accel, accel);
        }
        else if (Phase == Stage.Braking)
        {
            along = -accel;
        }
        else
        {
            // Terminal homes on the port as a POSITION, and that is the whole difference
            // between arriving and passing through. Holding a closing speed is not an
            // approach: a ship doing a steady 0.05 m/s toward a port two metres away goes
            // through it, out the other side, and continues at 0.05 m/s for as long as
            // anybody watches — which is exactly what this did, reaching 0.44 m at tick
            // 120 000 and being eleven kilometres away by tick 240 000 with the throttle shut
            // the entire time.
            //
            // So the target speed is proportional to what is left, and the loop closes on
            // range as well as on rate.
            // The target speed falls with what is left, and it does NOT fall below the creep.
            //
            // That floor is load-bearing rather than a unit conversion. The capture envelope is
            // two metres wide, so an approach that slows to a few millimetres a second outside
            // it never crosses: the ship reached 0.056 m from the port, drifting at under a
            // centimetre a second, and was still there four hundred thousand ticks later with
            // `Docked` false the whole time. A floor of CreepSpeed covers two metres in forty
            // seconds, so the envelope is entered and the latches get their chance.
            double desired = Math.Max(CreepSpeed, Math.Min(ClosingSpeedCap, range * 0.05));
            along = Math.Clamp((desired - closing) * 4.0, -accel, accel);
        }

        // A little of the lateral error, or the ship drifts off the centreline. Bounded by
        // LateralShare so it cannot take the nose off the corridor and shut the throttle.
        // The guard is on the normalised result, not on the offset. A vector whose components
        // are all non-zero can still have a length that rounds to zero — the components
        // underflow to nothing once they pass below 2⁻⁶⁴ of the scale — so testing the raw
        // vector lets a correctly-guarded normalise throw. It threw, at the moment of arrival,
        // which is exactly when the lateral offset passes through zero.
        Fix128Vec lateral = offset - port.Axis * Dot(offset, port.Axis);
        Fix128Vec sideways = Fix128Vec.Zero;
        Fix128Vec lateralDirection = lateral.Length == Fix128.Zero
            ? Fix128Vec.Zero
            : lateral.Normalized();

        if (!lateralDirection.IsZero)
        {
            double lateralSpeed = Dot(ship.Velocity, lateralDirection).ToDouble();
            double lateralAccel = Math.Clamp(
                -lateralSpeed * 0.25, -accel * LateralShare, accel * LateralShare);
            sideways = lateralDirection * Fix128.FromDouble(lateralAccel);
        }

        // The commanded acceleration, then the engine direction that produces it. The gravity
        // term is what makes this a rendezvous rather than a collision in any frame where the
        // pull is not cancelled: the engine has to supply `wanted - g`, not `wanted`.
        // The command is along the LIVE bearing, not the fixed corridor axis, and that is what
        // makes an overshoot recoverable. A signed acceleration applied along a fixed axis
        // cannot tell "close faster" from "back away": past the port the two swap meanings, the
        // law reads a large positive rate error, and it accelerates into the distance at
        // forty-four metres a second. Along the bearing, a negative `along` means "toward the
        // port" wherever the ship happens to be, so overshooting simply turns the ship round.
        Fix128Vec line = inward;
        Fix128Vec wanted = line * Fix128.FromDouble(along) + sideways - frameGravity;
        if (wanted.IsZero)
        {
            return new Command(line, Fix128.Zero, Fix128Vec.Zero);
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

        // A proportional-derivative loop, and the derivative term is not optional.
        //
        // Asking for the whole error over a tenth of a second and letting the hull's rate limit
        // do the rest is unstable: the helm overshoots, the error reverses, and at 120 Hz the
        // ship slews back and forth through ±0.1 rad every tick without ever settling. The
        // trace of a docking in its last metres showed the nose at -0.42, then +0.99, then
        // -0.64 within two seconds, with the throttle slamming open and shut behind it and the
        // range wandering between 1.2 m and 7.5 m. It approached nothing.
        double rate = attitude.AngularVelocity.Z.ToDouble();
        double command = error * AttitudeGain - rate * AttitudeDamping;

        return new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.FromDouble(command));
    }

    private static Fix128 Dot(Fix128Vec a, Fix128Vec b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
