#nullable enable
using Core.Db;
using Core.Trader;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    [Theory]
    [InlineData(-31)] [InlineData(360)] [InlineData(1440)]
    public async Task DatedBuildReviewCannotApproveBeforeOpenAfterExpiryOrOnAnotherDate(int minutes)
    {
        var x = await SelectedSessionFixture(preOpen: false);
        var prior = await x.Workflow().ApproveAsync(x.Approval());
        x.F.RunningImplementation = "new-build"; x.F.Now = x.F.Now.AddMinutes(minutes);
        var current = x.Current with { PreparedUtc = x.F.Now.UtcDateTime,
            Strategy = prior.Strategy with { Strategy = prior.Strategy.Strategy with { ImplementationIdentity = x.F.ImplementationIdentity } } };
        Assert.ThrowsAny<Exception>(() => BrokerImplementationReviewWorkflow.Prepare(prior, current, x.F.Now,
            x.F.TransformAccount!(x.F.Project()), true, x.F.ImplementationIdentity, "fixture", SelectedSessionCalendar(), x.F.Now));
        Assert.Equal(prior.RevisionId, x.F.Store.State!.RevisionId); Assert.Null(x.Grants.Value);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task BuildReviewBindsTheExistingDatedPermissionWithoutRenewingOrReplacingIt(bool selected)
    {
        Fixture f; HostGrants grants; BrokerManagedSession prior; BrokerStrategyReview current;
        global::Core.Trader.DelphiLive.ITsxSessionCalendar calendar;
        if (selected)
        {
            var x = await SelectedSessionFixture(preOpen: false);
            prior = await x.Workflow().ApproveAsync(x.Approval());
            f = x.F; grants = x.Grants; current = x.Current; calendar = SelectedSessionCalendar();
        }
        else
        {
            var x = await October22Fixture(preOpen: false);
            prior = await x.Workflow().ApproveAsync(x.Approval());
            f = x.F; grants = x.Grants; current = x.Current; calendar = October22Calendar();
        }
        var original = JsonSerializer.Serialize(prior);
        f.RunningImplementation = "corrected-market-build";
        current = current with { Strategy = prior.Strategy with { Strategy = prior.Strategy.Strategy with { ImplementationIdentity = f.ImplementationIdentity } } };
        var proposal = BrokerImplementationReviewWorkflow.Prepare(prior, current, f.Now, f.TransformAccount!(f.Project()),
            true, f.ImplementationIdentity, "fixture", calendar, f.Now);
        Assert.Equal(prior.EffectiveReview.RequestId, proposal.Review.PermissionRequestId);
        var workflow = new BrokerImplementationReviewWorkflow(f.Store, f, grants, calendar, new HostClock(f));
        var saved = await workflow.ApproveAsync(proposal);
        Assert.Equal(f.ImplementationIdentity, saved.Strategy.Strategy.ImplementationIdentity);
        Assert.Equal(prior.EffectiveReview.RequestId, saved.EffectiveReview.RequestId);
        Assert.Equal(prior.EffectiveReview.NoTransferScope!.PurchaseExpiresAt, saved.EffectiveReview.NoTransferScope!.PurchaseExpiresAt);
        Assert.Equal(original, JsonSerializer.Serialize(prior));
        Assert.Equal(BrokerAccountImport.Hash(prior.Account.Risk), BrokerAccountImport.Hash(saved.Account.Risk));
        Assert.Equal(BrokerAccountImport.Hash(prior.SessionContinuation), BrokerAccountImport.Hash(saved.SessionContinuation));
        Assert.Equal(BrokerAccountImport.Hash(prior.October22), BrokerAccountImport.Hash(saved.October22));
        Assert.Equal(BrokerAccountImport.Hash(prior.Memory), BrokerAccountImport.Hash(saved.Memory));
        Assert.Null(grants.Value); Assert.Equal(0, f.Transport.Sent);
        foreach (Guid? wrong in new Guid?[] { null, Guid.NewGuid() })
        {
            var altered = saved with { ImplementationReview = saved.ImplementationReview! with { PermissionRequestId = wrong } };
            Assert.Equal(prior.Strategy.Strategy.ImplementationIdentity, altered.Strategy.Strategy.ImplementationIdentity);
            Assert.ThrowsAny<Exception>(() => BrokerManagedSessionRepository.ValidateNext(prior, altered, calendar));
        }
        f.Store.Restart();
        Assert.Equal(saved.Strategy.Fingerprint, f.Store.State!.Strategy.Fingerprint);
        var recovered = await workflow.ApproveAsync(proposal);
        Assert.Equal(saved.RevisionId, recovered.RevisionId);
        int polls = 0;
        var host = new BrokerExecutionHost(grants, new BrokerManagedSessionControls(f.Store),
            (s, ct) => { polls++; return Task.FromResult(s); }, calendar, f.ImplementationIdentity, new HostClock(f));
        await host.PollAsync(true); Assert.Equal(0, polls);
        await host.EnableAsync(saved.BindingId, saved.SessionId, saved.Strategy.Fingerprint, "fixture");
        await host.PollAsync(true); Assert.Equal(1, polls);
        f.Now = saved.EffectiveReview.NoTransferScope.PurchaseExpiresAt!.Value;
        await host.PollAsync(true); Assert.Equal(1, polls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.EnableAsync(saved.BindingId, saved.SessionId, saved.Strategy.Fingerprint, "fixture"));
    }
    private sealed record BuildFacts(SourceSwitchFacts Source, BrokerManagedSession Prior, BrokerStrategyReview Current)
    {
        internal BrokerImplementationReviewWorkflow Workflow() => new(Source.Existing.F.Store, Source.Existing.F,
            Source.Existing.Grants, BrokerTrialCarryForwardTests.Calendar(), new HostClock(Source.Existing.F));
        internal BrokerImplementationReviewProposal Prepare() => BrokerImplementationReviewWorkflow.Prepare(Prior,
            Current, Source.Existing.F.Now, Source.Existing.F.TransformAccount!(Source.Existing.F.Project()), true,
            Source.Existing.F.ImplementationIdentity, "fixture", BrokerTrialCarryForwardTests.Calendar(), Source.Existing.F.Now);
    }
    private static async Task<BuildFacts> UpdatedBuildFacts()
    {
        var source = await SwitchFacts();
        var prior = await source.Workflow().ApproveAsync(source.Prepare());
        source.Existing.F.RunningImplementation = "snapshot-performance-build";
        var strategy = prior.Strategy with { Strategy = prior.Strategy.Strategy with
            { ImplementationIdentity = source.Existing.F.ImplementationIdentity } };
        return new(source, prior, source.Target with { Strategy = strategy });
    }

    [Fact]
    public async Task BuildOnlyApprovalPreservesSourceSignalsFinancialHistoryAndCutoffRequiresSeparateEnable()
    {
        var x = await UpdatedBuildFacts(); var prior = x.Prior; var f = x.Source.Existing.F;
        var saved = await x.Workflow().ApproveAsync(x.Prepare());
        Assert.Equal("ImplementationReviewed", saved.Event);
        Assert.Equal(x.Current.Strategy.Fingerprint, saved.Strategy.Fingerprint);
        Assert.Equal(PaperGateProfile.PortfolioRulesOnlyV1, saved.Strategy.GateProfile);
        Assert.Equal(prior.SourceSwitch, saved.SourceSwitch);
        Assert.Equal(prior.StrategyEffectiveUtc, saved.StrategyEffectiveUtc);
        Assert.Equal(BrokerAccountImport.Hash(prior.Memory), BrokerAccountImport.Hash(saved.Memory));
        Assert.Equal(prior.Approval.Fingerprint, saved.Approval.Fingerprint);
        Assert.Equal(prior.Continuation!.Fingerprint, saved.Continuation!.Fingerprint);
        Assert.Equal(BrokerAccountImport.Hash(prior.Account.Risk), BrokerAccountImport.Hash(saved.Account.Risk));
        Assert.Equal(BrokerAccountImport.Hash(prior.Protection), BrokerAccountImport.Hash(saved.Protection));
        Assert.Equal(BrokerAccountImport.Hash(prior.Orders), BrokerAccountImport.Hash(saved.Orders));
        Assert.Null(x.Source.Existing.Grants.Value); Assert.Equal(0, f.Transport.Sent);
        int polls = 0;
        var host = new BrokerExecutionHost(x.Source.Existing.Grants, new BrokerManagedSessionControls(f.Store),
            (s, ct) => { polls++; return Task.FromResult(s); }, BrokerTrialCarryForwardTests.Calendar(),
            f.ImplementationIdentity, new HostClock(f));
        await host.PollAsync(true); Assert.Equal(0, polls);
        await host.EnableAsync(saved.BindingId, saved.SessionId, saved.Strategy.Fingerprint, "fixture");
        await host.PollAsync(true); Assert.Equal(1, polls);
        f.Now = new(2026,10,21,20,0,0,TimeSpan.Zero);
        await host.PollAsync(true); Assert.Equal(1, polls);
        Assert.Equal(f.Now, saved.EffectiveReview.NoTransferScope!.PurchaseExpiresAt);
    }

    [Fact]
    public async Task BuildReviewLostAcknowledgementRecoversExactRequestAndOldJsonHashIsUnchanged()
    {
        var x = await UpdatedBuildFacts(); var f = x.Source.Existing.F; var proposal = x.Prepare();
        string old = JsonSerializer.Serialize(x.Prior);
        Assert.DoesNotContain("ImplementationReview", old);
        Assert.Equal(BrokerAccountImport.Hash(x.Prior), BrokerAccountImport.Hash(JsonSerializer.Deserialize<BrokerManagedSession>(old)));
        f.Store.LoseResponseAtEvent = "ImplementationReviewed";
        await Assert.ThrowsAsync<IOExceptionForTest>(() => x.Workflow().ApproveAsync(proposal));
        f.Store.Restart(); f.Now = f.Now.AddMinutes(3);
        var recovered = await x.Workflow().ApproveAsync(proposal);
        Assert.Equal(proposal.Review.ReviewId, recovered.RevisionId);
        Assert.Equal(x.Prior.Revision + 1, recovered.Revision);
        await Assert.ThrowsAsync<InvalidOperationException>(() => x.Workflow().ApproveAsync(proposal with
            { Review = proposal.Review with { ReviewedBy = "changed" } }));
        Assert.Equal(0, f.Transport.Sent); Assert.Null(x.Source.Existing.Grants.Value);
    }

    [Theory]
    [InlineData("host")][InlineData("revision")][InlineData("build")][InlineData("expired")]
    [InlineData("source expired during read")][InlineData("build changed during read")][InlineData("close during read")]
    [InlineData("risk")][InlineData("cash")][InlineData("blocks")][InlineData("id")]
    public async Task BuildReviewRechecksAuthorityAccountAndFreshness(string change)
    {
        var x = await UpdatedBuildFacts(); var f = x.Source.Existing.F; var p = x.Prepare();
        if (change == "host") x.Source.Existing.Grants.Value = new(x.Prior.BindingId, x.Prior.SessionId,
            x.Prior.Strategy.Fingerprint, x.Prior.Strategy.Strategy.ImplementationIdentity, f.Now, "fixture", true);
        if (change == "revision") f.Store.State = x.Prior with { RevisionId = Guid.NewGuid() };
        if (change == "build") f.RunningImplementation = "other";
        if (change == "expired") f.Now = f.Now.AddMinutes(3);
        if (change == "id") p = p with { Review = p.Review with { ReviewId = Guid.NewGuid() } };
        var original = f.TransformAccount!;
        if (change == "risk") f.TransformAccount = a => { var b = original(a); return b with { Risk = b.Risk with { RevisionId = Guid.NewGuid() } }; };
        if (change == "cash") f.TransformAccount = a => { var b = original(a); return b with { Import = b.Import with
            { Plan = b.Import.Plan with { Observation = b.Import.Plan.Observation with { Cash = 1 } } } }; };
        if (change == "blocks") f.TransformAccount = a => original(a) with { NewRiskBlocks = ["pending"] };
        if (change == "source expired during read")
        {
            p = p with { SourceUpdatedAt = f.Now.AddSeconds(-119) };
            f.TransformAccount = a => { var b = original(a); f.Now = f.Now.AddSeconds(2); return b; };
        }
        if (change == "build changed during read") f.TransformAccount = a => { var b = original(a); f.RunningImplementation = "other"; return b; };
        if (change == "close during read") f.TransformAccount = a => { var b = original(a); f.Now = new(2026,10,21,20,0,0,TimeSpan.Zero); return b; };
        await Assert.ThrowsAnyAsync<Exception>(() => x.Workflow().ApproveAsync(p));
        Assert.Null(f.Store.State!.ImplementationReview); Assert.Equal(0, f.Transport.Sent);
    }

    [Theory]
    [InlineData("no switch")][InlineData("same build")][InlineData("profile")][InlineData("policy")]
    [InlineData("source stale")][InlineData("source future")][InlineData("review stale")][InlineData("no storage")]
    [InlineData("orders")][InlineData("daily hold")][InlineData("capital hold")][InlineData("invalidated")]
    [InlineData("expired")][InlineData("next day")]
    public async Task BuildReviewPreparationRejectsTradingOrPermissionChanges(string change)
    {
        var x = await UpdatedBuildFacts(); var f = x.Source.Existing.F;
        var prior = x.Prior; var current = x.Current; var at = f.Now; var sourceAt = at;
        if (change == "no switch") prior = prior with { SourceSwitch = null };
        if (change == "same build") current = current with { Strategy = prior.Strategy };
        if (change == "profile") current = current with { Strategy = current.Strategy with { GateProfile = PaperGateProfile.NormalV1 } };
        if (change == "policy") current = current with { Strategy = current.Strategy with { Strategy = current.Strategy.Strategy with { PolicyVersionId = Guid.NewGuid() } } };
        if (change == "source stale") sourceAt = at.AddMinutes(-3);
        if (change == "source future") sourceAt = at.AddSeconds(1);
        if (change == "review stale") current = current with { PreparedUtc = at.AddMinutes(-3).UtcDateTime };
        if (change == "orders") prior = prior with { Orders = [new(Guid.NewGuid(), Guid.NewGuid(), null, "fixture", "FIX", "BUY", 1, 1, "Initial", null, at)] };
        if (change == "daily hold") prior = prior with { Memory = prior.Memory with { DailyLossHold = true } };
        if (change == "capital hold") prior = prior with { Memory = prior.Memory with { CapitalReviewRequired = true } };
        if (change == "invalidated") prior = prior with { ReviewInvalidated = true };
        if (change == "expired") at = new(2026,10,21,20,0,0,TimeSpan.Zero);
        if (change == "next day") at = at.AddDays(1);
        Assert.ThrowsAny<Exception>(() => BrokerImplementationReviewWorkflow.Prepare(prior, current, sourceAt,
            f.TransformAccount!(f.Project()), change != "no storage", f.ImplementationIdentity, "fixture",
            BrokerTrialCarryForwardTests.Calendar(), at));
        Assert.Null(f.Store.State!.ImplementationReview); Assert.Equal(0, f.Transport.Sent);
    }

    [Theory]
    [InlineData("memory")][InlineData("cutoff")][InlineData("ceiling")][InlineData("source")][InlineData("buy mode")]
    [InlineData("phase")][InlineData("review id")][InlineData("original approval")]
    public async Task BuildReviewRepositoryRejectsHiddenStateChanges(string change)
    {
        var x = await UpdatedBuildFacts(); var saved = await x.Workflow().ApproveAsync(x.Prepare());
        var bad = change switch
        {
            "memory" => saved with { Memory = saved.Memory with { HighestClosingValue = 1 } },
            "cutoff" => saved with { Continuation = saved.Continuation! with { Approval = saved.Continuation.Approval with
                { Review = saved.Continuation.Approval.Review with { NoTransferScope = saved.EffectiveReview.NoTransferScope! with { PurchaseExpiresAt = saved.SavedAt.AddDays(1) } } } } },
            "ceiling" => saved with { Continuation = saved.Continuation! with { Approval = saved.Continuation.Approval with
                { Review = saved.Continuation.Approval.Review with { PurchaseCeiling = 999 } } } },
            "source" => saved with { SourceSwitch = saved.SourceSwitch! with { Reviewer = "changed" } },
            "buy mode" => saved with { NewBuysStopped = !saved.NewBuysStopped },
            "phase" => saved with { Phase = BrokerManagedPhase.Paused },
            "review id" => saved with { ImplementationReview = saved.ImplementationReview! with { ReviewId = Guid.NewGuid() } },
            _ => saved with { Approval = saved.Approval with { Reason = "changed" } }
        };
        Assert.ThrowsAny<Exception>(() => BrokerManagedSessionRepository.ValidateNext(x.Prior, bad, BrokerTrialCarryForwardTests.Calendar()));
    }
}
