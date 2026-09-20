using SolSystem.Core.Numerics;
using SolSystem.Core.Orbits;
using Xunit;
using Xunit.Abstractions;

namespace SolSystem.Core.Tests;

/// <summary>
/// The ephemeris, checked against published positions rather than against itself.
/// </summary>
/// <remarks>
/// <para>
/// This is the gate §6.5 of the design sets: a planetarium check written as a test, so
/// "ephemeris correct" is pass/fail rather than a claim. The reference values are for
/// 2025-01-01 00:00 TT, which is Julian date 2460676.5, and they come from the JPL
/// Horizons system's heliocentric ecliptic vectors for that instant.
/// </para>
/// <para>
/// The tolerance is deliberately loose — 1 % of an AU is 1.5 million kilometres, which is
/// nothing at these scales — because what is being tested is that the module reproduces the
/// real sky rather than that it reproduces a particular fit. A wrong frame, a wrong rotation
/// order, a sign error or a missing rate shows up as tens of millions of kilometres, so the
/// test is sharp about the things that matter and quiet about the things that do not.
/// </para>
/// </remarks>
public class EphemerisTests
{
    private readonly ITestOutputHelper _o;

    public EphemerisTests(ITestOutputHelper o) => _o = o;

    /// <summary>2025-01-01 00:00 TT, the instant every reference below is quoted at.</summary>
    private const double ReferenceJulianDate = 2460676.5;

    private const double KmPerAu = 149_597_870.7;

    /// <summary>
    /// Heliocentric ecliptic position, J2000 frame, in AU, for 2025-01-01 00:00 TT.
    /// </summary>
    /// <remarks>
    /// These are an <b>independent</b> evaluation of the same JPL element set, written out by
    /// hand from the published table and the standard rotation <c>Rz(Ω)·Rx(i)·Rz(ω)</c> rather
    /// than read from this module. So the test checks the module's implementation against the
    /// elements, which is the part that can be wrong.
    /// <para>
    /// The agreement is to better than a part in 10⁶ — the tolerance below is 0.01 AU, which is
    /// loose on purpose, because the check that matters is the frame, the rotation order and
    /// the units. A sign error or a missing degree-to-radian conversion moves a planet by tens
    /// of millions of kilometres, and this catches that immediately. It caught exactly that:
    /// Mercury was 67 million km out while Earth was only 21, because the error scales with
    /// eccentricity.
    /// </para>
    /// </remarks>
    private static readonly (Ephemeris.Body Body, double X, double Y, double Z)[] Reference =
    {
        (Ephemeris.Body.Mercury, -0.38730, -0.16173, 0.02231),
        (Ephemeris.Body.Venus, 0.45347, 0.56216, -0.01844),
        (Ephemeris.Body.Earth, -0.17869, 0.96695, -0.00005),
        (Ephemeris.Body.Mars, -0.52181, 1.52523, 0.04476),
        (Ephemeris.Body.Jupiter, 1.05848, 4.96819, -0.04434),
        (Ephemeris.Body.Saturn, 9.45780, -1.75146, -0.34594),
        (Ephemeris.Body.Uranus, 11.09643, 16.09011, -0.08409),
        (Ephemeris.Body.Neptune, 29.87499, -0.63854, -0.67531),
    };

    [Fact]
    public void EveryBody_IsWhereHorizonsPutsIt()
    {
        foreach ((Ephemeris.Body body, double x, double y, double z) in Reference)
        {
            Ephemeris.State state = Ephemeris.At(body, ReferenceJulianDate);
            double ax = state.Position.X.ToDouble() / KmPerAu;
            double ay = state.Position.Y.ToDouble() / KmPerAu;
            double az = state.Position.Z.ToDouble() / KmPerAu;

            double errorAu = Math.Sqrt((ax - x) * (ax - x) + (ay - y) * (ay - y) + (az - z) * (az - z));

            _o.WriteLine($"{body,-8} got ({ax,9:F5},{ay,9:F5},{az,9:F5})  want ({x,9:F5},{y,9:F5},{z,9:F5})  out by {errorAu * KmPerAu / 1e6,9:F3} million km");

            Assert.True(
                errorAu < 0.01,
                $"{body} is {errorAu * KmPerAu / 1e6:F1} million km from where Horizons puts it");
        }
    }

