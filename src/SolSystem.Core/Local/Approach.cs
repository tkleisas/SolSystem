using SolSystem.Core.Numerics;

namespace SolSystem.Core.Local;

/// <summary>
/// Flying a ship down a station's corridor and onto its port.
/// </summary>
/// <remarks>
/// <para>
/// The velocity profile is the classical <see cref="Glideslope"/> — Hablani et al., 2002, via the
/// Space Shuttle — and the job of this type is to fly it with one engine that points along the
/// nose. That constraint is the whole difficulty, and it is what four earlier versions of this law
/// kept rediscovering:
/// </para>
/// <list type="bullet">
/// <item><b>Slowing down means turning round.</b> The engine fires along the nose, so deceleration
/// requires a reversal, and a crewed hull reverses at six degrees a second — half a minute during
/// which the engine is useless and the ship coasts. The glideslope's required deceleration is
/// <c>(v₀−v_T)·v/r₀</c>, largest where the ship is fastest, so the corridor has to be long enough
/// for the reversal to happen inside it. That is a hard constraint, not a tuning problem.</item>
/// <item><b>A law that re-decides every tick at a threshold chatters, and every chatter is a
/// reversal order.</b> One version issued six thousand of them on a single approach. The phases are
/// therefore latched: once braking starts it runs until the ship is back on the profile.</item>
/// <item><b>Nose-forward, the ship can only accelerate.</b> It regulates the final approach by
/// <em>coasting</em>, never by braking, which is why the creep never overshoots and never flips
/// again. A symmetric controller cannot do this, and it is what made the earlier versions
/// oscillate.</item>
/// </list>
/// <para>
/// The four phases are the flight, in order: run the corridor down, brake onto the profile, creep
/// in nose-first, and settle inside the contact range.
/// </para>
/// </remarks>
internal struct Approach
{
    /// <summary>Which part of the manoeuvre the ship is in.</summary>
    internal enum Stage
    {
        /// <summary>Running the corridor down, nose forward, accelerating toward the profile.</summary>
        Run,

        /// <summary>Nose aft, full retrograde, latched until the ship is back on the profile.</summary>
        Brake,

        /// <summary>Nose forward and thrust-only: creeping in, regulating by coasting.</summary>
        Creep,

        /// <summary>Inside the contact range. The latches have it and the law stops manoeuvring.</summary>
        Hold,
    }

    /// <summary>Where in the manoeuvre the ship is.</summary>
    internal Stage Phase { get; private set; }

    /// <summary>
    /// The rate the corridor is run at, in metres per second, before the drive caps it.
    /// </summary>
    /// <remarks>
    /// A hundred-tonne crewed hull covering two kilometres in a few minutes. It is also, and more
    /// importantly, near what the physics allows: the glideslope's peak deceleration is
    /// <c>(v₀−v_T)·v₀/r₀</c>, which for four milligee over two kilometres caps the initial rate at
    /// <c>sqrt(a·r₀) ≈ 8.9 m/s</c>. <see cref="ProfileFor"/> reduces it to whatever the drive and the
    /// corridor can actually fly.
    /// </remarks>
    private static readonly Fix128 CorridorRate = Fix128.FromDouble(10.0);

    /// <summary>
    /// The rate at contact, in metres per second.
    /// </summary>
    /// <remarks>
    /// The glideslope's intercept, and the number that makes the whole law work. It must be
    /// <em>positive</em> — a profile commanding zero at contact approaches the port asymptotically
    /// and stops outside the capture envelope, which is the failure that cost several days — and it
    /// must be under what the latches accept, which is <see cref="Docking.MaxClosingSpeed"/>. A fifth
    /// of that leaves room for the overshoot every real approach has.
    /// </remarks>
    private static readonly Fix128 ContactRate = Fix128.FromDouble(0.10);

    /// <summary>
    /// The rate at which the ship stops braking and comes about for the last time, in m/s.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The handover is on the rate, not on the profile, and that is the whole of it.</b> The
    /// first version of this released the brake as soon as the ship was back on the glideslope — at
    /// 4.7 metres a second, a thousand metres out — and the creep phase, which can only accelerate,
    /// immediately ran away with it: the trace shows the range climbing past a hundred kilometres
    /// with the throttle pinned. The profile is a line the ship can only follow <em>downward</em> by
    /// braking, so releasing the brake anywhere above the rate the creep can hold is a one-way trip.
    /// </para>
    /// <para>
    /// The cost of a late handover is a real one, which is why it is a compromise rather than zero.
    /// Coming about takes half a minute during which the ship cannot brake, so at 3 m/s the reversal
    /// would eat ninety metres of corridor; at 0.3 m/s it eats nine. Below about a fifth of a metre a
    /// second the last stretch takes longer than a player will wait. Three tenths is fast enough to
    /// close the remaining corridor in a few minutes and slow enough that the reversal is cheap.
    /// </para>
    /// </remarks>
    private static readonly Fix128 HandoverRate = ContactRate * Fix128.FromDouble(1.5);


