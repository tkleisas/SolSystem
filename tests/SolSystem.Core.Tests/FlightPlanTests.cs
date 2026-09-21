using SolSystem.Core.Orbits;
using Xunit;
using Xunit.Abstractions;

namespace SolSystem.Core.Tests;

/// <summary>
/// The trajectory planner, checked against figures from outside this project.
/// </summary>
/// <remarks>
/// A planner that agrees only with itself is worth nothing, so every number here is either a
/// published figure or a closed-form identity that can be derived by hand. The Earth–Mars figures are
/// the standard ones: a Hohmann transfer takes about eight and a half months and costs about
/// 5.6 km/s, and the launch window opens when Mars is about 44 degrees ahead.
/// </remarks>
public class FlightPlanTests
{
    private readonly ITestOutputHelper _o;

    public FlightPlanTests(ITestOutputHelper o) => _o = o;

    private const double Au = FlightPlan.KilometresPerAu;
    private const double EarthAu = 1.0;
    private const double MarsAu = 1.5237;
    private const double VenusAu = 0.7233;
    private const double JupiterAu = 5.2029;

    /// <summary>The client's courier: 5.5 kN on 140 tonnes.</summary>
    private const double Acceleration = 5.5 / 140.0;

    private static TransferOption Find(IEnumerable<TransferOption> options, string name) =>
        options.First(o => o.Name == name);

    [Fact]
    public void TheHohmannTransfer_ToMars_MatchesTheTextbook()
    {
        // 259 days, 5.6 km/s, both of which are the figures in every reference on the subject.
        List<TransferOption> options = FlightPlan.Options(
            EarthAu * Au, MarsAu * Au, Acceleration, 400_000.0);

        TransferOption ballistic = Find(options, "BALLISTIC");

        _o.WriteLine($"Earth -> Mars ballistic: {ballistic.Seconds / 86400.0:F1} days, "
            + $"{ballistic.DeltaV / 1000.0:F2} km/s");

        Assert.InRange(ballistic.Seconds / 86400.0, 258.0, 260.0);
        Assert.InRange(ballistic.DeltaV / 1000.0, 5.5, 5.7);
    }

    [Fact]
    public void TheLaunchWindow_ToMars_IsFortyFourDegrees()
    {
        // The classic figure. Mars must be 44 degrees ahead of the Earth when the ship leaves, and
        // that falls out of the transit time rather than being quoted from anywhere.
        double phase = FlightPlan.DeparturePhaseDegrees(EarthAu * Au, MarsAu * Au);

        _o.WriteLine($"Earth -> Mars departure phase: {phase:F1} degrees");

        Assert.InRange(phase, 44.0, 44.6);

        // Inbound the same arithmetic gives a NEGATIVE phase, because Venus has to be behind: the
        // ship is falling towards the Sun and arrives sooner than the target would have.
        double toVenus = FlightPlan.DeparturePhaseDegrees(EarthAu * Au, VenusAu * Au);
        _o.WriteLine($"Earth -> Venus departure phase: {toVenus:F1} degrees");

        Assert.True(toVenus < 0.0, $"Venus should be behind, and the phase is {toVenus:F1}");
        Assert.InRange(toVenus, -54.6, -53.4);
    }

    [Fact]
    public void DirectAndBallistic_DifferByAnOrderOfMagnitude_InBothDirections()
    {
        // The whole shape of the game, in one assertion each way.
        List<TransferOption> options = FlightPlan.Options(
            EarthAu * Au, MarsAu * Au, Acceleration, 400_000.0);

        TransferOption direct = Find(options, "DIRECT");
        TransferOption ballistic = Find(options, "BALLISTIC");

        _o.WriteLine($"Mars direct:    {direct.Seconds / 86400.0,6:F1} days, "
            + $"{direct.DeltaV / 1000.0,6:F1} km/s, peak {direct.PeakSpeed / 1000.0:F1} km/s");
        _o.WriteLine($"Mars ballistic: {ballistic.Seconds / 86400.0,6:F1} days, "
            + $"{ballistic.DeltaV / 1000.0,6:F1} km/s");

        // About eight times faster.
        Assert.True(direct.Seconds * 7.0 < ballistic.Seconds,
            $"direct is {ballistic.Seconds / direct.Seconds:F1}x faster, expected about 8x");

        // And about twenty times the fuel.
        Assert.True(direct.DeltaV > ballistic.DeltaV * 15.0,
            $"direct costs {direct.DeltaV / ballistic.DeltaV:F1}x the fuel, expected about 20x");
    }