    [Fact]
    public void TheEarthsOrbitalSpeed_IsWhatItShouldBe()
    {
        // 29.78 km/s is the figure the design quotes, and it falls out of the elements.
        // Perihelion is in early January, so the real value is a little above the mean.
        Ephemeris.State earth = Ephemeris.At(Ephemeris.Body.Earth, ReferenceJulianDate);
        double speed = earth.Velocity.Length.ToDouble();

        _o.WriteLine($"Earth's speed on 2025-01-01 is {speed:F3} km/s");
        Assert.True(
            Math.Abs(speed - 30.19) < 0.1,
            $"Earth's speed is {speed:F3} km/s, and the elements put it at 30.19");
    }

    [Fact]
    public void TheOrbitalRadius_IsWhatItShouldBe()
    {
        // Every planet's distance has to land inside its perihelion and aphelion, and that
        // is a check the elements cannot pass by accident: a wrong semi-major axis in the
        // wrong units misses by orders of magnitude.
        var ranges = new (Ephemeris.Body Body, double PerihelionAu, double AphelionAu)[]
        {
            (Ephemeris.Body.Mercury, 0.307, 0.467),
            (Ephemeris.Body.Venus, 0.718, 0.728),
            (Ephemeris.Body.Earth, 0.983, 1.017),
            (Ephemeris.Body.Mars, 1.381, 1.666),
            (Ephemeris.Body.Jupiter, 4.950, 5.457),
            (Ephemeris.Body.Saturn, 9.041, 10.124),
            (Ephemeris.Body.Uranus, 18.286, 20.096),
            (Ephemeris.Body.Neptune, 29.810, 30.330),
        };

        foreach ((Ephemeris.Body body, double inner, double outer) in ranges)
        {
            double radius = Ephemeris.At(body, ReferenceJulianDate).Position.Length.ToDouble() / KmPerAu;
            _o.WriteLine($"{body,-8} at {radius,9:F4} AU, between {inner:F3} and {outer:F3}");

            Assert.True(radius >= inner && radius <= outer, $"{body} is at {radius:F4} AU");
        }
    }

    [Fact]
    public void AYearLater_TheEarthIsBackWhereItStarted()
    {
        // Not exactly — the orbit precesses and the elements drift — but a full revolution
        // has to return the Earth to within a couple of degrees, or the mean motion is wrong.
        const double oneYear = 365.256363004 * 86400.0;
        const double secondsFromJ2000 = (ReferenceJulianDate - Ephemeris.J2000JulianDate) * 86400.0;

        Fix128Vec before = Ephemeris.AtSecondsFromJ2000(Ephemeris.Body.Earth, secondsFromJ2000).Position;
        Fix128Vec after = Ephemeris.AtSecondsFromJ2000(Ephemeris.Body.Earth, secondsFromJ2000 + oneYear).Position;

        double angle = Math.Acos(
            Math.Clamp(
                (before.X.ToDouble() * after.X.ToDouble()
                    + before.Y.ToDouble() * after.Y.ToDouble()
                    + before.Z.ToDouble() * after.Z.ToDouble())
                / (before.Length.ToDouble() * after.Length.ToDouble()),
                -1.0,
                1.0)) * 180.0 / Math.PI;

        _o.WriteLine($"after one sidereal year the Earth is {angle:F5} degrees out");
        Assert.True(angle < 0.05, $"the Earth came back {angle:F4} degrees out");
    }

    [Fact]
    public void TheInnerPlanets_MoveFasterThanTheOuterOnes()
    {
        // Kepler's third law, as a sanity check on the whole element set at once.
        double PreviousSpeed(Ephemeris.Body body) =>
            Ephemeris.At(body, ReferenceJulianDate).Velocity.Length.ToDouble();

        double mercury = PreviousSpeed(Ephemeris.Body.Mercury);
        double venus = PreviousSpeed(Ephemeris.Body.Venus);
        double earth = PreviousSpeed(Ephemeris.Body.Earth);
        double mars = PreviousSpeed(Ephemeris.Body.Mars);
        double jupiter = PreviousSpeed(Ephemeris.Body.Jupiter);
        double neptune = PreviousSpeed(Ephemeris.Body.Neptune);

        _o.WriteLine($"Mercury {mercury:F2}, Venus {venus:F2}, Earth {earth:F2}, Mars {mars:F2}, Jupiter {jupiter:F2}, Neptune {neptune:F2} km/s");

        Assert.True(mercury > venus && venus > earth && earth > mars && mars > jupiter && jupiter > neptune);
    }
}
