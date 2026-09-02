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
        DelayedIntradayOutcomeAssessment result = DelayedIntradayOutcomeCalculator.Assess(
            100m,
            200m,
            EntryUtc,
            PolicyPathWithExit(),
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
            PolicyPathWithExit(),
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
            PolicyPathWithExit(),
            [FiveMinuteBar(14, 35, 95m)],
            Array.Empty<OhlcvBar>());

        result.State.ShouldBe(DelayedIntradayOutcomeState.Pending);
        result.ReasonCode.ShouldBe("AwaitingAlignedPostDetectionXiuBar");
    }

    [Fact]
    public void HoldingPathRemainsPendingUntilAnExitAlertExists()
    {
        DelayedIntradayOutcomeAssessment result = DelayedIntradayOutcomeCalculator.Assess(
            100m,
            200m,
            EntryUtc,
            [PolicyBar(13, 30, 13, 45, 100m, 101m, 99m, 100m)],
            [FiveMinuteBar(14, 35, 100m)],
            [FiveMinuteBar(14, 35, 200m)]);

        result.State.ShouldBe(DelayedIntradayOutcomeState.Pending);
        result.ReasonCode.ShouldBe("NoExitAlertYet");
    }

    [Fact]
    public void ProvenMissingPolicySlotIsInvalid()
    {
        DelayedIntradayOutcomeAssessment result = DelayedIntradayOutcomeCalculator.Assess(
            100m,
            200m,
            EntryUtc,
            [
                PolicyBar(13, 30, 13, 45, 100m, 101m, 99m, 100m),
                PolicyBar(14, 0, 14, 15, 100m, 101m, 99m, 100m)
            ],
            Array.Empty<OhlcvBar>(),
            Array.Empty<OhlcvBar>());

        result.State.ShouldBe(DelayedIntradayOutcomeState.Invalid);
        result.ReasonCode.ShouldBe("MissingExpectedPolicyBar");
        result.FirstInvalidEventUtc.ShouldBe(Utc(2026, 8, 28, 13, 45));
    }

    [Fact]
    public void ProvenSkippedPolicySessionIsInvalid()
    {
        List<DelayedIntradayBar> bars = FullSessionPolicyBars();
        bars.Add(PolicyBar(
            2026, 9, 1, 13, 30, 13, 45,
            100m, 101m, 99m, 100m,
            sessionOrdinal: 3));

        DelayedIntradayOutcomeAssessment result = DelayedIntradayOutcomeCalculator.Assess(
            100m,
            200m,
            EntryUtc,
            bars,
            Array.Empty<OhlcvBar>(),
            Array.Empty<OhlcvBar>());

        result.State.ShouldBe(DelayedIntradayOutcomeState.Invalid);
        result.ReasonCode.ShouldBe("NonConsecutivePolicySessionOrdinal");
        result.FirstInvalidEventUtc.ShouldBe(Utc(2026, 9, 1, 13, 30));
    }

    [Fact]
    public void ReceiptOrderThatRewritesHistoryIsInvalid()
    {
        DelayedIntradayOutcomeAssessment result = DelayedIntradayOutcomeCalculator.Assess(
            100m,
            200m,
            EntryUtc,
            [
                PolicyBar(13, 30, 14, 15, 100m, 101m, 99m, 100m),
                PolicyBar(13, 45, 14, 0, 100m, 101m, 99m, 100m)
            ],
            Array.Empty<OhlcvBar>(),
            Array.Empty<OhlcvBar>());

        result.State.ShouldBe(DelayedIntradayOutcomeState.Invalid);
        result.ReasonCode.ShouldBe("PolicyReceiptOrderConflict");
        result.FirstInvalidEventUtc.ShouldBe(Utc(2026, 8, 28, 13, 45));
    }

    [Fact]
    public void MissingExactSymbolFillIsInvalidOnceALaterBarExists()
    {
        DelayedIntradayOutcomeAssessment result = DelayedIntradayOutcomeCalculator.Assess(
            100m,
            200m,
            EntryUtc,
            PolicyPathWithExit(),
            [FiveMinuteBar(14, 40, 94m)],
            [FiveMinuteBar(14, 35, 202m)]);

        result.State.ShouldBe(DelayedIntradayOutcomeState.Invalid);
        result.ReasonCode.ShouldBe("MissingExpectedSymbolFillBar");
        result.FirstInvalidEventUtc.ShouldBe(Utc(2026, 8, 28, 14, 35));
    }

    [Fact]
    public void MissingExactXiuFillIsInvalidOnceALaterBarExists()
    {
        DelayedIntradayOutcomeAssessment result = DelayedIntradayOutcomeCalculator.Assess(
            100m,
            200m,
            EntryUtc,
            PolicyPathWithExit(),
            [FiveMinuteBar(14, 35, 95m)],
            [FiveMinuteBar(14, 40, 202m)]);

        result.State.ShouldBe(DelayedIntradayOutcomeState.Invalid);
        result.ReasonCode.ShouldBe("MissingAlignedXiuFillBar");
        result.FirstInvalidEventUtc.ShouldBe(Utc(2026, 8, 28, 14, 35));
    }

    [Fact]
    public void AfterCloseDetectionUsesTheNextRegularSessionOpen()
    {
        List<DelayedIntradayBar> bars = FullSessionPolicyBars();
        bars[^1] = PolicyBar(
            2026, 8, 28, 19, 45, 20, 2,
            95m, 96m, 89m, 91m,
            sessionOrdinal: 1);

        DelayedIntradayOutcomeAssessment result = DelayedIntradayOutcomeCalculator.Assess(
            100m,
            200m,
            EntryUtc,
            bars,
            [
                FiveMinuteBar(2026, 8, 28, 20, 5, 50m),
                FiveMinuteBar(2026, 8, 31, 13, 30, 95m)
            ],
            [
                FiveMinuteBar(2026, 8, 28, 20, 5, 250m),
                FiveMinuteBar(2026, 8, 31, 13, 30, 202m)
            ]);

        result.State.ShouldBe(DelayedIntradayOutcomeState.Matured);
        result.Outcome!.FillBarStartUtc.ShouldBe(Utc(2026, 8, 31, 13, 30));
    }

    [Fact]
    public void UnprovenTailGapRemainsPending()
    {
        DelayedIntradayOutcomeAssessment result = DelayedIntradayOutcomeCalculator.Assess(
            100m,
            200m,
            EntryUtc,
            [PolicyBar(13, 30, 13, 45, 100m, 101m, 99m, 100m)],
            Array.Empty<OhlcvBar>(),
            Array.Empty<OhlcvBar>());

        result.State.ShouldBe(DelayedIntradayOutcomeState.Pending);
        result.ReasonCode.ShouldBe("NoExitAlertYet");
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

    private static DelayedIntradayBar PolicyBar(
        int year,
        int month,
        int day,
        int startHour,
        int startMinute,
        int receivedHour,
        int receivedMinute,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        int sessionOrdinal)
    {
        DateTime start = Utc(year, month, day, startHour, startMinute);
        return new DelayedIntradayBar(
            start,
            start.AddMinutes(15),
            Utc(year, month, day, receivedHour, receivedMinute),
            sessionOrdinal,
            startHour == 19 && startMinute == 45,
            open,
            high,
            low,
            close,
            10_000);
    }

    private static IReadOnlyList<DelayedIntradayBar> PolicyPathWithExit() =>
    [
        PolicyBar(13, 30, 13, 45, 100m, 101m, 99m, 100m),
        PolicyBar(13, 45, 14, 0, 100m, 101m, 99m, 100m),
        PolicyBar(14, 0, 14, 15, 100m, 101m, 99m, 100m),
        PolicyBar(14, 15, 14, 32, 95m, 96m, 89m, 91m)
    ];

    private static List<DelayedIntradayBar> FullSessionPolicyBars()
    {
        var bars = new List<DelayedIntradayBar>();
        DateTime start = EntryUtc;
        while (start <= Utc(2026, 8, 28, 19, 45))
        {
            DateTime received = start.AddMinutes(15);
            bars.Add(PolicyBar(
                start.Year,
                start.Month,
                start.Day,
                start.Hour,
                start.Minute,
                received.Hour,
                received.Minute,
                100m,
                101m,
                99m,
                100m,
                sessionOrdinal: 1));
            start = start.AddMinutes(15);
        }

        return bars;
    }

    private static OhlcvBar FiveMinuteBar(int hour, int minute, decimal open) =>
        new(Utc(2026, 8, 28, hour, minute), open, open + 1m, open - 1m, open, 1_000);

    private static OhlcvBar FiveMinuteBar(
        int year,
        int month,
        int day,
        int hour,
        int minute,
        decimal open) =>
        new(Utc(year, month, day, hour, minute), open, open + 1m, open - 1m, open, 1_000);

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
