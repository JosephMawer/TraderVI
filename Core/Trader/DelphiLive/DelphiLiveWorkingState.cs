#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.DelphiLive;

/// <summary>Totals for archived records excluded from the operational window. Never changes original capital.</summary>
public sealed record DelphiLiveWorkingTotals(decimal ArchivedCashNet, decimal ArchivedRealizedProfitLoss);

public sealed record DelphiLiveWorkingSelection(DelphiLivePortfolioSnapshot Snapshot,
    IReadOnlyDictionary<string, ImmutableArray<int>> RetainedIndices);

public static class DelphiLiveWorkingState
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");

    /// <summary>Call only after validating a complete transition, before persisting its next current checkpoint.</summary>
    public static DelphiLiveWorkingSelection Select(DelphiLivePortfolioSnapshot state)
    {
        var retained = new Dictionary<string, ImmutableArray<int>>(StringComparer.Ordinal);
        DateOnly date = state.CurrentSession ?? state.EffectiveSession;
        var latestClosed = state.Positions.Where(p => p.ClosedUtc.HasValue)
            .GroupBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.MaxBy(p => p.ClosedUtc)!.PositionId).ToHashSet();
        var positions = Keep("positions", state.Positions, p => p.ClosedUtc is null ||
            LocalDate(p.ClosedUtc.Value) >= date || latestClosed.Contains(p.PositionId));
        var actions = Keep("actions", state.Actions, a => a.TradingDate >= date || a.Status is "Pending" or "ExitPendingOvernight");
        var pending = actions.Where(a => a.Status is "Pending" or "ExitPendingOvernight")
            .ToDictionary(a => a.Intent.ActionId, a => a.AttemptCount);
        var quotes = Keep("quotes", state.Quotes, q => q.ActionId is Guid action &&
            pending.TryGetValue(action, out int attempt) && q.Observation.AttemptNumber == attempt);
        var exitIds = positions.Where(p => p.ClosedUtc.HasValue).Select(p => p.ExitActionId).ToHashSet();
        var fills = Keep("fills", state.Fills, f => f.TradingDate >= date || exitIds.Contains(f.ActionId));
        var lastMark = state.Marks.LastOrDefault()?.MarkId;
        var marks = Keep("marks", state.Marks, m => m.TradingDate >= date || m.MarkId == lastMark);
        retained["sessionOpeningPositions"] = Enumerable.Range(0, state.SessionOpeningPositions.Length).ToImmutableArray();
        var keptPositionIds = positions.Select(p => p.PositionId).ToHashSet();
        var sales = state.Fills.Where(f => f.Side == DelphiLiveActionSide.Sell).ToDictionary(f => f.ActionId);
        decimal newlyArchivedProfit = state.Positions.Where(p => p.ClosedUtc.HasValue && !keptPositionIds.Contains(p.PositionId))
            .Sum(p => sales[p.ExitActionId!.Value].Quantity * (sales[p.ExitActionId.Value].Price - p.AveragePurchasePrice));
        var totals = new DelphiLiveWorkingTotals(state.Cash - state.StartingCapital - CashNet(fills),
            (state.WorkingTotals?.ArchivedRealizedProfitLoss ?? 0m) + newlyArchivedProfit);
        return new(state with { Positions = positions, Actions = actions, Fills = fills, Quotes = quotes, Marks = marks,
            WorkingTotals = totals }, retained);

        ImmutableArray<T> Keep<T>(string name, ImmutableArray<T> items, Func<T, bool> predicate)
        {
            var indices = Enumerable.Range(0, items.Length).Where(i => predicate(items[i])).ToImmutableArray();
            retained[name] = indices;
            return indices.Select(i => items[i]).ToImmutableArray();
        }
    }

    public static decimal CashNet(IEnumerable<DelphiLiveLedgerFill> fills) =>
        fills.Sum(f => (f.Side == DelphiLiveActionSide.Sell ? 1m : -1m) * f.Price * f.Quantity);
    private static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Zone));
}
