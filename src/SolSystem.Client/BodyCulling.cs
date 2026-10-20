namespace SolSystem.Client;

/// <summary>
/// The frustum test that decides whether a body is worth drawing.
/// </summary>
/// <remarks>
/// <para>
/// The body renderer draws spheres with the painter's algorithm rather than through the depth
/// buffer, so it has to know when a body is outside the view. The rule is the visible cone of the
/// screen: the wider half-angle (the screen is wider than it is tall) plus the body's own angular
/// radius, with a degree of slack so the limb is not cut at the frame edge.
/// </para>
/// <para>
/// <b>It used to be half the vertical field of view clamped at 90 degrees</b>, and both halves of
/// that were wrong. At 16:9 the horizontal half-angle is 46.8 degrees, so a small body at the
/// horizontal edge was culled before it arrived; and the Earth from low orbit subtends about
/// 140 degrees, so it was culled at 90 degrees while twenty degrees of its limb were still in
/// frame. The Earth would vanish as the view swung past it, with the draw and the frustum
/// disagreeing — the symptom that started this test.
/// </para>
/// </remarks>
internal static class BodyCulling
{
    /// <summary>How much slack, in radians, to leave beyond the visible cone.</summary>
    private const double MarginRadians = Math.PI / 180.0;

    /// <summary>
    /// Whether a body whose centre is <paramref name="facing"/> radians off the view axis, and whose
    /// angular radius is <paramref name="angularRadius"/>, can contribute a pixel.
    /// </summary>
    internal static bool IsVisible(double facing, double angularRadius, double aspectRatio,
        double fieldOfViewDegrees)
    {
        double verticalHalf = fieldOfViewDegrees * Math.PI / 180.0 * 0.5;
        double horizontalHalf = Math.Atan(Math.Tan(verticalHalf) * aspectRatio);

        double cullAngle = Math.Min(
            Math.PI,
            Math.Max(verticalHalf, horizontalHalf) + angularRadius + MarginRadians);

        return facing >= Math.Cos(cullAngle);
    }
}
