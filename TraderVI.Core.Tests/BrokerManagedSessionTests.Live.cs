#nullable enable
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Orders;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    internal static void UseNormalLive(Fixture f, bool progressive = true)
    {
        var policy = DelphiLivePolicyDefinition.Version1 with {
            PolicyVersionId = Guid.NewGuid(), UseProgressiveProfitProtection = progressive };
        var strategy = f.Strategy.Strategy with { Family = TradingStrategyFamily.DelphiLive,
            ShadowPolicy = null, LivePolicy = policy, PolicyVersionId = policy.PolicyVersionId,
            Selection = CompleteTradingStrategy.LiveSelection, MaximumHoldings = policy.MaximumHoldings };
        var id = Guid.NewGuid();
        f.Strategy = new(strategy, id, PaperGateProfile.NormalV1, new(id, Guid.NewGuid(), Guid.NewGuid(),
            "DelphiLive", PaperGateProfile.NormalV1, f.Now.AddDays(-1).UtcDateTime,
            strategy.Daily.StrategyVersionId, policy.PolicyVersionId, null, null));
    }

    [Fact]
    public async Task LiveRetainsRealCostsAndSavesRaisedProtectionBeforeReadingTriggerBid()
    {
        var f = new Fixture(); UseNormalLive(f);
        await f.Workflow().ApproveAsync(f.Approval(retain: true, ongoing: true));
        var activated = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(100m, activated.Account.Positions.Single().CostBasis);
        Assert.NotNull(activated.Protection.Single().State.LiveProtection);
        Assert.Null(activated.Protection.Single().State.ShadowProtection);
        Assert.Equal(0, f.Transport.Sent);
        f.Now = f.Now.AddMinutes(6);
        f.TransformInput = input => input with {
            Bars = ImmutableDictionary<string, ImmutableArray<TradingPriceBar>>.Empty.Add("AAA",
                [new(Guid.NewGuid(), f.Now.UtcDateTime.AddMinutes(-6), f.Now.UtcDateTime.AddMinutes(-1), 5, 11, 11, 11, 11)]) };
        f.QuoteRead = input => {
            Assert.Equal("LiveProtectionPrepared", f.Store.State!.Event);
            Assert.Equal(10.945m, f.Store.State.Memory.Symbols["AAA"].LiveProtection!.Progressive!.StopPrice);
            return [new("AAA", input.EvaluatedUtc, input.EvaluatedUtc.AddSeconds(1), 11, 10.94m, 11, "TmxQuoteFieldsV1")];
        };
        var protectedSale = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal("SELL", Assert.Single(protectedSale.Orders).Side);
        Assert.Equal(10, protectedSale.Orders.Single().Quantity);
        Assert.Equal(1, f.Transport.Sent);
        f.Store.Restart();
        await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(1, f.Transport.Sent); // Unknown original order is recovered, never retransmitted.
    }

    [Fact]
    public async Task LiveContinuesTheSameProtectionAcrossVerifiedDaysWithoutShadowSessionTwoExit()
    {
        var f = new Fixture(); UseNormalLive(f);
        await f.Workflow().ApproveAsync(f.Approval(retain: true, ongoing: true));
        var first = await f.Workflow().TickAsync(f.Binding.BindingId);
        AdvanceVerifiedRiskDay(f); f.Store.Restart();
        var second = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(first.SessionId, second.SessionId);
        Assert.Equal(first.Protection.Single().EstablishedAt, second.Protection.Single().EstablishedAt);
        Assert.NotNull(second.Memory.Symbols["AAA"].LiveProtection);
        Assert.Empty(second.Orders);
    }

    [Fact]
    public async Task LiveReconcilesRequiredExitsBeforeActivatingTheRetainedAccount()
    {
        var f = new Fixture(); UseNormalLive(f);
        await f.Workflow().ApproveAsync(f.Approval(ongoing: true));
        var transition = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Null(transition.ActivatedAt);
        var exit = Assert.Single(transition.Orders);
        Assert.Equal("TransitionExit", exit.Kind);
        f.Facts.Add(f.Fact(exit.ExternalId, "AAA", "SELL", 10, 110, f.Now)); f.Cash = 1110;
        var active = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.NotNull(active.ActivatedAt);
        Assert.Empty(active.Account.Positions);
        Assert.All(active.Orders, order => Assert.True(order.Reconciled));
        Assert.Equal(1, f.Transport.Sent);
    }

    [Fact]
    public void LiveTransferPreservesAnIndependentCarriedFloorAndNeverInventsAHigh()
    {
        var f = new Fixture(); UseNormalLive(f, progressive: false);
        var holding = f.Project().Positions.Single();
        var prior = new BrokerPositionProtection(holding.PositionId, "manual", 10.5m, f.Now.AddDays(-1),
            new(holding.PositionId, null, null, null, null, null, null, ExitReason: "PendingProtectiveSale"));
        var transferred = BrokerLiveProtection.Transfer(f.Strategy, holding, prior, f.Now);
        Assert.Equal(10.5m, transferred.CarriedFloor);
        Assert.Null(transferred.State.LiveProtection!.HighestCompletedFiveMinuteClose);
        Assert.Null(transferred.State.LiveProtection.FloorPrice);
        Assert.Equal("PendingProtectiveSale", transferred.State.ExitReason);
        Assert.Equal(10m, transferred.State.LiveProtection.AveragePurchasePrice);
    }

    [Fact]
    public void LiveReplayRetainsFastDownsideThroughAReboundButDoesNotInventBidTouches()
    {
        var f = new Fixture(); UseNormalLive(f, progressive: false);
        var holding = f.Project().Positions.Single();
        var saved = BrokerLiveProtection.Transfer(f.Strategy, holding, null, f.Now.AddHours(-1));
        var input = TradingRecommendationTests.LiveInput();
        input = input with { Assignment = input.Assignment with { Strategy = f.Strategy.Strategy },
            EvaluatedUtc = f.Now.UtcDateTime, CurrentHoldings = f.Project().Account.Holdings,
            PreviousMemory = TradingRecommendationMemory.Empty, ActualFills = [],
            Bars = ImmutableDictionary<string, ImmutableArray<TradingPriceBar>>.Empty.Add("AAA", [
                new(Guid.NewGuid(),f.Now.UtcDateTime.AddMinutes(-15),f.Now.UtcDateTime.AddMinutes(-10),5,10,10,8,8.9m),
                new(Guid.NewGuid(),f.Now.UtcDateTime.AddMinutes(-10),f.Now.UtcDateTime.AddMinutes(-5),5,11,11,8,11)]) };
        var recovered = BrokerLiveProtection.Replay(input, [saved]);
        Assert.Equal("FastDownside10Pct", recovered.Memory.Symbols["AAA"].ExitReason);
        var justLow = input with { Bars = input.Bars.SetItem("AAA", [input.Bars["AAA"][1]]) };
        Assert.Null(BrokerLiveProtection.Replay(justLow, [saved]).Memory.Symbols["AAA"].ExitReason);
    }

    [Fact]
    public void BoundLiveInputsCannotBorrowAnotherPolicyProfileOrPaperOwnership()
    {
        var input = TradingRecommendationTests.LiveInput();
        var source = input.LiveInputs[0].Input;
        var strategy = input.Assignment.Strategy;
        var id = Guid.NewGuid(); var batch = Guid.NewGuid(); var pair = Guid.NewGuid();
        var account = new PaperComparisonAccount(id, batch, pair, "DelphiLive", PaperGateProfile.NormalV1,
            source.BarEndUtc.AddMinutes(-1), strategy.Daily.StrategyVersionId, strategy.PolicyVersionId, null, null);
        var admission = new PaperGateAdmission(id, pair, batch, account.Profile, Guid.NewGuid(),
            source.DailySetup!.DelphiRunId, strategy.Daily.StrategyVersionId, strategy.PolicyVersionId, account.EffectiveUtc);
        source = source with { DailySetup = source.DailySetup with { ComparisonAdmission = admission },
            IsHeld = true, HasPendingBuy = true };
        var clean = BrokerLiveObservationPolicy.Bind(source, 1, strategy, account, false);
        Assert.False(clean.Input.IsHeld); Assert.False(clean.Input.HasPendingBuy);
        Assert.True(clean.Input.ExactPairPersistedOnTime);
        Assert.False(BrokerLiveObservationPolicy.Bind(source, 1, strategy, account, true).Input.ExactPairPersistedOnTime);
        Assert.Throws<InvalidOperationException>(() => BrokerLiveObservationPolicy.Bind(source, 1,
            strategy with { PolicyVersionId = Guid.NewGuid() }, account, false));
        Assert.Throws<InvalidOperationException>(() => BrokerLiveObservationPolicy.Bind(source, 1,
            strategy, account with { Profile = PaperGateProfile.PortfolioRulesOnlyV1 }, false));
        Assert.Throws<InvalidOperationException>(() => BrokerLiveObservationPolicy.Bind(source, 1, strategy, null, false));
    }

    [Fact]
    public void ANoTransferScopeCannotReplaceAnyExistingLaunchQualification()
    {
        var f = new Fixture(); UseNormalLive(f);
        var quote = new ManualBuyQuote(10.01m, 10, f.Now, "OPEN", f.Now.AddHours(-1), f.Now.AddHours(1), f.Now);
        var records = ImmutableArray.CreateBuilder<ManualSellRecord>();
        foreach (string symbol in new[] { "AAA", "BBB" })
        {
            if (symbol == "BBB") f.Facts.Add(f.Fact("second-buy", symbol, "BUY", 10, 100, f.Now.AddDays(-1)));
            var id = Guid.NewGuid(); var fact = f.Fact("order-" + id.ToString("D"), symbol, "SELL", 10, 100, f.Now);
            f.Facts.Add(fact);
            var intent = new ManualSellIntent(id, "fixture-identity", "fixture-account", "security-" + symbol,
                "Fixture TFSA", symbol, "XTSE", 10, f.Now, quote, f.Now);
            var result = new WealthsimpleMarketOrderState("FILLED", 10, 10, new(10, "CAD"), new(100, "CAD"),
                new(100, "CAD"), new(0, "CAD"), new(0, "CAD"), f.Now, f.Now, null, "REGULAR", f.Now);
            records.Add(new(intent, WealthsimpleSubmissionStatus.Acknowledged, [result], f.Now, InventoryVerifiedAt: f.Now));
        }
        f.Cash = 1100;
        var review = f.Approval().Review;
        var scope = new BrokerNoTransferSessionScope(Guid.NewGuid(), f.Binding.BindingId, f.Risk.BaselineId,
            review.Session, f.Now, "offline fixture", f.Project().Import.Plan.Observation) { OpeningSales = records.ToImmutable() };
        Assert.True(scope.OpeningSalesVerified);
        var scoped = review with { NoTransferScope = scope };
        Assert.All(BrokerActivationReview.RequiredChecks, check => Assert.Contains(check, scoped.RequiredQualificationChecks));
        Assert.Contains("opening-native-sale-fills", scoped.RequiredQualificationChecks);
        Assert.Contains("supervised-no-transfer-cash", scoped.RequiredQualificationChecks);
        Assert.Throws<InvalidOperationException>(() => scoped.Validate(f.Project(), f.Now));
        scoped = scoped with { Qualifications = scoped.RequiredQualificationChecks.Select(check => new BrokerQualification(
            check, f.Binding.BindingId, f.Strategy.Fingerprint, f.Now, Fixture.Hash, "Synthetic fixture only")).ToImmutableArray() };
        scoped.Validate(f.Project(), f.Now);
        var packet = new BrokerExecutionQualificationBundle(f.Binding.BindingId, f.Strategy.Fingerprint,
            f.ImplementationIdentity, review.Session, f.Now, f.Now.AddHours(1), "synthetic fixture", scoped.Qualifications, scope);
        var reopened = JsonSerializer.Deserialize<BrokerExecutionQualificationBundle>(JsonSerializer.Serialize(packet))!;
        reopened.Validate(f.Now); Assert.True(reopened.NoTransferScope!.OpeningSalesVerified);
        var first = BrokerFirstExecutionReviewPolicy.Prepare(new(f.Strategy,
            new(f.Strategy.PaperAccountId, "Synthetic Normal", "DelphiLive", "{}", ["Synthetic qualification only"]), f.Now.UtcDateTime, []),
            f.Project(), reopened, f.ImplementationIdentity, f.Now);
        Assert.True(first.CanApprove, string.Join("; ", first.Blocks)); Assert.Equal(11, first.Checks.Length);
        Assert.Equal(1000m, first.PurchaseCeiling); // Actual buying power after both verified sales.
        Assert.Throws<InvalidOperationException>(() => (scoped with { NoTransferScope = scope with {
            CashOnlyAnchor = scope.CashOnlyAnchor with { AccountValue = 1101 } } }).Validate(f.Project(), f.Now));
        var incomplete = scoped with { Qualifications = scoped.Qualifications.Where(q => q.Check != "native-order-lifecycle").ToImmutableArray() };
        Assert.Contains("native-order-lifecycle", Assert.Throws<InvalidOperationException>(() => incomplete.Validate(f.Project(), f.Now)).Message);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void LiveSignalExitSurvivesLaterObservationsAndNextSessionRecovery(bool nextSession)
    {
        var input = TradingRecommendationTests.LiveInput();
        var first = input.LiveInputs[0].Input; var second = input.LiveInputs[1].Input;
        var bar = first.Stock.Bars.Last();
        var shock = new DelphiLiveFiveMinuteBar(bar.ObservationId, bar.Symbol, bar.SessionDate, bar.StartUtc,
            bar.EndUtc, 100, 100, 89, 89, bar.Volume, bar.ReceivedUtc, bar.Provider, bar.SourceContractVersion, bar.Disposition);
        DelphiLiveFiveMinuteSeries WithShock(DelphiLiveFiveMinuteSeries series) => new(series.Symbol, series.SessionDate,
            series.SessionOpenUtc, series.OperationalContinuityStartUtc,
            series.Bars.Select(b => b.EndUtc == shock.EndUtc ? shock : b));
        first = first with { Stock = WithShock(first.Stock) }; second = second with { Stock = WithShock(second.Stock) };
        var holding = new ReportedTradingHolding(Guid.NewGuid(), "ABC", 10, 1000, input.TradingDate);
        input = input with { CurrentHoldings = [holding], EntriesPaused = true, Quotes = [],
            LiveInputs = [new(first, 1), new(second, 1)] };
        if (nextSession)
            input = input with { TradingDate = input.TradingDate.AddDays(1), EvaluatedUtc = input.EvaluatedUtc.AddDays(1),
                SessionOpenUtc = input.SessionOpenUtc.AddDays(1), SessionCloseUtc = input.SessionCloseUtc.AddDays(1) };
        var result = TradingRecommendationEngine.Evaluate(input);
        Assert.Equal("FastDownside10Pct", Assert.Single(result.Recommendations).Reason);
        Assert.Equal("FastDownside10Pct", result.Memory.Symbols["ABC"].ExitReason);
    }
}
