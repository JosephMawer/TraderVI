#nullable enable
using Core.Db;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    private sealed record SourceSwitchFacts(ContinuedFacts Existing, BrokerManagedSession Prior, BrokerStrategyReview Target)
    {
        internal BrokerLiveSourceSwitchWorkflow Workflow() => new(Existing.F.Store, Existing.F, Existing.Grants,
            BrokerTrialCarryForwardTests.Calendar(), new HostClock(Existing.F));
        internal BrokerLiveSourceSwitchProposal Prepare(bool storage = true) => BrokerLiveSourceSwitchWorkflow.Prepare(
            Prior, Target, Existing.F.Now, Existing.F.TransformAccount!(Existing.F.Project()), storage,
            Existing.F.ImplementationIdentity, "synthetic reviewer", BrokerTrialCarryForwardTests.Calendar(), Existing.F.Now);
    }
    private static async Task<SourceSwitchFacts> SwitchFacts()
    {
        var x = ContinuedFixture();
        var prior = await x.Workflow().ApproveAsync(x.Approval());
        x.F.RunningImplementation = "source-switch-build";
        var old = prior.Strategy;
        var policy = old.Strategy.ReadLivePolicy() with { PolicyVersionId = Guid.NewGuid() };
        var account = Guid.NewGuid();
        var target = old with { PaperAccountId = account, GateProfile = PaperGateProfile.PortfolioRulesOnlyV1,
            Comparison = old.Comparison! with { AccountId = account, Profile = PaperGateProfile.PortfolioRulesOnlyV1,
                LivePolicyVersionId = policy.PolicyVersionId }, LiveComparisonSuccessor = null,
            Strategy = old.Strategy with { PolicyVersionId = policy.PolicyVersionId, LivePolicy = policy,
                ImplementationIdentity = x.F.ImplementationIdentity, Name = "Synthetic Portfolio rules only" } };
        return new(x, prior, new(target, new(account, "Synthetic ungated", "DelphiLive", "{}",
            ["Synthetic offline source; no broker qualification"]), x.F.Now.UtcDateTime, []));
    }

    [Fact]
    public async Task UngatedSwitchPreservesFinancialAuthorityAndHistoryAndEnablesSeparately()
    {
        var x = await SwitchFacts(); var p = x.Prior; var request = x.Prepare();
        var saved = await x.Workflow().ApproveAsync(request);
        Assert.Equal(PaperGateProfile.PortfolioRulesOnlyV1, saved.Strategy.GateProfile);
        Assert.Equal(x.Target.Strategy.Fingerprint, saved.Strategy.Fingerprint);
        Assert.Equal(p.SessionId, saved.SessionId); Assert.Equal(p.Approval.Fingerprint, saved.Approval.Fingerprint);
        Assert.Equal(p.Continuation!.Fingerprint, saved.Continuation!.Fingerprint);
        Assert.Equal(BrokerAccountImport.Hash(p.EffectiveReview.NoTransferScope), BrokerAccountImport.Hash(saved.EffectiveReview.NoTransferScope));
        Assert.Equal(p.EffectiveReview.PurchaseCeiling, saved.EffectiveReview.PurchaseCeiling);
        Assert.Equal(p.ActivatedAt, saved.ActivatedAt); Assert.Equal(saved.SavedAt.UtcDateTime, saved.StrategyEffectiveUtc);
        Assert.Equal(BrokerAccountImport.Hash(p.Account.Risk), BrokerAccountImport.Hash(saved.Account.Risk));
        Assert.Equal(BrokerAccountImport.Hash(p.Orders), BrokerAccountImport.Hash(saved.Orders));
        Assert.Equal(BrokerAccountImport.Hash(p.Protection), BrokerAccountImport.Hash(saved.Protection));
        Assert.Equal(p.Memory.HighestClosingValue, saved.Memory.HighestClosingValue);
        Assert.Null(x.Existing.Grants.Value); Assert.Equal(0, x.Existing.F.Transport.Sent);
        int calls = 0;
        var host = new BrokerExecutionHost(x.Existing.Grants, new BrokerManagedSessionControls(x.Existing.F.Store),
            (s, ct) => { calls++; return Task.FromResult(s); }, BrokerTrialCarryForwardTests.Calendar(),
            x.Existing.F.ImplementationIdentity, new HostClock(x.Existing.F));
        await host.PollAsync(true); Assert.Equal(0, calls);
        await host.EnableAsync(saved.BindingId, saved.SessionId, saved.Strategy.Fingerprint, "fixture");
        await host.PollAsync(true); Assert.Equal(1, calls);
        x.Existing.F.Now = new(2026, 10, 21, 20, 0, 0, TimeSpan.Zero);
        await host.PollAsync(true); Assert.Equal(1, calls);
        Assert.Equal(new DateTimeOffset(2026,10,21,20,0,0,TimeSpan.Zero), saved.EffectiveReview.NoTransferScope!.PurchaseExpiresAt);
    }

    [Theory]
    [InlineData("stopped buys")][InlineData("paused")]
    public async Task SwitchPreservesOperatorModes(string mode)
    {
        var x = await SwitchFacts();
        var prior = mode == "paused" ? x.Prior with { Phase = BrokerManagedPhase.Paused }
            : x.Prior with { NewBuysStopped = true };
        x = x with { Prior = prior }; x.Existing.F.Store.State = prior;
        var saved = await x.Workflow().ApproveAsync(x.Prepare());
        Assert.Equal(prior.Phase, saved.Phase); Assert.Equal(prior.NewBuysStopped, saved.NewBuysStopped);
    }

    [Fact]
    public async Task SwitchResetsOnlyDerivedSignalsAndPreventsOldPaperOrNormalConfirmationReuse()
    {
        var x = await SwitchFacts();
        var signals = new TradingSymbolState(null, null, null, DelphiLiveEvaluationState.Initial(true),
            Guid.NewGuid(), x.Existing.F.Now.AddMinutes(-5).UtcDateTime, 7);
        var memory = x.Prior.Memory with { Symbols = x.Prior.Memory.Symbols.Add("FIX", signals) };
        x = x with { Prior = x.Prior with { Memory = memory } }; x.Existing.F.Store.State = x.Prior;
        var saved = await x.Workflow().ApproveAsync(x.Prepare()); var clean = saved.Memory.Symbols["FIX"];
        Assert.Null(clean.LiveEvaluation); Assert.Null(clean.LastLiveResult); Assert.Null(clean.LiveSessionId); Assert.Null(clean.LiveEpoch);
        Assert.Equal(saved.SavedAt.UtcDateTime, clean.LastLiveBarUtc);
        Assert.Equal(memory.HighestClosingValue, saved.Memory.HighestClosingValue);
        Assert.Equal(memory.TradingDate, saved.Memory.TradingDate);
        Assert.Equal(BrokerAccountImport.Hash(x.Prior), saved.SourceSwitch!.PreviousSessionHash);
        x.Existing.F.Store.Restart(); Assert.Equal(saved.SourceSwitch.RequestId, x.Existing.F.Store.State!.SourceSwitch!.RequestId);
    }

    [Fact]
    public async Task SwitchLostAcknowledgementRecoversSameRequestWithoutFinancialOrHostEffects()
    {
        var x = await SwitchFacts(); var proposal = x.Prepare();
        x.Existing.F.Store.LoseResponseAtEvent = "PortfolioRulesOnlyApproved";
        await Assert.ThrowsAnyAsync<Exception>(() => x.Workflow().ApproveAsync(proposal));
        var revision = x.Existing.F.Store.State!.Revision; x.Existing.F.Store.Restart();
        x.Existing.F.Now = x.Existing.F.Now.AddMinutes(3);
        var saved = await x.Workflow().ApproveAsync(proposal);
        Assert.Equal(revision, saved.Revision); Assert.Equal(proposal.Review.RequestId, saved.SourceSwitch!.RequestId);
        Assert.Null(x.Existing.Grants.Value); Assert.Equal(0, x.Existing.F.Transport.Sent); Assert.Equal(1, x.Existing.Risks.Appends);
        await Assert.ThrowsAsync<InvalidOperationException>(() => x.Workflow().ApproveAsync(proposal with
            { Review = proposal.Review with { Reviewer = "different" } }));
    }

    [Theory]
    [InlineData("no storage")][InlineData("source stale")][InlineData("source future")][InlineData("review stale")]
    [InlineData("source blocks")][InlineData("definition build")][InlineData("reviewer")][InlineData("different pair")]
    [InlineData("different batch")][InlineData("daily config")][InlineData("intraday risk")][InlineData("same source")]
    [InlineData("memory hold")][InlineData("invalidated")][InlineData("order hold")][InlineData("order")]
    [InlineData("initial only")][InlineData("expired")][InlineData("next day")]
    public async Task SourceSwitchPreparationRejectsIncompleteOrExpandedScope(string change)
    {
        var x = await SwitchFacts(); var prior = x.Prior; var target = x.Target; var at = x.Existing.F.Now; var updated = at;
        if (change == "source stale") updated = at.AddMinutes(-3);
        if (change == "source future") updated = at.AddSeconds(1);
        if (change == "review stale") target = target with { PreparedUtc = at.AddMinutes(-3).UtcDateTime };
        if (change == "source blocks") target = target with { Blocks = ["source blocked"] };
        if (change == "definition build") target = target with { Strategy = target.Strategy with { Strategy = target.Strategy.Strategy with { ImplementationIdentity = "wrong" } } };
        if (change == "different pair") target = target with { Strategy = target.Strategy with { Comparison = target.Strategy.Comparison! with { PairId = Guid.NewGuid() } } };
        if (change == "different batch") target = target with { Strategy = target.Strategy with { Comparison = target.Strategy.Comparison! with { BatchId = Guid.NewGuid() } } };
        if (change == "daily config") target = target with { Strategy = target.Strategy with { Strategy = target.Strategy.Strategy with
            { Daily = target.Strategy.Strategy.Daily with { ModelSetSha256 = new string('B', 64) } } } };
        if (change == "intraday risk") target = target with { Strategy = target.Strategy with { Strategy = target.Strategy.Strategy with
            { LivePolicy = target.Strategy.Strategy.LivePolicy! with { UseProgressiveProfitProtection = !prior.Strategy.Strategy.LivePolicy!.UseProgressiveProfitProtection } } } };
        if (change == "same source") target = target with { Strategy = prior.Strategy };
        if (change == "memory hold") prior = prior with { Memory = prior.Memory with { DailyLossHold = true } };
        if (change == "invalidated") prior = prior with { ReviewInvalidated = true };
        if (change == "order hold") prior = prior with { TrialOrderReviewRequired = true };
        if (change == "order") prior = prior with { Orders = [new(Guid.NewGuid(), Guid.NewGuid(), null, "security", "FIX", "BUY", 1, 10, "Initial", null, at)] };
        if (change == "initial only") prior = prior with { Continuation = null };
        if (change == "expired") at = new(2026,10,21,20,0,0,TimeSpan.Zero);
        if (change == "next day") at = at.AddDays(1);
        Assert.ThrowsAny<Exception>(() => BrokerLiveSourceSwitchWorkflow.Prepare(prior, target, updated,
            x.Existing.F.TransformAccount!(x.Existing.F.Project()), change != "no storage", x.Existing.F.ImplementationIdentity,
            change == "reviewer" ? "" : "fixture", BrokerTrialCarryForwardTests.Calendar(), at));
        Assert.Null(x.Existing.F.Store.State!.SourceSwitch); Assert.Equal(0, x.Existing.F.Transport.Sent);
    }

    [Theory]
    [InlineData("old host")][InlineData("revision")][InlineData("build")][InlineData("review expired")]
    [InlineData("cash")][InlineData("risk")][InlineData("account block")][InlineData("close during read")]
    public async Task SourceSwitchApprovalRechecksAuthorityAndFreshAccount(string change)
    {
        var x = await SwitchFacts(); var request = x.Prepare(); var f = x.Existing.F;
        if (change == "old host") x.Existing.Grants.Value = new(x.Prior.BindingId, x.Prior.SessionId, x.Prior.Strategy.Fingerprint,
            x.Prior.Strategy.Strategy.ImplementationIdentity, f.Now, "fixture", true);
        if (change == "revision") f.Store.State = x.Prior with { RevisionId = Guid.NewGuid() };
        if (change == "build") f.RunningImplementation = "other";
        if (change == "review expired") f.Now = f.Now.AddMinutes(3);
        var original = f.TransformAccount!;
        if (change == "cash") f.TransformAccount = a => { var p = original(a); return p with { Import = p.Import with
            { Plan = p.Import.Plan with { Observation = p.Import.Plan.Observation with { Cash = 999 } } } }; };
        if (change == "risk") f.TransformAccount = a => { var p = original(a); return p with { Risk = p.Risk with { RevisionId = Guid.NewGuid() } }; };
        if (change == "account block") f.TransformAccount = a => original(a) with { NewRiskBlocks = ["pending order"] };
        if (change == "close during read") f.TransformAccount = a => { var p = original(a); f.Now = new(2026,10,21,20,0,0,TimeSpan.Zero); return p; };
        await Assert.ThrowsAnyAsync<Exception>(() => x.Workflow().ApproveAsync(request));
        Assert.Null(f.Store.State!.SourceSwitch); Assert.Equal(0, f.Transport.Sent);
    }

    [Theory]
    [InlineData("ceiling")][InlineData("cutoff")][InlineData("risk")][InlineData("memory")][InlineData("request id")]
    [InlineData("phase")][InlineData("initial approval")]
    public async Task DurableSourceSwitchRejectsHiddenAuthorityOrFinancialMutation(string change)
    {
        var x = await SwitchFacts(); var saved = await x.Workflow().ApproveAsync(x.Prepare());
        var bad = change switch
        {
            "ceiling" => saved with { Continuation = saved.Continuation! with { Approval = saved.Continuation.Approval with
                { Review = saved.Continuation.Approval.Review with { PurchaseCeiling = saved.EffectiveReview.PurchaseCeiling + 1 } } } },
            "cutoff" => saved with { Continuation = saved.Continuation! with { Approval = saved.Continuation.Approval with
                { Review = saved.Continuation.Approval.Review with { NoTransferScope = saved.EffectiveReview.NoTransferScope! with { PurchaseExpiresAt = saved.SavedAt.AddDays(1) } } } } },
            "risk" => saved with { Account = saved.Account with { Risk = saved.Account.Risk with { RevisionId = Guid.NewGuid() } } },
            "memory" => saved with { Memory = saved.Memory with { HighestClosingValue = 1 } },
            "request id" => saved with { SourceSwitch = saved.SourceSwitch! with { RequestId = Guid.NewGuid() } },
            "phase" => saved with { Phase = BrokerManagedPhase.Paused },
            _ => saved with { Approval = saved.Approval with { Reason = "replaced" } }
        };
        Assert.ThrowsAny<Exception>(() => BrokerManagedSessionRepository.ValidateNext(x.Prior, bad, BrokerTrialCarryForwardTests.Calendar()));
    }

    [Fact]
    public async Task PreviousGrantCannotRunSwitchedSourceEvenWithCurrentBuild()
    {
        var x = await SwitchFacts(); var saved = await x.Workflow().ApproveAsync(x.Prepare()); int calls = 0;
        x.Existing.Grants.Value = new(saved.BindingId, saved.SessionId, x.Prior.Strategy.Fingerprint,
            x.Existing.F.ImplementationIdentity, x.Existing.F.Now, "fixture", true)
            { ContinuationId = saved.Continuation!.Approval.Review.RequestId };
        var host = new BrokerExecutionHost(x.Existing.Grants, new BrokerManagedSessionControls(x.Existing.F.Store),
            (s, ct) => { calls++; return Task.FromResult(s); }, BrokerTrialCarryForwardTests.Calendar(),
            x.Existing.F.ImplementationIdentity, new HostClock(x.Existing.F));
        await host.PollAsync(true); Assert.Equal(0, calls); Assert.Equal("Blocked", host.Status.State);
    }

    [Fact]
    public void UngatedSourceStillRequiresProspectiveConfirmationAndValidData()
    {
        var input = TradingRecommendationTests.LiveInput();
        var strategy = input.Assignment.Strategy;
        var comparison = new PaperComparisonAccount(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "DelphiLive",
            PaperGateProfile.PortfolioRulesOnlyV1, input.SessionOpenUtc, strategy.Daily.StrategyVersionId,
            strategy.PolicyVersionId, null, null);
        var observations = input.LiveInputs.Select(o =>
        {
            var daily = o.Input.DailySetup!;
            var admission = new PaperGateAdmission(comparison.AccountId, comparison.PairId, comparison.BatchId,
                comparison.Profile, Guid.NewGuid(), daily.DelphiRunId, daily.DailyStrategyVersionId,
                strategy.PolicyVersionId, comparison.EffectiveUtc);
            var failedDaily = new DelphiLiveDailySetupQuality(daily.DelphiRunId, daily.CandidateId,
                daily.DailyStrategyVersionId, daily.CommonDelphiComposite,
                daily.SourceLenses.Select(l => new DelphiLiveSourceLensQuality(l.Lens, false, l.IsPublished,
                    l.Rank, l.RankingKey, l.ReasonEvidence, "Breadth")).ToImmutableArray()) { ComparisonAdmission = admission };
            return BrokerLiveObservationPolicy.Bind(o.Input with { DailySetup = failedDaily }, 1, strategy, comparison, false);
        }).ToImmutableArray();
        input = input with { LiveInputs = observations };
        Assert.Contains(TradingRecommendationEngine.Evaluate(input).Recommendations, r => r.Action == "Buy");
        var boundary = observations[0].Input.BarEndUtc;
        var oldMemory = TradingRecommendationEngine.Evaluate(input).Memory;
        input = input with { Assignment = input.Assignment with { EffectiveUtc = boundary },
            PreviousMemory = BrokerLiveSourceSwitchWorkflow.FreshSignals(oldMemory, new DateTimeOffset(boundary)) };
        Assert.DoesNotContain(TradingRecommendationEngine.Evaluate(input).Recommendations, r => r.Action == "Buy");
        input = input with { Assignment = input.Assignment with { EffectiveUtc = input.SessionOpenUtc },
            PreviousMemory = TradingRecommendationMemory.Empty,
            LiveInputs = observations.Select(o => o with { Input = o.Input with { ExactPairPersistedOnTime = false } }).ToImmutableArray() };
        Assert.DoesNotContain(TradingRecommendationEngine.Evaluate(input).Recommendations, r => r.Action == "Buy");
    }

    [Fact]
    public async Task EarlierSessionHashAndJsonRemainUnchangedWhenNoSwitchExists()
    {
        var x = await SwitchFacts(); var json = JsonSerializer.Serialize(x.Prior);
        Assert.DoesNotContain("SourceSwitch", json);
        Assert.Equal(BrokerAccountImport.Hash(x.Prior), BrokerAccountImport.Hash(JsonSerializer.Deserialize<BrokerManagedSession>(json)!));
    }
}
