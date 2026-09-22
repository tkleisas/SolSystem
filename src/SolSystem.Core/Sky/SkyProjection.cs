using SolSystem.Core.Numerics;

namespace SolSystem.Core.Sky;

/// <summary>
/// Where a thing in the sky appears to be, from where you are standing.
/// </summary>
/// <remarks>
/// <para>
/// Two effects move a star from the direction the catalogue gives it, and both are small, and both
/// are real:
/// </para>
/// <list type="bullet">
/// <item><b>Parallax</b> — the observer is not at the barycentre. The Earth is 1 AU out, so a star
/// at distance <c>d</c> appears displaced by up to <c>1 AU / d</c> radians. For Proxima Centauri
/// that is 0.77 arcseconds, the largest in the sky, and it is how the distance to it was first
/// measured. For anything beyond a few hundred parsecs it is below a milliarcsecond.</item>
/// <item><b>Aberration</b> — the observer is moving. Light arrives as though from a direction
/// tilted into the direction of travel, by <c>v/c</c>, which for the Earth's 29.8 km/s is 20.5
/// arcseconds. That is twenty-six times Proxima's parallax and applies to <em>every</em> star
/// equally, so the whole sky leans into the Earth's motion and back again over a year.</item>
/// </list>
/// <para>
/// Aberration is the one people forget, and forgetting it is not visible in a still frame. It is a
/// uniform 20-arcsecond lean that rotates once a year, so what it actually does is make the stars
/// swim against the planets that do not. Getting it right is cheap: one vector subtraction.
/// </para>
/// <para>
/// Both effects are computed to first order in <c>v/c</c>, which is 1 × 10⁻⁴ for the Earth. The
/// second-order term is 10⁻⁸, which is a hundredth of a catalogue quantisation step.
/// </para>
/// </remarks>
internal static class SkyProjection
{
    /// <summary>One parsec in kilometres, and therefore the scale from distance to position.</summary>
    internal const double KilometresPerParsec = 3.0856775814913673e13;

    /// <summary>Speed of light in kilometres per second.</summary>
    internal const double LightKilometresPerSecond = 299792.458;

    /// <summary>
    /// The apparent direction of a star, in the J2000 ecliptic frame.
    /// </summary>
    /// <remarks>
    /// The catalogue direction is where the star is seen from the barycentre. Everything after that
    /// is the correction for not being there.
    /// </remarks>
    internal static Fix128Vec Apparent(in Star star, in SkyObserver observer)
    {
        Fix128Vec direction = star.Direction;

        // Parallax: place the star at its distance and look at it from where the observer actually
        // is. A star stored as being at infinity keeps its catalogue direction, which is the whole
        // meaning of that encoding.
        if (star.HasDistance)
        {
            double kilometres = star.DistanceParsecs * KilometresPerParsec;
            Fix128Vec starPosition = direction * Fix128.FromDouble(kilometres);
            Fix128Vec offset = starPosition - observer.Position;

            if (!offset.IsZero)
            {
                direction = offset.Normalized();
            }
        }

        // Aberration: the component of the observer's motion across the line of sight, which tilts
        // the apparent direction into the direction of travel. Parallel to the motion there is no
        // displacement at all — a star straight ahead stays straight ahead — so the correction is
        // the velocity with its radial part removed.
        Fix128Vec beta = observer.Velocity * Fix128.FromDouble(1.0 / LightKilometresPerSecond);
        Fix128 radial = Fix128Vec.Dot(direction, beta);
        Fix128Vec perpendicular = beta - (direction * radial);

        Fix128Vec apparent = direction + perpendicular;
        return apparent.IsZero ? direction : apparent.Normalized();
    }

    /// <summary>
    /// Altitude and azimuth of a direction, in degrees.
    /// </summary>
    /// <remarks>
    /// Altitude is measured from the horizon, and azimuth from north through east — so 90° is due
    /// east, 180° due south and 270° due west. Values below zero are below the horizon, which the
    /// renderer needs to know about and a planetarium view usually hides.
    /// </remarks>
    internal static (double Altitude, double Azimuth) AltAz(in SkyObserver observer, Fix128Vec direction)
    {
        if (!observer.HasHorizon)
        {
            throw new InvalidOperationException(
                "An observer in free space has no horizon, so it has no altitude or azimuth. " +
                "Give it a surface position, or use the direction vector directly.");
        }

        double up = Fix128Vec.Dot(direction, observer.Up).ToDouble();
        double east = Fix128Vec.Dot(direction, observer.East).ToDouble();
        double north = Fix128Vec.Dot(direction, observer.North).ToDouble();

        double altitude = Math.Asin(Math.Clamp(up, -1.0, 1.0)) * 180.0 / Math.PI;

        double azimuth = Math.Atan2(east, north) * 180.0 / Math.PI;
        if (azimuth < 0.0)
        {
            azimuth += 360.0;
        }

        return (altitude, azimuth);
    }

    /// <summary>
    /// Angular separation between two directions, in arcseconds.
    /// </summary>
    /// <remarks>
    /// Through the cross product rather than the dot product, because the interesting separations
    /// here are arcseconds and <c>acos</c> near zero loses half its significant figures to the
    /// flatness of the cosine. The sine form keeps them.
    /// </remarks>
    internal static double SeparationArcseconds(Fix128Vec a, Fix128Vec b)
    {
        Fix128Vec cross = new(
            (a.Y * b.Z) - (a.Z * b.Y),
            (a.Z * b.X) - (a.X * b.Z),
            (a.X * b.Y) - (a.Y * b.X));

        double sine = cross.Length.ToDouble();
        double cosine = Fix128Vec.Dot(a, b).ToDouble();

        return Math.Atan2(sine, cosine) * 180.0 / Math.PI * 3600.0;
    }
}
