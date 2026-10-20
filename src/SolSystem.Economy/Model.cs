using System.Globalization;

namespace SolSystem.Economy;

/// <summary>The fixed description of a site: what it is, what it needs and what it can make.</summary>
internal sealed record SiteSpec(
    string Name,
    int Population,
    double SolarKwPerM2,
    double SolarKm2,
    double SolarEfficiency,
    double SunFraction,
    double BatteryMWh,
    double ReactorMWhPerDay,
    double FarmKm2,
    double FarmKwPerM2,
    double FarmYieldKgPerM2Day,
    double FoodPerPersonKg,
    double WaterPerPersonKg,
    double Closure,
    double RecyclerKWhPerTonne,
    double PartsWearPerDay,
    double AIKWhPerDay,
    double LifeSupportMWh,
    double HabitatMWh,
    double FoodPrice,
    double WaterPrice,
    double PartsPrice,
    double InitialFood,
    double InitialWater,
    double InitialNutrients,
    double InitialWaste,
    double InitialParts,
    double InitialCredits,
    double FoodTargetDays = 30.0,
    double WaterTargetDays = 30.0,
    double PartsTarget = 30.0);

/// <summary>One site's mutable state and its daily step.</summary>
internal sealed class SiteState
{
    internal SiteState(SiteSpec spec)
    {
        Spec = spec;
        Food = spec.InitialFood;
        Water = spec.InitialWater;
        Nutrients = spec.InitialNutrients;
        Waste = spec.InitialWaste;
        Parts = spec.InitialParts;
        Credits = spec.InitialCredits;
        Battery = spec.BatteryMWh;
    }

    internal SiteSpec Spec { get; }

    internal double Food { get; set; }

    internal double Water { get; set; }

    internal double Nutrients { get; set; }

    internal double Waste { get; set; }

    internal double Parts { get; set; }

    internal double Credits { get; set; }

    internal double Battery { get; set; }

    internal double FoodMin { get; private set; } = double.MaxValue;

    internal double WaterMin { get; private set; } = double.MaxValue;

    internal double PartsMin { get; private set; } = double.MaxValue;

    internal double EnergyDeficitMWh { get; private set; }

    internal double MinMarginMWh { get; private set; } = double.MaxValue;

    internal double HungerDays { get; private set; }

    internal double BlackoutDays { get; private set; }

    internal double TradeTonnes { get; private set; }

    internal double FoodUse => Spec.Population * Spec.FoodPerPersonKg / 1000.0;

    internal double WaterUse => Spec.Population * Spec.WaterPerPersonKg / 1000.0;

    internal double FoodDays => FoodUse <= 0 ? double.PositiveInfinity : Food / FoodUse;

    internal double WaterDays => WaterUse <= 0 ? double.PositiveInfinity : Water / WaterUse;

    internal double SurplusFood => Math.Max(0.0, Food - (Spec.FoodTargetDays * FoodUse));

    internal double DeficitFood => Math.Max(0.0, (Spec.FoodTargetDays * FoodUse) - Food);

    internal double SurplusWater => Math.Max(0.0, Water - (Spec.WaterTargetDays * WaterUse));

    internal double DeficitWater => Math.Max(0.0, (Spec.WaterTargetDays * WaterUse) - Water);

    internal double SurplusParts => Math.Max(0.0, Parts - Spec.PartsTarget);

    internal double DeficitParts => Math.Max(0.0, Spec.PartsTarget - Parts);

