#nullable enable
using Core.Calibration;
using Core.Practice;
using Core.Runtime;
using Core.TMX;
using Core.TMX.Models.Domain;
using Core.Trader;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class SystemShadowProgressiveProtectionTests
{
    [Fact]
    public async Task AllFourShadowVariantsUseBidTouchesAndOneExistingSellOwner()
    {
        var e = new Environment();
        var controller = new SystemShadowController(e);
        foreach (var portfolio in e.Ledger.Portfolios)
        {
            var id = Guid.NewGuid();
            e.Ledger.Positions.Add(new(id, portfolio.PortfolioId, "SYNTH", "Open", 10, 100m, 1000m, 1000m,
                e.Now.AddDays(-1), e.Now.AddDays(-1).Date, 0, 0, 100m, null, 100m, true, 105m, e.Now.AddMinutes(-5), 0m, null, null));
            e.Ledger.ProgressiveCloses[id] = [new(e.Now.AddMinutes(-5), 105m, e.Now.AddMinutes(-3), 100m)];
        }
        e.Bid = 104.475m;
        (await controller.PollProgressiveProtectionAsync()).ShouldBe(4);
        e.Ledger.PendingOrders.Count.ShouldBe(4);
        e.Ledger.PendingOrders.ShouldAllBe(o => o.Side == "Sell" && o.ReasonCode == "TrailingProfit" && o.EarliestFillUtc > o.SignalReceivedUtc);
        (await new SystemShadowController(e).PollProgressiveProtectionAsync()).ShouldBe(0);
        e.Ledger.ProgressiveStates.Values.ShouldAllBe(s => s.ExitTriggered && s.TightestGap == 0.005m && s.StopPrice == 104.475m);
    }

    [Fact]
    public async Task AddOnKeepsEarnedProtectionAndPollingDoesNotCollectBars()
    {
        var e = new Environment();
        var p = e.Ledger.Portfolios[0];
        var id = Guid.NewGuid();
        e.Ledger.Positions.Add(new(id, p.PortfolioId, "SYNTH", "Open", 125, 101m, 12625m, 13000m,
            e.Now.AddDays(-1), e.Now.AddDays(-1).Date, 1, 0, 104m, null, 102m, true, 105m, e.Now.AddMinutes(-5), 0m, null, null));
        e.Ledger.ProgressiveStates[id] = ProgressiveProfitProtection.Carry(100m, 102m, e.Now.AddMinutes(-5), 105m);
        e.Bid = 105m;
        var controller = new SystemShadowController(e);
        await controller.PollProgressiveProtectionAsync();
        var state = e.Ledger.ProgressiveStates[id];
        state.AverageCost.ShouldBe(101m);
        state.StopPrice.ShouldBe(104.475m);
        state.TightestGap.ShouldBe(0.005m);
        int reads = e.QuoteReads;
        await new SystemShadowController(e).PollProgressiveProtectionAsync();
        e.QuoteReads.ShouldBe(reads);
        e.Now = state.NextBidCheckUtc!.Value;
        e.Bid = 104.60m;
        await controller.PollProgressiveProtectionAsync();
        e.Ledger.ProgressiveStates[id].NextBidCheckUtc.ShouldBe(e.Now.AddMinutes(1));
        e.BarCollections.ShouldBe(0);
    }

    private sealed class Environment : ISystemShadowEnvironment
    {
        public DateTime Now = new(2026, 10, 16, 15, 0, 0, DateTimeKind.Utc);
        public decimal? Bid;
        public int QuoteReads, BarCollections;
        public PracticeShadowStore Ledger { get; }
        public Environment() => Ledger = new(() => Now, Guid.NewGuid, Guid.NewGuid(),
            SystemShadowPolicyConfig.Default with { UseProgressiveProfitProtection = true }, Guid.NewGuid());
        public DateTime UtcNow => Now;
        public Guid NewId() => Guid.NewGuid();
        public CodeProvenance Code => throw new NotSupportedException();
        public ISystemShadowStore Store => Ledger;
        public Task<IAsyncDisposable?> AcquireLeaseAsync(CancellationToken ct) => Task.FromResult<IAsyncDisposable?>(null);
        public Task<bool> EntriesPausedAsync() => Task.FromResult(false);
        public Task<IReadOnlyList<OperatorExitRequest>> ExitRequestsAsync() => Task.FromResult<IReadOnlyList<OperatorExitRequest>>([]);
        public Task<SystemShadowRuntimePortfolio> ConfigureAsync(SystemShadowRuntimePortfolio p) => Task.FromResult(p);
        public Task<bool> IsCurrentDailyRunAsync(Guid? id) => Task.FromResult(true);
        public Task<SystemShadowDelphiRun?> GetDailyRunAsync(DateTime day, DateTime cutoff, CancellationToken ct) => Task.FromResult<SystemShadowDelphiRun?>(null);
        public Task<IReadOnlyList<SystemShadowDelphiCandidate>> GetCandidatesAsync(Guid run, string lens, int count, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SystemShadowDelphiCandidate>>([]);
        public Task<TmxIntradayBatch?> CollectAsync(IntradayPollContext context, string symbol, int interval, DateTime start, List<string> warnings, CancellationToken ct)
        { BarCollections++; throw new InvalidOperationException("The protection wake must not collect bars."); }
        public Task<ProgressiveProtectionQuote> ReadProtectionQuoteAsync(string symbol, CancellationToken ct = default)
        { QuoteReads++; var requested = Now; Now = Now.AddSeconds(1); return Task.FromResult(new ProgressiveProtectionQuote(Bid, requested, Now, "SyntheticBidV1")); }
    }
}
