using SolSystem.Core.Local;
using SolSystem.Core.Numerics;

namespace SolSystem.Core.Orbits;

/// <summary>
/// A station: a mass with a docking port, in orbit around a body.
/// </summary>
/// <remarks>
/// <para>
/// A station lives in a body's <b>local frame</b> — metres, with the body's centre at the
/// origin — and that is not an arbitrary choice. A ship under thrust moves at metres per
/// second squared, and at 120 Hz one tick is 7 x 10⁻⁵ m. Put that ship in heliocentric
/// coordinates, where Earth's position is 1.5 x 10¹¹ m, and every tick's motion is thirty
/// orders of magnitude below the grid: the ship does not move at all. The frame has to be
/// anchored to whatever the ship is near, which is why this type exists rather than a station
/// being a heliocentric position with a name attached.
/// </para>
/// <para>
/// The frame is inertial over the timescales the action layer cares about. A planet does
/// accelerate as it orbits the Sun, but at 6 mm/s², so over a docking approach the error is
/// below the station's own structure. Adding that acceleration is a change to
/// <see cref="Step"/> and nothing else.
/// </para>
/// </remarks>
internal struct Station
{
    /// <summary>The body this station orbits.</summary>
    internal Ephemeris.Body Host;

    /// <summary>Name. Content, not logic, and it is what a contract will refer to.</summary>
    internal string Name;

    /// <summary>Offset from the host's centre, in metres.</summary>
    internal Fix128Vec Offset;

    /// <summary>Velocity relative to the host, in metres per second.</summary>
    internal Fix128Vec Velocity;

    /// <summary>The host's gravitational parameter, in m³/s².</summary>
    internal Fix128 GmMetres;

    /// <summary>Where the docking port is, relative to the station's centre.</summary>
    internal Fix128Vec PortOffset;

    /// <summary>Which way the port faces: the corridor an arriving ship travels down.</summary>
    internal Fix128Vec PortAxis;

    internal Station(
        Ephemeris.Body host,
        string name,
        Fix128Vec offset,
        Fix128Vec velocity,
        Fix128 gmMetres,
        Fix128Vec portOffset,
        Fix128Vec portAxis)
    {
        Host = host;
        Name = name;
        Offset = offset;
        Velocity = velocity;
        GmMetres = gmMetres;
        PortOffset = portOffset;
        PortAxis = portAxis;
    }

    /// <summary>
    /// Meridian, the Earth-orbit station, in its circular orbit with the harbour facing +x.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Low Earth orbit at 6 778.1 km — about 400 km up — with the port on the station's own axis
    /// and the corridor running along +x in the local frame, the same convention the docking law
    /// flies. Every caller that wants the Earth station builds it here, so the orbit, the port
    /// and the corridor have one definition; a test that wants a different port offset makes its
    /// own station and says why.
    /// </para>
    /// <para>
    /// <paramref name="name"/> relabels the site for a caller that started beside it under a
    /// different name; the orbit is Meridian's whatever it is called. What the station <em>is</em>
    /// — a 2 km wheel turning at 0.95 rpm — is the model's business, not this record's. See
    /// <c>docs/SETTING.md</c> §7 for the setting.
    /// </para>
    /// </remarks>
    internal static Station Meridian(string name = "Meridian") => InCircularOrbit(
        Ephemeris.Body.Earth,
        name,
        Fix128.FromDouble(6_778_100.0),
        Fix128Vec.Zero,
        new Fix128Vec(Fix128.One, Fix128.Zero, Fix128.Zero));

    /// <summary>
    /// A station in a circular orbit of radius <paramref name="radiusMetres"/> about its host.
    /// </summary>
    /// <remarks>
    /// The speed is <c>sqrt(GM/r)</c> and the velocity is perpendicular to the radius, which is
    /// the definition of a circular orbit. Getting the direction wrong is one of the two ways
    /// to make a station that looks right and is not: the other is a speed wrong by
    /// <c>sqrt(2)</c>, which is escape rather than orbit.
    /// </remarks>
    internal static Station InCircularOrbit(
        Ephemeris.Body host,
        string name,
        Fix128 radiusMetres,
        Fix128Vec portOffset,
        Fix128Vec portAxis)
    {
        // The body table carries GM in km³/s², the solar frame's unit. A station flies in the
        // local frame, metres, so the parameter comes across as km³ × 10⁹ m³/km³ before the
        // speed is taken. The factor is named rather than inlined because it is the same
        // conversion the local-frame constants in <see cref="Constants"/> were built from.
        const double CubicMetresPerCubicKilometre = 1_000_000_000.0;

        SolarSystem.Body body = SolarSystem.BodyOf(host);
        Fix128 gm = Fix128.FromDouble(body.GmKm * CubicMetresPerCubicKilometre);

        Fix128 speed = Fix128.Sqrt(gm / radiusMetres);

        return new Station(
            host,
            name,
            new Fix128Vec(radiusMetres, Fix128.Zero, Fix128.Zero),
            new Fix128Vec(Fix128.Zero, speed, Fix128.Zero),
            gm,
            portOffset,
            portAxis.Normalized());
    }

    /// <summary>The station's centre, relative to the host.</summary>
    internal readonly Fix128Vec Position => Offset;

    /// <summary>Advances the station one tick under its host's gravity.</summary>
    /// <remarks>
    /// Velocity Verlet, the same integrator the ship and the planets use, and for the same
    /// reason: it is symplectic, so a station left alone stays in its orbit instead of slowly
    /// spiralling in or out.
    /// </remarks>
    internal void Step(Fix128 dt)
    {
        Fix128 halfDt = dt * Fix128.Half;

        Fix128Vec acceleration = GravityAt(Offset);
        Offset += Velocity * dt + acceleration * (halfDt * dt);

        Fix128Vec newAcceleration = GravityAt(Offset);
        Velocity += (acceleration + newAcceleration) * halfDt;
    }

    /// <summary>The station's docking port, in the host's local frame.</summary>
    /// <remarks>
    /// Rotated with the station, which for now means it is not rotated: a station that holds
    /// its attitude is the simplest thing that works and is what a real one does anyway. A
    /// tumbling station is a legitimately interesting hazard and a later concern.
    /// </remarks>
    internal readonly DockingPort Port => new(Offset + PortOffset, PortAxis);

    /// <summary>Gravitational acceleration from the host at <paramref name="position"/>.</summary>
    internal readonly Fix128Vec GravityAt(Fix128Vec position)
    {
        Fix128 r = position.Length;
        if (r == Fix128.Zero)
        {
            throw new InvalidOperationException("A station cannot be at the centre of its host.");
        }

        // GM/r²/r rather than GM/r³ for the same reason the local gravity uses it: r³ overflows
        // Q64.64 at about 2.6 AU, and a station at Jupiter is inside that.
        Fix128 scale = GmMetres / r / r / r;
        return new Fix128Vec(
            -position.X * scale,
            -position.Y * scale,
            -position.Z * scale);
    }

    /// <summary>The gravity source this station's host presents to a ship in the same frame.</summary>
    internal readonly GravitySource GravitySource => new(Fix128Vec.Zero, GmMetres);
}
