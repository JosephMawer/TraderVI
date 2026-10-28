#nullable enable
using Core.TMX.Models.Domain;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    [Theory]
    [InlineData("ready")][InlineData("paused")][InlineData("preflight-expired")][InlineData("save-expired")][InlineData("uncertain")]
    public async Task RecoveredMinutePairFlowsThroughRealAccountReservationAndOriginalOrderRecovery(string scenario)
    {
        var f = new Fixture(); UseNormalLive(f); f.Facts.Clear(); f.Cash = 1100;
        f.Strategy = f.Strategy with { Strategy = f.Strategy.Strategy with { Selection = CompleteTradingStrategy.MinuteSelection,
            MinuteEntry = new(f.Strategy.Strategy.ReadLivePolicy(), "SyntheticV1", TimeSpan.FromSeconds(45)) } };
        await f.Workflow().ApproveAsync(f.Approval(ongoing: true));
        await f.Workflow().TickAsync(f.Binding.BindingId);
        f.Now = f.Now.AddMinutes(3).AddSeconds(10);
        f.TransformInput = input => WithMinuteContext(input) with { EntriesPaused = scenario == "paused" };
        f.QuoteRead = input => [new("BBB", input.EvaluatedUtc, input.EvaluatedUtc.AddSeconds(1), 109.3m, 109.29m, 109.31m, "TmxQuoteFieldsV1")];
        if (scenario == "preflight-expired") f.Transport.BeforePrepare = () => f.Now = f.Now.AddSeconds(40);
        if (scenario == "save-expired") f.Store.AfterAppend = e => { if (e == "OrderIntentReserved") f.Now = f.Now.AddSeconds(40); };
        f.Transport.ThrowOnSubmit = scenario == "uncertain";
        var state = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(scenario is "ready" or "uncertain" ? 1 : 0, f.Transport.Sent);
        Assert.NotEmpty(state.LatestDecision!.MinuteEvidence!.Value);
        if (scenario is "ready" or "uncertain" or "save-expired")
        {
            var order = Assert.Single(state.Orders); Assert.NotNull(order.SignalExpiresUtc);
            f.Store.Restart(); f.Store.AfterAppend = null; f.Transport.BeforePrepare = null;
            var sent = f.Transport.Sent;
            var recovered = await f.Workflow().TickAsync(f.Binding.BindingId);
            Assert.Equal(sent, f.Transport.Sent);
            Assert.Equal(order.RequestId, Assert.Single(recovered.Orders).RequestId);
        }
        else Assert.Empty(state.Orders);
    }

    [Fact]
    public async Task MissingMinuteEntryHistoryDoesNotDisableHeldBidProtection()
    {
        var f = new Fixture(); UseNormalLive(f);
        f.Strategy = f.Strategy with { Strategy = f.Strategy.Strategy with { Selection = CompleteTradingStrategy.MinuteSelection,
            MinuteEntry = new(f.Strategy.Strategy.ReadLivePolicy(), "SyntheticV1", TimeSpan.FromSeconds(45)) } };
        await f.Workflow().ApproveAsync(f.Approval(retain: true, ongoing: true));
        await f.Workflow().TickAsync(f.Binding.BindingId);
        f.Now = f.Now.AddMinutes(6);
        f.TransformInput = input => input with { MinuteInputs = [], Bars = ImmutableDictionary<string, ImmutableArray<TradingPriceBar>>.Empty.Add("AAA",
            [new(Guid.NewGuid(), f.Now.UtcDateTime.AddMinutes(-6), f.Now.UtcDateTime.AddMinutes(-1), 5, 11, 11, 11, 11)]) };
        f.QuoteRead = input => [new("AAA", input.EvaluatedUtc, input.EvaluatedUtc.AddSeconds(1), 11, 10.94m, 11, "TmxQuoteFieldsV1")];
        var sale = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal("SELL", Assert.Single(sale.Orders).Side); Assert.Equal(1, f.Transport.Sent);
        Assert.Null(sale.Orders[0].SignalExpiresUtc);
    }

    private static TradingRecommendationInput WithMinuteContext(TradingRecommendationInput input)
    {
        var end = input.EvaluatedUtc.AddTicks(-input.EvaluatedUtc.Ticks % TimeSpan.TicksPerMinute);
        int minutes = (int)(end - input.SessionOpenUtc).TotalMinutes;
        DelphiLiveMinuteEvidenceBook Book(string symbol, decimal rise)
        {
            var batch = new DelphiLiveCollectorBatch(Guid.NewGuid(), "SyntheticV1", symbol, input.SessionOpenUtc, input.SessionCloseUtc, end,
                TimeSpan.FromSeconds(45), end.AddSeconds(5), end.AddSeconds(7), end.AddSeconds(8), Enumerable.Range(0, minutes).Select(n =>
                    new OhlcvBar(input.SessionOpenUtc.AddMinutes(n), 100 + n * rise, 100 + (n + 1) * rise + .01m,
                        100 + n * rise - .01m, 100 + (n + 1) * rise, 100)).ToImmutableArray(), "{}", null);
            return DelphiLiveCollectorBatches.Apply(DelphiLiveCollectorBatches.Empty(batch), batch);
        }
        DelphiLiveTrueRangeRulerMeasurement Ruler(int n) => new(n, input.TradingDate.AddDays(-1), DelphiLiveScalarMeasurement.Available(.02m));
        var setup = new DelphiLiveDailySetupQuality(input.FrozenRunId!.Value, Guid.NewGuid(), input.Assignment.Strategy.Daily.StrategyVersionId,
            .5m, [new(DelphiLiveSourceLens.Continuation, true, true, 1, .5m, "Synthetic", "Synthetic")]);
        return input with { MinuteInputs = [new(Book("BBB", .1m), Book("XIU", 0), new(Ruler(5), Ruler(10), Ruler(14), Ruler(20)), 100, 100, setup)] };
    }
}
