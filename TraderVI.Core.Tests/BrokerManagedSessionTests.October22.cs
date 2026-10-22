#nullable enable
using Core.Db;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    internal static async Task<BrokerOctober22Continuation> SyntheticOctober22Continuation() => (await October22Fixture()).Continuation;
    private static ITsxSessionCalendar October22Calendar() => new ReviewedTsxSessionCalendar(new("fixture", "offline",
        new(2026,10,19), new(2026,10,23), [new(2026,10,19),new(2026,10,20),new(2026,10,21),new(2026,10,22),new(2026,10,23)]));
    private sealed record October22Facts(SourceSwitchFacts Original, BrokerManagedSession Prior,
        BrokerOctober22Continuation Continuation, BrokerStrategyReview Current, ContinuationRisks Risks)
    {
        internal Fixture F => Original.Existing.F;
        internal HostGrants Grants => Original.Existing.Grants;
        internal BrokerOctober22Workflow Workflow() => new(F.Store, Risks, F, Grants, October22Calendar(), new HostClock(F));
        internal BrokerFirstExecutionReview Review(bool daily = true, bool storage = true) => BrokerOctober22ReviewPolicy.Prepare(
            Prior, Continuation, Current with { PreparedUtc = F.Now.UtcDateTime }, F.Now, F.TransformAccount!(F.Project()),
            storage, F.ImplementationIdentity, October22Calendar(), F.Now, daily);
        internal BrokerExecutionApproval Approval()
        {
            var review = Review(); Assert.True(review.CanApprove, string.Join(";", review.Blocks));
            return new(review.Activation!, "fixture", "October 22 only; synthetic offline test");
        }
    }
    private static async Task<October22Facts> October22Fixture(bool preOpen = true)
    {
        var x = await SwitchFacts(); var prior = await x.Workflow().ApproveAsync(x.Prepare());
        var f = x.Existing.F; var calendar = October22Calendar();
        f.Now = new(2026, 10, 21, 19, 59, 21, TimeSpan.Zero);
        prior = prior with { Event = "StrategyEvaluated", SavedAt = f.Now };
        f.Store.State = prior; f.Store.Calendar = calendar;
        var risks = new ContinuationRisks(prior.Account.Risk, calendar, () => f.Now);
        f.Now = new(2026, 10, 22, preOpen ? 13 : 14, 0, 0, TimeSpan.Zero);
        f.RunningImplementation = "october22-reviewed-build";
        var continuation = new BrokerOctober22Continuation(prior.SessionId, prior.RevisionId, BrokerAccountImport.Hash(prior),
            f.Now, prior.Account.Risk with { TrialCarry = null, Continuation = null }, prior.EffectiveReview.NoTransferScope!,
            f.Now, "fixture", true, preOpen);
        var scope = continuation.Scope(calendar); var expected = continuation.Apply(prior.Account.Risk, calendar);
        f.Risk = expected;
        f.TransformAccount = account =>
        {
            var observed = prior.Account.Import.Plan.Observation with { SnapshotAt = f.Now, CheckedAt = f.Now };
            var import = account.Import with { Plan = account.Import.Plan with { Observation = observed } };
            var evidence = new BrokerNoTransferRiskEvidenceSource(scope, calendar, new HostClock(f), preOpenReviewOnly: true)
                .ReadAsync(import, expected, [], default).GetAwaiter().GetResult();
            return BrokerAccountProjector.Project(f.Binding, import, expected, evidence.Coverage, f.Now) with { NewRiskBlocks = evidence.Blocks };
        };
        var current = x.Target with { Strategy = prior.Strategy with { Strategy = prior.Strategy.Strategy with { ImplementationIdentity = f.ImplementationIdentity } },
            PreparedUtc = f.Now.UtcDateTime };
        return new(x, prior, continuation, current, risks);
    }

    [Fact]
    public async Task October22ApprovalPreservesAllPriorAuthorityRiskAndMemoryAndEnablesSeparately()
    {
        var x = await October22Fixture(); var approval = x.Approval();
        Assert.Empty(approval.Review.RequiredQualificationChecks);
        Assert.Equal(x.Prior.EffectiveReview.PurchaseCeiling, approval.Review.PurchaseCeiling);
        Assert.Null(x.Grants.Value); Assert.Equal(0, x.Risks.Appends);
        var saved = await x.Workflow().ApproveAsync(approval);
        Assert.Equal(x.Prior.Continuation!.Fingerprint, saved.Continuation!.Fingerprint);
        Assert.Equal(BrokerAccountImport.Hash(x.Prior.SourceSwitch), BrokerAccountImport.Hash(saved.SourceSwitch));
        Assert.Equal(BrokerAccountImport.Hash(x.Prior.ImplementationReview), BrokerAccountImport.Hash(saved.ImplementationReview));
        Assert.Equal(x.Prior.Approval.Fingerprint, saved.Approval.Fingerprint);
        Assert.Equal(BrokerAccountImport.Hash(x.Prior.Memory), BrokerAccountImport.Hash(saved.Memory));
        Assert.Equal(x.Prior.Account.Risk.SessionReference, saved.Account.Risk.SessionReference);
        Assert.Equal(x.Prior.Account.Risk.DrawdownReference, saved.Account.Risk.DrawdownReference);
        Assert.Equal(x.Prior.Account.Risk.Through, saved.Account.Risk.Through);
        Assert.Equal(x.Prior.Account.Risk.LastCompletedSession, saved.Account.Risk.LastCompletedSession);
        Assert.Equal(BrokerOctober21Continuation.ExecutionDate, saved.Continuation.Approval.Review.Session);
        Assert.Equal(BrokerOctober22Continuation.ExecutionDate, saved.EffectiveReview.Session);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 20, 0, 0, TimeSpan.Zero), saved.EffectiveReview.NoTransferScope!.PurchaseExpiresAt);
        Assert.Null(x.Grants.Value); Assert.Equal(0, x.F.Transport.Sent);
        Assert.Equal(saved.SavedAt.UtcDateTime, saved.StrategyEffectiveUtc);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(x.Risks.Receipt)) < 256 * 1024);
        x.F.Store.Restart(); Assert.Equal(saved.October22!.Fingerprint, x.F.Store.State!.October22!.Fingerprint);
        int calls = 0;
        var host = new BrokerExecutionHost(x.Grants, new BrokerManagedSessionControls(x.F.Store),
            (s, ct) => { calls++; return Task.FromResult(s); }, October22Calendar(), x.F.ImplementationIdentity, new HostClock(x.F));
        await host.PollAsync(true); Assert.Equal(0, calls);
        await host.EnableAsync(saved.BindingId, saved.SessionId, saved.Strategy.Fingerprint, "fixture");
        Assert.Equal(saved.EffectiveReview.RequestId, x.Grants.Value!.ContinuationId);
        await host.PollAsync(true); Assert.Equal(0, calls);
        long revision = x.F.Store.State!.Revision;
        await x.F.Workflow().TickAsync(saved.BindingId); Assert.Equal(revision, x.F.Store.State!.Revision);
        x.F.Now = new(2026,10,22,13,30,0,TimeSpan.Zero); await host.PollAsync(true); Assert.Equal(1, calls);
        x.F.Now = new(2026,10,22,20,0,0,TimeSpan.Zero); await host.PollAsync(true); Assert.Equal(1, calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.EnableAsync(saved.BindingId, saved.SessionId, saved.Strategy.Fingerprint, "fixture"));
    }

    [Theory]
    [InlineData("risk acknowledgement")][InlineData("session acknowledgement")]
    public async Task October22LostAcknowledgementRecoversOriginalRequestWithoutDuplicatingAuthority(string lost)
    {
        var x = await October22Fixture(); var approval = x.Approval();
        if (lost == "risk acknowledgement") x.Risks.LoseResponse = true;
        else x.F.Store.LoseResponseAtEvent = "October22ContinuationApproved";
        await Assert.ThrowsAnyAsync<Exception>(() => x.Workflow().ApproveAsync(approval));
        x.F.Store.Restart(); var saved = await x.Workflow().ApproveAsync(approval);
        Assert.Equal(1, x.Risks.Appends); Assert.Equal(x.Continuation.RiskRequest().RequestHash, saved.October22!.RiskRequest.RequestHash);
        Assert.Null(x.Grants.Value); Assert.Empty(saved.Orders); Assert.Equal(0, x.F.Transport.Sent);
    }

    [Theory]
    [InlineData("old grant")][InlineData("stale review")][InlineData("changed revision")][InlineData("risk changed")]
    [InlineData("new build")][InlineData("larger allowance")][InlineData("cutoff")][InlineData("other date")]
    public async Task October22ApprovalRejectsChangedAuthorityOrExpiredFacts(string change)
    {
        var x = await October22Fixture(); var approval = x.Approval();
        switch (change)
        {
            case "old grant": x.Grants.Value = new(x.Prior.BindingId,x.Prior.SessionId,x.Prior.Strategy.Fingerprint,x.Prior.Strategy.Strategy.ImplementationIdentity,x.F.Now,"fixture",true); break;
            case "stale review": x.F.Now = x.F.Now.AddMinutes(3); break;
            case "changed revision": x.F.Store.State = x.Prior with { RevisionId = Guid.NewGuid() }; break;
            case "risk changed": x.Risks.State = x.Risks.State with { CapitalReviewRequired = true }; break;
            case "new build": x.F.RunningImplementation = "changed-again"; break;
            case "larger allowance": approval = approval with { Review = approval.Review with { PurchaseCeiling = approval.Review.PurchaseCeiling + 1 } }; break;
            case "cutoff": x.F.Now = new(2026,10,22,20,0,0,TimeSpan.Zero); break;
            case "other date": x.F.Now = x.F.Now.AddDays(1); break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => x.Workflow().ApproveAsync(approval));
        Assert.Null(x.F.Store.State!.October22); Assert.Equal(0, x.Risks.Appends); Assert.Equal(0, x.F.Transport.Sent);
    }

    [Theory]
    [InlineData("missing daily")][InlineData("storage")][InlineData("cash")][InlineData("stale account")][InlineData("hold")]
    public async Task October22ReviewRequiresCurrentAccountStorageAndDailyEvidence(string change)
    {
        var x = await October22Fixture(); var original = x.F.TransformAccount!;
        x.F.TransformAccount = a =>
        {
            var account = original(a);
            return change switch
            {
                "cash" => account with { Import = account.Import with { Plan = account.Import.Plan with { Observation = account.Import.Plan.Observation with { Cash = 1 } } } },
                "hold" => account with { Risk = account.Risk with { DailyLossHold = true } },
                "stale account" => account with { Account = account.Account with { ReportedUtc = x.F.Now.UtcDateTime.AddSeconds(-31) } },
                _ => account
            };
        };
        Assert.False(x.Review(change != "missing daily", change != "storage").CanApprove);
        Assert.Equal(0, x.Risks.Appends); Assert.Null(x.F.Store.State!.October22);
    }

    [Fact]
    public async Task October22SourceAndRiskScopeCannotCarryStalePreOpenFactsIntoTrading()
    {
        var x = await October22Fixture(); var calendar = October22Calendar();
        var updated = new DateTimeOffset(2026,10,21,20,0,0,TimeSpan.Zero);
        Assert.True(BrokerOctober22Continuation.SourceReady(x.Continuation,updated,calendar,x.F.Now,true));
        Assert.False(BrokerOctober22Continuation.SourceReady(x.Continuation,updated,calendar,x.F.Now,false));
        var projected = x.F.TransformAccount!(x.F.Project());
        var evidence = await new BrokerNoTransferRiskEvidenceSource(x.Continuation.Scope(calendar),calendar,new HostClock(x.F))
            .ReadAsync(projected.Import,x.F.Risk,[],default);
        Assert.Null(evidence.Coverage);
        x.F.Now = new(2026,10,22,13,30,0,TimeSpan.Zero);
        Assert.False(BrokerOctober22Continuation.SourceReady(x.Continuation,updated,calendar,x.F.Now,true));
        Assert.True(BrokerOctober22Continuation.SourceReady(x.Continuation,x.F.Now,calendar,x.F.Now,false));
    }

    [Theory]
    [InlineData(21)][InlineData(23)]
    public async Task October22CannotApproveTonightOrRenewOnAnotherDate(int day)
    {
        var x = await October22Fixture();
        var changed = x.Continuation with { ConfirmedAt = new(2026,10,day,13,0,0,TimeSpan.Zero) };
        Assert.Throws<InvalidOperationException>(() => changed.Validate(October22Calendar()));
        Assert.DoesNotContain("October22", JsonSerializer.Serialize(x.Prior)); // Absent fields retain legacy hashes.
    }

    [Theory]
    [InlineData("source")][InlineData("order hold")][InlineData("capital hold")][InlineData("daily hold")]
    [InlineData("invalidated")][InlineData("risk reference")][InlineData("permission anchor")]
    public async Task October22RejectsAnAlteredPriorAssignmentOrFinancialReference(string change)
    {
        var x = await October22Fixture(); var prior = x.Prior;
        prior = change switch
        {
            "source" => prior with { SourceSwitch = null },
            "order hold" => prior with { TrialOrderReviewRequired = true },
            "capital hold" => prior with { Memory = prior.Memory with { CapitalReviewRequired = true } },
            "daily hold" => prior with { Memory = prior.Memory with { DailyLossHold = true } },
            "invalidated" => prior with { ReviewInvalidated = true },
            "risk reference" => prior with { Account = prior.Account with { Risk = prior.Account.Risk with
                { SessionReference = prior.Account.Risk.SessionReference with { Value = 1 } } } },
            _ => prior with { Continuation = prior.Continuation! with { Approval = prior.Continuation.Approval with
                { Review = prior.Continuation.Approval.Review with { NoTransferScope = prior.EffectiveReview.NoTransferScope! with
                    { CashOnlyAnchor = prior.EffectiveReview.NoTransferScope!.CashOnlyAnchor with { Cash = 1 } } } } } }
        };
        var changed = x.Continuation with { PreviousSessionHash = BrokerAccountImport.Hash(prior) };
        Assert.Throws<InvalidOperationException>(() => changed.RequirePrior(prior, October22Calendar()));
    }

    [Theory]
    [InlineData("profile")][InlineData("paper source")][InlineData("risk setting")]
    public async Task October22CannotSilentlyReplaceStrategyOrLoosenRules(string change)
    {
        var x = await October22Fixture(); var current = x.Current;
        current = current with { Strategy = change switch
        {
            "profile" => current.Strategy with { GateProfile = PaperGateProfile.NormalV1 },
            "paper source" => current.Strategy with { PaperAccountId = Guid.NewGuid() },
            _ => current.Strategy with { Strategy = current.Strategy.Strategy with
                { LivePolicy = current.Strategy.Strategy.ReadLivePolicy() with { MaximumHoldings = 1 } } }
        } };
        Assert.Throws<ArgumentException>(() => BrokerOctober22ReviewPolicy.Prepare(x.Prior,x.Continuation,current,
            x.F.Now,x.F.TransformAccount!(x.F.Project()),true,x.F.ImplementationIdentity,October22Calendar(),x.F.Now,true));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task October22PreservesPausedAndStoppedBuyingModes(bool paused)
    {
        var x = await October22Fixture(); var prior = paused ? x.Prior with { Phase = BrokerManagedPhase.Paused }
            : x.Prior with { NewBuysStopped = true };
        x = x with { Prior = prior, Continuation = x.Continuation with { PreviousSessionHash = BrokerAccountImport.Hash(prior) } };
        x.F.Store.State = prior;
        // The reviewed scope/risk association binds the changed prior revision hash as well.
        var expected = x.Continuation.Apply(prior.Account.Risk,October22Calendar());
        x.F.Risk = expected;
        var original = x.F.TransformAccount!;
        x.F.TransformAccount = a => original(a) with { Risk = expected };
        var saved = await x.Workflow().ApproveAsync(x.Approval());
        Assert.Equal(prior.Phase,saved.Phase); Assert.Equal(prior.NewBuysStopped,saved.NewBuysStopped);
    }
}
