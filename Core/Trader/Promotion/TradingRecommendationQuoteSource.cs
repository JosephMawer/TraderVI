#nullable enable
using Core.Trader.DelphiLive;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.Promotion;

/// <summary>Market observations only. This adapter has no order or fill capability.</summary>
public sealed class TradingRecommendationQuoteSource : ITradingRecommendationQuoteSource
{
    public async Task<ImmutableArray<TradingQuote>> ReadAsync(TradingRecommendationInput input, CancellationToken token)
    {
        var result = ImmutableArray.CreateBuilder<TradingQuote>();
        using var source = new TmxDelphiLiveMarketDataSource();
        bool live = input.Assignment.Strategy.Family == TradingStrategyFamily.DelphiLive;
        var policy=live ? input.Assignment.Strategy.ReadLivePolicy() : DelphiLivePolicyDefinition.Version1;
        var preliminary=TradingRecommendationEngine.Evaluate(input with {Quotes=[]});
        bool canBuy=TradingAccountReadinessPolicy.Evaluate(input.Account,input.Assignment.AccountLabel,input.TradingDate,
            input.EvaluatedUtc,input.CurrentLedgerFingerprint,input.EntriesPaused).CanConsiderNewRisk &&
            !preliminary.Memory.DailyLossHold && !preliminary.Memory.CapitalReviewRequired &&
            input.CurrentDailyStrategyId==input.Assignment.Strategy.Daily.StrategyVersionId && input.FrozenRunId.HasValue &&
            input.ImplementationIdentity==input.Assignment.Strategy.ImplementationIdentity;
        var eligible=canBuy && live?preliminary.Memory.Symbols.Where(s=>s.Value.LastLiveResult?.ConfirmedLiveEligible==true &&
            s.Value.LastLiveBarUtc>=input.SessionOpenUtc && input.EvaluatedUtc-s.Value.LastLiveBarUtc<=policy.BarInterval+policy.CollectionOffset)
            .Select(s=>s.Key):Enumerable.Empty<string>();
        // Protect actual holdings first; bound the entire observation phase by the existing policy's quote window.
        var symbols = input.CurrentHoldings.Where(h => !preliminary.Memory.Symbols.TryGetValue(h.Symbol, out var memory) ||
                (memory.ShadowProtection?.Progressive ?? memory.LiveProtection?.Progressive) is not { } progressive ||
                ProgressiveProfitProtection.IsDue(progressive, input.EvaluatedUtc))
            .Select(h => h.Symbol)
            .Concat(eligible).Distinct(StringComparer.OrdinalIgnoreCase);
        DateTime deadline=input.EvaluatedUtc+policy.QuoteAttemptWindow;
        if(deadline>input.SessionCloseUtc)deadline=input.SessionCloseUtc;
        foreach (var symbol in symbols)
        {
            DateTime requested = DateTime.UtcNow;
            if(requested>=deadline)break;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(deadline-requested);
                var receipt = await source.GetQuoteAsync(new(Guid.NewGuid(), symbol,
                    input.CurrentHoldings.Any(h=>string.Equals(h.Symbol,symbol,StringComparison.OrdinalIgnoreCase))?"Sell":"Buy", 1, requested, requested), timeout.Token);
                result.Add(new(symbol, requested, receipt.ReceivedUtc, receipt.Price, receipt.Bid, receipt.Ask, receipt.SourceContractVersion));
            }
            catch (Exception error) when (!token.IsCancellationRequested)
            { result.Add(new(symbol, requested, DateTime.UtcNow, null, null, null, "Unavailable:" + error.GetType().Name)); }
        }
        return result.ToImmutable();
    }
}
