#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader;

/// <summary>Descriptive, cash-flow-free paper-account closing observations, not a promotion score.</summary>
public sealed record PortfolioClosingObservation(DateOnly Date, decimal? Value);
public sealed record PortfolioComparisonPoint(DateOnly Date, decimal? Index);
public sealed record PortfolioPeriodResult(DateOnly? From, DateOnly? Through, decimal? Return,
    decimal? ObservedDrawdown, int ValidCloses, int ObservedDates, string Evidence,
    IReadOnlyList<PortfolioComparisonPoint> Points);

public static class PortfolioComparison
{
    // A shared window is anchored to saved observations, never today's clock or an inferred quote.
    public static DateOnly[] Window(IEnumerable<PortfolioClosingObservation> observations, int? calendarDays)
    {
        var dates = observations.Select(x => x.Date).Distinct().OrderBy(x => x).ToArray();
        if (dates.Length == 0) return dates;
        DateOnly from = calendarDays.HasValue ? dates[^1].AddDays(-calendarDays.Value) : dates[0];
        return dates.Where(d => d >= from).ToArray();
    }

    public static PortfolioPeriodResult Calculate(IEnumerable<PortfolioClosingObservation> observations,
        IReadOnlyList<DateOnly> sharedDates)
    {
        var dates = sharedDates.Distinct().OrderBy(x => x).ToArray();
        if (dates.Length == 0) return new(null, null, null, null, 0, 0, "No saved closing history", []);
        // Ambiguous duplicates and explicitly incomplete marks remain unavailable, even if values agree.
        var values = observations.GroupBy(x => x.Date).ToDictionary(g => g.Key,
            g => g.Count() == 1 && g.Single().Value is >= 0m ? g.Single().Value : null);
        decimal? first = values.GetValueOrDefault(dates[0]);
        decimal? last = values.GetValueOrDefault(dates[^1]);
        int valid = dates.Count(d => values.GetValueOrDefault(d).HasValue);
        var points = dates.Select(d => new PortfolioComparisonPoint(d,
            first is > 0m ? values.GetValueOrDefault(d) / first * 100m : null)).ToArray();
        decimal? change = dates.Length >= 2 && first is > 0m && last.HasValue ? last / first - 1m : null;
        decimal? drawdown = null;
        if (dates.Length >= 2 && valid == dates.Length && first is > 0m)
        {
            decimal peak = first.Value, worst = 0m;
            foreach (DateOnly date in dates)
            {
                decimal value = values[date]!.Value;
                peak = System.Math.Max(peak, value);
                worst = System.Math.Min(worst, value / peak - 1m);
            }
            drawdown = worst;
        }
        string evidence = valid == 0 ? "No usable closes in this window"
            : dates.Length < 2 ? "At least two closing dates needed"
            : first is not > 0m || !last.HasValue ? "Matching endpoints unavailable"
            : valid < dates.Length ? $"{valid}/{dates.Length} observed dates · gaps"
            : $"{valid} saved closes · not promotion evidence";
        return new(dates[0], dates[^1], change, drawdown, valid, dates.Length, evidence, points);
    }
}
