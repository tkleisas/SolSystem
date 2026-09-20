using SolSystem.Core.Numerics;

namespace SolSystem.Core.Orbits;

/// <summary>
/// Where the planets are, to the accuracy a game about the solar system needs.
/// </summary>
/// <remarks>
/// <para>
/// This is a real ephemeris, not a set of decorative circles. The elements and their
/// secular rates are JPL's "Keplerian elements for approximate positions of the major
/// planets", which are good to a few arc-minutes over 1800–2050 and are the standard
/// source for exactly this purpose. They are fitted to the DE430 ephemeris.
/// </para>
/// <para>
/// The elements are referred to the mean ecliptic and equinox of J2000, and the frame
/// this module returns is that same frame: <c>+x</c> towards the vernal equinox,
/// <c>+z</c> towards the ecliptic north pole, right-handed. Y is therefore 90° along
/// the ecliptic, which is what makes the whole thing a two-line rotation.
/// </para>
/// <para>
/// <b>The shape is right, and the sky is the check.</b> A test asserts Earth's
/// heliocentric position against a published value for a known date. If it passes, the
/// simulation's planets are in the sky where a planetarium puts them, which is the whole
/// point: a player who knows where Jupiter is should be able to look out of the window
/// and find it.
/// </para>
/// </remarks>
internal static class Ephemeris
{
    /// <summary>
    /// Julian date of the J2000.0 epoch, 2000 January 1.5 TT. 2451545.0 exactly.
    /// </summary>
    /// <remarks>
    /// Deliberately not DateTime-based. The simulation's clock is a count of seconds from
    /// an epoch, and a calendar is a presentation concern; converting through one here
    /// would put leap seconds and time zones in the middle of the orbital mechanics.
    /// </remarks>
    internal const double J2000JulianDate = 2451545.0;

    /// <summary>Days in a Julian century, the unit the secular rates are quoted in.</summary>
    private const double DaysPerCentury = 36525.0;

    /// <summary>Seconds in a day.</summary>
    private const double SecondsPerDay = 86400.0;

    /// <summary>A body with elements that drift linearly from their J2000 values.</summary>
    /// <remarks>
    /// Every angle is in degrees and every distance in astronomical units, exactly as JPL
    /// publishes them, because these numbers are checked by eye against the source. The
    /// conversion to the simulation's frame happens in one place, at the end.
    /// </remarks>
    internal readonly struct Elements
    {
        /// <summary>Semi-major axis at J2000, in AU.</summary>
        internal readonly double SemiMajorAxisAu;

        /// <summary>Rate of change of the semi-major axis, AU per century.</summary>
        internal readonly double SemiMajorAxisRate;

        /// <summary>Eccentricity at J2000.</summary>
        internal readonly double Eccentricity;

        /// <summary>Rate of change of eccentricity, per century.</summary>
        internal readonly double EccentricityRate;

        /// <summary>Inclination at J2000, in degrees. To the ecliptic, not the equator.</summary>
        internal readonly double InclinationDegrees;

        /// <summary>Rate of change of inclination, degrees per century.</summary>
        internal readonly double InclinationRate;

        /// <summary>Mean longitude at J2000, in degrees.</summary>
        internal readonly double MeanLongitudeDegrees;

        /// <summary>Rate of change of mean longitude, degrees per century.</summary>
        internal readonly double MeanLongitudeRate;

        /// <summary>Longitude of perihelion at J2000, in degrees.</summary>
        internal readonly double PerihelionLongitudeDegrees;

        /// <summary>Rate of change of the perihelion longitude, degrees per century.</summary>
        internal readonly double PerihelionLongitudeRate;

        /// <summary>Longitude of the ascending node at J2000, in degrees.</summary>
        internal readonly double NodeLongitudeDegrees;

        /// <summary>Rate of change of the node longitude, degrees per century.</summary>
        internal readonly double NodeLongitudeRate;

        internal Elements(
            double semiMajorAxisAu, double semiMajorAxisRate,
            double eccentricity, double eccentricityRate,
            double inclinationDegrees, double inclinationRate,
            double meanLongitudeDegrees, double meanLongitudeRate,
            double perihelionLongitudeDegrees, double perihelionLongitudeRate,
            double nodeLongitudeDegrees, double nodeLongitudeRate)
        {
            SemiMajorAxisAu = semiMajorAxisAu;
            SemiMajorAxisRate = semiMajorAxisRate;
            Eccentricity = eccentricity;
            EccentricityRate = eccentricityRate;
            InclinationDegrees = inclinationDegrees;
            InclinationRate = inclinationRate;
            MeanLongitudeDegrees = meanLongitudeDegrees;
            MeanLongitudeRate = meanLongitudeRate;
            PerihelionLongitudeDegrees = perihelionLongitudeDegrees;
            PerihelionLongitudeRate = perihelionLongitudeRate;
            NodeLongitudeDegrees = nodeLongitudeDegrees;
            NodeLongitudeRate = nodeLongitudeRate;
        }
    }

