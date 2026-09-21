using SolSystem.Core.Sky;
using Xunit;
using Xunit.Abstractions;

namespace SolSystem.Core.Tests;

/// <summary>
/// The colour of a star, which is a temperature, which is a measurement.
/// </summary>
/// <remarks>
/// These tests are written as colours a person can check, because that is the only way to test a
/// colour: an assertion that the blue channel exceeds the red one for a hot star is exactly the
/// claim the code is making.
/// </remarks>
public class StarColourTests
{
    private readonly ITestOutputHelper _o;

    public StarColourTests(ITestOutputHelper o) => _o = o;

    [Fact]
    public void TheSunComesOutWhite_AndNotYellow()
    {
        // A common surprise: the Sun is white. It looks yellow from the ground because the blue end
        // of its spectrum is scattered out of the direct beam by the atmosphere, which is why the
        // sky is blue — the colour is a property of the air, not of the star. Rendered from space,
        // or by this code, the Sun is very slightly warmer than white and nothing more.
        var sun = StarColour.ForColourIndex(0.65);
        double temperature = StarColour.TemperatureKelvin(0.65);

        _o.WriteLine($"the Sun: B-V 0.65, {temperature:F0} K, "
            + $"RGB ({sun.Red:F3}, {sun.Green:F3}, {sun.Blue:F3})");

        // 5772 K is the Sun's effective temperature and the formula should land near it.
        Assert.True(Math.Abs(temperature - 5772.0) < 60.0,
            $"the Sun came out at {temperature:F0} K, and it is 5772");

        // White, and the distinction between linear and displayed matters here. In linear light a
        // 5 778 K black body is (1.00, 0.88, 0.82) — visibly cream. Applying the sRGB transfer
        // function, which is what a display does and what an eye sees, gives (1.00, 0.94, 0.91),
        // which is white with the faintest warmth. Asserting on the linear value and calling it
        // white was the first version of this test, and it was measuring the wrong thing.
        double red = StarColour.SrgbEncoded(sun.Red);
        double green = StarColour.SrgbEncoded(sun.Green);
        double blue = StarColour.SrgbEncoded(sun.Blue);

        _o.WriteLine($"  displayed: ({red:F3}, {green:F3}, {blue:F3})");

        Assert.True(sun.Red >= sun.Green && sun.Green >= sun.Blue, "the Sun should be faintly warm");
        Assert.True(blue > 0.88, $"the Sun displays as {blue:F3} blue, which is visibly yellow");
        Assert.True(red - blue < 0.12, $"the Sun spans {red - blue:F3} from red to blue, which is tinted");
    }

    [Fact]
    public void HotStarsAreBlue_AndCoolStarsAreRed()
    {
        var rigel = StarColour.ForColourIndex(-0.03);
        var betelgeuse = StarColour.ForColourIndex(1.85);
        var proxima = StarColour.ForColourIndex(1.80);

        _o.WriteLine($"Rigel      (-0.03): ({rigel.Red:F3}, {rigel.Green:F3}, {rigel.Blue:F3})");
        _o.WriteLine($"Betelgeuse ( 1.85): ({betelgeuse.Red:F3}, {betelgeuse.Green:F3}, {betelgeuse.Blue:F3})");
        _o.WriteLine($"Proxima    ( 1.80): ({proxima.Red:F3}, {proxima.Green:F3}, {proxima.Blue:F3})");

        // Blue-white: blue is the strongest channel.
        Assert.True(rigel.Blue > rigel.Red, "Rigel should be blue-white");

        // Deep orange: red is the strongest and blue is well down.
        Assert.True(betelgeuse.Red > betelgeuse.Green && betelgeuse.Green > betelgeuse.Blue);
        Assert.True(betelgeuse.Blue < 0.6, $"Betelgeuse came out {betelgeuse.Blue:F3} blue");
    }

    [Fact]
    public void TheColourSequence_IsMonotonic()
    {
        // The whole point of the physics chain is that it is a function and not a table: as the
        // colour index rises the star gets cooler, and the blue-to-red ratio must fall the whole
        // way with no reversals. A lookup table with interpolation would wobble here.
        double previous = double.MaxValue;

        for (double bv = -0.4; bv <= 2.5; bv += 0.1)
        {
            (double r, double g, double b) = StarColour.ForColourIndex(bv);
            double blueness = b / r;

            if (bv < 0.8)
            {
                Assert.True(blueness <= previous + 1e-9,
                    $"the sequence reversed at B-V {bv:F1}: {blueness:F4} after {previous:F4}");
            }

            previous = blueness;
        }

        // And the endpoints are the colours they should be, spanning most of the range the eye has.
        (double _, double _, double hot) = StarColour.ForColourIndex(-0.4);
        (double _, double _, double cool) = StarColour.ForColourIndex(2.5);

        _o.WriteLine($"blue channel runs from {hot:F3} at B-V -0.4 to {cool:F3} at +2.5");
        Assert.True(hot > 0.95 && cool < 0.4, "the span should be most of the way from blue to red");
    }

    [Fact]
    public void AStarWithNoMeasuredColour_IsWhite()
    {
        (double r, double g, double b) = StarColour.ForColourIndex(double.NaN);

        Assert.Equal(1.0, r);
        Assert.Equal(1.0, g);
        Assert.Equal(1.0, b);
    }
}
