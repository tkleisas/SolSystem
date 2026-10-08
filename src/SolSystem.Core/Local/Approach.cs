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

    /// <summary>
    /// The range the rendezvous hands the corridor's profile its ship at, in metres.
    /// </summary>
    /// <remarks>
    /// The corridor's own launch convention: 400 m of standoff, the same figure a client
    /// session starts a player at. A profile built from a range the ship is already inside
    /// would be a slope the ship has to catch up with rather than give way to.
    /// </remarks>
    private static readonly Fix128 RendezvousHandoverRange = Fix128.FromDouble(450.0);

    /// <summary>
    /// The relative speed the ship must be under to enter the corridor, in m/s.
    /// </summary>
    /// <remarks>
    /// The pilot delivers the ship AT the corridor's own pace rather than at rest — at rest
    /// at a radial offset is not a held state in the Hil frame, and a law that aimed for it
    /// handed the profile a ship the orbital drift was already carrying away. The delivery
    /// rate is the profile's own initial figure for the hand-over range, so a hull arriving
    /// slightly above its own slope meets the brake with slope to spare; five metres a
    /// second is that with a margin too generous to be clever.
    /// </remarks>
    private static readonly Fix128 RendezvousHandoverSpeed = Fix128.FromDouble(5.0);

    /// <summary>
    /// The rendezvous's position gain, per metre of error, in m/s².
    /// </summary>
    /// <remarks>
    /// A closed-loop rate of about 0.004 s⁻¹ — a quarter hour's settle — and deliberately
    /// slow, and the reason is the Hill coupling rather than the torch. A full-order radial
    /// burn drove the ship inward at fourteen metres a second, and the along-track drift
    /// that motion generates (<c>−2nẋ</c>, half the torch at that rate) outran the
    /// along-track correction the saturated burn had left: the ship arrived at the mouth
    /// 245 m off the centreline and the profile had no line left to fly. A slow loop keeps
    /// the radial speed near a metre a second, and the drift it generates is one the
    /// along-track correction can actually cancel. At two kilometres of error the gain
    /// asks for 0.024 m/s² — the torch saturates only in the opening minutes, and the loop
    /// otherwise unwinds at the drive's own pace, which is the honest constraint.
    /// </remarks>
    private static readonly Fix128 RendezvousGainP = Fix128.FromDouble(1.5e-5);

    /// <summary>
    /// The rendezvous's rate gain, per m/s of velocity error, in m/s².
    /// </summary>
    /// <remarks>
    /// ζ ≈ 2.5 against the position gain — deliberately over-damped, and the reason is the
    /// runway. The plan's slope tightens as the ship closes, and a loop that merely matches
    /// the slope carries a tracking lag of 1/k_d of a second; at one point the ship arrived
    /// at the mouth at four hundred and fifty metres going six point three, with
    /// sqrt(2·a·114) = five hundred fourteen metres of stopping distance and a
    /// hundred-and-fourteen-metre runway, so it coasted through the mouth and the profile
    /// had no line to fly. The over-damped loop makes the ship's own velocity its own
    /// business and arrive at the slope's pace instead.
    /// </remarks>
    private static readonly Fix128 RendezvousGainD = Fix128.FromDouble(0.02);


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



    /// <summary>Whether the corridor's profile has been captured yet.</summary>
    private bool _haveProfile;

    /// <summary>
    /// Whether the rendezvous has delivered the ship to the corridor yet.
    /// </summary>
    /// <remarks>
    /// A field of the law's own state, not a decision remade per tick: the hand-over fires
    /// once, and after it the corridor's profile is rebuilt from the range the ship
    /// actually arrived at — a hull that reached the mouth four hundred metres out and one
    /// that limps in at eighty should not share a glideslope.
    /// </remarks>
    private bool _rendezvousComplete;

    /// <summary>The approach profile, built from the corridor the ship was launched down.</summary>
    private Glideslope _profile;

    /// <summary>
    /// The command for this tick.
    /// </summary>
    /// <param name="ship">The ship, read only. It is a struct, so pass it by value.</param>
    /// <param name="port">The port being approached.</param>
    /// <param name="portVelocity">
    /// The port's own velocity, at this instant. A docking law that measures closing against
    /// absolute speed reads a ship in perfect formation as approaching at close to the
    /// orbital speed, and never sees contact — the world's frame is Earth-centred, and both
    /// bodies carry 7 668 m/s of it. Held-frame callers pass zero, and the law is exactly the
    /// laws those tests proved.
    /// </param>
    /// <param name="frameGravity">
    /// Gravitational acceleration on the ship in this frame, if any — a real figure the ship's
    /// own integration has already applied, which the thrust cancels. In a station's own
    /// frame there is none — both are falling together — and so it is in the world's real
    /// frame too: the law cancels nothing, and the tidal pull left over is orders of
    /// magnitude below a torch.
    /// </param>
    internal Command Next(in Ship ship, DockingPort port, Fix128Vec portVelocity, Fix128Vec frameGravity,
        Fix128 meanMotion = default)
    {
        Fix128Vec inward = -port.Axis;
        Fix128Vec offset = ship.Position - port.Position;
        Fix128 range = offset.Length;
        Fix128 accel = DriveAcceleration(ship);

        // THE RENDEZVOUS: from the launch down to the corridor's mouth. In the world's real
        // frame the ship and the station are two bodies of one field, and the relative
        // dynamics between them are the Hill frame's own — an approach a torch cannot hour
        // out of the profile's shape. The law therefore grows a pilot that flies the Hill
        // dynamics instead of pretending they are not there, and the corridor's profile
        // starts where the rendezvous hands over, with the range the ship actually arrived
        // at. A mean motion of zero is the held frame: no station turning above a fixed
        // port, no Hill terms, and the law is exactly the laws the held-frame tests proved.
        if (meanMotion > Fix128.Zero && !_rendezvousComplete)
        {
            Fix128Vec relativeVelocity = ship.Velocity - portVelocity;
            Fix128 closingAtHandover = -Fix128Vec.Dot(relativeVelocity, port.Axis.Normalized());

            if (range <= RendezvousHandoverRange && closingAtHandover.Abs() < RendezvousHandoverSpeed)
            {
                // At the mouth, at rest: the ship and its corridor can now share one
                // profile. This is the pilot's whole verdict, and it is final.
                _rendezvousComplete = true;
                _profile = ProfileFor(range, accel);
                _haveProfile = true;
            }
            else
            {
                return RendezvousCommand(ship, port, portVelocity, meanMotion, accel);
            }
        }
        else if (!_haveProfile)
        {
            // Built from the corridor the ship actually starts down, so it is feasible by
            // construction: the rate is reduced until the drive can fly the line.
            Fix128 startRange = (ship.Position - port.Position).Length;
            _profile = ProfileFor(startRange, accel);
            _haveProfile = true;
        }

        // Closing rate, measured toward the port rather than along the axis, and measured
        // AGAINST THE PORT'S OWN MOTION. The two agree until the ship passes the port and
        // then they are opposites, and a law that throttles on one while steering by the
        // other runs away: a hundred kilometres of it, with "closing" reading a steady ten
        // metres a second. The relative part is the real frame's: two bodies sharing an
        // orbit share 7 668 m/s of it, and none of that is docking.
        Fix128Vec toPort = offset.IsZero ? inward : -offset.Normalized();
        Fix128 closing = Fix128Vec.Dot(ship.Velocity - portVelocity, toPort);

        Fix128 commanded = _profile.RateAt(range);

        // The latches have it: stop manoeuvring. Everything before this is trying to reach a state;
        // this is the state. Left flying, the law keeps correcting and a correction at a few
        // centimetres is a charge through the port and out the other side.
        if (Phase != Stage.Hold && Docking.Evaluate(ship, port, portVelocity).Contact)
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
            // The aim is the corridor. It is the direction the port faces, it is what the
            // envelope measures against — and in the real frame it turns with the orbit,
            // slow enough that the hull's own turn rate chases it without effort. What it
            // still never does is swing inside the last metres; the port's axis rotates at
            // the orbital rate, not at the rate a centimetre of lateral error moves it.
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
    /// When set, the rendezvous pilot emits one arithmetic trace per call, for the same
    /// reasons <c>--pilot-debug</c> exists on the probe world.
    /// </summary>
    internal static bool DumpPilot;

    /// <summary>
    /// The rendezvous pilot: one command, from the launch to the corridor's mouth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The physics it flies is the Hill dynamics — the relative motion of two bodies under
    /// one point field, measured in the rotating frame of the station's orbit. Written with
    /// the radial axis outward, the along-track the way the station itself moves, and the
    /// normal the orbit's pole, the relative accelerations are:
    /// </para>
    /// <para>
    /// <c>ẍ = 3n²x + 2nẏ + ax</c><br/>
    /// <c>ÿ = −2nẋ + ay</c><br/>
    /// <c>z̈ = −n²z + az</c>
    /// </para>
    /// <para>
    /// The law inverts them — that is the whole of the feedforward — so what remains for
    /// the feedback is three decoupled double integrators, on which a position gain and a
    /// rate gain behave the way an instrument panel says they do. The inversion is what
    /// makes the torch loop honest: the <c>3n²x</c> tidal term at two kilometres is 7.7e-3
    /// m/s² and the Coriolis at eight metres a second of relative motion is half the torch,
    /// and neither the position gain nor the rate gain would have survived them
    /// uncompensated — the pilot on the held frame's assumptions could not hold formation,
    /// and the launch the Kepler race took away is it.
    /// </para>
    /// </remarks>
    /// <param name="meanMotion">The station's mean orbital motion, radians per second.</param>
    private static Command RendezvousCommand(
        in Ship ship, DockingPort port, Fix128Vec portVelocity, Fix128 meanMotion, Fix128 accel)
    {
        // The Hill frame about the station. The port's axis is the radial direction in this
        // world's orbits — the corridor is carried around at nearly the same rate the
        // station turns — the normal is the orbit's pole, and the along-track is their
        // cross product, which is the direction the station itself is going.
        Fix128Vec radial = port.Axis.Normalized();
        Fix128Vec orbitNormal = new(Fix128.Zero, Fix128.Zero, Fix128.One);
        Fix128Vec alongTrack = Fix128Vec.Cross(orbitNormal, radial).Normalized();

        Fix128Vec relativePosition = ship.Position - port.Position;
        Fix128Vec relativeVelocity = ship.Velocity - portVelocity;

        // The delivery point: the corridor's mouth, 450 m out along the port's axis.
        Fix128Vec targetPosition = radial * RendezvousHandoverRange;

        // The delivery plan. The pilot does not chase one fixed figure — a coast with one
        // constant target builds speed the whole way and finishes at twelve metres a
        // second where the profile asked for four. The plan is the slope the drive can
        // honestly fly: the velocity the ship could own if it braked now and a half of its
        // error remains, bounded by the corridor's own ceiling. And the plan's direction is
        // THE SIGN OF THE ERROR: a ship that has fallen through the target radius is told
        // to climb back, not to keep its inward-bound plan — the first version here
        // hard-wired "always inward" and the ship, past the target, drew an orbital
        // oscillation around it instead of stopping.
        Fix128Vec offsetToTarget = targetPosition - relativePosition;
        Fix128 distanceToTarget = offsetToTarget.Length;
        Fix128 descentRate = Fix128.Sqrt(Fix128.FromWhole(2) * accel * distanceToTarget);
        descentRate = Fix128.Min(descentRate, CorridorRate);
        Fix128Vec targetVelocity = distanceToTarget == Fix128.Zero
            ? Fix128Vec.Zero
            : offsetToTarget * (descentRate / distanceToTarget);

        // The errors, decomposed onto the frame's axes.
        Fix128 ex = Fix128Vec.Dot(targetPosition - relativePosition, radial);
        Fix128 ey = Fix128Vec.Dot(targetPosition - relativePosition, alongTrack);
        Fix128 ez = Fix128Vec.Dot(targetPosition - relativePosition, orbitNormal);
        Fix128 evx = Fix128Vec.Dot(targetVelocity - relativeVelocity, radial);
        Fix128 evy = Fix128Vec.Dot(targetVelocity - relativeVelocity, alongTrack);
        Fix128 evz = Fix128Vec.Dot(targetVelocity - relativeVelocity, orbitNormal);

        // The relative position and velocity, decomposed, for the dynamics' own terms.
        Fix128 px = Fix128Vec.Dot(relativePosition, radial);
        Fix128 py = Fix128Vec.Dot(relativePosition, alongTrack);
        Fix128 pz = Fix128Vec.Dot(relativePosition, orbitNormal);
        Fix128 vx = Fix128Vec.Dot(relativeVelocity, radial);
        Fix128 vy = Fix128Vec.Dot(relativeVelocity, alongTrack);
        Fix128 vz = Fix128Vec.Dot(relativeVelocity, orbitNormal);

        // The wanted accelerations, then the dynamics' terms cancelled out of them.
        Fix128 desiredAx = RendezvousGainP * ex + RendezvousGainD * evx;
        Fix128 desiredAy = RendezvousGainP * ey + RendezvousGainD * evy;
        Fix128 desiredAz = RendezvousGainP * ez + RendezvousGainD * evz;

        Fix128 ux = desiredAx - (Fix128.FromWhole(3) * meanMotion * meanMotion * px) - (Fix128.FromWhole(2) * meanMotion * vy);
        Fix128 uy = desiredAy + (Fix128.FromWhole(2) * meanMotion * vx);
        Fix128 uz = desiredAz + (meanMotion * meanMotion * pz);

        // Reassemble onto the world's axes, then ask for it as a thrust the hull can make.
        Fix128Vec wanted = radial * ux + alongTrack * uy + orbitNormal * uz;
        Fix128 magnitude = wanted.Length;

        if (DumpPilot)
        {
            Console.WriteLine(
                $"  [pilot] e=({ex.ToDouble():F1},{ey.ToDouble():F1},{ez.ToDouble():F3}) "
                + $"ev=({evx.ToDouble():F3},{evy.ToDouble():F3},{evz.ToDouble():F3}) "
                + $"u=({ux.ToDouble():F5},{uy.ToDouble():F5},{uz.ToDouble():F5}) "
                + $"basis=({radial.X.ToDouble():F3},{radial.Y.ToDouble():F3})");
        }

        Fix128Vec turn;
        Fix128 throttle;
        if (magnitude == Fix128.Zero)
        {
            // Nothing to burn yet, and still a nose to hold: the corridor's law takes over
            // later and wants a nose-first hull.
            turn = TurnTowards(ship.Attitude, -port.Axis);
            throttle = Fix128.Zero;
            return new Command(Fix128Vec.Zero, throttle, turn);
        }

        Fix128Vec direction = wanted * (Fix128.One / magnitude);
        Fix128 demand = accel > Fix128.Zero
            ? Fix128.Clamp(magnitude / accel, Fix128.Zero, Fix128.One)
            : Fix128.Zero;

        // The throttle is gated on the nose pointing where it thrusts, the way the corridor's
        // own gates are: a misaligned burn is an unplanned vector and the engine's place on
        // the hull does not allow steering around it.
        Fix128 alignment = Fix128Vec.Dot(ship.Attitude.Forward, direction);
        Fix128 required = FiringAtIdle + ((FiringAtFullThrottle - FiringAtIdle) * demand);

        turn = TurnTowards(ship.Attitude, direction);
        throttle = alignment > required ? demand : Fix128.Zero;

        return new Command(direction, throttle, turn);
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
