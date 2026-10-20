using Xunit;

namespace SolSystem.Client.Tests;

/// <summary>
/// The body renderer's frustum test, at the angles where the old one failed.
/// </summary>
public class BodyCullingTests
{
    private const double Aspect = 16.0 / 9.0;

    private static double Radians(double degrees) => degrees * Math.PI / 180.0;

    [Fact]
    public void ABodyNearTheHorizontalEdgeIsVisible()
    {
        // Half the vertical field is 30 degrees; half the horizontal is 46.8. A one-degree body
        // forty degrees off-axis is on screen, and the old vertical-only cull hid it.
        Assert.True(BodyCulling.IsVisible(
            Math.Cos(Radians(40.0)), Radians(1.0), Aspect, 60.0));
    }

    [Fact]
    public void TheEarthStaysVisibleWithItsCentreBehindTheCamera()
    {
        // The Earth from low orbit subtends about 140 degrees. With its centre a hundred degrees
        // off-axis, its limb is still thirty degrees from the centre of the view; the old 90-degree
        // clamp culled it there, which is the Earth that vanished when the view swung past.
        Assert.True(BodyCulling.IsVisible(
            Math.Cos(Radians(100.0)), Radians(70.0), Aspect, 60.0));
    }

    [Fact]
    public void TheEarthIsCulledWhenItsLimbLeavesTheFrame()
    {
        // Its nearest limb is fifty degrees off-axis, outside the 46.8-degree horizontal half;
        // there is no part of it left to draw.
        Assert.False(BodyCulling.IsVisible(
            Math.Cos(Radians(120.0)), Radians(70.0), Aspect, 60.0));
    }
}
