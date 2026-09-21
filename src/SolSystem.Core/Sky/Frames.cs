namespace SolSystem.Core.Sky;

/// <summary>
/// The rotation between the two frames the sky lives in.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the joint between the star catalogue and the ephemeris, and it is the one place a
/// mistake puts every planet in the wrong constellation.</b> The star catalogue is equatorial —
/// right ascension and declination, measured against the Earth's axis. The ephemeris is ecliptic —
/// measured against the plane of the Earth's orbit. They share the vernal equinox, so the rotation
/// between them is a single rotation about that shared axis, by the obliquity of the ecliptic.
/// </para>
/// <para>
/// The obliquity at J2000 is 23° 26′ 21.448″, and it is not a constant: it decreases by about 47
/// arcseconds a century, so by 2185 it is 23° 25′ 34″ — a difference of 47 arcseconds, which is
/// below what any renderer will show and well above what a test should ignore. The linear term is
/// carried; the 46-arcsecond periodic wobble in it is not, and does not need to be.
/// </para>
/// <para>
/// Getting the sign wrong is the classic error and it is not self-correcting: the ecliptic pole
/// sits at declination +66° 34′, not −66° 34′, and a catalogue rotated the wrong way puts the
/// summer solstice in December. The test for it checks a known star against a known ecliptic
/// latitude rather than checking that the rotation is orthogonal, because an orthogonal rotation
/// through the wrong angle passes every symmetry test there is.
/// </para>
/// </remarks>
internal static class Frames
{
    /// <summary>Mean obliquity of the ecliptic at J2000, in degrees.</summary>
    internal const double ObliquityJ2000Degrees = 23.439291111111;

    /// <summary>Change in the obliquity, in degrees per Julian century.</summary>
    internal const double ObliquityRatePerCentury = -0.0130041666667;

    /// <summary>Julian date of the J2000 epoch: 2000 January 1, 12:00 TT.</summary>
    internal const double J2000JulianDate = 2451545.0;

    /// <summary>Mean obliquity at a Julian date, in degrees.</summary>
    internal static double ObliquityDegrees(double julianDate) =>
        ObliquityJ2000Degrees + ObliquityRatePerCentury * ((julianDate - J2000JulianDate) / 36525.0);

    /// <summary>
    /// Rotates an equatorial direction into the ecliptic frame the ephemeris uses.
    /// </summary>
    /// <remarks>
    /// A rotation about <c>+x</c>, which is the vernal equinox and is common to both frames. The
    /// equatorial pole <c>(0, 0, 1)</c> becomes <c>(0, sin ε, cos ε)</c>, which is the celestial
    /// pole at ecliptic latitude 90° − ε — the check that the sign is right.
    /// </remarks>
    internal static Numerics.Fix128Vec EquatorialToEcliptic(Numerics.Fix128Vec v, double obliquityDegrees)
    {
        (double sin, double cos) = SinCosDegrees(obliquityDegrees);
        return new Numerics.Fix128Vec(
            v.X,
            v.Y * Numerics.Fix128.FromDouble(cos) + v.Z * Numerics.Fix128.FromDouble(sin),
            v.Y * Numerics.Fix128.FromDouble(-sin) + v.Z * Numerics.Fix128.FromDouble(cos));
    }

    /// <summary>Rotates an ecliptic direction back into the equatorial frame.</summary>
    internal static Numerics.Fix128Vec EclipticToEquatorial(Numerics.Fix128Vec v, double obliquityDegrees)
    {
        (double sin, double cos) = SinCosDegrees(obliquityDegrees);
        return new Numerics.Fix128Vec(
            v.X,
            v.Y * Numerics.Fix128.FromDouble(cos) + v.Z * Numerics.Fix128.FromDouble(-sin),
            v.Y * Numerics.Fix128.FromDouble(sin) + v.Z * Numerics.Fix128.FromDouble(cos));
    }

    /// <summary>
    /// A unit vector from right ascension and declination, both in degrees, equatorial J2000.
    /// </summary>
    /// <remarks>
    /// Right ascension increases eastward from the vernal equinox, along the equator; declination
    /// is the angle from the equator. So <c>+x</c> is RA 0h Dec 0°, <c>+y</c> is RA 6h Dec 0°, and
    /// <c>+z</c> is the north celestial pole — which is the convention the rotation above assumes.
    /// </remarks>
    internal static Numerics.Fix128Vec FromEquatorialDegrees(double raDegrees, double decDegrees)
    {
        (double sinRa, double cosRa) = SinCosDegrees(raDegrees);
        (double sinDec, double cosDec) = SinCosDegrees(decDegrees);

        return new Numerics.Fix128Vec(
            Numerics.Fix128.FromDouble(cosDec * cosRa),
            Numerics.Fix128.FromDouble(cosDec * sinRa),
            Numerics.Fix128.FromDouble(sinDec));
    }

    /// <summary>Right ascension and declination of an equatorial unit vector, in degrees.</summary>
    internal static (double RightAscension, double Declination) ToEquatorialDegrees(
        Numerics.Fix128Vec v)
    {
        double x = v.X.ToDouble();
        double y = v.Y.ToDouble();
        double z = v.Z.ToDouble();

        double ra = Math.Atan2(y, x) * 180.0 / Math.PI;
        if (ra < 0.0)
        {
            ra += 360.0;
        }

        double dec = Math.Atan2(z, Math.Sqrt((x * x) + (y * y))) * 180.0 / Math.PI;
        return (ra, dec);
    }

    /// <summary>Ecliptic longitude and latitude of an ecliptic vector, in degrees.</summary>
    internal static (double Longitude, double Latitude) ToEclipticDegrees(Numerics.Fix128Vec v)
    {
        double x = v.X.ToDouble();
        double y = v.Y.ToDouble();
        double z = v.Z.ToDouble();

        double longitude = Math.Atan2(y, x) * 180.0 / Math.PI;
        if (longitude < 0.0)
        {
            longitude += 360.0;
        }

        double latitude = Math.Atan2(z, Math.Sqrt((x * x) + (y * y))) * 180.0 / Math.PI;
        return (longitude, latitude);
    }

    private static (double Sin, double Cos) SinCosDegrees(double degrees)
    {
        double radians = degrees * Math.PI / 180.0;
        return (Math.Sin(radians), Math.Cos(radians));
    }
}
