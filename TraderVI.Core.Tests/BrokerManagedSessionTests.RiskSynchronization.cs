#nullable enable
using Core.Db;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    private sealed class ExistingRiskJournal(params BrokerRiskContinuationReceipt[] receipts) : IBrokerAccountRiskContinuationStore
    {
        internal readonly Dictionary<Guid, BrokerRiskContinuationReceipt> Entries = receipts.ToDictionary(r => r.Request.RequestId);
        internal Action? OnRead;
        public Task<BrokerRiskContinuationReceipt?> FindAsync(Guid id, CancellationToken ct)
        { OnRead?.Invoke(); return Task.FromResult(Entries.GetValueOrDefault(id)); }
        public Task<BrokerAccountRiskState> ReadAsync(Guid id, CancellationToken ct) => throw new InvalidOperationException("Only exact receipt reads are permitted in this fixture.");
        public Task<BrokerRiskContinuationReceipt> AppendAsync(BrokerRiskContinuationRequest request, CancellationToken ct) => throw new InvalidOperationException("Synchronization must not append risk events.");
    }

    private sealed record RiskSyncFacts(OngoingFacts Original, BrokerManagedSession Prior,
        BrokerAccountProjection Account, ExistingRiskJournal Journal)
    {
        internal Fixture F => Original.F;
        internal BrokerRiskStateSynchronizationWorkflow Workflow() => new(F.Store, Journal, F,
            Original.Existing.Grants, SelectedSessionCalendar(), new HostClock(F));
    }

    private static async Task<RiskSyncFacts> RiskSyncFixture()
    {
        var x = await OngoingFixture();
        var prior = await x.Workflow().ApproveAsync(x.Review);
        var calendar = SelectedSessionCalendar();
        var bought = calendar.GetSessionBounds(prior.Account.Risk.TradingDate).OpenUtc.AddMinutes(5);
        var order = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), null, "owned-security", "FIX", "BUY", 2, 10,
            "Initial", null, bought, WealthsimpleSubmissionStatus.Acknowledged);
        var fact = new BrokerOrderFact(order.ExternalId, order.SecurityId, "FIX.TO", "BUY", "POSTED", "CAD", "EQUITY",
            2, 2, 20, 20, 0, 0, bought, bought.AddSeconds(1));
        order = order with { Latest = fact, Reconciled = true };
        var position = new BrokerPositionCycle(Guid.NewGuid(), order.SecurityId, fact.Symbol, 2, 20,
            bought.AddSeconds(1), order.ExternalId, null);
        var invested = prior.Account.Import.Plan.Observation with
        {
            Cash = prior.OngoingManagement!.CashAnchor.Cash - 20,
            Orders = prior.OngoingManagement.CashAnchor.Orders.Append(fact).ToArray(),
            Holdings = [new(order.SecurityId, fact.Symbol, 2, 2, 20, 20, bought.AddSeconds(1), 0)],
            Instruments = [new(order.SecurityId, fact.Symbol, "XTSE", "CAD", "EQUITY")]
        };
        prior = prior with { Orders = [order], Protection = [BrokerLiveProtection.Transfer(prior.Strategy, position, null, bought)],
            Account = prior.Account with { Positions = [position], Import = prior.Account.Import with {
                Plan = prior.Account.Import.Plan with { Observation = invested } },
                Account = prior.Account.Account with { Holdings = [new(position.PositionId, position.Symbol, 2, 20, prior.Account.Risk.TradingDate)] } } };
        var risk = prior.Account.Risk;
        var receipts = new List<BrokerRiskContinuationReceipt>();
        foreach (bool opening in new[] { false, true })
        {
            var date = opening ? calendar.GetNextSession(risk.TradingDate) : risk.TradingDate;
            var bounds = calendar.GetSessionBounds(date);
            var at = opening ? bounds.OpenUtc : bounds.CloseUtc;
            var request = new BrokerRiskContinuationRequest(Guid.NewGuid(), risk.BaselineId, risk.RevisionId,
                opening ? BrokerRiskContinuationKind.Opening : BrokerRiskContinuationKind.Closing,
                new(Guid.NewGuid(), risk.BindingId, date, opening ? BrokerRiskBoundaryKind.Opening : BrokerRiskBoundaryKind.Closing,
                    opening ? BrokerAccountSessionValuation.LiveOpening : BrokerAccountSessionValuation.SessionClosing,
                    at, at.AddMinutes(1), risk.SessionReference.Value, new string('A', 64)),
                new(Guid.NewGuid(), risk.BindingId, risk.Through, at, at.AddMinutes(1), new string('B', 64)), null);
            risk = BrokerAccountRiskContinuation.Apply(risk, request, calendar, at.AddMinutes(1));
            receipts.Add(new(request, risk, at.AddMinutes(1)));
        }
        x.F.Now = calendar.GetSessionBounds(risk.TradingDate).OpenUtc.AddMinutes(10);
        var observed = prior.Account.Import.Plan.Observation with { SnapshotAt = x.F.Now, CheckedAt = x.F.Now };
        var account = prior.Account with { Risk = risk,
            Import = prior.Account.Import with { Plan = prior.Account.Import.Plan with { Observation = observed } },
            Account = prior.Account.Account with { TradingDate = risk.TradingDate, ReportedUtc = x.F.Now.UtcDateTime } };
        prior = prior with { NewBuysStopped = true };
        x.F.Store.State = prior; x.F.TransformAccount = _ => account;
        return new(x, prior, account, new(receipts.ToArray()));
    }

    [Fact]
    public async Task RiskSynchronizationReplaysExistingBoundariesThenAllowsSeparateBuildReview()
    {
        var x = await RiskSyncFixture();
        Assert.DoesNotContain("RiskSynchronization", System.Text.Json.JsonSerializer.Serialize(x.Prior));
        var current = x.Original.Existing.Current with { PreparedUtc = x.F.Now.UtcDateTime,
            Strategy = x.Prior.Strategy with { Strategy = x.Prior.Strategy.Strategy with { ImplementationIdentity = "new-build" } } };
        Assert.Throws<InvalidOperationException>(() => BrokerImplementationReviewWorkflow.Prepare(x.Prior, current,
            x.F.Now, x.Account, true, "new-build", "fixture", SelectedSessionCalendar(), x.F.Now));
        var saved = await x.Workflow().SynchronizeAsync(x.Prior.BindingId);
        Assert.Equal("RiskStateSynchronized", saved.Event);
        Assert.Equal(x.Account.Risk, saved.Account.Risk);
        Assert.Equal(2, saved.RiskSynchronization!.Receipts.Length);
        Assert.Equal(x.Prior.Memory, saved.Memory); Assert.Equal(x.Prior.Protection, saved.Protection);
        Assert.Single(saved.Protection); Assert.Single(saved.Orders);
        Assert.Equal(x.Prior.Orders, saved.Orders); Assert.Equal(x.Prior.OngoingManagement, saved.OngoingManagement);
        Assert.Equal(x.Prior.Phase, saved.Phase); Assert.True(saved.NewBuysStopped);
        Assert.Null(saved.ImplementationReview); Assert.Null(x.Original.Existing.Grants.Value);
        Assert.Equal(0, x.F.Transport.Sent);
        BrokerManagedSessionRepository.ValidateNext(x.Prior, saved, SelectedSessionCalendar());
        Assert.NotNull(BrokerImplementationReviewWorkflow.Prepare(saved, current, x.F.Now, x.Account, true,
            "new-build", "fixture", SelectedSessionCalendar(), x.F.Now));
        x.F.Store.Restart();
        Assert.Equal(saved.RevisionId, (await x.Workflow().SynchronizeAsync(x.Prior.BindingId)).RevisionId);
    }

    [Fact]
    public async Task RiskSynchronizationLostAcknowledgementRecoversWithoutRepeatingRiskEvents()
    {
        var x = await RiskSyncFixture(); x.F.Store.LoseResponseAtEvent = "RiskStateSynchronized";
        await Assert.ThrowsAnyAsync<Exception>(() => x.Workflow().SynchronizeAsync(x.Prior.BindingId));
        var saved = x.F.Store.State!; x.F.Store.Restart();
        Assert.Equal(saved.RevisionId, (await x.Workflow().SynchronizeAsync(x.Prior.BindingId)).RevisionId);
        Assert.Equal(0, x.F.Transport.Sent); Assert.Null(x.Original.Existing.Grants.Value);
    }

    [Theory]
    [InlineData("missing")][InlineData("corrupt")][InlineData("fork")][InlineData("host")]
    [InlineData("host-during-read")][InlineData("cash")][InlineData("stale")]
    public async Task RiskSynchronizationRejectsMissingProofAndChangedOperationalState(string kind)
    {
        var x = await RiskSyncFixture();
        var id = x.Account.Risk.RevisionId!.Value;
        var receipt = x.Journal.Entries[id];
        if (kind == "missing") x.Journal.Entries.Remove(id);
        if (kind == "corrupt") x.Journal.Entries[id] = receipt with { State = receipt.State with { DailyLossHold = true } };
        if (kind == "fork") x.Journal.Entries[id] = receipt with { Request = receipt.Request with { PreviousRevisionId = Guid.NewGuid() } };
        void Enable() => x.Original.Existing.Grants.Value = new(x.Prior.BindingId, x.Prior.SessionId,
            x.Prior.Strategy.Fingerprint, x.F.ImplementationIdentity, x.F.Now, "fixture", true);
        if (kind == "host") Enable();
        if (kind == "host-during-read") x.Journal.OnRead = Enable;
        if (kind == "cash") x.F.TransformAccount = _ => x.Account with { Import = x.Account.Import with {
            Plan = x.Account.Import.Plan with { Observation = x.Account.Import.Plan.Observation with { Cash = 1 } } } };
        if (kind == "stale") x.F.TransformAccount = _ => x.Account with { Account = x.Account.Account with { ReportedUtc = x.F.Now.AddMinutes(-2).UtcDateTime } };
        await Assert.ThrowsAnyAsync<Exception>(() => x.Workflow().SynchronizeAsync(x.Prior.BindingId));
        Assert.Equal(x.Prior.RevisionId, x.F.Store.State!.RevisionId); Assert.Equal(0, x.F.Transport.Sent);
    }

    [Theory]
    [InlineData("memory")][InlineData("permission")][InlineData("buy-control")][InlineData("risk")][InlineData("proof")]
    public async Task RiskSynchronizationArchiveRejectsHiddenChanges(string kind)
    {
        var x = await RiskSyncFixture(); var saved = await x.Workflow().SynchronizeAsync(x.Prior.BindingId);
        var changed = kind switch
        {
            "memory" => saved with { Memory = saved.Memory with { HighestClosingValue = 1 } },
            "permission" => saved with { OngoingManagement = saved.OngoingManagement! with { Review = saved.OngoingManagement.Review with { PurchaseCeiling = 999999 } } },
            "buy-control" => saved with { NewBuysStopped = false },
            "risk" => saved with { Account = saved.Account with { Risk = saved.Account.Risk with { Through = saved.Account.Risk.Through.AddMinutes(1) } } },
            _ => saved with { RiskSynchronization = saved.RiskSynchronization! with { Receipts = saved.RiskSynchronization.Receipts.RemoveAt(0) } }
        };
        Assert.ThrowsAny<Exception>(() => BrokerManagedSessionRepository.ValidateNext(x.Prior, changed, SelectedSessionCalendar()));
    }
}
