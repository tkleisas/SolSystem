namespace SolSystem.Core.Sky;

/// <summary>
/// What colour a star is, from the one number the catalogue measured.
/// </summary>
/// <remarks>
/// <para>
/// A star's colour is not a choice. It is the colour of a black body at the star's surface
/// temperature, and the catalogue carries a measurement — the B−V colour index — from which that
/// temperature follows. So the whole chain is physics with one empirical step in it:
/// </para>
/// <list type="number">
/// <item>B−V to temperature, by Ballesteros' formula, which is a two-parameter fit to the
/// observed main sequence and good to a few hundred kelvin across the range the catalogue
/// covers.</item>
/// <item>Temperature to a spectrum, by Planck's law.</item>
/// <item>Spectrum to RGB, by integrating against the CIE colour-matching functions and converting
/// to sRGB.</item>
/// </list>
/// <para>
/// The alternative — a lookup table of "blue, white, yellow, orange, red" — is what most games do
/// and it throws away the measurement. The difference shows in a sky: with the real relation,
/// Betelgeuse comes out a deep orange and Rigel a hard blue-white, and the two are adjacent in
/// Orion, which is exactly the contrast the eye notices.
/// </para>
/// <para>
/// The result is <em>chromaticity only</em>, normalised so the brightest channel is one. Brightness
/// comes from the magnitude, separately, and mixing the two here would make the colour of a star
/// depend on how far away it is.
/// </para>
/// </remarks>
internal static class StarColour
{
    /// <summary>
    /// Effective temperature in kelvin from a B−V colour index.
    /// </summary>
    /// <remarks>
    /// Ballesteros, <i>New insights into black bodies</i> (2012). A fit rather than a table, which
    /// matters here because the catalogue's colour indices run from about −0.4 for the hottest
    /// stars to +2.5 for the coolest, and a table would need interpolating at every star anyway.
    /// </remarks>
    internal static double TemperatureKelvin(double colourIndex)
    {
        double bv = colourIndex;
        return 4600.0 * ((1.0 / ((0.92 * bv) + 1.7)) + (1.0 / ((0.92 * bv) + 0.62)));
    }

    /// <summary>
    /// Linear sRGB for a black body at a temperature, normalised so the largest channel is one.
    /// </summary>
    /// <remarks>
    /// Integrated properly rather than approximated: Planck's law against the CIE 1931
    /// colour-matching functions at 5 nm, then the sRGB primaries, then a clamp of the small
    /// negatives that the primaries produce outside the gamut. It costs a few hundred multiplications
    /// per distinct temperature and there are only a few thousand distinct colour indices in the
    /// catalogue, so a renderer should cache it per star rather than per frame.
    /// </remarks>
    internal static (double Red, double Green, double Blue) LinearRgb(double temperatureKelvin)
    {
        double x = 0.0;
        double y = 0.0;
        double z = 0.0;

        for (int i = 0; i < Wavelengths.Length; i++)
        {
            double lambda = Wavelengths[i];
            double radiance = Planck(lambda, temperatureKelvin);
            x += radiance * CieX[i];
            y += radiance * CieY[i];
            z += radiance * CieZ[i];
        }

        // CIE XYZ to linear sRGB, the standard matrix for the sRGB primaries and D65 white.
        double r = (3.2406 * x) - (1.5372 * y) - (0.4986 * z);
        double g = (-0.9689 * x) + (1.8758 * y) + (0.0415 * z);
        double b = (0.0557 * x) - (0.2040 * y) + (1.0570 * z);

        r = Math.Max(0.0, r);
        g = Math.Max(0.0, g);
        b = Math.Max(0.0, b);

        double peak = Math.Max(r, Math.Max(g, b));
        if (peak <= 0.0)
        {
            return (1.0, 1.0, 1.0);
        }

        return (r / peak, g / peak, b / peak);
    }

    /// <summary>
    /// The colour to draw a star, from its B−V index. Returns linear sRGB with the peak at one.
    /// </summary>
    /// <remarks>
    /// A star with no measured colour comes back white, which is the honest answer for an unknown
    /// temperature and is also what a star looks like when you do not know any better.
    /// </remarks>
    internal static (double Red, double Green, double Blue) ForColourIndex(double colourIndex)
    {
        if (double.IsNaN(colourIndex))
        {
            return (1.0, 1.0, 1.0);
        }

        // Beyond the fit's range the formula still behaves, but the catalogue has a handful of
        // entries outside it whose indices are wrong rather than extreme, so they are clamped.
        double bv = Math.Clamp(colourIndex, -0.4, 2.5);
        return LinearRgb(TemperatureKelvin(bv));
    }

    /// <summary>
    /// Applies the sRGB transfer function to a linear channel value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Needed because <see cref="LinearRgb"/> works in linear light, which is where the physics is,
    /// and every display works in something else. Skipping this is the difference between a Sun at
    /// (1.00, 0.88, 0.82) and one at (1.00, 0.94, 0.91) — the first looks distinctly cream, the
    /// second looks white, and only the second is what a person sees when they look at the Sun.
    /// </para>
    /// <para>
    /// The piecewise form is the standard one rather than a plain 2.2 power, because the linear
    /// segment near zero is what keeps very dim stars from turning muddy — which matters when the
    /// faintest thing in the catalogue is magnitude 6.5 and the renderer is drawing it at one pixel.
    /// </para>
    /// </remarks>
    internal static double SrgbEncoded(double linear)
    {
        double value = Math.Clamp(linear, 0.0, 1.0);
        return value <= 0.0031308
            ? value * 12.92
            : (1.055 * Math.Pow(value, 1.0 / 2.4)) - 0.055;
    }