    /// <summary>Lateral offset inside which no correction is attempted, in metres.</summary>
    private static readonly Fix128 LateralDeadband = Fix128.FromDouble(0.05);

    /// <summary>Lateral rate inside which no correction is attempted, in metres per second.</summary>
    private static readonly Fix128 LateralRateDeadband = Fix128.FromDouble(0.005);

    /// <summary>Fraction of the drive a lateral correction may use.</summary>
    /// <remarks>
    /// Small, because the throttle is gated on the nose pointing the right way: a correction big
    /// enough to swing the nose more than about twenty-five degrees off the corridor shuts the engine
    /// down entirely, and then the ship coasts, holding its attitude, arriving never.
    /// </remarks>
    private static readonly Fix128 LateralShare = Fix128.FromDouble(0.10);

    /// <summary>
    /// Alignment required to fire the engine at full throttle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The requirement scales with how much thrust is asked for, and that is not a refinement —
    /// it is the fix for the largest error in this law.</b> A fixed gate is wrong in both
    /// directions. Tight, and the endgame suffers: at 0.9 the engine stayed shut through the last
    /// half metre, where the commanded direction flips as the ship nudges across the axis, and the
    /// ship hovered four centimetres from the port with a closing rate of zero. Loose, and the
    /// reversal suffers catastrophically, because the engine fires along the <em>nose</em> and a
    /// nose sixty degrees off the command puts half the thrust sideways.
    /// </para>
    /// <para>
    /// That is exactly what happened. A 180-degree reversal at a gate of 0.5 fires from the moment
    /// the nose is sixty degrees round, and over the twenty seconds of the turn it throws the ship
    /// <b>eighteen metres off the corridor axis</b> — measured, and then twenty-four by the time the
    /// turn finishes. The lateral channel is a damper, so it cannot see a position error at all, and
    /// the ship then flies the whole approach twenty-four metres wide and arrives outside the capture
    /// envelope with everything else about the approach looking perfect.
    /// </para>
    /// <para>
    /// Scaling the gate means a full-authority burn waits until the ship is within eight degrees,
    /// while a one-per-cent correction fires whenever it likes. Big burns are patient; small ones are
    /// not, which is the right way round.
    /// </para>
    /// </remarks>
    private static readonly Fix128 FiringAtFullThrottle = Fix128.FromDouble(0.99);

    /// <summary>Alignment required to fire the engine at a whisper.</summary>
    private static readonly Fix128 FiringAtIdle = Fix128.FromDouble(0.50);

    /// <summary>Helm gain: radians of commanded rate per radian of pointing error.</summary>
    private static readonly Fix128 AttitudeGain = Fix128.FromDouble(2.0);

    /// <summary>Helm damping. Critical is 2·sqrt(gain) = 2.83; slightly over is what a docking wants.</summary>
    private static readonly Fix128 AttitudeDamping = Fix128.FromDouble(2.9);



    /// <summary>Whether the corridor axis and the profile have been captured yet.</summary>
    private bool _haveProfile;

    /// <summary>
    /// The direction the ship travels to reach the port, latched on the first tick.
    /// </summary>
    /// <remarks>
    /// Latched rather than recomputed, and from the port's axis rather than from the live bearing.
    /// Inside the last metres a lateral error of a few centimetres swings the bearing through tens of
    /// degrees, so a law that chases it is steering on noise — one trace shows the nose at −0.93, then
    /// +0.98, then −0.94 within seconds. A corridor is a fixed direction, and that is exactly what
    /// makes it flyable.
    /// </remarks>
    private Fix128Vec _axis;

    /// <summary>The approach profile, built from the corridor the ship was launched down.</summary>
    private Glideslope _profile;

