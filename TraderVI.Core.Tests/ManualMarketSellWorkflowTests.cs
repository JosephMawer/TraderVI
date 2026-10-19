#nullable enable
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public class ManualMarketSellWorkflowTests
{
    [Fact]
    public async Task ReviewedOpeningRequestRecoversTheSameOrderAcrossWorkflowRestart()
    {
        var f = new Fixture(); var request = Guid.NewGuid();
        var sent = await f.Flow.SellReviewedAsync(f.Selection, request);
        Assert.Equal(request, sent.Order!.Intent.RequestId); Assert.Equal(1, f.Broker.Submissions);
        var restarted = new ManualMarketSellWorkflow(f.Broker, f.Journal, f.Clock);
        var recovered = await restarted.SellReviewedAsync(f.Selection, request);
        Assert.True(recovered.Order!.IsResolved); Assert.Equal(1, f.Broker.Submissions);
        Assert.Equal(request, f.Broker.LastReadId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.SellReviewedAsync(f.Selection with { Quantity = 19 }, request));
        Assert.Equal(1, f.Broker.Submissions);
    }

    [Fact]
    public async Task OneClickSellsExactEntireHoldingOnlyAfterDurableIntentAndFreshChecks()
    {
        var f = new Fixture();
        f.Broker.OnSubmit = order =>
        {
            var saved = Assert.Single(f.Journal.Sells);
            Assert.Equal(20, order.Quantity); Assert.Equal(order.RequestId, saved.Intent.RequestId);
            Assert.Null(saved.Submission); Assert.Empty(saved.Observations);
        };
        var result = await f.Flow.SellAllAsync(f.Selection);
        Assert.Equal(WealthsimpleSubmissionStatus.Acknowledged, result.Order!.Submission);
        Assert.False(result.Order.IsResolved); Assert.Equal(1, f.Broker.ContextReads); Assert.Equal(1, f.Broker.Submissions);
        Assert.Null((await f.Flow.SellAllAsync(f.Selection)).Order); Assert.Equal(1, f.Broker.Submissions);
    }

    [Theory]
    [InlineData("closed")][InlineData("before-open")][InlineData("after-close")]
    [InlineData("stale-quote")][InlineData("future-quote")][InlineData("crossed-quote")]
    [InlineData("stale-holdings")][InlineData("future-holdings")][InlineData("stale-pending")]
    [InlineData("pending")][InlineData("quantity")][InlineData("missing-holding")]
    [InlineData("account")][InlineData("security")][InlineData("identity")]
    public async Task MissingChangedOrIneligibleFactsNeverSubmit(string failure)
    {
        var f = new Fixture(); var c = f.Broker.Context;
        f.Broker.Context = failure switch
        {
            "closed" => c with { Quote = c.Quote with { MarketStatus = "CLOSED" } },
            "before-open" => c with { Quote = c.Quote with { SessionOpen = f.Clock.Now.AddMinutes(1) } },
            "after-close" => c with { Quote = c.Quote with { SessionClose = f.Clock.Now } },
            "stale-quote" => c with { Quote = c.Quote with { QuotedAt = f.Clock.Now.AddMinutes(-3) } },
            "future-quote" => c with { Quote = c.Quote with { QuotedAt = f.Clock.Now.AddSeconds(1) } },
            "crossed-quote" => c with { Quote = c.Quote with { Bid = 100 } },
            "stale-holdings" => c with { Snapshot = c.Snapshot with { RetrievedAt = f.Clock.Now.AddMinutes(-1) } },
            "future-holdings" => c with { Snapshot = c.Snapshot with { RetrievedAt = f.Clock.Now.AddSeconds(1) } },
            "stale-pending" => c with { PendingOrders = c.PendingOrders with { RetrievedAt = f.Clock.Now.AddMinutes(-1) } },
            "pending" => c with { PendingOrders = c.PendingOrders with { Orders = [new("FIX", "SELL", "CANCEL_PENDING", "LIMIT", 1, new(11,"CAD"), f.Clock.Now)] } },
            "quantity" => c with { Snapshot = c.Snapshot with { Holdings = [c.Snapshot.Holdings[0] with { Quantity = 21 }] } },
            "missing-holding" => c with { Snapshot = c.Snapshot with { Holdings = [] } },
            "account" => c with { Account = new("other-account", "Other") },
            "security" => c with { Security = new("other-security", "FIX.TO", "XTSE") { SourceIdentity = "identity-fixture" } },
            _ => c with { Security = new("security-fixture", "FIX.TO", "XTSE") }
        };
        Assert.Null((await f.Flow.SellAllAsync(f.Selection)).Order);
        Assert.Equal(0, f.Broker.Submissions); Assert.Empty(f.Journal.Sells);
    }

    [Theory][InlineData(0)][InlineData(-1)][InlineData(1.5)][InlineData(2147483648)]
    public async Task InvalidWholeHoldingNeverRoundsOrCallsBroker(decimal quantity)
    {
        var f = new Fixture();
        Assert.Null((await f.Flow.SellAllAsync(f.Selection with { Quantity = quantity })).Order);
        Assert.Equal(0, f.Broker.ContextReads); Assert.Equal(0, f.Broker.Submissions);
    }

    [Theory][InlineData(1)][InlineData(7)]
    public async Task WholeHoldingDoesNotRequireTenShareLots(int shares)
    {
        var f = new Fixture(); var c = f.Broker.Context;
        f.Broker.Context = c with { Snapshot = c.Snapshot with { Holdings = [c.Snapshot.Holdings[0] with { Quantity = shares }] } };
        Assert.Equal(shares, (await f.Flow.SellAllAsync(f.Selection with { Quantity = shares })).Order!.Intent.Quantity);
    }

    [Fact]
    public async Task SellDoesNotDependOnAvailableCashOrBuyingSafetyHolds()
    {
        var f = new Fixture(); var c = f.Broker.Context;
        f.Broker.Context = c with { Snapshot = c.Snapshot with { Cash = new(-10,"CAD"), BuyingPower = null } };
        Assert.NotNull((await f.Flow.SellAllAsync(f.Selection)).Order); Assert.Equal(1, f.Broker.Submissions);
    }

    [Theory][InlineData("disconnect")][InlineData("revision")][InlineData("during-read")][InlineData("read-failure")]
    public async Task SessionAndReadFailuresPreventSubmission(string failure)
    {
        var f = new Fixture();
        if (failure == "disconnect") f.Broker.IsConnected = false;
        if (failure == "revision") f.Broker.SessionRevision++;
        if (failure == "during-read") f.Broker.OnContext = () => f.Broker.SessionRevision++;
        if (failure == "read-failure") f.Broker.ReadStatus = WealthsimpleReadStatus.Unavailable;
        Assert.Null((await f.Flow.SellAllAsync(f.Selection)).Order); Assert.Equal(0, f.Broker.Submissions);
    }

    [Theory][InlineData(1)][InlineData(2)]
    public async Task StorageFailureNeverLosesAnUncertainTransmission(int failSave)
    {
        var f = new Fixture(); f.Journal.FailSave = failSave;
        if (failSave == 1)
        {
            await Assert.ThrowsAsync<IOException>(() => f.Flow.SellAllAsync(f.Selection));
            Assert.Equal(0, f.Broker.Submissions); Assert.Empty(f.Journal.Sells);
        }
        else
        {
            var result = await f.Flow.SellAllAsync(f.Selection);
            Assert.Contains("could not be saved", result.Message); Assert.Equal(1, f.Broker.Submissions);
            Assert.Null(Assert.Single(f.Journal.Sells).Submission);
            var restarted = new ManualMarketSellWorkflow(f.Broker, f.Journal, f.Clock);
            Assert.Null((await restarted.SellAllAsync(f.Selection with { SelectionId = Guid.NewGuid() })).Order);
        }
    }

    [Fact]
    public async Task TimeoutAndMissingLookupRetainOriginalIntentAndBlockBothSides()
    {
        var f = new Fixture(); f.Broker.ThrowSubmit = true;
        var order = (await f.Flow.SellAllAsync(f.Selection)).Order!;
        Assert.Equal(WealthsimpleSubmissionStatus.OutcomeUnknown, order.Submission);
        f.Broker.ReadStatus = WealthsimpleReadStatus.Unavailable;
        Assert.False((await f.Flow.CheckOrderAsync(order.Intent.RequestId)).Order!.IsResolved);
        Assert.True(((IManualBuyJournal)f.Journal).HasUnresolvedOrders());
        var buy = new ManualMarketBuyWorkflowTests.Fixture();
        var buyFlow = new ManualMarketBuyWorkflow(buy.Broker, f.Journal, buy.Safety, buy.Clock);
        Assert.Null((await buyFlow.PrepareAsync(buy.Account, buy.Origin)).Prepared);
        Assert.Equal(0, buy.Broker.Submissions);
    }

    [Fact]
    public async Task UnresolvedBuyBlocksSell()
    {
        var f = new Fixture(); var buy = new ManualMarketBuyWorkflowTests.Fixture();
        var order = (await buy.Flow.SubmitAsync(await buy.Prepare(), 1)).Order!;
        f.Journal.Save(order);
        Assert.Null((await f.Flow.SellAllAsync(f.Selection)).Order); Assert.Equal(0, f.Broker.Submissions);
    }

    [Fact]
    public async Task OverlappingSellClickIsRejectedRatherThanQueued()
    {
        var f = new Fixture(); var gate = new SemaphoreSlim(1,1);
        var flow = new ManualMarketSellWorkflow(f.Broker, f.Journal, f.Clock, gate);
        await gate.WaitAsync();
        Assert.Null((await flow.SellAllAsync(f.Selection)).Order); Assert.Equal(0, f.Broker.ContextReads);
        gate.Release(); Assert.NotNull((await flow.SellAllAsync(f.Selection)).Order);
    }

    [Fact]
    public async Task FillRemainsReservedUntilFreshInventoryMatchesAndRecoveryNeverSubmits()
    {
        var f = new Fixture(); var order = (await f.Flow.SellAllAsync(f.Selection)).Order!;
        f.Broker.Inventory = new(20, false, f.Clock.Now);
        var first = (await f.Flow.CheckOrderAsync(order.Intent.RequestId)).Order!;
        Assert.True(first.Latest!.IsFilled); Assert.False(first.IsResolved);
        var restarted = new ManualMarketSellWorkflow(f.Broker, f.Journal, f.Clock);
        Assert.Null((await restarted.SellAllAsync(f.Selection with { SelectionId = Guid.NewGuid() })).Order);
        f.Broker.Inventory = new(0, false, f.Clock.Now);
        var final = (await restarted.CheckOrderAsync(order.Intent.RequestId)).Order!;
        Assert.True(final.IsResolved); Assert.NotNull(final.InventoryVerifiedAt); Assert.Single(final.Observations);
        Assert.Equal(1, f.Broker.Submissions); Assert.Equal(order.Intent.RequestId, f.Broker.LastReadId);
    }

    [Theory][InlineData("stale")][InlineData("future")][InlineData("pending")][InlineData("missing")]
    public async Task UnverifiedInventoryCannotReleaseReservation(string failure)
    {
        var f = new Fixture(); var order = (await f.Flow.SellAllAsync(f.Selection)).Order!;
        f.Broker.Inventory = failure switch { "stale" => new(0,false,f.Clock.Now.AddSeconds(-1)),
            "future" => new(0,false,f.Clock.Now.AddSeconds(1)), "pending" => new(0,true,f.Clock.Now), _ => null };
        Assert.False((await f.Flow.CheckOrderAsync(order.Intent.RequestId)).Order!.IsResolved);
    }

    [Fact]
    public async Task InventoryExceptionDoesNotDiscardObservedFill()
    {
        var f = new Fixture(); var order = (await f.Flow.SellAllAsync(f.Selection)).Order!; f.Broker.ThrowInventory = true;
        await Assert.ThrowsAsync<IOException>(() => f.Flow.CheckOrderAsync(order.Intent.RequestId));
        Assert.True(Assert.Single(f.Journal.Sells).Latest!.IsFilled); Assert.False(Assert.Single(f.Journal.Sells).IsResolved);
    }

    [Fact]
    public async Task TerminalPartialFillUsesExactRemainingInventoryAndPreservesNetProceeds()
    {
        var f = new Fixture(); var order = (await f.Flow.SellAllAsync(f.Selection)).Order!;
        f.Broker.State = f.State(3.5m, "PARTIALLY_FILLED") with { FilledNetValue = new(34.5m,"CAD"), Fees = new(0.5m,"CAD") };
        var partial = (await f.Flow.CheckOrderAsync(order.Intent.RequestId)).Order!;
        Assert.False(partial.IsResolved); Assert.Equal(34.5m,partial.Latest!.FilledNetValue!.Amount);
        f.Broker.State = f.Broker.State with { Status = "CANCELLED" }; f.Broker.Inventory = new(16.5m,false,f.Clock.Now);
        Assert.True((await f.Flow.CheckOrderAsync(order.Intent.RequestId)).Order!.IsResolved);
        Assert.Equal(1, f.Broker.Submissions);
    }

    [Fact]
    public async Task ChangedTerminalPartialFillCannotReuseEarlierInventoryVerification()
    {
        var f = new Fixture(); var order = (await f.Flow.SellAllAsync(f.Selection)).Order!;
        f.Broker.State = f.State(3,"CANCELLED"); f.Broker.Inventory = new(17,false,f.Clock.Now);
        Assert.True((await f.Flow.CheckOrderAsync(order.Intent.RequestId)).Order!.IsResolved);
        f.Broker.State = f.State(20);
        var changed = (await f.Flow.CheckOrderAsync(order.Intent.RequestId)).Order!;
        Assert.True(changed.ConflictingObservation); Assert.False(changed.IsResolved);
    }

    [Theory][InlineData("quantity")][InlineData("future")][InlineData("regression")][InlineData("missing-quantity")][InlineData("missing-fill-time")]
    public async Task ConflictingFillFactsRemainBlocked(string failure)
    {
        var f = new Fixture(); var order = (await f.Flow.SellAllAsync(f.Selection)).Order!;
        await f.Flow.CheckOrderAsync(order.Intent.RequestId);
        f.Broker.State = failure switch {
            "quantity" => f.State(20) with { SubmittedQuantity = 21 },
            "future" => f.State(20) with { RetrievedAt = f.Clock.Now.AddSeconds(1) },
            "regression" => f.State(3,"PARTIALLY_FILLED"),
            "missing-quantity" => f.State(20) with { FilledQuantity = null },
            _ => f.State(20) with { LastFilledAt = null } };
        var result = (await f.Flow.CheckOrderAsync(order.Intent.RequestId)).Order!;
        Assert.True(result.ConflictingObservation); Assert.False(result.IsResolved);
    }

    [Fact]
    public async Task LoadingHistoryAndLimitEditingNeverSubmit()
    {
        var f = new Fixture(); using var vm = new ManualSellViewModel(f.Flow);
        await vm.LoadAsync(); await vm.CheckAsync(); Assert.Equal(0,f.Broker.Submissions);
        var limit = new LimitSellTicketViewModel(f.Selection) { QuantityText = "7", LimitPriceText = "11.50" };
        Assert.Contains("80.50",limit.Estimate); Assert.False(limit.CanSubmit);
        limit.QuantityText="21"; Assert.Contains("within the holding",limit.Estimate); Assert.False(limit.CanSubmit);
        await vm.SellAllAsync(f.Selection); Assert.Equal(1,f.Broker.Submissions);
        Assert.Contains("Net proceeds",vm.FillSummary); Assert.Single(vm.Orders);
        await vm.LoadAsync(); Assert.Equal(1,f.Broker.Submissions);
    }

    [Fact]
    public async Task ProtectedJournalKeepsBothSidesCompatibleImmutableAndExclusivelyLocked()
    {
        using var temp = new TempDirectory(); var protector = new Protector(); var f = new Fixture();
        var sell = (await f.Flow.SellAllAsync(f.Selection)).Order!;
        var buy = new ManualMarketBuyWorkflowTests.Fixture(); var buyOrder=(await buy.Flow.SubmitAsync(await buy.Prepare(),1)).Order!;
        using (var journal = new ManualBuyFileJournal(temp.Path,protector))
        {
            journal.Save(buyOrder); journal.SaveSell(sell);
            Assert.Equal(buyOrder.Intent,Assert.Single(journal.ReadAll()).Intent); Assert.Equal(sell.Intent,Assert.Single(journal.ReadSells()).Intent);
            Assert.True(journal.HasUnresolvedOrders());
            Assert.Throws<InvalidOperationException>(()=>journal.SaveSell(sell with { Intent=sell.Intent with { Quantity=19 } }));
            using var other=new ManualBuyFileJournal(temp.Path,protector); Assert.Throws<IOException>(()=>other.ReadSells());
        }
        using (var reopened=new ManualBuyFileJournal(temp.Path,protector))
        {
            Assert.Equal(sell.Intent,Assert.Single(reopened.ReadSells()).Intent); Assert.True(reopened.HasUnresolvedOrders());
            var file=Assert.Single(Directory.GetFiles(temp.Path,"*.sell"));
            Assert.DoesNotContain("identity-fixture",System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)));
            File.WriteAllText(file,"corrupt"); Assert.ThrowsAny<Exception>(()=>reopened.ReadSells());
        }
    }

    internal sealed class Fixture
    {
        internal readonly ManualMarketBuyWorkflowTests.Clock Clock = new();
        internal readonly Journal Journal = new();
        internal readonly Broker Broker;
        internal readonly ManualMarketSellWorkflow Flow;
        internal readonly ManualSellSelection Selection;
        internal Fixture()
        {
            var account=new WealthsimpleTfsaAccount("account-fixture","Fixture TFSA");
            var security=new WealthsimpleCanadianSecurity("security-fixture","FIX.TO","XTSE") { SourceIdentity="identity-fixture" };
            var holding=new WealthsimpleHolding("FIX.TO","Fixture stock","EQUITY",20,new(200,"CAD"),null,null) { SecurityId="security-fixture" };
            var snapshot=new WealthsimpleTfsaSnapshot(account,Clock.Now,new(200,"CAD"),new(0,"CAD"),new(0,"CAD"),null,null,null,null,null,[holding]);
            Broker=new(new(account,security,snapshot,new(account,Clock.Now,[]),new(10.01m,10,Clock.Now,"OPEN",Clock.Now.AddHours(-1),Clock.Now.AddHours(1),Clock.Now)));
            Broker.State=State(20); Broker.Inventory=new(0,false,Clock.Now);
            Selection=ManualSellSelection.FromHolding(holding,snapshot,Broker.SessionRevision)!;
            Flow=new(Broker,Journal,Clock);
        }
        internal WealthsimpleMarketOrderState State(decimal filled,string status="FILLED")=>new(status,20,filled,new(10,"CAD"),new(filled*10,"CAD"),
            new(filled*10,"CAD"),new(0,"CAD"),new(0,"CAD"),Clock.Now,filled>0?Clock.Now:null,null,"REGULAR",Clock.Now);
    }
    internal sealed class Journal : IManualSellJournal
    {
        internal readonly List<ManualSellRecord> Sells=[];
        private readonly List<ManualBuyRecord> buys=[];
        internal int Saves, FailSave;
        public IReadOnlyList<ManualSellRecord> ReadSells()=>Sells.ToArray();
        public IReadOnlyList<ManualBuyRecord> ReadAll()=>buys.ToArray();
        public void Save(ManualBuyRecord order){buys.RemoveAll(o=>o.Intent.RequestId==order.Intent.RequestId);buys.Add(order);}
        public void SaveSell(ManualSellRecord order){if(++Saves==FailSave)throw new IOException("fixture failure");Sells.RemoveAll(o=>o.Intent.RequestId==order.Intent.RequestId);Sells.Add(order);}
        public void Dispose(){}
    }
    internal sealed class Broker(ManualSellContext context) : IManualSellBroker
    {
        public bool IsConnected {get;set;}=true;
        public long SessionRevision {get;set;}=1;
        internal ManualSellContext Context=context;
        internal ManualSellInventory? Inventory;
        internal WealthsimpleMarketOrderState? State;
        internal WealthsimpleReadStatus ReadStatus=WealthsimpleReadStatus.Success;
        internal int Submissions,ContextReads;
        internal bool ThrowSubmit,ThrowInventory;
        internal Action<WealthsimpleMarketSell>? OnSubmit;
        internal Action? OnContext;
        internal Guid LastReadId;
        public Task<WealthsimpleReadResult<ManualSellContext>> ReadManualSellContextAsync(WealthsimpleTfsaAccount account,string symbol,CancellationToken ct)
        {ContextReads++;OnContext?.Invoke();return Task.FromResult(new WealthsimpleReadResult<ManualSellContext>(ReadStatus,Context));}
        public Task<WealthsimpleMarketOrderSubmission> SubmitManualSellAsync(WealthsimpleMarketSell order,CancellationToken ct)
        {Submissions++;OnSubmit?.Invoke(order);if(ThrowSubmit)throw new IOException("private fixture detail");return Task.FromResult(new WealthsimpleMarketOrderSubmission(WealthsimpleSubmissionStatus.Acknowledged));}
        public Task<WealthsimpleReadResult<WealthsimpleMarketOrderState>> ReadManualSellOrderAsync(ManualSellIntent intent,CancellationToken ct)
        {LastReadId=intent.RequestId;return Task.FromResult(new WealthsimpleReadResult<WealthsimpleMarketOrderState>(ReadStatus,State));}
        public Task<WealthsimpleReadResult<ManualSellInventory>> ReadManualSellInventoryAsync(ManualSellIntent intent,CancellationToken ct)
        {if(ThrowInventory)throw new IOException("fixture inventory failure");return Task.FromResult(new WealthsimpleReadResult<ManualSellInventory>(ReadStatus,Inventory));}
    }
    private sealed class Protector : IManualBuyProtector
    {
        private readonly byte[] key=RandomNumberGenerator.GetBytes(32);
        public byte[] Protect(byte[] data){using var aes=new AesGcm(key,16);var nonce=RandomNumberGenerator.GetBytes(12);var result=new byte[28+data.Length];nonce.CopyTo(result,0);aes.Encrypt(nonce,data,result.AsSpan(28),result.AsSpan(12,16));return result;}
        public byte[] Unprotect(byte[] data){using var aes=new AesGcm(key,16);var result=new byte[data.Length-28];aes.Decrypt(data.AsSpan(0,12),data.AsSpan(28),data.AsSpan(12,16),result);return result;}
    }
    private sealed class TempDirectory : IDisposable
    {
        internal readonly string Path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"TraderVI-manual-sells-test-"+Guid.NewGuid().ToString("N"));
        public void Dispose(){var full=System.IO.Path.GetFullPath(Path);if(full.StartsWith(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)&&System.IO.Path.GetFileName(full).StartsWith("TraderVI-manual-sells-test-")&&Directory.Exists(full))Directory.Delete(full,true);}
    }
}
