#nullable enable
using Core.Db;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    internal static BrokerOctober21Continuation SyntheticOctober21Continuation() => ContinuedFixture().Continuation;
    private sealed record ContinuedFacts(Fixture F, BrokerManagedSession Close, BrokerOctober21Continuation Continuation,
        BrokerNativeTrialObservation Before, BrokerNativeTrialObservation After, BrokerNoTransferSessionScope Scope,
        BrokerExecutionQualificationBundle Packet, ContinuationRisks Risks, HostGrants Grants)
    {
        internal BrokerContinuationWorkflow Workflow() => new(F.Store, Risks, F, Grants,
            BrokerTrialCarryForwardTests.Calendar(), new HostClock(F));
        internal BrokerExecutionApproval Approval()
        {
            var review = BrokerFirstExecutionReviewPolicy.Prepare(After.Strategy with { PreparedUtc = F.Now.UtcDateTime },
                F.TransformAccount!(F.Project()), Packet, F.ImplementationIdentity, F.Now);
            Assert.True(review.CanApprove, string.Join(";", review.Blocks));
            return new(review.Activation! with { RequestId = Scope.ScopeId,
                PurchaseCeiling = Math.Min(review.PurchaseCeiling, Close.EffectiveReview.PurchaseCeiling - Close.CommittedPurchaseCost) },
                "fixture", "October 21 only; synthetic offline test");
        }
    }
    private static ContinuedFacts ContinuedFixture()
    {
        var (f, _, native20, scope20) = NextSessionFacts();
        var calendar = BrokerTrialCarryForwardTests.Calendar();
        var originalRisk = f.Risk;
        var originalAccount = f.Project();
        var approval20 = f.Approval(ongoing: true) with { Review = f.Approval(ongoing: true).Review with
        { NoTransferScope = scope20, RequiredExits = [], RetainedPositions = [] } };
        var closeAt = new DateTimeOffset(calendar.GetSessionBounds(scope20.Session).CloseUtc).AddMilliseconds(400);
        var close = new BrokerManagedSession(approval20.Review.RequestId, f.Binding.BindingId, 49, Guid.NewGuid(), Guid.NewGuid(),
            "StrategyEvaluated", closeAt, approval20, BrokerManagedPhase.Active, f.Now, originalAccount, [],
            TradingRecommendationMemory.Empty with { TradingDate = scope20.Session, HighestClosingValue = originalRisk.DrawdownReference.Value },
            [], ["The supervised no-transfer scope is expired. New buys stay stopped."]);
        f.Store.State = close; f.Store.Calendar = calendar;
        f.Now = new(2026, 10, 21, 14, 0, 0, TimeSpan.Zero);
        var continuation = new BrokerOctober21Continuation(close.SessionId, close.RevisionId, BrokerAccountImport.Hash(close),
            close.SavedAt, originalRisk, scope20, f.Now, "fixture", true)
            { PurchaseExpiresAt = calendar.GetSessionBounds(new(2026,10,21)).CloseUtc };
        var scope = continuation.Scope(calendar); scope.Validate(calendar);
        BrokerNativeTrialObservation Observe(int process, DateTimeOffset started) => native20 with
        {
            ObservationId = Guid.NewGuid(), ProcessId = process, ProcessStartedAt = started, CapturedAt = f.Now,
            Account = native20.Account with { SnapshotAt = f.Now, CheckedAt = f.Now },
            Strategy = FirstStrategyReview(f), NormalUpdatedAt = f.Now, ContinuationReview = continuation,
            Buys = native20.Buys.Select(b => b with { LastCheckedAt = f.Now }).ToImmutableArray(),
            Sales = native20.Sales.Select(s => s with { LastCheckedAt = f.Now }).ToImmutableArray()
        };
        var before = Observe(10, f.Now.AddHours(-1)); f.Now = f.Now.AddMinutes(2);
        var after = Observe(11, f.Now.AddMinutes(-1));
        var expected = continuation.Apply(originalRisk, calendar);
        f.Risk = expected;
        f.TransformAccount = account =>
        {
            var observation = after.Account with { SnapshotAt = f.Now, CheckedAt = f.Now };
            var import = account.Import with { Plan = account.Import.Plan with { Observation = observation } };
            var coverage = new BrokerNoTransferRiskEvidenceSource(scope, calendar, new HostClock(f))
                .ReadAsync(import, expected, [], CancellationToken.None).GetAwaiter().GetResult();
            return BrokerAccountProjector.Project(f.Binding, import, expected, coverage.Coverage, f.Now) with
                { NewRiskBlocks = coverage.Blocks };
        };
        var packet = new BrokerExecutionQualificationBundle(f.Binding.BindingId, f.Strategy.Fingerprint,
            f.ImplementationIdentity, scope.Session, f.Now, scope.PurchaseExpiresAt!.Value, "fixture",
            BrokerActivationReview.ChecksFor(scope).Select(check => new BrokerQualification(check, f.Binding.BindingId,
                f.Strategy.Fingerprint, f.Now, Fixture.Hash, "Synthetic fixture; never native evidence") { FirstSessionTrialOnly = true }).ToImmutableArray(), scope);
        return new(f, close, continuation, before, after, scope, packet, new(originalRisk, calendar, () => f.Now), new());
    }

    private sealed class ContinuationRisks(BrokerAccountRiskState initial, ITsxSessionCalendar calendar,
        Func<DateTimeOffset> now) : IBrokerAccountRiskContinuationStore
    {
        internal BrokerAccountRiskState State = initial;
        internal BrokerRiskContinuationReceipt? Receipt;
        internal bool LoseResponse;
        internal int Appends;
        public Task<BrokerAccountRiskState> ReadAsync(Guid id, CancellationToken ct) => Task.FromResult(State);
        public Task<BrokerRiskContinuationReceipt?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Receipt?.Request.RequestId == id ? Receipt : null);
        public Task<BrokerRiskContinuationReceipt> AppendAsync(BrokerRiskContinuationRequest request, CancellationToken ct)
        {
            State = BrokerAccountRiskContinuation.Apply(State, request, calendar, now());
            Receipt = new(request, State, now()); Appends++;
            if (LoseResponse) { LoseResponse = false; throw new IOException("Synthetic lost risk acknowledgement"); }
            return Task.FromResult(Receipt);
        }
    }

    [Fact]
    public async Task October21PreservesOriginalAssignmentAndFinancialHistoryAndRequiresSeparateEnable()
    {
        var x = ContinuedFixture(); var old = x.Close;
        BrokerNativeTrialQualification.ValidateRecovery(x.Before, x.After, x.Scope, BrokerTrialCarryForwardTests.Calendar(), x.F.Now);
        var saved = await x.Workflow().ApproveAsync(x.Approval());
        Assert.Equal(old.Approval.Fingerprint, saved.Approval.Fingerprint);
        Assert.Equal(old.SessionId, saved.SessionId); Assert.Equal(old.ActivatedAt, saved.ActivatedAt);
        Assert.Equal(BrokerAccountImport.Hash(old.Memory), BrokerAccountImport.Hash(saved.Memory));
        Assert.Equal(old.Orders, saved.Orders); Assert.Equal(old.Protection, saved.Protection);
        Assert.Equal(old.Account.Risk.SessionReference, saved.Account.Risk.SessionReference);
        Assert.Equal(old.Account.Risk.DrawdownReference, saved.Account.Risk.DrawdownReference);
        Assert.Equal(old.Account.Risk.LastCompletedSession, saved.Account.Risk.LastCompletedSession);
        Assert.Equal(old.Account.Risk.Through, saved.Account.Risk.Through);
        Assert.Equal(old.Account.Risk.TrialCarry!.Fingerprint, saved.Account.Risk.TrialCarry!.Fingerprint);
        Assert.Equal(new DateOnly(2026,10,21), saved.EffectiveReview.Session);
        Assert.Null(x.Grants.Value); Assert.Equal(0, x.F.Transport.Sent);
        x.F.Store.Restart();
        Assert.Equal(saved.Continuation!.Fingerprint, x.F.Store.State!.Continuation!.Fingerprint);
        Assert.Equal(saved.Revision, (await x.Workflow().ApproveAsync(x.Approval())).Revision);
        Assert.Equal(1, x.Risks.Appends);
    }

    [Theory]
    [InlineData("risk acknowledgement")]
    [InlineData("session acknowledgement")]
    public async Task InterruptedContinuationRecoversOriginalRequestWithoutRepeatingAnyTradeOrRiskEvent(string lost)
    {
        var x = ContinuedFixture(); var approval = x.Approval();
        if (lost == "risk acknowledgement") x.Risks.LoseResponse = true;
        else x.F.Store.LoseResponseAtEvent = "October21ContinuationApproved";
        await Assert.ThrowsAnyAsync<Exception>(() => x.Workflow().ApproveAsync(approval));
        Assert.Null(x.Grants.Value); Assert.Equal(0, x.F.Transport.Sent);
        x.F.Store.Restart();
        var saved = await x.Workflow().ApproveAsync(approval);
        Assert.Equal(x.Continuation.RiskRequest().RequestId, saved.Continuation!.Approval.Review.RequestId);
        Assert.Equal(1, x.Risks.Appends); Assert.Empty(saved.Orders); Assert.Equal(0, x.F.Transport.Sent);
    }

    [Theory]
    [InlineData("revision")][InlineData("risk hold")][InlineData("memory hold")][InlineData("order")]
    [InlineData("source")][InlineData("build")][InlineData("cash")][InlineData("no confirmation")]
    [InlineData("missing check")][InlineData("old host")][InlineData("expired review")][InlineData("after close")][InlineData("refilled ceiling")]
    public async Task ContinuationRejectsChangedOrIncompleteEvidenceWithoutSavingAuthority(string change)
    {
        var x = ContinuedFixture(); var approval = x.Approval();
        if (change == "revision") x.F.Store.State = x.Close with { RevisionId = Guid.NewGuid() };
        if (change == "risk hold") x.Risks.State = x.Risks.State with { DailyLossHold = true };
        if (change == "memory hold") x.F.Store.State = x.Close with { Memory = x.Close.Memory with { CapitalReviewRequired = true } };
        if (change == "order") x.F.Store.State = x.Close with { Orders = [new(Guid.NewGuid(), Guid.NewGuid(), null, "security", "FIX", "BUY", 1, 10, "Initial", null, x.F.Now)] };
        if (change == "source") approval = approval with { Review = approval.Review with { Strategy = approval.Review.Strategy with { PaperAccountId = Guid.NewGuid() } } };
        if (change == "build") x.F.RunningImplementation = "other build";
        if (change == "cash") { var source = x.F.TransformAccount!; x.F.TransformAccount = a => { var p = source(a); return p with { Import = p.Import with { Plan = p.Import.Plan with { Observation = p.Import.Plan.Observation with { Cash = 999 } } } }; }; }
        if (change == "no confirmation") approval = approval with { Review = approval.Review with { NoTransferScope = x.Scope with { Continuation = x.Continuation with { NoOutsideChangesConfirmed = false } } } };
        if (change == "missing check") approval = approval with { Review = approval.Review with { Qualifications = approval.Review.Qualifications.RemoveAt(0) } };
        if (change == "refilled ceiling") approval = approval with { Review = approval.Review with { PurchaseCeiling = x.Close.EffectiveReview.PurchaseCeiling + 1 } };
        if (change == "old host") x.Grants.Value = new(x.Close.BindingId, x.Close.SessionId, x.F.Strategy.Fingerprint, x.F.ImplementationIdentity, x.F.Now, "fixture", true);
        if (change == "expired review") x.F.Now = x.F.Now.AddMinutes(3);
        if (change == "after close") x.F.Now = new(2026,10,21,20,0,0,TimeSpan.Zero);
        await Assert.ThrowsAnyAsync<Exception>(() => x.Workflow().ApproveAsync(approval));
        Assert.Null(x.F.Store.State!.Continuation); Assert.Equal(0, x.Risks.Appends); Assert.Equal(0, x.F.Transport.Sent);
    }

    [Fact]
    public async Task OldHostGrantCannotRunContinuationEvenWhenStrategyAndBuildAreIdentical()
    {
        var x = ContinuedFixture(); var saved = await x.Workflow().ApproveAsync(x.Approval()); int calls = 0;
        x.Grants.Value = new(saved.BindingId, saved.SessionId, saved.Strategy.Fingerprint, x.F.ImplementationIdentity,
            x.F.Now.AddMinutes(-1), "fixture", true);
        var host = new BrokerExecutionHost(x.Grants, new BrokerManagedSessionControls(x.F.Store),
            (s, ct) => { calls++; return Task.FromResult(s); }, BrokerTrialCarryForwardTests.Calendar(), x.F.ImplementationIdentity, new HostClock(x.F));
        await host.PollAsync(true); Assert.Equal(0, calls); Assert.Equal("Blocked", host.Status.State);
        await host.EnableAsync(saved.BindingId, saved.SessionId, saved.Strategy.Fingerprint, "fixture");
        Assert.Equal(saved.EffectiveReview.RequestId, x.Grants.Value!.ContinuationId);
        await host.PollAsync(true); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RiskOnlySaveCannotQualifyOldPermissionAndOrdinaryResumeCannotRenewExpiry()
    {
        var x = ContinuedFixture(); var calendar = BrokerTrialCarryForwardTests.Calendar();
        var import = x.F.TransformAccount!(x.F.Project()).Import;
        var old = await new BrokerNoTransferRiskEvidenceSource(x.Continuation.PreviousScope, calendar, new HostClock(x.F))
            .ReadAsync(import, x.F.Risk, [], default);
        Assert.Null(old.Coverage); Assert.NotEmpty(old.Blocks);
        var saved = await x.Workflow().ApproveAsync(x.Approval());
        x.F.Now = new(2026,10,21,20,0,0,TimeSpan.Zero);
        var expired = await new BrokerNoTransferRiskEvidenceSource(x.Scope, calendar, new HostClock(x.F))
            .ReadAsync(import, x.F.Risk, [], default);
        Assert.Null(expired.Coverage); Assert.NotEmpty(expired.Blocks);
        await x.F.Workflow().StopNewBuysAsync(saved.BindingId); await x.F.Workflow().ResumeBuyingAsync(saved.BindingId);
        Assert.Equal(x.Scope.PurchaseExpiresAt, x.F.Store.State!.EffectiveReview.NoTransferScope!.PurchaseExpiresAt);
        var nextDay = x.Scope with { Session = new(2026,10,22) };
        Assert.ThrowsAny<Exception>(() => nextDay.Validate(calendar));
    }

    [Fact]
    public void NewBuildRequiresItsOwnNativeRecoveryAndPreservesEarlierPacketBytes()
    {
        var x = ContinuedFixture(); var calendar = BrokerTrialCarryForwardTests.Calendar();
        var dir = Path.Combine(Path.GetTempPath(), "TraderVI-continuation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var old = Path.Combine(dir, "execution-qualification.dat"); File.WriteAllText(old, "Preserved historical proof");
            var store = new BrokerNativeTrialEvidenceFileStore(Path.Combine(dir, "execution-qualification-20261021-fixture.dat"), new HostProtector());
            store.Capture(x.Before);
            Assert.Throws<InvalidOperationException>(() => store.Complete(x.Before, x.Scope, calendar, "fixture", x.Before.CapturedAt));
            var changed = x.After with { Strategy = x.After.Strategy with { Strategy = x.F.Strategy with
                { Strategy = x.F.Strategy.Strategy with { ImplementationIdentity = "changed build" } } } };
            Assert.Throws<InvalidOperationException>(() => store.Complete(changed, x.Scope, calendar, "fixture", x.F.Now));
            var packet = store.Complete(x.After, x.Scope, calendar, "fixture", x.F.Now);
            Assert.Equal(9, packet.Qualifications.Length); Assert.All(packet.Qualifications, q => Assert.True(q.FirstSessionTrialOnly));
            Assert.Equal("Preserved historical proof", File.ReadAllText(old));
            Assert.Equal(x.Continuation.Fingerprint, store.ReadCheckpoint(new(2026,10,21))!.ContinuationReview!.Fingerprint);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static void OfferContinuationSignal(ContinuedFacts x)
    {
        var f = x.F; var market = TradingRecommendationTests.LiveInput();
        var offset = f.Now.UtcDateTime.Date - market.EvaluatedUtc.Date;
        var date = DateOnly.FromDateTime(f.Now.UtcDateTime);
        DelphiLiveFiveMinuteSeries Rebase(DelphiLiveFiveMinuteSeries series) => new(series.Symbol, date,
            series.SessionOpenUtc + offset, series.OperationalContinuityStartUtc + offset,
            series.Bars.Select(b => new DelphiLiveFiveMinuteBar(b.ObservationId, b.Symbol, date,
                b.StartUtc + offset, b.EndUtc + offset, b.Open, b.High, b.Low, b.Close, b.Volume,
                b.ReceivedUtc + offset, b.Provider, b.SourceContractVersion, b.Disposition)));
        var comparison = f.Strategy.Comparison!;
        var observations = market.LiveInputs.Select(item =>
        {
            var source = item.Input; var daily = source.DailySetup!;
            return BrokerLiveObservationPolicy.Bind(source with
            {
                BarEndUtc = source.BarEndUtc + offset, EvaluatedUtc = source.EvaluatedUtc + offset,
                Stock = Rebase(source.Stock), Xiu = Rebase(source.Xiu), Policy = f.Strategy.Strategy.ReadLivePolicy(),
                DailySetup = new DelphiLiveDailySetupQuality(daily.DelphiRunId, daily.CandidateId,
                    f.Strategy.Strategy.Daily.StrategyVersionId, daily.CommonDelphiComposite, daily.SourceLenses)
                    { ComparisonAdmission = new(comparison.AccountId, comparison.PairId, comparison.BatchId,
                        comparison.Profile, Guid.NewGuid(), daily.DelphiRunId, f.Strategy.Strategy.Daily.StrategyVersionId,
                        f.Strategy.Strategy.PolicyVersionId, comparison.EffectiveUtc) }
            }, 1, f.Strategy.Strategy, comparison, false);
        }).ToImmutableArray();
        f.TransformInput = input => input with { FrozenRunId = market.FrozenRunId, Candidates = market.Candidates,
            LiveInputs = observations, Quotes = [new("ABC", f.Now.UtcDateTime, f.Now.UtcDateTime.AddSeconds(1),
                105m, 104.99m, 105.01m, "TmxQuoteFieldsV1")] };
    }

    [Theory]
    [InlineData("eligible")][InlineData("preflight crosses close")][InlineData("intent save crosses close")]
    public async Task ContinuedNormalCanSelectANeverOwnedStockButBothCloseGuardsPreventTransmission(string timing)
    {
        var x = ContinuedFixture(); await x.Workflow().ApproveAsync(x.Approval());
        OfferContinuationSignal(x);
        var close = new DateTimeOffset(BrokerTrialCarryForwardTests.Calendar().GetSessionBounds(new(2026,10,21)).CloseUtc);
        if (timing == "preflight crosses close") x.F.Transport.BeforePrepare = () => x.F.Now = close;
        if (timing == "intent save crosses close") x.F.Store.AfterAppend = evt => { if (evt == "OrderIntentReserved") x.F.Now = close; };
        var saved = await x.F.Workflow().TickAsync(x.Close.BindingId);
        if (timing == "eligible")
        {
            Assert.Equal(1, x.F.Transport.Sent); var order = Assert.Single(saved.Orders);
            Assert.Equal("ABC", order.Symbol); Assert.DoesNotContain(x.Before.Account.Orders, o => o.Symbol == order.Symbol);
        }
        else
        {
            Assert.Equal(0, x.F.Transport.Sent);
            Assert.Equal(timing == "preflight crosses close" ? "TrialPurchasesEnded" : "ReservedBuyExpiredBeforeSend", saved.Event);
            if (timing == "preflight crosses close") Assert.Empty(saved.Orders);
            else { Assert.Single(saved.Orders); Assert.True(saved.TrialOrderReviewRequired); }
        }
    }
}