    /// <summary>
    /// The command for this tick.
    /// </summary>
    /// <param name="ship">The ship, read only. It is a struct, so pass it by value.</param>
    /// <param name="port">The port being approached.</param>
    /// <param name="frameGravity">
    /// Gravitational acceleration on the ship in this frame, if any. In a station's own frame there
    /// is none — both are falling together. Pass the real figure in a frame where the pull is not
    /// cancelled.
    /// </param>
    internal Command Next(in Ship ship, DockingPort port, Fix128Vec frameGravity)
    {
        if (!_haveProfile)
        {
            _axis = port.Axis;

            // Built from the corridor the ship actually starts down, so it is feasible by
            // construction: the rate is reduced until the drive can fly the line.
            Fix128 startRange = (ship.Position - port.Position).Length;
            _profile = ProfileFor(startRange, DriveAcceleration(ship));
            _haveProfile = true;
        }

        Fix128Vec inward = -_axis;
        Fix128Vec offset = ship.Position - port.Position;
        Fix128 range = offset.Length;

        // Closing rate, measured toward the port rather than along the fixed axis. The two agree
        // until the ship passes the port and then they are opposites, and a law that throttles on one
        // while steering by the other runs away: a hundred kilometres of it, with "closing" reading a
        // steady ten metres a second.
        Fix128Vec toPort = offset.IsZero ? inward : -offset.Normalized();
        Fix128 closing = Fix128Vec.Dot(ship.Velocity, toPort);

        Fix128 accel = DriveAcceleration(ship);
        Fix128 commanded = _profile.RateAt(range);

        // The latches have it: stop manoeuvring. Everything before this is trying to reach a state;
        // this is the state. Left flying, the law keeps correcting and a correction at a few
        // centimetres is a charge through the port and out the other side.
        if (Phase != Stage.Hold && Docking.Evaluate(ship, port, Fix128Vec.Zero).Contact)
        {
            Phase = Stage.Hold;
        }

        Fix128 along;
        switch (Phase)
        {
            case Stage.Hold:
                // Settled: the latches have the ship, and the law's job is over. NOT an
                // active station-keep, whatever the clamp used to say: the ship is
                // nose-first and thrust-only, so it cannot null its own residual rate
                // without a reversal it must never take this close. The clamp on -closing
                // produced exactly one command — full throttle, because the aim below is
                // the corridor and the corridor points the way the ship was already going —
                // which threw a docked ship through the port and forty metres a second out
                // the other side. Every docking test quite properly stops at contact, so
                // the runaway had never been ticked until a probe flew two minutes past it.
                along = Fix128.Zero;
                break;

            case Stage.Run:
                // Nose forward. Accelerate toward the profile; if the ship is already above it, it
                // cannot slow down without turning round, so the brake takes over.
                if (closing > commanded)
                {
                    Phase = Stage.Brake;
                    along = -accel;
                }
                else
                {
                    along = accel;
                }

                break;

            case Stage.Brake:
                // Nose aft, and braking in PROPORTION to how far above the profile the ship is.
                //
                // Full authority is wrong here and the reason is worth recording, because it looks
                // like the safe choice. Braking at the drive's maximum from three tenths of a metre
                // a second brings the ship to rest in a metre and a quarter, so a full-authority
                // brake that starts at eleven metres stops at ten and the pure-coast creep after it
                // has nothing left to coast with. Proportional braking tracks the line down instead,
                // converging on it rather than crossing it, and the ship arrives at the line's own
                // contact rate because that is where the line goes.
                along = Fix128.Clamp((commanded - closing) * Fix128.FromWhole(2), -accel, Fix128.Zero);

                if (closing <= HandoverRate)
                {
                    Phase = Stage.Creep;
                }

                break;

            default:
                // Creep: nose forward, thrust-only, regulating by coasting. Never brakes, so it never
                // flips again, so it never overshoots.
                //
                // The target is a CONSTANT — the handover rate — and not the profile, which is the
                // subtlest bug in this law and the one that took a trace to find.
                //
                // The profile falls with range: 0.23 m/s at thirty metres, 0.20 at twenty. A
                // thrust-only phase tracking a falling target thrusts whenever it is a hair below,
                // and thrust is the one thing it cannot undo. The rate therefore ratchets upward
                // with every tick of noise, and the ship that was creeping in at 0.23 m/s is doing
                // three metres a second and climbing by the time it reaches the port. The trace:
                //
                //   r=29.9995  closing=0.23143  commanded=0.2313  along=0.00000
                //   r=26.2806  closing=0.21502  commanded=0.2150  along=0.03920
                //   r=23.5787  closing=-0.02466 commanded=0.2032  along=0.03920
                //
                // The creep does not thrust at all, and that is the whole of it.
                //
                // A thrust-only actuator can only ever add speed. Every attempt to make it *track* a
                // rate therefore ratchets: a pulse whenever the rate is a hair low, and no way to take
                // the surplus back. Four versions of this tried, with full authority, with a quarter,
                // with a deadband, and each ended with the ship arriving faster than the one before —
                // 0.30 m/s at the handover to 0.72 at the port, which is past the 0.5 the latches
                // accept, so a geometrically perfect approach was refused by the envelope for arriving
                // too fast. The brake has already delivered the ship at exactly the handover rate;
                // nothing removes speed from a coasting ship in vacuum; so it arrives at that rate.
                //
                // The lateral correction stays, because it is perpendicular: it steers the ship onto
                // the centreline without touching the approach rate.
                along = Fix128.Zero;

                break;
        }

        // No lateral thrust in the creep, and the reason is the engine's position rather than the
        // control law's preference.
        //
        // The engine fires along the NOSE, so "thrust sideways" and "point sideways" are the same
        // instruction. A creep that corrects its lateral offset is therefore also turning the ship
        // away from the corridor — and the envelope requires the nose within ten degrees of the
        // approach axis. One approach arrived at 0.6 m doing a healthy 0.14 m/s with the nose
        // NINETY-SEVEN degrees off, rotating back at the maximum six degrees a second, which takes
        // sixteen seconds and two metres of corridor it does not have. It sailed past a port it was
        // perfectly lined up to hit.
        //
        // So the centring is done in the run and the brake, where there is room and time, and the
        // creep is a pure coast down a fixed line with the nose on it.
        Fix128Vec sideways = Phase == Stage.Creep || Phase == Stage.Hold
            ? Fix128Vec.Zero
            : LateralCorrection(ship, port, offset, range, accel);

        // Which way "positive along" points depends on the phase, and getting it wrong is a
        // seventeen-metre-a-second runaway.
        //
        // For the run and the brake it is the fixed corridor axis, and that is right: `along` there
        // means "burn toward the port" or "burn retrograde", and those are the same directions all
        // the way down the corridor. For the creep and the hold it is the live direction to the
        // port, because `along` there means "close whatever gap is left" — and once the ship is past
        // the port the fixed axis points the *other way*. The trace of one approach shows the ship
        // crossing the port at 0.73 m/s, then being told to close the gap, and accelerating away
        // down the +x axis to minus seventeen metres a second with the throttle at a quarter.
        Fix128Vec line = Phase == Stage.Creep || Phase == Stage.Hold ? toPort : inward;
        Fix128Vec wanted = (line * along) + sideways - frameGravity;

        // The guard is on the LENGTH, not on the components. A vector whose components are all
        // non-zero can still have a length that rounds to zero once they pass below 2⁻⁶⁴ of the
        // scale, so `IsZero` says no and `Normalized` throws. It threw here, on the first tick of a
        // coasting approach, because a near-zero `along` and a near-zero lateral correction sum to a
        // vector smaller than the type can measure.
        if (wanted.Length == Fix128.Zero)
        {
            // Nothing to burn, but the helm still has a job: the ship has to be pointing the right
            // way when it arrives.
            //
            // Two bugs lived in this one line. Returning a zero turn left the nose pointing AFT from
            // the braking reversal for the whole of the creep — the ship coasted the last eleven
            // metres sideways-on and was refused for being a hundred and seventy degrees out of
            // alignment, frozen, for six hundred consecutive ticks. And aiming at `line`, which in
            // this phase is the live bearing to the port, is aiming at a direction that swings to
            // ninety degrees as the range closes: the ship's nose followed it round and arrived at
            // twenty degrees and opening.
            //
            // The aim is the corridor. It is the direction the port faces, it is what the envelope
            // measures against, and it does not move.
            return new Command(inward, Fix128.Zero, TurnTowards(ship.Attitude, inward));
        }

        Fix128Vec direction = wanted.Normalized();

        // Inside the last few metres the direction to the port is dominated by whatever the last
        // correction left behind. Blend onto the corridor so the aim is continuous and the helm stops
        // chasing noise.
        if (Phase == Stage.Creep || Phase == Stage.Hold)
        {
            // Aim straight down the corridor. Not blended toward it — the correction it would be
            // blended with is zero, and at this range anything derived from the bearing to the port
            // is noise.
            direction = inward;
        }

        Fix128Vec turn = TurnTowards(ship.Attitude, direction);
        Fix128 alignment = Fix128Vec.Dot(ship.Attitude.Forward, direction);

        Fix128 needed = wanted.Length;
        Fix128 demand = accel > Fix128.Zero ? Fix128.Clamp(needed / accel, Fix128.Zero, Fix128.One) : Fix128.Zero;

        // The more thrust is asked for, the better the aim has to be. See FiringAtFullThrottle.
        Fix128 required = FiringAtIdle + ((FiringAtFullThrottle - FiringAtIdle) * demand);
        Fix128 throttle = alignment > required ? demand : Fix128.Zero;


        return new Command(direction, throttle, turn);
    }

