#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader;

public sealed partial class SystemShadowController
{
    /// <summary>Quote-only wake: never collects bars, sizes entries, or changes original fill timing.</summary>
    public async Task<int> PollProgressiveProtectionAsync(CancellationToken ct = default)
    {
        if (!await pollGate.WaitAsync(0, ct)) return 0;
        try
        {
            if (!await environment.Store.HasProgressiveProtectionSchemaAsync(ct)) return 0;
            DateTime local = PaperTradingMonitor.ToToronto(environment.UtcNow);
            if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ||
                local.TimeOfDay < RegularOpen || local.TimeOfDay >= RegularClose) return 0;
            await using var lease = await environment.AcquireLeaseAsync(ct);
            int signals = 0;
            foreach (var saved in await environment.Store.GetRunnablePortfoliosAsync(ct))
                signals += await ApplyProgressiveProtectionAsync(await environment.ConfigureAsync(saved), ct);
            return signals;
        }
        finally { pollGate.Release(); }
    }

    private async Task<int> ApplyProgressiveProtectionAsync(SystemShadowRuntimePortfolio portfolio, CancellationToken ct)
    {
        var store = environment.Store;
        if (!await store.HasProgressiveProtectionSchemaAsync(ct))
        {
            if (portfolio.Settings.UseProgressiveProfitProtection)
                throw new InvalidOperationException("Progressive protection requires reviewed migration 20261017_043_AddProgressiveProfitProtection.sql.");
            return 0;
        }
        int signals = 0;
        var pending = await store.GetPendingOrdersAsync(portfolio.PortfolioId, ct);
        foreach (var position in (await store.GetPositionsAsync(portfolio.PortfolioId, ct)).Where(p => p.Status == "Open"))
        {
            if (pending.Any(p => p.PositionId == position.PositionId && p.Side == "Sell")) continue;
            var saved = await store.ReadProgressiveProtectionAsync(position.PositionId, ct);
            if (saved is null && !portfolio.Settings.UseProgressiveProfitProtection) continue;
            if (!environment.IsProtectionSession(environment.UtcNow)) continue;
            var state = saved ?? ProgressiveProfitProtection.Carry(position.AverageCost, position.TrailingStopPrice, environment.UtcNow);
            foreach (var close in (await store.ReadProgressiveClosesAsync(position, state.LastCloseEndUtc, environment.UtcNow, ct)).OrderBy(p => p.EndUtc))
            {
                DateTime local = PaperTradingMonitor.ToToronto(close.EndUtc);
                if (close.EndUtc <= position.EntryUtc || close.ReceivedUtc > environment.UtcNow || close.ReceivedUtc < close.EndUtc ||
                    local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || local.TimeOfDay <= RegularOpen || local.TimeOfDay > RegularClose) continue;
                state = ProgressiveProfitProtection.ObserveClose(state, close.AverageCost, close.EndUtc,
                    close.Close, environment.UtcNow, position.TrailingStopPrice);
            }
            // Blended cost may change, but a purchase never resets the high, floor or earned gap.
            state = ProgressiveProfitProtection.Preserve(state with { AverageCost = position.AverageCost }, position.TrailingStopPrice, environment.UtcNow);
            if (state != saved) await store.SaveProgressiveProtectionAsync(position.PositionId, state, ct);
            DateTime now = environment.UtcNow;
            DateTime localNow = PaperTradingMonitor.ToToronto(now);
            if (localNow.TimeOfDay < RegularOpen || localNow.TimeOfDay >= RegularClose) continue;
            if (!state.ExitTriggered && !ProgressiveProfitProtection.IsDue(state, now)) continue;
            if (!state.ExitTriggered)
            {
                ProgressiveProtectionQuote quote;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(ProgressiveProfitProtection.MaximumReceiptAge);
                    quote = await environment.ReadProtectionQuoteAsync(position.Symbol, timeout.Token);
                }
                catch (Exception error) when (!ct.IsCancellationRequested)
                { quote = new(null, now, environment.UtcNow, "Unavailable:" + error.GetType().Name); }
                var decision = ProgressiveProfitProtection.ObserveBid(state,
                    environment.IsProtectionSession(quote.ReceivedUtc) ? quote.Bid : null,
                    quote.RequestedUtc, quote.ReceivedUtc, environment.UtcNow, quote.SourceContract);
                state = decision.State;
                await store.SaveProgressiveProtectionAsync(position.PositionId, state, ct);
            }
            if (state.ExitTriggered && state.LastBidReceivedUtc.HasValue)
            {
                // The existing owner cancels conflicting buys and deduplicates the full-position sell.
                if (await store.TryCreateOrderAsync(portfolio.PortfolioId, null, position.PositionId, null, position.Symbol,
                    "Sell", "RiskExit", state.LastBidReceivedUtc.Value, null, "TrailingProfit", ct,
                    config: portfolio.Settings, strategyVersionId: portfolio.StrategyVersionId)) signals++;
            }
        }
        return signals;
    }
}
