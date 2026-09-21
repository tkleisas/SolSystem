using SolSystem.Core.Numerics;

namespace SolSystem.Core.Sky;

/// <summary>
/// The Milky Way, as a direction and a width.
/// </summary>
/// <remarks>
/// <para>
/// The band is the disc of the galaxy seen from inside it, and it is the largest single feature of
/// the real sky — a third of the visible stars are in it, and it is what a dark sky actually looks
/// like. It is modelled here as what it physically is: a great circle on the sky, the galactic
/// equator, with the stars concentrated towards it.
/// </para>
/// <para>
/// The two directions that define it are the galactic poles, in J2000 equatorial coordinates. The
/// north galactic pole is at right ascension 12h 51m 26s, declination +27° 07′ 41″ — in the
/// constellation Coma Berenices, which is why that otherwise unremarkable patch of sky is where the
/// galaxy's axis points. The galactic centre is at 17h 45m 37s, −28° 56′ 10″, in Sagittarius, and it
/// is in the sky the brightest part of the band.
/// </para>
/// <para>
/// Those two numbers fix the whole frame, and having them means the band is in the right place
/// rather than drawn as a decorative smear: from Earth it runs through Cygnus, down through
/// Sagittarius and Scorpius, and across to Carina, and any renderer that puts it elsewhere will look
/// wrong to anyone who has been outside.
/// </para>
/// </remarks>
internal static class MilkyWay
{
    /// <summary>Right ascension of the north galactic pole, in degrees.</summary>
    internal const double NorthPoleRightAscensionDegrees = 192.85948;

    /// <summary>Declination of the north galactic pole, in degrees.</summary>
    internal const double NorthPoleDeclinationDegrees = 27.12825;

    /// <summary>
    /// The north galactic pole in the J2000 ecliptic frame, which is the frame the sky works in.
    /// </summary>
    private static readonly Fix128Vec Pole = Frames.EquatorialToEcliptic(
        Frames.FromEquatorialDegrees(NorthPoleRightAscensionDegrees, NorthPoleDeclinationDegrees),
        Frames.ObliquityJ2000Degrees);

    /// <summary>Galactic latitude of a direction, in degrees. Zero is the plane of the galaxy.</summary>
    internal static double LatitudeDegrees(Fix128Vec directionEcliptic)
    {
        double sine = ((directionEcliptic.X * Pole.X)
            + (directionEcliptic.Y * Pole.Y)
            + (directionEcliptic.Z * Pole.Z)).ToDouble();

        return Math.Asin(Math.Clamp(sine, -1.0, 1.0)) * 180.0 / Math.PI;
    }

    /// <summary>
    /// How bright the band is at a galactic latitude, as a fraction of its brightness in the plane.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A Gaussian in galactic latitude with a scale of about 5°, which is the thin-disc scale height
    /// seen from inside it, times a falloff in longitude because the galaxy is not uniform round its
    /// own circle — the view towards the centre in Sagittarius is far denser than the view outward
    /// through Auriga, and a band of even brightness looks drawn rather than observed.
    /// </para>
    /// <para>
    /// The numbers are a fit to what the unaided eye sees under a dark sky, not to a photograph or a
    /// surface-brightness map. Those disagree by a factor of several, because a camera integrates
    /// and an eye does not, and the eye is what the game is drawing for.
    /// </para>
    /// </remarks>
    internal static double Brightness(Fix128Vec directionEcliptic)
    {
        double latitude = LatitudeDegrees(directionEcliptic);

        // Gaussian in latitude. Beyond about three scale heights there is nothing to see.
        double band = Math.Exp(-0.5 * Math.Pow(latitude / 5.0, 2.0));
        if (band < 0.01)
        {
            return 0.0;
        }

        // And along the band: brightest towards the galactic centre, faintest towards the anticentre
        // and the outer arms. The longitude is measured from the centre, so the variation is a
        // raised cosine over the full circle.
        double longitude = LongitudeFromCentreDegrees(directionEcliptic);
        double towards = Math.Cos(longitude * Math.PI / 180.0);   // +1 at the centre, -1 opposite
        double along = 0.45 + (0.55 * ((towards + 1.0) / 2.0));

        return band * along;
    }

    /// <summary>Galactic longitude measured from the galactic centre, in degrees, 0 to 360.</summary>
    /// <remarks>
    /// Built from the pole and the centre rather than from the full three-angle IAU frame, because
    /// only one thing is wanted from it: how far round the band from the middle this direction is.
    /// </remarks>
    internal static double LongitudeFromCentreDegrees(Fix128Vec directionEcliptic)
    {
        // The galactic centre, in the ecliptic frame, and the direction at right angles to it in the
        // galactic plane — which between them span the plane.
        Fix128Vec centre = Frames.EquatorialToEcliptic(
            Frames.FromEquatorialDegrees(266.4051, -28.936175), Frames.ObliquityJ2000Degrees);

        Fix128Vec along = centre;
        Fix128Vec across = new(
            Pole.Y * along.Z - Pole.Z * along.Y,
            Pole.Z * along.X - Pole.X * along.Z,
            Pole.X * along.Y - Pole.Y * along.X);

        double c = ((directionEcliptic.X * along.X)
            + (directionEcliptic.Y * along.Y)
            + (directionEcliptic.Z * along.Z)).ToDouble();

        double s = ((directionEcliptic.X * across.X)
            + (directionEcliptic.Y * across.Y)
            + (directionEcliptic.Z * across.Z)).ToDouble();

        double degrees = Math.Atan2(s, c) * 180.0 / Math.PI;
        return degrees < 0.0 ? degrees + 360.0 : degrees;
    }
}
