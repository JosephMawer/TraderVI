#nullable enable
using System;

namespace Core.Trader;

/// <summary>ADR-0090. State belongs to one actual position cycle, never to a symbol globally.</summary>
public sealed record ProgressiveProfitProtectionState(
    decimal AverageCost, decimal? HighestClose = null, decimal? TightestGap = null,
    decimal? StopPrice = null, DateTime? LastCloseEndUtc = null, DateTime? StopEstablishedUtc = null,
    DateTime? LastBidReceivedUtc = null, DateTime? NextBidCheckUtc = null, decimal? LastBid = null,
    bool ExitTriggered = false)
{
    public string RuleVersion { get; init; } = ProgressiveProfitProtection.Version;
    public DateTime? LastQuoteRequestedUtc { get; init; }
    public DateTime? LastQuoteReceivedUtc { get; init; }
    public string? QuoteSourceContract { get; init; }
}

public sealed record ProgressiveHistoricalClose(DateTime EndUtc, decimal Close, DateTime ReceivedUtc, decimal AverageCost);
public sealed record ProgressiveProtectionQuote(decimal? Bid, DateTime RequestedUtc, DateTime ReceivedUtc, string SourceContract);

public sealed record ProgressiveBidDecision(ProgressiveProfitProtectionState State, bool Breached, bool Usable);

/// <summary>Pure calculation; callers persist a raised stop before requesting its trigger quote.</summary>
public static class ProgressiveProfitProtection
{
    public const string Version = "ProgressiveProfitProtectionV1";
    public const decimal NearStopFraction = 0.0025m;
    public static readonly TimeSpan MaximumReceiptAge = TimeSpan.FromSeconds(60);

    public static ProgressiveProfitProtectionState Carry(decimal averageCost, decimal? existingStop,
        DateTime nowUtc, decimal? highestFiveMinuteClose = null)
    {
        Price(averageCost); Utc(nowUtc);
        if (existingStop.HasValue) Price(existingStop.Value);
        if (highestFiveMinuteClose.HasValue) Price(highestFiveMinuteClose.Value);
        var state = new ProgressiveProfitProtectionState(averageCost, highestFiveMinuteClose,
            StopPrice: existingStop, StopEstablishedUtc: existingStop.HasValue ? nowUtc : null);
        return highestFiveMinuteClose.HasValue ? Raise(state, highestFiveMinuteClose.Value, nowUtc) : state;
    }

    public static ProgressiveProfitProtectionState ObserveClose(ProgressiveProfitProtectionState state,
        decimal averageCost, DateTime barEndUtc, decimal close, DateTime persistedUtc, decimal? existingStop = null)
    {
        Validate(state); Price(averageCost); Price(close); Utc(barEndUtc); Utc(persistedUtc);
        if (barEndUtc > persistedUtc || barEndUtc.Ticks % TimeSpan.FromMinutes(5).Ticks != 0)
            throw new ArgumentException("A received, completed five-minute close is required.");
        state = Preserve(state with { AverageCost = averageCost }, existingStop, persistedUtc);
        if (state.LastCloseEndUtc >= barEndUtc) return state;
        state = state with { HighestClose = System.Math.Max(state.HighestClose ?? close, close), LastCloseEndUtc = barEndUtc };
        return Raise(state, close, persistedUtc);
    }

    public static ProgressiveProfitProtectionState Preserve(ProgressiveProfitProtectionState state,
        decimal? existingStop, DateTime persistedUtc)
    {
        Validate(state); Utc(persistedUtc);
        if (!existingStop.HasValue) return state;
        Price(existingStop.Value);
        return state.StopPrice is null || existingStop > state.StopPrice
            ? state with { StopPrice = existingStop, StopEstablishedUtc = persistedUtc, NextBidCheckUtc = null }
            : state;
    }

    public static bool IsDue(ProgressiveProfitProtectionState state, DateTime nowUtc)
    {
        Validate(state); Utc(nowUtc);
        return state.ExitTriggered || !state.NextBidCheckUtc.HasValue || nowUtc >= state.NextBidCheckUtc.Value;
    }

