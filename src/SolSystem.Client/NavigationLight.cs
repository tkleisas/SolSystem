namespace SolSystem.Client;

/// <summary>
/// When each navigation light is lit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every light in the convention flashes, and a steady one is not the convention.</b> The Cygnus
/// system flashes all five; SpaceX's Dragon carries a strobe. That matters beyond fidelity: a
/// flashing light is unambiguously artificial, and the whole job of a navigation light is to be
/// picked out from the sky behind it. A steady white lamp at four hundred metres is a star.
/// </para>
/// <para>
/// The rates are the real ones, as far as they are specified anywhere — the reds and greens flash
/// together at about one hertz, the dorsal whites and the ventral yellow are on the same clock so
/// that the count is what distinguishes them and not the timing, and the strobes are much faster
/// and much shorter, which is what makes them read as strobes rather than as lamps.
/// </para>
/// </remarks>
internal static class NavigationLight
{
    /// <summary>Seconds per flash cycle for the position lights.</summary>
    private const double PositionPeriod = 1.0;

    /// <summary>Fraction of the cycle a position light is lit for.</summary>
    private const double PositionDuty = 0.55;

    /// <summary>Seconds per cycle for an anti-collision strobe.</summary>
    private const double StrobePeriod = 0.72;

    /// <summary>
    /// Fraction of the cycle a strobe is lit for — short and sharp, which is the whole difference
    /// between a strobe and a lamp.
    /// </summary>
    private const double StrobeDuty = 0.09;

    /// <summary>
    /// How bright a named part should be at this instant, from 0 to 1.
    /// </summary>
    /// <remarks>
    /// Parts that are not lights come back at 1, so a caller can multiply unconditionally. The
    /// edges are softened over a few per cent of the cycle rather than switched, because a hard
    /// on/off at sixty frames a second beats against the frame rate and produces a flicker at a
    /// rate nobody chose.
    /// </remarks>
    internal static float Brightness(string materialName, double seconds)
    {
        if (materialName.Contains("Strobe", StringComparison.Ordinal))
        {
            return Pulse(seconds, StrobePeriod, StrobeDuty);
        }

        if (materialName.Contains("Nav", StringComparison.Ordinal))
        {
            // The dorsal pair and the ventral yellow share a clock with the reds and greens, so
            // that what tells them apart is the COUNT and the colour, never the rhythm.
            return Pulse(seconds, PositionPeriod, PositionDuty);
        }

        return 1.0f;
    }

    /// <summary>Whether a material is a navigation light at all.</summary>
    internal static bool IsLight(string materialName) =>
        materialName.Contains("Nav", StringComparison.Ordinal)
        || materialName.Contains("Strobe", StringComparison.Ordinal);

    private static float Pulse(double seconds, double period, double duty)
    {
        double phase = (seconds / period) % 1.0;
        const double Softness = 0.06;

        if (phase < duty - Softness)
        {
            return 1.0f;
        }

        if (phase < duty)
        {
            return (float)((duty - phase) / Softness);
        }

        return 0.0f;
    }
}
