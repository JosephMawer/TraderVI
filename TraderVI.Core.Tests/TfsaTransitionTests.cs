using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public class TfsaTransitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 3, 30, 0, TimeSpan.Zero);
    private static readonly WealthsimpleTfsaAccount Account = new("synthetic-tfsa", "TFSA fixture");
    private static WealthsimpleTfsaSnapshot Snapshot(params WealthsimpleHolding[] holdings) =>
        new(Account, Now, new(500, "CAD"), new(100, "CAD"), new(70, "CAD"), null, null, null, null, null, holdings);
    private static WealthsimpleHolding Holding(string id, string symbol, decimal quantity) =>
        new(symbol, "Fixture", "EQUITY", quantity, new(100, "CAD"), new(90, "CAD"), new(10, "CAD")) { SecurityId = id };
    private static WealthsimplePendingOrders EmptyOrders() => new(Account, Now, []);

    [Theory]
    [InlineData(37, 1000, 10, 30)]
    [InlineData(37, 275, 10, 20)]
    [InlineData(7, 1000, 10, 7)]
    [InlineData(37, 75, 10, 7)]
    [InlineData(37, 9, 10, 0)]
    public void TenSharePreferenceNeverIncreasesRiskOrCash(int risk, decimal cash, decimal price, int expected) =>
        Assert.Equal(expected, WholeShareLimitSizing.BuyQuantity(risk, cash, price));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.5)]
    [InlineData(17.25)]
    public void BothOrderSidesRejectNonpositiveAndFractionalQuantities(decimal quantity)
    {
        foreach (var side in Enum.GetValues<WholeShareOrderSide>())
            Assert.Throws<ArgumentException>(() => new WholeShareLimitOrder(side, quantity, 2.50m));
    }

    [Fact]
    public void ExitCanSellAllSeventeenWholeShares() => Assert.Equal(17, new WholeShareLimitOrder(WholeShareOrderSide.Sell, 17, 2.50m).Quantity);

    [Fact]
    public void OverlapRetainsActualSharesAndNonoverlapRequiresExit()
    {
        var holdings = Snapshot(Holding("same", "FIX", 17), Holding("exit", "EXIT", 7));
        var preview = TfsaTransitionPlanning.Preview(holdings, EmptyOrders(), [new("FIX", "same"), new("NEW", "new")], 200, Now);
        Assert.Empty(preview.Blocks);
        Assert.Equal(70m, preview.AvailablePurchaseCash);
        Assert.Equal(new[] { "Retain", "Exit at transition", "Await entry evaluation" }, preview.Rows.Select(r => r.Action));
        Assert.Equal(17m, preview.Rows[0].ExistingQuantity);
        Assert.Null(preview.Rows[2].ExistingQuantity);
        Assert.Equal(90m, holdings.Holdings[0].BookValue.Amount);
    }

    [Fact]
    public void UnresolvedInstrumentCannotCauseLiquidationOrFalseOverlap()
    {
        var p = TfsaTransitionPlanning.Preview(Snapshot(Holding("actual", "FIX", 20)), EmptyOrders(), [new("FIX", null)], 50, Now);
        Assert.Contains(p.Blocks, b => b.Contains("unique Canadian"));
        Assert.DoesNotContain(p.Rows, r => r.Action is "Retain" or "Exit at transition");
    }

    [Fact]
    public void ReconciledPreviewRequiresSameCurrentFinancialFactsAndUsesMarketHandoverPolicy()
    {
        var snapshot = Snapshot(Holding("same", "FIX", 17), Holding("exit", "EXIT", 7));
        var reconciliation = new BrokerAccountReconciliation("identity", Account.Id, Now, Now, 100, 70, 500, [],
            [new("same", "FIX", 17, 17, 90, 90, Now.AddDays(-1), 0), new("exit", "EXIT", 7, 7, 90, 90, Now.AddDays(-1), 0)], [], []);
        var incoming = new[] { new TfsaIncomingHolding("FIX", "same"), new TfsaIncomingHolding("NEW", "new") };
        var result = TfsaTransitionPlanning.PreviewReconciled(snapshot, EmptyOrders(), incoming, 100, Now, reconciliation);
        Assert.Empty(result.Blocks);
        Assert.Contains("at market", result.Rows.Single(r => r.Action == "Exit at transition").Explanation);
        Assert.Contains("New buys wait", result.Rows.Single(r => r.Action == "Exit at transition").Explanation);
        foreach (var changed in new[] {
            reconciliation with { AccountId = "other" }, reconciliation with { CheckedAt = Now.AddMinutes(-3) },
            reconciliation with { Cash = 99 }, reconciliation with { Holdings = [] },
            reconciliation with { HistoryConflicts = ["Missing historical fill"] } })
            Assert.NotEmpty(TfsaTransitionPlanning.PreviewReconciled(snapshot, EmptyOrders(), incoming, 100, Now, changed).Blocks);
    }

    [Fact]
    public void SameSymbolDifferentBrokerInstrumentDoesNotOverlap()
    {
        var p = TfsaTransitionPlanning.Preview(Snapshot(Holding("actual", "FIX", 20)), EmptyOrders(), [new("FIX", "different")], 50, Now);
        Assert.DoesNotContain(p.Rows, r => r.Action == "Retain");
    }

    [Fact]
    public void FractionsRemainExactAndBlockTransition()
    {
        var p = TfsaTransitionPlanning.Preview(Snapshot(Holding("actual", "FIX", 17.125m)), EmptyOrders(), [new("FIX", "actual")], 50, Now);
        Assert.Single(p.Blocks); Assert.Equal(17.125m, p.Rows[0].ExistingQuantity); Assert.Equal("Resolve quantity", p.Rows[0].Action);
    }

    [Fact]
    public void PendingSellDoesNotIncreaseFundingAndCancellationRemainsPending()
    {
        var orders = new WealthsimplePendingOrders(Account, Now, [new("FIX", "SELL", "CANCEL_PENDING", "LIMIT", 100, new(10, "CAD"), Now)]);
        var p = TfsaTransitionPlanning.Preview(Snapshot(), orders, [], 2000, Now);
        Assert.Equal(70m, p.AvailablePurchaseCash); Assert.Contains(p.Blocks, b => b.Contains("outstanding orders"));
    }

    [Fact]
    public void UnsupportedCurrencyCannotBecomeAnInferredExit()
    {
        var holding = Holding("us", "FIX", 10) with { Value = new(100, "USD"), BookValue = new(90, "USD") };
        var p = TfsaTransitionPlanning.Preview(Snapshot(holding), EmptyOrders(), [], 50, Now);
        Assert.Single(p.Blocks); Assert.Equal("Verify instrument", p.Rows.Single().Action);
    }

    [Fact]
    public void MissingStaleOrWrongAccountOrdersAreNotZeroOrders()
    {
        foreach (var orders in new WealthsimplePendingOrders[] { null, EmptyOrders() with { RetrievedAt = Now.AddMinutes(-3) },
            EmptyOrders() with { Account = new("other", "Other TFSA") } })
            Assert.Contains(TfsaTransitionPlanning.Preview(Snapshot(), orders, [], 50, Now).Blocks, b => b.Contains("not been verified"));
        Assert.Contains(TfsaTransitionPlanning.Preview(Snapshot() with { RetrievedAt = Now.AddSeconds(1) }, EmptyOrders(), [], 50, Now).Blocks, b => b.Contains("Refresh broker"));
    }

    [Fact]
    public void RankingUsesSameLatestDateAndPrefersSupportedChampionOnTie()
    {
        var champion = Portfolio("OperationalChampion", 100, 100);
        var comparison = Portfolio("GateComparison", 100, 100);
        var losing = Portfolio("GateComparison", 100, 99);
        var ranked = TfsaLiveStrategySelection.Rank([losing, comparison, champion]);
        Assert.Equal(champion.PortfolioId, ranked[0].Snapshot.PortfolioId);
        Assert.Equal(0m, ranked[0].Day.Return); Assert.False(ranked[0].Day.IsClosing);
        Assert.False(ranked[1].SupportsCurrentPromotion);
        Assert.Equal(-0.01m, ranked[2].Day.Return);
        Assert.Empty(TfsaLiveStrategySelection.Rank([]));
    }

    [Fact]
    public void ExactNormalComparisonCanBePreviewedWithoutRecommendationPromotion()
    {
        var source = Portfolio("GateComparison", 100, 99);
        var comparison = new PaperComparisonAccount(source.PortfolioId, Guid.NewGuid(), Guid.NewGuid(),
            "DelphiLive", PaperGateProfile.NormalV1, source.UpdatedUtc, Guid.NewGuid(), source.PolicyVersionId, null, null);
        var candidate = TfsaLiveStrategySelection.Rank([source with { ComparisonAccount = comparison }]).Single();
        Assert.True(candidate.SupportsTransitionReview);
        Assert.False(candidate.SupportsCurrentPromotion);
        foreach (var mismatch in new[] {
            comparison with { Profile = PaperGateProfile.PortfolioRulesOnlyV1 },
            comparison with { AccountId = Guid.NewGuid() },
            comparison with { LivePolicyVersionId = Guid.NewGuid() },
            comparison with { StrategyCode = "BreakoutTop3" },
            comparison with { ShadowStrategyVersionId = Guid.NewGuid() },
            comparison with { ShadowSettingsJson = "{}" } })
            Assert.False(TfsaLiveStrategySelection.Rank([source with { ComparisonAccount = mismatch }]).Single().SupportsTransitionReview);
        Assert.False(TfsaLiveStrategySelection.Rank([source]).Single().SupportsTransitionReview);
    }

    [Fact]
    public void BrokerLiveDefinitionsPreserveExactComparisonProfileIdentity()
    {
        var policy = DelphiLivePolicyDefinition.Version1;
        var strategy = TradingStrategyResolutionTests.Strategy() with {
            Family = TradingStrategyFamily.DelphiLive, ShadowPolicy = null, LivePolicy = policy,
            PolicyVersionId = policy.PolicyVersionId, MaximumHoldings = policy.MaximumHoldings,
            Selection = CompleteTradingStrategy.LiveSelection };
        var account = Guid.NewGuid();
        var comparison = new PaperComparisonAccount(account, Guid.NewGuid(), Guid.NewGuid(), "DelphiLive",
            PaperGateProfile.NormalV1, Now.UtcDateTime, strategy.Daily.StrategyVersionId, policy.PolicyVersionId, null, null);
        var reviewed = new BrokerManagedStrategy(strategy, account, comparison.Profile, comparison);
        reviewed.Validate();
        foreach (var mismatch in new[] {
            comparison with { AccountId = Guid.NewGuid() }, comparison with { DailyStrategyVersionId = Guid.NewGuid() },
            comparison with { LivePolicyVersionId = Guid.NewGuid() }, comparison with { StrategyCode = "BreakoutTop3" },
            comparison with { ShadowStrategyVersionId = Guid.NewGuid() }, comparison with { ShadowSettingsJson = "{}" } })
            Assert.Throws<ArgumentException>(() => (reviewed with { Comparison = mismatch }).Validate());
        var bypass = reviewed with { GateProfile = PaperGateProfile.PortfolioRulesOnlyV1,
            Comparison = comparison with { Profile = PaperGateProfile.PortfolioRulesOnlyV1 } };
        bypass.Validate(); // Definition review is available; execution still needs explicit dated source approval.
        Assert.Throws<ArgumentException>(() => (bypass with { Comparison = comparison }).Validate());
        Assert.NotEqual(reviewed.Fingerprint, bypass.Fingerprint);
    }

    [Fact]
    public void NativeDiscoveryFindsOriginalNormalIdentityWithoutApprovingAnUnverifiedPolicy()
    {
        var source = Portfolio("GateComparison", 100, 99);
        var comparison = new PaperComparisonAccount(source.PortfolioId, Guid.NewGuid(), Guid.NewGuid(),
            "DelphiLive", PaperGateProfile.NormalV1, source.UpdatedUtc, Guid.NewGuid(), Guid.NewGuid(), null, null);
        var candidate = TfsaLiveStrategySelection.Rank([source with { ComparisonAccount = comparison }]).Single();
        Assert.True(candidate.HasNormalLiveComparisonIdentity);
        Assert.False(candidate.SupportsTransitionReview); Assert.False(candidate.SupportsCurrentPromotion);
        Assert.False(TfsaLiveStrategySelection.Rank([source with { ComparisonAccount = comparison with {
            Profile = PaperGateProfile.PortfolioRulesOnlyV1 } }]).Single().HasNormalLiveComparisonIdentity);
    }

    [Fact]
    public void AssignedProgressiveSuccessorRetainsOriginalComparisonAndRejectsAnyOtherRuleChange()
    {
        var original = DelphiLivePolicyDefinition.Version1;
        var current = original with { PolicyVersionId = Guid.NewGuid(), UseProgressiveProfitProtection = true };
        var strategy = TradingStrategyResolutionTests.Strategy() with {
            Family = TradingStrategyFamily.DelphiLive, ShadowPolicy = null, LivePolicy = current,
            PolicyVersionId = current.PolicyVersionId, MaximumHoldings = current.MaximumHoldings,
            Selection = CompleteTradingStrategy.LiveSelection };
        var account = Guid.NewGuid();
        var comparison = new PaperComparisonAccount(account, Guid.NewGuid(), Guid.NewGuid(), "DelphiLive",
            PaperGateProfile.NormalV1, Now.UtcDateTime, strategy.Daily.StrategyVersionId, original.PolicyVersionId, null, null);
        var reviewed = new BrokerManagedStrategy(strategy, account, comparison.Profile, comparison);
        Assert.Throws<ArgumentException>(reviewed.Validate);
        var proof = new BrokerLiveComparisonSuccessor(account, Guid.NewGuid(), original);
        reviewed = reviewed with { LiveComparisonSuccessor = proof }; reviewed.Validate();
        Assert.Equal(original.PolicyVersionId, reviewed.Comparison.LivePolicyVersionId);
        var reopened = System.Text.Json.JsonSerializer.Deserialize<BrokerManagedStrategy>(
            System.Text.Json.JsonSerializer.Serialize(reviewed));
        reopened.Validate(); Assert.Equal(reviewed.Fingerprint, reopened.Fingerprint);
        foreach (var bad in new[] { proof with { AccountId = Guid.NewGuid() }, proof with { AssignmentId = Guid.Empty },
            proof with { OriginalPolicy = original with { PolicyVersionId = Guid.NewGuid() } },
            proof with { OriginalPolicy = original with { UseProgressiveProfitProtection = true } } })
            Assert.Throws<ArgumentException>(() => (reviewed with { LiveComparisonSuccessor = bad }).Validate());
        var changed = current with { SelectedRawMoveThreshold = 0.15m };
        Assert.Throws<ArgumentException>(() => (reviewed with { Strategy = strategy with { LivePolicy = changed } }).Validate());
        Assert.NotEqual(reviewed.Fingerprint, (reviewed with { LiveComparisonSuccessor = proof with { AssignmentId = Guid.NewGuid() } }).Fingerprint);
    }

    private static DelphiLivePortfolioSnapshot Portfolio(string role, decimal opening, decimal end)
    {
        var date = new DateOnly(2026, 9, 9); var utc = Now.UtcDateTime.AddHours(-8);
        return DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "OperationalChampion", null, 100, "CAD", date, utc, utc.AddMinutes(-1), "fixture", "fixture")) with {
            Role = role, // Selection also rejects comparison roles when optional comparison metadata is absent.
            Marks = [new(Guid.NewGuid(), date, DelphiLivePortfolioMarkKind.Opening, utc, true, opening, [], "OK"),
                new(Guid.NewGuid(), date, DelphiLivePortfolioMarkKind.Checkpoint, utc.AddMinutes(5), true, end, [], "OK")] };
    }
}
