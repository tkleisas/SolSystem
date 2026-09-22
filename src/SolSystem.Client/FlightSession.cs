using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;
using SolSystem.Core.Sky;

namespace SolSystem.Client;

/// <summary>
/// One flying session: where the ship is, which way it is looking, and what is in the sky.
/// </summary>
/// <remarks>
/// <para>
/// The client owns no physics. Everything here comes from <c>SolSystem.Core</c> — the ephemeris,
/// the frame arithmetic, the star catalogue, the sky projection — and this type is the layer that
/// answers the two questions a renderer asks: <em>where is the camera</em> and <em>what direction
/// is that pixel</em>.
/// </para>
/// <para>
/// The frame is the J2000 ecliptic, in kilometres, which is the frame the planets come out of the
/// ephemeris in and the frame the star catalogue was rotated into at load. A renderer that has to
/// convert between frames per object is a renderer with a bug in it waiting to happen, so there is
/// exactly one frame and everything is in it.
/// </para>
/// </remarks>
internal sealed class FlightSession
{
    private readonly StarCatalogue _stars;

    private Station _station;

    /// <summary>
    /// Where the observer sits relative to the station's port, and how fast.
    /// </summary>
    /// <remarks>
    /// Held as an offset rather than as an absolute position, and that is what makes the clock
    /// work: the Earth moves 30 km every second, so an observer whose absolute position was fixed
    /// would be left behind by its own planet within a frame or two.
    /// </remarks>
    private Fix128Vec _localOffset;

    private Fix128Vec _localVelocity;

    /// <summary>One metre in kilometres. The scale between the local frame and the solar frame.</summary>
    private static readonly Fix128 MetresToKilometres = Fix128.FromDouble(0.001);

    /// <summary>The ship's tick, which is also the station's — see <see cref="Advance"/>.</summary>
    private const double TickSeconds = 1.0 / 120.0;

    private FlightSession(StarCatalogue stars, double julianDate, Station station)
    {
        _stars = stars;
        JulianDate = julianDate;
        _station = station;
    }

    /// <summary>The stars, in the ecliptic frame.</summary>
    internal ReadOnlySpan<Star> Stars => _stars.Stars;

    /// <summary>The clock, as a Julian date.</summary>
    internal double JulianDate { get; private set; }

    /// <summary>The station being approached.</summary>
    internal Station Station => _station;

    /// <summary>
    /// Where the observer is, heliocentric, in kilometres.
    /// </summary>
    /// <remarks>
    /// The Earth's centre plus the station's offset from it plus the ship's offset from the station.
    /// The station's offset matters — it is 6 778 km, which is a hundredth of an Earth radius and a
    /// measurable parallax against the Moon — and the ship's does not, but it costs nothing to be
    /// consistent about it.
    /// </remarks>
    internal Fix128Vec ObserverPosition { get; private set; }

    /// <summary>The observer's velocity, heliocentric, km/s.</summary>
    internal Fix128Vec ObserverVelocity { get; private set; }

    /// <summary>The sky observer, rebuilt whenever the position or the time changes.</summary>
    internal SkyObserver Observer { get; private set; }

    /// <summary>Which way is up on screen.</summary>
    internal Fix128Vec Up { get; private set; }

    /// <summary>Loads a session: stars, clock, and a station in orbit round the Earth.</summary>
    internal static FlightSession Start(LaunchOptions options)
    {
        StarCatalogue stars = StarCatalogue.Load(Path.Combine(
            RepositoryRoot(), "art", "sky", "stars.bin"));

        var system = new SolarSystem();
        system.SetTime(Fix128.FromDouble((options.JulianDate - Ephemeris.J2000JulianDate) * 86400.0));

        Ephemeris.State earth = system.Heliocentric(Ephemeris.Body.Earth);

        // The corridor runs along +x in the station's local frame, so a ship sitting on it is at
        // station + axis·d and closes by travelling against the axis. Same convention as the
        // docking law, because it is the same corridor.
        var station = Station.InCircularOrbit(
            Ephemeris.Body.Earth,
            options.Station,
            Fix128.FromDouble(6_778_100.0),
            Fix128Vec.Zero,
            new Fix128Vec(Fix128.One, Fix128.Zero, Fix128.Zero));

        var session = new FlightSession(stars, options.JulianDate, station);
        session._localOffset = station.Port.Axis * Fix128.FromDouble(options.Standoff);
        session._localVelocity = Fix128Vec.Zero;

        session.Recompose(earth);
        session.AimAt(options.Aim, earth);
        return session;
    }

