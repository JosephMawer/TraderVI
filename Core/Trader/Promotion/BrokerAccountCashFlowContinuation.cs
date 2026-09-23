#nullable enable
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.Promotion;

/// <summary>
/// One confirmed external capital movement. EventKey is the stable uppercase SHA-256 of the owned
/// account and broker event identity, never a new local request ID. Before/After reconstruct the SAME
/// effective instant and identical held shares/price evidence, differing only by this cash movement.
/// The producer must establish actual cash-effect timing, completion and strategy-compatible marks;
/// a feed occurrence time, pending deposit or graph/net-deposit delta alone cannot establish this.
/// </summary>
public sealed record BrokerRiskCashFlowEvent(string EventKey, decimal Amount,
    BrokerRiskBoundaryBook Before, BrokerRiskBoundaryBook After)
{
    public override string ToString() => "Confirmed external cash movement (financial data omitted)";
}

/// <summary>
/// Complete reconciled external-flow coverage over (From, Through]. Events are in effective order;
/// simultaneous events need a verified cash-book chain in that order. The source fingerprint includes
/// the complete event/settlement evidence, including resolved pending/cancelled entries. Orders,
/// dividends, interest, fees and internal movements within this account are not external capital.
/// Empty coverage uses BrokerRiskNoFlowEvidence instead. This is an evidence contract, not a broker reader.
/// </summary>
public sealed record BrokerRiskCashFlowInterval(Guid EvidenceId, Guid BindingId,
    DateTimeOffset From, DateTimeOffset Through, DateTimeOffset ObservedAt,
    string SourceFingerprint, ImmutableArray<BrokerRiskCashFlowEvent> Events)
{
    public override string ToString() => "Reconciled external cash-flow interval (financial data omitted)";
}

/// <summary>Preserves percentage loss and existing holds without rewriting the initial baseline.</summary>
public static class BrokerAccountCashFlowContinuation
{
    internal static BrokerAccountRiskState Apply(BrokerAccountRiskState state, BrokerRiskContinuationRequest request,
        ITsxSessionCalendar calendar, DateTimeOffset now)
    {
        var interval = request.CashFlows!;
        if (interval.EvidenceId == Guid.Empty || interval.BindingId != state.BindingId ||
            interval.From != state.Through || interval.Through <= interval.From ||
            interval.ObservedAt < interval.Through || interval.ObservedAt > now ||
            !CompleteTradingStrategy.IsHash(interval.SourceFingerprint) || interval.Events.IsDefaultOrEmpty)
            throw new InvalidOperationException("Complete, owned and timed cash-flow reconciliation is required.");

        // Process daily boundaries in order, even when flows arrive overnight or exactly at the close.
        // An initial setup after the close has no obligation to invent an earlier closing value.
        var nextBoundary = calendar.IsRegularSession(state.TradingDate) &&
            state.LastCompletedSession != state.TradingDate &&
            state.Through <= calendar.GetSessionBounds(state.TradingDate).CloseUtc
                ? calendar.GetSessionBounds(state.TradingDate).CloseUtc
                : calendar.GetSessionBounds(calendar.GetNextSession(state.TradingDate)).OpenUtc;
        if (interval.Through > nextBoundary)
            throw new InvalidOperationException("Continue the next session boundary before later cash movements.");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        BrokerRiskCashFlowEvent? previous = null;
        var session = state.SessionReference;
        var high = state.DrawdownReference;
        foreach (var flow in interval.Events)
        {
            if (flow is null || !CompleteTradingStrategy.IsHash(flow.EventKey) ||
                flow.EventKey != flow.EventKey.ToUpperInvariant() || !keys.Add(flow.EventKey) || flow.Amount == 0m ||
                flow.Before is null || flow.After is null)
                throw new InvalidOperationException("Each cash movement needs a unique, stable broker identity and signed amount.");
            var before = flow.Before;
            var after = flow.After;
            ValidateBook(before, state.BindingId, interval.ObservedAt);
            ValidateBook(after, state.BindingId, interval.ObservedAt);
            if (before.At != after.At || before.At <= interval.From || before.At > interval.Through ||
                before.EvidenceId == after.EvidenceId || after.Cash - before.Cash != flow.Amount ||
                !SameHoldings(before, after) || previous is not null &&
                (before.At < previous.After.At || before.At == previous.After.At &&
                    (before.EvidenceId != previous.After.EvidenceId || before.VerifiedAt != previous.After.VerifiedAt ||
                        before.Cash != previous.After.Cash || !SameHoldings(before, previous.After))))
                throw new InvalidOperationException("Cash-flow books must isolate the confirmed movement at its effective instant.");

            decimal valueBefore = Value(before), valueAfter = Value(after);
            if (valueBefore <= 0m || valueAfter <= 0m)
                throw new InvalidOperationException("A cash movement cannot reset risk history through a zero or negative account value.");
            // Keep full decimal precision: adding Amount to a reference would dilute the loss percentage.
            session = Adjust(session, valueBefore, valueAfter, after);
            high = Adjust(high, valueBefore, valueAfter, after);
            previous = flow;
        }
        return state with { RevisionId = request.RequestId, SessionReference = session, DrawdownReference = high,
            Through = interval.Through };
    }

