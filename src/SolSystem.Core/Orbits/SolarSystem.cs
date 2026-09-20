using SolSystem.Core.Numerics;

namespace SolSystem.Core.Orbits;

/// <summary>
/// The system as a whole: where everything is, at one instant, on one clock.
/// </summary>
/// <remarks>
/// <para>
/// This is the join between the ephemeris and the local frame, and it is deliberately the only
/// place that knows both. The solar frame works in kilometres and seconds, the local frame in
/// metres and seconds, and a ship flies in the local frame of whatever body it is near — so
/// something has to do the conversion, and having exactly one such place is what keeps the two
/// frames from leaking into each other.
/// </para>
/// <para>
/// <b>The clock is a count of seconds from J2000, not a date.</b> A calendar is a presentation
/// concern; putting months and leap seconds inside the simulation would be the same mistake as
/// putting floats in it. The conversion to a Julian date happens once, in the ephemeris.
/// </para>
/// <para>
/// Bodies are placed from <see cref="Ephemeris"/> rather than integrated. That is not a
/// shortcut: JPL's elements are fitted to a real ephemeris and are better than anything a
/// fixed-point integrator will produce over a game's timespan, and they cost a Kepler solve per
/// body per query instead of a step per body per tick.
/// </para>
/// </remarks>
internal sealed class SolarSystem
{
    /// <summary>Seconds since J2000.0. The simulation's only notion of when.</summary>
    internal double SecondsFromJ2000 { get; private set; }

    /// <summary>Seconds in a day, for the conversion the ephemeris needs.</summary>
    private const double SecondsPerDay = 86400.0;

    /// <summary>Metres in a kilometre, for the frame conversion.</summary>
    private const double MetresPerKilometre = 1000.0;

    /// <summary>A body in the system, and what it is made of.</summary>
    internal readonly struct Body
    {
        /// <summary>Which planet this is.</summary>
        internal readonly Ephemeris.Body Kind;

        /// <summary>Display name. Content, not logic — it lives here so nothing else hard-codes it.</summary>
        internal readonly string Name;

        /// <summary>Equatorial radius, in kilometres.</summary>
        internal readonly double RadiusKm;

        /// <summary>
        /// Standard gravitational parameter, in km³/s². Zero for a body that does not pull.
        /// </summary>
        internal readonly double GmKm;

        internal Body(Ephemeris.Body kind, string name, double radiusKm, double gmKm)
        {
            Kind = kind;
            Name = name;
            RadiusKm = radiusKm;
            GmKm = gmKm;
        }
    }

    /// <summary>
    /// The bodies, with the radii and gravitational parameters the simulation needs.
    /// </summary>
    /// <remarks>
    /// Radii are IAU mean values and GM is from the same source as the elements. The Moon is
    /// absent for now: it needs its own geocentric elements rather than heliocentric ones, and
    /// nothing in the skeleton requires it yet.
    /// </remarks>
    internal static readonly Body[] Bodies =
    {
        new(Ephemeris.Body.Mercury, "Mercury", 2_439.7, 2.2032e4),
        new(Ephemeris.Body.Venus, "Venus", 6_051.8, 3.24859e5),
        new(Ephemeris.Body.Earth, "Earth", 6_378.1, 3.986004418e5),
        new(Ephemeris.Body.Mars, "Mars", 3_396.2, 4.282837e4),
        new(Ephemeris.Body.Jupiter, "Jupiter", 71_492.0, 1.26686534e8),
        new(Ephemeris.Body.Saturn, "Saturn", 60_268.0, 3.7931187e7),
        new(Ephemeris.Body.Uranus, "Uranus", 25_559.0, 5.793939e6),
        new(Ephemeris.Body.Neptune, "Neptune", 24_764.0, 6.836529e6),
    };

