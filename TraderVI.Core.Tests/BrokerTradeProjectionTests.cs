#nullable enable
using Core.Db;
using Core.WSTrade.Accounts;
using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class BrokerTradeProjectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Position = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Trade = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static BrokerOrderFact Buy => new("buy", "security", "FIX", "BUY", "FILLED", "CAD", "EQUITY",
        20, 20, 200, 201, 1, 0, Now.AddDays(-1).AddMinutes(-1), Now.AddDays(-1));
    private static BrokerOrderFact Sell(int shares = 20) => new("sell", "security", "FIX", "SELL", "FILLED", "CAD", "EQUITY",
        shares, shares, shares * 15.08m, shares * 15.08m - 1, 1, 0, Now.AddMinutes(-3), Now.AddMinutes(-2));
    private static BrokerImportLegacyLedger Ledger(bool legacy = true) => new("TFSA", new string('A', 64),
        legacy ? [new(Position, "FIX.TO", new(2026, 9, 23), 20, 201, true)] : [],
        legacy ? [new(Trade, Position, "FIX.TO", "BUY", new(2026, 9, 23), Buy.LastFilledAt, 20, 200, 1, 201)] : []);
    private static BrokerImportPlan Plan(BrokerImportLegacyLedger baseline, params BrokerOrderFact[] orders)
    {
        decimal quantity = 0, cost = 0;
        foreach (var o in orders)
            if (o.Side == "BUY") { quantity += o.FilledQuantity; cost += o.NetValue ?? 0; }
            else { cost -= cost * o.FilledQuantity / quantity; quantity -= o.FilledQuantity; }
        var observation = new BrokerAccountReconciliation("identity", "account", Now, Now, 500, 500, 700, orders,
            [new("security", "FIX", quantity, quantity, cost, cost, Buy.LastFilledAt, 0)], [], [])
            { ActivityFingerprint = new string('B', 64), Instruments = [new("security", "FIX", "XTSE", "CAD", "EQUITY")] };
        return BrokerAccountImport.Preview(observation, new(true, baseline, null, null), Now);
    }
    [Fact] public void FullExitPreservesReviewedIdentitiesAndUsesActualNetProceeds()
    {
        var baseline = Ledger(); var result = BrokerTradeProjector.Build(Plan(baseline, Buy, Sell()), baseline);
        var exit = result.Trades.Single(t => t.Side == "SELL");
        Assert.Equal(Position, exit.PositionId); Assert.Equal(15.08m, exit.Price); Assert.Equal(301.60m, exit.Gross);
        Assert.Equal(300.60m, exit.Net); Assert.Equal(99.60m, exit.RealizedProfit); Assert.Equal(Sell().LastFilledAt, exit.LastFilledAt);
        Assert.Equal(Trade, result.Trades.Single(t => t.Side == "BUY").TradeId);
        Assert.True(result.Trades.Single(t => t.Side == "BUY").Legacy);
        Assert.False(Assert.Single(result.Positions).Active);
    }
    [Fact] public void PartialSaleKeepsPositionAndProportionalBook()
    {
        var baseline = Ledger(); var result = BrokerTradeProjector.Build(Plan(baseline, Buy, Sell(5)), baseline);
        var p = Assert.Single(result.Positions); Assert.Equal(Position, p.PositionId); Assert.True(p.Active);
        Assert.Equal(15, p.Quantity); Assert.Equal(150.75m, p.Cost); Assert.Equal(10.05m, p.EntryPrice);
        Assert.Equal(24.15m, result.Trades.Single(t => t.Side == "SELL").RealizedProfit);
    }
    [Fact] public void AddThenSellMaintainsWeightedAverageCost()
    {
        var baseline = Ledger(); var add = Buy with { ExternalId = "add", CreatedAt = Now.AddMinutes(-10), LastFilledAt = Now.AddMinutes(-9),
            GrossValue = 300, NetValue = 301 };
        var p = Assert.Single(BrokerTradeProjector.Build(Plan(baseline, Buy, add, Sell(5)), baseline).Positions);
        Assert.Equal(Position, p.PositionId); Assert.Equal(35, p.Quantity); Assert.Equal(439.25m, p.Cost);
    }
    [Fact] public void ReopenedHoldingGetsNewIdentityAndDoesNotInheritLegacyProtectionIdentity()
    {
        var baseline = Ledger(); var reopen = Buy with { ExternalId = "reopen", CreatedAt = Now.AddMinutes(-1), LastFilledAt = Now };
        var result = BrokerTradeProjector.Build(Plan(baseline, Buy, Sell(), reopen), baseline);
        Assert.Equal(2, result.Positions.Count); Assert.False(result.Positions.Single(p => p.PositionId == Position).Active);
        Assert.NotEqual(Position, result.Positions.Single(p => p.Active).PositionId);
    }
    [Fact] public void RetryAndSerializationProduceIdenticalOrderAndPositionIdentities()
    {
        var baseline = Ledger(); var plan = Plan(baseline, Buy, Sell());
        var first = BrokerTradeProjector.Build(plan, baseline);
        var restored = BrokerTradeProjector.Build(JsonSerializer.Deserialize<BrokerImportPlan>(JsonSerializer.Serialize(plan))!, baseline);
        Assert.Equal(BrokerAccountImport.Hash(first), BrokerAccountImport.Hash(restored));
    }
    [Fact] public void PostingOnlyStatusChangeLeavesProjectedFactsIdentical()
    {
        var baseline = Ledger();
        Assert.Equal(BrokerAccountImport.Hash(BrokerTradeProjector.Build(Plan(baseline, Buy, Sell()), baseline)),
            BrokerAccountImport.Hash(BrokerTradeProjector.Build(Plan(baseline, Buy with { Status = "POSTED" }, Sell()), baseline)));
    }
    [Fact] public void NewCanadianHoldingGetsVerifiedListingSymbol()
    {
        var baseline = Ledger(false); var result = BrokerTradeProjector.Build(Plan(baseline, Buy), baseline);
        Assert.Equal("FIX.TO", Assert.Single(result.Positions).Symbol); Assert.False(result.Trades[0].Legacy);
    }
    [Fact] public void MissingListingForNewHoldingIsBlocked()
    {
        var baseline = Ledger(false); var plan = Plan(baseline, Buy);
        Assert.Throws<InvalidOperationException>(() => BrokerTradeProjector.Build(plan with { Observation = plan.Observation with { Instruments = null } }, baseline));
    }
    [Theory] [InlineData("PARTIALLY_FILLED")] [InlineData("PENDING")]
    public void UnresolvedOrderCannotBeProjected(string status)
    {
        var baseline = Ledger(); var plan = Plan(baseline, Buy, Sell(5) with { Status = status });
        Assert.Throws<InvalidOperationException>(() => BrokerTradeProjector.Build(plan, baseline));
    }
    [Fact] public void CancelledPartFilledOrderRecordsOnlyFilledShares()
    {
        var baseline = Ledger(); var result = BrokerTradeProjector.Build(Plan(baseline, Buy, Sell(5) with { Status = "CANCELLED", SubmittedQuantity = 20 }), baseline);
        Assert.Equal(5, result.Trades.Single(t => t.Side == "SELL").Quantity); Assert.Equal(15, result.Positions[0].Quantity);
    }
    [Fact] public void WithholdingIsNotSilentlyDroppedFromTradeLog()
    {
        var baseline = Ledger(); var sell = Sell() with { Tax = 1, NetValue = Sell().NetValue - 1 };
        Assert.Throws<InvalidOperationException>(() => BrokerTradeProjector.Build(Plan(baseline, Buy, sell), baseline));
    }
    [Fact] public void AlteredReviewedReferenceIsRejectedEvenWhenFlagsClaimValid()
    {
        var baseline = Ledger(); var plan = Plan(baseline, Buy, Sell());
        Assert.Throws<InvalidOperationException>(() => BrokerTradeProjector.Build(plan with
            { Rows = plan.Rows.Select(r => r with { LegacyTradeId = null, LegacyPositionId = null }).ToArray() }, baseline));
    }
    [Fact] public void ReceiptPermitsOnlyTheExactCommittedLedgerAndOriginalBinding()
    {
        var baseline = Ledger(); var binding = new BrokerImportBinding(Guid.NewGuid(), "TFSA", "broker", baseline.Fingerprint, Now, "fixture");
        var current = baseline with { Fingerprint = new string('C', 64) };
        var receipt = new BrokerTradeSyncReceipt(Guid.NewGuid(), binding.BindingId, null, baseline, current.Fingerprint, new([], []));
        Assert.Equal(baseline, BrokerTradeSyncStorage.ValidateBaseline(binding, current, receipt));
        Assert.Throws<InvalidOperationException>(() => BrokerTradeSyncStorage.ValidateBaseline(binding, baseline, receipt));
        Assert.Throws<InvalidOperationException>(() => BrokerTradeSyncStorage.ValidateBaseline(binding with { BindingId = Guid.NewGuid() }, current, receipt));
    }
}
