#nullable enable
using Core.Db;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    private sealed record OngoingFacts(SelectedSessionFacts Existing, BrokerManagedSession Prior,
        BrokerOngoingManagement Review)
    {
        internal Fixture F => Existing.F;
        internal BrokerOngoingManagementWorkflow Workflow(Func<CancellationToken, Task>? readiness = null) => new(
            F.Store, Existing.Risks, F, Existing.Grants, SelectedSessionCalendar(), readiness ?? (_ => Task.CompletedTask), new HostClock(F));
        internal BrokerOngoingRiskEvidenceSource Source(BrokerOngoingManagement agreement,
            Func<ImmutableArray<string>, DateTime, CancellationToken, Task<ImmutableArray<BrokerRiskHoldingBar>>>? bars = null) =>
            new(agreement, SelectedSessionCalendar(), bars ?? ((_, _, _) => Task.FromResult(ImmutableArray<BrokerRiskHoldingBar>.Empty)), new HostClock(F));
    }
    private static async Task<OngoingFacts> OngoingFixture()
    {
        var x = await SelectedSessionFixture(); var prior = await x.Workflow().ApproveAsync(x.Approval());
        var review = BrokerOngoingManagement.Prepare(prior, prior.Strategy, x.F.Now, "fixture", true, true);
        return new(x, prior, review);
    }

    [Fact]
    public async Task OngoingRequiresExplicitAgreementAndPreservesFinancialAuthority()
    {
        var x = await OngoingFixture();
        Assert.Throws<InvalidOperationException>(() => (x.Review with { PauseBeforeOutsideChanges = false }).RequirePrior(x.Prior));
        Assert.Throws<InvalidOperationException>(() => (x.Review with { NoOutsideChangesConfirmed = false }).RequirePrior(x.Prior));
        var saved = await x.Workflow().ApproveAsync(x.Review);
        Assert.NotNull(saved.OngoingManagement); Assert.Null(saved.EffectiveReview.NoTransferScope);
        Assert.Equal(x.Prior.EffectiveReview.PurchaseCeiling, saved.EffectiveReview.PurchaseCeiling);
        Assert.Equal(x.Prior.Account.Risk.SessionReference, saved.Account.Risk.SessionReference);
        Assert.Equal(x.Prior.Account.Risk.DrawdownReference, saved.Account.Risk.DrawdownReference);
        Assert.Equal(x.Prior.Account.Risk.Through, saved.Account.Risk.Through);
        Assert.Equal(x.Prior.Approval.Fingerprint, saved.Approval.Fingerprint);
        Assert.Equal(x.Prior.SessionContinuation!.Fingerprint, saved.SessionContinuation!.Fingerprint);
        Assert.True(saved.RequiresFullFillReview); Assert.Null(x.Existing.Grants.Value);
        Assert.Equal(0, x.F.Transport.Sent);
        BrokerManagedSessionRepository.ValidateNext(x.Prior, saved, SelectedSessionCalendar());
        x.F.Store.Restart();
        var recovered = await x.Workflow().ApproveAsync(x.Review);
        Assert.Equal(saved.OngoingManagement!.Fingerprint, recovered.OngoingManagement!.Fingerprint);
    }

    [Theory]
    [InlineData("stale")][InlineData("configuration")][InlineData("host")][InlineData("allowance")][InlineData("pause")]
    public async Task OngoingApprovalFailsClosedWithoutChangingExistingPermission(string reason)
    {
        var x = await OngoingFixture(); var review = x.Review;
        if (reason == "stale") x.F.Now = x.F.Now.AddMinutes(3);
        if (reason == "host") x.Existing.Grants.Value = new(x.Prior.BindingId, x.Prior.SessionId, x.Prior.Strategy.Fingerprint,
            x.F.ImplementationIdentity, x.F.Now, "fixture", true);
        if (reason == "allowance") review = review with { Review = review.Review with { PurchaseCeiling = review.Review.PurchaseCeiling + 1 } };
        if (reason == "pause") x.F.Store.State = x.Prior with { NewBuysStopped = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => x.Workflow(reason == "configuration"
            ? _ => throw new InvalidOperationException("changed configuration") : null).ApproveAsync(review));
        Assert.Null(x.F.Store.State!.OngoingManagement); Assert.Equal(0, x.F.Transport.Sent);
    }

    [Fact]
    public async Task CashOnlyWeekendContinuationRecordsExactBoundariesWithoutNewApprovalOrRefilledAllowance()
    {
        var x = await OngoingFixture(); var saved = await x.Workflow().ApproveAsync(x.Review);
        var risk = saved.Account.Risk; var capital = risk.DrawdownReference;
        x.F.Now = new(2026, 10, 26, 14, 0, 0, TimeSpan.Zero);
        var current = saved.Account.Import.Plan.Observation with { SnapshotAt = x.F.Now, CheckedAt = x.F.Now };
        var import = saved.Account.Import with { Plan = saved.Account.Import.Plan with { Observation = current } };
        var evidence = await x.Source(saved.OngoingManagement!).ReadAsync(import, risk, [], default);
        Assert.Empty(evidence.Blocks); Assert.Equal(2, evidence.Continuations.Length);
        foreach (var request in evidence.Continuations)
            risk = BrokerAccountRiskContinuation.Apply(risk, request, SelectedSessionCalendar(), x.F.Now);
        Assert.Equal(new DateOnly(2026,10,26), risk.TradingDate);
        Assert.Equal(new DateOnly(2026,10,23), risk.LastCompletedSession);
        Assert.True(risk.DrawdownReference.Value >= capital.Value);
        Assert.Equal(saved.Account.Risk.OngoingAuthorization, risk.OngoingAuthorization);
        Assert.Equal(saved.EffectiveReview.PurchaseCeiling, x.F.Store.State!.EffectiveReview.PurchaseCeiling);
        var again = await x.Source(saved.OngoingManagement!).ReadAsync(import, risk, [], default);
        Assert.Empty(again.Continuations); Assert.Empty(again.Blocks);
    }

    [Theory]
    [InlineData("cash")][InlineData("stale")][InlineData("outside-order")][InlineData("missing-order")]
    public async Task OngoingCashEvidenceNeverTreatsUnexplainedOrStaleBooksAsContinuation(string reason)
    {
        var x = await OngoingFixture(); var saved = await x.Workflow().ApproveAsync(x.Review);
        x.F.Now = x.F.Now.AddDays(3);
        var current = saved.Account.Import.Plan.Observation with { SnapshotAt = x.F.Now, CheckedAt = x.F.Now };
        var owned = ImmutableArray<BrokerStrategyOrder>.Empty;
        if (reason == "cash") current = current with { Cash = current.Cash + 1 };
        if (reason == "stale") current = current with { SnapshotAt = x.F.Now.AddSeconds(-31) };
        if (reason == "outside-order") current = current with { Orders = current.Orders.Append(current.Orders[0] with { ExternalId = "outside" }).ToArray() };
        if (reason == "missing-order") owned = [new(Guid.NewGuid(), Guid.NewGuid(), null, "security", "FIX", "BUY", 1, 10, "Entry", null, x.F.Now)];
        var import = saved.Account.Import with { Plan = saved.Account.Import.Plan with { Observation = current } };
        var evidence = await x.Source(saved.OngoingManagement!).ReadAsync(import, saved.Account.Risk, owned, default);
        Assert.NotEmpty(evidence.Blocks); Assert.Empty(evidence.Continuations); Assert.Null(evidence.Coverage);
    }

    [Fact]
    public async Task OngoingGrantRestoresAfterShutdownButDeliberatePauseSurvives()
    {
        var x = await OngoingFixture(); var saved = await x.Workflow().ApproveAsync(x.Review);
        int cycles = 0;
        BrokerExecutionHost Create() => new(x.Existing.Grants, new BrokerManagedSessionControls(x.F.Store),
            (state, _) => { cycles++; return Task.FromResult(state); }, SelectedSessionCalendar(), x.F.ImplementationIdentity, new HostClock(x.F));
        var host = Create(); await host.EnableAsync(saved.BindingId, saved.SessionId, saved.Strategy.Fingerprint, "fixture");
        Assert.True(x.Existing.Grants.Value!.RestoreAfterRestart);
        await host.ShutdownAsync(); x.F.Store.Restart();
        x.F.Now = new(2026,10,26,14,0,0,TimeSpan.Zero);
        await Create().PollAsync(true); Assert.Equal(1, cycles);
        await x.F.Workflow().StopNewBuysAsync(saved.BindingId);
        await Create().PollAsync(true); Assert.True(x.F.Store.State!.NewBuysStopped);
        await Create().DisableAsync(); await Create().PollAsync(true); Assert.Equal(2, cycles);
    }

    [Theory]
    [InlineData(true)][InlineData(false)]
    public async Task InvestedRolloverRequiresExactCloseAndOpenWithoutReplacingMissingEvidence(bool complete)
    {
        var x = await OngoingFixture(); var saved = await x.Workflow().ApproveAsync(x.Review);
        var boughtAt = new DateTimeOffset(2026,10,23,15,0,0,TimeSpan.Zero);
        var order = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), null, "fixture-security", "FIX", "BUY", 2, 10,
            "Entry", null, boughtAt.AddSeconds(-1), global::Core.WSTrade.Orders.WealthsimpleSubmissionStatus.Acknowledged);
        var fact = new BrokerOrderFact(order.ExternalId, order.SecurityId, order.Symbol, "BUY", "FILLED", "CAD", "EQUITY",
            2, 2, 20, 20, 0, 0, order.CreatedAt, boughtAt);
        x.F.Now = new(2026,10,26,14,0,0,TimeSpan.Zero);
        var current = saved.Account.Import.Plan.Observation with { SnapshotAt = x.F.Now, CheckedAt = x.F.Now,
            Cash = saved.OngoingManagement!.CashAnchor.Cash - 20,
            Orders = saved.OngoingManagement.CashAnchor.Orders.Append(fact).ToArray(),
            Holdings = [new(order.SecurityId, "FIX", 2, 2, 20, 20, boughtAt, 0)] };
        var import = saved.Account.Import with { Plan = saved.Account.Import.Plan with { Observation = current } };
        Task<ImmutableArray<BrokerRiskHoldingBar>> Bars(ImmutableArray<string> symbols, DateTime start, CancellationToken ct) =>
            Task.FromResult(complete ? ImmutableArray.Create(new BrokerRiskHoldingBar("FIX",
                new(Guid.NewGuid(), start, start.AddMinutes(5), 5, 11, 12, 10, 12))) : ImmutableArray<BrokerRiskHoldingBar>.Empty);
        var evidence = await x.Source(saved.OngoingManagement, Bars).ReadAsync(import, saved.Account.Risk, [order], default);
        if (!complete) { Assert.NotEmpty(evidence.Blocks); Assert.Empty(evidence.Continuations); return; }
        Assert.Empty(evidence.Blocks); Assert.Equal(2, evidence.Continuations.Length);
        Assert.Equal(current.Cash + 24, evidence.Continuations[0].Boundary!.Value);
        Assert.Equal(current.Cash + 22, evidence.Continuations[1].Boundary!.Value);
        Assert.All(evidence.Continuations, r => Assert.Equal(x.F.Now, r.Boundary!.ObservedAt));
    }

    [Theory]
    [InlineData("partial")][InlineData("unknown")][InlineData("historical-change")]
    public async Task OngoingReconciliationPreservesUncertainOrderAndHistoryBlocks(string reason)
    {
        var x = await OngoingFixture(); var saved = await x.Workflow().ApproveAsync(x.Review);
        x.F.Now = x.F.Now.AddDays(3);
        var current = saved.Account.Import.Plan.Observation with { SnapshotAt = x.F.Now, CheckedAt = x.F.Now };
        if (reason == "historical-change") current = current with { Orders = current.Orders.Skip(1).ToArray() };
        else
        {
            var request = Guid.NewGuid();
            var fact = new BrokerOrderFact("order-" + request.ToString("D"), "fixture", "FIX", "BUY",
                reason == "partial" ? "FILLED" : "PENDING", "CAD", "EQUITY", 2, 1, 10, 10, 0, 0, x.F.Now.AddSeconds(-2), x.F.Now.AddSeconds(-1));
            current = current with { Orders = current.Orders.Append(fact).ToArray() };
        }
        var import = saved.Account.Import with { Plan = saved.Account.Import.Plan with { Observation = current } };
        var evidence = await x.Source(saved.OngoingManagement!).ReadAsync(import, saved.Account.Risk, [], default);
        Assert.NotEmpty(evidence.Blocks); Assert.Null(evidence.Coverage); Assert.Empty(evidence.Continuations);
        Assert.Equal(saved.Account.Risk, x.F.Store.State!.Account.Risk);
    }
}
