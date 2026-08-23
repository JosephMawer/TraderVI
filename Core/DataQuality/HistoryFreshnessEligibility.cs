#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.DataQuality;

/// <summary>
/// Result of comparing one symbol's newest daily bar with Delphi's canonical
/// market-data session.
/// </summary>
public sealed record HistoryFreshnessDecision(
    bool IsEligible,
    DateTime? LatestBarDate,
    int SessionsBehind,
    string Reason);

/// <summary>
/// Hard universe-eligibility rule: Delphi may evaluate only symbols whose
/// newest daily bar belongs to the same completed TSX session as XIU.
/// </summary>
public static class HistoryFreshnessEligibility
{
    public static HistoryFreshnessDecision Evaluate(
        DateTime? latestBarDate,
        DateTime marketDataAsOf,
        IReadOnlyList<DateTime> benchmarkSessions)
    {
        DateTime referenceDate = marketDataAsOf.Date;
        DateTime[] completedSessions = benchmarkSessions
            .Select(session => session.Date)
            .Where(session => session <= referenceDate)
            .Distinct()
            .OrderBy(session => session)
            .ToArray();

        if (!latestBarDate.HasValue)
        {
            return new HistoryFreshnessDecision(
                false,
                null,
                completedSessions.Length,
                "No daily price history is available.");
        }

        DateTime latest = latestBarDate.Value.Date;
        int sessionsBehind = MarketDataFreshness.CountSessionsBehind(latest, completedSessions);

        if (latest == referenceDate)
        {
            return new HistoryFreshnessDecision(
                true,
                latest,
                0,
                "Latest bar matches the canonical market-data session.");
        }

        if (latest > referenceDate)
        {
            return new HistoryFreshnessDecision(
                false,
                latest,
                0,
                "Latest bar is after the canonical market-data session.");
        }

        return new HistoryFreshnessDecision(
            false,
            latest,
            sessionsBehind,
            $"Latest bar is {sessionsBehind} completed TSX session(s) behind.");
    }
}

/// <summary>
/// Reporting detail retained for each symbol rejected by the freshness rule.
/// </summary>
public sealed record HistoryFreshnessExclusion(
    string Symbol,
    DateTime LatestBarDate,
    int SessionsBehind,
    string Reason);
