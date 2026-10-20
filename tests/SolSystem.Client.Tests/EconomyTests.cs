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
        double price = market.BuyPrice(0);

        Assert.True(market.TryBuy(0, hold, ledger, ref ship));

        Assert.Equal(3_000.0 - price, ledger.Credits, 3);
        Assert.Equal(1.0, hold.TonnesOf("Machine parts"), 9);
        Assert.Equal(mass + 1.0, ship.Mass.ToDouble(), 6);
    }

    [Fact]
    public void TheWaystationPaysMoreForPartsThanMeridianCharges()
    {
        var ledger = new Ledger(3_000.0);
        var hold = new CargoHold();
        Ship ship = Courier();
        Market home = Market.For("Meridian");
        Market target = Market.For("Waystation");

        double buy = home.BuyPrice(0);
        double sell = target.SellPrice(0);
        Assert.True(sell > buy, $"sell {sell:F0} should beat buy {buy:F0}");

        Assert.True(home.TryBuy(0, hold, ledger, ref ship));
        double afterBuy = ledger.Credits;

        Assert.True(target.TrySell(0, hold, ledger, ref ship));

        Assert.Equal(afterBuy + sell, ledger.Credits, 3);
        Assert.Equal(0.0, hold.MassTonnes, 9);
    }

    [Fact]
    public void ThePriceDriftsAndIsAFunctionOfTheSessionAge()
    {
        Market market = Market.For("Meridian");
        double atStart = market.BuyPrice(0);

        market.Update(0.5);
        double halfDay = market.BuyPrice(0);

        Assert.NotEqual(atStart, halfDay);

        // The same age gives the same price: no wall clock, no random walk.
        market.Update(0.5);
        Assert.Equal(halfDay, market.BuyPrice(0), 9);
    }

    [Fact]
    public void BuyingRaisesThePriceAndSellingLowersIt()
    {
        Market buyMarket = Market.For("Meridian");
        var buyLedger = new Ledger(100_000.0);
        var buyHold = new CargoHold();
        Ship buyShip = Courier();

        double beforeBuy = buyMarket.BuyPrice(0);
        Assert.True(buyMarket.TryBuy(0, buyHold, buyLedger, ref buyShip));
        Assert.True(buyMarket.BuyPrice(0) > beforeBuy);

        Market sellMarket = Market.For("Waystation");
        var sellLedger = new Ledger(100_000.0);
        var sellHold = new CargoHold();
        Ship sellShip = Courier();
        Assert.True(sellHold.TryAdd("Machine parts", 5.0));

        double beforeSell = sellMarket.SellPrice(0);
        Assert.True(sellMarket.TrySell(0, sellHold, sellLedger, ref sellShip));
        Assert.True(sellMarket.SellPrice(0) < beforeSell);

        // And the pressure lets go as the clock passes.
        sellMarket.Update(5.0);
        Assert.True(sellMarket.SellPrice(0) > (beforeSell - (beforeSell * 0.02)));
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
        double fuelPrice = market.FuelPrice;
        Assert.True(market.TryRefuel(full, ledger, ref ship));

        Assert.Equal(full, ship.Propellant.ToDouble(), 6);
        Assert.Equal(mass, ship.Mass.ToDouble(), 6);
        Assert.Equal(10_000.0 - (10.0 * fuelPrice), ledger.Credits, 3);

        // The tank is full; a second purchase has nothing to load.
        Assert.False(market.TryRefuel(full, ledger, ref ship));
    }
}
