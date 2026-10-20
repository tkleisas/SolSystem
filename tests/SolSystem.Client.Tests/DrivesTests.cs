using SolSystem.Core.Local;
using SolSystem.Core.Numerics;
using Xunit;

namespace SolSystem.Client.Tests;

/// <summary>
/// The two drives, and the table that makes the choice a trade.
/// </summary>
public class DrivesTests
{
    private static Ship Courier(Drive drive)
    {
        var attitude = new Attitude(Fix128Vec.Zero, Fix128Vec.Zero);
        return Hulls.Courier(Fix128Vec.Zero, Fix128Vec.Zero, attitude, Drives.Engine(drive));
    }

    [Fact]
    public void TheWorkersDriveHasTwiceTheExhaustVelocity()
    {
        Assert.Equal(1_200_000.0,
            Drives.Engine(Drive.Workers).ExhaustVelocityMetresPerSecond.ToDouble(), 0);
        Assert.Equal(600_000.0,
            Drives.Engine(Drive.Illuminus).ExhaustVelocityMetresPerSecond.ToDouble(), 0);
    }

    [Fact]
    public void TheIlluminusDriveHasMoreThrust()
    {
        Assert.Equal(5.5, Drives.Engine(Drive.Workers).ThrustKilonewtons.ToDouble(), 3);
        Assert.Equal(9.0, Drives.Engine(Drive.Illuminus).ThrustKilonewtons.ToDouble(), 3);
    }

    [Fact]
    public void TheSameTankBuysTwiceTheDeltaVOnTheWorkers()
    {
        Ship workers = Courier(Drive.Workers);
        Ship illuminus = Courier(Drive.Illuminus);

        double ratio = workers.DeltaVRemaining.ToDouble() / illuminus.DeltaVRemaining.ToDouble();

        // Both hulls start with the same 40 t; the exhaust velocities are 2:1, so the delta-v is.
        Assert.InRange(ratio, 1.99, 2.01);
    }

    [Fact]
    public void TheIlluminusDriveAcceleratesHarder()
    {
        Ship workers = Courier(Drive.Workers);
        Ship illuminus = Courier(Drive.Illuminus);

        double workersAcceleration =
            workers.Engine.ThrustKilonewtons.ToDouble() / workers.Mass.ToDouble();
        double illuminusAcceleration =
            illuminus.Engine.ThrustKilonewtons.ToDouble() / illuminus.Mass.ToDouble();

        // kN / t is m/s² directly. The Illuminus is about 1.6x the Workers.
        Assert.True(illuminusAcceleration > workersAcceleration * 1.5);
    }
}
