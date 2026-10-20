using System;
using System.Collections.Generic;
using SolSystem.Core.Local;
using SolSystem.Core.Numerics;

namespace SolSystem.Client;

/// <summary>A good a station will buy and sell, per tonne.</summary>
internal readonly record struct MarketGood(string Name, double BuyPrice, double SellPrice);

/// <summary>
/// A station's market: what it sells, what it buys, and what fuel costs.
/// </summary>
/// <remarks>
/// <para>
/// The first market is deliberately asymmetric. Meridian sells machine parts cheap and buys water
/// cheap; the waystation is the reverse, because it is a small Workers module that cannot make
/// parts and has a recycler that makes water. A pilot who carries parts out and water back can
/// make a living; a pilot who carries the contract's medical supplies is paid for the delivery.
/// </para>
/// <para>
/// The ship's mass moves with every purchase, because the hold is part of the hull for the engine:
/// one tonne of cargo is one tonne of dry mass, which is one tonne less acceleration. Selling the
/// cargo back is the only way to get it off again.
/// </para>
/// </remarks>
internal sealed class Market
{
    /// <summary>Tonnes a single fuel purchase loads, before the tank's own limit.</summary>
    internal const double FuelLotTonnes = 10.0;

    internal Market(string station, IReadOnlyList<MarketGood> goods, double fuelPrice)
    {
        Station = station;
        Goods = goods;
        FuelPrice = fuelPrice;
    }

    internal string Station { get; }

    internal IReadOnlyList<MarketGood> Goods { get; }

    /// <summary>Credits per tonne of propellant.</summary>
    internal double FuelPrice { get; }

    /// <summary>The two stations the contract knows about.</summary>
    internal static Market For(string station) => station switch
    {
        "Waystation" => new Market(
            station,
            [
                new MarketGood("Machine parts", 3_600.0, 3_200.0),
                new MarketGood("Water", 300.0, 200.0),
            ],
            400.0),

        _ => new Market(
            station,
            [
                new MarketGood("Machine parts", 2_000.0, 1_000.0),
                new MarketGood("Water", 200.0, 100.0),
            ],
            250.0),
    };

    /// <summary>Buys one tonne of a good into the hold, if the credits and the hold allow it.</summary>
    internal bool TryBuy(int index, CargoHold hold, Ledger ledger, ref Ship ship)
    {
        MarketGood good = Goods[index];

        if (!hold.TryAdd(good.Name, 1.0))
        {
            return false;
        }

        if (!ledger.TrySpend(good.BuyPrice, $"buy 1 t {good.Name.ToLowerInvariant()}"))
        {
            hold.TryRemove(good.Name, 1.0);
            return false;
        }

        ship.Mass += Fix128.One;
        return true;
    }

    /// <summary>Sells one tonne from the hold.</summary>
    internal bool TrySell(int index, CargoHold hold, Ledger ledger, ref Ship ship)
    {
        MarketGood good = Goods[index];

        if (!hold.TryRemove(good.Name, 1.0))
        {
            return false;
        }

        ship.Mass -= Fix128.One;
        ledger.Credit(good.SellPrice, $"sell 1 t {good.Name.ToLowerInvariant()}");
        return true;
    }

    /// <summary>Loads up to <paramref name="tonnes"/> of propellant, limited by the tank.</summary>
    internal bool TryRefuel(double maxPropellantTonnes, Ledger ledger, ref Ship ship,
        double tonnes = FuelLotTonnes)
    {
        double room = maxPropellantTonnes - ship.Propellant.ToDouble();
        double load = Math.Min(tonnes, room);

        if (load <= 1e-9)
        {
            return false;
        }

        double cost = load * FuelPrice;
        if (!ledger.TrySpend(cost, $"fuel {load:F1} t"))
        {
            return false;
        }

        Fix128 fuel = Fix128.FromDouble(load);
        ship.Propellant += fuel;
        ship.Mass += fuel;
        return true;
    }
}
