#nullable enable
using Core.WSTrade.Accounts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public partial class BrokerAccountImportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 14, 0, 0, TimeSpan.Zero);
    private static readonly WealthsimpleTfsaAccount Account = new("synthetic-account", "TFSA");
    private static readonly Guid PositionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TradeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static BrokerOrderFact Buy(string id = "order-buy") => new(id, "security-fixture", "FIX", "BUY", "FILLED", "CAD", "EQUITY",
        10, 10, 100, 101, 1, 0, Now.AddDays(-1).AddMinutes(-1), Now.AddDays(-1));
    private static BrokerOrderFact Sell(decimal quantity = 4) => new("order-sell", "security-fixture", "FIX", "SELL", "FILLED", "CAD", "EQUITY",
        quantity, quantity, quantity * 12, quantity * 12 - 1, 1, 0, Now.AddMinutes(-21), Now.AddMinutes(-20));
    private static BrokerAccountReconciliation Observation(decimal quantity = 10, decimal cost = 101, params BrokerOrderFact[] orders)
    {
        var snapshot = new WealthsimpleTfsaSnapshot(Account, Now, new(300, "CAD"), new(199, "CAD"), new(198, "CAD"), null, null, null, null, null,
            quantity == 0 ? [] : [new("FIX", "Fixture", "EQUITY", quantity, new(quantity * 11, "CAD"), new(cost, "CAD"), null) { SecurityId = "security-fixture" }]);
        var history = new BrokerAccountHistory("synthetic-identity", Account.Id, Now, orders.Length == 0 ? [Buy()] : orders, [])
            { ActivityFingerprint = new string('A', 64) };
        return WealthsimpleReconciliation.Compare(history, snapshot, new(Account, Now, []), Now, false);
    }
    private static BrokerImportState State(bool legacy = false) => new(true,
        new("TFSA", new string('B', 64), legacy ? [new(PositionId, "FIX", new(2026, 9, 15), 10, 101, true)] : [],
            legacy ? [new(TradeId, PositionId, "FIX", "BUY", new(2026, 9, 15), Buy().LastFilledAt, 10, 100, 1, 101)] : []), null, null);
    private static BrokerImportPlan Plan(BrokerAccountReconciliation? observation = null, BrokerImportState? state = null) =>
        BrokerAccountImport.Preview(observation ?? Observation(), state ?? State(), Now);
    private static BrokerImportState Linked(BrokerImportPlan plan)
    {
        var binding = new BrokerImportBinding(Guid.NewGuid(), "TFSA", plan.BrokerKey, plan.LegacyFingerprint, Now, "fixture");
        return State(plan.Rows.Any(r => r.LegacyTradeId.HasValue)) with { Binding = binding,
            Latest = new(Guid.NewGuid(), binding.BindingId, "fixture-hash", Now, plan) };
    }

    [Fact] public void EmptyLegacyAccountPreviewsBrokerHistoryWithoutInventingLegacyTrades()
    {
        var plan = Plan(); Assert.True(plan.CanImport, string.Join("; ", plan.Blocks));
        var row = Assert.Single(plan.Rows); Assert.Equal("Add broker order", row.Action); Assert.Null(row.LegacyTradeId);
        Assert.Equal(101, row.Net); Assert.Equal(1, row.Fees); Assert.Equal(10, plan.Observation.Holdings.Single().BrokerQuantity);
    }
    [Fact] public void InitialReviewLinksExistingTradeAndPreservesPositionIdentity()
    {
        var plan = Plan(state: State(true)); Assert.True(plan.CanImport, string.Join("; ", plan.Blocks));
        var row = Assert.Single(plan.Rows); Assert.Equal("Match recorded trade", row.Action);
        Assert.Equal(TradeId, row.LegacyTradeId); Assert.Equal(PositionId, row.LegacyPositionId);
    }
    [Fact] public void RefreshKeepsCumulativeOrdersOnceAndAddsPartialExitWithActualFees()
    {
        var state = Linked(Plan(state: State(true)));
        var plan = Plan(Observation(6, 60.6m, Buy(), Sell()), state);
        Assert.True(plan.CanImport, string.Join("; ", plan.Blocks)); Assert.Equal(2, plan.Rows.Count);
        Assert.Equal("Already saved", plan.Rows.Single(r => r.Side == "BUY").Action);
        Assert.Equal("Add broker order", plan.Rows.Single(r => r.Side == "SELL").Action);
        Assert.Equal(TradeId, plan.Rows.Single(r => r.Side == "BUY").LegacyTradeId);
        Assert.Equal(47, plan.Rows.Single(r => r.Side == "SELL").Net);
        Assert.Equal(60.6m, plan.Observation.Holdings.Single().ReconstructedBookValue);
    }
    [Fact] public void CompletedRoundTripIsRetainedInHistoryWithZeroOpenQuantity()
    {
        var plan = Plan(Observation(0, 0, Buy(), Sell(10)));
        Assert.True(plan.CanImport); Assert.Equal(2, plan.Rows.Count);
        Assert.Equal(0, plan.Observation.Holdings.Single().BrokerQuantity);
    }
    [Fact] public void PostedHistoryImportsAndAdvancesSavedFillStatusWithoutRewritingPriorFacts()
    {
        var original = Plan(state: State(true)); var state = Linked(original);
        var plan = Plan(Observation(10, 101, Buy() with { Status = "POSTED" }), state);
        Assert.True(plan.CanImport, string.Join("; ", plan.Blocks));
        Assert.Equal("Already saved", Assert.Single(plan.Rows).Action); Assert.Equal(TradeId, plan.Rows[0].LegacyTradeId);
        Assert.Equal("POSTED", plan.Observation.Orders[0].Status); Assert.Equal("FILLED", original.Observation.Orders[0].Status);
        Assert.True(Plan(Observation(10, 101, Buy() with { Status = "POSTED" }), State(true)).CanImport);
        Assert.False(Plan(Observation(), Linked(plan)).CanImport);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void PostedStatusDoesNotPermitImportedOrderAmountOrTimestampCorrections(bool amount)
    {
        var state = Linked(Plan()); var order = Buy() with { Status = "POSTED" };
        order = amount ? order with { GrossValue = 101, NetValue = 102 } : order with { LastFilledAt = order.LastFilledAt!.Value.AddSeconds(1) };
        var observation = Observation(10, amount ? 102 : 101, order);
        Assert.False(Plan(observation, state).CanImport);
    }
    [Fact] public void CancelledPartlyFilledOrderUsesOnlyFilledQuantity()
    {
        var order = Buy() with { Status = "CANCELLED", FilledQuantity = 5, GrossValue = 50, NetValue = 51 };
        var plan = Plan(Observation(5, 51, order)); Assert.True(plan.CanImport); Assert.Equal(5, plan.Rows.Single().Quantity);
    }
    [Fact] public void UnfilledTerminalOrderRemainsAnOrderWithoutCreatingShares()
    {
        var order = Buy() with { Status = "REJECTED", FilledQuantity = 0, GrossValue = null, NetValue = null, Fees = null, Tax = null, LastFilledAt = null };
        var plan = Plan(Observation(0, 0, order)); Assert.True(plan.CanImport); Assert.Equal(0, plan.Rows.Single().Quantity); Assert.Empty(plan.Observation.Holdings);
    }
    [Fact] public void EmptyCashOnlyAccountCanBeLinked()
    {
        var plan = Plan(Observation() with { Orders = [], Holdings = [] }); Assert.True(plan.CanImport); Assert.Empty(plan.Rows);
    }
    [Theory] [InlineData("EXPIRED")] [InlineData("CANCELLED")] [InlineData("CANCELED")]
    public void EmptyTerminalOrderIsSavedAsHistoryWithoutCreatingOrMatchingATrade(string status)
    {
        var empty = Sell() with { FilledQuantity = 0, Status = status, GrossValue = null, NetValue = null,
            Fees = null, Tax = null, LastFilledAt = null, FillQuantityWasNull = true };
        var plan = Plan(Observation(10, 101, Buy(), empty), State(true));
        Assert.True(plan.CanImport, string.Join("; ", plan.Blocks));
        var row = plan.Rows.Single(r => r.Side == "SELL");
        Assert.Equal(0, row.Quantity); Assert.Null(row.LegacyTradeId); Assert.Null(row.Net);
        Assert.True(plan.Observation.Orders.Single(o => o.Side == "SELL").FillQuantityWasNull);
        Assert.Equal(10, plan.Observation.Holdings.Single().ReconstructedQuantity);
        var restored = JsonSerializer.Deserialize<BrokerImportPlan>(JsonSerializer.Serialize(plan))!;
        Assert.Equal(plan.Fingerprint, restored.Fingerprint);
        Assert.True(Plan(restored.Observation, Linked(plan)).CanImport);
        Assert.DoesNotContain("FillQuantityWasNull", JsonSerializer.Serialize(Buy()));
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void IncompleteOrStaleAccountObservationsCannotBeSaved(int change)
    {
        var o = Observation(); o = change switch
        {
            0 => o with { SnapshotAt = Now.AddMinutes(-3) }, 1 => o with { CheckedAt = Now.AddSeconds(1) },
            2 => o with { Cash = null }, 3 => o with { BuyingPower = -1 }, 4 => o with { AccountValue = null },
            5 => o with { ActivityFingerprint = null }, 6 => o with { Blocks = ["Pending order"] },
            _ => o with { HistoryConflicts = ["Prior correction"] }
        };
        Assert.False(Plan(o).CanImport);
    }
    [Fact] public void SchemaAbsenceAllowsPreviewButPreventsSave() => Assert.False(Plan(state: State() with { Installed = false }).CanImport);
    [Theory] [InlineData(true)] [InlineData(false)]
    public void ALinkedAccountCannotBeRetargetedToAnotherIdentityOrAccount(bool identity)
    {
        var state = Linked(Plan()); var o = identity ? Observation() with { IdentityId = "different" } : Observation() with { AccountId = "different" };
        Assert.False(Plan(o, state).CanImport);
    }
    [Fact] public void ChangedLegacyLedgerAfterLinkingBlocksUpdates()
    {
        var state = Linked(Plan()); state = state with { Legacy = state.Legacy with { Fingerprint = new string('C', 64) } };
        Assert.False(Plan(state: state).CanImport);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ChangedOrMissingPreviouslyImportedOrderNeverReplacesSavedFacts(int change)
    {
        var state = Linked(Plan()); var o = Observation();
        o = change switch { 0 => o with { Orders = [], Holdings = [] },
            1 => o with { Orders = [Buy() with { Status = "CANCELLED" }] },
            _ => o with { Orders = [Buy() with { GrossValue = 101, NetValue = 102 }] } };
        Assert.False(Plan(o, state).CanImport); Assert.Equal(Buy(), state.Latest!.Plan.Observation.Orders.Single());
    }
    [Fact] public void EarlierSnapshotCannotReplaceNewerImport()
    {
        var state = Linked(Plan()); Assert.False(Plan(Observation() with { SnapshotAt = Now.AddSeconds(-1) }, state).CanImport);
    }
    [Fact] public void AmbiguousLegacyMatchIsBlockedInsteadOfSelectingFirstOrder()
    {
        var another = Buy("order-another") with { CreatedAt = Buy().CreatedAt.AddMinutes(2), LastFilledAt = Buy().LastFilledAt!.Value.AddMinutes(2) };
        var state = State(true); state = state with { Legacy = state.Legacy with { Trades = [state.Legacy.Trades.Single() with { FillAt = null }] } };
        var plan = Plan(Observation(20, 202, Buy(), another), state);
        Assert.False(plan.CanImport); Assert.All(plan.Rows, row => Assert.Null(row.LegacyTradeId));
    }
    [Fact] public void TwoLegacyTradesCannotBothClaimOneBrokerOrder()
    {
        var state = State(true); state = state with { Legacy = state.Legacy with { Trades = [state.Legacy.Trades[0], state.Legacy.Trades[0] with { TradeId = Guid.NewGuid() }] } };
        Assert.False(Plan(state: state).CanImport);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void FinancialDateOrIdentityMismatchDoesNotSilentlyMatchLegacyHistory(int change)
    {
        var state = State(true); var trade = state.Legacy.Trades[0];
        trade = change switch { 0 => trade with { Net = 100 }, 1 => trade with { Fees = null }, 2 => trade with { Symbol = "FIX.TO" },
            3 => trade with { TradingDate = new(2026, 9, 14) }, _ => trade with { FillAt = Now } };
        state = state with { Legacy = state.Legacy with { Trades = [trade] } }; Assert.False(Plan(state: state).CanImport);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void InconsistentLegacyPositionCannotBeLinked(bool quantity)
    {
        var state = State(true); var p = state.Legacy.Positions[0];
        state = state with { Legacy = state.Legacy with { Positions = [quantity ? p with { Quantity = 11 } : p with { CostBasis = 99 }] } };
        Assert.False(Plan(state: state).CanImport);
    }
    [Fact] public void MissingPositionHistoryBlocksImport()
    {
        var state = State(true); state = state with { Legacy = state.Legacy with { Trades = [] } }; Assert.False(Plan(state: state).CanImport);
    }
    [Fact] public void SameFactsAtLaterReadKeepReviewFingerprintAndSerializationIdentity()
    {
        var plan = Plan(); var o = Observation() with { CheckedAt = Now.AddSeconds(10), SnapshotAt = Now.AddSeconds(10) };
        var next = BrokerAccountImport.Preview(o, State(), Now.AddSeconds(10)); Assert.Equal(plan.Fingerprint, next.Fingerprint);
        Assert.Equal(plan.Fingerprint, JsonSerializer.Deserialize<BrokerImportPlan>(JsonSerializer.Serialize(plan))!.Fingerprint);
    }
    [Fact] public void LegacyClosedPositionMatchesWithoutChangingItsOriginalStoredQuantity()
    {
        var state = State(true); var sale = Sell(10);
        state = state with { Legacy = state.Legacy with { Positions = [state.Legacy.Positions[0] with { IsActive = false }],
            Trades = [state.Legacy.Trades[0], new(Guid.NewGuid(), PositionId, "FIX", "SELL", new(2026, 9, 16), sale.LastFilledAt, 10, 120, 1, 119)] } };
        var plan = Plan(Observation(0, 0, Buy(), sale), state);
        Assert.True(plan.CanImport, string.Join("; ", plan.Blocks)); Assert.Equal(2, plan.Rows.Count(r => r.LegacyPositionId == PositionId));
        Assert.Equal(10, state.Legacy.Positions[0].Quantity);
    }
    [Fact] public void LegacyPositionCannotSilentlyCombineClosedAndReopenedCycles()
    {
        var state = State(true); var sale = Sell(10); var rebuy = Buy("order-rebuy") with { CreatedAt = Now.AddMinutes(-11), LastFilledAt = Now.AddMinutes(-10) };
        state = state with { Legacy = state.Legacy with { Trades = [state.Legacy.Trades[0],
            new(Guid.NewGuid(), PositionId, "FIX", "SELL", new(2026, 9, 16), sale.LastFilledAt, 10, 120, 1, 119),
            new(Guid.NewGuid(), PositionId, "FIX", "BUY", new(2026, 9, 16), rebuy.LastFilledAt, 10, 100, 1, 101)] } };
        Assert.False(Plan(Observation(10, 101, Buy(), sale, rebuy), state).CanImport);
    }
    [Fact] public void ReviewedLegacyMatchSurvivesANewSimilarOrder()
    {
        var initial = State(true); initial = initial with { Legacy = initial.Legacy with { Trades = [initial.Legacy.Trades[0] with { FillAt = null }] } };
        var first = Plan(state: initial); var state = Linked(first) with { Legacy = initial.Legacy };
        var another = Buy("order-another") with { CreatedAt = Buy().CreatedAt.AddMinutes(2), LastFilledAt = Buy().LastFilledAt!.Value.AddMinutes(2) };
        var plan = Plan(Observation(20, 202, Buy(), another), state);
        Assert.True(plan.CanImport, string.Join("; ", plan.Blocks));
        Assert.Equal(TradeId, plan.Rows.Single(r => r.ExternalId == Buy().ExternalId).LegacyTradeId);
        Assert.Null(plan.Rows.Single(r => r.ExternalId == another.ExternalId).LegacyTradeId);
    }
    [Fact] public void TamperingWithReviewedRowsFailsCommitPreparation()
    {
        var plan = Plan(); var request = new BrokerImportRequest(Guid.NewGuid(), plan.Fingerprint, "fixture", plan with { Rows = [] });
        Assert.Throws<InvalidOperationException>(() => BrokerAccountImport.Prepare(request, State(), Now));
    }
    [Fact] public void DuplicateOrdersFailBeforeSaving() => Assert.Throws<InvalidOperationException>(() => Plan(Observation() with { Orders = [Buy(), Buy()] }));
    [Fact] public void ClaimedMatchingHoldingsAreIndependentlyCheckedAgainstOrders()
    {
        var o = Observation(); o = o with { Holdings = [o.Holdings[0] with { BrokerQuantity = 20, ReconstructedQuantity = 20 }] };
        Assert.True(o.HoldingsMatch); Assert.False(Plan(o).CanImport);
    }
    [Fact] public void OverlappingOrdersCannotBypassMissingIndividualFillEvidence()
    {
        var o = Observation(20, 202, Buy(), Buy("order-two")); o = o with { Blocks = [] };
        Assert.False(Plan(o).CanImport);
    }
    [Fact] public async Task WorkflowRefreshesBeforeSaveAndRecoversLostResponseWithoutAnotherBrokerRead()
    {
        var store = new Store(); var workflow = new BrokerImportWorkflow(store, new Clock()); var reviewed = await workflow.PreviewAsync(Observation());
        Guid id = Guid.NewGuid(); int reads = 0;
        var saved = await workflow.ApplyAsync(reviewed, id, "fixture", _ => { reads++; return Task.FromResult(Observation()); });
        var retry = await workflow.ApplyAsync(reviewed, id, "fixture", _ => throw new Exception("Broker is offline"));
        Assert.Equal(saved, retry); Assert.Equal(1, reads); Assert.Single(store.Receipts);
    }
    [Fact] public async Task WorkflowRejectsChangedBrokerFactsAfterReview()
    {
        var store = new Store(); var workflow = new BrokerImportWorkflow(store, new Clock()); var reviewed = await workflow.PreviewAsync(Observation());
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyAsync(reviewed, Guid.NewGuid(), "fixture", _ => Task.FromResult(Observation() with { Cash = 198 })));
        Assert.Empty(store.Receipts);
    }
    [Fact] public async Task ConcurrentSaveInvalidatesOtherReviewedPlan()
    {
        var store = new Store(); var plan = Plan(); var first = new BrokerImportRequest(Guid.NewGuid(), plan.Fingerprint, "fixture", plan);
        await store.SaveAsync(first, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(first with { RequestId = Guid.NewGuid() }, default));
        Assert.Single(store.Receipts);
    }
    [Fact] public async Task LedgerChangeBetweenPreviewAndTransactionIsRejected()
    {
        var store = new Store(); var plan = Plan(); store.State = store.State with { Legacy = store.State.Legacy with { Fingerprint = new string('D', 64) } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(new(Guid.NewGuid(), plan.Fingerprint, "fixture", plan), default)); Assert.Empty(store.Receipts);
    }
    [Fact] public async Task RequestIdentityCannotBeReusedWithDifferentReviewer()
    {
        var store = new Store(); var workflow = new BrokerImportWorkflow(store, new Clock()); var reviewed = Plan(); var id = Guid.NewGuid();
        await workflow.ApplyAsync(reviewed, id, "fixture", _ => Task.FromResult(Observation()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyAsync(reviewed, id, "someone else", _ => Task.FromResult(Observation())));
    }
    [Fact] public async Task CancelledRefreshDoesNotSavePartialObservation()
    {
        var store = new Store(); var workflow = new BrokerImportWorkflow(store, new Clock());
        await Assert.ThrowsAsync<OperationCanceledException>(() => workflow.ApplyAsync(Plan(), Guid.NewGuid(), "fixture", _ => throw new OperationCanceledException()));
        Assert.Empty(store.Receipts); Assert.Null(store.State.Binding);
    }
    [Fact] public async Task UnchangedRefreshCreatesAnObservationWithoutDuplicatingOrderRows()
    {
        var store = new Store(); var workflow = new BrokerImportWorkflow(store, new Clock());
        await workflow.ApplyAsync(Plan(), Guid.NewGuid(), "fixture", _ => Task.FromResult(Observation()));
        var next = await workflow.PreviewAsync(Observation());
        Assert.Equal("Already saved", Assert.Single(next.Rows).Action);
        await workflow.ApplyAsync(next, Guid.NewGuid(), "fixture", _ => Task.FromResult(Observation()));
        Assert.Equal(2, store.Receipts.Count); Assert.Single(store.State.Latest!.Plan.Observation.Orders);
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Store : IBrokerImportStore
    {
        public BrokerImportState State = BrokerAccountImportTests.State();
        public readonly Dictionary<Guid, BrokerImportReceipt> Receipts = [];
        public Task<BrokerImportState> ReadAsync(CancellationToken ct) => Task.FromResult(State);
        public Task<BrokerImportReceipt?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Receipts.GetValueOrDefault(id));
        public Task<BrokerImportReceipt> SaveAsync(BrokerImportRequest request, CancellationToken ct)
        {
            var plan = BrokerAccountImport.Prepare(request, State, Now); ct.ThrowIfCancellationRequested();
            var binding = State.Binding ?? new(Guid.NewGuid(), "TFSA", plan.BrokerKey, plan.LegacyFingerprint, Now, request.ReviewedBy);
            var receipt = new BrokerImportReceipt(request.RequestId, binding.BindingId, request.RequestHash, Now, plan);
            Receipts.Add(request.RequestId, receipt); State = State with { Binding = binding, Latest = receipt }; return Task.FromResult(receipt);
        }
    }
}
