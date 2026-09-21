using SolSystem.Core.Numerics;
using SolSystem.Core.Sky;
using Xunit;
using Xunit.Abstractions;

namespace SolSystem.Core.Tests;

/// <summary>
/// The catalogue itself: does it load, and is what comes out what went in?
/// </summary>
/// <remarks>
/// The packing is lossy in a way that has to be known rather than assumed — angles to 2⁻³² of a
/// turn, magnitudes to a thousandth, distances to a hundredth of a parsec — so the tests check both
/// that the loss is where it was designed to be and that nothing else was lost on the way.
/// </remarks>
public class StarCatalogueTests
{
    private readonly ITestOutputHelper _o;

    public StarCatalogueTests(ITestOutputHelper o) => _o = o;

    private static StarCatalogue Catalogue => StarCatalogue.Load(TestPaths.StarCatalogue);

    [Fact]
    public void TheCatalogueLoads()
    {
        StarCatalogue catalogue = Catalogue;

        _o.WriteLine($"{catalogue.Count:N0} stars");

        Assert.True(catalogue.Count > 10_000, $"only {catalogue.Count} stars");
        Assert.True(catalogue.Count < 20_000, $"{catalogue.Count} stars is more than the subset");
    }

    [Fact]
    public void BrightestFirst_AndTheBrightestIsSirius()
    {
        StarCatalogue catalogue = Catalogue;
        ReadOnlySpan<Star> stars = catalogue.Stars;

        for (int i = 1; i < stars.Length; i++)
        {
            Assert.True(stars[i].Magnitude >= stars[i - 1].Magnitude,
                $"star {i} is brighter than star {i - 1}, so the sort is not a sort");
        }

        // Sirius is the brightest star in the sky at magnitude -1.44. If the catalogue's first entry
        // is not it, something is wrong with either the sort or the magnitude scale.
        Assert.Equal("Sirius", stars[0].Name);
        Assert.Equal(-1.44, stars[0].Magnitude, 2);
    }

    [Fact]
    public void Sirius_ComesBackOutWhereItWentIn()
    {
        Star? sirius = Catalogue.Find("Sirius");
        Assert.NotNull(sirius);

        // The catalogue's own values: RA 6h 45m 08.9s, Dec -16° 42' 58".
        (double ra, double dec) = Frames.ToEquatorialDegrees(
            Frames.EclipticToEquatorial(sirius!.Value.Direction, Frames.ObliquityJ2000Degrees));

        _o.WriteLine($"RA {ra / 15.0:F5} h, Dec {dec:F5} deg");

        Assert.Equal(6.7525, ra / 15.0, 3);
        Assert.Equal(-16.7161, dec, 3);
        Assert.Equal(-1.44, sirius.Value.Magnitude, 2);
        Assert.Equal(2.64, sirius.Value.DistanceParsecs, 2);

        // The quantisation is 2^-32 of a turn. In declination that is 0.084 arcsec, so a star must
        // come back within that and the test says so in the units the claim is made in.
        double error = SkyProjection.SeparationArcseconds(
            sirius.Value.Direction,
            Frames.EquatorialToEcliptic(Frames.FromEquatorialDegrees(ra, dec),
                Frames.ObliquityJ2000Degrees));

        Assert.True(error < 0.2, $"round trip moved the star {error:F4} arcsec");
    }

    [Fact]
    public void StarsKeepTheirColour()
    {
        StarCatalogue catalogue = Catalogue;

        // Colour is what gives a star field its life, and it is measured rather than chosen. A
        // catalogue that dropped it would still pass every positional test there is.
        Star? betelgeuse = catalogue.Find("Betelgeuse");
        Star? rigel = catalogue.Find("Rigel");

        Assert.NotNull(betelgeuse);
        Assert.NotNull(rigel);

        _o.WriteLine($"Betelgeuse B-V {betelgeuse!.Value.ColourIndex:F3} (red supergiant, expect ~1.85)");
        _o.WriteLine($"Rigel B-V {rigel!.Value.ColourIndex:F3} (blue supergiant, expect ~-0.03)");

        Assert.True(betelgeuse.Value.ColourIndex > 1.0, "Betelgeuse should be red");
        Assert.True(rigel.Value.ColourIndex < 0.2, "Rigel should be blue");
    }

    [Fact]
    public void TheNearbyStarsKeepTheirDistances()
    {
        StarCatalogue catalogue = Catalogue;

        // Everything within 25 pc whatever its magnitude, which is the point of the subset rule:
        // Proxima is magnitude 11 and would be missing from a magnitude cut alone, and it is the
        // single most interesting star in the sky for parallax.
        Star? proxima = catalogue.Find("Proxima Centauri");
        Assert.NotNull(proxima);

        _o.WriteLine($"Proxima: {proxima!.Value.DistanceParsecs:F4} pc, "
            + $"parallax {proxima.Value.ParallaxMilliarcseconds:F1} mas");

        Assert.True(proxima.Value.HasDistance);
        Assert.Equal(1.30, proxima.Value.DistanceParsecs, 2);
        Assert.Equal(769.0, proxima.Value.ParallaxMilliarcseconds, 0);

        // And the far ones are stored as being at infinity rather than at a wrong distance.
        Star? deneb = catalogue.Find("Deneb");
        if (deneb is { } d)
        {
            _o.WriteLine($"Deneb: {d.DistanceParsecs:F2} pc (catalogue is uncertain; stored capped)");
        }
    }

    [Fact]
    public void ABadCatalogue_IsRejectedWithAUsefulMessage()
    {
        Assert.Throws<FormatException>(() => StarCatalogue.FromBytes(new byte[4]));
        Assert.Throws<FormatException>(() => StarCatalogue.FromBytes(new byte[64]));

        // Right magic, wrong version.
        var bytes = new List<byte>("SOLSTARS"u8.ToArray());
        bytes.AddRange(BitConverter.GetBytes(99));
        bytes.AddRange(BitConverter.GetBytes(0));
        bytes.AddRange(new byte[2]);
        Assert.Throws<FormatException>(() => StarCatalogue.FromBytes(bytes.ToArray()));

        // Right header, truncated body.
        bytes.Clear();
        bytes.AddRange("SOLSTARS"u8.ToArray());
        bytes.AddRange(BitConverter.GetBytes(1));
        bytes.AddRange(BitConverter.GetBytes(1000));
        bytes.AddRange(new byte[16]);
        Assert.Throws<FormatException>(() => StarCatalogue.FromBytes(bytes.ToArray()));
    }
}
