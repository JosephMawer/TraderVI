#nullable enable

using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveLifecycleAndSafetyTests
{
    private static readonly DelphiLivePolicyDefinition Policy =
        DelphiLivePolicyDefinition.Version1;

    [Fact]
    public void EntryConfirmation_AllowsStrengthTierToChangeAcrossConsecutiveBars()
    {
        DelphiLiveLifecycleSnapshot state = DelphiLiveLifecycleSnapshot.NewSession(true);
        DateTime firstEnd = Utc(2026, 9, 8, 13, 50);

        DelphiLiveLifecycleDecision first = DelphiLiveLifecyclePolicy.Advance(
            state,
            Input(firstEnd, Strong(4, 0)));
        DelphiLiveLifecycleDecision second = DelphiLiveLifecyclePolicy.Advance(
            first.Snapshot,
            Input(firstEnd.AddMinutes(5), Strong(3, 0)));

        first.Snapshot.State.ShouldBe(DelphiLiveRecommendationState.Emerging);
        first.Snapshot.ReasonCode.ShouldBe(DelphiLiveLifecycleReasons.StrongConfirmationStarted);
        second.Snapshot.State.ShouldBe(DelphiLiveRecommendationState.EntryEligible);
        second.Snapshot.ReasonCode.ShouldBe(DelphiLiveLifecycleReasons.StrongConfirmationCompleted);
        second.MayCreateBuyDecision.ShouldBeTrue();
    }

    [Fact]
    public void MissingObservation_BreaksStrongConfirmationAndCannotBridgeTheGap()
    {
        DateTime firstEnd = Utc(2026, 9, 8, 13, 50);
        DelphiLiveLifecycleDecision first = DelphiLiveLifecyclePolicy.Advance(
            DelphiLiveLifecycleSnapshot.NewSession(true),
            Input(firstEnd, Strong(3, 0)));
        DelphiLiveLifecycleDecision missed = DelphiLiveLifecyclePolicy.Advance(
            first.Snapshot,
            Input(
                firstEnd.AddMinutes(5),
                Neutral(),
                valid: false,
                confidence: new(DelphiLiveDataConfidenceState.Ambiguous, 1)));
        DelphiLiveLifecycleDecision fresh = DelphiLiveLifecyclePolicy.Advance(
            missed.Snapshot,
            Input(firstEnd.AddMinutes(10), Strong(4, 0)));

        missed.Snapshot.State.ShouldBe(DelphiLiveRecommendationState.Watching);
        missed.Snapshot.ConsecutiveStrongObservations.ShouldBe(0);
        fresh.Snapshot.State.ShouldBe(DelphiLiveRecommendationState.Emerging);
    }

    [Fact]
    public void DismissedCandidate_RequiresTwoEntirelyFreshStrongObservationsToRecover()
    {
        DateTime end = Utc(2026, 9, 8, 14, 0);
        DelphiLiveLifecycleDecision weak1 = DelphiLiveLifecyclePolicy.Advance(
            DelphiLiveLifecycleSnapshot.NewSession(true),
            Input(end, Weak(3, 0)));
        DelphiLiveLifecycleDecision weak2 = DelphiLiveLifecyclePolicy.Advance(
            weak1.Snapshot,
            Input(end.AddMinutes(5), Weak(4, 0)));
        DelphiLiveLifecycleDecision recovery1 = DelphiLiveLifecyclePolicy.Advance(
            weak2.Snapshot,
            Input(end.AddMinutes(10), Strong(3, 0)));
        DelphiLiveLifecycleDecision recovery2 = DelphiLiveLifecyclePolicy.Advance(
            recovery1.Snapshot,
            Input(end.AddMinutes(15), Strong(4, 0)));

        weak2.Snapshot.State.ShouldBe(DelphiLiveRecommendationState.Dismissed);
        recovery1.Snapshot.State.ShouldBe(DelphiLiveRecommendationState.Dismissed);
        recovery1.Snapshot.ReasonCode.ShouldBe(DelphiLiveLifecycleReasons.DismissedRecoveryStarted);
        recovery2.Snapshot.State.ShouldBe(DelphiLiveRecommendationState.EntryEligible);
        recovery2.Snapshot.ReasonCode.ShouldBe(DelphiLiveLifecycleReasons.DismissedRecoveryCompleted);
    }

    [Fact]
    public void ProcessGap_ExpiresOrdinaryConfirmationButPreservesProtectiveExitState()
    {
        var pending = new DelphiLiveLifecycleSnapshot(
            DelphiLiveRecommendationState.ExitPending,
            1,
            1,
            Utc(2026, 9, 8, 14, 0),
            DelphiLiveLifecycleReasons.ExitPending);

        DelphiLiveLifecycleSnapshot resumed =
            DelphiLiveLifecyclePolicy.AfterProcessContinuityGap(pending, true);

        resumed.State.ShouldBe(DelphiLiveRecommendationState.ExitPending);
        resumed.ConsecutiveStrongObservations.ShouldBe(0);
        resumed.ConsecutiveStrongWeakeningObservations.ShouldBe(0);
        resumed.LastScheduledBarEndUtc.ShouldBeNull();
    }

    [Fact]
    public void ProfitProtection_ActivatesAtThreePercentAndRatchetsAtFivePercent()
    {
        Guid positionId = Guid.NewGuid();
        DateTime firstEnd = Utc(2026, 9, 8, 14, 0);
        DelphiLiveProfitProtectionState state =
            DelphiLiveProfitProtectionState.Open(positionId, 100m);

        DelphiLiveProtectionUpdate breakEven = DelphiLiveSafetyPolicy.ApplyCompletedClose(
            state, firstEnd, 103m, firstEnd.AddSeconds(1), Policy);
        DelphiLiveProtectionUpdate trailing = DelphiLiveSafetyPolicy.ApplyCompletedClose(
            breakEven.State, firstEnd.AddMinutes(5), 105m, firstEnd.AddMinutes(5).AddSeconds(1), Policy);
        DelphiLiveProtectionUpdate lowerClose = DelphiLiveSafetyPolicy.ApplyCompletedClose(
            trailing.State, firstEnd.AddMinutes(10), 104m, firstEnd.AddMinutes(10).AddSeconds(1), Policy);

        breakEven.State.Stage.ShouldBe(DelphiLiveProfitProtectionStage.BreakEven);
        breakEven.State.FloorPrice.ShouldBe(100m);
        trailing.State.Stage.ShouldBe(DelphiLiveProfitProtectionStage.Trailing);
        trailing.State.FloorPrice.ShouldBe(102.900000m);
        lowerClose.State.FloorPrice.ShouldBe(trailing.State.FloorPrice);
    }

    [Fact]
    public void ProfitFloor_CannotUseAQuoteReceivedBeforeTheFloorWasPersisted()
    {
        DateTime end = Utc(2026, 9, 8, 14, 0);
        DelphiLiveProfitProtectionState protection = DelphiLiveSafetyPolicy.ApplyCompletedClose(
            DelphiLiveProfitProtectionState.Open(Guid.NewGuid(), 100m),
            end,
            103m,
            end.AddSeconds(2),
            Policy).State;
        var input = SafetyInput(
            held: true,
            momentum: Neutral(),
            average: 100m,
            bid: 99m,
            bidReceived: end.AddSeconds(1),
            protection: protection);

        DelphiLiveSafetyPolicy.Evaluate(input, Policy).FiredExitRules
            .ShouldNotContain(DelphiLiveExitRule.ProfitProtectionFloorBreach);
    }

    [Fact]
    public void SimultaneousExitRules_UseFrozenPrecedenceButPreserveEveryTrigger()
    {
        DateTime end = Utc(2026, 9, 8, 14, 0);
        DelphiLiveProfitProtectionState protection = DelphiLiveSafetyPolicy.ApplyCompletedClose(
            DelphiLiveProfitProtectionState.Open(Guid.NewGuid(), 100m),
            end.AddMinutes(-5),
            106m,
            end.AddMinutes(-5).AddSeconds(1),
            Policy).State;
        DelphiLiveSafetyInput input = SafetyInput(
            held: true,
            momentum: Weak(4, 0),
            average: 100m,
            bid: 90m,
            bidReceived: end.AddSeconds(1),
            protection: protection) with
        {
            CompletedBarOpen = 100m,
            CompletedBarClose = 90m,
            SessionVwapReferenceAvailable = true,
            CloseBelowBufferedSessionVwap = true,
            PriorRangeReferenceAvailable = true,
            CloseBelowBufferedPriorTwentyMinuteLow = true,
            VolumeSupport = Family(DelphiLiveSignalFamily.VolumeSupport, DelphiLiveFamilyState.Weakening),
            PreviousValidMomentumWasStrongWeakening = true
        };

        DelphiLiveSafetyEvaluation decision = DelphiLiveSafetyPolicy.Evaluate(input, Policy);

        decision.PrimaryExitRule.ShouldBe(DelphiLiveExitRule.HardLoss5Pct);
        decision.FiredExitRules.ShouldBe([
            DelphiLiveExitRule.HardLoss5Pct,
            DelphiLiveExitRule.FastDownside10Pct,
            DelphiLiveExitRule.ProfitProtectionFloorBreach,
            DelphiLiveExitRule.ConfirmedSupportFailure,
            DelphiLiveExitRule.LiveWeakeningExit
        ]);
        decision.LiveWeakeningDetail.ShouldBe(DelphiLiveSafetyReasons.BroadImmediateWeakening);
    }

    [Fact]
    public void SupportFailure_BlocksEntryOnlyWhenAllThreePartsAgreeAndAreAvailable()
    {
        DelphiLiveSafetyInput complete = SafetyInput(false, Strong(3, 0)) with
        {
            SessionVwapReferenceAvailable = true,
            CloseBelowBufferedSessionVwap = true,
            PriorRangeReferenceAvailable = true,
            CloseBelowBufferedPriorTwentyMinuteLow = true,
            VolumeSupport = Family(DelphiLiveSignalFamily.VolumeSupport, DelphiLiveFamilyState.Weakening)
        };

        DelphiLiveSafetyPolicy.Evaluate(complete, Policy).EntrySafetyVetoActive.ShouldBeTrue();
        DelphiLiveSafetyPolicy.Evaluate(
            complete with { PriorRangeReferenceAvailable = false }, Policy)
            .EntrySafetyVetoActive.ShouldBeFalse();
    }

    [Fact]
    public void FirstVeryWeakObservation_ImmediatelyCreatesLiveWeakeningExit()
    {
        DelphiLiveSafetyEvaluation result = DelphiLiveSafetyPolicy.Evaluate(
            SafetyInput(true, Weak(4, 0), 100m),
            Policy);

        result.PrimaryExitRule.ShouldBe(DelphiLiveExitRule.LiveWeakeningExit);
        result.LiveWeakeningDetail.ShouldBe(DelphiLiveSafetyReasons.BroadImmediateWeakening);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Warmup_DoesNotReuseOrdinaryWeakeningOrSupportFailureEvidence(int weakening)
    {
        DelphiLiveSafetyInput input = SafetyInput(true, Weak(weakening, 0), 100m) with
        {
            IsWarmingUp = true,
            PreviousValidMomentumWasStrongWeakening = true,
            SessionVwapReferenceAvailable = true,
            CloseBelowBufferedSessionVwap = true,
            PriorRangeReferenceAvailable = true,
            CloseBelowBufferedPriorTwentyMinuteLow = true,
            VolumeSupport = Family(DelphiLiveSignalFamily.VolumeSupport, DelphiLiveFamilyState.Weakening)
        };

        DelphiLiveSafetyEvaluation result = DelphiLiveSafetyPolicy.Evaluate(input, Policy);

        result.RequiresProtectiveSell.ShouldBeFalse();
        result.EntrySafetyVetoActive.ShouldBeFalse();
        result.LiveWeakeningDetail.ShouldBeNull();
    }

    [Fact]
    public void Warmup_StillHonoursFreshHardLossAndFastDownsideProtection()
    {
        DelphiLiveSafetyInput input = SafetyInput(
            true, Weak(4, 0), 100m, 94m, Utc(2026, 9, 8, 13, 37)) with
        {
            IsWarmingUp = true,
            CompletedBarOpen = 110m,
            CompletedBarClose = 99m
        };

        DelphiLiveSafetyEvaluation result = DelphiLiveSafetyPolicy.Evaluate(input, Policy);

        result.FiredExitRules.ShouldBe([
            DelphiLiveExitRule.HardLoss5Pct,
            DelphiLiveExitRule.FastDownside10Pct
        ]);
        result.WarmupPhaseReason.ShouldBe(DelphiLiveSafetyReasons.WarmupHardLoss5Pct);
    }

    private static DelphiLiveLifecycleInput Input(
        DateTime end,
        DelphiLiveMomentumJudgment momentum,
        bool valid = true,
        DelphiLiveDataConfidence? confidence = null) =>
        new(
            end,
            true,
            valid,
            confidence ?? DelphiLiveDataConfidence.Normal,
            momentum,
            false,
            false,
            false,
            false,
            true);

    private static DelphiLiveSafetyInput SafetyInput(
        bool held,
        DelphiLiveMomentumJudgment momentum,
        decimal? average = null,
        decimal? bid = null,
        DateTime? bidReceived = null,
        DelphiLiveProfitProtectionState? protection = null) =>
        new(
            held,
            false,
            average,
            bid,
            bidReceived,
            null,
            null,
            false,
            false,
            false,
            false,
            Family(DelphiLiveSignalFamily.VolumeSupport, DelphiLiveFamilyState.Neutral),
            momentum,
            false,
            protection);

    private static DelphiLiveMomentumJudgment Strong(int supportive, int weakening) =>
        Combined(supportive, weakening);

    private static DelphiLiveMomentumJudgment Weak(int weakening, int supportive) =>
        Combined(supportive, weakening);

    private static DelphiLiveMomentumJudgment Neutral() => Combined(0, 0);

    private static DelphiLiveMomentumJudgment Combined(int supportive, int weakening)
    {
        DelphiLiveSignalFamily[] names = Enum.GetValues<DelphiLiveSignalFamily>();
        var families = new List<DelphiLiveFamilyJudgment>(4);
        int index = 0;
        for (; index < supportive; index++)
            families.Add(Family(names[index], DelphiLiveFamilyState.Supportive));
        for (int count = 0; count < weakening; count++, index++)
            families.Add(Family(names[index], DelphiLiveFamilyState.Weakening));
        while (index < names.Length)
        {
            families.Add(Family(names[index], DelphiLiveFamilyState.Neutral));
            index++;
        }
        return DelphiLiveFamilyCombiner.Combine(families);
    }

    private static DelphiLiveFamilyJudgment Family(
        DelphiLiveSignalFamily family,
        DelphiLiveFamilyState state) =>
        new(family, state, state.ToString());

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