    /// <summary>Store must check all prior event keys under the same account fence as its append.</summary>
    internal static void RequireUnseenEvents(BrokerRiskContinuationRequest request, IEnumerable<string> savedKeys)
    {
        if (request.CashFlows is not { } interval) return;
        var existing = savedKeys.ToHashSet(StringComparer.Ordinal);
        if (interval.Events.Any(e => existing.Contains(e.EventKey)))
            throw new InvalidOperationException("This broker cash movement is already saved. Recover its request or reconcile the changed evidence.");
    }

    private static void ValidateBook(BrokerRiskBoundaryBook book, Guid bindingId, DateTimeOffset observedAt)
    {
        if (book.EvidenceId == Guid.Empty || book.BindingId != bindingId || book.At == default ||
            book.VerifiedAt < book.At || book.VerifiedAt > observedAt || book.Cash < 0m || book.Holdings.IsDefault ||
            book.Holdings.Any(h => h is null || h.PositionId == Guid.Empty || string.IsNullOrWhiteSpace(h.Symbol) ||
                h.Quantity <= 0 || h.SavedPrice is not > 0m || h.SavedPriceEvidenceId is null ||
                h.SavedPriceEvidenceId == Guid.Empty || h.SavedPriceAt is null || h.SavedPriceAt == default(DateTimeOffset) ||
                h.SavedPriceAt > book.At) ||
            book.Holdings.Select(h => h.PositionId).Distinct().Count() != book.Holdings.Length ||
            book.Holdings.Select(h => h.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() != book.Holdings.Length)
            throw new InvalidOperationException("A cash-flow valuation needs a complete reconciled book and identified price evidence.");
    }

    private static bool SameHoldings(BrokerRiskBoundaryBook a, BrokerRiskBoundaryBook b) =>
        a.Holdings.OrderBy(h => h.PositionId).SequenceEqual(b.Holdings.OrderBy(h => h.PositionId));
    private static decimal Value(BrokerRiskBoundaryBook book) => book.Cash + book.Holdings.Sum(h => h.Quantity * h.SavedPrice!.Value);
    private static BrokerRiskReference Adjust(BrokerRiskReference reference, decimal before, decimal after, BrokerRiskBoundaryBook book)
    {
        decimal adjusted = reference.Value * after / before;
        if (adjusted <= 0m) throw new InvalidOperationException("The cash-adjusted reference is outside supported decimal precision.");
        return new(adjusted, BrokerRiskReferenceSource.ConfirmedCashFlowAdjustment, book.EvidenceId, book.At);
    }
}
