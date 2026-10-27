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
    public async Task<ImmutableArray<TradingQuote>> ReadAsync(TradingRecommendationInput input, CancellationToken token) =>
        (await ReadDetailedAsync(input, token)).Quotes;

    public async Task<TradingQuoteReadResult> ReadDetailedAsync(TradingRecommendationInput input, CancellationToken token)
    {
        using var source = new TmxDelphiLiveMarketDataSource();
        return await ObserveAsync(input, async (symbol, requested, ct) =>
        {
            var receipt = await source.GetQuoteAsync(new(Guid.NewGuid(), BrokerMarketSymbols.Market(input, symbol),
                input.CurrentHoldings.Any(h=>string.Equals(h.Symbol,symbol,StringComparison.OrdinalIgnoreCase))?"Sell":"Buy",
                1, requested, requested), ct);
            return new TradingQuote(symbol, requested, receipt.ReceivedUtc, receipt.Price, receipt.Bid, receipt.Ask, receipt.SourceContractVersion);
        }, TimeProvider.System, token);
    }

    internal static async Task<TradingQuoteReadResult> ObserveAsync(TradingRecommendationInput input,
        Func<string,DateTime,CancellationToken,Task<TradingQuote>> read, TimeProvider clock, CancellationToken token)
    {
        var result = ImmutableArray.CreateBuilder<TradingQuote>();
        var attempts = ImmutableArray.CreateBuilder<TradingQuoteAttempt>();
        bool live = input.Assignment.Strategy.Family == TradingStrategyFamily.DelphiLive;
        var policy=live ? input.Assignment.Strategy.ReadLivePolicy() : DelphiLivePolicyDefinition.Version1;
        var preliminary=TradingRecommendationEngine.Evaluate(input with {Quotes=[]});
        var readiness = TradingAccountReadinessPolicy.Evaluate(input.Account,input.Assignment.AccountLabel,input.TradingDate,
            input.EvaluatedUtc,input.CurrentLedgerFingerprint,input.EntriesPaused);
        bool canBuy=readiness.CanConsiderNewRisk &&
            !preliminary.Memory.DailyLossHold && !preliminary.Memory.CapitalReviewRequired &&
            input.CurrentDailyStrategyId==input.Assignment.Strategy.Daily.StrategyVersionId && input.FrozenRunId.HasValue &&
            input.ImplementationIdentity==input.Assignment.Strategy.ImplementationIdentity;
        var eligible=canBuy && live?preliminary.Memory.Symbols.Where(s=>s.Value.LastLiveResult?.ConfirmedLiveEligible==true &&
            s.Value.LastLiveBarUtc>=input.SessionOpenUtc && input.EvaluatedUtc-s.Value.LastLiveBarUtc<=policy.BarInterval+policy.CollectionOffset)
            .Select(s=>s.Key):Enumerable.Empty<string>();
        var eligibleSymbols = eligible.ToImmutableArray();
        if (live)
            foreach (var symbol in preliminary.Memory.Symbols.Where(s => s.Value.LastLiveResult?.ConfirmedLiveEligible == true)
                .Select(s => s.Key).Except(eligibleSymbols, StringComparer.OrdinalIgnoreCase))
                attempts.Add(new(symbol, null, null, canBuy ? "NotRequestedSignalNotFresh" : "NotRequestedNewRiskBlocked"));
        // Protect actual holdings first; bound the entire observation phase by the existing policy's quote window.
        var symbols = input.CurrentHoldings.Where(h => !preliminary.Memory.Symbols.TryGetValue(h.Symbol, out var memory) ||
                (memory.ShadowProtection?.Progressive ?? memory.LiveProtection?.Progressive) is not { } progressive ||
                ProgressiveProfitProtection.IsDue(progressive, input.EvaluatedUtc))
            .Select(h => h.Symbol)
            .Concat(eligibleSymbols).Distinct(StringComparer.OrdinalIgnoreCase);
        DateTime deadline=input.EvaluatedUtc+policy.QuoteAttemptWindow;
        if(deadline>input.SessionCloseUtc)deadline=input.SessionCloseUtc;
        foreach (var symbol in symbols)
        {
            DateTime requested = clock.GetUtcNow().UtcDateTime;
            if(requested>=deadline)
            { attempts.Add(new(symbol, null, null, "NotRequestedQuoteWindowEnded")); continue; }
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(deadline-requested);
                var quote = await read(symbol, requested, timeout.Token);
                result.Add(quote);
                attempts.Add(new(symbol, requested, quote.ReceivedUtc, "Returned"));
            }
            catch (Exception error) when (!token.IsCancellationRequested)
            {
                DateTime received = clock.GetUtcNow().UtcDateTime;
                result.Add(new(symbol, requested, received, null, null, null, "Unavailable:" + error.GetType().Name));
                attempts.Add(new(symbol, requested, received, "Failed", error.GetType().Name));
            }
        }
        return new(result.ToImmutable(), attempts.ToImmutable(), preliminary.EntryRiskBlocks ?? readiness.NewRiskBlocks);
    }
}
