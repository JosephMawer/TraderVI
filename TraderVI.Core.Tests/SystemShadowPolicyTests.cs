#nullable enable

using Core.Db;
using Core.Trader;
using Shouldly;
using System;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class SystemShadowPolicyTests
{
    private static readonly DateTime BarEnd =
        new(2026, 9, 4, 14, 50, 0, DateTimeKind.Utc);

    [Fact]
    public void EntryRequiresPreviousCloseAndShortMomentumButAllowsFlat()
    {
        SystemShadowPolicy.EvaluateEntry(Evidence(10m, 10.10m, 10.10m))
            .IsEligible.ShouldBeTrue();
        SystemShadowPolicy.EvaluateEntry(Evidence(10m, 10.10m, 10.09m))
            .Reason.ShouldBe(SystemShadowEntryReason.FallingFromPreviousFiveMinuteClose);
        SystemShadowPolicy.EvaluateEntry(Evidence(10m, 9.99m, 9.99m))
            .Reason.ShouldBe(SystemShadowEntryReason.BelowPreviousSessionClose);
    }

    [Theory]
    [InlineData(false, false, true, SystemShadowEntryReason.MissingEvidence)]
    [InlineData(true, true, false, SystemShadowEntryReason.LateEvidence)]
    [InlineData(true, false, true, SystemShadowEntryReason.ConflictingEvidence)]
    public void EntryFailsClosedOnUnusableEvidence(
        bool complete,
        bool late,
        bool conflicting,
        SystemShadowEntryReason expected)
    {
        SystemShadowEntryDecision result = SystemShadowPolicy.EvaluateEntry(
            Evidence(10m, 10m, 10m) with
            {
                IsComplete = complete,
                IsLate = late,
                IsConflicting = conflicting
            });

        result.IsEligible.ShouldBeFalse();
        result.Reason.ShouldBe(expected);
    }

    [Fact]
    public void EqualWeightSizingReservesTwentyFivePercentForOneAddOn()
    {
        decimal target = SystemShadowPolicy.PositionTarget(12_000m, 3);

        target.ShouldBe(4_000m);
        SystemShadowPolicy.InitialBudget(target).ShouldBe(3_000m);
        SystemShadowPolicy.AddOnBudget(target).ShouldBe(1_000m);
        SystemShadowPolicy.WholeSharesForBuy(3_000m, 10m).ShouldBe(299);
    }

    [Fact]
    public void HardStopIsFivePercentAndCannotBeBypassed()
    {
        SystemShadowPolicy.HardStopPrice(100m).ShouldBe(95m);
        SystemShadowPolicy.EvaluateFiveMinuteRisk(100m, 95m, null)
            .ShouldBe(SystemShadowExitReason.HardLoss);
    }

    [Fact]
    public void FifteenMinuteTrailArmsAboveCostAwareBreakEvenAndNeverFalls()
    {
        SystemShadowTrailingState opened = SystemShadowTrailingState.Open(100m);
        SystemShadowTrailingDecision first = SystemShadowPolicy.EvaluateFifteenMinuteClose(
            opened,
            completedBarStartUtc: BarEnd.AddMinutes(-15),
            completedBarLow: 100m,
            completedBarClose: 106m);
        first.State.ProfitProtectionArmed.ShouldBeTrue();
        first.State.TrailingStopPrice.ShouldBe(100.70m);

        SystemShadowTrailingDecision higher = SystemShadowPolicy.EvaluateFifteenMinuteClose(
            first.State,
            completedBarStartUtc: BarEnd,
            completedBarLow: 105m,
            completedBarClose: 110m);
        higher.State.TrailingStopPrice.ShouldBe(104.5m);

        SystemShadowPolicy.EvaluateFifteenMinuteClose(
                higher.State,
                completedBarStartUtc: BarEnd.AddMinutes(15),
                completedBarLow: 104m,
                completedBarClose: 104.25m)
            .ExitReason.ShouldBe(SystemShadowExitReason.TrailingProfit);
    }

    [Fact]
    public void RepeatedFifteenMinuteBarCannotTestItsLowAgainstTheTrailItJustArmed()
    {
        DateTime barStartUtc = BarEnd.AddMinutes(-15);
        SystemShadowTrailingDecision first = SystemShadowPolicy.EvaluateFifteenMinuteClose(
            SystemShadowTrailingState.Open(100m),
            barStartUtc,
            completedBarLow: 99m,
            completedBarClose: 106m);

        first.State.ProfitProtectionArmed.ShouldBeTrue();
        SystemShadowTrailingDecision repeated = SystemShadowPolicy.EvaluateFifteenMinuteClose(
            first.State,
            barStartUtc,
            completedBarLow: 99m,
            completedBarClose: 106m);

        repeated.ExitReason.ShouldBe(SystemShadowExitReason.None);
        repeated.State.ShouldBe(first.State);
        SystemShadowPolicy.EvaluateFifteenMinuteClose(
                repeated.State,
                barStartUtc.AddMinutes(15),
                completedBarLow: 99m,
                completedBarClose: 101m)
            .ExitReason.ShouldBe(SystemShadowExitReason.TrailingProfit);
    }

    [Fact]
    public void SessionTwoRotationRequiresLossStallAndQualifiedContender()
    {
        var stalled = new SystemShadowEntryDecision(
            false,
            SystemShadowEntryReason.FallingFromPreviousFiveMinuteClose);

        SystemShadowPolicy.ShouldReplaceAtSessionTwoOpening(
                latestPrice: 99m,
                averageCost: 100m,
                stalled,
                contenderQualifies: true,
                tradingSessionOrdinal: 2)
            .ShouldBeTrue();
        SystemShadowPolicy.ShouldReplaceAtSessionTwoOpening(
                latestPrice: 101m,
                averageCost: 100m,
                stalled,
                contenderQualifies: true,
                tradingSessionOrdinal: 2)
            .ShouldBeFalse();
    }

    [Fact]
    public void SessionTwoCloseExitsOnlyWhenCostsAreNotCovered()
    {
        SystemShadowPolicy.ShouldExitAtSessionTwoClose(100m, 100m, 2).ShouldBeTrue();
        SystemShadowPolicy.ShouldExitAtSessionTwoClose(101m, 100m, 2).ShouldBeFalse();
    }

    [Fact]
    public void PortfolioGuardsPauseBuyingAndRequireCapitalReviewAtAcceptedThresholds()
    {
        SystemShadowGuardDecision daily = SystemShadowPolicy.EvaluateGuards(
            currentValue: 9_700m,
            sessionOpeningValue: 10_000m,
            highestClosingValue: 10_000m);
        daily.DailyBuyingPaused.ShouldBeTrue();
        daily.CapitalReviewRequired.ShouldBeFalse();

        SystemShadowGuardDecision drawdown = SystemShadowPolicy.EvaluateGuards(
            currentValue: 9_000m,
            sessionOpeningValue: 9_200m,
            highestClosingValue: 10_000m);
        drawdown.CapitalReviewRequired.ShouldBeTrue();
    }

    [Fact]
    public void FillBoundaryIsStrictlyAfterReceipt()
    {
        SystemShadowPolicy.EarliestFiveMinuteFillBoundary(
                new DateTime(2026, 9, 4, 14, 52, 4, DateTimeKind.Utc))
            .ShouldBe(new DateTime(2026, 9, 4, 14, 55, 0, DateTimeKind.Utc));
        SystemShadowPolicy.EarliestFiveMinuteFillBoundary(
                new DateTime(2026, 9, 4, 14, 55, 0, DateTimeKind.Utc))
            .ShouldBe(new DateTime(2026, 9, 4, 15, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void RestartBoundaryCannotBackfillATradeWhileTheHostWasClosed()
    {
        SystemShadowPolicy.EarliestObservableFillBoundary(
                new DateTime(2026, 9, 4, 14, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 4, 17, 2, 9, DateTimeKind.Utc))
            .ShouldBe(new DateTime(2026, 9, 4, 17, 5, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void PendingBuyFillsOnlyItsExactImmediateWindow()
    {
        DateTime earliestFillUtc = new(2026, 9, 4, 15, 0, 0, DateTimeKind.Utc);
        DateTime uninterruptedHostStartUtc = new(2026, 9, 4, 14, 0, 0, DateTimeKind.Utc);

        SystemShadowPolicy.EvaluatePendingBuyFill(
                earliestFillUtc,
                uninterruptedHostStartUtc,
                latestCompletedFiveMinuteBarUtc: earliestFillUtc.AddMinutes(-5))
            .ShouldBe(SystemShadowPendingBuyAction.Wait);
        SystemShadowPolicy.EvaluatePendingBuyFill(
                earliestFillUtc,
                uninterruptedHostStartUtc,
                latestCompletedFiveMinuteBarUtc: earliestFillUtc)
            .ShouldBe(SystemShadowPendingBuyAction.Fill);
        SystemShadowPolicy.EvaluatePendingBuyFill(
                earliestFillUtc,
                uninterruptedHostStartUtc,
                latestCompletedFiveMinuteBarUtc: earliestFillUtc.AddMinutes(5))
            .ShouldBe(SystemShadowPendingBuyAction.Requalify);
    }

    [Fact]
    public void PendingBuyRequiresRequalificationWhenRestartMissedItsWindow()
    {
        DateTime earliestFillUtc = new(2026, 9, 4, 15, 0, 0, DateTimeKind.Utc);

        SystemShadowPolicy.EvaluatePendingBuyFill(
                earliestFillUtc,
                hostStartedUtc: earliestFillUtc.AddMinutes(1),
                latestCompletedFiveMinuteBarUtc: earliestFillUtc.AddMinutes(5))
            .ShouldBe(SystemShadowPendingBuyAction.Requalify);
    }

    [Fact]
    public async Task RepositoryRefusesALateBuyFillEvenIfAControllerCallerRegresses()
    {
        DateTime earliestFillUtc = new(2026, 9, 4, 15, 0, 0, DateTimeKind.Utc);
        var order = new SystemShadowPendingOrder(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            "CRDL",
            "Buy",
            "Initial",
            earliestFillUtc.AddMinutes(-5),
            earliestFillUtc,
            100m,
            "Qualified");

        await Should.ThrowAsync<ArgumentException>(() =>
            new SystemShadowRepository().FillPendingOrderAsync(
                order,
                rawFillPrice: 10m,
                fillUtc: earliestFillUtc.AddMinutes(5),
                tradingDate: new DateTime(2026, 9, 4),
                sameDayReentryCount: 0));
    }

    [Fact]
    public void SameDayReentryIsLimitedAndRequiresPriceBasedExit()
    {
        SystemShadowPolicy.CanEnterAgainToday(0, false).ShouldBeTrue();
        SystemShadowPolicy.CanEnterAgainToday(1, true).ShouldBeTrue();
        SystemShadowPolicy.CanEnterAgainToday(1, false).ShouldBeFalse();
        SystemShadowPolicy.CanEnterAgainToday(2, true).ShouldBeFalse();
    }

    private static SystemShadowEntryEvidence Evidence(
        decimal previousSession,
        decimal previousFive,
        decimal latest) =>
        new(
            previousSession,
            previousFive,
            latest,
            BarEnd,
            BarEnd.AddMinutes(2));
}