    /// <summary>
    /// A profile the drive can actually fly down the corridor it has been given.
    /// </summary>
    /// <remarks>
    /// The corridor rate is capped by what the drive can shed. The glideslope's peak deceleration is
    /// <c>(v₀−v_T)·v₀/r₀</c>, so a short corridor or a weak drive means a gentler approach rather
    /// than an arrival at speed. Building it here, from the ship's own acceleration and the range it
    /// was launched at, is what makes the law work for a courier and a freighter with no separate
    /// tuning — the freighter simply flies a gentler slope.
    /// </remarks>
    private static Glideslope ProfileFor(Fix128 range, Fix128 accel)
    {
        // a = (v₀ − v_T)·v₀/r₀, solved for v₀ with v_T small enough to drop from the product.
        Fix128 initial = Fix128.Min(CorridorRate,
            Fix128.Sqrt(accel * Fix128.Max(range, Fix128.FromDouble(1e-6))));

        // Never plan a profile that asks for less than the ship is committed to at contact.
        if (initial < ContactRate)
        {
            initial = ContactRate;
        }

        return Glideslope.For(range, initial, ContactRate);
    }

    /// <summary>The drive's acceleration, capped by the hull's own ceiling.</summary>
    private static Fix128 DriveAcceleration(in Ship ship)
    {
        Fix128 accel = ship.Engine.ThrustKilonewtons / ship.Mass;
        return Fix128.Min(accel, ship.Engine.MaxAccelerationInMetresPerSecondSquared);
    }

