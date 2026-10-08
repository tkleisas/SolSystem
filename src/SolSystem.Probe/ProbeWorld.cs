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
    internal const double TickSeconds = Constants.NavigationTickSeconds;

    private readonly SolarSystem _system = new();
    private readonly Dictionary<string, Station> _stations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The guidance law, kept across ticks. Its phase latch is the whole point.</summary>
    private Approach _approach;

    internal ProbeWorld()
    {
        // Two stations around the Earth, as far apart in kind as two stations can be: a low
        // orbit at 6 778 km and one at geostationary radius. The phase 0 gate calls for two,
        // and they are here rather than one because a single station cannot show that the
        // frame is per-body and not global.
        // Fully qualified because `Station` is also the name of the accessor below, and a
        // method shadows a type of the same name inside the class that declares it.
        Add("Meridian", SolSystem.Core.Orbits.Station.Meridian());

        Add("Anchorage", SolSystem.Core.Orbits.Station.InCircularOrbit(
            Ephemeris.Body.Earth, "Anchorage", F(42_164_000.0),
            Fix128Vec.Zero, new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.One)));
    }

    /// <summary>Seconds since J2000. The world's only notion of when, exact to the tick.</summary>
    internal Fix128 Time => _system.SecondsFromJ2000;

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

    /// <summary>Where in its approach the ship is: 0 closing, 1 braking, 2 terminal, 3 hold.</summary>
    internal int Phase { get; private set; }

    /// <summary>Prints the pilot's decisions at intervals. Diagnostic only.</summary>
    internal static bool Debug;

    /// <summary>Turns the pilot off, so the ship only coasts. Diagnostic only.</summary>
    internal static bool Manual;

    private static Fix128 F(double value) => Fix128.FromDouble(value);

    /// <summary>One navigation tick, precomputed once: the step the world advances by.</summary>
    private static readonly Fix128 Tick = F(TickSeconds);

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

        // THE SHIP LAUNCHES WITH THE STATION'S ORBITAL VELOCITY, plus the closing it was
        // asked for. In the world's real frame — the client's frame, Earth-centred, both
        // bodies under the same point field — there is no such thing as a ship at rest
        // beside a station: at 6 778 km the local speed is 7 668 m/s, and a launched ship
        // that was handed anything else is not rendezvousing, it is being left behind at
        // that rate. The closing speed rides on top of the shared orbital velocity, which
        // is also exactly what holds formation in the client: two bodies with the same
        // state, in the same field, stay together to the tidal terms.
        var velocity = home.Velocity + (inward * F(closingMetresPerSecond));

        // Launched pointing at the port, the way the docking tests launch. This is not a
        // convenience and it took an investigation to see why.
        //
        // The nose used to point OUTWARD, for a reason that had already left the world. When
        // this probe still modelled gravity, a ship at rest two kilometres above a station was
        // not hovering — it was falling at 8.7 m/s², and the law aimed the nose outward to hold
        // station, exactly as a landing rocket does. The gravity went when the stations were
        // held (see HoldStations), and the outward nose stayed: a 180-degree turn before
        // anything else, whose throttle-gate leakage leaves half a metre of lateral error at
        // the port plane. The contact sphere is eighty centimetres across, so the ship missed
        // by nine centimetres, sailed through, and the thrust-only creep could not come back —
        // the transcript showed a ship that never docked, receding at the handover rate, while
        // every check stayed green. The launch attitude has to agree with the control law, and
        // the law flies nose-first down the corridor.
        Fix128Vec wanted = -port.Axis;
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
        _system.Advance(Tick);

        // THE LAW AND THE SHIP FLY ON ONE INSTANT. The port's state is read BEFORE the
        // stations move, so the law sees ship and port at the same tick. The held frame
        // hid the price of getting this wrong: the port moves 64 m per tick in the real
        // frame, and a law fed the port of tick t+1 and the ship of tick t reads a corridor
        // whose direction swings by forty degrees every step. Stations then step; the
        // ship's step uses the host's timeless field. At the end of the tick, ship and
        // stations agree on the instant; at the next, the law reads that instant twice.
        if (Ship is Ship ship && HomeStationName is not null)
        {
            Station home = Station(HomeStationName);
            var sources = new[] { home.GravitySource };

            if (!Manual)
            {
                Fly(ref ship, home);
            }
            else
            {
                // Coasting: the pilot is off and the engine is shut, so the ship falls
                // purely under the host's gravity — the same field the station falls
                // under, which is what makes this the honest frame: two identical
                // bodies in one field have to stay in formation.
                ship.Step(sources, F(TickSeconds), Command.Coast);
            }

            // THE LATCH. Contact made the law stop flying; contact is also the moment the
            // ship is mechanically the station's. A "docked" hull whose own orbit keeps
            // integrating is not docked — it is in formation, and the Hill dynamics of a
            // contact-scale residual + the phase race take it away over minutes, which is
            // what the first real-frame run did: contact, then eighty metres and falling.
            // Latched means kinematic: the ship's position and velocity are the port's,
            // every tick, until something undocks. No elasticity, no resonance — a hard
            // clamp worth naming as that.
            if (Phase == (int)SolSystem.Core.Local.Approach.Stage.Hold
                && HomeStationName is not null)
            {
                Station latched = Station(HomeStationName);
                ship = new Ship(latched.Port.Position, latched.Velocity,
                    ship.DryMass, ship.Propellant, ship.Engine, ship.Attitude);
            }

            Ship = ship;
        }

        // Stations orbit their host, uncapped, exactly as the client's session advances
        // them: every tick, under the same point field the ship feels. A probe whose
        // stations did not move would be testing a frame no player flies — and its ship,
        // left behind at the station's own orbital speed, would narrate the distance
        // growing while every instrument read green.
        foreach (string name in _stations.Keys.ToList())
        {
            Station station = _stations[name];
            station.Step(F(TickSeconds));
            _stations[name] = station;
        }
    }

    /// <summary>
    /// Flies the phase-based approach from the docking tests, so a probe manoeuvre is the
    /// same manoeuvre the tests assert on.
    /// </summary>
    /// <remarks>
    /// Three phases, chosen by distance rather than by time so they cannot drift apart as
    /// the mass changes under the burn: close along the corridor, come about and brake, then
    /// creep the last few metres — plus the hold, entered at contact, which is the law leaving
    /// the ship to the latches. The ship goes by <c>ref</c> because it is a mutable struct;
    /// passing it by value would write every burn to a copy and the probe would report a
    /// ship that never moved.
    /// </remarks>
    private void Fly(ref Ship ship, Station home)
    {
        // The law lives in the core now. The probe used to carry its own copy, and that copy
        // was written four times because a probe is the worst place to develop a controller:
        // every fix had to be re-derived without tests. What is left here is the frame
        // decision, which is the probe's business, and nothing else. The real frame hands
        // the law the port's own velocity and nothing to cancel: two bodies sharing an orbit
        // are falling together, and the tidal residue is below any torch's resolution. The
        // station's mean motion rides along, which is what tells the law the Hill frame's
        // terms and turns the rendezvous pilot on.
        Command command = _approach.Next(ship, home.Port, home.Velocity, Fix128Vec.Zero,
            home.OrbitalRate);
        Phase = (int)_approach.Phase;

        if (Debug && (Ticks < 60 || Ticks % 10000 == 0))
        {
            DockingReport report = Docking.Evaluate(ship, home.Port, home.Velocity);
            Fix128Vec nose = ship.Attitude.Forward;
            Fix128Vec radialB = home.Port.Axis.Normalized();
            Fix128Vec normalB = new(Fix128.Zero, Fix128.Zero, Fix128.One);
            Fix128Vec alongB = SolSystem.Core.Numerics.Fix128Vec.Cross(normalB, radialB).Normalized();
            Fix128Vec rp = ship.Position - home.Port.Position;
            Fix128Vec rv = ship.Velocity - home.Velocity;
            Console.WriteLine($"  [law] t={Ticks,7} phase={Phase} "
                + $"hill p=({SolSystem.Core.Numerics.Fix128Vec.Dot(rp, radialB).ToDouble():F1},"
                + $"{SolSystem.Core.Numerics.Fix128Vec.Dot(rp, alongB).ToDouble():F1},"
                + $"{SolSystem.Core.Numerics.Fix128Vec.Dot(rp, normalB).ToDouble():F3}) "
                + $"v=({SolSystem.Core.Numerics.Fix128Vec.Dot(rv, radialB).ToDouble():F3},"
                + $"{SolSystem.Core.Numerics.Fix128Vec.Dot(rv, alongB).ToDouble():F3},"
                + $"{SolSystem.Core.Numerics.Fix128Vec.Dot(rv, normalB).ToDouble():F3}) "
                + $"dir=({command.ThrustDirection.X.ToDouble():F3},{command.ThrustDirection.Y.ToDouble():F3}) "
                + $"nose={nose.X.ToDouble():F3},{nose.Y.ToDouble():F3}");
        }

        if (Debug && Ticks % 24000 == 0)
        {
            DockingReport report = Docking.Evaluate(ship, home.Port, home.Velocity);
            Console.WriteLine($"  [pilot] t={Ticks,7} phase={_approach.Phase,-8} "
                + $"range={report.Range.ToDouble(),10:F2} closing={report.ClosingSpeed.ToDouble(),9:F4} "
                + $"thr={command.Throttle.ToDouble():F3} prop={ship.Propellant.ToDouble():F6}");
        }

        // Real gravity: the host's point field at the frame's origin, the same source the
        // station integrates under and the same one the client's loop hands its hull. The
        // parked zero-mass source of the held-frame era is retired; a probe that felt no
        // gravity was not flying the world the player flies.
        ship.Step(new[] { home.GravitySource }, F(TickSeconds), command);
    }

    /// <summary>
    /// Legacy switch: whether the stations are held still in their own frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The held frame is retired.</b> It was the probe's honest boundary once, and was
    /// stated plainly: a station at 6 778 km orbits at 7 668 m/s, the reference torch makes
    /// 0.039, and no ship hovers there — what keeps it beside a station is that both are
    /// falling together. The held frame approximated that by freezing the station, which is
    /// a frame no player flies and a rendezvous no player can perform.
    /// </para>
    /// <para>
    /// The real frame needs no freezing: Earth-centred, both bodies under the same point
    /// field, falling together for real. The residual terms the held frame refused to model
    /// are now just present at their true (tiny) size, and what the probe tests is the
    /// manoeuvre the player performs. The switch remains for an A/B against the old frame;
    /// nothing but history flies behind it.
    /// </para>
    /// </remarks>
    internal static bool HoldStations = false;

    /// <summary>
    /// Angular velocity that swings the ship's attitude until its nose points along
    /// <paramref name="to"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DELETED. This was the probe's private copy of the helm's turn logic, written before the
    /// law moved to <see cref="Approach"/>, and it had been dead since: nothing called it.
    /// The live version is <see cref="Approach.TurnTowards"/>, which this predated by four
    /// rewrites and whose history the comments above are part of. Deleted in the audit's dead
    /// batch; the remarks are kept for the archaeology.
    /// </para>
    /// </remarks>
}
