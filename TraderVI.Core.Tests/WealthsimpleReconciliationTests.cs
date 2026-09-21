using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public class WealthsimpleReconciliationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 13, 0, 0, TimeSpan.Zero);
    private static readonly WealthsimpleTfsaAccount Account = new("account-fixture", "TFSA");
    private static BrokerOrderFact Buy(string id = "buy", decimal quantity = 10, decimal cost = 101, int minutes = -30) =>
        new(id, "security-fixture", "FIX", "BUY", "FILLED", "CAD", "EQUITY", quantity, quantity, cost - 1, cost, 1, 0,
            Now.AddMinutes(minutes - 1), Now.AddMinutes(minutes));
    private static BrokerOrderFact Sell(string id = "sell", decimal quantity = 4, int minutes = -20) =>
        new(id, "security-fixture", "FIX", "SELL", "FILLED", "CAD", "EQUITY", quantity, quantity, 48, 47, 1, 0,
            Now.AddMinutes(minutes - 1), Now.AddMinutes(minutes));
    private static WealthsimpleTfsaSnapshot Snapshot(decimal quantity = 6, decimal cost = 60.6m) => new(Account, Now,
        new(200, "CAD"), new(50, "CAD"), new(49, "CAD"), null, null, null, null, null,
        quantity == 0 ? [] : [new("FIX", "Fixture", "EQUITY", quantity, new(72, "CAD"), new(cost, "CAD"), null) { SecurityId = "security-fixture" }]);
    private static BrokerAccountHistory History(params BrokerOrderFact[] orders) => new("identity-fixture", Account.Id, Now.AddSeconds(-1), orders, []) { ActivityFingerprint = "activity-fixture" };
    private static BrokerAccountReconciliation Compare(BrokerAccountHistory history, WealthsimpleTfsaSnapshot snapshot = null,
        BrokerAccountReconciliation previous = null, bool unresolved = false) => WealthsimpleReconciliation.Compare(history,
            snapshot ?? Snapshot(), new(Account, Now, []), Now, unresolved, previous);

    [Fact]
    public void ReconstructsPartialSalesActualCostsAndEntryHistoryWithoutReplayingCumulativeFills()
    {
        var history = History(Sell(), Buy());
        var result = Compare(history);
        Assert.True(result.HoldingsMatch);
        var row = Assert.Single(result.Holdings);
        Assert.Equal(6, row.ReconstructedQuantity); Assert.Equal(60.6m, row.ReconstructedBookValue);
        Assert.Equal(6.6m, row.RealizedProceedsLessCost); Assert.Equal(Buy().LastFilledAt, row.FirstBuyCompletedAt);
        var again = Compare(history, previous: result);
        Assert.Equal(row, Assert.Single(again.Holdings)); Assert.True(again.HoldingsMatch);
        Assert.Equal(50, result.Cash); Assert.Equal(49, result.BuyingPower);
    }
    [Fact]
    public void PartialThenFinalObservationRebuildsOnlyOnceAndKeepsOriginalEntry()
    {
        var partial = Buy() with { Status = "PARTIALLY_FILLED", FilledQuantity = 5, GrossValue = 50, NetValue = 51 };
        var first = Compare(History(partial), Snapshot(5, 51));
        Assert.False(first.HoldingsMatch);
        var second = Compare(History(Buy()), Snapshot(10, 101), first);
        Assert.True(second.HoldingsMatch); Assert.Equal(10, Assert.Single(second.Holdings).ReconstructedQuantity);
    }
    [Fact]
    public void PostedCompletionPreservesRawStatusAndDoesNotReapplyAnAlreadyFilledOrder()
    {
        var first = Compare(History(Buy(), Sell()));
        var posted = Compare(History(Buy() with { Status = "POSTED" }, Sell() with { Status = "POSTED" }), previous: first);
        Assert.True(posted.HoldingsMatch); Assert.Empty(posted.HistoryConflicts);
        Assert.Equal(first.Holdings, posted.Holdings);
        Assert.All(posted.Orders, o => Assert.Equal("POSTED", o.Status));
        Assert.All(first.Orders, o => Assert.Equal("FILLED", o.Status));
        Assert.False(Compare(History(Buy(), Sell()), previous: posted).HoldingsMatch);
    }
    [Theory]
    [InlineData("quantity")] [InlineData("amount")] [InlineData("timestamp")] [InlineData("identity")]
    public void PostingCannotConcealACorrectionToPreviouslyConfirmedFacts(string change)
    {
        var prior = Compare(History(Buy()), Snapshot(10, 101));
        var order = Buy() with { Status = "POSTED" };
        order = change switch { "quantity" => order with { FilledQuantity = 9 }, "amount" => order with { NetValue = 102 },
            "timestamp" => order with { LastFilledAt = order.LastFilledAt.Value.AddSeconds(1) }, _ => order with { SecurityId = "other" } };
        Assert.NotEmpty(Compare(History(order), Snapshot(10, 101), prior).HistoryConflicts);
    }
    [Theory]
    [InlineData("partial")] [InlineData("empty")] [InlineData("fees")] [InlineData("timestamp")]
    public void PostedOrdersRequireCompleteFullFillFacts(string missing)
    {
        var order = Buy() with { Status = "POSTED" };
        order = missing switch { "partial" => order with { FilledQuantity = 5 }, "empty" => order with { FilledQuantity = 0 },
            "fees" => order with { Fees = null }, _ => order with { LastFilledAt = null } };
        Assert.False(WealthsimpleReconciliation.Valid(order, Now));
        Assert.False(Compare(History(order), Snapshot(10, 101)).HoldingsMatch);
    }
    [Fact]
    public void CancelledPartialFillIsAnActualPositionAndNoFillCancellationCreatesNone()
    {
        var cancelled = Buy() with { Status = "CANCELLED", FilledQuantity = 5, GrossValue = 50, NetValue = 51 };
        Assert.True(Compare(History(cancelled), Snapshot(5, 51)).HoldingsMatch);
        var empty = cancelled with { FilledQuantity = 0, GrossValue = null, NetValue = null, Fees = null, Tax = null, LastFilledAt = null };
        Assert.True(Compare(History(empty), Snapshot(0)).HoldingsMatch);
    }
    [Theory] [InlineData("EXCHANGE_TRADED_FUND")] [InlineData("OPTION")] [InlineData("CRYPTO")]
    public void HistoricalEtfSupportDoesNotMakeUnsupportedCurrentHoldingsReady(string type)
    {
        var snapshot = Snapshot(10, 101);
        snapshot = snapshot with { Holdings = [snapshot.Holdings[0] with { SecurityType = type }] };
        var result = Compare(History(Buy() with { SecurityType = type }), snapshot);
        Assert.False(result.HoldingsMatch);
    }
    [Fact] public void SecurityTypeCannotChangeWithinACompletedRoundTrip()
    {
        var result = Compare(History(Buy(), Sell( quantity: 10) with { SecurityType = "EXCHANGE_TRADED_FUND" }), Snapshot(0));
        Assert.False(result.HoldingsMatch); Assert.Contains(result.Blocks, b => b.Contains("type changed"));
    }
    [Theory]
    [InlineData("EXPIRED")] [InlineData("CANCELLED")] [InlineData("CANCELED")]
    public void EmptyTerminalReportCannotEraseAPreviouslyObservedPartialFill(string status)
    {
        var partial = Buy() with { Status = status, FilledQuantity = 5, GrossValue = 50, NetValue = 51 };
        var first = Compare(History(partial), Snapshot(5, 51));
        var empty = partial with { FilledQuantity = 0, GrossValue = null, NetValue = null, Fees = null, Tax = null,
            LastFilledAt = null, FillQuantityWasNull = true };
        var next = Compare(History(empty), Snapshot(0), first);
        Assert.False(next.HoldingsMatch); Assert.NotEmpty(next.HistoryConflicts);
    }
    [Theory]
    [InlineData("EXPIRED")] [InlineData("CANCELLED")] [InlineData("CANCELED")]
    public void ClaimedNullTerminalProvenanceCannotBypassIncompleteFillValidation(string status)
    {
        var empty = Buy() with { Status = status, FilledQuantity = 0, GrossValue = null, NetValue = null, Fees = null, Tax = null,
            LastFilledAt = null, FillQuantityWasNull = true };
        Assert.True(WealthsimpleReconciliation.Valid(empty, Now));
        Assert.False(WealthsimpleReconciliation.Valid(empty with { Status = "FILLED" }, Now));
        Assert.False(WealthsimpleReconciliation.Valid(empty with { Status = "REJECTED" }, Now));
        Assert.False(WealthsimpleReconciliation.Valid(empty with { Status = "CANCEL_PENDING" }, Now));
        Assert.False(WealthsimpleReconciliation.Valid(empty with { FilledQuantity = 1 }, Now));
        Assert.False(WealthsimpleReconciliation.Valid(empty with { Fees = 1 }, Now));
        Assert.False(WealthsimpleReconciliation.Valid(empty with { LastFilledAt = Now.AddMinutes(-1) }, Now));
        Assert.False(WealthsimpleReconciliation.Valid(empty with { FillQuantityWasNull = false, LastFilledAt = Now.AddMinutes(-1) }, Now));
    }
    [Fact]
    public void CompletedRoundTripRemainsInHistoryAndDoesNotCreateAnOpenPosition()
    {
        var sale = Sell(quantity: 10) with { GrossValue = 120, NetValue = 119 };
        var result = Compare(History(Buy(), sale), Snapshot(0));
        Assert.True(result.HoldingsMatch); Assert.Equal(2, result.Orders.Count);
        var row = Assert.Single(result.Holdings); Assert.Equal(0, row.ReconstructedQuantity);
        Assert.Equal(0, row.ReconstructedBookValue); Assert.Null(row.FirstBuyCompletedAt); Assert.Equal(18, row.RealizedProceedsLessCost);
    }
    [Fact]
    public void ReentryAfterCompleteSaleStartsANewEntryDate()
    {
        var sale = Sell(quantity: 10) with { GrossValue = 120, NetValue = 119 };
        var next = Buy("next", 3, 31, -5);
        var result = Compare(History(Buy(), sale, next), Snapshot(3, 31));
        Assert.True(result.HoldingsMatch); Assert.Equal(next.LastFilledAt, Assert.Single(result.Holdings).FirstBuyCompletedAt);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void HistoricalDisappearanceRegressionCorrectionOrIdentityChangeStaysBlocked(int change)
    {
        var prior = Compare(History(Buy()), Snapshot(10, 101));
        var changed = change switch {
            0 => History(),
            1 => History(Buy() with { FilledQuantity = 9 }),
            2 => History(Buy() with { GrossValue = 101, NetValue = 102 }),
            3 => History(Buy() with { SecurityId = "other" }),
            _ => History(Buy() with { Status = "SUBMITTED" }) };
        var result = Compare(changed, Snapshot(10, 101), prior);
        Assert.NotEmpty(result.HistoryConflicts); Assert.False(result.HoldingsMatch);
        Assert.NotEmpty(Compare(History(Buy()), Snapshot(10, 101), result).HistoryConflicts);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void IncompleteOrInvalidFactsNeverReportAMatch(int change)
    {
        var order = change switch {
            0 => Buy() with { Fees = null }, 1 => Buy() with { NetValue = 100 },
            2 => Buy() with { LastFilledAt = null }, 3 => Buy() with { LastFilledAt = Now.AddMinutes(1) },
            4 => Buy() with { Currency = "USD" }, 5 => Buy() with { FilledQuantity = 10.5m },
            6 => Buy() with { SubmittedQuantity = 0 }, _ => Buy() with { Side = "SHORT" } };
        Assert.False(Compare(History(order), Snapshot(10, 101)).HoldingsMatch);
    }
    [Fact]
    public void FractionalBrokerHoldingsArePreservedAndBlockWholeShareHandover()
    {
        var result = Compare(History(Buy()), Snapshot(10.25m, 101));
        Assert.False(result.HoldingsMatch); Assert.Equal(10.25m, Assert.Single(result.Holdings).BrokerQuantity);
    }
    [Fact]
    public void MissingPurchaseAndOverlappingOrdersCannotBeHiddenByMatchingNetHoldings()
    {
        Assert.False(Compare(History(Sell()), Snapshot(0)).HoldingsMatch);
        var sale = Sell() with { CreatedAt = Buy().CreatedAt };
        var result = Compare(History(Buy(), sale));
        Assert.True(Assert.Single(result.Holdings).Matches); Assert.False(result.HoldingsMatch);
        Assert.Contains(result.Blocks, b => b.Contains("Overlapping"));
    }
    [Fact]
    public void StaleOrCrossAccountObservationsAndLocalUncertaintyBlockHandover()
    {
        Assert.False(Compare(History(Buy(), Sell()) with { RetrievedAt = Now.AddMinutes(-3) }).HoldingsMatch);
        Assert.False(Compare(History(Buy(), Sell()), unresolved: true).HoldingsMatch);
        Assert.Throws<InvalidOperationException>(() => Compare(History(Buy()) with { AccountId = "other" }));
        Assert.Throws<InvalidOperationException>(() => Compare(History(Buy(), Buy())));
        Assert.False(Compare(History(Buy(), Sell()), Snapshot() with { Cash = new(20, "USD") }).HoldingsMatch);
    }
    [Fact]
    public void JournalRoundTripIsScopedProtectedExclusiveAndRejectsRollback()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TraderVI-reconciliation-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = Compare(History(Buy(), Sell()));
            using (var journal = new ManualBuyFileJournal(directory, new Protector()))
            {
                journal.SaveReconciliation(result);
                Assert.Null(journal.ReadReconciliation("other", Account.Id));
                Assert.Equal(result.Holdings, journal.ReadReconciliation(result.IdentityId, Account.Id).Holdings);
                Assert.DoesNotContain("identity-fixture", Encoding.UTF8.GetString(File.ReadAllBytes(Assert.Single(Directory.GetFiles(directory, "*.reconciliation")))));
                using var rival = new ManualBuyFileJournal(directory, new Protector());
                Assert.Throws<IOException>(() => rival.ReadReconciliation(result.IdentityId, Account.Id));
                Assert.Throws<InvalidOperationException>(() => journal.SaveReconciliation(result with { CheckedAt = Now.AddMinutes(-1) }));
                var original = File.ReadAllBytes(Assert.Single(Directory.GetFiles(directory, "*.reconciliation")));
                journal.SaveReconciliation(result with { CheckedAt = Now.AddSeconds(1) });
                Assert.Equal(2, Directory.GetFiles(directory, "*.reconciliation").Length);
                Assert.Equal(original, File.ReadAllBytes(Directory.GetFiles(directory, "*.reconciliation").OrderBy(p => p).First()));
                Assert.Equal(Now.AddSeconds(1), journal.ReadReconciliation(result.IdentityId, Account.Id).CheckedAt);
            }
            using var reopened = new ManualBuyFileJournal(directory, new Protector());
            Assert.True(reopened.ReadReconciliation(result.IdentityId, Account.Id).HoldingsMatch);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task WorkflowSharesOrderGateAndDoesNotSaveAfterIdentitySwitchOrFailedRead()
    {
        var source = new Source(); var store = new Store(); using var gate = new SemaphoreSlim(1);
        var workflow = new BrokerReconciliationWorkflow(source, store, store, gate, new Clock());
        var result = await workflow.RefreshAsync(Account, 1);
        Assert.True(result.HoldingsMatch); Assert.Equal(1, store.Saves);
        await gate.WaitAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.RefreshAsync(Account, 1));
        gate.Release();
        source.SwitchLogin = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.RefreshAsync(Account, 1));
        Assert.Equal(1, store.Saves);
        source.SwitchLogin = false; source.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.RefreshAsync(Account, source.SessionRevision));
        Assert.Equal(1, store.Saves);
        source.Fail = false; source.ChangedActivity = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.RefreshAsync(Account, source.SessionRevision));
        Assert.Equal(1, store.Saves);
    }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    [Theory]
    [InlineData("history", "reading order history")]
    [InlineData("snapshot", "reading balances and holdings")]
    [InlineData("pending", "reading pending orders")]
    [InlineData("checkpoint", "checking activity after the read")]
    public async Task FailedReconciliationNamesTheFailedReadAndSafeDiagnosticWithoutSaving(string failAt, string step)
    {
        var source = new Source { FailAt = failAt }; var store = new Store(); using var gate = new SemaphoreSlim(1);
        var workflow = new BrokerReconciliationWorkflow(source, store, store, gate, new Clock());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.RefreshAsync(Account, 1));
        Assert.Contains(step, error.Message); Assert.Contains("Diagnostic: fixture response shape", error.Message);
        Assert.Contains("No partial result was saved", error.Message); Assert.Equal(0, store.Saves);
        source.FailAt = null;
        Assert.True((await workflow.RefreshAsync(Account, 1)).HoldingsMatch); Assert.Equal(1, store.Saves);
    }
    [Fact]
    public async Task FirstReconciliationCannotOmitAnAlreadyObservedManualRoundTrip()
    {
        var source = new Source(); var store = new Store(); using var gate = new SemaphoreSlim(1);
        var at = Now.AddHours(-1);
        var quote = new ManualBuyQuote(10, 9, at, "OPEN", at.AddHours(-1), at.AddHours(4), at);
        var request = Guid.NewGuid();
        store.Buys = [new(new(request, "identity-fixture", Account.Id, "security-fixture", "TFSA", "FIX", "XTSE", 10,
            new("FIX", "Manual", "Manual"), 10, quote, 500, at), WealthsimpleSubmissionStatus.Acknowledged,
            [new("FILLED", 10, 10, new(10, "CAD"), new(100, "CAD"), new(100, "CAD"), new(0, "CAD"), new(0, "CAD"),
                at, at, null, "REGULAR", at)])];
        var result = await new BrokerReconciliationWorkflow(source, store, store, gate, new Clock()).RefreshAsync(Account, 1);
        Assert.False(result.HoldingsMatch); Assert.Contains(result.HistoryConflicts, b => b.Contains("manual journal"));
        store.Buys = [];
        var again = await new BrokerReconciliationWorkflow(source, store, store, gate, new Clock()).RefreshAsync(Account, 1);
        Assert.NotEmpty(again.HistoryConflicts);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PostedBrokerHistoryCanMatchSavedManualFillOnlyWithIdenticalAmounts(bool changedAmount)
    {
        var source = new Source(); var store = new Store(); using var gate = new SemaphoreSlim(1);
        var at = Now.AddHours(-1); var request = Guid.NewGuid();
        var quote = new ManualBuyQuote(10, 9, at, "OPEN", at.AddHours(-1), at.AddHours(4), at);
        store.Buys = [new(new(request, "identity-fixture", Account.Id, "security-fixture", "TFSA", "FIX", "XTSE", 10,
            new("FIX", "Manual", "Manual"), 10, quote, 500, at), WealthsimpleSubmissionStatus.Acknowledged,
            [new("FILLED", 10, 10, new(10, "CAD"), new(100, "CAD"), new(changedAmount ? 102 : 101, "CAD"), new(1, "CAD"), new(0, "CAD"),
                at, at, null, "REGULAR", at)])];
        source.HistoryOverride = History(Buy("order-" + request.ToString("D")) with { Status = "POSTED" }, Sell());
        var result = await new BrokerReconciliationWorkflow(source, store, store, gate, new Clock()).RefreshAsync(Account, 1);
        Assert.Equal(!changedAmount, result.HoldingsMatch);
        Assert.Equal(changedAmount, result.HistoryConflicts.Any());
    }
    private sealed class Protector : IManualBuyProtector
    {
        public byte[] Protect(byte[] bytes) => bytes.Select(b => (byte)(b ^ 0xAA)).ToArray();
        public byte[] Unprotect(byte[] bytes) => Protect(bytes);
    }
    private sealed class Source : IBrokerReconciliationSource
    {
        public bool IsConnected => true;
        public long SessionRevision { get; private set; } = 1;
        public bool SwitchLogin, Fail, ChangedActivity;
        public string FailAt;
        public BrokerAccountHistory HistoryOverride;
        private WealthsimpleReadResult<T> Result<T>(string step, T value) => FailAt == step
            ? new(WealthsimpleReadStatus.UnsupportedResponse, Stage: "fixture response shape") : new(WealthsimpleReadStatus.Success, value);
        public Task<WealthsimpleReadResult<BrokerActivityCheckpoint>> ReadTfsaActivityCheckpointAsync(WealthsimpleTfsaAccount account, CancellationToken ct) =>
            Task.FromResult(Result<BrokerActivityCheckpoint>("checkpoint",
                new("identity-fixture", Account.Id, ChangedActivity ? "changed" : "activity-fixture", Now)));
        public Task<WealthsimpleReadResult<BrokerAccountHistory>> ReadTfsaOrderHistoryAsync(WealthsimpleTfsaAccount account, CancellationToken ct)
        {
            if (SwitchLogin) SessionRevision++;
            return Task.FromResult(Fail ? new WealthsimpleReadResult<BrokerAccountHistory>(WealthsimpleReadStatus.Unavailable) : Result("history", HistoryOverride ?? History(Buy(), Sell())));
        }
        public Task<WealthsimpleReadResult<WealthsimpleTfsaSnapshot>> ReadTfsaSnapshotAsync(WealthsimpleTfsaAccount account, CancellationToken ct) => Task.FromResult(Result("snapshot", Snapshot()));
        public Task<WealthsimpleReadResult<WealthsimplePendingOrders>> ReadTfsaPendingOrdersAsync(WealthsimpleTfsaAccount account, CancellationToken ct) => Task.FromResult(Result<WealthsimplePendingOrders>("pending", new(Account, Now, [])));
    }
    private sealed class Store : IManualBuyJournal, IBrokerReconciliationJournal
    {
        private BrokerAccountReconciliation saved;
        public int Saves;
        public IReadOnlyList<ManualBuyRecord> Buys = [];
        public IReadOnlyList<ManualBuyRecord> ReadAll() => Buys;
        public void Save(ManualBuyRecord record) => throw new NotSupportedException();
        public BrokerAccountReconciliation ReadReconciliation(string identityId, string accountId) => saved;
        public void SaveReconciliation(BrokerAccountReconciliation result) { saved = result; Saves++; }
        public void Dispose() { }
    }
}