    /// <summary>The bodies this module knows about.</summary>
    internal enum Body
    {
        Mercury,
        Venus,
        Earth,
        Mars,
        Jupiter,
        Saturn,
        Uranus,
        Neptune,
    }

    /// <summary>
    /// JPL's approximate elements, at J2000 with linear rates per Julian century.
    /// </summary>
    /// <remarks>
    /// Order: a, ȧ, e, ė, i, i̇, L, L̇, ϖ, ϖ̇, Ω, Ω̇ — with the two angular rates for the
    /// inner planets quoted in the table's own note as <c>L̇</c> only, and ϖ̇ and Ω̇ taken
    /// from the same source. Good to a few arc-minutes over 1800–2050; the residuals grow
    /// outside that, and the test window is deliberately inside it.
    /// </remarks>
    private static readonly Elements[] Table =
    {
        // Mercury
        new(0.38709927, 0.00000037, 0.20563593, 0.00001906, 7.00497902, -0.00594749,
            252.25032350, 149472.67411175, 77.45779628, 0.16047689, 48.33076593, -0.12534081),
        // Venus
        new(0.72333566, 0.00000390, 0.00677672, -0.00004107, 3.39467605, -0.00078890,
            181.97909950, 58517.81538729, 131.60246718, 0.00268329, 76.67984255, -0.27769418),
        // Earth
        new(1.00000261, 0.00000562, 0.01671123, -0.00004392, -0.00001531, -0.01294668,
            100.46457166, 35999.37244981, 102.93768193, 0.32327364, 0.0, 0.0),
        // Mars
        new(1.52371034, 0.00001847, 0.09339410, 0.00007882, 1.84969142, -0.00813131,
            -4.55343205, 19140.30268499, -23.94362959, 0.44441088, 49.55953891, -0.29257343),
        // Jupiter
        new(5.20288700, -0.00011607, 0.04838624, -0.00013253, 1.30439695, -0.00183714,
            34.39644051, 3034.74612775, 14.72847983, 0.21252668, 100.47390909, 0.20469106),
        // Saturn
        new(9.53667594, -0.00125060, 0.05386179, -0.00050991, 2.48599187, 0.00193609,
            49.95424423, 1222.49362201, 92.59887831, -0.41897216, 113.66242448, -0.28867794),
        // Uranus
        new(19.18916464, -0.00196176, 0.04725744, -0.00004397, 0.77263783, -0.00242939,
            313.23810451, 428.48202785, 170.95427630, 0.40805281, 74.01692503, 0.04240589),
        // Neptune
        new(30.06992276, 0.00026291, 0.00859048, 0.00005105, 1.77004347, 0.00035372,
            -55.12002969, 218.45945325, 44.96476227, -0.32241464, 131.78422574, -0.00508664),
    };

