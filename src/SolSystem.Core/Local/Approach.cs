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
        /// <summary>Running the corridor down at the profile speed.</summary>
        Closing,

        /// <summary>Creeping the last metres into the capture envelope.</summary>
        Terminal,

        /// <summary>Inside contact range: killing the residual rate and settling on the latches.</summary>
        Hold,

        /// <summary>
        /// Braking overshot and the ship is drifting in on the creep. Only the terminal laws
        /// are allowed from here.
        /// </summary>
        /// <remarks>
        /// A fourth phase, and it exists because the first three cycle. Braking sheds speed
        /// until the closing rate reaches <see cref="BrakeComplete"/>, which happens wherever it
        /// happens; if that is still tens of metres out the law re-enters Closing, which
        /// accelerates, which trips the brake trigger again, which brakes to a crawl again. A
        /// trace of one approach shows the cycle four times — out to 1 160 m, in to 21 m, out to
        /// 86 m, in to 6 m — each pass spending propellant and arriving nowhere. Recovery is
        /// terminal-only: it creeps in from wherever the overshoot left it.
        /// </remarks>
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


    /// <summary>Speed the terminal phase holds, in m/s.</summary>
    private const double CreepSpeed = 0.05;

    /// <summary>
    /// Closing speed the hold phase maintains, in m/s.
    /// </summary>
    /// <remarks>
    /// Slow enough that the latches can hold it, positive enough that the envelope sees the ship
    /// as approaching rather than leaving.
    /// </remarks>
    private const double HoldSpeed = 0.02;


    /// <summary>
    /// Fastest the terminal phase will ask for, in m/s.
    /// </summary>
    /// <remarks>
    /// Deliberately well inside <see cref="Docking.MaxClosingSpeed"/> rather than at it. The
    /// envelope accepts 0.5 m/s, so a terminal approach that arrives at 0.65 does not dock: it
    /// sails through the port, has to come about, and comes back. One approach did exactly that,
    /// reaching 0.038 m — two hundred times inside the corridor — and then taking another
    /// eighteen thousand ticks to be captured, because the latches would not have it at that
    /// speed. A quarter of the limit leaves room for the overshoot that any real approach has.
    /// </remarks>
    private const double TerminalSpeedCap = 0.12;


    /// <summary>
    /// Range over which the helm blends from the bearing to the port onto the corridor axis.
    /// </summary>
    private const double CorridorBlendRange = 10.0;


    /// <summary>Helm gain: radians of commanded rate per radian of pointing error.</summary>
    private const double AttitudeGain = 2.0;

    /// <summary>
    /// Helm damping: how much of the current rate is subtracted from the command.
    /// </summary>
    /// <remarks>
    /// Critical damping is <c>2·sqrt(gain)</c>, which for a gain of 2 is 2.83. It was 2.4, a
    /// little under, which left the turn brisk and also left a residual oscillation that the
    /// deadband now handles; at 2.9 the response is very slightly over-damped, which is what a
    /// docking wants and almost nothing else does.
    /// </remarks>
    private const double AttitudeDamping = 2.9;

    /// <summary>
    /// Target closing speed per metre of corridor still to run.
    /// </summary>
    /// <remarks>
    /// A tenth: a metre a second from ten metres out, and five centimetres at the floor. Low
    /// enough that the ship is always slowing as it arrives, which is what stops the reversal
    /// oscillation — the failure mode of every symmetric law in this file is that braking flips
    /// the nose one way and the correction flips it back, and a ship that never needs to
    /// re-accelerate near the port never flips at all.
    /// </remarks>
    private const double ApproachGain = 0.1;


    /// <summary>
    /// Alignment above which the engine is allowed to fire.
    /// </summary>
    /// <remarks>
    /// Half, which is sixty degrees off the commanded direction and much looser than it sounds.
    /// The gate exists so the ship does not burn fuel pushing sideways while it comes about, and
    /// the thrust along the nose is already scaled by the alignment in the ship's own step — so a
    /// tight gate buys nothing and costs the endgame. At 0.9 the engine stayed shut through the
    /// last half metre, where the commanded direction flips as the ship nudges across the axis,
    /// and the ship hovered four centimetres from the port with a closing speed of zero.
    /// </remarks>
    private const double Firing = 0.50;

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
        // How fast the distance to the port is shrinking. Positive means approaching.
        //
        // This is the rate of change of `range`, not the component of velocity down the corridor
        // axis, and the two only agree while the ship is short of the port. Past it they disagree
        // in sign, and a law that steers by one and throttles by the other runs away: the trace
        // showed 10 m/s and a hundred kilometres of separation with "closing" reading a steady
        // ten. Range and its rate are a matched pair, so the law uses those for the throttle and
        // the latched corridor for the helm.
        Fix128Vec toPort = offset.IsZero ? inward : -offset.Normalized();
        double closing = Dot(ship.Velocity, toPort).ToDouble();

        // What the drive can do, which moves as the tanks empty.
        double accel = ship.Engine.ThrustKilonewtons.ToDouble() / ship.Mass.ToDouble();
        accel = Math.Min(accel, ship.Engine.MaxAccelerationInMetresPerSecondSquared.ToDouble());

        // Capture: the latches have it, so stop manoeuvring.
        //
        // The last stage, and the only one that is a latch rather than a law. Everything before it
        // is trying to reach a state; this is the state. Left flying, the approach law keeps
        // correcting, and a correction at a few centimetres is a charge through the port and out
        // the other side.
        //
        // The envelope being satisfied is necessary and not sufficient. Its tolerances say what
        // the latches can *hold*, not how close a ship should try to get: a hull two metres out,
        // closing at a third of a metre a second and pointing the right way is inside every
        // tolerance and is not docked, it is hovering on the doorstep. Requiring contact as well
        // is the difference — an earlier version latched on the envelope alone and reported every
        // arrival at 1.997 m, which is exactly the edge of the capture range and a suspiciously
        // exact number to see four times.
        if (Phase != Stage.Hold
            && Docking.Evaluate(ship, port, Fix128Vec.Zero).Contact)
        {
            Phase = Stage.Hold;
        }

        if (accel <= 0.0)
        {
            return Command.Coast;
        }

        double maxTurnRate = Attitude.CrewedMaxTurnRate.ToDouble();
        double turnSeconds = maxTurnRate <= 0.0 ? 0.0 : Math.PI / maxTurnRate;

        // Whether the ship is over the cruise speed, which is the only thing the stage latch needs.
        bool overProfile = closing > CruiseSpeed;

        // The ship is over the profile, so the approach is a braking problem rather than a cruise.
        // Latched: a trigger re-tested every tick re-arms itself as the ship slows.
        //
        // The two phases fly the SAME law — the profile governs the whole approach from two
        // kilometres out — and the stage exists only so the helm knows how much corridor is left
        // to blend onto. An earlier version let the closing phase accelerate freely and handed over
        // to the profile at thirty metres, which meant the ship arrived at the handover doing
        // twelve metres a second with the throttle gated shut while it turned round.
        if (Phase == Stage.Closing && overProfile)
        {
            Phase = Stage.Terminal;
        }

        // The acceleration the ship needs along the corridor, in its own frame.
        // The speed the approach is aiming for at this range, and the distance needed to get down
        // to it. Both are functions of what is left, and together they are the whole law.
        //
        // The target is proportional to the distance with a floor, so it never asks the ship to
        // stop and it never asks it to creep: at a hundred metres it wants a metre a second, at a
        // metre it wants a tenth, and at the floor it wants five centimetres. The braked distance
        // includes the reversal, because coming about costs thirty seconds of coasting and the
        // ship has to have the corridor for it.
        double target = Math.Max(CreepSpeed, range * ApproachGain);
        double stopping = ((closing * closing) - (target * target)) / (2.0 * accel)
            + (closing * Math.PI / Attitude.CrewedMaxTurnRate.ToDouble());

        double along;
        if (Phase == Stage.Hold)
        {
            along = Math.Clamp((HoldSpeed - closing) * 3.0, -accel, accel);
        }
        else if (stopping >= 0.0 && closing > target)
        {
            // Hot: brake.
            along = -accel;
        }
        else if (closing < target)
        {
            // Slow: close. A proportional term rather than full thrust, because near the port the
            // target is a few centimetres a second and full thrust overshoots it by more than the
            // target itself. A law that jumps between full authority in both directions when the
            // quantity it is regulating is five centimetres a second cannot settle: it nudges the
            // ship to eight millimetres from the port and coasts there, never closing and never
            // being captured, for three hundred thousand ticks.
            along = Math.Clamp((target - closing) * 0.5, -accel, accel);
        }
        else
        {
            along = 0.0;
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

        // The commanded acceleration, then the engine direction that produces it. The gravity term
        // is what makes this a rendezvous rather than a collision in any frame where the pull is
        // not cancelled: the engine has to supply `wanted - g`, not `wanted`.
        //
        // `along` is signed in the CORRIDOR sense — positive means toward the port, negative means
        // away — so the direction it multiplies has to be the fixed corridor axis, not the live
        // bearing to the port. Those two agree until the ship passes the port and then they are
        // opposites, and a braking command along the live bearing accelerates the ship instead.
        // The plot of a whole approach showed exactly that: two metres out, throttle pinned at
        // maximum, driven a hundred kilometres over the next quarter of a million ticks, every
        // number self-consistent. The live bearing is used only in the hold phase, where the
        // command is a rate to kill and there is no sign to get wrong.
        Fix128Vec line = Phase == Stage.Hold && range > 1e-9 ? toPort : inward;
        Fix128Vec wanted = line * Fix128.FromDouble(along) + sideways - frameGravity;
        if (wanted.IsZero)
        {
            return new Command(line, Fix128.Zero, Fix128Vec.Zero);
        }

        Fix128Vec direction = wanted.Normalized();

        // Fold in the corridor alignment: below a metre of lateral offset the bearing to the
        // port is dominated by whatever the last correction left behind, and a helm that chases
        // it saws the nose back and forth. The plot of a docking shows about thirty degrees of
        // chatter through the whole of the hold phase, with the throttle pulsing behind it.
        //
        // The corridor direction is blended in as the ship closes, so the aim is continuous: at
        // ten metres it is the bearing, at ten centimetres it is the corridor, and in between it
        // is a mix of the two.
        if (Phase == Stage.Terminal || Phase == Stage.Hold)
        {
            double blend = Math.Clamp(range / CorridorBlendRange, 0.0, 1.0);
            direction = (direction * Fix128.FromDouble(blend)
                + inward * Fix128.FromDouble(1.0 - blend)).Normalized();
        }

        Fix128Vec turn = TurnTowards(ship.Attitude, direction);
        double alignment = Dot(ship.Attitude.Forward, direction).ToDouble();

        double needed = wanted.Length.ToDouble();
        Fix128 throttle = alignment > Firing
            ? Fix128.FromDouble(Math.Clamp(needed / accel, 0.0, 1.0))
            : Fix128.Zero;


        return new Command(direction, throttle, turn);
    }

    /// <summary>
    /// The fastest the approach will run, in m/s.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A number about the game rather than about the maths: a hundred-tonne crewed hull crossing a
    /// two-kilometre corridor at ten metres a second takes three and a half minutes, which is a
    /// dock you can watch. Everything above it is the physics refusing to be hurried.
    /// </para>
    /// <para>
    /// It barely matters, which is worth knowing. Halving it to five costs fifteen seconds of
    /// approach, because the corridor is dominated by the braking and the reversal rather than by
    /// the cruise. What it must not exceed is what the corridor can afford: at four milligee a
    /// reversal costs thirty seconds of coasting, so braking from <c>v</c> needs
    /// <c>v²/2a + 30v</c> metres — 1 575 m from ten metres a second, 469 from five, and 324 from
    /// four. A ship faster than its corridor allows cannot stop in it.
    /// </para>
    /// </remarks>
    private const double CruiseSpeed = 10.0;

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

        double rate = attitude.AngularVelocity.Z.ToDouble();

        // Proportional only, plus a brake when the error is inside the deadband. The damping
        // term is gone and that is the whole fix.
        //
        // <para>
        // A P-D helm with a rate-limited actuator does not settle, it oscillates — and the
        // oscillation is violent and easy to miss, because it happens at the tick rate. The
        // command is gain times the error minus damping times the rate; once the actuator
        // saturates, the rate stops changing while the error keeps falling, so the damping term
        // takes over and reverses the command. The measurement at 120 Hz, with a 7-degree error:
        // </para>
        // <code>
        //   t=3551  turn=-0.060  w=-0.060
        //   t=3552  turn=+0.420  w=+0.105   (clamped)
        //   t=3553  turn=-0.061  w=-0.061
        //   t=3554  turn=+0.421  w=+0.105   (clamped)
        // </code>
        // <para>
        // The ship flips its rotation direction every 8 ms and makes no progress at all. On the
        // plot of a whole docking this is the thirty degrees of chatter through the brake and the
        // hold, with the throttle pulsing behind it.
        // </para>
        // <para>
        // Proportional alone cannot overshoot here the way it would in a damped system: a ship in
        // vacuum has no rotational drag, so a hull that stops commanding stops turning in the
        // same tick. The error therefore falls monotonically and the only question is when to
        // stop. The deadband answers that, and a brake handles the case where the ship is already
        // turning too fast to stop inside it.
        // </para>
        const double ErrorDeadband = 0.005;     // a third of a degree
        const double RateDeadband = 0.002;      // rad/s

        if (Math.Abs(error) < ErrorDeadband)
        {
            // Inside the deadband: kill any residual rate, then leave it alone.
            return Math.Abs(rate) < RateDeadband
                ? Fix128Vec.Zero
                : new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.FromDouble(-rate * 4.0));
        }

        double command = error * AttitudeGain;

        return new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.FromDouble(command));
    }

    private static Fix128 Dot(Fix128Vec a, Fix128Vec b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
