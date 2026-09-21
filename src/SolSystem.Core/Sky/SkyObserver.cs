using SolSystem.Core.Numerics;

namespace SolSystem.Core.Sky;

/// <summary>
/// Where the sky is being looked at from, and which way is up.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is in the J2000 ecliptic frame, which is the frame the planets come out of
/// <see cref="Orbits.Ephemeris"/> in. That choice is what lets a renderer draw stars and planets in
/// the same pass without a frame conversion in the inner loop — and the conversion is the one step
/// where a sign error is both catastrophic and invisible, so it happens once, here, where it can be
/// tested.
/// </para>
/// <para>
/// The horizon triad is only meaningful for an observer standing on a rotating body. In free space
/// the sky has no up, and a renderer supplies its own — so <see cref="HasHorizon"/> is false and the
/// triad is zero rather than a silently arbitrary default.
/// </para>
/// </remarks>
internal readonly struct SkyObserver
{
    /// <summary>Position in kilometres, heliocentric, J2000 ecliptic.</summary>
    internal readonly Fix128Vec Position;

    /// <summary>Velocity in kilometres per second, heliocentric, J2000 ecliptic.</summary>
    internal readonly Fix128Vec Velocity;

    /// <summary>Unit vector towards local east. Zero when there is no horizon.</summary>
    internal readonly Fix128Vec East;

    /// <summary>Unit vector towards local north. Zero when there is no horizon.</summary>
    internal readonly Fix128Vec North;

    /// <summary>Unit vector towards the zenith. Zero when there is no horizon.</summary>
    internal readonly Fix128Vec Up;

    /// <summary>Latitude in degrees, or zero when there is no horizon.</summary>
    internal readonly double LatitudeDegrees;

    /// <summary>Local mean sidereal time in turns, or zero when there is no horizon.</summary>
    internal readonly double LocalSiderealTurns;

    internal SkyObserver(
        Fix128Vec position,
        Fix128Vec velocity,
        Fix128Vec east,
        Fix128Vec north,
        Fix128Vec up,
        double latitudeDegrees,
        double localSiderealTurns)
    {
        Position = position;
        Velocity = velocity;
        East = east;
        North = north;
        Up = up;
        LatitudeDegrees = latitudeDegrees;
        LocalSiderealTurns = localSiderealTurns;
    }

    /// <summary>Whether this observer has a horizon — that is, whether it is standing on a body.</summary>
    internal bool HasHorizon => !Up.IsZero;

    /// <summary>
    /// An observer in free space: no horizon, and so no horizon coordinates.
    /// </summary>
    /// <param name="position">Heliocentric position in kilometres, J2000 ecliptic.</param>
    /// <param name="velocity">Heliocentric velocity in km/s, J2000 ecliptic.</param>
    internal static SkyObserver InSpace(Fix128Vec position, Fix128Vec velocity) =>
        new(position, velocity, Fix128Vec.Zero, Fix128Vec.Zero, Fix128Vec.Zero, 0.0, 0.0);

    /// <summary>
    /// An observer standing on the surface of a rotating body.
    /// </summary>
    /// <param name="latitudeDegrees">Geodetic latitude, north positive.</param>
    /// <param name="longitudeDegrees">Longitude, east positive.</param>
    /// <param name="julianDate">The instant, for the body's rotation.</param>
    /// <param name="position">The body's centre, heliocentric, kilometres, J2000 ecliptic.</param>
    /// <param name="velocity">The body's velocity, km/s, J2000 ecliptic.</param>
    /// <remarks>
    /// <para>
    /// The observer is placed at the body's <em>centre</em> rather than on its surface, and that is
    /// not laziness: the offset is 6 378 km against a star at 4 × 10¹³, so it changes a star's
    /// position by 0.03 arcseconds, which is a third of a catalogue quantisation step. It matters for
    /// the Moon and not at all for anything else, and adding it correctly needs the body's rotation
    /// and obliquity as well as its radius — so it is left out and stated rather than half done.
    /// </para>
    /// <para>
    /// The horizon triad is built in the <em>equatorial</em> frame, where latitude and sidereal time
    /// mean what they say, and rotated into the ecliptic at the end. Building it directly in the
    /// ecliptic is the tempting shortcut and it is wrong: the celestial pole is not the ecliptic
    /// pole, and the error is the obliquity.
    /// </para>
    /// </remarks>
    internal static SkyObserver OnSurface(
        double latitudeDegrees,
        double longitudeDegrees,
        double julianDate,
        Fix128Vec position,
        Fix128Vec velocity)
    {
        double localSidereal = SiderealTime.LocalTurns(julianDate, longitudeDegrees);
        double obliquity = Frames.ObliquityDegrees(julianDate);

        // In the equatorial frame: the zenith sits directly above the observer's latitude, at the
        // hour angle the sidereal time gives it. East and north follow from it and from the pole.
        double latitude = latitudeDegrees * Math.PI / 180.0;
        double theta = localSidereal * 2.0 * Math.PI;

        double sinLat = Math.Sin(latitude);
        double cosLat = Math.Cos(latitude);
        double sinTheta = Math.Sin(theta);
        double cosTheta = Math.Cos(theta);

        var upEquatorial = new Fix128Vec(
            Fix128.FromDouble(cosLat * cosTheta),
            Fix128.FromDouble(cosLat * sinTheta),
            Fix128.FromDouble(sinLat));

        // East is the direction of increasing right ascension, which is the body's rotation.
        var eastEquatorial = new Fix128Vec(
            Fix128.FromDouble(-sinTheta),
            Fix128.FromDouble(cosTheta),
            Fix128.Zero);

        // North completes the triad: up × east would be left-handed, so it is east × up reversed —
        // computed as up × east is wrong and gives a triad that mirrors the sky.
        var northEquatorial = new Fix128Vec(
            Fix128.FromDouble(-sinLat * cosTheta),
            Fix128.FromDouble(-sinLat * sinTheta),
            Fix128.FromDouble(cosLat));

        return new SkyObserver(
            position,
            velocity,
            Frames.EquatorialToEcliptic(eastEquatorial, obliquity).Normalized(),
            Frames.EquatorialToEcliptic(northEquatorial, obliquity).Normalized(),
            Frames.EquatorialToEcliptic(upEquatorial, obliquity).Normalized(),
            latitudeDegrees,
            localSidereal);
    }
}
