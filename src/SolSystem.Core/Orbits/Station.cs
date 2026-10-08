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
    /// <summary>
    /// Advances the station one tick, and its attitude with it.
    /// </summary>
    /// <remarks>
    /// A station in orbit turns with its orbit — the local-vertical attitude, the way a real
    /// one keeps its rings floor-down and its corridor nadir-outward. The corridor is the
    /// direction a ship comes from, and a ship comes from whatever altitude it launched at:
    /// a fixed axis would point at empty sky a quarter of an orbit later. So the port's
    /// offset and axis are carried around at the orbital rate, which is exactly the turn the
    /// station itself makes, about the same normal the orbit bends around.
    /// </remarks>
    internal void Step(Fix128 dt)
    {
        Fix128 halfDt = dt * Fix128.Half;

        Fix128Vec acceleration = GravityAt(Offset);
        Offset += Velocity * dt + acceleration * (halfDt * dt);

        Fix128Vec newAcceleration = GravityAt(Offset);
        Velocity += (acceleration + newAcceleration) * halfDt;

        // The local-vertical turn: one orbital angle this tick, about the orbit's normal.
        // The rate is the station's own speed over its own radius — the same measure
        // everywhere on a circular orbit, and the honest one here since this station is
        // propagated, not pinned to a table.
        Fix128 omega = Velocity.Length / Offset.Length;
        Fix128 theta = omega * dt;

        // The turn's trigonometry is a SERIES, not the table lookups, and the reason is
        // both speed and drift. A ten-day probe is a hundred million of these ticks; at the
        // table's six-parts-per-billion interpolation error the corridor's axis decays to
        // half its length in ten days, because per tick the rotation loses a few parts per
        // billion of norm. For an orbital tick θ ≤ 1.2e-3 rad the series sin θ = θ − θ³/6
        // and cos θ = 1 − θ²/2 + θ⁴/24 are exact to the Q64.64 grid — their next terms are
        // below it — so the axis keeps its length to a few parts in a hundred million over a
        // DAY, and the series is a handful of multiplies.
        Fix128 thetaSquared = theta * theta;
        Fix128 sin;
        Fix128 cos;
        if (thetaSquared < Fix128.FromDouble(1e-4))
        {
            sin = theta - (theta * thetaSquared * Fix128.FromDouble(1.0 / 6.0));
            cos = Fix128.One - (thetaSquared * Fix128.Half)
                + (thetaSquared * thetaSquared * Fix128.FromDouble(1.0 / 24.0));
        }
        else
        {
            // A station in a sun-grazing orbit would ask more of a tick than the series is
            // cut off for; the table answers it.
            sin = Trig128.SinRadians(theta);
            cos = Trig128.CosRadians(theta);
        }

        PortAxis = RotatedAbout(PortAxis, new(Fix128.Zero, Fix128.Zero, Fix128.One), sin, cos);
        if (PortOffset != Fix128Vec.Zero)
        {
            PortOffset = RotatedAbout(PortOffset, new(Fix128.Zero, Fix128.Zero, Fix128.One), sin, cos);
        }
    }

    /// <summary>Rotates a vector about the z axis, with the angle's sine and cosine given.</summary>
    /// <remarks>
    /// Rodrigues' rotation, in fixed point: <c>v cos θ + (u×v) sin θ + u(u·v)(1 − cos θ)</c>,
    /// with the trigonometry hoisted to the caller. The rotation in use here spends one sine,
    /// one cosine and a dozen multiplies on the orbital turn; the first version of this
    /// primitive kept the cross terms and dropped <c>v cos θ</c>, which shrinks a vector by
    /// cos θ a tick and quietly sends a corridor's axis to zero, where a docking report
    /// divides by it. The wind-down was a divide-by-zero at the first docking probe; the
    /// general formula is checked by the same probe continuing to fly.
    /// </remarks>
    private static Fix128Vec RotatedAbout(Fix128Vec vector, Fix128Vec unitAxis, Fix128 sin, Fix128 cos)
    {
        Fix128Vec rotated =
            vector * cos
            + Fix128Vec.Cross(unitAxis, vector) * sin
            + unitAxis * (Fix128Vec.Dot(unitAxis, vector) * (Fix128.One - cos));

        return rotated;
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

    /// <summary>
    /// The station's mean orbital motion, in radians per second.
    /// </summary>
    /// <remarks>
    /// The measure the attitude turn already uses: the station's own speed over its own
    /// radius. A rendezvous law needs it because the relative dynamics it flies are
    /// themselves a function of how fast the station turns — the Hill frame is the station's
    /// own, and the Coriolis that appears in it is <c>2n</c>, not anybody else's figure.
    /// </remarks>
    internal readonly Fix128 OrbitalRate => Velocity.Length / Offset.Length;
}