    /// <summary>
    /// Rebuilds the absolute observer state from the Earth and the local offset.
    /// </summary>
    private void Recompose(Ephemeris.State earth)
    {
        // The two frames have different units and joining them is where a client goes wrong.
        //
        // The solar frame is KILOMETRES: the Earth is 1.5 x 10^8 of them from the Sun. The local
        // frame is METRES, because a docking corridor four hundred metres long is not a useful
        // quantity in kilometres. So the station's offset and velocity are scaled here, once, at the
        // one place the two frames meet — and the first version of this did not, which put the
        // observer six point eight million kilometres from the Earth instead of six thousand eight
        // hundred, and drew the Earth as a distant dot.
        ObserverPosition = earth.Position + (Station.Offset * MetresToKilometres)
            + (_localOffset * MetresToKilometres);

        ObserverVelocity = earth.Velocity + (Station.Velocity * MetresToKilometres) + _localVelocity;

        // In free space there is no horizon, so the sky observer carries a position and a velocity
        // for parallax and aberration and nothing else. The horizon triad belongs to a surface
        // observer, which this is not.
        Observer = SkyObserver.InSpace(ObserverPosition, ObserverVelocity);
    }

    /// <summary>
    /// Points the camera at whichever interesting thing was asked for.
    /// </summary>
    /// <remarks>
    /// The Sun and the Earth are opposite each other from a station in low Earth orbit — the Sun is
    /// 1 AU one way and the Earth is 6 778 km the other — so the two aims are about 180 degrees
    /// apart and one of them is always behind you.
    /// </remarks>
    private void AimAt(LaunchOptions.ViewAim aim, Ephemeris.State earth)
    {
        _ = earth;

        Fix128Vec target = aim switch
        {
            // The Sun is at the origin of the heliocentric frame, so the direction to it is simply
            // the reverse of where we are.
            LaunchOptions.ViewAim.Sun => -ObserverPosition,

            // The Earth's centre is directly below the station, so the direction to it is the
            // reverse of the station's offset — NOT anything involving the Earth's heliocentric
            // position, which is 150 million kilometres away and points at the Sun's antipode. The
            // first version of this aimed at the anti-sun and showed an empty patch of sky.
            LaunchOptions.ViewAim.Earth => -(Station.Offset + _localOffset),

            // The galactic centre, which is the brightest part of the band and in Sagittarius.
            LaunchOptions.ViewAim.MilkyWay => Frames.EquatorialToEcliptic(
                Frames.FromEquatorialDegrees(266.4051, -28.936175), Frames.ObliquityJ2000Degrees),

            _ => -Station.Port.Axis,
        };

        Look(target.Normalized(), PreferredUp());
    }

    /// <summary>
    /// Sets the camera direction, with an up vector that keeps the horizon level.
    /// </summary>
    /// <remarks>
    /// The ecliptic pole is the natural "up" for a solar-system view — it is the direction the
    /// planets orbit about, and it makes the sky rotate about the vertical as the clock runs, which
    /// is what a viewer expects. Looking straight along the pole is the degenerate case and is
    /// handled by picking another axis rather than by producing a NaN.
    /// </remarks>
    private void Look(Fix128Vec forward, Fix128Vec up)
    {
        Fix128Vec unit = forward.Normalized();
        Fix128Vec pole = up.Normalized();

        Fix128Vec right = Fix128Vec.Cross(unit, pole);
        if (right.Length < Fix128.FromDouble(1e-6))
        {
            right = Fix128Vec.Cross(unit, new Fix128Vec(Fix128.Zero, Fix128.One, Fix128.Zero));
        }

        // The session's own aim direction used to be published here as Forward. Nothing read
        // it: the camera is built from the ship's attitude every frame, and the last renderer
        // consumer went in the audit's first batch. The property is gone; what remains is the
        // up vector, which the sky observer's triad is built around.
        Up = Fix128Vec.Cross(right.Normalized(), unit).Normalized();
    }

