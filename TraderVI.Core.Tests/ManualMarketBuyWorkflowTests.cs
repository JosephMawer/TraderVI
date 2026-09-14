#nullable enable
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public class ManualMarketBuyWorkflowTests
{
    [Theory]
    [InlineData(1)] [InlineData(7)] [InlineData(17)] [InlineData(20)]
    public async Task ExactOperatorQuantityIsSavedBeforeOneSubmission(int quantity)
    {
        var f = new Fixture(); var review = await f.Prepare();
        f.Broker.OnSubmit = order =>
        {
            var saved = Assert.Single(f.Journal.Records);
            Assert.Equal(quantity, saved.Intent.Quantity); Assert.Equal(order.RequestId, saved.Intent.RequestId);
            Assert.False(saved.IsResolved); Assert.True(f.Safety.Held); Assert.Null(saved.Latest);
        };
        var result = await f.Flow.SubmitAsync(review, quantity);
        Assert.Equal(quantity, result.Order!.Intent.Quantity); Assert.Equal(1, f.Broker.Submissions);
        Assert.Equal("Hold", result.Order.Intent.Origin.Direction); Assert.Null(result.Order.Latest);
        Assert.False(result.Order.IsResolved); Assert.False(f.Safety.Held);
    }

    [Theory]
    [InlineData("stale-quote")] [InlineData("future-quote")] [InlineData("closed")]
    [InlineData("after-close")] [InlineData("before-open")] [InlineData("pending")]
    [InlineData("cash")] [InlineData("currency")] [InlineData("stale-account")]
    [InlineData("future-account")] [InlineData("crossed")]
    public async Task IncompleteOrIneligibleChecksCannotSubmit(string failure)
    {
        var f = new Fixture(); var c = f.Broker.Context;
        f.Broker.Context = failure switch
        {
            "stale-quote" => c with { Quote = c.Quote with { QuotedAt = f.Clock.Now.AddMinutes(-3) } },
            "future-quote" => c with { Quote = c.Quote with { QuotedAt = f.Clock.Now.AddSeconds(1) } },
            "closed" => c with { Quote = c.Quote with { MarketStatus = "CLOSED" } },
            "after-close" => c with { Quote = c.Quote with { SessionClose = f.Clock.Now } },
            "before-open" => c with { Quote = c.Quote with { SessionOpen = f.Clock.Now.AddMinutes(1) } },
            "pending" => c with { PendingOrders = c.PendingOrders with { Orders = [new("FIX", "BUY", "CANCEL_PENDING", "MARKET", 1, null, f.Clock.Now)] } },
            "cash" => c with { Snapshot = c.Snapshot with { Cash = new(0, "CAD") } },
            "currency" => c with { Snapshot = c.Snapshot with { BuyingPower = new(1000, "USD") } },
            "stale-account" => c with { Snapshot = c.Snapshot with { RetrievedAt = f.Clock.Now.AddMinutes(-1) } },
            "future-account" => c with { Snapshot = c.Snapshot with { RetrievedAt = f.Clock.Now.AddSeconds(1) } },
            _ => c with { Quote = c.Quote with { Bid = 12 } }
        };
        var review = await f.Prepare();
        Assert.NotNull(f.Flow.ValidateReview(review, 1));
        Assert.Null((await f.Flow.SubmitAsync(review, 1)).Order);
        Assert.Equal(0, f.Broker.Submissions); Assert.Empty(f.Journal.Records);
    }

    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(101)] [InlineData(int.MaxValue)]
    public async Task InvalidOrUnaffordableQuantityCannotReachBroker(int shares)
    {
        var f = new Fixture(); Assert.Null((await f.Flow.SubmitAsync(await f.Prepare(), shares)).Order);
        Assert.Equal(0, f.Broker.Submissions); Assert.Empty(f.Journal.Records);
    }

    [Fact]
    public async Task CashCeilingUsesTheLowerOfCashAndBuyingPower()
    {
        var f = new Fixture(); f.Broker.Context = f.Broker.Context with { Snapshot = f.Broker.Context.Snapshot with { Cash = new(15, "CAD") } };
        var review = await f.Prepare(); Assert.Null(f.Flow.ValidateReview(review, 1)); Assert.NotNull(f.Flow.ValidateReview(review, 2));
    }

    [Fact]
    public async Task BuyAlwaysRechecksSafetyAndAccountBeforeTransmission()
    {
        var f = new Fixture(); var review = await f.Prepare(); f.Safety.Reason = "Buying paused";
        Assert.Null((await f.Flow.SubmitAsync(review, 1)).Order); Assert.Equal(0, f.Broker.Submissions);
        f.Safety.Reason = null; f.Broker.Context = f.Broker.Context with { Snapshot = f.Broker.Context.Snapshot with { Cash = new(5, "CAD") } };
        Assert.Null((await f.Flow.SubmitAsync(review, 1)).Order); Assert.Equal(0, f.Broker.Submissions);
    }

    [Fact]
    public async Task IncreasedAskRequiresAnotherExplicitReview()
    {
        var f = new Fixture(); var review = await f.Prepare();
        f.Broker.Context = f.Broker.Context with { Quote = f.Broker.Context.Quote with { Ask = 11 } };
        var updated = await f.Flow.SubmitAsync(review, 3);
        Assert.Null(updated.Order); Assert.NotNull(updated.UpdatedReview); Assert.Equal(0, f.Broker.Submissions);
        Assert.NotNull((await f.Flow.SubmitAsync(updated.UpdatedReview!, 3)).Order); Assert.Equal(1, f.Broker.Submissions);
    }

    [Theory] [InlineData("expiry")] [InlineData("session")] [InlineData("disconnect")]
    public async Task ReviewMustRemainCurrent(string change)
    {
        var f = new Fixture(); var review = await f.Prepare();
        if (change == "expiry") f.Clock.Now = f.Clock.Now.AddSeconds(31);
        if (change == "session") f.Broker.SessionRevision++;
        if (change == "disconnect") f.Broker.IsConnected = false;
        Assert.Null((await f.Flow.SubmitAsync(review, 1)).Order); Assert.Equal(0, f.Broker.Submissions);
    }

    [Fact]
    public async Task ChangedInstrumentOrAccountCannotRetargetAReviewedOrder()
    {
        var f = new Fixture(); var review = await f.Prepare();
        f.Broker.Context = f.Broker.Context with { Security = new("other-id", "FIX.TO", "XTSE") { SourceIdentity = "identity-fixture" } };
        Assert.Null((await f.Flow.SubmitAsync(review, 1)).Order); Assert.Equal(0, f.Broker.Submissions);
    }

    [Fact]
    public async Task BrokerHoldingIdentityCannotBeReplacedByAMatchingCanadianTicker()
    {
        var f = new Fixture();
        var origin = ManualBuyOrigin.FromHolding(new("FIX", "Foreign fixture equity", "EQUITY", 7, new(70,"CAD"), null, null) { SecurityId = "foreign-security" });
        var result = await f.Flow.PrepareAsync(f.Account, origin!);
        Assert.Null(result.Prepared); Assert.Contains("not the selected broker holding", result.Message); Assert.Equal(0, f.Broker.Submissions);
        Assert.Null(ManualBuyOrigin.FromHolding(new("FIX", "Unknown fixture", "EQUITY", 1, null, null, null)));
    }

    [Fact]
    public async Task DuplicateClickCannotResubmitEvenAnImmediatelyRejectedOrder()
    {
        var f = new Fixture(); f.Broker.SubmissionStatus = WealthsimpleSubmissionStatus.Rejected;
        var review = await f.Prepare();
        await Task.WhenAll(f.Flow.SubmitAsync(review, 7), f.Flow.SubmitAsync(review, 7));
        Assert.Equal(1, f.Broker.Submissions); Assert.Single(f.Journal.Records);
    }

    [Fact]
    public async Task JournalFailureBeforeTransmissionSendsNothing()
    {
        var f = new Fixture(); var review = await f.Prepare(); f.Journal.FailSaveNumber = 1;
        await Assert.ThrowsAsync<IOException>(() => f.Flow.SubmitAsync(review, 1)); Assert.Equal(0, f.Broker.Submissions);
    }

    [Fact]
    public async Task JournalFailureAfterTransmissionKeepsOriginalIntentForRecovery()
    {
        var f = new Fixture(); var review = await f.Prepare(); f.Journal.FailSaveNumber = 2;
        var sent = await f.Flow.SubmitAsync(review, 7);
        Assert.NotNull(sent.Order); Assert.Equal(1, f.Broker.Submissions);
        var original = Assert.Single(f.Journal.Records); Assert.Null(original.Submission); Assert.False(original.IsResolved);
        var restarted = new ManualMarketBuyWorkflow(f.Broker, f.Journal, f.Safety, f.Clock);
        Assert.Null((await restarted.PrepareAsync(f.Account, f.Origin)).Prepared);
        f.Broker.State = f.State(7, 7);
        var recovered = await restarted.CheckOrderAsync(original.Intent.RequestId);
        Assert.True(recovered.Order.IsResolved); Assert.Equal(original.Intent.RequestId, f.Broker.LastReadId);
        Assert.Equal(1, f.Broker.Submissions);
    }

    [Fact]
    public async Task TimeoutAndNullLookupBlockReplacementAcrossWorkflowRestart()
    {
        var f = new Fixture(); f.Broker.ThrowOnSubmit = true;
        var sent = await f.Flow.SubmitAsync(await f.Prepare(), 7);
        Assert.Equal(WealthsimpleSubmissionStatus.OutcomeUnknown, sent.Order!.Submission);
        f.Broker.ReadStatus = WealthsimpleReadStatus.Unavailable;
        var restart = new ManualMarketBuyWorkflow(f.Broker, f.Journal, f.Safety, f.Clock);
        var check = await restart.CheckOrderAsync(sent.Order.Intent.RequestId);
        Assert.False(check.Order.IsResolved); Assert.Null((await restart.PrepareAsync(f.Account, f.Origin)).Prepared);
        Assert.Equal(1, f.Broker.Submissions);
    }

    [Fact]
    public async Task PartialAndRepeatedCumulativeFillsDoNotDuplicateQuantity()
    {
        var f = new Fixture(); var sent = await f.Flow.SubmitAsync(await f.Prepare(), 7);
        f.Broker.State = f.State(7, 3, "PARTIALLY_FILLED");
        await f.Flow.CheckOrderAsync(sent.Order!.Intent.RequestId);
        var repeated = await f.Flow.CheckOrderAsync(sent.Order.Intent.RequestId);
        Assert.Single(repeated.Order.Observations); Assert.Equal(3, repeated.Order.Latest!.FilledQuantity);
        Assert.False(repeated.Order.IsResolved);
        f.Broker.State = f.State(7, 7); var complete = await f.Flow.CheckOrderAsync(sent.Order.Intent.RequestId);
        Assert.True(complete.Order.IsResolved); Assert.Equal(2, complete.Order.Observations.Count); Assert.Equal(7, complete.Order.Latest!.FilledQuantity);
        Assert.Equal(1, f.Broker.Submissions);
    }

    [Theory] [InlineData("missing-fees")] [InlineData("regression")] [InlineData("unknown-status")]
    public async Task IncompleteOrConflictingBrokerFactsStayUnresolved(string mode)
    {
        var f = new Fixture(); var sent = await f.Flow.SubmitAsync(await f.Prepare(), 7);
        if (mode == "regression") { f.Broker.State = f.State(7, 4, "PARTIALLY_FILLED"); await f.Flow.CheckOrderAsync(sent.Order!.Intent.RequestId); }
        f.Broker.State = mode switch
        {
            "missing-fees" => f.State(7, 7) with { Fees = null },
            "regression" => f.State(7, 3, "PARTIALLY_FILLED"),
            _ => f.State(7, 7, "UNRECOGNIZED")
        };
        var checkedOrder = await f.Flow.CheckOrderAsync(sent.Order!.Intent.RequestId);
        Assert.False(checkedOrder.Order.IsResolved);
        Assert.Equal(mode == "regression", checkedOrder.Order.ConflictingObservation);
    }

    [Theory] [InlineData("CANCELLED")] [InlineData("EXPIRED")]
    public async Task TerminalPartialOrderKeepsItsActualFill(string status)
    {
        var f = new Fixture(); var sent = await f.Flow.SubmitAsync(await f.Prepare(), 7);
        f.Broker.State = f.State(7, 3, status);
        var observed = await f.Flow.CheckOrderAsync(sent.Order!.Intent.RequestId);
        Assert.True(observed.Order.IsResolved); Assert.Equal(3, observed.Order.Latest!.FilledQuantity);
        Assert.Equal(30, observed.Order.Latest.FilledNetValue!.Amount);
    }

    [Fact]
    public async Task FileJournalRetainsProtectedIntentAndFailsClosedOnCorruption()
    {
        var f = new Fixture(); var order = (await f.Flow.SubmitAsync(await f.Prepare(), 7)).Order!;
        using var directory = new TempDirectory(); var protector = new FixtureProtector();
        using (var first = new ManualBuyFileJournal(directory.Path, protector))
        {
            first.Save(order);
            using var other = new ManualBuyFileJournal(directory.Path, protector);
            Assert.Throws<IOException>(() => other.ReadAll());
            Assert.Throws<InvalidOperationException>(() => first.Save(order with { Intent = order.Intent with { Quantity = 9 } }));
        }
        var path = System.IO.Path.Combine(directory.Path, order.Intent.RequestId.ToString("N") + ".order");
        Assert.DoesNotContain("identity-fixture", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)));
        using (var reopened = new ManualBuyFileJournal(directory.Path, protector)) Assert.Equal(order.Intent, Assert.Single(reopened.ReadAll()).Intent);
        File.WriteAllText(path, "corrupt");
        using var damaged = new ManualBuyFileJournal(directory.Path, protector); Assert.ThrowsAny<Exception>(() => damaged.ReadAll());
    }

    [Fact]
    public async Task ViewModelRequiresTypedQuantityAndDoesNotSubmitOnInitializationOrRefresh()
    {
        var f = new Fixture(); using var vm = new ManualBuyTicketViewModel(f.Flow, f.Origin);
        await vm.InitializeAsync(); Assert.Equal("", vm.QuantityText); Assert.False(vm.CanSubmit);
        foreach (string invalid in new[] { "0", "-1", "1.5", "1e2", "2147483648", "text" }) { vm.QuantityText = invalid; Assert.False(vm.CanSubmit); }
        vm.QuantityText = "17"; Assert.True(vm.CanSubmit); Assert.Contains("170.00", vm.EstimateText); Assert.Contains("17", vm.SubmitText);
        await vm.RefreshAsync(); Assert.Equal("17", vm.QuantityText); Assert.Equal(0, f.Broker.Submissions);
        f.Broker.State = f.State(17, 17); await vm.SubmitAsync();
        Assert.Equal(17, Assert.Single(f.Journal.Records).Intent.Quantity); Assert.True(vm.HasSelectedOrder); Assert.False(vm.CanSubmit);
        Assert.Contains("Filled: 17 of 17", vm.FillSummary);
    }

    [Fact]
    public async Task ReopeningTicketRecoversPendingOrderRatherThanStartingANewOne()
    {
        var f = new Fixture(); await f.Flow.SubmitAsync(await f.Prepare(), 7);
        using var vm = new ManualBuyTicketViewModel(f.Flow, new("OTHER", "Another grid", "Hold"));
        await vm.InitializeAsync(); Assert.True(vm.HasSelectedOrder); Assert.False(vm.CanSubmit); Assert.Equal("FIX.TO", vm.SelectedOrder!.Symbol);
        Assert.Equal(1, f.Broker.Submissions);
    }

    internal sealed class Fixture
    {
        internal Clock Clock = new();
        internal MemoryJournal Journal = new();
        internal Safety Safety = new();
        internal FakeBroker Broker;
        internal ManualMarketBuyWorkflow Flow;
        internal WealthsimpleTfsaAccount Account = new("account-fixture", "Fixture TFSA");
        internal ManualBuyOrigin Origin = new("FIX", "Delphi Breakout #1 · saved Sep 10, 2026", "Hold");
        internal Fixture()
        {
            var security = new WealthsimpleCanadianSecurity("security-fixture", "FIX.TO", "XTSE") { SourceIdentity = "identity-fixture" };
            var snapshot = new WealthsimpleTfsaSnapshot(Account, Clock.Now, new(1000,"CAD"), new(1000,"CAD"), new(1000,"CAD"), null, null, null, null, null, []);
            Broker = new(new(Account, security, snapshot, new(Account, Clock.Now, []), new(10, 9.99m, Clock.Now, "OPEN", Clock.Now.AddHours(-2), Clock.Now.AddHours(4), Clock.Now)));
            Flow = new(Broker, Journal, Safety, Clock);
        }
        internal async Task<PreparedManualBuy> Prepare() => (await Flow.PrepareAsync(Account, Origin)).Prepared!;
        internal WealthsimpleMarketBuyState State(int submitted, decimal filled, string status = "FILLED") => new(status, submitted, filled, new(10,"CAD"),
            new(filled*10,"CAD"),new(filled*10,"CAD"),new(0,"CAD"),new(0,"CAD"),Clock.Now,filled>0?Clock.Now:null,null,"REGULAR",Clock.Now);
    }
    internal sealed class Clock : TimeProvider { internal DateTimeOffset Now = new(2026,9,10,16,0,0,TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    internal sealed class FakeBroker(ManualBuyContext context) : IManualBuyBroker
    {
        public bool IsConnected { get; set; } = true;
        public long SessionRevision { get; set; } = 1;
        internal ManualBuyContext Context = context;
        internal int Submissions;
        internal Action<WealthsimpleMarketBuy>? OnSubmit;
        internal bool ThrowOnSubmit;
        internal WealthsimpleSubmissionStatus SubmissionStatus = WealthsimpleSubmissionStatus.Acknowledged;
        internal WealthsimpleReadStatus ReadStatus = WealthsimpleReadStatus.Success;
        internal WealthsimpleMarketBuyState? State;
        internal Guid LastReadId;
        public Task<WealthsimpleReadResult<IReadOnlyList<WealthsimpleTfsaAccount>>> ReadTfsaAccountsAsync(CancellationToken ct) => Task.FromResult(new WealthsimpleReadResult<IReadOnlyList<WealthsimpleTfsaAccount>>(WealthsimpleReadStatus.Success, [Context.Account]));
        public Task<WealthsimpleReadResult<ManualBuyContext>> ReadManualBuyContextAsync(WealthsimpleTfsaAccount account, string symbol, CancellationToken ct) => Task.FromResult(new WealthsimpleReadResult<ManualBuyContext>(WealthsimpleReadStatus.Success, Context));
        public Task<WealthsimpleMarketBuySubmission> SubmitManualBuyAsync(WealthsimpleMarketBuy order, CancellationToken ct)
        { Submissions++; OnSubmit?.Invoke(order); if (ThrowOnSubmit) throw new IOException("private-fixture-error"); return Task.FromResult(new WealthsimpleMarketBuySubmission(SubmissionStatus)); }
        public Task<WealthsimpleReadResult<WealthsimpleMarketBuyState>> ReadManualBuyOrderAsync(ManualBuyIntent intent, CancellationToken ct)
        { LastReadId = intent.RequestId; return Task.FromResult(new WealthsimpleReadResult<WealthsimpleMarketBuyState>(ReadStatus, State)); }
    }
    internal sealed class MemoryJournal : IManualBuyJournal
    {
        internal List<ManualBuyRecord> Records = [];
        internal int Saves, FailSaveNumber;
        public IReadOnlyList<ManualBuyRecord> ReadAll() => Records.ToArray();
        public void Save(ManualBuyRecord record) { if (++Saves == FailSaveNumber) throw new IOException(); Records.RemoveAll(x => x.Intent.RequestId == record.Intent.RequestId); Records.Add(record); }
        public void Dispose() { }
    }
    internal sealed class Safety : IManualBuySafetySource
    {
        internal bool Held;
        internal string? Reason;
        public Task<IManualBuySafetyLease> AcquireAsync(CancellationToken ct) { Held = true; return Task.FromResult<IManualBuySafetyLease>(new Lease(this)); }
        private sealed class Lease(Safety owner) : IManualBuySafetyLease { public string? BlockReason => owner.Reason; public ValueTask DisposeAsync() { owner.Held=false; return ValueTask.CompletedTask; } }
    }
    private sealed class FixtureProtector : IManualBuyProtector
    {
        private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
        public byte[] Protect(byte[] data) { using var aes = new AesGcm(key,16); var nonce = RandomNumberGenerator.GetBytes(12); var result = new byte[28+data.Length]; nonce.CopyTo(result,0); aes.Encrypt(nonce,data,result.AsSpan(28),result.AsSpan(12,16)); return result; }
        public byte[] Unprotect(byte[] data) { using var aes = new AesGcm(key,16); var result = new byte[data.Length-28]; aes.Decrypt(data.AsSpan(0,12),data.AsSpan(28),data.AsSpan(12,16),result); return result; }
    }
    private sealed class TempDirectory : IDisposable
    {
        internal string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),"TraderVI-manual-orders-test-"+Guid.NewGuid().ToString("N"));
        public void Dispose() { var full=System.IO.Path.GetFullPath(Path); if(full.StartsWith(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) && System.IO.Path.GetFileName(full).StartsWith("TraderVI-manual-orders-test-") && Directory.Exists(full)) Directory.Delete(full,true); }
    }
}
