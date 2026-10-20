using SolSystem.Core.Local;
using SolSystem.Core.Numerics;
using Xunit;

namespace SolSystem.Client.Tests;

/// <summary>
/// The ledger, the hold, and the two station markets.
/// </summary>
public class EconomyTests
{
    private static Ship Courier()
    {
        var attitude = new Attitude(Fix128Vec.Zero, Fix128Vec.Zero);
        return Hulls.Courier(Fix128Vec.Zero, Fix128Vec.Zero, attitude);
    }

    [Fact]
    public void SpendingMoreThanTheBalanceChangesNothing()
    {
        var ledger = new Ledger(1_000.0);
        var hold = new CargoHold();
        Ship ship = Courier();
        Market market = Market.For("Meridian");
        double mass = ship.Mass.ToDouble();

        Assert.False(market.TryBuy(0, hold, ledger, ref ship));

        Assert.Equal(1_000.0, ledger.Credits, 3);
        Assert.Equal(0.0, hold.MassTonnes, 9);
        Assert.Equal(mass, ship.Mass.ToDouble(), 6);
    }

    [Fact]
    public void BuyingOneTonneMovesCreditsHoldAndMassTogether()
    {
        var ledger = new Ledger(3_000.0);
        var hold = new CargoHold();
        Ship ship = Courier();
        Market market = Market.For("Meridian");
        double mass = ship.Mass.ToDouble();

        Assert.True(market.TryBuy(0, hold, ledger, ref ship));

        Assert.Equal(1_000.0, ledger.Credits, 3);
        Assert.Equal(1.0, hold.TonnesOf("Machine parts"), 9);
        Assert.Equal(mass + 1.0, ship.Mass.ToDouble(), 6);
    }

    [Fact]
    public void TheWaystationPaysMoreForPartsThanMeridianCharges()
    {
        var ledger = new Ledger(3_000.0);
        var hold = new CargoHold();
        Ship ship = Courier();

        Assert.True(Market.For("Meridian").TryBuy(0, hold, ledger, ref ship));
        double afterBuy = ledger.Credits;

        Assert.True(Market.For("Waystation").TrySell(0, hold, ledger, ref ship));

        // 2 000 out, 3 200 in: the run pays 1 200 before fuel.
        Assert.Equal(afterBuy + 3_200.0, ledger.Credits, 3);
        Assert.Equal(0.0, hold.MassTonnes, 9);
    }

    [Fact]
    public void TheHoldRefusesTheTwentyFirstTonne()
    {
        var hold = new CargoHold();

        Assert.True(hold.TryAdd("Water", CargoHold.CapacityTonnes));
        Assert.False(hold.TryAdd("Water", 1.0));
        Assert.Equal(CargoHold.CapacityTonnes, hold.MassTonnes, 9);
    }

    [Fact]
    public void RefuellingStopsAtTheTankAndChargesForWhatItLoads()
    {
        var ledger = new Ledger(10_000.0);
        Ship ship = Courier();
        double full = ship.Propellant.ToDouble();
        double mass = ship.Mass.ToDouble();

        // Burn ten tonnes first, then buy the lot back.
        ship.Propellant -= Fix128.FromDouble(10.0);
        ship.Mass -= Fix128.FromDouble(10.0);

        Market market = Market.For("Meridian");
        Assert.True(market.TryRefuel(full, ledger, ref ship));

        Assert.Equal(full, ship.Propellant.ToDouble(), 6);
        Assert.Equal(mass, ship.Mass.ToDouble(), 6);
        Assert.Equal(10_000.0 - (10.0 * 250.0), ledger.Credits, 3);

        // The tank is full; a second purchase has nothing to load.
        Assert.False(market.TryRefuel(full, ledger, ref ship));
    }
}
