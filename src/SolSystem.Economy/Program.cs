using System.Globalization;

namespace SolSystem.Economy;

internal static class Program
{
    private static int Main(string[] args)
    {
        int days = 365;
        string selected = "all";
        string outDir = Path.Combine("artifacts", "economy");
        bool printSites = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--days":
                    if (i + 1 >= args.Length || !int.TryParse(args[i + 1], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out days) || days <= 0)
                    {
                        Console.Error.WriteLine("--days expects a positive integer");
                        return 2;
                    }

                    i++;
                    break;
                case "--scenario":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--scenario expects a name");
                        return 2;
                    }

                    selected = args[++i];
                    break;
                case "--out":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("--out expects a directory");
                        return 2;
                    }

                    outDir = args[++i];
                    break;
                case "--sites":
                    printSites = true;
                    break;
                case "--help":
                    Console.WriteLine("usage: SolSystem.Economy [--days N] [--scenario NAME|all] [--out DIR]");
                    Console.WriteLine("scenarios: " + string.Join(", ", Scenario.All().Select(s => s.Name)));
                    return 0;
                default:
                    Console.Error.WriteLine($"unknown argument '{args[i]}'");
                    return 2;
            }
        }

        List<(string Name, Simulation Simulation)> scenarios = Scenario.All()
            .Where(s => selected == "all" || s.Name == selected)
            .ToList();

        if (scenarios.Count == 0)
        {
            Console.Error.WriteLine($"unknown scenario '{selected}'");
            return 2;
        }

        Directory.CreateDirectory(outDir);
        List<(string Name, SimulationSummary Summary)> summaries = [];
        foreach ((string name, Simulation simulation) in scenarios)
        {
            simulation.Run(days);
            SimulationSummary summary = simulation.Summary(days);
            summaries.Add((name, summary));
            Console.WriteLine($"{name,-18} food {summary.MinFoodDays,6:F1} d  water {summary.MinWaterDays,6:F1} d  "
                + $"parts {summary.MinParts,6:F1} t  energy deficit {summary.EnergyDeficitMWh,8:F0} MWh  "
                + $"hunger {summary.HungerDays,5:F0}  blackout {summary.BlackoutDays,4:F0}  "
                + $"trade {summary.TradeTonnes,8:F0} t");
            if (printSites)
            {
                foreach (SiteState site in simulation.Sites.Values)
                {
                    Console.WriteLine("  " + site.Format());
                }
            }
        }

        string report = Report.Build(days, summaries);
        string path = Path.Combine(outDir, "report.md");
        File.WriteAllText(path, report);
        Console.WriteLine($"wrote {path}");
        return 0;
    }
}
