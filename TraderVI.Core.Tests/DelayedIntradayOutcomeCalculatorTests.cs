#nullable enable

using Core.Calibration;
using Core.TMX.Models.Domain;
using Core.Trader;
using Shouldly;
using System;
using System.Collections.Generic;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelayedIntradayOutcomeCalculatorTests
{
    private static readonly DateTime EntryUtc = Utc(2026, 8, 28, 13, 30);

    [Fact]
    public void ExitUsesFirstFiveMinuteOpenAfterDetection_NotTriggerPrice()
    {
        DelayedIntradayBar policy = PolicyBar(
            startHour: 14,
            startMinute: 15,
            receivedHour: 14,
            receivedMinute: 32,
            open: 95m,
            high: 96m,
            low: 89m,
            close: 91m);

        DelayedIntradayOutcomeAssessment result = DelayedIntradayOutcomeCalculator.Assess(
            100m,
            200m,
            EntryUtc,
            [policy],
            [FiveMinuteBar(14, 30, 88m), FiveMinuteBar(14, 35, 87m)],
            [FiveMinuteBar(14, 30, 202m), FiveMinuteBar(14, 35, 201m)]);

        result.State.ShouldBe(DelayedIntradayOutcomeState.Matured);
        DelayedIntradayOutcomeV1 outcome = result.Outcome!;
        outcome.ExitReason.ShouldBe(IntradaySwingReason.ConditionalLossLimit);
        outcome.TriggerPrice.ShouldBe(90m);
        outcome.DetectedUtc.ShouldBe(Utc(2026, 8, 28, 14, 32));
        outcome.FillBarStartUtc.ShouldBe(Utc(2026, 8, 28, 14, 35));
        outcome.RawExitPrice.ShouldBe(87m);
        outcome.FillLagMinutes.ShouldBe(3d);
    }

    [Fact]
    public void ReportsZeroCommissionGrossReturnAndSeparateConservativeSensitivity()
    {
        DelayedIntradayOutcomeAssessment result = DelayedIntradayOutcomeCalculator.Assess(
            100m,
            200m,
            EntryUtc,
            [PolicyBar(14, 15, 14, 32, 95m, 96m, 89m, 91m)],
            [FiveMinuteBar(14, 35, 95m)],
            [FiveMinuteBar(14, 35, 202m)]);

        DelayedIntradayOutcomeV1 outcome = result.Outcome!;
        outcome.GrossReturn.ShouldBe(-0.05d, 0.0000001d);
        outcome.XiuReturn.ShouldBe(0.01d, 0.0000001d);
        outcome.GrossExcessReturn.ShouldBe(-0.06d, 0.0000001d);
        double expectedConservative = (double)((95m * 0.9975m) / (100m * 1.0025m) - 1m);
        outcome.ConservativeNetReturn.ShouldBe(expectedConservative, 0.0000001d);
        outcome.ExecutionFrictionRatePerSide.ShouldBe(0.0025m);
    }

    [Fact]
    public void MissingAlignedXiuFillRemainsPending()
    {
        DelayedIntradayOutcomeAssessment result = DelayedIntradayOutcomeCalculator.Assess(
            100m,
            200m,
            EntryUtc,
            [PolicyBar(14, 15, 14, 32, 95m, 96m, 89m, 91m)],
            [FiveMinuteBar(14, 35, 95m)],
            Array.Empty<OhlcvBar>());

        result.State.ShouldBe(DelayedIntradayOutcomeState.Pending);
        result.PendingReason.ShouldBe("AwaitingAlignedPostDetectionXiuBar");
    }

    [Fact]
    public void HoldingPathRemainsPendingUntilAnExitAlertExists()
    {
        DelayedIntradayOutcomeAssessment result = DelayedIntradayOutcomeCalculator.Assess(
            100m,
            200m,
            EntryUtc,
            [PolicyBar(14, 15, 14, 32, 100m, 101m, 99m, 100m)],
            [FiveMinuteBar(14, 35, 100m)],
            [FiveMinuteBar(14, 35, 200m)]);

        result.State.ShouldBe(DelayedIntradayOutcomeState.Pending);
        result.PendingReason.ShouldBe("NoExitAlertYet");
    }

    private static DelayedIntradayBar PolicyBar(
        int startHour,
        int startMinute,
        int receivedHour,
        int receivedMinute,
        decimal open,
        decimal high,
        decimal low,
        decimal close)
    {
        DateTime start = Utc(2026, 8, 28, startHour, startMinute);
        return new DelayedIntradayBar(
            start,
            start.AddMinutes(15),
            Utc(2026, 8, 28, receivedHour, receivedMinute),
            1,
            false,
            open,
            high,
            low,
            close,
            10_000);
    }

    private static OhlcvBar FiveMinuteBar(int hour, int minute, decimal open) =>
        new(Utc(2026, 8, 28, hour, minute), open, open + 1m, open - 1m, open, 1_000);

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
