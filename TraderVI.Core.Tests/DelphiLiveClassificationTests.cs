#nullable enable

using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Generic;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveClassificationTests
{
    private static readonly DelphiLivePolicyDefinition Policy =
        DelphiLivePolicyDefinition.Version1;
    private static readonly DateTime BarEndUtc =
        new(2026, 9, 4, 14, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(-4, DelphiLiveFamilyState.Weakening)]
    [InlineData(-3, DelphiLiveFamilyState.Weakening)]
    [InlineData(-2, DelphiLiveFamilyState.NegativeLeaning)]
    [InlineData(-1, DelphiLiveFamilyState.Neutral)]
    [InlineData(0, DelphiLiveFamilyState.Neutral)]
    [InlineData(1, DelphiLiveFamilyState.Neutral)]
    [InlineData(2, DelphiLiveFamilyState.PositiveLeaning)]
    [InlineData(3, DelphiLiveFamilyState.Supportive)]
    [InlineData(4, DelphiLiveFamilyState.Supportive)]
    public void PersistenceClassificationPreservesEveryScoreBoundary(
        int score,
        DelphiLiveFamilyState expected)
    {
        var measurements = new DelphiLivePersistenceMeasurements(
            DelphiLiveMeasurementAvailability.Available,
            System.Collections.Immutable.ImmutableArray<DelphiLiveIntervalComparison>.Empty,
            score,
            DelphiLiveReasonCodes.Available);

        DelphiLivePersistenceJudgment result =
            DelphiLiveFamilyClassifiers.ClassifyPersistence(measurements);

        result.Family.State.ShouldBe(expected);
        result.Score.ShouldBe(score);
    }

    public static IEnumerable<object[]> PriceMovementGrid()
    {
        yield return new object[] { -0.25m, -0.05m, DelphiLiveFamilyState.Weakening, DelphiLiveReasonCodes.Available };
        yield return new object[] { -0.25m, 0m, DelphiLiveFamilyState.Neutral, DelphiLiveReasonCodes.RawMoveWithoutRelativeAgreement };
        yield return new object[] { -0.25m, 0.05m, DelphiLiveFamilyState.Neutral, DelphiLiveReasonCodes.RawRelativeConflict };
        yield return new object[] { 0m, -0.05m, DelphiLiveFamilyState.Neutral, DelphiLiveReasonCodes.RelativeMoveWithoutRawAgreement };
        yield return new object[] { 0m, 0m, DelphiLiveFamilyState.Neutral, DelphiLiveReasonCodes.MixedOrFlat };
        yield return new object[] { 0m, 0.05m, DelphiLiveFamilyState.Neutral, DelphiLiveReasonCodes.RelativeMoveWithoutRawAgreement };
        yield return new object[] { 0.25m, -0.05m, DelphiLiveFamilyState.Neutral, DelphiLiveReasonCodes.RawRelativeConflict };
        yield return new object[] { 0.25m, 0m, DelphiLiveFamilyState.Neutral, DelphiLiveReasonCodes.RawMoveWithoutRelativeAgreement };
        yield return new object[] { 0.25m, 0.05m, DelphiLiveFamilyState.Supportive, DelphiLiveReasonCodes.Available };
    }

    [Theory]
    [MemberData(nameof(PriceMovementGrid))]
    public void PriceMovementRequiresJointRawAndRelativeAgreement(
        decimal rawUnits,
        decimal excessUnits,
        DelphiLiveFamilyState expectedState,
        string expectedReason)
    {
        DelphiLivePriceMovementWindowJudgment result =
            DelphiLiveFamilyClassifiers.ClassifyPriceMovementWindow(
                WindowFromUnits(TimeSpan.FromMinutes(20), rawUnits, excessUnits),
                DelphiLiveScalarMeasurement.Available(0.04m),
                Policy);

        result.State.ShouldBe(expectedState);
        result.ReasonCode.ShouldBe(expectedReason);
        result.RawMoveUnits.ShouldBe(rawUnits);
        result.ExcessUnits.ShouldBe(excessUnits);
    }

    [Theory]
    [InlineData(0.010, 0.005, DelphiLivePriceMovementDirection.AlignedUp)]
    [InlineData(-0.010, -0.005, DelphiLivePriceMovementDirection.AlignedDown)]
    [InlineData(0.010, 0.020, DelphiLivePriceMovementDirection.RisingButLagging)]
    [InlineData(-0.010, -0.020, DelphiLivePriceMovementDirection.FallingButOutperforming)]
    [InlineData(0.010, 0.010, DelphiLivePriceMovementDirection.MixedOrFlat)]
    [InlineData(0.000, -0.010, DelphiLivePriceMovementDirection.MixedOrFlat)]
    public void PriceMovementRetainsPreThresholdDirectionMetadata(
        double stockReturn,
        double benchmarkReturn,
        DelphiLivePriceMovementDirection expected)
    {
        DelphiLiveWindowReturnMeasurement measurement = WindowFromReturns(
            TimeSpan.FromMinutes(20),
            (decimal)stockReturn,
            (decimal)benchmarkReturn);

        DelphiLiveFamilyClassifiers.ClassifyPriceMovementWindow(
                measurement,
                DelphiLiveScalarMeasurement.Available(0.10m),
                Policy)
            .Direction.ShouldBe(expected);
    }

    public static IEnumerable<object[]> WindowHierarchy()
    {
        DelphiLiveFamilyState[] states =
        {
            DelphiLiveFamilyState.Supportive,
            DelphiLiveFamilyState.Neutral,
            DelphiLiveFamilyState.Weakening
        };
        foreach (DelphiLiveFamilyState twenty in states)
        {
            foreach (DelphiLiveFamilyState hour in states)
            {
                DelphiLiveFamilyState expected =
                    twenty == DelphiLiveFamilyState.Supportive && hour == DelphiLiveFamilyState.Weakening ||
                    twenty == DelphiLiveFamilyState.Weakening && hour == DelphiLiveFamilyState.Supportive
                        ? DelphiLiveFamilyState.NeutralConflict
                        : twenty is DelphiLiveFamilyState.Supportive or DelphiLiveFamilyState.Weakening
                            ? twenty
                            : hour;
                yield return new object[] { twenty, hour, expected };
            }
        }
    }

    [Theory]
    [MemberData(nameof(WindowHierarchy))]
    public void TwentyMinuteAndOneHourWindowsUseTheFrozenHierarchy(
        DelphiLiveFamilyState twentyMinute,
        DelphiLiveFamilyState oneHour,
        DelphiLiveFamilyState expected)
    {
        DelphiLivePriceMovementMeasurements measurements = PriceMovement(
            WindowForState(TimeSpan.FromMinutes(20), twentyMinute),
            WindowForState(TimeSpan.FromHours(1), oneHour));

        DelphiLivePriceMovementJudgment result =
            DelphiLiveFamilyClassifiers.ClassifyPriceMovement(
                measurements,
                Ruler(10, 0.04m),
                Policy);

        result.Family.State.ShouldBe(expected);
    }

    [Fact]
    public void TwentyMinuteWindowVotesAloneUntilOneHourMatures()
    {
        DelphiLivePriceMovementMeasurements measurements = PriceMovement(
            WindowForState(TimeSpan.FromMinutes(20), DelphiLiveFamilyState.Supportive),
            NotMatureWindow(TimeSpan.FromHours(1)));

        DelphiLivePriceMovementJudgment result =
            DelphiLiveFamilyClassifiers.ClassifyPriceMovement(
                measurements,
                Ruler(10, 0.04m),
                Policy);

        result.Family.State.ShouldBe(DelphiLiveFamilyState.Supportive);
        result.Family.ReasonCode.ShouldBe(DelphiLiveReasonCodes.TwentyMinuteOnly);
    }

    [Fact]
    public void AnUnavailableRequiredOneHourWindowCannotBeTreatedAsNeutral()
    {
        DelphiLivePriceMovementMeasurements measurements = PriceMovement(
            WindowForState(TimeSpan.FromMinutes(20), DelphiLiveFamilyState.Supportive),
            UnavailableWindow(TimeSpan.FromHours(1)));

        DelphiLiveFamilyClassifiers.ClassifyPriceMovement(
                measurements,
                Ruler(10, 0.04m),
                Policy)
            .Family.State.ShouldBe(DelphiLiveFamilyState.Unavailable);
    }

    [Fact]
    public void OnlyTheOperationalTenSessionRulerMayControlV1()
    {
        DelphiLivePriceMovementMeasurements measurements = PriceMovement(
            WindowForState(TimeSpan.FromMinutes(20), DelphiLiveFamilyState.Supportive),
            NotMatureWindow(TimeSpan.FromHours(1)));

        DelphiLiveFamilyClassifiers.ClassifyPriceMovement(
                measurements,
                Ruler(14, 0.04m),
                Policy)
            .Family.State.ShouldBe(DelphiLiveFamilyState.Unavailable);
    }

    public static IEnumerable<object[]> VolumeGrid()
    {
        yield return new object[] { 0.10m, 0.001m, DelphiLiveFamilyState.Supportive, DelphiLiveReasonCodes.DirectionalVolumeSupportive };
        yield return new object[] { 0.10m, 0m, DelphiLiveFamilyState.Neutral, DelphiLiveReasonCodes.VolumePriceNotDirectional };
        yield return new object[] { 0.10m, -0.001m, DelphiLiveFamilyState.Neutral, DelphiLiveReasonCodes.VolumePriceConflict };
        yield return new object[] { 0.09m, 0.001m, DelphiLiveFamilyState.Neutral, DelphiLiveReasonCodes.DirectionalVolumeWithinDeadband };
        yield return new object[] { -0.09m, -0.001m, DelphiLiveFamilyState.Neutral, DelphiLiveReasonCodes.DirectionalVolumeWithinDeadband };
        yield return new object[] { -0.10m, 0.001m, DelphiLiveFamilyState.Neutral, DelphiLiveReasonCodes.VolumePriceConflict };
        yield return new object[] { -0.10m, 0m, DelphiLiveFamilyState.Neutral, DelphiLiveReasonCodes.VolumePriceNotDirectional };
        yield return new object[] { -0.10m, -0.001m, DelphiLiveFamilyState.Weakening, DelphiLiveReasonCodes.DirectionalVolumeWeakening };
    }

    [Theory]
    [MemberData(nameof(VolumeGrid))]
    public void VolumeSupportRequiresBalanceAndPriceSignAgreement(
        decimal balance,
        decimal priceReturn,
        DelphiLiveFamilyState expected,
        string reason)
    {
        var measurements = new DelphiLiveDirectionalVolumeMeasurements(
            BarEndUtc,
            DelphiLiveScalarMeasurement.Available(balance),
            DelphiLiveScalarMeasurement.Available(priceReturn),
            1_000);

        DelphiLiveVolumeSupportJudgment result =
            DelphiLiveFamilyClassifiers.ClassifyVolumeSupport(measurements, Policy);

        result.Family.State.ShouldBe(expected);
        result.Family.ReasonCode.ShouldBe(reason);
    }

    public static IEnumerable<object[]> StructureReferenceCombinations()
    {
        for (int previous = 0; previous < 4; previous++)
        {
            for (int vwap = 0; vwap < 4; vwap++)
            {
                for (int range = 0; range < 4; range++)
                {
                    int[] values = { previous, vwap, range };
                    int available = 0;
                    int bullish = 0;
                    int bearish = 0;
                    foreach (int value in values)
                    {
                        if (value != 3)
                            available++;
                        if (value == 0)
                            bullish++;
                        if (value == 2)
                            bearish++;
                    }
                    DelphiLiveFamilyState expected = available < 2
                        ? DelphiLiveFamilyState.Unavailable
                        : bullish > 0 && bearish > 0
                            ? DelphiLiveFamilyState.NeutralConflict
                            : bullish >= 2
                                ? DelphiLiveFamilyState.Supportive
                                : bullish == 1
                                    ? DelphiLiveFamilyState.PositiveLeaning
                                    : bearish >= 2
                                        ? DelphiLiveFamilyState.Weakening
                                        : bearish == 1
                                            ? DelphiLiveFamilyState.NegativeLeaning
                                            : DelphiLiveFamilyState.Neutral;
                    yield return new object[] { previous, vwap, range, expected };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(StructureReferenceCombinations))]
    public void PriceStructureExhaustivelyAppliesTheNoConflictRule(
        int previous,
        int vwap,
        int range,
        DelphiLiveFamilyState expected)
    {
        DelphiLivePriceStructureMeasurements measurements = new(
            BarEndUtc,
            DelphiLiveScalarMeasurement.Available(100m),
            LevelReference(previous),
            LevelReference(vwap),
            RangeReference(range));

        DelphiLivePriceStructureJudgment result =
            DelphiLiveFamilyClassifiers.ClassifyPriceStructure(
                measurements,
                Ruler(10, 0.04m),
                Policy);

        result.Family.State.ShouldBe(expected);
    }

    [Fact]
    public void StructureBufferBoundariesAreInclusiveAndPriorRangeExcludesSmallCrosses()
    {
        var above = new DelphiLivePriceStructureMeasurements(
            BarEndUtc,
            DelphiLiveScalarMeasurement.Available(100.2m),
            DelphiLiveScalarMeasurement.Available(100m),
            DelphiLiveScalarMeasurement.Available(100m),
            new DelphiLivePriorRangeMeasurements(
                DelphiLiveMeasurementAvailability.Unavailable,
                null,
                null,
                DelphiLiveReasonCodes.Unavailable));
        DelphiLivePriceStructureJudgment aboveResult =
            DelphiLiveFamilyClassifiers.ClassifyPriceStructure(
                above,
                Ruler(10, 0.04m),
                Policy);
        aboveResult.PreviousClose.State.ShouldBe(DelphiLiveStructureReferenceState.Above);
        aboveResult.SessionVwap.State.ShouldBe(DelphiLiveStructureReferenceState.Above);

        var below = above with
        {
            CurrentClose = DelphiLiveScalarMeasurement.Available(99.8m)
        };
        DelphiLiveFamilyClassifiers.ClassifyPriceStructure(
                below,
                Ruler(10, 0.04m),
                Policy)
            .PreviousClose.State.ShouldBe(DelphiLiveStructureReferenceState.Below);

        var smallRangeCross = new DelphiLivePriceStructureMeasurements(
            BarEndUtc,
            DelphiLiveScalarMeasurement.Available(100.1m),
            DelphiLiveScalarMeasurement.Unavailable(),
            DelphiLiveScalarMeasurement.Unavailable(),
            new DelphiLivePriorRangeMeasurements(
                DelphiLiveMeasurementAvailability.Available,
                100m,
                99m,
                DelphiLiveReasonCodes.Available));
        DelphiLiveFamilyClassifiers.ClassifyPriceStructure(
                smallRangeCross,
                Ruler(10, 0.04m),
                Policy)
            .PriorTwentyMinuteRange.State
            .ShouldBe(DelphiLiveStructureReferenceState.InsideOrAtRange);
    }

    private static DelphiLiveWindowReturnMeasurement WindowFromUnits(
        TimeSpan horizon,
        decimal rawUnits,
        decimal excessUnits,
        decimal ruler = 0.04m)
    {
        decimal stockReturn = rawUnits * ruler;
        decimal excessReturn = excessUnits * ruler;
        return WindowFromReturns(horizon, stockReturn, stockReturn - excessReturn);
    }

    private static DelphiLiveWindowReturnMeasurement WindowFromReturns(
        TimeSpan horizon,
        decimal stockReturn,
        decimal benchmarkReturn) =>
        new(
            horizon,
            DelphiLiveScalarMeasurement.Available(stockReturn),
            DelphiLiveScalarMeasurement.Available(benchmarkReturn),
            DelphiLiveScalarMeasurement.Available(stockReturn - benchmarkReturn));

    private static DelphiLiveWindowReturnMeasurement WindowForState(
        TimeSpan horizon,
        DelphiLiveFamilyState state) =>
        state switch
        {
            DelphiLiveFamilyState.Supportive => WindowFromUnits(horizon, 0.25m, 0.05m),
            DelphiLiveFamilyState.Weakening => WindowFromUnits(horizon, -0.25m, -0.05m),
            DelphiLiveFamilyState.Neutral => WindowFromUnits(horizon, 0m, 0m),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

    private static DelphiLiveWindowReturnMeasurement NotMatureWindow(TimeSpan horizon) =>
        new(
            horizon,
            DelphiLiveScalarMeasurement.NotMature(),
            DelphiLiveScalarMeasurement.NotMature(),
            DelphiLiveScalarMeasurement.NotMature());

    private static DelphiLiveWindowReturnMeasurement UnavailableWindow(TimeSpan horizon) =>
        new(
            horizon,
            DelphiLiveScalarMeasurement.Available(0.01m),
            DelphiLiveScalarMeasurement.Unavailable(),
            DelphiLiveScalarMeasurement.Unavailable());

    private static DelphiLivePriceMovementMeasurements PriceMovement(
        DelphiLiveWindowReturnMeasurement twenty,
        DelphiLiveWindowReturnMeasurement hour) =>
        new(
            BarEndUtc,
            twenty,
            hour,
            NotMatureWindow(TimeSpan.FromHours(2)),
            NotMatureWindow(TimeSpan.FromHours(3)),
            DelphiLiveScalarMeasurement.Unavailable());

    private static DelphiLiveTrueRangeRulerMeasurement Ruler(
        int sessionCount,
        decimal value) =>
        new(
            sessionCount,
            new DateOnly(2026, 9, 3),
            DelphiLiveScalarMeasurement.Available(value));

    private static DelphiLiveScalarMeasurement LevelReference(int code) =>
        code switch
        {
            0 => DelphiLiveScalarMeasurement.Available(99m),
            1 => DelphiLiveScalarMeasurement.Available(100m),
            2 => DelphiLiveScalarMeasurement.Available(101m),
            3 => DelphiLiveScalarMeasurement.Unavailable(),
            _ => throw new ArgumentOutOfRangeException(nameof(code))
        };

    private static DelphiLivePriorRangeMeasurements RangeReference(int code) =>
        code switch
        {
            0 => new(
                DelphiLiveMeasurementAvailability.Available,
                99m,
                98m,
                DelphiLiveReasonCodes.Available),
            1 => new(
                DelphiLiveMeasurementAvailability.Available,
                101m,
                99m,
                DelphiLiveReasonCodes.Available),
            2 => new(
                DelphiLiveMeasurementAvailability.Available,
                102m,
                101m,
                DelphiLiveReasonCodes.Available),
            3 => new(
                DelphiLiveMeasurementAvailability.Unavailable,
                null,
                null,
                DelphiLiveReasonCodes.Unavailable),
            _ => throw new ArgumentOutOfRangeException(nameof(code))
        };
}
