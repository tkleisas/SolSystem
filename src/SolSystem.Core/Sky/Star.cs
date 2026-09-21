using SolSystem.Core.Numerics;

namespace SolSystem.Core.Sky;

/// <summary>
/// One star, as the simulation needs it: a direction, a brightness and a colour.
/// </summary>
/// <remarks>
/// <para>
/// Position is a <em>unit vector</em> rather than a pair of angles, and in the ecliptic frame the
/// ephemeris already uses rather than the equatorial frame the catalogue stores. Both are decided
/// at load, once, for a reason that shows up in a renderer: the sky is drawn every frame and the
/// planets are drawn every frame, so anything that has to be rotated or converted per frame is
/// work done sixty times a second to produce the same answer.
/// </para>
/// <para>
/// The stored angles are quantised to 2⁻³² of a turn, which is 0.084 arcsec. That is worth stating
/// because it is not negligible against the smallest effect the sky models: Proxima Centauri's
/// parallax is 772 milliarcseconds, so its annual wobble is about nine quantisation steps. A
/// renderer will never see it — human acuity is 60 arcsec and a 4K sky pixel is 160 — but a test
/// that measures parallax has to allow for it.
/// </para>
/// </remarks>
internal readonly struct Star
{
    /// <summary>
    /// Unit direction in the J2000 ecliptic frame — the frame <see cref="Orbits.Ephemeris"/> uses.
    /// </summary>
    internal readonly Fix128Vec Direction;

    /// <summary>Apparent visual magnitude. Lower is brighter; the naked-eye limit is about 6.5.</summary>
    internal readonly double Magnitude;

    /// <summary>
    /// B−V colour index, or <see cref="double.NaN"/> where the catalogue has none.
    /// </summary>
    /// <remarks>
    /// Blue is negative, red is positive: Rigel is about −0.03, Betelgeuse about +1.85, and the Sun
    /// is +0.65. This is what gives a star field its colour, and it is measured rather than chosen.
    /// </remarks>
    internal readonly double ColourIndex;

    /// <summary>
    /// Distance in parsecs, or zero for a star treated as being at infinity.
    /// </summary>
    /// <remarks>
    /// Zero is not "unknown" — it is "far enough that its parallax is below the quantisation of the
    /// catalogue". Everything past 655 pc is stored that way, and 655 pc is a parallax of 1.5
    /// milliarcseconds, which is a fifth of a quantisation step.
    /// </remarks>
    internal readonly double DistanceParsecs;

    /// <summary>Proper name, or empty.</summary>
    internal readonly string Name;

    internal Star(Fix128Vec direction, double magnitude, double colourIndex, double distanceParsecs,
        string name)
    {
        Direction = direction;
        Magnitude = magnitude;
        ColourIndex = colourIndex;
        DistanceParsecs = distanceParsecs;
        Name = name;
    }

    /// <summary>Whether the catalogue gives this star a distance at all.</summary>
    internal bool HasDistance => DistanceParsecs > 0.0;

    /// <summary>Parallax in milliarcseconds, or zero for a star at infinity.</summary>
    internal double ParallaxMilliarcseconds =>
        HasDistance ? 1000.0 / DistanceParsecs : 0.0;

    public override string ToString() =>
        Name.Length > 0
            ? $"{Name} (mag {Magnitude:F2})"
            : $"mag {Magnitude:F2} star";
}
