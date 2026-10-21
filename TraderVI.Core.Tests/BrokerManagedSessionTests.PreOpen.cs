#nullable enable
using Core.Trader;
using Core.Trader.Promotion;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    [Fact]
    public async Task PreOpenApprovalArmsExactHostWithoutExecutionAndStartsPollingAtOpen()
    {
        var x = ContinuedFixture(preOpen: true); var calendar = BrokerTrialCarryForwardTests.Calendar();
        var saved = await x.Workflow().ApproveAsync(x.Approval());
        Assert.True(saved.EffectiveReview.NoTransferScope!.Continuation!.PreOpenApproved);
        Assert.Null(x.Grants.Value); Assert.Equal(0, x.F.Transport.Sent);
        x.F.Store.Restart(); int calls = 0;
        var host = new BrokerExecutionHost(x.Grants, new BrokerManagedSessionControls(x.F.Store),
            (s, ct) => { calls++; return Task.FromResult(s); }, calendar, x.F.ImplementationIdentity, new HostClock(x.F));
        await host.EnableAsync(saved.BindingId, saved.SessionId, saved.Strategy.Fingerprint, "fixture");
        Assert.Equal(saved.EffectiveReview.RequestId, x.Grants.Value!.ContinuationId);
        Assert.Contains("Armed", host.Status.Message);
        await host.PollAsync(true); Assert.Equal(0, calls);
        var open = new DateTimeOffset(calendar.GetSessionBounds(BrokerOctober21Continuation.ExecutionDate).OpenUtc);
        x.F.Now = open.AddTicks(-1); await host.PollAsync(true); Assert.Equal(0, calls);
        Assert.Equal(open, host.Status.NextCheckAt);
        var untouched = x.F.Store.State!.Revision;
        await x.F.Workflow().TickAsync(saved.BindingId);
        Assert.Equal(untouched, x.F.Store.State!.Revision); Assert.Equal(0, x.F.Transport.Sent);
        x.F.Now = open; await host.PollAsync(true); Assert.Equal(1, calls);
        x.F.Now = open.AddHours(6.5); await host.PollAsync(true); Assert.Equal(1, calls);
        Assert.Equal(open.AddHours(6.5), saved.EffectiveReview.NoTransferScope.PurchaseExpiresAt);
    }

    [Theory]
    [InlineData("missing daily")][InlineData("old source")][InlineData("future source")]
    [InlineData("cash change")][InlineData("hold")][InlineData("storage")][InlineData("stale account")]
    public void PreOpenReviewKeepsCurrentAccountRiskStorageAndExplicitDailyRequirements(string change)
    {
        var x = ContinuedFixture(preOpen: true); var calendar = BrokerTrialCarryForwardTests.Calendar();
        var account = x.F.TransformAccount!(x.F.Project());
        var updated = new DateTimeOffset(2026,10,20,20,0,0,TimeSpan.Zero);
        if (change == "old source") updated = updated.AddDays(-1);
        if (change == "future source") updated = x.F.Now.AddSeconds(1);
        if (change == "hold") account = account with { Risk = account.Risk with { DailyLossHold = true } };
        if (change == "cash change") account = account with { Import = account.Import with { Plan = account.Import.Plan with
            { Observation = account.Import.Plan.Observation with { Cash = account.Import.Plan.Observation.Cash + 1 } } } };
        if (change == "stale account") account = account with { Account = account.Account with { ReportedUtc = x.F.Now.UtcDateTime.AddSeconds(-31) } };
        var review = BrokerContinuationReviewPolicy.Prepare(x.Close, x.Continuation, x.After.Strategy,
            updated, account, change != "storage", x.F.ImplementationIdentity, calendar, x.F.Now, change != "missing daily");
        Assert.False(review.CanApprove); Assert.Null(review.Activation); Assert.NotEmpty(review.Blocks);
        Assert.Null(x.F.Store.State!.Continuation); Assert.Equal(0, x.F.Transport.Sent);
    }

    [Fact]
    public async Task PreOpenReviewCoverageCannotAuthorizeExecutionBeforeOpenAndNeedsFreshEvidenceAfterOpen()
    {
        var x = ContinuedFixture(preOpen: true); var calendar = BrokerTrialCarryForwardTests.Calendar();
        var account = x.F.TransformAccount!(x.F.Project());
        var executionEvidence = await new BrokerNoTransferRiskEvidenceSource(x.Scope, calendar, new HostClock(x.F))
            .ReadAsync(account.Import, x.F.Risk, [], default);
        Assert.Null(executionEvidence.Coverage); Assert.NotEmpty(executionEvidence.Blocks);
        Assert.True(BrokerOctober21Continuation.SourceReady(x.Continuation, x.F.Now.AddHours(-17), calendar, x.F.Now, true));
        x.F.Now = new(2026,10,21,13,30,0,TimeSpan.Zero);
        Assert.False(BrokerOctober21Continuation.SourceReady(x.Continuation, x.F.Now.AddHours(-17), calendar, x.F.Now, true));
        Assert.True(BrokerOctober21Continuation.SourceReady(x.Continuation, x.F.Now, calendar, x.F.Now, false));
        var stale = await new BrokerNoTransferRiskEvidenceSource(x.Scope, calendar, new HostClock(x.F))
            .ReadAsync(account.Import, x.F.Risk, [], default);
        Assert.Null(stale.Coverage); // Pre-open observations do not become fresh at 9:30.
    }

    [Theory]
    [InlineData(20,13,0,true)][InlineData(21,13,30,true)][InlineData(21,20,0,true)]
    [InlineData(22,13,0,true)][InlineData(21,13,0,false)]
    public void PreOpenFlagDoesNotAuthorizeAnotherDateOrChangeOrdinaryConfirmationTiming(int day,int hour,int minute,bool preOpen)
    {
        var x = ContinuedFixture(preOpen: true);
        var changed = x.Continuation with { ConfirmedAt = new(2026,10,day,hour,minute,0,TimeSpan.Zero), PreOpenApproved = preOpen };
        Assert.Throws<InvalidOperationException>(() => changed.Validate(BrokerTrialCarryForwardTests.Calendar()));
    }

    [Fact]
    public async Task ArmedStartRechecksChangedAccountAndRetainsOriginalReferences()
    {
        var x = ContinuedFixture(preOpen: true); var saved = await x.Workflow().ApproveAsync(x.Approval());
        x.F.Now = new(2026,10,21,13,30,0,TimeSpan.Zero);
        var original = x.F.TransformAccount!;
        x.F.TransformAccount = a => original(a) with { NewRiskBlocks = ["Changed account at open"] };
        var blocked = await x.F.Workflow().TickAsync(saved.BindingId);
        Assert.Equal("PreOpenAccountRecheckBlocked", blocked.Event); Assert.Equal(0, x.F.Transport.Sent);
        Assert.Equal(saved.Account.Risk.SessionReference, blocked.Account.Risk.SessionReference);
        Assert.Equal(saved.Account.Risk.DrawdownReference, blocked.Account.Risk.DrawdownReference);
        Assert.Equal(saved.Continuation!.Fingerprint, blocked.Continuation!.Fingerprint);
    }
}