    [Fact]
    public void QuarterThrottle_TradesTimeForFuel()
    {
        // The identity behind the economy option: on a constant-thrust crossing, time goes as
        // 1/sqrt(a) and delta-v as sqrt(a), so quartering the thrust is exactly two for two.
        //
        // It holds exactly where there is no gravity to fight, which is why the first assertion is
        // made at Jupiter rather than at Mars. At 3 AU the Sun pulls at 0.0006 m/s2 against a thrust
        // of 0.039, so the exchange is within a couple of per cent of the ideal.
        List<TransferOption> far = FlightPlan.Options(
            EarthAu * Au, JupiterAu * Au, Acceleration, 2_000_000.0);

        TransferOption farDirect = Find(far, "DIRECT");
        TransferOption farEconomy = Find(far, "ECONOMY");

        double timeRatio = farEconomy.Seconds / farDirect.Seconds;
        double fuelRatio = farEconomy.DeltaV / farDirect.DeltaV;

        _o.WriteLine($"Jupiter full:    {farDirect.Seconds / 86400.0,6:F1} days, "
            + $"{farDirect.DeltaV / 1000.0:F1} km/s");
        _o.WriteLine($"Jupiter quarter: {farEconomy.Seconds / 86400.0,6:F1} days, "
            + $"{farEconomy.DeltaV / 1000.0:F1} km/s");
        _o.WriteLine($"  time x{timeRatio:F3}, fuel x{fuelRatio:F3}");

        // Not a tight band, and the looseness is the honest part: the identity is asymptotic and
        // three astronomical units is not infinity. Even there the Sun still pulls at 0.0006 m/s2
        // against a thrust of 0.039, and the residual is two and a half per cent.
        Assert.InRange(timeRatio, 1.95, 2.10);
        Assert.InRange(fuelRatio, 0.48, 0.53);

        // In the inner system the identity does NOT hold, and it fails in a particular direction
        // worth knowing: the Sun's pull does not scale down with the throttle, so throttling back
        // costs more time and more fuel than the clean ratio promises. A planner that quietly used
        // the ideal figure here would under-quote both.
        List<TransferOption> near = FlightPlan.Options(
            EarthAu * Au, MarsAu * Au, Acceleration, 400_000.0);

        TransferOption nearDirect = Find(near, "DIRECT");
        TransferOption nearEconomy = Find(near, "ECONOMY");

        double nearTimeRatio = nearEconomy.Seconds / nearDirect.Seconds;
        double nearFuelRatio = nearEconomy.DeltaV / nearDirect.DeltaV;

        _o.WriteLine($"Mars full:       {nearDirect.Seconds / 86400.0,6:F1} days, "
            + $"{nearDirect.DeltaV / 1000.0:F1} km/s");
        _o.WriteLine($"Mars quarter:    {nearEconomy.Seconds / 86400.0,6:F1} days, "
            + $"{nearEconomy.DeltaV / 1000.0:F1} km/s");
        _o.WriteLine($"  time x{nearTimeRatio:F3}, fuel x{nearFuelRatio:F3}");

        // Worse on both counts than the ideal two-for-two — but still a large saving in fuel, which
        // is the point of offering it.
        Assert.True(nearTimeRatio > 2.0, $"expected more than double the time, got {nearTimeRatio:F2}");
        Assert.True(nearFuelRatio > 0.5, $"expected worse than half the fuel, got {nearFuelRatio:F2}");
        Assert.True(nearFuelRatio < 0.75, $"and still a large saving, got {nearFuelRatio:F2}");
    }

    [Fact]
    public void JupiterDirect_CostsMostOfTheTanks()
    {
        // A courier with 403 km/s aboard. Jupiter direct is three quarters of it, one way — which is
        // the fact that makes the outer system a commitment rather than a trip.
        List<TransferOption> options = FlightPlan.Options(
            EarthAu * Au, JupiterAu * Au, Acceleration, 403_000.0);

        TransferOption direct = Find(options, "DIRECT");
        TransferOption ballistic = Find(options, "BALLISTIC");

        _o.WriteLine($"Jupiter direct:    {direct.Seconds / 86400.0,7:F1} days, "
            + $"{direct.DeltaV / 1000.0,6:F1} km/s  feasible {direct.Feasible}");
        _o.WriteLine($"Jupiter ballistic: {ballistic.Seconds / 86400.0,7:F1} days, "
            + $"{ballistic.DeltaV / 1000.0,6:F1} km/s  feasible {ballistic.Feasible}");

        Assert.True(direct.Feasible);
        Assert.True(direct.DeltaV > 300_000.0, "Jupiter direct should cost over 300 km/s");
        Assert.True(ballistic.DeltaV < 20_000.0, "and ballistic under 20");

        // Two and a half years ballistic, against three months direct.
        Assert.Equal(998.0, ballistic.Seconds / 86400.0, 0);
    }

    [Fact]
    public void AnOptionTheShipCannotAfford_SaysSo()
    {
        // A ship with nearly empty tanks must be told that Jupiter is not on the list, rather than
        // being given a number it cannot pay.
        List<TransferOption> options = FlightPlan.Options(
            EarthAu * Au, JupiterAu * Au, Acceleration, 50_000.0);

        TransferOption direct = Find(options, "DIRECT");
        TransferOption ballistic = Find(options, "BALLISTIC");

        Assert.False(direct.Feasible);
        Assert.True(ballistic.Feasible);
    }

    [Fact]
    public void GoingInwards_IsCheaperAndFaster_ThanGoingOut()
    {
        // Falling towards the Sun is free: the ballistic burn to Venus is smaller than to Mars, and
        // the direct crossing is shorter. A planner that had the sign of the gravity correction
        // backwards would get both of these wrong.
        List<TransferOption> toVenus = FlightPlan.Options(
            EarthAu * Au, VenusAu * Au, Acceleration, 400_000.0);
        List<TransferOption> toMars = FlightPlan.Options(
            EarthAu * Au, MarsAu * Au, Acceleration, 400_000.0);

        Assert.True(Find(toVenus, "DIRECT").DeltaV < Find(toMars, "DIRECT").DeltaV);
        Assert.True(Find(toVenus, "BALLISTIC").DeltaV < Find(toMars, "BALLISTIC").DeltaV);
        Assert.True(Find(toVenus, "DIRECT").Seconds < Find(toMars, "DIRECT").Seconds);
    }
}
