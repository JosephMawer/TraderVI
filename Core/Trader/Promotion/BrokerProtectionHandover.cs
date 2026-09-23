#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.Promotion;

/// <summary>A carried protective obligation; its causal boundary is separate from the real purchase date.</summary>
public sealed record BrokerPositionProtection(Guid PositionId, string Owner, decimal? CarriedFloor,
    DateTimeOffset EstablishedAt, TradingSymbolState State)
{
    public DateTime? ThroughUtc { get; init; }
}
public sealed record BrokerProtectionMark(Guid PositionId, decimal Price, DateTimeOffset ObservedAt, string EvidenceHash);

public static class BrokerProtectionHandover
{
    public static ImmutableArray<string> RecoveryBlocks(TradingRecommendationInput input, ImmutableArray<BrokerPositionProtection> protection)
    {
        var blocks = ImmutableArray.CreateBuilder<string>();
        foreach (var saved in protection)
        {
            var holding = input.CurrentHoldings.SingleOrDefault(h => h.PositionId == saved.PositionId);
            if (holding is null) continue;
            foreach (var session in input.PriorProtectionSessions.Where(s => s.CloseUtc > (saved.ThroughUtc ?? saved.EstablishedAt.UtcDateTime)))
            {
                if (session.CloseUtc.Kind != DateTimeKind.Utc || session.CloseUtc >= input.SessionOpenUtc || session.SessionsBeforeCurrent < 1 ||
                    !input.HoldingSessionOrdinals.TryGetValue(holding.PositionId, out int currentOrdinal))
                { blocks.Add("Protection recovery needs reviewed session boundaries and the actual holding age."); continue; }
                if (currentOrdinal - session.SessionsBeforeCurrent >= 2 &&
                    (!input.Bars.TryGetValue(holding.Symbol, out var bars) || !bars.Any(b => Valid(b, input.EvaluatedUtc) &&
                        b.Minutes == 5 && b.StartUtc.AddMinutes(5) == session.CloseUtc)))
                    blocks.Add($"{holding.Symbol}: recover the missed session closing bar before further strategy orders.");
            }
        }
        return blocks.Distinct().ToImmutableArray();
    }

    public static ImmutableArray<BrokerPositionProtection> Transfer(BrokerManagedStrategy incoming,
        ImmutableArray<BrokerPositionCycle> holdings, ImmutableArray<BrokerPositionProtection> prior,
        ImmutableArray<BrokerProtectionMark> marks, DateTimeOffset activation)
    {
        incoming.Validate();
        // Cross-family execution needs a separately qualified adapter. Do not pretend five/fifteen-minute histories are interchangeable.
        if (incoming.Strategy.Family != TradingStrategyFamily.DailyShadow)
            throw new InvalidOperationException("This supervised execution adapter supports the reviewed Daily Shadow family.");
        if (prior.GroupBy(p => p.PositionId).Any(g => g.Count() != 1) || marks.GroupBy(p => p.PositionId).Any(g => g.Count() != 1))
            throw new InvalidOperationException("Protection ownership or price evidence is ambiguous.");
        return holdings.Select(h =>
        {
            var previous = prior.SingleOrDefault(p => p.PositionId == h.PositionId);
            var mark = marks.SingleOrDefault(p => p.PositionId == h.PositionId);
            if (mark is null || mark.Price <= 0 || mark.ObservedAt > activation || activation - mark.ObservedAt > TimeSpan.FromMinutes(2) ||
                !CompleteTradingStrategy.IsHash(mark.EvidenceHash) || h.CostBasis <= 0 || h.Quantity <= 0 ||
                previous is { EstablishedAt: var at } && at > activation)
                throw new InvalidOperationException("Real cost and fresh identified price/protection evidence are required for every retained holding.");
            var oldState = previous?.State;
            if (oldState is not null && oldState.PositionId != h.PositionId)
                throw new InvalidOperationException("Protection belongs to another position cycle.");
            decimal average = h.CostBasis / h.Quantity;
            var trail = oldState?.ShadowProtection is { } saved ? saved with { AverageCost = average }
                : SystemShadowTrailingState.Open(average);
            decimal? floor = new[] { previous?.CarriedFloor, oldState?.LiveProtection?.FloorPrice, trail.TrailingStopPrice }
                .Where(p => p.HasValue).Max();
            if (floor is <= 0) throw new InvalidOperationException("An established protective floor is invalid.");
            // Keep carried levels independently; a fresh quote is not a completed fifteen-minute historical high.
            return new BrokerPositionProtection(h.PositionId, incoming.VersionId.ToString("D"), floor, activation,
                new(h.PositionId, trail, null, null, null, null, null, ExitReason: oldState?.ExitReason));
        }).ToImmutableArray();
    }

