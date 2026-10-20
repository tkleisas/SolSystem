using System;
using System.Collections.Generic;
using SolSystem.Core.Local;
using SolSystem.Core.Numerics;

namespace SolSystem.Client;

/// <summary>A good a station will buy and sell. The spread is a fraction of the mid price.</summary>
internal readonly record struct MarketGood(string Name, double MidPrice, double Spread);

/// <summary>
/// A station's market: a moving mid price, a bid/ask spread, and the pressure the pilot's own
/// trades put on it.
/// </summary>
/// <remarks>
/// <para>
/// The price is not a constant with a number in a table. Each good has a base mid, a station phase
/// and a slow three-day drift, plus a pressure term that every tonne bought raises and every tonne
/// sold lowers. The pressure decays over about three quarters of a day, so a pilot who dumps twenty
/// tonnes at once moves the price against themselves and then waits for it to come back; a pilot
/// who sells a tonne at a time gets a better average. That is the first real trading decision in
/// the game, and it costs one array and an exponential.
/// </para>
/// <para>
/// Everything is a function of the session clock and the trades, not of wall time or a random
/// number generator, so two runs that make the same decisions see the same prices.
/// </para>
/// </remarks>
internal sealed class Market
{
    /// <summary>Tonnes a single fuel purchase loads, before the tank's own limit.</summary>
    internal const double FuelLotTonnes = 10.0;

    /// <summary>Mid-price movement per tonne traded, as a fraction.</summary>
    private const double TradePressure = 0.012;

    /// <summary>Days the pressure takes to fall to 1/e.</summary>
    private const double PressureDecayDays = 0.75;

    private readonly MarketGood[] _goods;
    private readonly double[] _mid;
    private readonly double[] _pressure;
    private readonly double _phase;
    private readonly double _fuelBase;
    private double _fuelPressure;
    private double _lastDays;

    internal Market(string station, IReadOnlyList<MarketGood> goods, double fuelPrice, double phase)
    {
        Station = station;
        _goods = new MarketGood[goods.Count];
        _mid = new double[goods.Count];
        _pressure = new double[goods.Count];
        _phase = phase;
        _fuelBase = fuelPrice;

        for (int i = 0; i < goods.Count; i++)
        {
            _goods[i] = goods[i];
            _mid[i] = goods[i].MidPrice;
        }

        Update(0.0);
    }

    internal string Station { get; }

    internal IReadOnlyList<MarketGood> Goods => _goods;

    /// <summary>What the station asks for a tonne, including the spread and the current pressure.</summary>
    internal double BuyPrice(int index) =>
        _mid[index] * (1.0 + _goods[index].Spread) * (1.0 + _pressure[index]);

    /// <summary>What the station pays for a tonne, including the spread and the pressure.</summary>
    internal double SellPrice(int index) =>
        _mid[index] * (1.0 - _goods[index].Spread) * (1.0 + _pressure[index]);

    /// <summary>Credits per tonne of propellant, drifting on its own slower cycle.</summary>
    internal double FuelPrice =>
        _fuelBase * (1.0 + (0.05 * Math.Sin(((_lastDays / 5.0) + _phase) * Math.PI * 2.0)))
        * (1.0 + _fuelPressure);

    /// <summary>
    /// Advances the drift and decays the pressure. <paramref name="days"/> is the session's age in
    /// days, not a delta; calling it twice with the same value changes nothing.
    /// </summary>
    internal void Update(double days)
    {
        days = Math.Max(0.0, days);
        double elapsed = Math.Max(0.0, days - _lastDays);
        double decay = Math.Exp(-elapsed / PressureDecayDays);
        _lastDays = days;

        for (int i = 0; i < _goods.Length; i++)
        {
            double drift = 1.0
                + (0.10 * Math.Sin((((days / 3.0) + _phase + (i * 0.31)) * Math.PI * 2.0)));

            _mid[i] = _goods[i].MidPrice * drift;
            _pressure[i] *= decay;
        }

        _fuelPressure *= decay;
    }

    /// <summary>Buys one tonne of a good into the hold, if the credits and the hold allow it.</summary>
    internal bool TryBuy(int index, CargoHold hold, Ledger ledger, ref Ship ship)
    {
        MarketGood good = _goods[index];
        double price = BuyPrice(index);

        if (!hold.TryAdd(good.Name, 1.0))
        {
            return false;
        }

        if (!ledger.TrySpend(price, $"buy 1 t {good.Name.ToLowerInvariant()} at {price:N0}"))
        {
            hold.TryRemove(good.Name, 1.0);
            return false;
        }

        _pressure[index] += TradePressure;
        ship.Mass += Fix128.One;
        return true;
    }

    /// <summary>Sells one tonne from the hold. The buyer's price falls as the station takes it.</summary>
    internal bool TrySell(int index, CargoHold hold, Ledger ledger, ref Ship ship)
    {
        MarketGood good = _goods[index];
        double price = SellPrice(index);

        if (!hold.TryRemove(good.Name, 1.0))
        {
            return false;
        }

        _pressure[index] -= TradePressure;
        ship.Mass -= Fix128.One;
        ledger.Credit(price, $"sell 1 t {good.Name.ToLowerInvariant()} at {price:N0}");
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

        double price = FuelPrice;
        double cost = load * price;
        if (!ledger.TrySpend(cost, $"fuel {load:F1} t at {price:N0}"))
        {
            return false;
        }

        _fuelPressure += TradePressure * (load / FuelLotTonnes);

        Fix128 fuel = Fix128.FromDouble(load);
        ship.Propellant += fuel;
        ship.Mass += fuel;
        return true;
    }

    /// <summary>The two stations the contract knows about, with opposite prices.</summary>
    internal static Market For(string station) => station switch
    {
        "Waystation" => new Market(
            station,
            [
                new MarketGood("Machine parts", 3_400.0, 0.058823529),
                new MarketGood("Water", 250.0, 0.20),
            ],
            400.0,
            0.61),

        _ => new Market(
            station,
            [
                new MarketGood("Machine parts", 1_500.0, 1.0 / 3.0),
                new MarketGood("Water", 150.0, 1.0 / 3.0),
            ],
            250.0,
            0.12),
    };
}
