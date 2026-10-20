using SolSystem.Core.Orbits;
using Xunit;

namespace SolSystem.Client.Tests;

/// <summary>
/// The session's strategic step: the clock and both stations analytic, the corridor carried.
/// </summary>
public class FlightSessionTests
{
    [Fact]
    public void ACoarseSessionStepKeepsTheStationsAndTheCorridor()
    {
        FlightSession session = FlightSession.Start(LaunchOptions.Parse([]));

        Station home = session.Station;
        Station target = session.Target;
        double radius = home.Offset.Length.ToDouble();
        double separation = (target.Offset - home.Offset).Length.ToDouble();
        double local = session.LocalOffset.Length.ToDouble();
        double julianDate = session.JulianDate;

        session.AdvanceCoarse(3_600.0);

        Station newHome = session.Station;
        Station newTarget = session.Target;

        Assert.InRange(
            Math.Abs(session.JulianDate - (julianDate + (3_600.0 / 86_400.0))), 0.0, 1e-9);
        Assert.InRange(Math.Abs(newHome.Offset.Length.ToDouble() - radius), 0.0, 0.10);
        Assert.InRange(
            Math.Abs((newTarget.Offset - newHome.Offset).Length.ToDouble() - separation), 0.0, 0.5);
        Assert.InRange(Math.Abs(session.LocalOffset.Length.ToDouble() - local), 0.0, 0.10);
    }
}
