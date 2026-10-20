using Microsoft.Xna.Framework;
using SolSystem.Core.Numerics;
using Xunit;

namespace SolSystem.Client.Tests;

/// <summary>
/// The camera basis the reticles and the starfield project through.
/// </summary>
/// <remarks>
/// The Sun's nav marker sat away from the Sun for a long time because this basis was the ship's
/// deck rather than the screen's up. The deck is not perpendicular to a pitched forward, and the
/// direction-only projections divide by the forward component and use the up directly. These
/// tests state the property the projection needs.
/// </remarks>
public class CameraTests
{
    [Fact]
    public void TheReturnedScreenUpIsOrthogonalToThePitchedForward()
    {
        var camera = new Camera();
        camera.Use("chase");
        camera.StartAt(0.0, double.NaN, 0.0);

        (Matrix _, Fix128Vec forward, Fix128Vec up) = camera.Build(
            Vector3.UnitX, Vector3.UnitZ, Vector3.Zero);

        Vector3 f = ToVector(forward);
        Vector3 u = ToVector(up);

        Assert.InRange(Math.Abs(Vector3.Dot(f, u)), 0.0, 1e-4);
        Assert.InRange(Math.Abs(u.Length() - 1.0f), 0.0, 1e-4);

        // The chase camera is lifted and looks slightly down, so its forward has a deck component.
        // If the returned up is the deck, the dot above is not zero and the vertical projection is
        // compressed — which is exactly what put the Sun's diamond in the wrong place.
        Assert.True(Math.Abs(f.Z) > 0.05f, $"the view should be pitched; forward was {f}");
        Assert.True(Math.Abs(u.Z - 1.0f) > 0.01f, $"up should not be the deck; up was {u}");
    }

    private static Vector3 ToVector(Fix128Vec v) => new(
        (float)v.X.ToDouble(), (float)v.Y.ToDouble(), (float)v.Z.ToDouble());
}
