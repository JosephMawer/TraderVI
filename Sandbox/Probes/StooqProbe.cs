using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Sandbox.Probes;

/// <summary>
/// Probes Stooq's daily CSV endpoint for the US confirming indices. Used as a
/// fallback / cross-check against the Yahoo source.
/// </summary>
public sealed class StooqProbe : IProbe
{
    public string Slug => "stooq";
    public string Description => "Stooq daily CSV — full history for ^GSPC / ^NYA / ^DJI (last 10 rows shown).";

    // Stooq symbol map. Canonical → Stooq.
    // Note Stooq uses ^spx (NOT ^gspc) for the S&P 500.
    private static readonly (string Canonical, string Stooq, string Description)[] StooqIndices =
    [
        ("^GSPC", "^spx", "S&P 500"),
        ("^NYA",  "^nya", "NYSE Composite"),
        ("^DJI",  "^dji", "Dow Jones Industrial Average"),
    ];

    public async Task RunAsync()
    {
        Console.WriteLine("=== Stooq daily CSV — US indices ===");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TraderVI-Sandbox/1.0");

        foreach (var (canonical, stooq, description) in StooqIndices)
        {
            // i=d → daily. Full history is returned in one CSV.
            var url = $"https://stooq.com/q/d/l/?s={stooq}&i=d";
            Console.WriteLine($"\n--- {canonical}  ({description})  via Stooq '{stooq}' ---");
            Console.WriteLine($"GET {url}");

            try
            {
                var csv = await http.GetStringAsync(url);
                if (string.IsNullOrWhiteSpace(csv) || csv.StartsWith("No data", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("⚠️  Stooq returned no data.");
                    continue;
                }

                var rows = ParseStooqCsv(csv).ToList();
                if (rows.Count == 0)
                {
                    Console.WriteLine("⚠️  CSV parsed to 0 rows. First 200 chars of response:");
                    Console.WriteLine(csv[..Math.Min(200, csv.Length)]);
                    continue;
                }

                Console.WriteLine($"{"Date",-12} {"Open",10} {"High",10} {"Low",10} {"Close",10} {"Volume",14}");
                Console.WriteLine(new string('─', 70));
                foreach (var r in rows.TakeLast(10))
                {
                    Console.WriteLine(
                        $"{r.Date:yyyy-MM-dd} {r.Open,10:F2} {r.High,10:F2} {r.Low,10:F2} {r.Close,10:F2} {r.Volume,14:N0}");
                }

                var first = rows.First();
                var last = rows.Last();
                Console.WriteLine($"Total bars: {rows.Count}  |  Range: {first.Date:yyyy-MM-dd} → {last.Date:yyyy-MM-dd}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Stooq probe failed for {stooq}: {ex.Message}");
            }
        }
    }

    private readonly record struct StooqBar(
        DateTime Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

    private static IEnumerable<StooqBar> ParseStooqCsv(string csv)
    {
        using var reader = new StringReader(csv);
        string? line = reader.ReadLine(); // header: Date,Open,High,Low,Close,Volume
        if (line is null) yield break;

        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(',');
            if (parts.Length < 5) continue;

            // Stooq sometimes returns "N/D" for missing volume on indices.
            if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            if (!decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var o)) continue;
            if (!decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var h)) continue;
            if (!decimal.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var l)) continue;
            if (!decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var c)) continue;

            long vol = 0;
            if (parts.Length >= 6)
                long.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out vol);

            yield return new StooqBar(date, o, h, l, c, vol);
        }
    }
}
