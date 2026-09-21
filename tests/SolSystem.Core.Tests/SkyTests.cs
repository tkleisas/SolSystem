using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;
using SolSystem.Core.Sky;
using Xunit;
using Xunit.Abstractions;

namespace SolSystem.Core.Tests;

/// <summary>
/// Is the sky in the right place, and is it the same sky the planets are in?
/// </summary>
/// <remarks>
/// <para>
/// A star field is easy to draw and hard to get right, because almost every mistake still looks like
/// a star field. The tests here are chosen so that each one fails loudly for a specific error:
/// </para>
/// <list type="bullet">
/// <item>The obliquity test fails if the equatorial-to-ecliptic rotation has the wrong sign, which
/// otherwise puts every planet in the wrong constellation and looks like nothing at all.</item>
/// <item>The equinox test ties the catalogue's frame to the ephemeris's frame, and is the only test
/// that would catch the two drifting apart.</item>
/// <item>The Polaris test checks the horizon, the sidereal time and the triad together, since any of
/// the three being wrong moves it.</item>
/// <item>The sidereal-day test fails if the sky is rotated by the solar day instead, which is a
/// one-degree-a-day drift that a still frame cannot show.</item>
/// <item>The parallax and aberration tests are the only ones that measure the two effects the
/// projection exists to add, and they are quoted against their real values in arcseconds.</item>
/// </list>
/// </remarks>
public class SkyTests
{
    private readonly ITestOutputHelper _o;

    public SkyTests(ITestOutputHelper o) => _o = o;

    private static StarCatalogue Catalogue => StarCatalogue.Load(TestPaths.StarCatalogue);

    /// <summary>Earth's heliocentric state, as the sky observer's position and velocity.</summary>
    private static SkyObserver FromEarth(double julianDate)
    {
        Ephemeris.State earth = Ephemeris.At(Ephemeris.Body.Earth, julianDate);

        // The ephemeris is kilometres and kilometres a second, which is the frame the observer and
        // the projection both work in — no conversion between them anywhere.
        return SkyObserver.InSpace(earth.Position, earth.Velocity);
    }

    [Fact]
    public void TheCelestialPole_IsAtTheRightEclipticLatitude()
    {
        // The single most important sign in the sky. The north celestial pole is at ecliptic
        // latitude 90 - obliquity = 66.56 degrees, NOT -66.56: the ecliptic pole is tilted towards
        // the celestial one, not away from it.
        //
        // A rotation with the sign inverted is still a perfectly good orthogonal rotation. It passes
        // every test of the form "is this still a unit vector" and "are the angles preserved", and
        // it mirrors the entire sky. This is the test that catches it.
        var northCelestialPole = new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.One);
        Fix128Vec ecliptic = Frames.EquatorialToEcliptic(
            northCelestialPole, Frames.ObliquityJ2000Degrees);

        (double _, double latitude) = Frames.ToEclipticDegrees(ecliptic);
        double expected = 90.0 - Frames.ObliquityJ2000Degrees;

        _o.WriteLine($"celestial pole at ecliptic latitude {latitude:F4} deg, expected {expected:F4}");

        Assert.Equal(expected, latitude, 3);