    internal void Step(double solarFactor, double farmFactor, double closure, double aiFactor,
        double partsLoss)
    {
        // ---------------------------------------------------------------- power
        double solar = Spec.SolarKwPerM2 * Spec.SolarKm2 * 1e6 * Spec.SolarEfficiency
            * Spec.SunFraction * 24.0 / 1000.0 * solarFactor;
        double reactor = Spec.ReactorMWhPerDay;
        double farmDemand = Spec.FarmKm2 * 1e6 * Spec.FarmKwPerM2 * 24.0 / 1000.0 * farmFactor;
        double recycler = WaterUse * Spec.RecyclerKWhPerTonne / 1000.0;
        double fixedDemand = Spec.LifeSupportMWh + Spec.HabitatMWh
            + (Spec.AIKWhPerDay / 1000.0 * aiFactor);
        double demand = farmDemand + recycler + fixedDemand;
        double supply = solar + reactor;

        // Dispatch: life support and recycling first, the farm second, the rest is unserved.
        double priority = Math.Min(supply, recycler + fixedDemand);
        double farmAvailable = Math.Min(farmDemand, Math.Max(0.0, supply - priority));
        double served = priority + farmAvailable;
        double unserved = Math.Max(0.0, demand - served);

        double net = supply - demand;
        if (net >= 0.0)
        {
            Battery = Math.Min(Spec.BatteryMWh, Battery + net);
        }
        else
        {
            double draw = Math.Min(Battery, -net);
            Battery -= draw;
            unserved = Math.Max(0.0, -net - draw);
        }

        EnergyDeficitMWh += unserved;
        MinMarginMWh = Math.Min(MinMarginMWh, supply - demand);
        if (unserved > 0.01)
        {
            BlackoutDays++;
        }

        // ---------------------------------------------------------------- food, water, waste
        double farmFraction = farmDemand <= 0.0 ? 1.0 : farmAvailable / farmDemand;
        // The AI runs the farm's climate and the recycler's process control, so an AI
        // outage is an automation outage even when the power is fine.
        double farmFood = Spec.FarmKm2 * 1e6 * Spec.FarmYieldKgPerM2Day * farmFraction * 4.0
            / 1000.0 * (0.5 + 0.5 * aiFactor);
        Food += farmFood - FoodUse;
        if (Food < 0.0)
        {
            HungerDays++;
            Food = 0.0;
        }

        double loss = WaterUse * (1.0 - (closure * (0.5 + 0.5 * aiFactor)));
        Water -= loss;
        if (Water < 0.0)
        {
            Water = 0.0;
        }

        // Nutrients follow the food and the closure; the waste is what the recycler did not take.
        double nutrientUse = farmFood * 0.02;
        Nutrients += (nutrientUse * closure) - nutrientUse;
        if (Nutrients < 0.0)
        {
            Nutrients = 0.0;
        }

        double wasteProduced = FoodUse * 0.10 + WaterUse * 0.03;
        Waste += wasteProduced * (1.0 - closure);

        Parts -= (Spec.PartsWearPerDay * (1.0 + (unserved > 0.01 ? 0.25 : 0.0))) + partsLoss;
        if (Parts < 0.0)
        {
            Parts = 0.0;
        }

        FoodMin = Math.Min(FoodMin, Food);
        WaterMin = Math.Min(WaterMin, Water);
        PartsMin = Math.Min(PartsMin, Parts);
    }

    internal void Pay(double credits) => Credits += credits;

    internal void Charge(double credits) => Credits = Math.Max(0.0, Credits - credits);

    internal void AddTrade(double tonnes) => TradeTonnes += tonnes;

    internal string Format() => string.Create(CultureInfo.InvariantCulture,
        $"{Spec.Name,-10} food {Food,8:F1} t ({FoodDays,5:F1} d)  water {Water,8:F0} t "
        + $"({WaterDays,5:F1} d)  parts {Parts,6:F1}  energy deficit {EnergyDeficitMWh,8:F0} MWh  "
        + $"hunger {HungerDays,3:F0} d  credits {Credits,10:F0}");
}

/// <summary>A trade route: the capacity and cost of moving one tonne along it.</summary>
internal sealed record TradeLink(
    string From,
    string To,
    double DeltaV,
    int Days,
    double CapacityPerDay,
    double CostPerTonne);

/// <summary>A disruption, applied to one site or to the trade network.</summary>
internal sealed record Disruption(
    int StartDay,
    int Duration,
    string Kind,
    string? Target,
    double Solar = 1.0,
    double Farm = 1.0,
    double Closure = 0.995,
    double Trade = 1.0,
    double AI = 1.0,
    double Price = 1.0,
    double PartsLoss = 0.0);

/// <summary>The daily loop: the sites, the trade, the events, the metrics.</summary>
internal sealed class Simulation
{
    private readonly Dictionary<string, SiteState> _sites;
    private readonly IReadOnlyList<TradeLink> _links;

    internal Simulation(IReadOnlyList<SiteSpec> specs, IReadOnlyList<TradeLink> links,
        IReadOnlyList<Disruption> events)
    {
        _sites = specs.ToDictionary(s => s.Name, s => new SiteState(s), StringComparer.Ordinal);
        _links = links;
        Events = events;
    }

    internal IReadOnlyList<Disruption> Events { get; }

    internal IReadOnlyDictionary<string, SiteState> Sites => _sites;

    internal double TradeTonnes { get; private set; }