    /// <summary>Planck's law, up to a constant. Only the shape matters after normalisation.</summary>
    private static double Planck(double wavelengthNm, double temperatureKelvin)
    {
        const double h = 6.62607015e-34;
        const double c = 2.99792458e8;
        const double k = 1.380649e-23;

        double lambda = wavelengthNm * 1e-9;
        double numerator = (2.0 * h * c * c) / Math.Pow(lambda, 5.0);
        double exponent = (h * c) / (lambda * k * temperatureKelvin);

        // The exponential overflows above about 700 K nm / lambda, which cannot happen inside the
        // visible band at any temperature a star has, but a guard costs nothing.
        return exponent > 700.0 ? 0.0 : numerator / (Math.Exp(exponent) - 1.0);
    }

    /// <summary>Visible wavelengths at 5 nm, which is finer than the eye can resolve in colour.</summary>
    private static readonly double[] Wavelengths = BuildWavelengths();

    private static double[] BuildWavelengths()
    {
        var values = new List<double>();
        for (double lambda = 380.0; lambda <= 780.0; lambda += 5.0)
        {
            values.Add(lambda);
        }

        return values.ToArray();
    }

    // The CIE 1931 2-degree colour-matching functions, at the same 5 nm spacing. These are the
    // standard tabulated values; the well-known multi-lobe Gaussian fits to them are accurate to
    // about 1% and would do, but the table is the same size and has no error at all.
    private static readonly double[] CieX = BuildCieX();
    private static readonly double[] CieY = BuildCieY();
    private static readonly double[] CieZ = BuildCieZ();

    private static double[] BuildCieX() => new[]
    {
        0.0014, 0.0022, 0.0042, 0.0076, 0.0143, 0.0232, 0.0435, 0.0776, 0.1344, 0.2148,
        0.2839, 0.3285, 0.3483, 0.3481, 0.3362, 0.3187, 0.2908, 0.2511, 0.1954, 0.1421,
        0.0956, 0.0580, 0.0320, 0.0147, 0.0049, 0.0024, 0.0093, 0.0291, 0.0633, 0.1096,
        0.1655, 0.2257, 0.2904, 0.3597, 0.4334, 0.5121, 0.5945, 0.6784, 0.7621, 0.8425,
        0.9163, 0.9786, 1.0263, 1.0567, 1.0622, 1.0456, 1.0026, 0.9384, 0.8544, 0.7514,
        0.6424, 0.5419, 0.4479, 0.3608, 0.2835, 0.2187, 0.1649, 0.1212, 0.0874, 0.0636,
        0.0468, 0.0329, 0.0227, 0.0158, 0.0114, 0.0081, 0.0058, 0.0041, 0.0029, 0.0020,
        0.0014, 0.0010, 0.0007, 0.0005, 0.0003, 0.0002, 0.0002, 0.0001, 0.0001, 0.0000,
        0.0000,
    };

    private static double[] BuildCieY() => new[]
    {
        0.0000, 0.0001, 0.0001, 0.0002, 0.0004, 0.0006, 0.0012, 0.0022, 0.0040, 0.0073,
        0.0116, 0.0168, 0.0230, 0.0298, 0.0380, 0.0480, 0.0600, 0.0739, 0.0910, 0.1126,
        0.1390, 0.1693, 0.2080, 0.2586, 0.3230, 0.4073, 0.5030, 0.6082, 0.7100, 0.7932,
        0.8620, 0.9149, 0.9540, 0.9803, 0.9950, 1.0000, 0.9950, 0.9786, 0.9520, 0.9154,
        0.8700, 0.8163, 0.7570, 0.6949, 0.6310, 0.5668, 0.5030, 0.4412, 0.3810, 0.3210,
        0.2650, 0.2170, 0.1750, 0.1382, 0.1070, 0.0816, 0.0610, 0.0446, 0.0320, 0.0232,
        0.0170, 0.0119, 0.0082, 0.0057, 0.0041, 0.0029, 0.0021, 0.0015, 0.0010, 0.0007,
        0.0005, 0.0004, 0.0002, 0.0002, 0.0001, 0.0001, 0.0001, 0.0000, 0.0000, 0.0000,
        0.0000,
    };

    private static double[] BuildCieZ() => new[]
    {
        0.0065, 0.0105, 0.0201, 0.0362, 0.0679, 0.1102, 0.2074, 0.3713, 0.6456, 1.0391,
        1.3856, 1.6230, 1.7471, 1.7826, 1.7721, 1.7441, 1.6692, 1.5281, 1.2876, 1.0419,
        0.8130, 0.6162, 0.4652, 0.3533, 0.2720, 0.2123, 0.1582, 0.1117, 0.0782, 0.0573,
        0.0422, 0.0298, 0.0203, 0.0134, 0.0087, 0.0057, 0.0039, 0.0027, 0.0021, 0.0018,
        0.0017, 0.0014, 0.0011, 0.0010, 0.0008, 0.0006, 0.0003, 0.0002, 0.0002, 0.0001,
        0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000,
        0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000,
        0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000, 0.0000,
        0.0000,
    };
}
