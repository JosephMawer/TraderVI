#nullable enable
using Core.Runtime;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader.Promotion;

/// <summary>Real position protection, independent of the selected paper account's holdings and funds.</summary>
public static class BrokerLiveProtection
{
    public static BrokerPositionProtection Transfer(BrokerManagedStrategy strategy, BrokerPositionCycle holding,
        BrokerPositionProtection? previous, DateTimeOffset activation)
    {
        strategy.Validate();
        if (holding.Quantity <= 0 || holding.CostBasis <= 0 ||
            previous is not null && (previous.PositionId != holding.PositionId || previous.State.PositionId != holding.PositionId))
            throw new InvalidOperationException("Live handover requires the actual position's quantity, cost and protection identity.");
        var policy = strategy.Strategy.ReadLivePolicy();
        decimal average = holding.CostBasis / holding.Quantity;
        var old = previous?.State;
        var live = old?.LiveProtection ?? DelphiLiveProfitProtectionState.Open(holding.PositionId, average);
        var progressive = live.Progressive ?? old?.ShadowProtection?.Progressive;
        if (old?.LiveProtection is null && progressive is { StopPrice: not null, TightestGap: null })
            throw new InvalidOperationException("Review cross-family carried protection before transferring an unclassified progressive floor.");
        decimal? carried = new[] { previous?.CarriedFloor, live.FloorPrice,
            old?.ShadowProtection?.TrailingStopPrice, progressive?.StopPrice }.Where(p => p.HasValue).Max();
        if (carried is <= 0) throw new InvalidOperationException("An established protective floor is invalid.");
        live = live with { AveragePurchasePrice = average };
        if (progressive is not null)
            live = live with { Progressive = ProgressiveProfitProtection.Preserve(progressive with { AverageCost = average },
                carried, activation.UtcDateTime) };
        // The carried floor stays an independent obligation. A Shadow fifteen-minute high or a
        // current quote cannot manufacture a Live five-minute high or a new break-even stage.
        if (policy.UseProgressiveProfitProtection || live.Progressive is not null)
            live = EngineStrategyReassignment.Rebase(live, policy, activation.UtcDateTime);
        return new(holding.PositionId, strategy.VersionId.ToString("D"), carried, activation,
            new(holding.PositionId, null, live, null, null, null, null, ExitReason: old?.ExitReason));
    }

    public static ImmutableArray<string> RecoveryBlocks(TradingRecommendationInput input,
        ImmutableArray<BrokerPositionProtection> protection)
    {
        // Live has no Daily Shadow Session-2 exit. Replay only actual, identified Live evidence;
        // never apply a fifteen-minute policy or infer a bid-floor touch from a bar low.
        var blocks = ImmutableArray.CreateBuilder<string>();
        foreach (var saved in protection.Where(p => input.CurrentHoldings.Any(h => h.PositionId == p.PositionId)))
            if (saved.State.LiveProtection is null || saved.State.PositionId != saved.PositionId)
                blocks.Add("Recover this real position's Delphi Live protection before further strategy orders.");
        return blocks.ToImmutable();
    }

    public static (TradingRecommendationMemory Memory, ImmutableArray<BrokerPositionProtection> Protection) Replay(
        TradingRecommendationInput input, ImmutableArray<BrokerPositionProtection> protection)
    {
        var memory = input.PreviousMemory.Symbols.ToBuilder();
        var updated = protection.ToBuilder();
        var policy = input.Assignment.Strategy.ReadLivePolicy();
        for (int index = 0; index < protection.Length; index++)
        {
            var saved = protection[index];
            var holding = input.CurrentHoldings.SingleOrDefault(h => h.PositionId == saved.PositionId);
            if (holding is null) continue;
            var state = memory.TryGetValue(holding.Symbol, out var cached) && cached.PositionId == holding.PositionId
                ? cached : saved.State;
            var live = (state.LiveProtection ?? saved.State.LiveProtection
                ?? throw new InvalidOperationException("The real holding has no saved Live protection."))
                with { AveragePurchasePrice = holding.CostBasis / holding.Quantity };
            DateTime boundary = saved.EstablishedAt.UtcDateTime;
            var lastBuy = input.ActualFills.Where(f => f.PositionId == holding.PositionId && f.Side == "BUY")
                .Select(f => (DateTime?)f.FillUtc).Max();
            if (lastBuy > boundary) boundary = lastBuy.Value;
            DateTime through = saved.ThroughUtc ?? boundary;
            var bars = input.Bars.GetValueOrDefault(holding.Symbol);
            foreach (var bar in (bars.IsDefault ? [] : bars).Where(b => b.Minutes == 5 && b.EvidenceId != Guid.Empty &&
                b.StartUtc.Kind == DateTimeKind.Utc && b.ReceivedUtc.Kind == DateTimeKind.Utc &&
                b.StartUtc >= boundary && b.StartUtc.AddMinutes(5) > through &&
                b.ReceivedUtc >= b.StartUtc.AddMinutes(5) && b.ReceivedUtc <= input.EvaluatedUtc &&
                b.Low > 0 && b.High >= b.Low && b.Open >= b.Low && b.Open <= b.High && b.Close >= b.Low && b.Close <= b.High)
                .OrderBy(b => b.StartUtc))
            {
                live = DelphiLiveSafetyPolicy.ApplyCompletedClose(live, bar.StartUtc.AddMinutes(5), bar.Close,
                    input.EvaluatedUtc, policy).State;
                // Fast downside is a completed-bar rule; hard-loss/profit-floor triggers require
                // an actual observed bid and cannot be recovered from a bar low.
                if (bar.Close / bar.Open - 1m <= policy.FastDownsideReturnFloor)
                    state = state with { ExitReason = state.ExitReason ?? DelphiLiveExitRule.FastDownside10Pct.ToString() };
                through = bar.StartUtc.AddMinutes(5);
            }
            var quote = input.Quotes.LastOrDefault(q => q.Symbol.Equals(holding.Symbol, StringComparison.OrdinalIgnoreCase) &&
                (q.SourceContract == "TmxQuoteFieldsV1" || q.SourceContract == BrokerHoldingQuote.Contract) && q.Bid is > 0 && q.RequestedUtc >= input.EvaluatedUtc &&
                q.ReceivedUtc >= q.RequestedUtc && q.ReceivedUtc <= input.EvaluatedUtc + policy.QuoteAttemptWindow &&
                q.ReceivedUtc > saved.EstablishedAt.UtcDateTime && q.ReceivedUtc < input.SessionCloseUtc);
            if (saved.CarriedFloor is { } floor && quote?.Bid <= floor)
                state = state with { ExitReason = state.ExitReason ?? "CarriedProtectionFloor" };
            state = state with { PositionId = holding.PositionId, ShadowProtection = null, LiveProtection = live };
            updated[index] = saved with { State = state, ThroughUtc = through };
            memory[holding.Symbol] = state;
        }
        return (input.PreviousMemory with { Symbols = memory.ToImmutable() }, updated.ToImmutable());
    }
}