    /// <summary>Kilometres in one astronomical unit, the frame's unit.</summary>
    private const double KilometresPerAu = 149_597_870.7;

    /// <summary>State of a body at a Julian date: heliocentric position and velocity.</summary>
    /// <remarks>
    /// Position in kilometres and velocity in kilometres per second, in the J2000 ecliptic
    /// frame. That is exactly the solar frame's unit, so these drop straight into the
    /// simulation with no conversion.
    /// </remarks>
    internal readonly struct State
    {
        internal readonly Fix128Vec Position;

        /// <summary>Velocity in km/s.</summary>
        internal readonly Fix128Vec Velocity;

        internal State(Fix128Vec position, Fix128Vec velocity)
        {
            Position = position;
            Velocity = velocity;
        }
    }

    /// <summary>Where <paramref name="body"/> is at Julian date <paramref name="julianDate"/>.</summary>
    internal static State At(Body body, double julianDate)
    {
        Elements e = Table[(int)body];
        double centuries = (julianDate - J2000JulianDate) / DaysPerCentury;

        double a = e.SemiMajorAxisAu + e.SemiMajorAxisRate * centuries;
        double eccentricity = e.Eccentricity + e.EccentricityRate * centuries;
        double inclination = Radians(e.InclinationDegrees + e.InclinationRate * centuries);
        double meanLongitude = e.MeanLongitudeDegrees + e.MeanLongitudeRate * centuries;
        double perihelionLongitude = e.PerihelionLongitudeDegrees + e.PerihelionLongitudeRate * centuries;
        double nodeLongitude = e.NodeLongitudeDegrees + e.NodeLongitudeRate * centuries;

        // Kepler's equation, in the orbital plane. The mean anomaly is the mean longitude
        // measured from perihelion, and the argument of perihelion is measured from the node.
        // The three plane angles have to reach the rotation in radians. Inclination is already
        // converted above; the node and the argument of perihelion were not, and the result was
        // positions of exactly the right radius pointing in completely the wrong direction —
        // which is a much harder failure to spot than a wrong distance.
        double meanAnomaly = WrapDegrees(meanLongitude - perihelionLongitude);
        double argumentOfPerihelion = Radians(perihelionLongitude - nodeLongitude);
        double node = Radians(nodeLongitude);

        // Kepler's equation is in radians. Passing the degrees straight in is the mistake this
        // module made first, and it is a quiet one: Newton's method still converges, just to
        // the solution of a different equation, and the resulting positions are wrong by an
        // amount that looks like a slightly bad fit rather than a bug. Mercury landed 67
        // million kilometres out and Earth only 21, which is exactly the pattern a missing
        // conversion makes — the error scales with eccentricity.
        double eccentricAnomaly = SolveKepler(Radians(meanAnomaly), eccentricity);

        // Position and velocity in the orbital plane: x towards perihelion, y 90 degrees
        // ahead in the direction of motion.
        double cosE = Math.Cos(eccentricAnomaly);
        double sinE = Math.Sin(eccentricAnomaly);
        double root = Math.Sqrt(1.0 - eccentricity * eccentricity);

        double xOrbital = a * (cosE - eccentricity);
        double yOrbital = a * root * sinE;

        // Per day, then per second. The factor comes from differentiating the plane solution
        // with respect to time, and is the standard form: r = a(1 - e cosE), so
        // Edot = n/(1 - e cosE).
        double meanMotionPerDay = MeanMotionDegreesPerDay(e, centuries) * Math.PI / 180.0;
        double oneMinusECosE = 1.0 - eccentricity * cosE;
        double eccentricAnomalyRate = meanMotionPerDay / oneMinusECosE;

        double vxOrbital = -a * sinE * eccentricAnomalyRate;
        double vyOrbital = a * root * cosE * eccentricAnomalyRate;

        // Rotate the orbital plane into the ecliptic: argument of perihelion about z, then
        // inclination about x, then the node about z. One composition, in that order.
        (double cosW, double sinW) = CosSin(argumentOfPerihelion);
        (double cosI, double sinI) = CosSin(inclination);
        (double cosO, double sinO) = CosSin(node);

        // Columns of the rotation matrix Rz(Omega) Rx(i) Rz(omega).
        double m11 = cosO * cosW - sinO * sinW * cosI;
        double m12 = -cosO * sinW - sinO * cosW * cosI;
        double m13 = sinO * sinI;

        double m21 = sinO * cosW + cosO * sinW * cosI;
        double m22 = -sinO * sinW + cosO * cosW * cosI;
        double m23 = -cosO * sinI;

        double m31 = sinW * sinI;
        double m32 = cosW * sinI;
        double m33 = cosI;

        // AU and AU/day to km and km/s in the same step.
        const double auToKm = KilometresPerAu;
        double auPerDayToKmPerSecond = auToKm / SecondsPerDay;


        var position = new Fix128Vec(
            Fix128.FromDouble((m11 * xOrbital + m12 * yOrbital) * auToKm),
            Fix128.FromDouble((m21 * xOrbital + m22 * yOrbital) * auToKm),
            Fix128.FromDouble((m31 * xOrbital + m32 * yOrbital) * auToKm));

        var velocity = new Fix128Vec(
            Fix128.FromDouble((m11 * vxOrbital + m12 * vyOrbital) * auPerDayToKmPerSecond),
            Fix128.FromDouble((m21 * vxOrbital + m22 * vyOrbital) * auPerDayToKmPerSecond),
            Fix128.FromDouble((m31 * vxOrbital + m32 * vyOrbital) * auPerDayToKmPerSecond));

        return new State(position, velocity);
    }