    /// <summary>
    /// The acceleration that pulls the ship back onto the corridor centreline.
    /// </summary>
    /// <remarks>
    /// The guard is on the normalised result and not on the raw offset: a vector whose components are
    /// all non-zero can still have a length that rounds to zero once they pass below 2⁻⁶⁴ of the
    /// scale, so testing the raw vector lets a correctly-guarded normalise throw. It threw, at the
    /// moment of arrival, which is exactly when the lateral offset passes through zero.
    /// </remarks>
    private static Fix128Vec LateralCorrection(in Ship ship, DockingPort port, Fix128Vec offset,
        Fix128 range, Fix128 accel)
    {
        Fix128Vec lateral = offset - (port.Axis * Fix128Vec.Dot(offset, port.Axis));
        if (lateral.Length == Fix128.Zero)
        {
            return Fix128Vec.Zero;
        }

        Fix128Vec direction = lateral.Normalized();
        if (direction.IsZero)
        {
            return Fix128Vec.Zero;
        }

        // A spring AND a damper, because a damper alone cannot see a position error: a ship ten
        // metres off the axis and moving parallel to it has zero lateral velocity, so a pure damper
        // computes zero correction and leaves it there. That is not hypothetical — it is what the
        // twenty-four-metre drift above did once it had been thrown off, and it would have stayed
        // there for the whole approach.
        Fix128 speed = Fix128Vec.Dot(ship.Velocity, direction);
        Fix128 offsetMetres = lateral.Length;

        // Five centimetres a second per metre of error, so twenty metres asks for a metre a second
        // and the loop has something to damp.
        //
        // Both terms have a deadband. The corridor is only a metre wide and the capture envelope
        // takes anything inside it, so correcting a two-centimetre offset is work done for nothing —
        // and in a thrust-only phase, work done for nothing is speed that can never be taken back.
        Fix128 desiredRate = -offsetMetres * RatePerMetre;
        Fix128 error = desiredRate - speed;

        // The deadband SHRINKS with the range, and it has to, because the envelope's alignment
        // tolerance is an angle. Ten degrees at two metres allows thirty-five centimetres of lateral
        // offset; at thirteen centimetres it allows two. A fixed five-centimetre deadband therefore
        // stops correcting at exactly the point where a five-centimetre offset becomes twenty degrees
        // of misalignment — which is how a ship that had crept to within thirteen centimetres of the
        // port was refused for not being lined up.
        Fix128 deadband = Fix128.Min(LateralDeadband, range * DeadbandPerMetre);
        if (offsetMetres < deadband && speed.Abs() < LateralRateDeadband)
        {
            return Fix128Vec.Zero;
        }

        Fix128 correction = Fix128.Clamp(
            error * Fix128.Half, -accel * LateralShare, accel * LateralShare);

        return direction * correction;
    }

