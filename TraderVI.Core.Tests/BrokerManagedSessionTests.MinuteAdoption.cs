#nullable enable
using Core.Db;
using Core.Runtime;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    [Theory]
    [InlineData(false, false)][InlineData(true, false)][InlineData(true, true)]
    public async Task MinuteAdoptionPreservesInvestedOngoingAuthorityPauseAndProtectionAcrossRestart(bool invested, bool paused)
    {
        var x = await MinuteAdoptionFixture(invested, paused);
        var proposal = x.Prepare(); var saved = await x.Workflow().ApproveAsync(proposal);
        Assert.NotNull(saved.MinuteAdoption); Assert.NotNull(saved.Strategy.Strategy.MinuteEntry);
        Assert.Equal(x.Prior.OngoingManagement, saved.OngoingManagement);
        Assert.Equal(x.Prior.Orders, saved.Orders); Assert.Equal(x.Prior.Protection, saved.Protection);
        Assert.Equal(x.Prior.Memory, saved.Memory); Assert.Equal(paused, saved.NewBuysStopped);
        Assert.Equal(x.Prior.CommittedPurchaseCost, saved.CommittedPurchaseCost);
        Assert.Equal(x.Prior.EffectiveReview.PurchaseCeiling, saved.EffectiveReview.PurchaseCeiling);
        Assert.Equal(x.Prior.Account.Risk, saved.Account.Risk);
        Assert.Equal(x.Prior.StrategyEffectiveUtc, saved.StrategyEffectiveUtc);
        Assert.Equal(x.F.ImplementationIdentity, saved.Strategy.Strategy.ImplementationIdentity);
        Assert.Equal(0, x.F.Transport.Sent);
        x.F.Store.Restart();
        Assert.Equal(saved.Strategy.Fingerprint, x.F.Store.State!.Strategy.Fingerprint);
        Assert.Equal(saved.RevisionId, (await x.Workflow().ApproveAsync(proposal)).RevisionId);
        if (invested) Assert.True(Assert.Single(EngineBrokerDiagnostics.From(saved, true).Protection!.Holdings).SavedProtectionMatches);
    }

    [Fact]
    public async Task LostMinuteApprovalAcknowledgementRecoversOriginalApprovalWithoutOrderOrAllowanceReset()
    {
        var x = await MinuteAdoptionFixture(true, true); var proposal = x.Prepare();
        x.F.Store.LoseResponseAtEvent = "MinuteStrategyApproved";
        await Assert.ThrowsAsync<IOExceptionForTest>(() => x.Workflow().ApproveAsync(proposal));
        x.F.Store.Restart(); var recovered = await x.Workflow().ApproveAsync(proposal);
        Assert.Equal(proposal.Review.ReviewId, recovered.RevisionId);
        Assert.Equal(BrokerAccountImport.Hash(x.Prior.Orders), BrokerAccountImport.Hash(recovered.Orders));
        Assert.Equal(x.Prior.CommittedPurchaseCost, recovered.CommittedPurchaseCost);
        Assert.Equal(x.Prior.EffectiveReview.PurchaseCeiling, recovered.EffectiveReview.PurchaseCeiling);
        Assert.True(recovered.NewBuysStopped);
        Assert.Equal(0, x.F.Transport.Sent);
    }

    [Theory]
    [InlineData("risk")][InlineData("pending")][InlineData("protection")][InlineData("stale")]
    [InlineData("strategy")][InlineData("qualification")][InlineData("cash")]
    public async Task MinuteReviewRejectsChangedFinancialStrategyOrSourceEvidence(string problem)
    {
        var x = await MinuteAdoptionFixture(true, true);
        var prior = x.Prior; var current = x.Current; var account = prior.Account; var qualification = x.Qualification;
        if (problem == "risk") prior = prior with { Memory = prior.Memory with { DailyLossHold = true } };
        if (problem == "pending") prior = prior with { Orders = [prior.Orders[0] with { Reconciled = false }] };
        if (problem == "protection") prior = prior with { Protection = [] };
        if (problem == "stale") current = current with { PreparedUtc = x.F.Now.AddMinutes(-3).UtcDateTime };
        if (problem == "strategy") current = current with { Strategy = current.Strategy with { Strategy = current.Strategy.Strategy with { MaximumHoldings = 2 } } };
        if (problem == "qualification") qualification = qualification with { EvidenceHash = "missing" };
        if (problem == "cash") account = account with { Import = account.Import with { Plan = account.Import.Plan with
            { Observation = account.Import.Plan.Observation with { Cash = account.Import.Plan.Observation.Cash + 1 } } } };
        Assert.ThrowsAny<Exception>(() => BrokerMinuteStrategyAdoptionWorkflow.Prepare(prior, current, account, true,
            qualification, x.F.ImplementationIdentity, "fixture", SelectedSessionCalendar(), x.F.Now));
        Assert.Equal(0, x.F.Transport.Sent);
    }

    [Theory]
    [InlineData("pause")][InlineData("risk")][InlineData("protection")][InlineData("orders")][InlineData("permission")]
    public async Task MinuteReviewCannotHideUnrelatedStateChangesInItsDurableAppend(string problem)
    {
        var x = await MinuteAdoptionFixture(true, true); var saved = await x.Workflow().ApproveAsync(x.Prepare());
        var changed = problem switch
        {
            "pause" => saved with { NewBuysStopped = false },
            "risk" => saved with { Memory = saved.Memory with { HighestClosingValue = 100000 } },
            "protection" => saved with { Protection = [] },
            "orders" => saved with { Orders = [] },
            _ => saved with { MinuteAdoption = saved.MinuteAdoption! with { OngoingPermissionId = Guid.NewGuid() } }
        };
        Assert.ThrowsAny<Exception>(() => BrokerManagedSessionRepository.ValidateNext(x.Prior, changed, SelectedSessionCalendar()));
    }

    [Fact]
    public async Task LaterBuildReviewRetainsMinuteDefinitionAndCannotSilentlyReturnToFiveMinutes()
    {
        var x = await MinuteAdoptionFixture(true, true); var saved = await x.Workflow().ApproveAsync(x.Prepare());
        x.F.RunningImplementation = "later-minute-build";
        var current = x.Current with { Strategy = saved.Strategy with
            { Strategy = saved.Strategy.Strategy with { ImplementationIdentity = x.F.ImplementationIdentity } } };
        var proposal = BrokerImplementationReviewWorkflow.Prepare(saved, current, x.F.Now, saved.Account, true,
            x.F.ImplementationIdentity, "fixture", SelectedSessionCalendar(), x.F.Now.AddMilliseconds(1));
        x.F.Now = x.F.Now.AddMilliseconds(1);
        var next = await new BrokerImplementationReviewWorkflow(x.F.Store, x.F, x.Original.Existing.Grants,
            SelectedSessionCalendar(), new HostClock(x.F)).ApproveAsync(proposal);
        Assert.Equal("later-minute-build", next.Strategy.Strategy.ImplementationIdentity);
        Assert.NotNull(next.Strategy.Strategy.MinuteEntry); Assert.Equal(saved.MinuteAdoption, next.MinuteAdoption);
    }

    private sealed record MinuteAdoptionFacts(OngoingFacts Original, BrokerManagedSession Prior,
        BrokerStrategyReview Current, BrokerMinuteSourceQualification Qualification)
    {
        public Fixture F => Original.F;
        public BrokerMinuteAdoptionProposal Prepare() => BrokerMinuteStrategyAdoptionWorkflow.Prepare(Prior, Current,
            Prior.Account, true, Qualification, F.ImplementationIdentity, "fixture", SelectedSessionCalendar(), F.Now);
        public BrokerMinuteStrategyAdoptionWorkflow Workflow() => new(F.Store, F, Original.Existing.Grants, SelectedSessionCalendar(),
            _ => Task.FromResult(Qualification), _ => Task.FromResult(Current), new HostClock(F));
    }
    private static async Task<MinuteAdoptionFacts> MinuteAdoptionFixture(bool invested, bool paused)
    {
        var x = await OngoingFixture(); var prior = await x.Workflow().ApproveAsync(x.Review);
        x.F.Now = new(2026, 10, 23, 14, 0, 0, TimeSpan.Zero);
        var observed = prior.Account.Import.Plan.Observation with { SnapshotAt = x.F.Now, CheckedAt = x.F.Now };
        var account = prior.Account with { Import = prior.Account.Import with { Plan = prior.Account.Import.Plan with { Observation = observed } },
            Account = prior.Account.Account with { ReportedUtc = x.F.Now.UtcDateTime } };
        if (invested)
        {
            var bought = x.F.Now.AddMinutes(-1);
            var order = new BrokerStrategyOrder(Guid.NewGuid(), Guid.NewGuid(), null, "owned-security", "FIX", "BUY", 2, 10, "Initial", null,
                bought, WealthsimpleSubmissionStatus.Acknowledged);
            var fact = new BrokerOrderFact(order.ExternalId, order.SecurityId, "FIX.TO", "BUY", "POSTED", "CAD", "EQUITY", 2, 2, 20, 20, 0, 0, bought, bought.AddSeconds(1));
            order = order with { Latest = fact, Reconciled = true };
            var position = new BrokerPositionCycle(Guid.NewGuid(), order.SecurityId, fact.Symbol, 2, 20, bought.AddSeconds(1), order.ExternalId, null);
            observed = observed with { Cash = prior.OngoingManagement!.CashAnchor.Cash - 20,
                Orders = prior.OngoingManagement.CashAnchor.Orders.Append(fact).ToArray(),
                Holdings = [new(order.SecurityId, fact.Symbol, 2, 2, 20, 20, bought.AddSeconds(1), 0)],
                Instruments = [new(order.SecurityId, fact.Symbol, "XTSE", "CAD", "EQUITY")] };
            account = account with { Positions = [position], Import = account.Import with { Plan = account.Import.Plan with { Observation = observed } },
                Account = account.Account with { Holdings = [new(position.PositionId, position.Symbol, 2, 20, new(2026,10,23))] } };
            prior = prior with { Orders = [order], Protection = [BrokerLiveProtection.Transfer(prior.Strategy, position, null, bought)] };
        }
        prior = prior with { Account = account, NewBuysStopped = paused }; x.F.Store.State = prior;
        x.F.TransformAccount = _ => account; x.F.RunningImplementation = "minute-reviewed-build";
        var current = x.Existing.Current with { PreparedUtc = x.F.Now.UtcDateTime,
            Strategy = prior.Strategy with { Strategy = prior.Strategy.Strategy with { ImplementationIdentity = x.F.ImplementationIdentity } } };
        return new(x, prior, current, new(TmxCollectorSource.Contract, TimeSpan.FromSeconds(45),
            x.F.ImplementationIdentity, new string('A', 64), x.F.Now.AddMinutes(-1)));
    }
}
