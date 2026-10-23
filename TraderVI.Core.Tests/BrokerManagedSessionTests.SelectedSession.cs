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
    internal static async Task<BrokerSessionContinuation> SyntheticSelectedSessionContinuation() => (await SelectedSessionFixture()).Continuation;
    private static ITsxSessionCalendar SelectedSessionCalendar() => new ReviewedTsxSessionCalendar(new("fixture", "offline",
        new(2026,10,19), new(2026,10,27), [new(2026,10,19),new(2026,10,20),new(2026,10,21),new(2026,10,22),new(2026,10,23),new(2026,10,26),new(2026,10,27)]));
    private sealed record SelectedSessionFacts(SourceSwitchFacts Original, BrokerManagedSession Prior,
        BrokerSessionContinuation Continuation, BrokerStrategyReview Current, ContinuationRisks Risks)
    {
        internal Fixture F => Original.Existing.F;
        internal HostGrants Grants => Original.Existing.Grants;
        internal BrokerSessionContinuationWorkflow Workflow() => new(F.Store, Risks, F, Grants, SelectedSessionCalendar(), new HostClock(F));
        internal BrokerFirstExecutionReview Review(bool daily = true, bool storage = true) => BrokerSessionReviewPolicy.Prepare(
            Prior, Continuation, Current with { PreparedUtc = F.Now.UtcDateTime }, F.Now, F.TransformAccount!(F.Project()),
            storage, F.ImplementationIdentity, SelectedSessionCalendar(), F.Now, daily);
        internal BrokerExecutionApproval Approval()
        {
            var review = Review(); Assert.True(review.CanApprove, string.Join(";", review.Blocks));
            return new(review.Activation!, "fixture", "Selected session only; synthetic offline test");
        }
    }
    private static async Task<SelectedSessionFacts> SelectedSessionFixture(bool preOpen = true)
    {
        var x = await SwitchFacts(); var prior = await x.Workflow().ApproveAsync(x.Prepare());
        var f = x.Existing.F; var calendar = SelectedSessionCalendar();
        f.Now = new(2026, 10, 21, 19, 59, 21, TimeSpan.Zero);
        prior = prior with { Event = "StrategyEvaluated", SavedAt = f.Now };
        f.Store.State = prior; f.Store.Calendar = calendar;
        var risks = new ContinuationRisks(prior.Account.Risk, calendar, () => f.Now);
        f.Now = new(2026, 10, 23, preOpen ? 13 : 14, 0, 0, TimeSpan.Zero);
        f.RunningImplementation = "selected-session-reviewed-build";
        var continuation = BrokerSessionContinuation.Create(prior, new(2026,10,23), f.Now, "fixture", true, calendar);
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
    public async Task SelectedSessionApprovalPreservesAllPriorAuthorityRiskAndMemoryAndEnablesSeparately()
    {
        var x = await SelectedSessionFixture(); var approval = x.Approval();
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
        Assert.Equal(new DateOnly(2026,10,23), saved.EffectiveReview.Session);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 20, 0, 0, TimeSpan.Zero), saved.EffectiveReview.NoTransferScope!.PurchaseExpiresAt);
        Assert.Null(x.Grants.Value); Assert.Equal(0, x.F.Transport.Sent);
        Assert.Equal(saved.SavedAt.UtcDateTime, saved.StrategyEffectiveUtc);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(x.Risks.Receipt)) < 256 * 1024);
        x.F.Store.Restart(); Assert.Equal(saved.SessionContinuation!.Fingerprint, x.F.Store.State!.SessionContinuation!.Fingerprint);
        int calls = 0;
        var host = new BrokerExecutionHost(x.Grants, new BrokerManagedSessionControls(x.F.Store),
            (s, ct) => { calls++; return Task.FromResult(s); }, SelectedSessionCalendar(), x.F.ImplementationIdentity, new HostClock(x.F));
        await host.PollAsync(true); Assert.Equal(0, calls);
        await host.EnableAsync(saved.BindingId, saved.SessionId, saved.Strategy.Fingerprint, "fixture");
        Assert.Equal(saved.EffectiveReview.RequestId, x.Grants.Value!.ContinuationId);
        await host.PollAsync(true); Assert.Equal(0, calls);
        long revision = x.F.Store.State!.Revision;
        await x.F.Workflow().TickAsync(saved.BindingId); Assert.Equal(revision, x.F.Store.State!.Revision);
        x.F.Now = new(2026,10,23,13,30,0,TimeSpan.Zero); await host.PollAsync(true); Assert.Equal(1, calls);
        x.F.Now = new(2026,10,23,20,0,0,TimeSpan.Zero); await host.PollAsync(true); Assert.Equal(1, calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.EnableAsync(saved.BindingId, saved.SessionId, saved.Strategy.Fingerprint, "fixture"));
    }

    [Theory]
    [InlineData("risk acknowledgement")][InlineData("session acknowledgement")]
    public async Task SelectedSessionLostAcknowledgementRecoversOriginalRequestWithoutDuplicatingAuthority(string lost)
    {
        var x = await SelectedSessionFixture(); var approval = x.Approval();
        if (lost == "risk acknowledgement") x.Risks.LoseResponse = true;
        else x.F.Store.LoseResponseAtEvent = "SessionContinuationApproved";
        await Assert.ThrowsAnyAsync<Exception>(() => x.Workflow().ApproveAsync(approval));
        x.F.Store.Restart(); var saved = await x.Workflow().ApproveAsync(approval);
        Assert.Equal(1, x.Risks.Appends); Assert.Equal(x.Continuation.RiskRequest().RequestHash, saved.SessionContinuation!.RiskRequest.RequestHash);
        Assert.Null(x.Grants.Value); Assert.Empty(saved.Orders); Assert.Equal(0, x.F.Transport.Sent);
    }

    [Fact]
    public async Task SelectedSessionRejectedPermissionSaveRecoversOriginalRiskWithFreshReview()
    {
        var x = await SelectedSessionFixture(); var firstApproval = x.Approval();
        x.F.Store.BeforeAppend = next =>
        {
            if (next.Event == "SessionContinuationApproved")
                throw new IOException("Synthetic archive rejection before permission commit");
        };
        await Assert.ThrowsAsync<IOException>(() => x.Workflow().ApproveAsync(firstApproval));
        Assert.Equal(x.Prior.RevisionId, x.F.Store.State!.RevisionId);
        Assert.Null(x.F.Store.State.SessionContinuation); Assert.Equal(1, x.Risks.Appends);
        var originalRisk = JsonSerializer.Serialize(x.Risks.Receipt);
        x.F.Store.Restart(); x.F.Store.BeforeAppend = null; x.F.Now = x.F.Now.AddMinutes(3);
        x = x with { Continuation = x.Risks.Receipt!.Request.SessionContinuation! };
        var freshApproval = x.Approval();
        Assert.NotEqual(firstApproval.Fingerprint, freshApproval.Fingerprint);
        var saved = await x.Workflow().ApproveAsync(freshApproval);
        Assert.Equal(originalRisk, JsonSerializer.Serialize(x.Risks.Receipt));
        Assert.Equal(1, x.Risks.Appends); Assert.Equal(x.Prior.Revision + 1, saved.Revision);
        Assert.Equal(x.Risks.Receipt!.Request.RequestHash, saved.SessionContinuation!.RiskRequest.RequestHash);
        Assert.Null(x.Grants.Value); Assert.Empty(saved.Orders); Assert.Equal(0, x.F.Transport.Sent);
        var packed = global::Core.Runtime.EngineArchiveCodec.Pack(JsonSerializer.Serialize(saved));
        Assert.All(packed.Contents.Values, fragment =>
        {
            using var document = JsonDocument.Parse(fragment);
            Assert.True(document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array);
        });
        Assert.Equal(JsonSerializer.Serialize(saved),
            JsonSerializer.Serialize(JsonSerializer.Deserialize<BrokerManagedSession>(
                global::Core.Runtime.EngineArchiveCodec.Restore(packed.Manifest, packed.Contents))));
    }

    [Theory]
    [InlineData("old grant")][InlineData("stale review")][InlineData("changed revision")][InlineData("risk changed")]
    [InlineData("new build")][InlineData("larger allowance")][InlineData("cutoff")][InlineData("other date")]
    public async Task SelectedSessionApprovalRejectsChangedAuthorityOrExpiredFacts(string change)
    {
        var x = await SelectedSessionFixture(); var approval = x.Approval();
        switch (change)
        {
            case "old grant": x.Grants.Value = new(x.Prior.BindingId,x.Prior.SessionId,x.Prior.Strategy.Fingerprint,x.Prior.Strategy.Strategy.ImplementationIdentity,x.F.Now,"fixture",true); break;
            case "stale review": x.F.Now = x.F.Now.AddMinutes(3); break;
            case "changed revision": x.F.Store.State = x.Prior with { RevisionId = Guid.NewGuid() }; break;
            case "risk changed": x.Risks.State = x.Risks.State with { CapitalReviewRequired = true }; break;
            case "new build": x.F.RunningImplementation = "changed-again"; break;
            case "larger allowance": approval = approval with { Review = approval.Review with { PurchaseCeiling = approval.Review.PurchaseCeiling + 1 } }; break;
            case "cutoff": x.F.Now = new(2026,10,23,20,0,0,TimeSpan.Zero); break;
            case "other date": x.F.Now = x.F.Now.AddDays(1); break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => x.Workflow().ApproveAsync(approval));
        Assert.Null(x.F.Store.State!.SessionContinuation); Assert.Equal(0, x.Risks.Appends); Assert.Equal(0, x.F.Transport.Sent);
    }

    [Theory]
    [InlineData("missing daily")][InlineData("storage")][InlineData("cash")][InlineData("stale account")][InlineData("hold")]
    public async Task SelectedSessionReviewRequiresCurrentAccountStorageAndDailyEvidence(string change)
    {
        var x = await SelectedSessionFixture(); var original = x.F.TransformAccount!;
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
        Assert.Equal(0, x.Risks.Appends); Assert.Null(x.F.Store.State!.SessionContinuation);
    }

    [Fact]
    public async Task SelectedSessionSourceAndRiskScopeCannotCarryStalePreOpenFactsIntoTrading()
    {
        var x = await SelectedSessionFixture(); var calendar = SelectedSessionCalendar();
        var updated = new DateTimeOffset(2026,10,21,20,0,0,TimeSpan.Zero);
        Assert.True(BrokerSessionContinuation.SourceReady(x.Continuation,updated,calendar,x.F.Now,true));
        Assert.False(BrokerSessionContinuation.SourceReady(x.Continuation,updated,calendar,x.F.Now,false));
        var projected = x.F.TransformAccount!(x.F.Project());
        var evidence = await new BrokerNoTransferRiskEvidenceSource(x.Continuation.Scope(calendar),calendar,new HostClock(x.F))
            .ReadAsync(projected.Import,x.F.Risk,[],default);
        Assert.Null(evidence.Coverage);
        x.F.Now = new(2026,10,23,13,30,0,TimeSpan.Zero);
        Assert.False(BrokerSessionContinuation.SourceReady(x.Continuation,updated,calendar,x.F.Now,true));
        Assert.True(BrokerSessionContinuation.SourceReady(x.Continuation,x.F.Now,calendar,x.F.Now,false));
    }

    [Theory]
    [InlineData(21)][InlineData(22)][InlineData(24)]
    public async Task SelectedSessionCannotApproveTonightOrRenewOnAnotherDate(int day)
    {
        var x = await SelectedSessionFixture();
        var changed = x.Continuation with { ConfirmedAt = new(2026,10,day,13,0,0,TimeSpan.Zero) };
        Assert.Throws<InvalidOperationException>(() => changed.Validate(SelectedSessionCalendar()));
        Assert.DoesNotContain("SessionContinuation", JsonSerializer.Serialize(x.Prior)); // Absent fields retain legacy hashes.
    }

    [Theory]
    [InlineData("source")][InlineData("order hold")][InlineData("capital hold")][InlineData("daily hold")]
    [InlineData("invalidated")][InlineData("risk reference")][InlineData("permission anchor")]
    public async Task SelectedSessionRejectsAnAlteredPriorAssignmentOrFinancialReference(string change)
    {
        var x = await SelectedSessionFixture(); var prior = x.Prior;
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
        var changed = x.Continuation with { PreviousSessionHash = BrokerSessionContinuation.StateHash(prior) };
        Assert.Throws<InvalidOperationException>(() => changed.RequirePrior(prior, SelectedSessionCalendar()));
    }

    [Theory]
    [InlineData("profile")][InlineData("paper source")][InlineData("risk setting")]
    public async Task SelectedSessionCannotSilentlyReplaceStrategyOrLoosenRules(string change)
    {
        var x = await SelectedSessionFixture(); var current = x.Current;
        current = current with { Strategy = change switch
        {
            "profile" => current.Strategy with { GateProfile = PaperGateProfile.NormalV1 },
            "paper source" => current.Strategy with { PaperAccountId = Guid.NewGuid() },
            _ => current.Strategy with { Strategy = current.Strategy.Strategy with
                { LivePolicy = current.Strategy.Strategy.ReadLivePolicy() with { MaximumHoldings = 1 } } }
        } };
        Assert.Throws<ArgumentException>(() => BrokerSessionReviewPolicy.Prepare(x.Prior,x.Continuation,current,
            x.F.Now,x.F.TransformAccount!(x.F.Project()),true,x.F.ImplementationIdentity,SelectedSessionCalendar(),x.F.Now,true));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task SelectedSessionPreservesPausedAndStoppedBuyingModes(bool paused)
    {
        var x = await SelectedSessionFixture(); var prior = paused ? x.Prior with { Phase = BrokerManagedPhase.Paused }
            : x.Prior with { NewBuysStopped = true };
        x = x with { Prior = prior, Continuation = x.Continuation with { PreviousSessionHash = BrokerSessionContinuation.StateHash(prior) } };
        x.F.Store.State = prior;
        // The reviewed scope/risk association binds the changed prior revision hash as well.
        var expected = x.Continuation.Apply(prior.Account.Risk,SelectedSessionCalendar());
        x.F.Risk = expected;
        var original = x.F.TransformAccount!;
        x.F.TransformAccount = a => original(a) with { Risk = expected };
        var saved = await x.Workflow().ApproveAsync(x.Approval());
        Assert.Equal(prior.Phase,saved.Phase); Assert.Equal(prior.NewBuysStopped,saved.NewBuysStopped);
    }
    [Fact]
    public async Task SelectedSessionRepeatedReviewsLinkPriorPermissionWithoutEmbeddingEveryDay()
    {
        var x = await SelectedSessionFixture(); var first = await x.Workflow().ApproveAsync(x.Approval());
        var before = JsonSerializer.Serialize(first.Account.Risk).Length;
        var calendar = SelectedSessionCalendar();
        foreach (int day in new[] { 26, 27 })
        {
            var prior = x.F.Store.State!;
            x.F.Now = new(2026,10,day,13,0,0,TimeSpan.Zero);
            var continuation = BrokerSessionContinuation.Create(prior, new(2026,10,day), x.F.Now, "fixture", true, calendar);
            var risk = continuation.ExpectedRisk(calendar); var scope = continuation.Scope(calendar);
            x.F.Risk = risk;
            x.F.TransformAccount = account =>
            {
                var observation = prior.Account.Import.Plan.Observation with { SnapshotAt = x.F.Now, CheckedAt = x.F.Now };
                var import = account.Import with { Plan = account.Import.Plan with { Observation = observation } };
                var evidence = new BrokerNoTransferRiskEvidenceSource(scope,calendar,new HostClock(x.F),preOpenReviewOnly:true)
                    .ReadAsync(import,risk,[],default).GetAwaiter().GetResult();
                return BrokerAccountProjector.Project(x.F.Binding,import,risk,evidence.Coverage,x.F.Now) with { NewRiskBlocks = evidence.Blocks };
            };
            x = x with { Prior = prior, Continuation = continuation, Risks = new(prior.Account.Risk,calendar,()=>x.F.Now) };
            var saved = await x.Workflow().ApproveAsync(x.Approval());
            Assert.Equal(prior.RevisionId, continuation.PreviousRevisionId);
            Assert.Equal(prior.EffectiveReview.Session, continuation.PreviousSession);
            Assert.Null(continuation.OriginScope.SessionContinuation);
            Assert.Equal(new DateOnly(2026,10,21), continuation.OriginScope.Session);
            Assert.Equal(first.Account.Risk.Through, saved.Account.Risk.Through);
            Assert.Equal(first.Account.Risk.LastCompletedSession, saved.Account.Risk.LastCompletedSession);
            Assert.Equal(first.Account.Risk.SessionReference, saved.Account.Risk.SessionReference);
            Assert.Equal(BrokerAccountImport.Hash(first.Memory), BrokerAccountImport.Hash(saved.Memory));
            Assert.Equal(first.EffectiveReview.PurchaseCeiling, saved.EffectiveReview.PurchaseCeiling);
            Assert.InRange(JsonSerializer.Serialize(saved.Account.Risk).Length, before - 100, before + 100);
            Assert.Null(x.Grants.Value); Assert.Equal(0,x.F.Transport.Sent);
            x.F.Store.Restart();
        }
    }

    [Fact]
    public async Task SelectedSessionRequiresConfirmationForTheEntireMissedInterval()
    {
        var x = await SelectedSessionFixture();
        Assert.Equal(new DateOnly(2026,10,21), x.Continuation.PreviousSession);
        Assert.Equal(new DateOnly(2026,10,23), x.Continuation.Session);
        Assert.Throws<InvalidOperationException>(() => (x.Continuation with { NoOutsideChangesConfirmed = false }).Validate(SelectedSessionCalendar()));
        Assert.Throws<InvalidOperationException>(() => BrokerSessionContinuation.Create(x.Prior,new(2026,10,24),
            new(2026,10,24,13,0,0,TimeSpan.Zero),"fixture",true,SelectedSessionCalendar()));
        Assert.False(BrokerSessionContinuation.SourceReady(x.Continuation,x.F.Now,SelectedSessionCalendar(),x.F.Now.AddDays(1),true));
        Assert.Throws<InvalidOperationException>(() => (x.Continuation with { OriginScope = x.Continuation.Scope(SelectedSessionCalendar()) }).Validate(SelectedSessionCalendar()));
    }

}
