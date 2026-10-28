#nullable enable
using Core.Runtime;
using Core.Trader;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>
/// The trading owner already publishes revision-bound RealObservation subscriptions atomically with
/// its financial state. The collector reads that projection, never broker credentials or live accounts.
/// An unavailable daily source cannot remove a verified holding or pending order from the union.
/// </summary>
public sealed class CollectorSubscriptionRepository
{
    public async Task<CollectorSubscriptionSnapshot> ReadAsync(DateOnly date, DelphiLiveSessionBounds bounds,
        CancellationToken ct = default)
    {
        var real = await new EngineRealObservationStore().ReadAsync(ct);
        var candidates = ImmutableArray.CreateBuilder<string>();
        foreach (var item in real)
        {
            if (item.Strategy.Strategy.Family != Core.Trader.Promotion.TradingStrategyFamily.DelphiLive ||
                item.Strategy.Comparison is not { } comparison) continue;
            var source = await new PaperComparisonRepository().ReadSourceAsync(comparison.BatchId, date.ToDateTime(TimeOnly.MinValue), ct);
            if (source is null || source.DailyStrategyVersionId != item.Strategy.Strategy.Daily.StrategyVersionId ||
                source.EffectiveUtc > DateTime.UtcNow || source.AdoptedUtc > DateTime.UtcNow || source.PublishedUtc > DateTime.UtcNow) continue;
            candidates.AddRange(PaperGateComparison.SelectLive(source, comparison.Profile).Keys);
        }
        return FromVerified(bounds, real, candidates.ToImmutable());
    }

    public static CollectorSubscriptionSnapshot FromVerified(DelphiLiveSessionBounds bounds,
        System.Collections.Generic.IReadOnlyList<EngineRealObservation> real, ImmutableArray<string> candidates)
    {
        foreach (var item in real) item.Strategy.Validate();
        var all = real.SelectMany(r => r.Holdings).ToArray();
        var result = new CollectorSubscriptionSnapshot(bounds.OpenUtc, bounds.CloseUtc,
            candidates.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray(),
            all.Where(h => h.Owner != "BrokerPending").Select(h => h.Symbol).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray(),
            all.Where(h => h.Owner == "BrokerPending").Select(h => h.Symbol).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray());
        _ = result.Symbols(); return result;
    }
}
