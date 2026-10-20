using SolSystem.Economy;
using Xunit;

namespace SolSystem.Economy.Tests;

/// <summary>
/// The economy simulation is the model's first independent check: if the sites cannot
/// survive their own constraints, the prices in docs/ECONOMY.md are theatre.
/// </summary>
public class EconomySimulationTests
{
    private static SimulationSummary Run(Simulation simulation, int days = 365)
    {
        simulation.Run(days);
        return simulation.Summary(days);
    }

    [Fact]
    public void TheBaselineKeepsEverySiteAlive()
    {
        SimulationSummary summary = Run(Scenario.Build([]));

        Assert.True(summary.MinFoodDays > 1.0, $"min food {summary.MinFoodDays:F1} d");
        Assert.True(summary.MinWaterDays > 1.0, $"min water {summary.MinWaterDays:F1} d");
        Assert.True(summary.MinParts >= 0.0, $"min parts {summary.MinParts}");
        Assert.True(summary.MinEnergyMarginMWh >= 0.0);
        Assert.True(summary.HungerDays == 0.0);
    }

    [Fact]
    public void TheMeridianHasPositivePowerAndFood()
    {
        Simulation simulation = Scenario.Build([]);
        simulation.Run(365);

        Assert.True(simulation.Sites["Meridian"].MinMarginMWh > 0.0);
        Assert.True(simulation.Sites["Meridian"].FoodDays > 1.0);
    }

    [Fact]
    public void TheSolarFlareCreatesUnservedLoad()
    {
        SimulationSummary summary = Run(Scenario.Build([Scenario.Flare()]));

        Assert.True(summary.EnergyDeficitMWh > 0.0);
        Assert.True(summary.BlackoutDays > 0.0);
    }

    [Fact]
    public void TheBlockadeCreatesHunger()
    {
        SimulationSummary summary = Run(Scenario.Build([Scenario.Blockade()]));

        Assert.True(summary.HungerDays > 0.0);
    }

    [Fact]
    public void TheTradeFillsTheMeridianReserve()
    {
        Simulation simulation = Scenario.Build([]);
        simulation.Run(1);

        Assert.True(simulation.Sites["Meridian"].Food > 240.0);
        Assert.True(simulation.Sites["Meridian"].TradeTonnes > 0.0);
    }

    [Fact]
    public void TheModelIsDeterministic()
    {
        SimulationSummary first = Run(Scenario.Build([Scenario.Flare(), Scenario.Blockade()]));
        SimulationSummary second = Run(Scenario.Build([Scenario.Flare(), Scenario.Blockade()]));

        Assert.Equal(first, second);
    }

    [Fact]
    public void TheRecyclerFailureCostsMoreWaterThanTheBaseline()
    {
        Simulation baseline = Scenario.Build([]);
        Simulation failure = Scenario.Build([Scenario.RecyclerFailure()]);
        baseline.Run(365);
        failure.Run(365);

        Assert.True(failure.Sites["Meridian"].WaterMin < baseline.Sites["Meridian"].WaterMin);
    }
}