    internal double CreditsSpent { get; private set; }

    internal double PriceFactor { get; private set; } = 1.0;

    internal void Run(int days)
    {
        for (int day = 1; day <= days; day++)
        {
            List<Disruption> active = Events
                .Where(e => day >= e.StartDay && day < e.StartDay + e.Duration)
                .ToList();

            double tradeFactor = active.Where(e => e.Trade != 1.0).Select(e => e.Trade).DefaultIfEmpty(1.0).Min();
            PriceFactor = active.Where(e => e.Price != 1.0).Select(e => e.Price).DefaultIfEmpty(1.0).Max();

            foreach (SiteState site in _sites.Values)
            {
                // Global events (Target null) hit every site; a targeted event hits one.
                List<Disruption> relevant = active
                    .Where(e => e.Target is null || e.Target == site.Spec.Name)
                    .ToList();

                double solarFactor = relevant.Where(e => e.Solar != 1.0).Select(e => e.Solar).DefaultIfEmpty(1.0).Min();
                double farmFactor = relevant.Where(e => e.Farm != 1.0).Select(e => e.Farm).DefaultIfEmpty(1.0).Min();
                double closure = Math.Min(site.Spec.Closure,
                    relevant.Where(e => e.Closure != 0.995).Select(e => e.Closure).DefaultIfEmpty(site.Spec.Closure).Min());
                double aiFactor = relevant.Where(e => e.AI != 1.0).Select(e => e.AI).DefaultIfEmpty(1.0).Min();
                double partsLoss = relevant.Sum(e => e.PartsLoss);
                site.Step(solarFactor, farmFactor, closure, aiFactor, partsLoss);
            }

            Trade(tradeFactor);
        }
    }

    internal void Trade(double tradeFactor)
    {
        if (tradeFactor <= 0.0)
        {
            return;
        }

        foreach (TradeLink link in _links)
        {
            SiteState from = _sites[link.From];
            SiteState to = _sites[link.To];

            // The link carries the to-site's worst deficit first: food, then water, then parts.
            double capacity = Math.Min(link.CapacityPerDay, 1e6) * tradeFactor;

            Move(ref capacity, from, to, from.SurplusFood, to.DeficitFood,
                link.CostPerTonne * PriceFactor, (s, t) => { s.Food -= t; s.AddTrade(t); },
                (s, t) => { s.Food += t; });

            Move(ref capacity, from, to, from.SurplusWater, to.DeficitWater,
                link.CostPerTonne * 0.5 * PriceFactor, (s, t) => { s.Water -= t; s.AddTrade(t); },
                (s, t) => { s.Water += t; });

            Move(ref capacity, from, to, from.SurplusParts, to.DeficitParts,
                link.CostPerTonne * 2.0 * PriceFactor, (s, t) => { s.Parts -= t; s.AddTrade(t); },
                (s, t) => { s.Parts += t; });
        }
    }

    private static void Move(ref double capacity, SiteState from, SiteState to,
        double surplus, double deficit, double costPerTonne,
        Action<SiteState, double> remove, Action<SiteState, double> add)
    {
        if (capacity <= 0.0 || surplus <= 0.0 || deficit <= 0.0)
        {
            return;
        }

        double tonnes = Math.Min(Math.Min(capacity, surplus), deficit);
        if (tonnes <= 1e-9)
        {
            return;
        }

        remove(from, tonnes);
        add(to, tonnes);
        to.Charge(costPerTonne * tonnes);
        capacity -= tonnes;
    }

    internal SimulationSummary Summary(int days)
    {
        List<SiteState> sites = _sites.Values.ToList();

        return new SimulationSummary(
            days,
            sites.Min(s => s.FoodDays),
            sites.Min(s => s.WaterDays),
            sites.Min(s => s.PartsMin),
            sites.Min(s => s.MinMarginMWh),
            sites.Sum(s => s.EnergyDeficitMWh),
            sites.Sum(s => s.HungerDays),
            sites.Sum(s => s.BlackoutDays),
            sites.Sum(s => s.TradeTonnes),
            sites.Sum(s => s.Credits));
    }
}

/// <summary>The numbers a scenario is judged by.</summary>
internal sealed record SimulationSummary(
    int Days,
    double MinFoodDays,
    double MinWaterDays,
    double MinParts,
    double MinEnergyMarginMWh,
    double EnergyDeficitMWh,
    double HungerDays,
    double BlackoutDays,
    double TradeTonnes,
    double Credits);