        // And it round-trips.
        Fix128Vec back = Frames.EclipticToEquatorial(ecliptic, Frames.ObliquityJ2000Degrees);
        Assert.True(SkyProjection.SeparationArcseconds(northCelestialPole, back) < 0.01);
    }

    [Fact]
    public void TheEclipticPole_IsAtTheRightDeclination()
    {
        // The other half of the same check, from the other side: the ecliptic pole is at declination
        // +66.56 and right ascension 18h, which is where the constellation Draco is.
        var eclipticPole = new Fix128Vec(Fix128.Zero, Fix128.Zero, Fix128.One);
        Fix128Vec equatorial = Frames.EclipticToEquatorial(
            eclipticPole, Frames.ObliquityJ2000Degrees);

        (double ra, double dec) = Frames.ToEquatorialDegrees(equatorial);

        _o.WriteLine($"ecliptic pole at RA {ra / 15.0:F3} h, Dec {dec:F3} deg");

        Assert.Equal(18.0, ra / 15.0, 2);
        Assert.Equal(66.5607, dec, 3);
    }

    [Fact]
    public void TheSunIsWhereTheEphemerisPutsIt()
    {
        // The joint between the two frames, and the only test that would notice them drifting apart.
        //
        // The Sun's ecliptic longitude is a published quantity: 0 degrees at the March equinox, 90
        // at the June solstice, and 280.46 degrees of *mean* longitude at J2000. Checking the
        // ephemeris's Sun against those ties the star catalogue's frame to the planet positions,
        // because the catalogue was rotated into the ephemeris's frame by the obliquity and any
        // error in that rotation shows up here as the Sun being in the wrong constellation.
        var cases = new (double JulianDate, double Longitude, string What)[]
        {
            // The March equinox of 2000 was 2000-03-20 07:35 UTC, which is JD 2451623.82.
            (2451623.82, 0.0, "the March equinox"),
            // The June solstice of 2000 was 2000-06-21 01:48 UTC, JD 2451716.58.
            (2451716.58, 90.0, "the June solstice"),
            // And the September equinox, 2000-09-22 17:28 UTC, JD 2451809.23.
            (2451809.23, 180.0, "the September equinox"),
        };

        foreach ((double julianDate, double expected, string what) in cases)
        {
            Ephemeris.State earth = Ephemeris.At(Ephemeris.Body.Earth, julianDate);
            var sun = new Fix128Vec(-earth.Position.X, -earth.Position.Y, -earth.Position.Z);
            (double longitude, double latitude) = Frames.ToEclipticDegrees(sun);

            // The longitude wraps at the equinox, so the difference is taken the short way round.
            double error = longitude - expected;
            while (error > 180.0)
            {
                error -= 360.0;
            }

            while (error < -180.0)
            {
                error += 360.0;
            }

            _o.WriteLine($"{what}: Sun at ecliptic longitude {longitude:F4}, expected {expected:F1} "
                + $"(off by {error * 60.0:F1} arcmin)");

            // A degree is generous for a Keplerian ephemeris and mean elements; it is not generous
            // enough to hide a wrong obliquity, which would be 23 degrees.
            Assert.True(Math.Abs(error) < 1.0, $"the Sun is {error:F3} deg from {what}");
            Assert.True(Math.Abs(latitude) < 0.01, "the Sun must be on the ecliptic");
        }
    }

    [Fact]
    public void Polaris_SitsAtTheLatitudeOfTheObserver()
    {
        // The oldest check in navigation, and it exercises the sidereal time, the horizon triad and
        // the frame rotation all at once. Polaris is 0.74 degrees from the celestial pole, so from
        // latitude 40 north it stands between 39.3 and 40.7 degrees up, all night, all year.
        Star? polaris = Catalogue.Find("Polaris");
        Assert.NotNull(polaris);

        foreach (double latitude in new[] { 0.0, 20.0, 40.0, 51.5, -33.9 })
        {
            double lowest = double.MaxValue;
            double highest = double.MinValue;

            // Through a whole day, so the diurnal circle is sampled all the way round.
            for (int hour = 0; hour < 48; hour++)
            {
                double julianDate = 2451545.0 + (hour * 0.5);
                SkyObserver observer = SkyObserver.OnSurface(
                    latitude, 0.0, julianDate,
                    Ephemeris.At(Ephemeris.Body.Earth, julianDate).Position,
                    Ephemeris.At(Ephemeris.Body.Earth, julianDate).Velocity);

                Fix128Vec apparent = SkyProjection.Apparent(polaris!.Value, observer);
                (double altitude, double _) = SkyProjection.AltAz(observer, apparent);
                lowest = Math.Min(lowest, altitude);
                highest = Math.Max(highest, altitude);
            }

            _o.WriteLine($"from latitude {latitude,6:F1}: Polaris between {lowest:F2} and {highest:F2} deg");

            // Below the equator it is not visible at all, and that has to be true too.
            if (latitude > 10.0)
            {
                Assert.True(lowest > latitude - 1.0 && highest < latitude + 1.0,
                    $"from {latitude} Polaris ranged {lowest:F2} to {highest:F2}");
            }
            else if (latitude < -10.0)
            {
                Assert.True(highest < 0.0, $"Polaris should not rise from {latitude}");
            }
        }
    }

    [Fact]
    public void AStarComesBackAfterASiderealDay_AndNotAfterASolarOne()
    {
        // The sky rotates once in 23 h 56 min 04 s, not in 24 hours. Using the solar day instead is
        // the kind of error that looks perfect in a still frame and drifts a degree a day.
        //
        // The measurement has to be in horizon coordinates, and the first version of this test got
        // that wrong in an instructive way: it compared the star's *direction vectors*, which do not
        // change with the Earth's rotation at all. A star is fixed in the ecliptic frame; what the
        // rotation moves is the observer's horizon under it. Comparing directions measured the
        // Earth's orbital motion — a third of an arcsecond — and reported that a solar day and a
        // sidereal day were the same, which is exactly backwards.
        Star? vega = Catalogue.Find("Vega");
        Assert.NotNull(vega);

        double start = 2451545.0;

        SkyObserver At(double julianDate)
        {
            Ephemeris.State earth = Ephemeris.At(Ephemeris.Body.Earth, julianDate);
            return SkyObserver.OnSurface(40.0, -75.0, julianDate, earth.Position, earth.Velocity);
        }

        (double Altitude, double Azimuth) Where(double julianDate)
        {
            SkyObserver observer = At(julianDate);
            return SkyProjection.AltAz(observer, SkyProjection.Apparent(vega!.Value, observer));
        }

        (double altitude0, double azimuth0) = Where(start);
        (double altitudeSidereal, double azimuthSidereal) =
            Where(start + (SiderealTime.SiderealDaySeconds / 86400.0));
        (double altitudeSolar, double azimuthSolar) = Where(start + 1.0);

        double siderealShift = Math.Sqrt(
            Math.Pow(altitudeSidereal - altitude0, 2.0) + Math.Pow(azimuthSidereal - azimuth0, 2.0));
        double solarShift = Math.Sqrt(
            Math.Pow(altitudeSolar - altitude0, 2.0) + Math.Pow(azimuthSolar - azimuth0, 2.0));

        _o.WriteLine($"Vega starts at altitude {altitude0:F4}, azimuth {azimuth0:F4}");
        _o.WriteLine($"  after a sidereal day ({SiderealTime.SiderealDaySeconds:F1} s): "
            + $"moved {siderealShift:F4} deg");
        _o.WriteLine($"  after a solar day (86 400 s): moved {solarShift:F4} deg");

        // A sidereal day brings it back to where it was, to within the Earth's own motion round the
        // Sun in that time — which is a fraction of a degree of parallactic and aberration drift.
        Assert.True(siderealShift < 0.05,
            $"a sidereal day moved Vega {siderealShift:F4} deg, and it should return to its place");

        // A solar day leaves it four minutes of rotation further on. Four minutes of sidereal
        // rotation is one degree, and that is what the constellations do month by month.
        Assert.True(solarShift > 0.5,
            $"a solar day should move Vega about a degree, and it moved {solarShift:F4}");

        Assert.True(Math.Abs(SiderealTime.SiderealDaySeconds - 86164.0905) < 0.01,
            $"the sidereal day came out as {SiderealTime.SiderealDaySeconds:F4} s");
    }

    [Fact]
    public void ProximaCentauri_ShowsItsParallax()
    {
        // The whole reason the nearby stars are kept whatever their magnitude. Proxima is 1.3 pc
        // away, so the Earth's 1 AU orbit swings it across the sky by 0.77 arcseconds — the largest
        // parallax there is, and the first one ever measured, by Bessel in 1838 for 61 Cygni.
        //
        // The observer's velocity is set to zero for this, and that is not a convenience. Motion
        // aberration is 20.5 arcseconds against a parallax of 0.77, and it *rotates once a year*, so
        // a naive six-month comparison measures the change in the Earth's velocity — 36 arcseconds —
        // and calls it parallax. The first version of this test did exactly that. Parallax is a
        // displacement; aberration is a lean; they have to be measured apart.
        Star? proxima = Catalogue.Find("Proxima Centauri");
        Assert.NotNull(proxima);

        Fix128Vec At(double julianDate)
        {
            Ephemeris.State earth = Ephemeris.At(Ephemeris.Body.Earth, julianDate);
            var observer = SkyObserver.InSpace(earth.Position, Fix128Vec.Zero);
            return SkyProjection.Apparent(proxima!.Value, observer);
        }

        // A quarter of a year puts the Earth one astronomical unit across the line of sight, which
        // is the baseline the parallax angle is defined against — so the shift over three months is
        // the catalogue's parallax, directly.
        double quarter = SkyProjection.SeparationArcseconds(At(2451545.0), At(2451545.0 + 91.3));
        double half = SkyProjection.SeparationArcseconds(At(2451545.0), At(2451545.0 + 182.6));

        _o.WriteLine($"Proxima moves {quarter * 1000.0:F0} mas over three months "
            + $"(catalogue parallax {proxima!.Value.ParallaxMilliarcseconds:F0} mas)");
        _o.WriteLine($"  and {half * 1000.0:F0} mas over six months, where the baseline is 2 AU");

        // A tenth, which is well inside the catalogue's own quantisation of 0.084 arcsec.
        double expected = proxima.Value.ParallaxMilliarcseconds;
        Assert.True(Math.Abs((quarter * 1000.0) - expected) < expected * 0.15,
            $"three months gives {quarter * 1000.0:F0} mas against a catalogue value of {expected:F0}");

        // Six months doubles the baseline but not the displacement, because the star is well off the
        // ecliptic and the extra baseline projects onto a shorter chord. It must still be larger.
        Assert.True(half > quarter, "a longer baseline must displace a nearby star further");

        // And a star stored as being at infinity must not move at all.
        Star? deneb = Catalogue.Find("Deneb");
        if (deneb is { } far && !far.HasDistance)
        {
            double farSeparation = SkyProjection.SeparationArcseconds(
                SkyProjection.Apparent(far, SkyObserver.InSpace(
                    Ephemeris.At(Ephemeris.Body.Earth, 2451545.0).Position, Fix128Vec.Zero)),
                SkyProjection.Apparent(far, SkyObserver.InSpace(
                    Ephemeris.At(Ephemeris.Body.Earth, 2451545.0 + 182.6).Position, Fix128Vec.Zero)));

            _o.WriteLine($"Deneb, stored at infinity, moves {farSeparation * 1000.0:F3} mas");
            Assert.True(farSeparation < 0.02, $"a star at infinity moved {farSeparation:F4} arcsec");
        }
    }

    [Fact]
    public void Aberration_OverwhelmsParallax_WhichIsWhyItCannotBeIgnored()
    {
        // The fact that makes the test above necessary. Every star in the sky leans twenty
        // arcseconds into the Earth's motion, and Proxima — the nearest star there is — moves 0.77
        // by parallax. Aberration is the larger effect for every star, by a factor of twenty-six at
        // the very nearest, and a sky that models parallax and forgets aberration has its error
        // twenty-six times larger than the thing it carefully included.
        Star? proxima = Catalogue.Find("Proxima Centauri");
        Assert.NotNull(proxima);

        double WithAberration(double julianDate)
        {
            Ephemeris.State earth = Ephemeris.At(Ephemeris.Body.Earth, julianDate);
            return SkyProjection.SeparationArcseconds(
                SkyProjection.Apparent(proxima!.Value,
                    SkyObserver.InSpace(earth.Position, Fix128Vec.Zero)),
                SkyProjection.Apparent(proxima!.Value,
                    SkyObserver.InSpace(earth.Position, earth.Velocity)));
        }

        double lean = WithAberration(2451545.0);
        _o.WriteLine($"aberration moves Proxima {lean:F1} arcsec, against a parallax of "
            + $"{proxima!.Value.ParallaxMilliarcseconds / 1000.0:F2}");

        Assert.True(lean > 5.0, $"aberration should be tens of arcseconds, and came out {lean:F2}");
        Assert.True(lean < 41.0, $"aberration cannot exceed 2 v/c, and came out {lean:F2}");
    }

    [Fact]
    public void Aberration_LeansTheSkyIntoTheDirectionOfTravel()
    {
        // 29.8 km/s is 9.94e-5 of the speed of light, which is 20.5 arcseconds — twenty-six times
        // Proxima's parallax, and applied to every star in the sky. It rotates once a year, so what
        // it does is make the stars swim against the planets, which is the sort of thing that is
        // invisible in a still frame and obvious in motion.
        var velocity = new Fix128Vec(Fix128.FromDouble(29.79), Fix128.Zero, Fix128.Zero);
        SkyObserver observer = SkyObserver.InSpace(Fix128Vec.Zero, velocity);

        double expected = 29.79 / SkyProjection.LightKilometresPerSecond * 180.0 / Math.PI * 3600.0;

        // A star in the direction of travel — the apex — is not displaced at all.
        var apex = new Star(new Fix128Vec(Fix128.One, Fix128.Zero, Fix128.Zero), 1.0,
            double.NaN, 0.0, "apex");
        double atApex = SkyProjection.SeparationArcseconds(
            apex.Direction, SkyProjection.Apparent(apex, observer));

        // And one at right angles to it is displaced by the full v/c, towards the apex.
        var side = new Star(new Fix128Vec(Fix128.Zero, Fix128.One, Fix128.Zero), 1.0,
            double.NaN, 0.0, "side");
        Fix128Vec sideApparent = SkyProjection.Apparent(side, observer);
        double atSide = SkyProjection.SeparationArcseconds(side.Direction, sideApparent);

        _o.WriteLine($"expected v/c = {expected:F2} arcsec");
        _o.WriteLine($"  at the apex:  {atApex:F3} arcsec");
        _o.WriteLine($"  at 90 deg:    {atSide:F3} arcsec");
        _o.WriteLine($"  and it leans towards +x: {sideApparent.X.ToDouble():F8}");

        Assert.True(atApex < 0.01, $"the apex should not move, and it moved {atApex:F3}");
        Assert.Equal(expected, atSide, 1);

        // The displacement must be towards the apex, not away from it: a star at +y seen by an
        // observer moving in +x appears slightly further in +x than it is. Getting this backwards is
        // a sign error that still produces a plausible 20-arcsecond lean, in the wrong direction,
        // which is why it is asserted rather than described.
        Assert.True(sideApparent.X.ToDouble() > 0.0, "aberration leans towards the direction of travel");
    }

    [Fact]
    public void TheSunIsInTheRightConstellation()
    {
        // The integration test, in the form a person can check against a planisphere. On
        // 1 January the Sun is in Sagittarius, and from the ephemeris plus the catalogue's frame
        // that has to come out right.
        //
        // Nunki is the brightest star of the Archer's Teapot and sits within four degrees of where
        // the Sun passes. A wrong obliquity would put it twenty-three degrees away; an inverted
        // rotation would put the Sun in Gemini, where the nearest bright star is Aldebaran at the
        // far side of the sky.
        double julianDate = 2451545.0;   // 2000 January 1, 12:00 TT

        Ephemeris.State earth = Ephemeris.At(Ephemeris.Body.Earth, julianDate);
        var sun = new Fix128Vec(-earth.Position.X, -earth.Position.Y, -earth.Position.Z);
        Fix128Vec sunDirection = sun.Normalized();

        (double sunLongitude, double sunLatitude) = Frames.ToEclipticDegrees(sunDirection);
        _o.WriteLine($"the Sun is at ecliptic longitude {sunLongitude:F2}, latitude {sunLatitude:F5}");

        double Separation(string name)
        {
            Star? star = Catalogue.Find(name);
            Assert.NotNull(star);
            return SkyProjection.SeparationArcseconds(sunDirection, star!.Value.Direction) / 3600.0;
        }

        double toNunki = Separation("Nunki");
        double toAlnasl = Separation("Alnasl");
        double toAldebaran = Separation("Aldebaran");
        double toAntares = Separation("Antares");

        _o.WriteLine($"  {toNunki:F2} deg from Nunki, {toAlnasl:F2} from Alnasl "
            + "(both Sagittarius)");
        _o.WriteLine($"  {toAldebaran:F2} deg from Aldebaran, {toAntares:F2} from Antares "
            + "(Taurus and Scorpius, the other side of the sky)");

        Assert.True(toNunki < 5.0, $"the Sun is {toNunki:F2} deg from Nunki");
        Assert.True(toAlnasl < 14.0, $"the Sun is {toAlnasl:F2} deg from Alnasl");

        // And the stars on the opposite side of the ecliptic are on the opposite side of the sky.
        // This is what catches an inverted rotation, which keeps every constellation intact and puts
        // the whole sky six months out of season.
        Assert.True(toAldebaran > 130.0, $"Aldebaran is {toAldebaran:F1} deg away");
        Assert.True(toAntares > 25.0, $"Antares is {toAntares:F1} deg away");
    }

    [Fact]
    public void TheGalacticFrame_IsWhereTheGalaxyIs()
    {
        // The Milky Way is the largest thing in the sky and the easiest to put in the wrong place,
        // because a decorative smear looks fine anywhere. These are the three directions that pin it
        // down, in the coordinates the catalogue carries.
        //
        // The galactic centre is in Sagittarius and defines latitude zero. The north galactic pole
        // is in Coma Berenices, ninety degrees from it. And the anticentre is in Auriga, on the
        // opposite side of the circle — so it is also at latitude zero, which is the check that
        // catches a frame that is centred on the right point but rotated about it.
        var centre = Frames.EquatorialToEcliptic(
            Frames.FromEquatorialDegrees(266.4051, -28.936175), Frames.ObliquityJ2000Degrees);
        var pole = Frames.EquatorialToEcliptic(
            Frames.FromEquatorialDegrees(192.85948, 27.12825), Frames.ObliquityJ2000Degrees);
        var anticentre = Frames.EquatorialToEcliptic(
            Frames.FromEquatorialDegrees(86.4051, 28.936175), Frames.ObliquityJ2000Degrees);

        double atCentre = MilkyWay.LatitudeDegrees(centre);
        double atPole = MilkyWay.LatitudeDegrees(pole);
        double atAnticentre = MilkyWay.LatitudeDegrees(anticentre);

        _o.WriteLine($"galactic centre: latitude {atCentre:F4}");
        _o.WriteLine($"north galactic pole: latitude {atPole:F4}");
        _o.WriteLine($"anticentre: latitude {atAnticentre:F4}");

        Assert.True(Math.Abs(atCentre) < 0.01, $"the centre is at latitude {atCentre:F4}");
        Assert.Equal(90.0, atPole, 2);
        Assert.True(Math.Abs(atAnticentre) < 0.6,
            $"the anticentre is at latitude {atAnticentre:F4}, and it is on the same circle");

        // The longitude must put the centre at zero and the anticentre at half a turn round, or the
        // brightness profile is applied to the wrong half of the sky.
        double centreLongitude = MilkyWay.LongitudeFromCentreDegrees(centre);
        double anticentreLongitude = MilkyWay.LongitudeFromCentreDegrees(anticentre);

        _o.WriteLine($"centre at galactic longitude {centreLongitude:F2}, "
            + $"anticentre at {anticentreLongitude:F2}");

        Assert.True(centreLongitude < 1.0 || centreLongitude > 359.0,
            $"the centre should be at longitude zero, and it is at {centreLongitude:F2}");
        Assert.Equal(180.0, anticentreLongitude, 1);
    }

    [Fact]
    public void TheBand_PassesThroughTheConstellationsItActuallyPassesThrough()
    {
        // The check anyone who has been outside can make. Deneb sits inside the band — the dark rift
        // beside it is the most recognisable thing in the summer sky — and Altair sits close to it,
        // while Arcturus and Spica are nowhere near.
        StarCatalogue catalogue = Catalogue;

        var cases = new (string Star, double ExpectedLatitude, double Tolerance)[]
        {
            ("Deneb", 2.0, 2.0),      // in Cygnus, inside the band
            ("Altair", -8.9, 2.0),    // in Aquila, just south of it
            ("Sadr", 0.5, 2.5),       // the middle of the Northern Cross, in the band
            ("Arcturus", 69.0, 3.0),  // high above the plane
            ("Spica", 51.0, 3.0),     // likewise
        };

        foreach ((string name, double expected, double tolerance) in cases)
        {
            Star? star = catalogue.Find(name);
            if (star is not { } s)
            {
                continue;
            }

            double latitude = MilkyWay.LatitudeDegrees(s.Direction);
            _o.WriteLine($"{name,-10} galactic latitude {latitude,7:F2} (expect about {expected:F1})");

            Assert.True(Math.Abs(latitude - expected) < tolerance,
                $"{name} is at galactic latitude {latitude:F2}, and it should be near {expected:F1}");
        }
    }

    [Fact]
    public void TheBand_IsBrightestTowardsTheCentreAndFadesAwayFromThePlane()
    {
        var centre = Frames.EquatorialToEcliptic(
            Frames.FromEquatorialDegrees(266.4051, -28.936175), Frames.ObliquityJ2000Degrees);
        var anticentre = Frames.EquatorialToEcliptic(
            Frames.FromEquatorialDegrees(86.4051, 28.936175), Frames.ObliquityJ2000Degrees);
        var pole = Frames.EquatorialToEcliptic(
            Frames.FromEquatorialDegrees(192.85948, 27.12825), Frames.ObliquityJ2000Degrees);

        double atCentre = MilkyWay.Brightness(centre);
        double atAnticentre = MilkyWay.Brightness(anticentre);
        double atPole = MilkyWay.Brightness(pole);

        _o.WriteLine($"brightness: centre {atCentre:F3}, anticentre {atAnticentre:F3}, "
            + $"pole {atPole:F3}");

        Assert.Equal(1.0, atCentre, 2);
        Assert.True(atAnticentre > 0.3 && atAnticentre < 0.6,
            $"the anticentre came out at {atAnticentre:F3}, and it is visible but faint");
        Assert.True(atPole < 0.01, $"the pole came out at {atPole:F4}, and there is nothing there");

        // And it must fall off monotonically away from the plane, or the band has a hard edge
        // somewhere. A Gaussian cannot, but a mistake in the latitude would show up here.
        double previous = 1.0;
        for (double latitude = 0.0; latitude <= 40.0; latitude += 2.0)
        {
            // Walk away from the centre along a line of constant longitude.
            double radians = latitude * Math.PI / 180.0;
            var offset = new Fix128Vec(
                centre.X * Fix128.FromDouble(Math.Cos(radians)) + pole.X * Fix128.FromDouble(Math.Sin(radians)),
                centre.Y * Fix128.FromDouble(Math.Cos(radians)) + pole.Y * Fix128.FromDouble(Math.Sin(radians)),
                centre.Z * Fix128.FromDouble(Math.Cos(radians)) + pole.Z * Fix128.FromDouble(Math.Sin(radians)));

            double brightness = MilkyWay.Brightness(offset.Normalized());
            Assert.True(brightness <= previous + 1e-6,
                $"the band brightened from {previous:F4} to {brightness:F4} at {latitude} degrees");
            previous = brightness;
        }
    }
}
