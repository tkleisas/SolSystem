namespace SolSystem.Economy;

/// <summary>The sites, routes and disruptions the first simulation runs.</summary>
internal static class Scenario
{
    internal static IReadOnlyList<(string Name, Simulation Simulation)> All()
    {
        return
        [
            ("baseline", Build([])),
            ("solar flare", Build([Flare()])),
            ("debris", Build([Debris()])),
            ("meteor storm", Build([Meteor()])),
            ("crop failure", Build([CropFailure()])),
            ("recycler failure", Build([RecyclerFailure()])),
            ("blockade", Build([Blockade()])),
            ("AI outage", Build([AiOutage()])),
            ("market shock", Build([MarketShock()])),
            ("combined", Build([Flare(), Debris(), Meteor(), CropFailure(), RecyclerFailure(), Blockade(), AiOutage(), MarketShock()])),
        ];
    }

    internal static Disruption Flare() => new(90, 5, "solar flare", null, Solar: 0.2, PartsLoss: 5);

    internal static Disruption Debris() => new(150, 1, "debris", null, Solar: 0.8, PartsLoss: 3);

    internal static Disruption Meteor() => new(210, 10, "meteor storm", null, Solar: 0.6, PartsLoss: 8);

    internal static Disruption CropFailure() => new(60, 30, "crop failure", "Meridian", Farm: 0.5);

    internal static Disruption RecyclerFailure() => new(120, 30, "recycler failure", "Meridian", Closure: 0.895);

    internal static Disruption Blockade() => new(180, 60, "blockade", null, Trade: 0);

    internal static Disruption AiOutage() => new(240, 20, "AI outage", "Meridian", AI: 0.5);

    internal static Disruption MarketShock() => new(300, 30, "market shock", null, Price: 1.5);

    internal static Simulation Build(IReadOnlyList<Disruption> events) =>
        new(Sites(), Links(), events);

    internal static IReadOnlyList<SiteSpec> Sites() =>
    [
        // 40 000 people, a 0.3 km² farm, 2 000 MWh of storage and 1 000 MWh/day of reactor.
        new SiteSpec(
            "Meridian", 40_000,
            SolarKwPerM2: 1.36, SolarKm2: 0.5, SolarEfficiency: 0.30, SunFraction: 0.60,
            BatteryMWh: 2_000, ReactorMWhPerDay: 1_000,
            FarmKm2: 0.30, FarmKwPerM2: 0.43, FarmYieldKgPerM2Day: 0.015,
            FoodPerPersonKg: 1.5, WaterPerPersonKg: 100,
            Closure: 0.995, RecyclerKWhPerTonne: 2.5, PartsWearPerDay: 0.5,
            AIKWhPerDay: 200, LifeSupportMWh: 0.5, HabitatMWh: 0.3,
            FoodPrice: 2_000, WaterPrice: 2_000, PartsPrice: 5_000,
            InitialFood: 240, InitialWater: 8_000, InitialNutrients: 120, InitialWaste: 0,
            InitialParts: 120, InitialCredits: 100_000),

        // 20 people on a Workers module; it imports nearly everything.
        new SiteSpec(
            "Waystation", 20,
            SolarKwPerM2: 1.36, SolarKm2: 0.01, SolarEfficiency: 0.30, SunFraction: 0.60,
            BatteryMWh: 50, ReactorMWhPerDay: 0,
            FarmKm2: 0, FarmKwPerM2: 0, FarmYieldKgPerM2Day: 0,
            FoodPerPersonKg: 1.5, WaterPerPersonKg: 100,
            Closure: 0.98, RecyclerKWhPerTonne: 2.5, PartsWearPerDay: 0.02,
            AIKWhPerDay: 20, LifeSupportMWh: 0.01, HabitatMWh: 0.01,
            FoodPrice: 3_600, WaterPrice: 4_000, PartsPrice: 8_000,
            InitialFood: 3, InitialWater: 60, InitialNutrients: 2, InitialWaste: 0,
            InitialParts: 10, InitialCredits: 5_000),

        // The source: the biosphere, the food and the parts, and the old fiat.
        new SiteSpec(
            "Earth", 0,
            SolarKwPerM2: 1.36, SolarKm2: 0, SolarEfficiency: 0, SunFraction: 0,
            BatteryMWh: 0, ReactorMWhPerDay: 0,
            FarmKm2: 0, FarmKwPerM2: 0, FarmYieldKgPerM2Day: 0,
            FoodPerPersonKg: 1.5, WaterPerPersonKg: 100,
            Closure: 1.0, RecyclerKWhPerTonne: 0, PartsWearPerDay: 0,
            AIKWhPerDay: 0, LifeSupportMWh: 0, HabitatMWh: 0,
            FoodPrice: 2_000, WaterPrice: 2_000, PartsPrice: 5_000,
            InitialFood: 1e6, InitialWater: 1e6, InitialNutrients: 1e6, InitialWaste: 0,
            InitialParts: 1e6, InitialCredits: 1e9),

        // 500 people on Mars, a small farm and a large solar deficit.
        new SiteSpec(
            "Mars", 500,
            SolarKwPerM2: 0.59, SolarKm2: 0.05, SolarEfficiency: 0.30, SunFraction: 0.50,
            BatteryMWh: 200, ReactorMWhPerDay: 0,
            FarmKm2: 0.01, FarmKwPerM2: 0.43, FarmYieldKgPerM2Day: 0.015,
            FoodPerPersonKg: 1.5, WaterPerPersonKg: 100,
            Closure: 0.995, RecyclerKWhPerTonne: 2.5, PartsWearPerDay: 0.05,
            AIKWhPerDay: 50, LifeSupportMWh: 0.02, HabitatMWh: 0.02,
            FoodPrice: 4_000, WaterPrice: 4_000, PartsPrice: 10_000,
            InitialFood: 3, InitialWater: 100, InitialNutrients: 2, InitialWaste: 0,
            InitialParts: 20, InitialCredits: 20_000),
    ];

    internal static IReadOnlyList<TradeLink> Links() =>
    [
        new("Earth", "Meridian", 9.4, 3, 100, 150),
        new("Earth", "Waystation", 9.4, 3, 20, 150),
        new("Earth", "Mars", 7.3, 200, 20, 400),
        new("Meridian", "Waystation", 0.0, 1, 20, 20),
        new("Meridian", "Mars", 5.0, 150, 20, 400),
    ];
}
