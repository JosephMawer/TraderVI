#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.DelphiLive;

public sealed record DelphiLivePortfolioPerformanceSummary(
    Guid PortfolioId, Guid GenerationId, Guid PolicyVersionId, string Role, string Currency,
    DateOnly Inception, DateOnly Through, decimal StartingCapital,
    decimal? TotalReturn, decimal? MaximumCheckpointDrawdown, int CompletedTradeCount, decimal? WinRate,
    decimal? MeanCheckpointExposure, decimal GrossTurnoverVsStartingCapital,
    int RequestedActionCount, int NoFillCount, decimal? NoFillRate, int PendingActionCount,
    int EstimatedFillCount, decimal? EstimatedFillRate,
    DelphiLiveMetricCoverage CheckpointCoverage, ImmutableDictionary<string, int> ExitCountsByReason);

/// <summary>
/// Descriptive generation statistics only. Every observed valid fill, including
/// EstimatedFill, participates; these metrics cannot approve a promotion.
/// </summary>
public static class DelphiLivePortfolioScorecard
{
    public static DelphiLivePortfolioPerformanceSummary Calculate(DelphiLivePortfolioSnapshot portfolio,
        DateOnly through, ITsxSessionCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        ArgumentNullException.ThrowIfNull(calendar);
        if (through < portfolio.EffectiveSession || portfolio.StartingCapital <= 0m)
            throw new ArgumentException("The report requires positive generation capital and a cutoff at or after inception.");
        var dates = new List<DateOnly>();
        for (DateOnly date = portfolio.EffectiveSession; date <= through; date = date.AddDays(1))
            if (calendar.IsRegularSession(date)) dates.Add(date);
        if (dates.Count == 0) throw new ArgumentException("The generation has no canonical session inside this report.");
        DateTime cutoff = calendar.GetSessionBounds(dates[^1]).CloseUtc;
        var marks = portfolio.Marks.Where(m => m.TradingDate >= portfolio.EffectiveSession && m.BarEndUtc <= cutoff)
            .GroupBy(m => m.BarEndUtc).ToDictionary(g => g.Key, g => g.Last());
        var endpoints = dates.SelectMany(date => Enumerable.Range(1, 78)
            .Select(n => (Date: date, End: calendar.GetSessionBounds(date).OpenUtc.AddMinutes(5 * n)))).ToArray();
        bool Usable(DelphiLiveLedgerMark? mark) => mark is { Complete: true, Nav: >= 0m } && mark.Reason != "CorporateActionUnsupported";
        var coverage = DelphiLiveCoverageCalculator.Calculate(endpoints.Select(e => Usable(marks.GetValueOrDefault(e.End))
            ? DelphiLiveOutcomeMetricState.Valid : DelphiLiveOutcomeMetricState.Invalid), DelphiLivePolicyDefinition.Version1);
        bool permitted = coverage.Readiness is DelphiLiveCoverageReadiness.Ready or DelphiLiveCoverageReadiness.Degraded;
        var closing = marks.GetValueOrDefault(cutoff);
        decimal? totalReturn = Usable(closing) && closing!.Kind == DelphiLivePortfolioMarkKind.Closing
            ? closing.Nav / portfolio.StartingCapital - 1m : null;
        decimal? drawdown = permitted
            ? DelphiLiveResearchScorecards.MaximumCheckpointDrawdown(portfolio.StartingCapital,
                endpoints.Where(e => Usable(marks.GetValueOrDefault(e.End))).Select(e => marks[e.End].Nav!.Value)) : null;
        // Average exact marked exposure within a session, then weight sessions
        // equally. Missing endpoints remain visible in coverage; no stale mark is carried.
        var dailyExposure = endpoints.GroupBy(e => e.Date).Select(group =>
        {
            var values = group.Where(e => Usable(marks.GetValueOrDefault(e.End)) && marks[e.End].Nav > 0m)
                .Select(e => marks[e.End].Positions.Sum(p => p.Quantity * p.Price) / marks[e.End].Nav!.Value).ToArray();
            return values.Length > 0 ? (decimal?)values.Average() : null;
        }).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        decimal? exposure = permitted && dailyExposure.Length > 0 ? dailyExposure.Average() : null;
        var fills = portfolio.Fills.Where(f => f.FilledUtc <= cutoff).ToArray();
        var filledActions = fills.Select(f => f.ActionId).ToHashSet();
        var actions = portfolio.Actions.Where(a => a.Intent.DecisionUtc <= cutoff).ToArray();
        var completed = portfolio.Positions.Where(p => p.ClosedUtc <= cutoff).Select(position =>
        {
            var buy = fills.Single(f => f.ActionId == position.EntryActionId);
            var sell = fills.Single(f => f.ActionId == position.ExitActionId);
            return sell.Price * sell.Quantity - buy.Price * buy.Quantity;
        }).ToArray();
        int noFill = actions.Count(a => !filledActions.Contains(a.Intent.ActionId));
        int pending = actions.Count(a => !filledActions.Contains(a.Intent.ActionId) &&
            (a.CompletedUtc is null || a.CompletedUtc > cutoff));
        int estimated = fills.Count(f => f.Confidence == DelphiLiveFillConfidence.EstimatedFill);
        var exits = actions.Where(a => a.Intent.Side == DelphiLiveActionSide.Sell && filledActions.Contains(a.Intent.ActionId))
            .GroupBy(a => a.PrimaryReason).ToImmutableDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        return new(portfolio.PortfolioId, portfolio.GenerationId, portfolio.PolicyVersionId, portfolio.Role, portfolio.Currency,
            portfolio.EffectiveSession, through, portfolio.StartingCapital, totalReturn, drawdown, completed.Length,
            completed.Length == 0 ? null : (decimal)completed.Count(pnl => pnl > 0m) / completed.Length,
            exposure, fills.Sum(f => f.Price * f.Quantity) / portfolio.StartingCapital,
            actions.Length, noFill, actions.Length == 0 ? null : (decimal)noFill / actions.Length, pending,
            estimated, fills.Length == 0 ? null : (decimal)estimated / fills.Length, coverage, exits);
    }
}