    public static (TradingRecommendationMemory Memory, ImmutableArray<BrokerPositionProtection> Protection) Replay(
        TradingRecommendationInput input, ImmutableArray<BrokerPositionProtection> protection)
    {
        var memory = input.PreviousMemory.Symbols.ToBuilder();
        var updated = protection.ToBuilder();
        for (int index = 0; index < protection.Length; index++)
        {
            var saved = protection[index];
            var holding = input.CurrentHoldings.SingleOrDefault(p => p.PositionId == saved.PositionId);
            if (holding is null || !input.Bars.TryGetValue(holding.Symbol, out var bars)) continue;
            var state = memory.TryGetValue(holding.Symbol, out var prior) && prior.PositionId == holding.PositionId ? prior : saved.State;
            var trail = (state.ShadowProtection ?? SystemShadowTrailingState.Open(holding.CostBasis / holding.Quantity))
                with { AverageCost = holding.CostBasis / holding.Quantity };
            DateTime boundary = saved.EstablishedAt.UtcDateTime;
            // Never run pre-addition prices against an average cost changed by a later actual fill.
            var lastBuy = input.ActualFills.Where(f => f.PositionId == holding.PositionId && f.Side == "BUY").Select(f => (DateTime?)f.FillUtc).Max();
            if (lastBuy > boundary) boundary = lastBuy.Value;
            var valid = bars.Where(b => Valid(b, input.EvaluatedUtc) && b.StartUtc.AddMinutes(b.Minutes) > boundary).ToArray();
            foreach (var five in valid.Where(b => b.Minutes == 5 && b.StartUtc.AddMinutes(5) > (saved.ThroughUtc ?? boundary)).OrderBy(b => b.StartUtc))
            {
                var fifteen = valid.Where(b => b.Minutes == 15 && b.StartUtc.AddMinutes(15) <= five.StartUtc.AddMinutes(5) &&
                    b.ReceivedUtc <= input.EvaluatedUtc).OrderBy(b => b.StartUtc).LastOrDefault();
                var decision = SystemShadowProtection.Evaluate(trail, new(five.StartUtc, five.Low, five.Close),
                    fifteen is null ? null : new(fifteen.StartUtc, fifteen.Low, fifteen.Close), boundary, input.Assignment.Strategy.ReadShadowPolicy());
                trail = decision.State;
                string? reason = state.ExitReason;
                if (decision.ExitReason != SystemShadowExitReason.None) reason ??= decision.ExitReason.ToString();
                if (saved.CarriedFloor is { } floor && (five.StartUtc < boundary ? five.Close : five.Low) <= floor)
                    reason ??= "CarriedProtectionFloor";
                var closedSession = input.PriorProtectionSessions.SingleOrDefault(s => s.CloseUtc == five.StartUtc.AddMinutes(5));
                if (closedSession is not null && input.HoldingSessionOrdinals.TryGetValue(holding.PositionId, out int ordinal) &&
                    SystemShadowPolicy.ShouldExitAtSessionTwoClose(five.Close, holding.CostBasis / holding.Quantity,
                        ordinal - closedSession.SessionsBeforeCurrent, input.Assignment.Strategy.ReadShadowPolicy()))
                    reason ??= "SessionTwoUnprofitable";
                state = state with { ShadowProtection = trail, ExitReason = reason };
                updated[index] = saved with { State = state, ThroughUtc = five.StartUtc.AddMinutes(5) };
            }
            memory[holding.Symbol] = state;
        }
        return (input.PreviousMemory with { Symbols = memory.ToImmutable() }, updated.ToImmutable());
    }

    private static bool Valid(TradingPriceBar b, DateTime now) => b.EvidenceId != Guid.Empty &&
        b.StartUtc.Kind == DateTimeKind.Utc && b.ReceivedUtc.Kind == DateTimeKind.Utc && b.Minutes is 5 or 15 &&
        b.ReceivedUtc >= b.StartUtc.AddMinutes(b.Minutes) && b.ReceivedUtc <= now &&
        b.Low > 0 && b.High >= b.Low && b.Open >= b.Low && b.Open <= b.High && b.Close >= b.Low && b.Close <= b.High;
}
