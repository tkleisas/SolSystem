namespace SolSystem.Core.Sky;

/// <summary>
/// What time it is, in the two senses the sky needs.
/// </summary>
/// <remarks>
/// <para>
/// A day is not a rotation. The Earth turns once in 23 h 56 min 4.09 s of solar time, not 24 hours,
/// because it is also moving round the Sun and has to turn a little further to bring the Sun back to
/// the same meridian. That four-minute difference is why the constellations rise two hours earlier
/// every month, and a sky that used the solar day would drift a full circuit of the sky over a year.
/// </para>
/// <para>
/// The formula is the standard linear expression with the two small corrections, good to about a
/// tenth of a second of time over the centuries the game spans. Terms beyond it are below a
/// milliarcsecond of sky rotation, which is a ten-thousandth of a pixel.
/// </para>
/// </remarks>
internal static class SiderealTime
{
    /// <summary>Julian date of the J2000 epoch.</summary>
    private const double J2000 = Frames.J2000JulianDate;

    /// <summary>
    /// Greenwich mean sidereal time, in turns.
    /// </summary>
    /// <remarks>
    /// Turns rather than degrees because the fixed-point trigonometry is turn-based, and because a
    /// sidereal time is an angle that gets multiplied by 2π to become a rotation — carrying it as a
    /// fraction of a turn means the wrap is free and exact.
    /// </remarks>
    internal static double GreenwichTurns(double julianDate)
    {
        double d = julianDate - J2000;
        double t = d / 36525.0;

        // 280.46061837° is where the equinox stood at J2000; 360.98564736629° a day is one turn plus
        // the 3 min 56 s the Earth has to make up for its own orbital motion.
        double degrees = 280.46061837
            + (360.98564736629 * d)
            + (0.000387933 * t * t)
            - (t * t * t / 38710000.0);

        double turns = degrees / 360.0;
        return turns - Math.Floor(turns);
    }

    /// <summary>
    /// Local mean sidereal time in turns, east longitude positive.
    /// </summary>
    /// <remarks>
    /// Fifteen degrees of longitude is one hour of sidereal time, so longitude divides by 360 — the
    /// same fraction-of-a-turn convention, and the same reason.
    /// </remarks>
    internal static double LocalTurns(double julianDate, double longitudeDegrees)
    {
        double turns = GreenwichTurns(julianDate) + (longitudeDegrees / 360.0);
        return turns - Math.Floor(turns);
    }

    /// <summary>
    /// The length of a sidereal day in seconds.
    /// </summary>
    /// <remarks>
    /// Derived rather than quoted, so that the constant in the formula above and the length of the
    /// day cannot disagree: the Earth turns 360.98564736629° in a mean solar day, so a full 360°
    /// takes 86400 × 360/360.98564736629 seconds. That is 86 164.0905 s, the familiar 23 h 56 min
    /// 4.09 s, and the test asserts they agree.
    /// </remarks>
    internal static double SiderealDaySeconds => 86400.0 * 360.0 / 360.98564736629;
}