    private static Fix128Vec PreferredUp() => new(Fix128.Zero, Fix128.Zero, Fix128.One);

    /// <summary>
    /// The apparent direction of a star, with parallax and aberration applied.
    /// </summary>
    internal Fix128Vec Apparent(in Star star) => SkyProjection.Apparent(star, Observer);

    /// <summary>
    /// Advances the clock and carries the observer with it.
    /// </summary>
    /// <remarks>
    /// The station is stepped with the same symplectic integrator and the same tick policy as the
    /// ship, so the two hold formation: both orbit under the host's gravity and only their honest
    /// relative dynamics separate them. The earlier version re-pinned the station at its starting
    /// offset on every call — while its own comment claimed it was being propagated — so the ship,
    /// which integrates real gravity, left the station behind at orbital speed: 38 km in five
    /// seconds, which is 7.7 km/s exactly. A station you cannot stay beside is not a station you
    /// can dock at, and no instrument on the HUD makes that fun.
    /// </remarks>
    internal void Advance(double seconds)
    {
        if (seconds <= 0.0)
        {
            return;
        }

        JulianDate += seconds / 86400.0;

        // The same tick count the ship's stepper computes from the same elapsed seconds, so the
        // two advance in lockstep — including the cap, under which station and hull fall behind
        // the clock together at extreme time compression rather than apart from each other.
        int ticks = Math.Clamp((int)Math.Round(seconds / TickSeconds), 0, 240);
        Fix128 dt = Fix128.FromDouble(TickSeconds);
        for (int i = 0; i < ticks; i++)
        {
            _station.Step(dt);
        }

        var system = new SolarSystem();
        system.SetTime(Fix128.FromDouble((JulianDate - Ephemeris.J2000JulianDate) * 86400.0));
        Ephemeris.State earth = system.Heliocentric(Ephemeris.Body.Earth);

        // What moves most is the Earth, and the observer rides it — which is the whole point,
        // because over an orbit the sky turns and the Sun comes round, and a session left running
        // for an hour should show it.
        Recompose(earth);
    }

    /// <summary>
    /// Where the observer sits relative to the station's port, in metres.
    /// </summary>
    /// <remarks>
    /// The local frame, and the frame the player's ship flies in. Exposed so that a client can drive
    /// the observer from the ship rather than the other way round — which is the way it has to be if
    /// the camera is going to be inside the thing being flown.
    /// </remarks>
    internal Fix128Vec LocalOffset => _localOffset;

    /// <summary>Moves the observer within the station's local frame, in metres.</summary>
    internal void SetLocalOffset(Fix128Vec offset, Fix128Vec velocity, Ephemeris.State earth)
    {
        _localOffset = offset;
        _localVelocity = velocity;
        Recompose(earth);
    }

    /// <summary>The Earth's state at the session's current time.</summary>
    internal Ephemeris.State Earth()
    {
        var system = new SolarSystem();
        system.SetTime(Fix128.FromDouble((JulianDate - Ephemeris.J2000JulianDate) * 86400.0));
        return system.Heliocentric(Ephemeris.Body.Earth);
    }

    /// <summary>The direction from the observer to a heliocentric point, as a unit vector.</summary>
    internal Fix128Vec DirectionTo(Fix128Vec heliocentricPoint)
    {
        Fix128Vec offset = heliocentricPoint - ObserverPosition;

        // A point coincident with the observer has no direction; Up is the arbitrary-but-valid
        // answer, and it replaced the spawn-aim fallback when that property was deleted.
        return offset.IsZero ? Up : offset.Normalized();
    }

    /// <summary>The distance from the observer to a heliocentric point, in kilometres.</summary>
    internal double DistanceTo(Fix128Vec heliocentricPoint) =>
        (heliocentricPoint - ObserverPosition).Length.ToDouble();

    /// <summary>The repository root, found by walking up for the art directory.</summary>
    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "art", "sky")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"could not find the repository root above {AppContext.BaseDirectory}");
    }
}
