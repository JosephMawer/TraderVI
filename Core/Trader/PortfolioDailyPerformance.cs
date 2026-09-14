#nullable enable
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader;

/// <summary>One paper session's saved opening and ending evidence, independent of comparison windows.</summary>
public sealed record PortfolioDailyObservation(DateOnly Date, decimal? OpeningValue, decimal? Value,
    DateTime? AsOfUtc, bool IsClosing, string Evidence);

public sealed record PortfolioDailyResult(DateOnly? Date, decimal? OpeningValue, decimal? Value,
    decimal? Gain, decimal? Return, DateTime? AsOfUtc, bool IsClosing, string Evidence);

public static class PortfolioDailyPerformance
{
    // These paper ledgers have no in-session deposits/withdrawals. Never apply this calculation to
    // broker/reference accounts without a cash-flow ledger, or substitute lifetime funding capital.
    public static PortfolioDailyResult Calculate(IEnumerable<PortfolioDailyObservation> history, DateOnly? date)
    {
        var days = history.Where(d => d.Date == date).ToArray();
        if (days.Length != 1)
            return new(date, null, null, null, null, null, false,
                days.Length == 0 ? "No saved session for this day" : "Conflicting daily observations");
        var day = days[0];
        decimal? opening = day.OpeningValue is > 0m ? day.OpeningValue : null;
        decimal? value = day.Value is >= 0m ? day.Value : null;
        decimal? gain = value - opening;
        return new(date, opening, value, gain, gain / opening, day.AsOfUtc, day.IsClosing,
            opening is null ? "Opening valuation unavailable" : day.Evidence);
    }

    public static IReadOnlyList<PortfolioDailyObservation> FromLiveMarks(IEnumerable<DelphiLiveLedgerMark> marks)
    {
        return marks.GroupBy(m => m.TradingDate).OrderBy(g => g.Key).Select(day =>
        {
            // Opening observations may be retried while prices arrive. Later evidence supersedes
            // earlier attempts; never fall back to a complete mark after an incomplete latest mark.
            var opening = day.LastOrDefault(m => m.Kind == DelphiLivePortfolioMarkKind.Opening);
            var latest = day.Where(m => m.Kind != DelphiLivePortfolioMarkKind.Opening)
                .OrderBy(m => m.BarEndUtc).LastOrDefault();
            bool closing = latest?.Kind == DelphiLivePortfolioMarkKind.Closing;
            string evidence = latest is null ? "Waiting for a valuation after opening"
                : !Usable(latest) ? "Latest valuation incomplete" + (string.IsNullOrWhiteSpace(latest.Reason) ? "" : $" · {latest.Reason}")
                : closing ? "Saved close" : "Partial day · saved checkpoint; no closing valuation";
            return new PortfolioDailyObservation(day.Key, Usable(opening) ? opening!.Nav : null,
                Usable(latest) ? latest!.Nav : null, latest?.BarEndUtc, closing, evidence);
        }).ToArray();
    }

    private static bool Usable(DelphiLiveLedgerMark? mark) =>
        mark is { Complete: true, Nav: >= 0m } && mark.Reason != "CorporateActionUnsupported";
}