    /// <summary>
    /// Angular velocity that swings the ship's nose onto <paramref name="direction"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Proportional, with a deadband, and no derivative term.</b> A P-D helm driving a
    /// rate-limited actuator does not settle, it oscillates — and it oscillates at the tick rate,
    /// which is why a trace of magnitudes never showed it. Once the actuator saturates the rate stops
    /// changing while the error keeps falling, so the damping term takes over and reverses the command
    /// every eight milliseconds:
    /// </para>
    /// <code>
    ///   t=3551  turn=-0.060  w=-0.060
    ///   t=3552  turn=+0.420  w=+0.105   (clamped)
    ///   t=3553  turn=-0.061  w=-0.061
    ///   t=3554  turn=+0.421  w=+0.105   (clamped)
    /// </code>
    /// <para>
    /// The ship flips its rotation direction twice per tick and makes no progress at all. Proportional
    /// alone cannot overshoot the way it would in a damped system, because a ship in vacuum has no
    /// rotational drag: a hull that stops commanding stops turning in the same tick. The error
    /// therefore falls monotonically and the only question is when to stop, which the deadband
    /// answers; a brake handles a ship already turning too fast to stop inside it.
    /// </para>
    /// <para>
    /// Worked from the attitude rather than from the angle between the nose and the target, because
    /// two anti-parallel vectors have a zero cross product and the obvious formulation tells a ship
    /// ordered to reverse <em>not to turn</em> — and the reversal is the whole point of the brake
    /// phase.
    /// </para>
    /// </remarks>
    internal static Fix128Vec TurnTowards(Attitude attitude, Fix128Vec direction)
    {
        Fix128Vec unit = direction.Normalized();
        if (unit.IsZero)
        {
            return Fix128Vec.Zero;
        }

        Fix128 wanted = Fix128.Atan2(unit.Y, unit.X);
        Fix128 error = wanted - attitude.RotationVector.Z;

        while (error > Pi)
        {
            error -= TwoPi;
        }

        while (error <= -Pi)
        {
            error += TwoPi;
        }

        Fix128 rate = attitude.AngularVelocity.Z;

        if (error.Abs() < ErrorDeadband)
        {
            return rate.Abs() < RateDeadband
                ? Fix128Vec.Zero
                : new Fix128Vec(Fix128.Zero, Fix128.Zero, -rate * Fix128.FromWhole(4));
        }

        Fix128 command = (error * AttitudeGain) - (rate * AttitudeDamping);
        return new Fix128Vec(Fix128.Zero, Fix128.Zero, command);
    }

    /// <summary>Half a turn, precomputed once: the fold bounds of the helm's error.</summary>
    private static readonly Fix128 Pi = Fix128.FromDouble(Math.PI);

    /// <summary>A full turn, precomputed once.</summary>
    private static readonly Fix128 TwoPi = Fix128.FromDouble(2.0 * Math.PI);

    /// <summary>Pointing error inside which the helm stops commanding, in radians. A third of a degree.</summary>
    private static readonly Fix128 ErrorDeadband = Fix128.FromDouble(0.005);

    /// <summary>Angular rate inside which the helm stops damping, in radians per second.</summary>
    private static readonly Fix128 RateDeadband = Fix128.FromDouble(0.002);

    /// <summary>The lateral spring's gain: metres per second of desired rate per metre of offset.</summary>
    private static readonly Fix128 RatePerMetre = Fix128.FromDouble(0.05);

    /// <summary>The lateral deadband's range scaling: the deadband never exceeds this share of the range.</summary>
    private static readonly Fix128 DeadbandPerMetre = Fix128.FromDouble(0.1);
}
