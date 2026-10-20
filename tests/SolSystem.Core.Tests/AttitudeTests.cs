using SolSystem.Core.Local;
using SolSystem.Core.Numerics;
using Xunit;

namespace SolSystem.Core.Tests;

/// <summary>
/// The attitude operation the strategic clock needs: carrying a ship through a frame rotation.
/// </summary>
public class AttitudeTests
{
    private static Fix128 F(double value) => Fix128.FromDouble(value);

    [Fact]
    public void RotatingTheAttitudeCarriesTheNose()
    {
        // Pointing +y (a quarter turn about z from +x), then the frame turns another quarter.
        var attitude = new Attitude(
            new Fix128Vec(Fix128.Zero, Fix128.Zero, F(Math.PI / 2.0)), Fix128Vec.Zero);

        Fix128Vec before = attitude.Forward;
        Fix128Vec after = attitude.RotatedAboutZ(F(Math.PI / 2.0)).Forward;

        Assert.InRange(before.X.ToDouble(), -1e-4, 1e-4);
        Assert.InRange(before.Y.ToDouble(), 1.0 - 1e-4, 1.0 + 1e-4);

        Assert.InRange(after.X.ToDouble(), -1.0 - 1e-4, -1.0 + 1e-4);
        Assert.InRange(after.Y.ToDouble(), -1e-4, 1e-4);
    }
}
