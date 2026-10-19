#nullable enable
using Core.Db;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed partial class BrokerManagedSessionTests
{
    private static void AdvanceVerifiedRiskDay(Fixture f, int days = 1)
    {
        f.Now = f.Now.AddDays(days);
        // Synthetic result of the separately tested, ordered closing/opening continuation workflow.
        var date = DateOnly.FromDateTime(f.Now.UtcDateTime);
        f.Risk = f.Risk with { RevisionId = Guid.NewGuid(), LastCompletedSession = f.Risk.TradingDate,
            TradingDate = date, Through = f.Now.AddHours(-1), DailyLossHold = false,
            SessionReference = new(1100, BrokerRiskReferenceSource.VerifiedSessionOpening, Guid.NewGuid(), f.Now.AddHours(-1)) };
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task OngoingAssignmentKeepsIdentityAndBuyingModeAcrossVerifiedDays(bool stopped)
    {
        var f = new Fixture();
        await f.Workflow().ApproveAsync(f.Approval(retain: true, ongoing: true));
        var first = await f.Workflow().TickAsync(f.Binding.BindingId);
        if (stopped) await f.Workflow().StopNewBuysAsync(f.Binding.BindingId);
        var protectedAt = first.Protection.Single().EstablishedAt;
        for (int day = 0; day < 2; day++)
        {
            AdvanceVerifiedRiskDay(f); f.Store.Restart();
            var restored = await f.Workflow().TickAsync(f.Binding.BindingId);
            Assert.Equal(BrokerManagedPhase.Active, restored.Phase);
            Assert.Equal(first.SessionId, restored.SessionId); Assert.Equal(first.Approval.Fingerprint, restored.Approval.Fingerprint);
            Assert.Equal(first.ActivatedAt, restored.ActivatedAt); Assert.Equal(protectedAt, restored.Protection.Single().EstablishedAt);
            Assert.Equal(stopped, restored.NewBuysStopped); Assert.Equal(f.Risk.TradingDate, restored.Memory.TradingDate);
            Assert.Equal(f.Risk.SessionReference, restored.Account.Risk.SessionReference);
        }
        Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task OngoingTransitionStillWaitsForReconciledRequiredExitAcrossDays()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval(ongoing: true));
        var exit = (await f.Workflow().TickAsync(f.Binding.BindingId)).Orders.Single();
        AdvanceVerifiedRiskDay(f); f.Store.Restart(); f.OfferBuy = true;
        var waiting = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Null(waiting.ActivatedAt); Assert.Equal(1, f.Transport.Sent);
        f.Facts.Add(f.Fact(exit.ExternalId, "AAA", "SELL", 10, 110, f.Now)); f.Cash = 1110;
        var active = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.NotNull(active.ActivatedAt); Assert.Equal(1, f.Transport.Sent);
        f.Now = f.Now.AddMinutes(6);
        var buying = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal("BUY", buying.Orders.Last().Side); Assert.Equal(2, f.Transport.Sent);
    }

    [Fact]
    public async Task OngoingRestartReplaysPriorDayBreachAfterAPriceRebound()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval(retain: true, ongoing: true));
        var active = await f.Workflow().TickAsync(f.Binding.BindingId);
        var at = active.ActivatedAt!.Value.UtcDateTime;
        AdvanceVerifiedRiskDay(f); f.Store.Restart();
        f.TransformInput = input => input with { Bars = ImmutableDictionary<string, ImmutableArray<TradingPriceBar>>.Empty.Add("AAA",
            [new(Guid.NewGuid(), at, at.AddMinutes(5), 5, 10, 10, 8, 9),
             new(Guid.NewGuid(), f.Now.UtcDateTime.AddMinutes(-6), f.Now.UtcDateTime.AddMinutes(-1), 5, 11, 11, 11, 11)]) };
        var recovered = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(at, active.ProtectionReplayFromUtc);
        Assert.Equal("HardLoss", recovered.Memory.Symbols["AAA"].ExitReason);
        Assert.Equal("SELL", Assert.Single(recovered.Orders).Side);
        Assert.True(recovered.ProtectionReplayFromUtc > at); Assert.Equal(1, f.Transport.Sent);
    }

    [Fact]
    public async Task UncontinuedRiskDayKeepsDailyHoldAndProtectionWithoutUsingYesterdaysOpening()
    {
        var f = new Fixture(); f.Risk = f.Risk with { DailyLossHold = true };
        await f.Workflow().ApproveAsync(f.Approval(retain: true, ongoing: true));
        var active = await f.Workflow().TickAsync(f.Binding.BindingId);
        f.Now = f.Now.AddDays(1); f.Store.Restart();
        f.TransformInput = input => input with { Bars = ImmutableDictionary<string, ImmutableArray<TradingPriceBar>>.Empty.Add("AAA",
            [new(Guid.NewGuid(), f.Now.UtcDateTime.AddMinutes(-6), f.Now.UtcDateTime.AddMinutes(-1), 5, 10, 10, 8, 9)]) };
        var recovered = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.True(recovered.Memory.DailyLossHold); Assert.Equal(active.Memory.TradingDate, recovered.Memory.TradingDate);
        Assert.Equal("SELL", Assert.Single(recovered.Orders).Side); Assert.Equal(1, f.Transport.Sent);
        Assert.Equal(active.Account.Risk.SessionReference, recovered.Account.Risk.SessionReference);
    }

    [Fact]
    public async Task VerifiedNewDayClearsOnlyDailyHoldAndKeepsCapitalHoldAndStoppedBuying()
    {
        var f = new Fixture(); f.Risk = f.Risk with { DailyLossHold = true, CapitalReviewRequired = true };
        await f.Workflow().ApproveAsync(f.Approval(retain: true, ongoing: true));
        await f.Workflow().TickAsync(f.Binding.BindingId); await f.Workflow().StopNewBuysAsync(f.Binding.BindingId);
        AdvanceVerifiedRiskDay(f); f.Store.Restart();
        var restored = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.False(restored.Memory.DailyLossHold); Assert.True(restored.Memory.CapitalReviewRequired);
        Assert.True(restored.NewBuysStopped); Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task UncertainBuyRetainsItsReservationAcrossDaysAndNeverResends()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval(ongoing: true));
        var exit = (await f.Workflow().TickAsync(f.Binding.BindingId)).Orders.Single();
        f.Facts.Add(f.Fact(exit.ExternalId, "AAA", "SELL", 10, 110, f.Now)); f.Cash = 1110;
        await f.Workflow().TickAsync(f.Binding.BindingId); f.Now = f.Now.AddMinutes(6); f.OfferBuy = true;
        f.Transport.ThrowOnSubmit = true;
        var pending = await f.Workflow().TickAsync(f.Binding.BindingId);
        var original = pending.Orders.Last(); var cost = pending.CommittedPurchaseCost;
        AdvanceVerifiedRiskDay(f); f.Store.Restart();
        var recovered = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(original, recovered.Orders.Last()); Assert.Equal(cost, recovered.CommittedPurchaseCost);
        Assert.Equal(2, f.Transport.Sent); Assert.Equal(BrokerManagedPhase.NeedsAttention, recovered.Phase);
    }

    [Theory]
    [InlineData("date")] [InlineData("time")] [InlineData("risk-future")] [InlineData("risk-backward")]
    public async Task OngoingRestartRejectsStaleOrRegressedAccountState(string problem)
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval(retain: true, ongoing: true));
        await f.Workflow().TickAsync(f.Binding.BindingId); AdvanceVerifiedRiskDay(f); f.Store.Restart();
        f.TransformAccount = a => problem switch
        {
            "date" => a with { Account = a.Account with { TradingDate = a.Account.TradingDate.AddDays(-1) } },
            "time" => a with { Account = a.Account with { ReportedUtc = f.Now.UtcDateTime.AddMinutes(-1) } },
            "risk-future" => a with { Risk = a.Risk with { TradingDate = a.Account.TradingDate.AddDays(1) } },
            _ => a with { Risk = a.Risk with { Through = f.Now.AddDays(-3) } }
        };
        var result = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal("AccountContinuationBlocked", result.Event); Assert.Equal(0, f.Transport.Sent);
        f.TransformAccount = null;
        Assert.Equal(BrokerManagedPhase.Active, (await f.Workflow().TickAsync(f.Binding.BindingId)).Phase);
    }

    [Fact]
    public async Task ChangedImplementationBlocksEvenTransitionSellsButStillRecoversOriginalOrders()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval(ongoing: true));
        f.RunningImplementation = "different-build";
        Assert.Equal("ImplementationChanged", (await f.Workflow().TickAsync(f.Binding.BindingId)).Event);
        Assert.Equal(0, f.Transport.Sent); f.RunningImplementation = null;
        var exit = (await f.Workflow().TickAsync(f.Binding.BindingId)).Orders.Single();
        f.Facts.Add(f.Fact(exit.ExternalId, "AAA", "SELL", 10, 110, f.Now));
        f.RunningImplementation = "different-build"; AdvanceVerifiedRiskDay(f); f.Store.Restart();
        var recovered = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.True(recovered.Orders.Single().Reconciled); Assert.Equal("ImplementationChanged", recovered.Event);
        Assert.Null(recovered.ActivatedAt); Assert.Equal(1, f.Transport.Sent);
    }

    [Fact]
    public async Task OngoingScopeIsExplicitImmutableAndDoesNotUpgradeOlderApprovals()
    {
        var f = new Fixture(); var old = f.Approval();
        Assert.DoesNotContain("Duration", JsonSerializer.Serialize(old));
        var ongoing = old with { Review = old.Review with { Duration = BrokerExecutionDuration.Ongoing } };
        Assert.NotEqual(old.Fingerprint, ongoing.Fingerprint);
        await f.Workflow().ApproveAsync(old);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workflow().ApproveAsync(ongoing));
        var state = f.Store.State!;
        var changed = state with { Approval = ongoing, Revision = state.Revision + 1, PreviousRevisionId = state.RevisionId, RevisionId = Guid.NewGuid() };
        Assert.Throws<InvalidOperationException>(() => BrokerManagedSessionRepository.ValidateNext(state, changed));
        AdvanceVerifiedRiskDay(f); f.Store.Restart();
        Assert.Equal(BrokerManagedPhase.Paused, (await f.Workflow().TickAsync(f.Binding.BindingId)).Phase);
        Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task InvalidDurationOrChangedBuildCannotReceiveApproval()
    {
        var f = new Fixture(); var approval = f.Approval(ongoing: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workflow().ApproveAsync(
            approval with { Review = approval.Review with { Duration = (BrokerExecutionDuration)99 } }));
        f.RunningImplementation = "different-build";
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workflow().ApproveAsync(approval));
        Assert.Null(f.Store.State); Assert.Equal(0, f.Transport.Sent);
    }

    [Fact]
    public async Task OngoingCeilingKeepsCompletedPurchaseCostsAfterADateChange()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval(ongoing: true));
        var exit = (await f.Workflow().TickAsync(f.Binding.BindingId)).Orders.Single();
        f.Facts.Add(f.Fact(exit.ExternalId, "AAA", "SELL", 10, 110, f.Now)); f.Cash = 1110;
        await f.Workflow().TickAsync(f.Binding.BindingId); f.Now = f.Now.AddMinutes(6); f.OfferBuy = true;
        var buying = await f.Workflow().TickAsync(f.Binding.BindingId); var buy = buying.Orders.Last();
        f.Facts.Add(f.Fact(buy.ExternalId, "BBB", "BUY", buy.Quantity, buy.Quantity * 10 + 1, f.Now));
        f.Cash -= buy.Quantity * 10 + 1; f.OfferBuy = false;
        var filled = await f.Workflow().TickAsync(f.Binding.BindingId);
        AdvanceVerifiedRiskDay(f); f.Store.Restart();
        var restored = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal(buy.Quantity * 10 + 1, restored.CommittedPurchaseCost);
        Assert.Equal(filled.Approval.Review.PurchaseCeiling, restored.Approval.Review.PurchaseCeiling);
        Assert.Equal(2, f.Transport.Sent);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task UnsupportedFamilyCannotStartLiquidatingBeforeItsAdapterExists(bool normalComparison)
    {
        var f = new Fixture(); var approval = f.Approval(ongoing: true); var policy = DelphiLivePolicyDefinition.Version1;
        var live = new BrokerManagedStrategy(f.Strategy.Strategy with { Family = TradingStrategyFamily.DelphiLive,
            ShadowPolicy = null, LivePolicy = policy, PolicyVersionId = policy.PolicyVersionId,
            Selection = CompleteTradingStrategy.LiveSelection, MaximumHoldings = policy.MaximumHoldings },
            Guid.NewGuid(), PaperGateProfile.NormalV1, null);
        if (normalComparison)
            live = live with { Comparison = new(live.PaperAccountId, Guid.NewGuid(), Guid.NewGuid(), "DelphiLive",
                PaperGateProfile.NormalV1, f.Now.UtcDateTime, live.Strategy.Daily.StrategyVersionId,
                policy.PolicyVersionId, null, null) };
        approval = approval with { Review = approval.Review with { Strategy = live,
            Qualifications = approval.Review.Qualifications.Select(q => q with { StrategyFingerprint = live.Fingerprint }).ToImmutableArray() } };
        live.Validate();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workflow().ApproveAsync(approval));
        Assert.Contains("adapter", error.Message); Assert.Null(f.Store.State); Assert.Equal(0, f.Transport.Sent);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MissedClosingExitSurvivesReboundAndMissingEvidenceCannotAdvanceRecovery(bool missing)
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval(retain: true, ongoing: true));
        var first = await f.Workflow().TickAsync(f.Binding.BindingId);
        var close = f.Now.UtcDateTime.Date.AddHours(20);
        AdvanceVerifiedRiskDay(f); f.Store.Restart();
        var position = first.Account.Positions.Single();
        var recoveredClose = new TradingPriceBar(Guid.NewGuid(), close.AddMinutes(-5), close, 5, 9.99m, 10, 9.99m, 9.99m);
        var rebound = new TradingPriceBar(Guid.NewGuid(), f.Now.UtcDateTime.AddMinutes(-6), f.Now.UtcDateTime.AddMinutes(-1), 5, 11, 11, 11, 11);
        f.TransformInput = input => input with { PriorProtectionSessions = [new(close, 1)],
            HoldingSessionOrdinals = ImmutableDictionary<Guid, int>.Empty.Add(position.PositionId, 3),
            Bars = ImmutableDictionary<string, ImmutableArray<TradingPriceBar>>.Empty.Add("AAA", missing ? [rebound] : [recoveredClose, rebound]) };
        if (missing)
        {
            var blocked = await f.Workflow().TickAsync(f.Binding.BindingId);
            Assert.Equal("ProtectionRecoveryBlocked", blocked.Event); Assert.Equal(0, f.Transport.Sent);
            Assert.Equal(first.ProtectionReplayFromUtc, blocked.ProtectionReplayFromUtc);
            missing = false; f.Store.Restart();
        }
        var result = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal("SessionTwoUnprofitable", result.Memory.Symbols["AAA"].ExitReason);
        Assert.Equal("SELL", Assert.Single(result.Orders).Side); Assert.Equal(1, f.Transport.Sent);
    }

    [Fact]
    public async Task FailedStrategyEvidenceReadLeavesRecoverableAssignmentAndProtection()
    {
        var f = new Fixture(); await f.Workflow().ApproveAsync(f.Approval(retain: true, ongoing: true));
        var first = await f.Workflow().TickAsync(f.Binding.BindingId); AdvanceVerifiedRiskDay(f); f.Store.Restart();
        f.TransformInput = _ => throw new InvalidOperationException("Synthetic missing calendar or evidence");
        var blocked = await f.Workflow().TickAsync(f.Binding.BindingId);
        Assert.Equal("StrategyEvidenceUnavailable", blocked.Event); Assert.Equal(BrokerManagedPhase.NeedsAttention, blocked.Phase);
        Assert.Equal(first.ProtectionReplayFromUtc, blocked.ProtectionReplayFromUtc); Assert.Equal(0, f.Transport.Sent);
        f.TransformInput = null;
        Assert.Equal(BrokerManagedPhase.Active, (await f.Workflow().TickAsync(f.Binding.BindingId)).Phase);
    }
}
