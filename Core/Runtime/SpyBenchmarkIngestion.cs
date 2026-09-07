#nullable enable
using Core.Indicators.Granville;
using Core.ML;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Core.Runtime;

public static class SpyBenchmarkIngestion
{
    public const string Symbol = "SPY";

    public static IReadOnlyList<DailyBar> SelectMissingBars(IReadOnlyList<DailyBar> existing,
        IReadOnlyList<UsIndexBar> response, DateTime from, DateTime through)
    {
        if (response.Any(bar => bar.Symbol != Symbol))
            throw new InvalidDataException("SPY collection returned a different benchmark identity.");
        var rows = response.Where(bar => bar.Date.Date >= from.Date && bar.Date.Date <= through.Date)
            .OrderBy(bar => bar.Date).ToArray();
        if (rows.Select(bar => bar.Date.Date).Distinct().Count() != rows.Length)
            throw new InvalidDataException("SPY collection returned duplicate dates.");
        var converted = rows.Select(bar => new DailyBar { Date = bar.Date.Date,
            Open = (float)bar.Open, High = (float)bar.High, Low = (float)bar.Low,
            Close = (float)bar.Close, Volume = bar.Volume }).ToArray();
        if (converted.Any(bar => !DailyBenchmarkPolicy.IsValidBar(bar)))
            throw new InvalidDataException("SPY collection returned malformed OHLCV.");
        var present = existing.Select(bar => bar.Date.Date).ToHashSet();
        return converted.Where(bar => !present.Contains(bar.Date)).ToArray();
    }
}