    public static ProgressiveBidDecision ObserveBid(ProgressiveProfitProtectionState state,
        decimal? bid, DateTime requestedUtc, DateTime receivedUtc, DateTime evaluatedUtc, string sourceContract = "Unspecified")
    {
        Validate(state); Utc(requestedUtc); Utc(receivedUtc); Utc(evaluatedUtc);
        state = state with { LastQuoteRequestedUtc = requestedUtc, LastQuoteReceivedUtc = receivedUtc, QuoteSourceContract = sourceContract };
        bool usable = bid is > 0m && requestedUtc <= receivedUtc && receivedUtc <= evaluatedUtc &&
            evaluatedUtc - receivedUtc <= MaximumReceiptAge &&
            (state.StopEstablishedUtc is null || requestedUtc >= state.StopEstablishedUtc && receivedUtc > state.StopEstablishedUtc);
        if (!usable)
            return new(state with { NextBidCheckUtc = evaluatedUtc.AddMinutes(1) }, false, false);
        // A near-stop poll never postpones detection of a breach already observed.
        bool breached = state.StopPrice.HasValue && bid <= state.StopPrice;
        bool near = state.StopPrice.HasValue && bid <= state.StopPrice * (1m + NearStopFraction);
        return new(state with { LastBid = bid, LastBidReceivedUtc = receivedUtc,
            ExitTriggered = state.ExitTriggered || breached,
            NextBidCheckUtc = evaluatedUtc.AddMinutes(near ? 1 : 5) }, state.ExitTriggered || breached, true);
    }

    private static ProgressiveProfitProtectionState Raise(ProgressiveProfitProtectionState state, decimal close, DateTime at)
    {
        decimal? gap = close >= state.AverageCost * 1.05m ? 0.005m :
            close >= state.AverageCost * 1.03m ? 0.0075m : close >= state.AverageCost * 1.015m ? 0.01m : null;
        if (state.TightestGap.HasValue) gap = gap.HasValue ? System.Math.Min(gap.Value, state.TightestGap.Value) : state.TightestGap;
        if (!gap.HasValue || !state.HighestClose.HasValue) return state;
        decimal candidate = decimal.Round(state.HighestClose.Value * (1m - gap.Value), 6, MidpointRounding.AwayFromZero);
        bool raised = !state.StopPrice.HasValue || candidate > state.StopPrice;
        return state with { TightestGap = gap, StopPrice = raised ? candidate : state.StopPrice,
            StopEstablishedUtc = raised ? at : state.StopEstablishedUtc,
            NextBidCheckUtc = raised ? null : state.NextBidCheckUtc };
    }

    public static void RequirePreserved(ProgressiveProfitProtectionState prior, ProgressiveProfitProtectionState? next)
    {
        Validate(prior);
        if (next is null || prior.StopPrice.HasValue && (next.StopPrice is null || next.StopPrice < prior.StopPrice) ||
            prior.HighestClose.HasValue && (next.HighestClose is null || next.HighestClose < prior.HighestClose) ||
            prior.TightestGap.HasValue && (next.TightestGap is null || next.TightestGap > prior.TightestGap) ||
            prior.ExitTriggered && !next.ExitTriggered)
            throw new InvalidOperationException("Earned progressive protection and observed exits cannot be discarded or loosened.");
        Validate(next);
    }

    public static void Validate(ProgressiveProfitProtectionState state)
    {
        ArgumentNullException.ThrowIfNull(state); Price(state.AverageCost);
        if (state.HighestClose.HasValue) Price(state.HighestClose.Value);
        if (state.StopPrice.HasValue) Price(state.StopPrice.Value);
        if (state.RuleVersion != Version || state.TightestGap is not (null or 0.01m or 0.0075m or 0.005m) ||
            state.StopPrice.HasValue != state.StopEstablishedUtc.HasValue)
            throw new ArgumentException("Invalid progressive protection state.");
        foreach (var at in new[] { state.LastCloseEndUtc, state.StopEstablishedUtc, state.LastBidReceivedUtc, state.NextBidCheckUtc,
            state.LastQuoteRequestedUtc, state.LastQuoteReceivedUtc })
            if (at.HasValue) Utc(at.Value);
    }
    private static void Price(decimal value) { if (value <= 0m) throw new ArgumentOutOfRangeException(nameof(value)); }
    private static void Utc(DateTime value) { if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC is required."); }
}
