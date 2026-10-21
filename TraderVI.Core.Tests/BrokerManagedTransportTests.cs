#nullable enable
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class BrokerManagedTransportTests
{
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task NativeBuyBoundaryRejectsCutoffBeforeAndAfterDurableJournalSave(bool crossesDuringSave)
    {
        var continuation = BrokerManagedSessionTests.SyntheticOctober21Continuation();
        var at = continuation.PurchaseExpiresAt.AddSeconds(-1);
        var f = new Fixture(at);
        var projection = f.Projection with { Risk = f.Projection.Risk with { Continuation = continuation } };
        f.Risk.State = projection.Risk;
        var account = f.Buys.Context.Account;
        var sells = new ManualMarketSellWorkflowTests.Broker(new(account, f.Buys.Context.Security,
            f.Buys.Context.Snapshot, f.Buys.Context.PendingOrders, f.Buys.Context.Quote));
        var journal = new CallbackJournal(f.Journal, () => { if (crossesDuringSave) f.Clock.Now = continuation.PurchaseExpiresAt; });
        var transport = new BrokerManagedOrderTransport(f.Buys, sells, journal, f.Safety, f.Risk, account, f.Gate, f.Clock);
        await using var prepared = await transport.PrepareAsync(f.Order, projection, default);
        if (!crossesDuringSave) f.Clock.Now = continuation.PurchaseExpiresAt;
        await Assert.ThrowsAsync<InvalidOperationException>(() => prepared.SubmitOnceAsync(default));
        Assert.Equal(0, f.Buys.Submissions);
        Assert.Equal(crossesDuringSave ? 1 : 0, f.Journal.ReadAll().Count);
        if (crossesDuringSave) Assert.Equal(f.Order.RequestId, Assert.Single(f.Journal.ReadAll()).Intent.RequestId);
    }
    private sealed class CallbackJournal(IManualSellJournal inner, Action afterSave) : IManualSellJournal
    {
        public IReadOnlyList<ManualBuyRecord> ReadAll() => inner.ReadAll();
        public IReadOnlyList<ManualSellRecord> ReadSells() => inner.ReadSells();
        public void Save(ManualBuyRecord record) { inner.Save(record); afterSave(); }
        public void SaveSell(ManualSellRecord record) => inner.SaveSell(record);
        public void Dispose() { }
    }
    [Fact]
    public async Task ExistingJournalAndSafetyLeaseCoverOneAttemptAndRejectDuplicate()
    {
        var f = new Fixture();
        f.Buys.OnSubmit = _ => { Assert.Single(f.Journal.ReadAll()); Assert.True(f.Safety.Held); Assert.Equal(0, f.Gate.CurrentCount); };
        await using (var prepared = await f.Transport.PrepareAsync(f.Order, f.Projection, default))
        {
            Assert.Equal(WealthsimpleSubmissionStatus.Acknowledged, await prepared.SubmitOnceAsync(default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => prepared.SubmitOnceAsync(default));
        }
        Assert.False(f.Safety.Held); Assert.Equal(1, f.Gate.CurrentCount);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Transport.PrepareAsync(f.Order, f.Projection, default));
        Assert.Equal(BrokerOrderCheck.OriginalOrderRecoveryRequired, BrokerOrderFailureEvidence.Read(failure)!.Check);
        Assert.Equal(1, f.Buys.Submissions);
    }

    [Theory]
    [InlineData("ask")]
    [InlineData("cost")]
    [InlineData("cash")]
    [InlineData("account")]
    [InlineData("stale")]
    [InlineData("risk")]
    public async Task ChangedBrokerOrRiskFactsStopBeforeReservationOrTransmission(string problem)
    {
        var f = new Fixture(); var context = f.Buys.Context;
        switch (problem)
        {
            case "ask": f.Buys.Context = context with { Quote = context.Quote with { Ask = 11 } }; break;
            case "cost": f.Buys.Context = context with { Snapshot = context.Snapshot with
                { Holdings = [context.Snapshot.Holdings[0] with { BookValue = new(99, "CAD") }] } }; break;
            case "cash": f.Buys.Context = context with { Snapshot = context.Snapshot with { Cash = new(999, "CAD") } }; break;
            case "account": f.Buys.Context = context with { Snapshot = context.Snapshot with { Account = new("foreign", "Other") } }; break;
            case "stale": f.Buys.Context = context with { Quote = context.Quote with { QuotedAt = f.Clock.Now.AddHours(-1) } }; break;
            default: f.Risk.State = f.Risk.State with { CapitalReviewRequired = true }; break;
        }
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Transport.PrepareAsync(f.Order, f.Projection, default));
        Assert.Equal(problem switch
        {
            "ask" => BrokerOrderCheck.AskAboveDecisionReference,
            "cost" => BrokerOrderCheck.HoldingsChanged,
            "cash" => BrokerOrderCheck.CashChanged,
            "account" => BrokerOrderCheck.ManualBuyValidation,
            "stale" => BrokerOrderCheck.ManualBuyValidation,
            _ => BrokerOrderCheck.RiskChanged
        }, BrokerOrderFailureEvidence.Read(failure)!.Check);
        Assert.Empty(f.Journal.ReadAll()); Assert.Equal(0, f.Buys.Submissions);
        Assert.Equal(1, f.Gate.CurrentCount); Assert.False(f.Safety.Held);
    }

    [Fact]
    public async Task ReplacedLoginAfterPreparationNeverTransmits()
    {
        var f = new Fixture();
        await using var prepared = await f.Transport.PrepareAsync(f.Order, f.Projection, default);
        f.Buys.SessionRevision++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => prepared.SubmitOnceAsync(default));
        Assert.Equal(0, f.Buys.Submissions); Assert.Empty(f.Journal.ReadAll());
    }

    [Fact]
    public async Task NativeTimeoutKeepsOriginalJournalIntentUnknown()
    {
        var f = new Fixture(); f.Buys.ThrowOnSubmit = true;
        await using var prepared = await f.Transport.PrepareAsync(f.Order, f.Projection, default);
        Assert.Equal(WealthsimpleSubmissionStatus.OutcomeUnknown, await prepared.SubmitOnceAsync(default));
        var saved = Assert.Single(f.Journal.ReadAll()); Assert.False(saved.IsResolved);
        Assert.Equal(f.Order.RequestId, saved.Intent.RequestId); Assert.Equal(1, f.Buys.Submissions);
    }

    private sealed class Fixture
    {
        internal readonly BrokerAccountProjection Projection;
        internal readonly ManualMarketBuyWorkflowTests.Clock Clock;
        internal readonly ManualMarketBuyWorkflowTests.FakeBroker Buys;
        internal readonly ManualMarketBuyWorkflowTests.Safety Safety = new();
        internal readonly ManualMarketSellWorkflowTests.Journal Journal = new();
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal readonly RiskStore Risk;
        internal readonly BrokerManagedOrderTransport Transport;
        internal readonly BrokerStrategyOrder Order;
        internal Fixture(DateTimeOffset? at = null)
        {
            var f = new BrokerManagedSessionTests.Fixture();
            if (at is { } now) { f.Now = now; f.Risk = f.Risk with { TradingDate = DateOnly.FromDateTime(now.UtcDateTime) }; }
            Projection = f.Project();
            Clock = new() { Now = f.Now }; var account = new WealthsimpleTfsaAccount("fixture-account", "Fixture TFSA");
            var security = new WealthsimpleCanadianSecurity("security-BBB", "BBB", "XTSE") { SourceIdentity = "fixture-identity" };
            var holding = new WealthsimpleHolding("AAA", "Fixture", "EQUITY", 10, new(100, "CAD"), new(100, "CAD"), null) { SecurityId = "security-AAA" };
            var snapshot = new WealthsimpleTfsaSnapshot(account, f.Now, new(1100, "CAD"), new(1000, "CAD"), new(900, "CAD"), null, null, null, null, null, [holding]);
            var pending = new WealthsimplePendingOrders(account, f.Now, []);
            var quote = new ManualBuyQuote(10, 9.99m, f.Now, "OPEN", f.Now.AddHours(-1), f.Now.AddHours(3), f.Now);
            Buys = new(new(account, security, snapshot, pending, quote));
            var sells = new ManualMarketSellWorkflowTests.Broker(new(account, security, snapshot, pending, quote));
            Risk = new(f.Risk, () => Assert.True(Safety.Held));
            Transport = new(Buys, sells, Journal, Safety, Risk, account, Gate, Clock);
            Order = new(Guid.NewGuid(), Guid.NewGuid(), null, security.Id, "BBB", "BUY", 3, 10, "Initial", 100, f.Now);
        }
    }
    private sealed class RiskStore(BrokerAccountRiskState state, Action read) : IBrokerAccountRiskContinuationStore
    {
        internal BrokerAccountRiskState State = state;
        public Task<BrokerAccountRiskState> ReadAsync(Guid id, CancellationToken ct) { read(); return Task.FromResult(State); }
        public Task<BrokerRiskContinuationReceipt?> FindAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrokerRiskContinuationReceipt> AppendAsync(BrokerRiskContinuationRequest r, CancellationToken ct) => throw new NotSupportedException();
    }
}
