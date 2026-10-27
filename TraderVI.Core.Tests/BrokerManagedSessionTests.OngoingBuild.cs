#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    [Theory]
    [InlineData("valid")][InlineData("stale")][InlineData("cash")][InlineData("pending")]
    [InlineData("protection")][InlineData("risk")][InlineData("source")]
    public async Task OngoingInvestedBuildReviewPreservesAuthorityAndRequiresUnchangedCheckedBook(string scenario)
    {
        var x = await OngoingFixture(); var prior = await x.Workflow().ApproveAsync(x.Review);
        x.F.Now = new(2026,10,23,14,0,0,TimeSpan.Zero);
        var bought = x.F.Now.AddMinutes(-1);
        var order = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), null, "owned-security", "FIX", "BUY", 2, 10, "Initial", null,
            bought, WealthsimpleSubmissionStatus.Acknowledged);
        var fact = new BrokerOrderFact(order.ExternalId, order.SecurityId, "FIX.TO", "BUY", "POSTED", "CAD", "EQUITY", 2, 2, 20, 20, 0, 0, bought, bought.AddSeconds(1));
        order = order with { Latest = fact, Reconciled = true };
        var position = new BrokerPositionCycle(Guid.NewGuid(), order.SecurityId, fact.Symbol, 2, 20, bought.AddSeconds(1), order.ExternalId, null);
        var observed = prior.Account.Import.Plan.Observation with { SnapshotAt = x.F.Now, CheckedAt = x.F.Now,
            Cash = prior.OngoingManagement!.CashAnchor.Cash - 20,
            Orders = prior.OngoingManagement.CashAnchor.Orders.Append(fact).ToArray(),
            Holdings = [new(order.SecurityId, fact.Symbol, 2, 2, 20, 20, bought.AddSeconds(1), 0)],
            Instruments = [new(order.SecurityId, fact.Symbol, "XTSE", "CAD", "EQUITY")] };
        var account = prior.Account with { Positions = [position],
            Import = prior.Account.Import with { Plan = prior.Account.Import.Plan with { Observation = observed } },
            Account = prior.Account.Account with { ReportedUtc = x.F.Now.UtcDateTime,
                Holdings = [new(position.PositionId, position.Symbol, 2, 20, new(2026,10,23))] } };
        prior = prior with { Account = account, Orders = [order], NewBuysStopped = true,
            Protection = [BrokerLiveProtection.Transfer(prior.Strategy, position, null, bought)] };
        if (scenario == "pending") prior = prior with { Orders = [order with { Reconciled = false }] };
        if (scenario == "protection") prior = prior with { Protection = [] };
        if (scenario == "risk") prior = prior with { Memory = prior.Memory with { DailyLossHold = true } };
        x.F.Store.State = prior;
        var fresh = account;
        if (scenario == "stale") fresh = account with { Account = account.Account with { ReportedUtc = x.F.Now.AddMinutes(-1).UtcDateTime } };
        if (scenario == "cash") fresh = account with { Import = account.Import with { Plan = account.Import.Plan with { Observation = observed with { Cash = observed.Cash + 1 } } } };
        x.F.TransformAccount = _ => fresh;
        x.F.RunningImplementation = "repaired-build";
        var current = x.Existing.Current with { PreparedUtc = x.F.Now.UtcDateTime,
            Strategy = prior.Strategy with { Strategy = prior.Strategy.Strategy with { ImplementationIdentity = x.F.ImplementationIdentity } } };
        BrokerImplementationReviewProposal Prepare() => BrokerImplementationReviewWorkflow.Prepare(prior, current,
            scenario == "source" ? x.F.Now.AddMinutes(-3) : x.F.Now, fresh, true, x.F.ImplementationIdentity, "fixture", SelectedSessionCalendar(), x.F.Now);
        if (scenario != "valid") { Assert.Throws<InvalidOperationException>(() => Prepare()); Assert.Equal(0, x.F.Transport.Sent); return; }
        var proposal = Prepare();
        var workflow = new BrokerImplementationReviewWorkflow(x.F.Store, x.F, x.Existing.Grants, SelectedSessionCalendar(), new HostClock(x.F));
        var saved = await workflow.ApproveAsync(proposal);
        Assert.Equal(x.F.ImplementationIdentity, saved.Strategy.Strategy.ImplementationIdentity);
        Assert.Equal(prior.OngoingManagement, saved.OngoingManagement);
        Assert.Equal(prior.Orders, saved.Orders); Assert.Equal(prior.Protection, saved.Protection);
        Assert.Equal(prior.Memory, saved.Memory); Assert.True(saved.NewBuysStopped);
        Assert.Equal(prior.CommittedPurchaseCost, saved.CommittedPurchaseCost);
        Assert.Equal(prior.Account.Risk, saved.Account.Risk);
        var diagnostic = EngineBrokerDiagnostics.From(saved, true).Protection!;
        Assert.True(diagnostic.NewBuysStopped);
        Assert.Equal(1, diagnostic.OwnedOrders); Assert.Equal(1, diagnostic.ReconciledOrders);
        Assert.True(Assert.Single(diagnostic.Holdings).SavedProtectionMatches);
        Assert.Null(Assert.Single(diagnostic.Holdings).LastUsableBidUtc);
        Assert.Null(x.Existing.Grants.Value); Assert.Equal(0, x.F.Transport.Sent);
        BrokerManagedSessionRepository.ValidateNext(prior, saved, SelectedSessionCalendar());
        x.F.Store.Restart(); Assert.Equal(saved.Strategy.Fingerprint, x.F.Store.State!.Strategy.Fingerprint);
        Assert.Equal(saved.RevisionId, (await workflow.ApproveAsync(proposal)).RevisionId);
        var altered = saved with { ImplementationReview = saved.ImplementationReview! with { PermissionRequestId = Guid.NewGuid() } };
        Assert.Equal(prior.Strategy.Strategy.ImplementationIdentity, altered.Strategy.Strategy.ImplementationIdentity);
        Assert.ThrowsAny<Exception>(() => BrokerManagedSessionRepository.ValidateNext(prior, altered, SelectedSessionCalendar()));
    }
}
