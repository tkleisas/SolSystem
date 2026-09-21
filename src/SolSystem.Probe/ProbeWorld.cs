using SolSystem.Core.Local;
using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;

namespace SolSystem.Probe;

/// <summary>
/// The world a probe runs against: one system, two stations, one ship.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the smallest world that can answer a question about flying a ship. The
/// simulation is not built yet — there is no economy, no factions and no strategic layer —
/// so a probe host that pretended otherwise would be a mock of something that does not
/// exist. What does exist is the frame arithmetic, the ephemeris, the stations, the ship and
/// the docking envelope, and those are what a probe can exercise today.
/// </para>
/// <para>
/// Everything here runs on the core's own types with no floating point in the integration
/// path. That is the property the harness exists to check: two runs of the same script
/// produce byte-identical transcripts, and if they do not, something in the simulation is
/// reading a clock, a random number or a float, and it will do so in a player's game.
/// </para>
/// </remarks>
internal sealed class ProbeWorld
{
    /// <summary>The navigation tick the design fixes. 120 Hz, one tick per step.</summary>
    internal const double TickSeconds = 1.0 / 120.0;

    private readonly SolarSystem _system = new();
    private readonly Dictionary<string, Station> _stations = new(StringComparer.OrdinalIgnoreCase);

    internal ProbeWorld()
    {
        // Two stations around the Earth, as far apart in kind as two stations can be: a low
        // orbit at 6 778 km and one at geostationary radius. The phase 0 gate calls for two,
        // and they are here rather than one because a single station cannot show that the
        // frame is per-body and not global.
        // Fully qualified because `Station` is also the name of the accessor below, and a
        // method shadows a type of the same name inside the class that declares it.
        Add("Meridian", SolSystem.Core.Orbits.Station.InCircularOrbit(
            Ephemeris.Body.Earth, "Meridian", F(6_778_100.0),
            new Fix128Vec(Fix128.Zero, Fix128.Zero, F(20.0)),
            new Fix128Vec(Fix128.One, Fix128.Zero, Fix128.Zero)));

        Add("Anchorage", SolSystem.Core.Orbits.Station.InCircularOrbit(
            Ephemeris.Body.Earth, "Anchorage", F(42_164_000.0),
            Fix128Vec.Zero, new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.One)));
    }

    /// <summary>Seconds since J2000. The world's only notion of when.</summary>
    internal double Time => _system.SecondsFromJ2000;

    /// <summary>Ticks advanced since the world was created.</summary>
    internal long Ticks { get; private set; }

    /// <summary>The ship, if one has been launched.</summary>
    internal Ship? Ship { get; private set; }

    /// <summary>The name of the station the ship was launched from, if any.</summary>
    /// <remarks>
    /// The name and not the station. <see cref="Station"/> is a mutable struct, so holding
    /// one here would hold a copy taken at launch — and a copy does not move. Every range
    /// measured against it would then grow at the station's own orbital speed, which is
    /// thirty-eight kilometres in five seconds and looks exactly like a ship flying away.
    /// </remarks>
    internal string? HomeStationName { get; private set; }

    /// <summary>The station the ship was launched from, at the world's current instant.</summary>
    internal Station? HomeStation =>
        HomeStationName is null ? null : Station(HomeStationName);

    /// <summary>Where in its approach the ship is: 0 closing, 1 braking, 2 terminal.</summary>
    internal int Phase { get; private set; }

    /// <summary>Prints the pilot's decisions at intervals. Diagnostic only.</summary>
    internal static bool Debug;

    /// <summary>Turns the pilot off, so the ship only coasts. Diagnostic only.</summary>
    internal static bool Manual;

    private static Fix128 F(double value) => Fix128.FromDouble(value);

    private void Add(string name, Station station) => _stations[name] = station;

    /// <summary>A named station.</summary>
    internal Station Station(string name)
    {
        if (!_stations.TryGetValue(name, out Station station))
        {
            throw new ProbeException(
                $"no station called '{name}' — have {string.Join(", ", _stations.Keys)}");
        }

        return station;
    }

    internal IEnumerable<string> StationNames => _stations.Keys;

    /// <summary>A body's heliocentric state, in kilometres and km/s.</summary>
    internal Ephemeris.State BodyState(Ephemeris.Body body) => _system.Heliocentric(body);

    /// <summary>The Moon, heliocentric, composed from the Earth and its geocentric elements.</summary>
    internal Ephemeris.State MoonState() => _system.MoonHeliocentric();

    /// <summary>The Earth's GM in metres, for anything that needs the local frame's pull.</summary>
    internal static Fix128 EarthGmLocal => Constants.EarthGmLocal;

    /// <summary>
    /// Launches the ship a given distance down a station's corridor, closing slowly.
    /// </summary>
    /// <remarks>
    /// The ship starts with the station's own velocity, which is the whole point of the
    /// local frame: a ship released near a station does not inherit thirty kilometres a
    /// second of orbital motion unless it is given it, and a probe that forgot would watch
    /// its ship leave at the Earth's orbital speed.
    /// </remarks>
    internal void Launch(string stationName, double standoffMetres, double closingMetresPerSecond)
    {
        Station home = Station(stationName);

        DockingPort port = home.Port;
        Fix128Vec inward = -port.Axis;

        var position = port.Position + port.Axis * F(standoffMetres);

        // No launch velocity but the closing speed, because the frame is held.
        //
        // This is the counterpart to HoldStations and it is worth being explicit about,
        // because the obvious version is wrong in a way that takes a while to see. Giving the
        // ship the station's orbital velocity — 7 668 m/s of it — is right in an inertial
        // frame and wrong in this one: with the port held still, a ship carrying that
        // velocity simply leaves, at 7.7 kilometres a second, and the range grows quadratically
        // while every number the pilot prints stays perfectly sensible. The two choices have
        // to agree: either both are in orbit and the station is propagated, or the frame is
        // held and the ship starts at rest in it.
        var velocity = inward * F(closingMetresPerSecond);

        // Launched already pointing along the direction the pilot will ask for, which is the
        // corridor-normal. This is not a convenience and it took three attempts to see why.
        //
        // A ship at rest two kilometres above a station is not hovering — it is falling at
        // 8.7 m/s², and the engine makes 0.039. The pilot's law therefore aims the nose
        // *outward* to hold station, exactly as a landing rocket does, and a ship launched
        // nose-inward is ninety degrees away from that: the alignment gate keeps the engine
        // shut while the ship falls, by the time it can fire it is closing at 8.6 m/s, and no
        // controller recovers. The launch attitude has to agree with the control law.
        Fix128Vec wanted = port.Axis;
        Fix128 angle = Fix128.FromDouble(
            Math.Atan2(wanted.Y.ToDouble(), wanted.X.ToDouble()));

        Ship = new Ship(
            position,
            velocity,
            F(90.0),
            F(9.9322),
            Engine.Crewed(F(3.92), Engine.CrewedSpecificImpulse),
            new Attitude(new Fix128Vec(Fix128.Zero, Fix128.Zero, angle), Fix128Vec.Zero));

        HomeStationName = stationName;
        Phase = 0;
    }

    /// <summary>Advances the world by <paramref name="ticks"/> navigation ticks.</summary>
    /// <remarks>
    /// One tick at a time through the same path a played frame would take. A probe that
    /// advanced the world in one jump would be testing an integrator no player ever runs.
    /// </remarks>
    internal void Advance(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            Step();
            Ticks++;
        }
    }

    private void Step()
    {
        // The ephemeris is analytic, so the clock is the only thing the system needs.
        _system.Advance(TickSeconds);

        // Stations are held by default. See HoldStations for why, and for what this probe
        // therefore does not test.
        if (!HoldStations)
        {
            foreach (string name in _stations.Keys.ToList())
            {
                Station station = _stations[name];
                station.Step(F(TickSeconds));
                _stations[name] = station;
            }
        }

        if (Ship is Ship ship && HomeStationName is not null)
        {
            Station home = Station(HomeStationName);

            if (!Manual)
            {
                Fly(ref ship, home);
            }
            else
            {
                // Coasting: the pilot is off and the engine is shut, so the ship falls
                // purely under the station's gravity. Two identical bodies in the same
                // field have to stay in the same place relative to each other, so this is
                // how the frame arithmetic gets checked without a controller in the way.
                var sources = new[] { home.GravitySource };
                ship.Step(sources, F(TickSeconds), Command.Coast);
            }

            Ship = ship;
        }
    }

    /// <summary>
    /// Flies the phase-based approach from the docking tests, so a probe manoeuvre is the
    /// same manoeuvre the tests assert on.
    /// </summary>
    /// <remarks>
    /// Three phases, chosen by distance rather than by time so they cannot drift apart as
    /// the mass changes under the burn: close along the corridor, come about and brake, then
    /// creep the last few metres. The ship goes by <c>ref</c> because it is a mutable struct;
    /// passing it by value would write every burn to a copy and the probe would report a
    /// ship that never moved.
    /// </remarks>
    private void Fly(ref Ship ship, Station home)
    {
        DockingPort port = home.Port;
        Fix128Vec inward = -port.Axis;

        Fix128Vec offset = ship.Position - port.Position;
        double rangeM = offset.Length.ToDouble();
        double closingM = Dot(ship.Velocity, inward).ToDouble();

        // What the engine can actually do, and what the station is already doing to the ship.
        double accel = ship.Engine.ThrustKilonewtons.ToDouble() / ship.Mass.ToDouble();
        double ceiling = ship.Engine.MaxAccelerationInMetresPerSecondSquared.ToDouble();
        accel = Math.Min(accel, ceiling);

        // Nothing to cancel: the approach is flown in the station's own frame, so the host's
        // pull is already accounted for by both of them falling together. See GravitySources.
        double gravityAlong = 0.0;

        // The closing law, and it is deliberately not a proportional one.
        //
        // A proportional law asks for an acceleration proportional to the speed error, which
        // means that once the ship is at its target speed it asks for *zero* — and a ship
        // under zero thrust in a held frame coasts at whatever speed it had. The first version
        // of this settled at 0.98 m/s with an alignment of 0.899, one thousandth below the
        // throttle gate, and stayed there for a hundred thousand ticks: throttle shut,
        // attitude held, arriving never.
        //
        // What a pilot does instead is accelerate until the remaining distance equals the
        // distance needed to stop, then brake. Three times the stopping distance is the
        // margin, because the theoretical figure assumes full thrust pointed exactly
        // retrograde from the first instant and this ship has to turn round to brake at all.
        // Braking means pointing the engine the *other* way, and at the crewed rate of six
        // degrees a second that is thirty seconds of coming about during which no thrust is
        // possible. So the trigger is not "when the stopping distance runs out" — it is "when
        // the stopping distance plus the distance covered while turning round runs out". The
        // first version knew only the first half, flipped the ship at 464 m doing 6.3 m/s,
        // coasted outward for the whole thirty seconds of the turn, and ended up eighteen
        // kilometres away with the brake still not lit.
        double turnSeconds = Math.PI / (6.0 * Math.PI / 180.0);
        double accelAlong = accel;
        double stopping = closingM * closingM / (2.0 * accelAlong);
        double flipDistance = closingM * turnSeconds;

        if (Phase == 0 && (stopping + flipDistance >= rangeM || rangeM <= 3.0))
        {
            Phase = 1;
        }
        else if (Phase == 1 && closingM <= 0.05)
        {
            Phase = 2;
        }

        double alongAccel;
        if (Phase == 0)
        {
            alongAccel = accelAlong;
        }
        else if (Phase == 1)
        {
            alongAccel = -accelAlong;
        }
        else
        {
            // Terminal: hold a slow creep and let the latches do the rest.
            alongAccel = Math.Clamp((0.05 - closingM) * 0.5, -accelAlong, accelAlong);
        }

        // A little of the lateral error too, or the ship drifts off the corridor. Small
        // enough not to swing the nose: the throttle gate holds the engine shut below 0.9
        // alignment, so a correction that tilts the nose more than about twenty-five degrees
        // off the corridor locks the throttle out entirely.
        Fix128Vec lateral = offset - port.Axis * Dot(offset, port.Axis);
        double lateralSpeed = lateral.IsZero
            ? 0.0
            : Dot(ship.Velocity, lateral.Normalized()).ToDouble();
        double lateralAccel = Math.Clamp(-lateralSpeed / 4.0, -accel * 0.1, accel * 0.1);

        double wantedAlong = alongAccel - gravityAlong;
        var wantedVector = new Fix128Vec(
            inward.X * Fix128.FromDouble(wantedAlong),
            inward.Y * Fix128.FromDouble(wantedAlong),
            inward.Z * Fix128.FromDouble(wantedAlong));

        if (!lateral.IsZero && Math.Abs(lateralAccel) > 1e-9)
        {
            Fix128Vec lateralDirection = lateral.Normalized();
            wantedVector += new Fix128Vec(
                lateralDirection.X * Fix128.FromDouble(lateralAccel),
                lateralDirection.Y * Fix128.FromDouble(lateralAccel),
                lateralDirection.Z * Fix128.FromDouble(lateralAccel));
        }

        Fix128Vec turn = TurnTowards(ship.Attitude, wantedVector);
        double alignment = Dot(ship.Attitude.Forward, wantedVector.Normalized()).ToDouble();

        // Throttle is how much of the drive the required acceleration needs, with the
        // alignment as a gate so the engine does not fire while the ship is still coming
        // about. It is not an error term: the error is already in the direction.
        double needed = wantedVector.Length.ToDouble();
        Fix128 throttle = alignment > 0.9
            ? Fix128.FromDouble(Math.Clamp(needed / accel, 0.0, 1.0))
            : Fix128.Zero;

        if (Debug && Ticks % 1200 == 0)
        {
            Console.WriteLine($"  [pilot] t={Ticks,5} range={rangeM,9:F1} closing={closingM,8:F3} "
                + $"phase={Phase} aAlong={alongAccel,7:F4} gAlong={gravityAlong,8:F4} "
                + $"need={needed,7:F4} align={alignment,6:F3} throttle={throttle.ToDouble(),5:F3} "
                + $"wv=({wantedVector.X.ToDouble(),8:F4},{wantedVector.Y.ToDouble(),8:F4}) "
                + $"nose=({ship.Attitude.Forward.X.ToDouble(),7:F4},{ship.Attitude.Forward.Y.ToDouble(),7:F4}) "
                + $"turnZ={turn.Z.ToDouble(),9:F4} "
                + $"v=({ship.Velocity.X.ToDouble(),9:F3},{ship.Velocity.Y.ToDouble(),9:F3})");
        }

        ship.Step(GravitySources, F(TickSeconds), new Command(wantedVector, throttle, turn));
    }

    private static Fix128 Dot(Fix128Vec a, Fix128Vec b) =>
        a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>
    /// Holds the stations still in their own frame instead of propagating their orbits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the probe's honest boundary and it is worth stating plainly, because the
    /// alternative is a harness that reports success on something it cannot actually do.
    /// </para>
    /// <para>
    /// A station at 6 778 km orbits at 7 668 m/s. <b>A ship there cannot hover</b>: the Earth
    /// pulls at 8.67 m/s² and the reference torch makes 0.039, so the drive is 220 times too
    /// weak, and no drive in this setting is within two orders of magnitude. What keeps a ship
    /// beside a station is not thrust but that both are falling together — so a rendezvous is
    /// flown in the station's own frame, which is the frame the capture envelope is defined
    /// in and the frame the docking tests fly in.
    /// </para>
    /// <para>
    /// In that frame the station is fixed and the local manifold is flat, to within the tidal
    /// gradient (3.4 × 10⁻⁶ m/s² per metre of separation) and the Coriolis term at twice the
    /// orbital rate. Over a two-kilometre approach those are millimetres, and they are not
    /// modelled. <b>What this probe therefore does not test is orbital manoeuvring</b> —
    /// phasing, plane changes, or a rendezvous that has to match a station's orbit rather
    /// than its velocity. Those need the Hill frame and its tidal terms, which is real work
    /// and is not done.
    /// </para>
    /// </remarks>
    internal static bool HoldStations = true;

    /// <summary>An empty gravity field, with the source parked clear of the port.</summary>
    /// <remarks>
    /// <para>
    /// <b>Why there is no gravity in this probe.</b> The station orbits at 6 778 km, where the
    /// Earth pulls at 8.67 m/s² and the reference torch makes 0.039. A ship there cannot
    /// hold station at all — it is not a control problem, it is that the drive is 220 times
    /// too weak to hover, and no drive in this setting is within two orders of magnitude of
    /// being able to. What keeps a ship beside a station is not thrust but the fact that both
    /// of them are falling together.
    /// </para>
    /// <para>
    /// So the approach is flown in the station's own frame, which is the frame the docking
    /// envelope is defined in and the frame the tests fly. The residual terms that a
    /// co-rotating frame would carry — the tidal gradient, about 3.4 × 10⁻⁶ m/s² per metre of
    /// separation, and the Coriolis term at twice the orbital rate — are real and are not
    /// modelled here. Over a two-kilometre approach they amount to millimetres.
    /// </para>
    /// <para>
    /// The source is parked away from the port rather than removed, because the gravity
    /// machinery refuses to have a source sitting on the ship and a zero-mass source at the
    /// origin would throw the moment the ship arrived.
    /// </para>
    /// </remarks>
    private static readonly GravitySource[] GravitySources =
    {
        new(new Fix128Vec(Fix128.FromDouble(-1.0e6), Fix128.Zero, Fix128.Zero), Fix128.Zero),
    };

    /// <summary>
    /// Angular velocity that swings the ship's attitude until its nose points along
    /// <paramref name="to"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Worked from the attitude rather than from the angle between the nose and the target,
    /// and that is not a style choice. Two anti-parallel vectors have a zero cross product,
    /// so the obvious formulation tells a ship ordered to reverse not to turn — and a
    /// reversal is the most common manoeuvre in docking, because braking means turning
    /// around. A half turn is just the rotation vector that points the other way, and
    /// subtracting two attitudes has no degenerate case at all.
    /// </para>
    /// <para>
    /// The error is asked for the whole way round and then handed in, so the hull's own
    /// rate limit is what decides how fast the ship comes about. Scaling it down inside
    /// this function instead looks equivalent and is a trap: a half turn scaled by a gain
    /// is a slow turn, the alignment gate stays shut for the whole of it, and the engine
    /// never lights — the ship simply falls. The first version of this did exactly that and
    /// reported a probe flying away from its station at a kilometre a second.
    /// </para>
    /// </remarks>
    private static Fix128Vec TurnTowards(Attitude attitude, Fix128Vec to)
    {
        Fix128Vec unit = to.Normalized();
        if (unit.IsZero)
        {
            return Fix128Vec.Zero;
        }

        // The attitude that would point the nose along `to`: a rotation about z, since the
        // corridor and the ship are both in the ecliptic plane.
        double wanted = Math.Atan2(unit.Y.ToDouble(), unit.X.ToDouble());
        double current = attitude.RotationVector.Z.ToDouble();

        double error = wanted - current;
        while (error > Math.PI)
        {
            error -= 2.0 * Math.PI;
        }

        while (error <= -Math.PI)
        {
            error += 2.0 * Math.PI;
        }

        // The whole error, over a tenth of a second. ClampAngularVelocity then holds it
        // inside what a crew can take, which is where the limit belongs.
        double rate = error / 0.1;
        return new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.FromDouble(rate));
    }
}
