using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sandbox.Probes;

namespace Sandbox;

/// <summary>
/// Sandbox dispatcher: pick a probe by slug.
///
/// Usage:
///   dotnet run --project Sandbox -- &lt;slug&gt;
///   dotnet run --project Sandbox                (no args → prints the list)
///
/// Adding a new probe = implement <see cref="IProbe"/>, add one entry to the
/// registry below.
/// </summary>
internal static class Program
{
    private static readonly IReadOnlyList<IProbe> Probes =
    [
        new YahooChartProbe(),
        new StooqProbe(),
        new TmxUsIndicesProbe(),
        new TmxSectorHistoryProbe(),
        new TmxXiuIntradayProbe(),
        new TmxXiuMarketHoursPollingProbe(),
        new DullnessCalibrationProbe(),
        new ObvBackfillProbe(),
        new ClimaxBackfillProbe(),
    ];

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var slug = args[0].Trim();
        var probe = Probes.FirstOrDefault(p =>
            string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));

        if (probe is null)
        {
            Console.WriteLine($"Unknown probe slug: '{slug}'.");
            Console.WriteLine();
            PrintUsage();
            return 2;
        }

        await probe.RunAsync();
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Sandbox — TraderVI one-shot probes / calibration scripts.");
        Console.WriteLine();
        Console.WriteLine("Usage:  dotnet run --project Sandbox -- <slug>");
        Console.WriteLine();
        Console.WriteLine("Available probes:");
        int width = Probes.Max(p => p.Slug.Length);
        foreach (var p in Probes)
            Console.WriteLine($"  {p.Slug.PadRight(width)}   {p.Description}");
    }
}
