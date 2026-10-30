#nullable enable
using Core.Db;
using Core.Trader.Promotion;
using System;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    [Fact]
    public void BuildReviewExtensionPreservesInstalledEngineRiskSourceConstructor()
    {
        Assert.NotNull(typeof(BrokerOngoingRiskEvidenceSource).GetConstructor([
            typeof(BrokerOngoingManagement), typeof(global::Core.Trader.DelphiLive.ITsxSessionCalendar),
            typeof(Func<System.Collections.Immutable.ImmutableArray<string>, DateTime, System.Threading.CancellationToken,
                Task<System.Collections.Immutable.ImmutableArray<BrokerRiskHoldingBar>>>), typeof(TimeProvider), typeof(bool)]));
    }

    [Theory]
    [InlineData(23, 20, 0)] // Exact Friday close.
    [InlineData(23, 23, 0)]
    [InlineData(24, 16, 0)] // Weekend.
    [InlineData(26, 12, 0)] // Monday before opening evidence exists.
    public async Task OngoingBuildCanBeReviewedAndArmedOutsideSessionWithoutAdvancingRiskOrTrading(int day, int hour, int minute)
    {
        var x = await RiskSyncFixture(); var prior = x.Prior; var f = x.F;
        f.Now = new(2026, 10, day, hour, minute, 0, TimeSpan.Zero);
        var observed = prior.Account.Import.Plan.Observation with { SnapshotAt = f.Now, CheckedAt = f.Now };
        var account = prior.Account with
        {
            Import = prior.Account.Import with { Plan = prior.Account.Import.Plan with { Observation = observed } },
            Account = prior.Account.Account with { ReportedUtc = f.Now.UtcDateTime, TradingDate = new(2026, 10, day) },
            NewRiskBlocks = day == 23 ? [] : [BrokerAccountProjector.RiskSessionContinuationRequired]
        };
        int boundaries = 0;
        var riskSource = new BrokerOngoingRiskEvidenceSource(prior.OngoingManagement!, SelectedSessionCalendar(),
            (_, _, _) => { boundaries++; throw new InvalidOperationException("Build review must not read opening prices."); },
            new HostClock(f), preserveRiskHistory: true);
        var evidence = await riskSource.ReadAsync(account.Import, account.Risk, prior.Orders, default);
        Assert.Empty(evidence.Blocks); Assert.Empty(evidence.Continuations); Assert.NotNull(evidence.Coverage);
        Assert.Equal(0, boundaries);
        f.TransformAccount = _ => account; f.RunningImplementation = "after-hours-build";
        var current = x.Original.Existing.Current with { PreparedUtc = f.Now.UtcDateTime,
            Strategy = prior.Strategy with { Strategy = prior.Strategy.Strategy with { ImplementationIdentity = f.ImplementationIdentity } } };
        // This is a fresh definition check, not a fabricated current market observation.
        var proposal = BrokerImplementationReviewWorkflow.Prepare(prior, current, f.Now, account, true,
            f.ImplementationIdentity, "fixture", SelectedSessionCalendar(), f.Now);
        var grants = x.Original.Existing.Grants;
        var workflow = new BrokerImplementationReviewWorkflow(f.Store, f, grants, SelectedSessionCalendar(), new HostClock(f));
        var saved = await workflow.ApproveAsync(proposal);
        Assert.Equal(prior.Account.Risk, saved.Account.Risk); Assert.Equal(prior.Memory, saved.Memory);
        Assert.Equal(prior.Protection, saved.Protection); Assert.Equal(prior.Orders, saved.Orders);
        Assert.Equal(prior.OngoingManagement, saved.OngoingManagement); Assert.Equal(prior.NewBuysStopped, saved.NewBuysStopped);
        Assert.Equal(prior.CommittedPurchaseCost, saved.CommittedPurchaseCost);
        Assert.Equal(account.NewRiskBlocks, saved.Account.NewRiskBlocks);
        Assert.Null(grants.Value); Assert.Equal(0, f.Transport.Sent);
        BrokerManagedSessionRepository.ValidateNext(prior, saved, SelectedSessionCalendar());
        f.Store.Restart(); Assert.Equal(saved.RevisionId, (await workflow.ApproveAsync(proposal)).RevisionId);
        int ticks = 0;
        var host = new BrokerExecutionHost(grants, new BrokerManagedSessionControls(f.Store),
            (s, _) => { ticks++; return Task.FromResult(s with { Blocks = ["Fresh opening evidence required."] }); },
            SelectedSessionCalendar(), f.ImplementationIdentity, new HostClock(f));
        await host.EnableAsync(saved.BindingId, saved.SessionId, saved.Strategy.Fingerprint, "fixture");
        await host.PollAsync(true); Assert.Equal(0, ticks); Assert.Equal("Waiting", host.Status.State);
        f.Now = new(2026, 10, 26, 13, 29, 59, TimeSpan.Zero);
        await host.PollAsync(true); Assert.Equal(0, ticks);
        f.Now = f.Now.AddSeconds(1);
        await host.PollAsync(true); Assert.Equal(1, ticks); Assert.Equal("Blocked", host.Status.State);
        Assert.Equal(0, f.Transport.Sent); Assert.Equal(prior.Account.Risk, f.Store.State!.Account.Risk);
        var openingImport = account.Import with { Plan = account.Import.Plan with { Observation =
            observed with { SnapshotAt = f.Now, CheckedAt = f.Now } } };
        // Production evidence (without the build-review option) still refuses missing close/open prices.
        var liveEvidence = await x.Original.Source(prior.OngoingManagement!).ReadAsync(openingImport,
            prior.Account.Risk, prior.Orders, default);
        Assert.NotEmpty(liveEvidence.Blocks); Assert.Empty(liveEvidence.Continuations);
        var unapproved = await riskSource.ReadAsync(openingImport,
            prior.Account.Risk with { OngoingAuthorization = null }, prior.Orders, default);
        Assert.NotEmpty(unapproved.Blocks); Assert.Empty(unapproved.Continuations);
    }

    [Theory]
    [InlineData("stale-account")][InlineData("other-block")][InlineData("future-risk")]
    [InlineData("changed-risk")][InlineData("stale-definition")][InlineData("expired-review")]
    [InlineData("host-on")][InlineData("missing-protection")][InlineData("changed-cash")]
    public async Task AfterHoursBuildReviewRetainsAccountDefinitionHostAndRiskGuards(string reason)
    {
        var x = await RiskSyncFixture(); var prior = x.Prior; var f = x.F;
        f.Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        var account = prior.Account with {
            Import = prior.Account.Import with { Plan = prior.Account.Import.Plan with { Observation =
                prior.Account.Import.Plan.Observation with { SnapshotAt = f.Now, CheckedAt = f.Now } } },
            Account = prior.Account.Account with { TradingDate = new(2026,10,26), ReportedUtc = f.Now.UtcDateTime },
            NewRiskBlocks = [BrokerAccountProjector.RiskSessionContinuationRequired] };
        if (reason == "stale-account") account = account with { Account = account.Account with { ReportedUtc = f.Now.AddMinutes(-1).UtcDateTime } };
        if (reason == "other-block") account = account with { NewRiskBlocks = ["Unresolved integrity incident blocks new exposure."] };
        if (reason == "future-risk") account = account with { Risk = account.Risk with { TradingDate = new(2026,10,27) } };
        if (reason == "changed-risk") account = account with { Risk = account.Risk with { Through = account.Risk.Through.AddSeconds(1) } };
        if (reason == "changed-cash") account = account with { Import = account.Import with { Plan = account.Import.Plan with {
            Observation = account.Import.Plan.Observation with { Cash = account.Import.Plan.Observation.Cash + 1 } } } };
        if (reason == "missing-protection") prior = prior with { Protection = [] };
        f.Store.State = prior; f.TransformAccount = _ => account; f.RunningImplementation = "after-hours-build";
        var current = x.Original.Existing.Current with { PreparedUtc = f.Now.UtcDateTime,
            Strategy = prior.Strategy with { Strategy = prior.Strategy.Strategy with { ImplementationIdentity = f.ImplementationIdentity } } };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            var proposal = BrokerImplementationReviewWorkflow.Prepare(prior, current,
                reason == "stale-definition" ? f.Now.AddMinutes(-3) : f.Now, account, true, f.ImplementationIdentity,
                "fixture", SelectedSessionCalendar(), f.Now);
            if (reason == "expired-review") f.Now = f.Now.AddMinutes(3);
            if (reason == "host-on") x.Original.Existing.Grants.Value = new(prior.BindingId, prior.SessionId,
                prior.Strategy.Fingerprint, f.ImplementationIdentity, f.Now, "fixture", true);
            await new BrokerImplementationReviewWorkflow(f.Store, f, x.Original.Existing.Grants,
                SelectedSessionCalendar(), new HostClock(f)).ApproveAsync(proposal);
        });
        Assert.Null(f.Store.State!.ImplementationReview); Assert.Equal(0, f.Transport.Sent);
    }
}