    /// <summary>The body entry for <paramref name="kind"/>.</summary>
    internal static Body BodyOf(Ephemeris.Body kind)
    {
        Body[] bodies = Bodies;
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i].Kind == kind)
            {
                return bodies[i];
            }
        }

        throw new ArgumentOutOfRangeException(nameof(kind), kind, "No such body in the system.");
    }

    /// <summary>A new system at the J2000 epoch.</summary>
    internal SolarSystem()
    {
        SecondsFromJ2000 = 0.0;
    }

    /// <summary>Advances the clock by <paramref name="seconds"/>.</summary>
    internal void Advance(double seconds) => SecondsFromJ2000 += seconds;

    /// <summary>Sets the clock. Used by a replay seeking to a timestamp.</summary>
    internal void SetTime(double secondsFromJ2000) => SecondsFromJ2000 = secondsFromJ2000;

    /// <summary>Heliocentric position and velocity, solar frame: kilometres and km/s.</summary>
    internal Ephemeris.State Heliocentric(Ephemeris.Body body) =>
        Ephemeris.AtSecondsFromJ2000(body, SecondsFromJ2000);

    /// <summary>
    /// Planet <paramref name="body"/> relative to planet <paramref name="origin"/>, in
    /// kilometres and km/s. This is the vector a transfer is planned against.
    /// </summary>
    internal Ephemeris.State Relative(Ephemeris.Body body, Ephemeris.Body origin)
    {
        Ephemeris.State a = Heliocentric(body);
        Ephemeris.State b = Heliocentric(origin);
        return new Ephemeris.State(a.Position - b.Position, a.Velocity - b.Velocity);
    }

    /// <summary>
    /// Where a point near <paramref name="body"/> is, in the local frame of that body.
    /// </summary>
    /// <remarks>
    /// The local frame's unit is the metre, so the result is the body's position expressed at
    /// metre scale. The origin is the body's centre, and the axes are the J2000 ecliptic's —
    /// rotating into a body-fixed frame is a separate concern and needs a rotation model that
    /// does not exist yet.
    /// <para>
    /// A position placed with this method inherits the body's orbital velocity, which is what
    /// makes a station in orbit stay put in the local frame instead of sliding backwards at
    /// 30 km/s. Nothing here rotates, orbits or otherwise maintains the offset: a site at a
    /// fixed offset is a site that is being held there, and holding it is the simulation's job
    /// once there is one.
    /// </para>
    /// </remarks>
    internal LocalPoint LocalTo(Ephemeris.Body body, Fix128Vec offsetKilometres)
    {
        Ephemeris.State state = Heliocentric(body);
        const double scale = MetresPerKilometre;

        var origin = new Fix128Vec(
            state.Position.X * Fix128.FromDouble(scale),
            state.Position.Y * Fix128.FromDouble(scale),
            state.Position.Z * Fix128.FromDouble(scale));

        var speed = new Fix128Vec(
            state.Velocity.X * Fix128.FromDouble(scale),
            state.Velocity.Y * Fix128.FromDouble(scale),
            state.Velocity.Z * Fix128.FromDouble(scale));

        var offset = new Fix128Vec(
            offsetKilometres.X * Fix128.FromDouble(scale),
            offsetKilometres.Y * Fix128.FromDouble(scale),
            offsetKilometres.Z * Fix128.FromDouble(scale));

        return new LocalPoint(body, origin + offset, speed);
    }

    /// <summary>
    /// A point expressed in the local frame of a body: metres and metres per second.
    /// </summary>
    /// <remarks>
    /// The body is carried alongside so that a caller knows whose frame the numbers are in.
    /// Without it a position is a triple of numbers that means nothing, and the frame bug that
    /// follows is a ship that appears to accelerate at 30 km/s.
    /// </remarks>
    internal readonly struct LocalPoint
    {
        /// <summary>The body whose frame this point is expressed in.</summary>
        internal readonly Ephemeris.Body Frame;

        /// <summary>Position in metres, from the body's centre.</summary>
        internal readonly Fix128Vec Position;

        /// <summary>Velocity in metres per second, relative to the body's centre.</summary>
        internal readonly Fix128Vec Velocity;

        internal LocalPoint(Ephemeris.Body frame, Fix128Vec position, Fix128Vec velocity)
        {
            Frame = frame;
            Position = position;
            Velocity = velocity;
        }
    }
}
