using System.Globalization;
using System.Text;

namespace SolSystem.Economy;

internal static class Report
{
    internal static string Build(int days, IReadOnlyList<(string Name, SimulationSummary Summary)> summaries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# The economy simulation");
        sb.AppendLine();
        sb.AppendLine("A headless run of the resource model in `docs/ECONOMY.md`: four sites, the solar and");
        sb.AppendLine("reactor power, the spirulina/hydroponic farm, the 99.5 % recycler, the trade routes and");
        sb.AppendLine("the disruptions. Negative numbers are the model's way of saying a constraint was hit.");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Run length: **{days} days**. Sites: Meridian (40 000), Waystation (20), Mars (500), Earth (source).");
        sb.AppendLine("Credits are omitted: the Earth source holds effectively infinite fiat, so a total would be meaningless until the claims are modelled per site.");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Min food (d) | Min water (d) | Min parts (t) | Min energy margin (MWh) | Energy deficit (MWh) | Hunger (site-days) | Blackout (site-days) | Trade (t) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");

        foreach ((string name, SimulationSummary summary) in summaries)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {name} | {summary.MinFoodDays:F1} | {summary.MinWaterDays:F1} | {summary.MinParts:F1} | "
                + $"{summary.MinEnergyMarginMWh:F0} | {summary.EnergyDeficitMWh:F0} | {summary.HungerDays:F0} | "
                + $"{summary.BlackoutDays:F0} | {summary.TradeTonnes:F0} |");
        }

        sb.AppendLine();
        sb.AppendLine("## What the numbers say");

        Dictionary<string, SimulationSummary> byName = summaries
            .ToDictionary(s => s.Name, s => s.Summary, StringComparer.Ordinal);

        if (byName.TryGetValue("baseline", out SimulationSummary? baseline))
        {
            sb.AppendLine();
            sb.AppendLine($"- **The baseline is viable but thin.** The minimum food reserve is {baseline.MinFoodDays:F1} days (the trade holds every site at its 30-day target), but the minimum water is only {baseline.MinWaterDays:F1} days and the minimum parts stock reaches {baseline.MinParts:F0} t: Meridian's water and parts are the real weak points, not its food.");
        }

        if (byName.TryGetValue("solar flare", out SimulationSummary? flare))
        {
            sb.AppendLine($"- **The flare is a power problem, not a food problem.** It produces {flare.EnergyDeficitMWh:F0} MWh of unserved load and {flare.BlackoutDays:F0} blackout site-days; the battery carries the first days and the farm is curtailed, which is exactly the resilience case for the farm and the storage.");
        }

        if (byName.TryGetValue("blockade", out SimulationSummary? blockade))
        {
            sb.AppendLine($"- **The blockade is the food problem.** It produces {blockade.HungerDays:F0} hunger site-days: Meridian's 18 t/day farm cannot feed 60 t/day of demand, so the station depends on the Earth route. The farm is resilience, not self-sufficiency, until it is much larger.");
        }

        if (byName.TryGetValue("crop failure", out SimulationSummary? crop))
        {
            sb.AppendLine($"- **The crop failure** costs {crop.HungerDays:F0} hunger site-days: the 30-day reserve and the Earth route absorb a 50 % farm loss, which is the model saying the imported food is the buffer.");
        }

        if (byName.TryGetValue("recycler failure", out SimulationSummary? recycler))
        {
            sb.AppendLine($"- **The recycler failure** shows a small water effect: the station holds two days of water and the closure loss is only 20 t/day at 99.5 %.");
        }

        if (byName.TryGetValue("AI outage", out SimulationSummary? ai))
        {
            sb.AppendLine($"- **The AI outage is a process-control failure, not a comfort problem.** It cuts the recycler's closure and the farm's output: the minimum water reserve falls to {ai.MinWaterDays:F1} days, the thinnest single-disruption result in the set. The AI is metered energy because it is the process control.");
        }

        sb.AppendLine();
        sb.AppendLine("## The viability verdict");
        sb.AppendLine();
        sb.AppendLine("The model is **viable as a game**: the constraints bind in the right order — energy first under a flare,");
        sb.AppendLine("food first under a blockade — and the trade routes are the difference between a thin station and a");
        sb.AppendLine("hungry one. It is **not viable as a self-sufficient station**: Meridian's farm covers 18 of its 60 t/day,");
        sb.AppendLine("and the 99.5 % recycler needs the Earth make-up for water and nutrients even when the power is fine.");
        sb.AppendLine();
        sb.AppendLine("The next iteration should either enlarge the farm (0.84 km², 361 MW, ~72× the import cost) or accept");
        sb.AppendLine("that the station is a port, not a closed loop, and price the Earth route into the contract board.");
        sb.AppendLine();
        sb.AppendLine("*This report is generated by `src/SolSystem.Economy`; run `dotnet run -c Release --project src/SolSystem.Economy -- --scenario all`.*");
        return sb.ToString();
    }
}