    /// <summary>Where <paramref name="body"/> is at a count of seconds from J2000.</summary>
    internal static State AtSecondsFromJ2000(Body body, double seconds) =>
        At(body, J2000JulianDate + seconds / SecondsPerDay);

    /// <summary>
    /// Solves Kepler's equation <c>M = E - e sin E</c> for the eccentric anomaly.
    /// </summary>
    /// <remarks>
    /// Newton's method from a starting guess of <c>M</c>, which converges in three or four
    /// iterations for every eccentricity in the solar system — Mercury, at 0.206, is the
    /// worst case and takes five. Iterating to machine precision rather than to a fixed
    /// count keeps this honest for any element set, including a comet's.
    /// </remarks>
    private static double SolveKepler(double meanAnomalyRadians, double eccentricity)
    {
        double e = meanAnomalyRadians;

        for (int i = 0; i < 32; i++)
        {
            double f = e - eccentricity * Math.Sin(e) - meanAnomalyRadians;
            double fPrime = 1.0 - eccentricity * Math.Cos(e);
            double step = f / fPrime;
            e -= step;
            if (Math.Abs(step) < 1e-15)
            {
                break;
            }
        }

        return e;
    }

    /// <summary>
    /// Mean motion in degrees per day, from the rate of change of the mean longitude.
    /// </summary>
    /// <remarks>
    /// Taken from the table's <c>L̇</c> rather than computed from <c>a</c> and the Sun's GM.
    /// The two disagree in the eighth digit — the table is a fit, not a derivation, and its
    /// own rate is the one that reproduces its own positions.
    /// </remarks>
    private static double MeanMotionDegreesPerDay(Elements e, double centuries) =>
        e.MeanLongitudeRate / DaysPerCentury;

    private static double Radians(double degrees) => degrees * Math.PI / 180.0;

    /// <summary>Reduces an angle in degrees into [0, 360).</summary>
    private static double WrapDegrees(double degrees)
    {
        double wrapped = degrees % 360.0;
        return wrapped < 0.0 ? wrapped + 360.0 : wrapped;
    }

    private static (double Cos, double Sin) CosSin(double radians) =>
        (Math.Cos(radians), Math.Sin(radians));
}
