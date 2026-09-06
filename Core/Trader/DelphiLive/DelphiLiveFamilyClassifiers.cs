#nullable enable

using System;
using System.Collections.Immutable;

namespace Core.Trader.DelphiLive;

public enum DelphiLivePriceMovementDirection
{
    Unavailable,
    AlignedUp,
    AlignedDown,
    RisingButLagging,
    FallingButOutperforming,
    MixedOrFlat
}

public enum DelphiLiveContextAlignment
{
    Unavailable,
    NotMature,
    Aligned,
    Mixed,
    Opposed
}

public sealed record DelphiLivePersistenceJudgment(
    DelphiLiveFamilyJudgment Family,
    int? Score);

public sealed record DelphiLivePriceMovementWindowJudgment(
    TimeSpan Horizon,
    DelphiLiveFamilyState State,
    DelphiLivePriceMovementDirection Direction,
    decimal? RawMoveUnits,
    decimal? ExcessUnits,
    string ReasonCode);

public sealed record DelphiLivePriceMovementJudgment(
    DelphiLiveFamilyJudgment Family,
    DelphiLivePriceMovementWindowJudgment TwentyMinute,
    DelphiLivePriceMovementWindowJudgment OneHour,
    DelphiLiveContextAlignment TwoHourContext,
    DelphiLiveContextAlignment ThreeHourContext,
    DelphiLiveContextAlignment PreviousCloseContext);

public enum DelphiLivePriceMovementCounterfactualFamily
{
    RawMoveUnits,
    ExcessUnits,
    VolatilityRuler
}

public sealed record DelphiLivePriceMovementCounterfactualJudgment(
    string VariantKey,
    DelphiLivePriceMovementCounterfactualFamily ThresholdFamily,
    decimal? UnitThreshold,
    int? RulerSessionCount,
    DelphiLivePriceMovementJudgment Judgment);

public static class DelphiLivePriceMovementCounterfactuals
{
    public const string RawMoveLower = "RawMoveUnitsLower";
    public const string RawMoveUpper = "RawMoveUnitsUpper";
    public const string ExcessLower = "ExcessUnitsLower";
    public const string ExcessUpper = "ExcessUnitsUpper";
    public const string MedianTrueRangePct14 = "MedianTrueRangePct14";
}

public sealed record DelphiLiveVolumeSupportJudgment(
    DelphiLiveFamilyJudgment Family,
    decimal? DirectionalVolumeBalance20,
    decimal? TwentyMinutePriceReturn);

public enum DelphiLiveStructureReferenceKind
{
    PreviousClose,
    SessionVwap,
    PriorTwentyMinuteRange
}

public enum DelphiLiveStructureReferenceState
{
    Unavailable,
    NotMature,
    Above,
    Below,
    AtLevel,
    Breakout,
    Breakdown,
    InsideOrAtRange
}

public sealed record DelphiLiveStructureReferenceJudgment(
    DelphiLiveStructureReferenceKind Reference,
    DelphiLiveStructureReferenceState State,
    decimal? PrimaryDistanceUnits,
    decimal? SecondaryDistanceUnits,
    string ReasonCode)
{
    public bool IsAvailable =>
        State is not DelphiLiveStructureReferenceState.Unavailable and
            not DelphiLiveStructureReferenceState.NotMature;

    public bool IsBullish =>
        State is DelphiLiveStructureReferenceState.Above or
            DelphiLiveStructureReferenceState.Breakout;

    public bool IsBearish =>
        State is DelphiLiveStructureReferenceState.Below or
            DelphiLiveStructureReferenceState.Breakdown;
}

public sealed record DelphiLivePriceStructureJudgment(
    DelphiLiveFamilyJudgment Family,
    DelphiLiveStructureReferenceJudgment PreviousClose,
    DelphiLiveStructureReferenceJudgment SessionVwap,
    DelphiLiveStructureReferenceJudgment PriorTwentyMinuteRange,
    int AvailableReferenceCount,
    int BullishReferenceCount,
    int BearishReferenceCount);

public static class DelphiLiveFamilyClassifiers
{
    public static DelphiLivePersistenceJudgment ClassifyPersistence(
        DelphiLivePersistenceMeasurements measurements)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        if (measurements.Availability == DelphiLiveMeasurementAvailability.NotMature)
        {
            return new DelphiLivePersistenceJudgment(
                Family(
                    DelphiLiveSignalFamily.Persistence,
                    DelphiLiveFamilyState.NotMature,
                    measurements.ReasonCode),
                null);
        }
        if (measurements.Availability != DelphiLiveMeasurementAvailability.Available ||
            !measurements.Score.HasValue)
        {
            return new DelphiLivePersistenceJudgment(
                Family(
                    DelphiLiveSignalFamily.Persistence,
                    DelphiLiveFamilyState.Unavailable,
                    measurements.ReasonCode),
                null);
        }

        int score = measurements.Score.Value;
        if (score is < -4 or > 4)
            throw new ArgumentOutOfRangeException(nameof(measurements), "Persistence score must be between -4 and +4.");
        (DelphiLiveFamilyState state, string reason) = score switch
        {
            >= 3 => (DelphiLiveFamilyState.Supportive, DelphiLiveReasonCodes.PersistenceSupportive),
            2 => (DelphiLiveFamilyState.PositiveLeaning, DelphiLiveReasonCodes.PersistencePositiveLeaning),
            >= -1 => (DelphiLiveFamilyState.Neutral, DelphiLiveReasonCodes.PersistenceNeutral),
            -2 => (DelphiLiveFamilyState.NegativeLeaning, DelphiLiveReasonCodes.PersistenceNegativeLeaning),
            _ => (DelphiLiveFamilyState.Weakening, DelphiLiveReasonCodes.PersistenceWeakening)
        };
        return new DelphiLivePersistenceJudgment(
            Family(DelphiLiveSignalFamily.Persistence, state, reason),
            score);
    }

    public static DelphiLivePriceMovementJudgment ClassifyPriceMovement(
        DelphiLivePriceMovementMeasurements measurements,
        DelphiLiveTrueRangeRulerMeasurement operationalRuler,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(operationalRuler);
        policy.Validate();

        DelphiLiveScalarMeasurement ruler =
            operationalRuler.SessionCount == policy.SelectedRulerSessions
                ? operationalRuler.MedianTrueRangePct
                : DelphiLiveScalarMeasurement.Unavailable(DelphiLiveReasonCodes.BaselineUnavailable);
        return ClassifyPriceMovementCore(
            measurements,
            ruler,
            policy.SelectedRawMoveThreshold,
            policy.SelectedExcessMoveThreshold);
    }

    public static ImmutableArray<DelphiLivePriceMovementCounterfactualJudgment>
        ClassifyPredeclaredPriceMovementCounterfactuals(
            DelphiLivePriceMovementMeasurements measurements,
            DelphiLiveVolatilityRulerMeasurements rulers,
            DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(rulers);
        policy.Validate();

        DelphiLiveScalarMeasurement operationalRuler =
            rulers.TenSession.SessionCount == policy.VolatilityRulers.OperationalSessions
                ? rulers.TenSession.MedianTrueRangePct
                : DelphiLiveScalarMeasurement.Unavailable(DelphiLiveReasonCodes.BaselineUnavailable);
        DelphiLiveScalarMeasurement challengerRuler =
            rulers.FourteenSession.SessionCount == policy.VolatilityRulers.ChallengerSessions
                ? rulers.FourteenSession.MedianTrueRangePct
                : DelphiLiveScalarMeasurement.Unavailable(DelphiLiveReasonCodes.BaselineUnavailable);

        return ImmutableArray.Create(
            new DelphiLivePriceMovementCounterfactualJudgment(
                DelphiLivePriceMovementCounterfactuals.RawMoveLower,
                DelphiLivePriceMovementCounterfactualFamily.RawMoveUnits,
                policy.RawMoveThresholds.Lower,
                null,
                ClassifyPriceMovementCore(
                    measurements,
                    operationalRuler,
                    policy.RawMoveThresholds.Lower,
                    policy.ExcessMoveThresholds.Operational)),
            new DelphiLivePriceMovementCounterfactualJudgment(
                DelphiLivePriceMovementCounterfactuals.RawMoveUpper,
                DelphiLivePriceMovementCounterfactualFamily.RawMoveUnits,
                policy.RawMoveThresholds.Upper,
                null,
                ClassifyPriceMovementCore(
                    measurements,
                    operationalRuler,
                    policy.RawMoveThresholds.Upper,
                    policy.ExcessMoveThresholds.Operational)),
            new DelphiLivePriceMovementCounterfactualJudgment(
                DelphiLivePriceMovementCounterfactuals.ExcessLower,
                DelphiLivePriceMovementCounterfactualFamily.ExcessUnits,
                policy.ExcessMoveThresholds.Lower,
                null,
                ClassifyPriceMovementCore(
                    measurements,
                    operationalRuler,
                    policy.RawMoveThresholds.Operational,
                    policy.ExcessMoveThresholds.Lower)),
            new DelphiLivePriceMovementCounterfactualJudgment(
                DelphiLivePriceMovementCounterfactuals.ExcessUpper,
                DelphiLivePriceMovementCounterfactualFamily.ExcessUnits,
                policy.ExcessMoveThresholds.Upper,
                null,
                ClassifyPriceMovementCore(
                    measurements,
                    operationalRuler,
                    policy.RawMoveThresholds.Operational,
                    policy.ExcessMoveThresholds.Upper)),
            new DelphiLivePriceMovementCounterfactualJudgment(
                DelphiLivePriceMovementCounterfactuals.MedianTrueRangePct14,
                DelphiLivePriceMovementCounterfactualFamily.VolatilityRuler,
                null,
                policy.VolatilityRulers.ChallengerSessions,
                ClassifyPriceMovementCore(
                    measurements,
                    challengerRuler,
                    policy.RawMoveThresholds.Operational,
                    policy.ExcessMoveThresholds.Operational)));
    }

    public static DelphiLivePriceMovementWindowJudgment ClassifyPriceMovementWindow(
        DelphiLiveWindowReturnMeasurement measurement,
        DelphiLiveScalarMeasurement operationalRuler,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        policy.Validate();

        return ClassifyPriceMovementWindowCore(
            measurement,
            operationalRuler,
            policy.SelectedRawMoveThreshold,
            policy.SelectedExcessMoveThreshold);
    }

    private static DelphiLivePriceMovementJudgment ClassifyPriceMovementCore(
        DelphiLivePriceMovementMeasurements measurements,
        DelphiLiveScalarMeasurement ruler,
        decimal rawThreshold,
        decimal excessThreshold)
    {
        DelphiLivePriceMovementWindowJudgment twentyMinute = ClassifyPriceMovementWindowCore(
            measurements.TwentyMinute,
            ruler,
            rawThreshold,
            excessThreshold);
        DelphiLivePriceMovementWindowJudgment oneHour = ClassifyPriceMovementWindowCore(
            measurements.OneHour,
            ruler,
            rawThreshold,
            excessThreshold);

        DelphiLiveFamilyJudgment family = CombinePriceMovementWindows(
            twentyMinute,
            oneHour);
        return new DelphiLivePriceMovementJudgment(
            family,
            twentyMinute,
            oneHour,
            ClassifyContext(measurements.TwoHour, family.State),
            ClassifyContext(measurements.ThreeHour, family.State),
            ClassifyPreviousCloseContext(measurements.PreviousCloseReturn, family.State));
    }

    private static DelphiLivePriceMovementWindowJudgment ClassifyPriceMovementWindowCore(
        DelphiLiveWindowReturnMeasurement measurement,
        DelphiLiveScalarMeasurement operationalRuler,
        decimal rawThreshold,
        decimal excessThreshold)
    {
        DelphiLiveMeasurementAvailability windowAvailability = CombineAvailability(
            measurement.StockReturn.Availability,
            measurement.BenchmarkReturn.Availability,
            measurement.ExcessReturn.Availability);
        if (windowAvailability == DelphiLiveMeasurementAvailability.NotMature)
        {
            return new DelphiLivePriceMovementWindowJudgment(
                measurement.Horizon,
                DelphiLiveFamilyState.NotMature,
                DelphiLivePriceMovementDirection.Unavailable,
                null,
                null,
                DelphiLiveReasonCodes.NotMature);
        }
        if (windowAvailability != DelphiLiveMeasurementAvailability.Available)
        {
            string reason = measurement.StockReturn.Availability != DelphiLiveMeasurementAvailability.Available
                ? measurement.StockReturn.ReasonCode
                : DelphiLiveReasonCodes.RelativeBaselineUnavailable;
            return new DelphiLivePriceMovementWindowJudgment(
                measurement.Horizon,
                DelphiLiveFamilyState.Unavailable,
                DelphiLivePriceMovementDirection.Unavailable,
                null,
                null,
                reason);
        }
        if (operationalRuler.Availability != DelphiLiveMeasurementAvailability.Available ||
            operationalRuler.Value is not > 0m)
        {
            return new DelphiLivePriceMovementWindowJudgment(
                measurement.Horizon,
                DelphiLiveFamilyState.Unavailable,
                DelphiLivePriceMovementDirection.Unavailable,
                null,
                null,
                DelphiLiveReasonCodes.BaselineUnavailable);
        }

        decimal stockReturn = measurement.StockReturn.RequireValue();
        decimal benchmarkReturn = measurement.BenchmarkReturn.RequireValue();
        decimal excessReturn = measurement.ExcessReturn.RequireValue();
        decimal ruler = operationalRuler.RequireValue();
        decimal rawUnits = stockReturn / ruler;
        decimal excessUnits = excessReturn / ruler;
        DelphiLivePriceMovementDirection direction = ClassifyDirection(
            stockReturn,
            benchmarkReturn,
            excessReturn);

        bool rawUp = rawUnits >= rawThreshold;
        bool rawDown = rawUnits <= -rawThreshold;
        bool excessUp = excessUnits >= excessThreshold;
        bool excessDown = excessUnits <= -excessThreshold;

        if (rawUp && excessUp)
        {
            return Window(
                measurement.Horizon,
                DelphiLiveFamilyState.Supportive,
                direction,
                rawUnits,
                excessUnits,
                DelphiLiveReasonCodes.Available);
        }
        if (rawDown && excessDown)
        {
            return Window(
                measurement.Horizon,
                DelphiLiveFamilyState.Weakening,
                direction,
                rawUnits,
                excessUnits,
                DelphiLiveReasonCodes.Available);
        }
        if ((rawUp && excessDown) || (rawDown && excessUp))
        {
            return Window(
                measurement.Horizon,
                DelphiLiveFamilyState.Neutral,
                direction,
                rawUnits,
                excessUnits,
                DelphiLiveReasonCodes.RawRelativeConflict);
        }
        if (rawUp || rawDown)
        {
            return Window(
                measurement.Horizon,
                DelphiLiveFamilyState.Neutral,
                direction,
                rawUnits,
                excessUnits,
                DelphiLiveReasonCodes.RawMoveWithoutRelativeAgreement);
        }
        if (excessUp || excessDown)
        {
            return Window(
                measurement.Horizon,
                DelphiLiveFamilyState.Neutral,
                direction,
                rawUnits,
                excessUnits,
                DelphiLiveReasonCodes.RelativeMoveWithoutRawAgreement);
        }

        string neutralReason = direction switch
        {
            DelphiLivePriceMovementDirection.RisingButLagging => DelphiLiveReasonCodes.RisingButLagging,
            DelphiLivePriceMovementDirection.FallingButOutperforming => DelphiLiveReasonCodes.FallingButOutperforming,
            DelphiLivePriceMovementDirection.AlignedUp or DelphiLivePriceMovementDirection.AlignedDown =>
                DelphiLiveReasonCodes.RawMoveBelowThreshold,
            _ => DelphiLiveReasonCodes.MixedOrFlat
        };
        return Window(
            measurement.Horizon,
            DelphiLiveFamilyState.Neutral,
            direction,
            rawUnits,
            excessUnits,
            neutralReason);
    }

    public static DelphiLiveVolumeSupportJudgment ClassifyVolumeSupport(
        DelphiLiveDirectionalVolumeMeasurements measurements,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        policy.Validate();

        DelphiLiveMeasurementAvailability availability = CombineAvailability(
            measurements.Balance.Availability,
            measurements.TwentyMinutePriceReturn.Availability);
        if (availability == DelphiLiveMeasurementAvailability.NotMature)
        {
            return new DelphiLiveVolumeSupportJudgment(
                Family(
                    DelphiLiveSignalFamily.VolumeSupport,
                    DelphiLiveFamilyState.NotMature,
                    DelphiLiveReasonCodes.NotMature),
                null,
                null);
        }
        if (availability != DelphiLiveMeasurementAvailability.Available)
        {
            string unavailableReason = measurements.Balance.Availability != DelphiLiveMeasurementAvailability.Available
                ? measurements.Balance.ReasonCode
                : measurements.TwentyMinutePriceReturn.ReasonCode;
            return new DelphiLiveVolumeSupportJudgment(
                Family(
                    DelphiLiveSignalFamily.VolumeSupport,
                    DelphiLiveFamilyState.Unavailable,
                    unavailableReason),
                measurements.Balance.Value,
                measurements.TwentyMinutePriceReturn.Value);
        }

        decimal balance = measurements.Balance.RequireValue();
        decimal priceReturn = measurements.TwentyMinutePriceReturn.RequireValue();
        if (balance is < -1m or > 1m)
            throw new ArgumentOutOfRangeException(nameof(measurements), "Directional Volume balance must be in [-1,+1].");

        decimal threshold = policy.DirectionalVolumeThreshold;
        DelphiLiveFamilyState state;
        string reason;
        if (balance >= threshold && priceReturn > 0m)
        {
            state = DelphiLiveFamilyState.Supportive;
            reason = DelphiLiveReasonCodes.DirectionalVolumeSupportive;
        }
        else if (balance <= -threshold && priceReturn < 0m)
        {
            state = DelphiLiveFamilyState.Weakening;
            reason = DelphiLiveReasonCodes.DirectionalVolumeWeakening;
        }
        else if (balance > -threshold && balance < threshold)
        {
            state = DelphiLiveFamilyState.Neutral;
            reason = DelphiLiveReasonCodes.DirectionalVolumeWithinDeadband;
        }
        else if (priceReturn == 0m)
        {
            state = DelphiLiveFamilyState.Neutral;
            reason = DelphiLiveReasonCodes.VolumePriceNotDirectional;
        }
        else
        {
            state = DelphiLiveFamilyState.Neutral;
            reason = DelphiLiveReasonCodes.VolumePriceConflict;
        }

        return new DelphiLiveVolumeSupportJudgment(
            Family(DelphiLiveSignalFamily.VolumeSupport, state, reason),
            balance,
            priceReturn);
    }

    public static DelphiLivePriceStructureJudgment ClassifyPriceStructure(
        DelphiLivePriceStructureMeasurements measurements,
        DelphiLiveTrueRangeRulerMeasurement operationalRuler,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(operationalRuler);
        policy.Validate();

        DelphiLiveScalarMeasurement ruler =
            operationalRuler.SessionCount == policy.SelectedRulerSessions
                ? operationalRuler.MedianTrueRangePct
                : DelphiLiveScalarMeasurement.Unavailable(DelphiLiveReasonCodes.BaselineUnavailable);
        DelphiLiveStructureReferenceJudgment previousClose = ClassifyLevel(
            DelphiLiveStructureReferenceKind.PreviousClose,
            measurements.CurrentClose,
            measurements.PreviousClose,
            ruler,
            policy.StructureBufferUnits);
        DelphiLiveStructureReferenceJudgment sessionVwap = ClassifyLevel(
            DelphiLiveStructureReferenceKind.SessionVwap,
            measurements.CurrentClose,
            measurements.SessionVwap,
            ruler,
            policy.StructureBufferUnits);
        DelphiLiveStructureReferenceJudgment priorRange = ClassifyPriorRange(
            measurements.CurrentClose,
            measurements.PriorTwentyMinuteRange,
            ruler,
            policy.StructureBufferUnits);

        DelphiLiveStructureReferenceJudgment[] references =
            { previousClose, sessionVwap, priorRange };
        int available = 0;
        int bullish = 0;
        int bearish = 0;
        foreach (DelphiLiveStructureReferenceJudgment reference in references)
        {
            if (reference.IsAvailable)
                available++;
            if (reference.IsBullish)
                bullish++;
            if (reference.IsBearish)
                bearish++;
        }

        DelphiLiveFamilyState state;
        string reason;
        if (available < policy.MinimumStructureReferences)
        {
            state = DelphiLiveFamilyState.Unavailable;
            reason = DelphiLiveReasonCodes.Unavailable;
        }
        else if (bullish > 0 && bearish > 0)
        {
            state = DelphiLiveFamilyState.NeutralConflict;
            reason = DelphiLiveReasonCodes.PriceStructureNeutralConflict;
        }
        else if (bullish >= 2)
        {
            state = DelphiLiveFamilyState.Supportive;
            reason = DelphiLiveReasonCodes.PriceStructureSupportive;
        }
        else if (bullish == 1)
        {
            state = DelphiLiveFamilyState.PositiveLeaning;
            reason = DelphiLiveReasonCodes.PriceStructurePositiveLeaning;
        }
        else if (bearish >= 2)
        {
            state = DelphiLiveFamilyState.Weakening;
            reason = DelphiLiveReasonCodes.PriceStructureWeakening;
        }
        else if (bearish == 1)
        {
            state = DelphiLiveFamilyState.NegativeLeaning;
            reason = DelphiLiveReasonCodes.PriceStructureNegativeLeaning;
        }
        else
        {
            state = DelphiLiveFamilyState.Neutral;
            reason = DelphiLiveReasonCodes.PriceStructureNeutral;
        }

        return new DelphiLivePriceStructureJudgment(
            Family(DelphiLiveSignalFamily.PriceStructure, state, reason),
            previousClose,
            sessionVwap,
            priorRange,
            available,
            bullish,
            bearish);
    }

    private static DelphiLiveFamilyJudgment CombinePriceMovementWindows(
        DelphiLivePriceMovementWindowJudgment twentyMinute,
        DelphiLivePriceMovementWindowJudgment oneHour)
    {
        if (twentyMinute.State == DelphiLiveFamilyState.NotMature)
        {
            return Family(
                DelphiLiveSignalFamily.PriceMovement,
                DelphiLiveFamilyState.NotMature,
                DelphiLiveReasonCodes.NotMature);
        }
        if (twentyMinute.State == DelphiLiveFamilyState.Unavailable)
        {
            return Family(
                DelphiLiveSignalFamily.PriceMovement,
                DelphiLiveFamilyState.Unavailable,
                twentyMinute.ReasonCode);
        }
        if (oneHour.State == DelphiLiveFamilyState.NotMature)
        {
            return Family(
                DelphiLiveSignalFamily.PriceMovement,
                twentyMinute.State,
                DelphiLiveReasonCodes.TwentyMinuteOnly);
        }
        if (oneHour.State == DelphiLiveFamilyState.Unavailable)
        {
            return Family(
                DelphiLiveSignalFamily.PriceMovement,
                DelphiLiveFamilyState.Unavailable,
                oneHour.ReasonCode);
        }

        bool twentyDirectional = IsDirectional(twentyMinute.State);
        bool hourDirectional = IsDirectional(oneHour.State);
        if (twentyDirectional && hourDirectional)
        {
            return twentyMinute.State == oneHour.State
                ? Family(
                    DelphiLiveSignalFamily.PriceMovement,
                    twentyMinute.State,
                    DelphiLiveReasonCodes.WindowsAgree)
                : Family(
                    DelphiLiveSignalFamily.PriceMovement,
                    DelphiLiveFamilyState.NeutralConflict,
                    DelphiLiveReasonCodes.MeaningfulWindowConflict);
        }
        if (twentyDirectional)
        {
            return Family(
                DelphiLiveSignalFamily.PriceMovement,
                twentyMinute.State,
                DelphiLiveReasonCodes.TwentyMinuteCarries);
        }
        if (hourDirectional)
        {
            return Family(
                DelphiLiveSignalFamily.PriceMovement,
                oneHour.State,
                DelphiLiveReasonCodes.OneHourCarries);
        }
        return Family(
            DelphiLiveSignalFamily.PriceMovement,
            DelphiLiveFamilyState.Neutral,
            DelphiLiveReasonCodes.NoMeaningfulVotingWindow);
    }

    private static DelphiLiveStructureReferenceJudgment ClassifyLevel(
        DelphiLiveStructureReferenceKind kind,
        DelphiLiveScalarMeasurement currentClose,
        DelphiLiveScalarMeasurement reference,
        DelphiLiveScalarMeasurement ruler,
        decimal buffer)
    {
        if (currentClose.Availability != DelphiLiveMeasurementAvailability.Available ||
            currentClose.Value is not > 0m)
            return UnavailableReference(kind, currentClose.ReasonCode);
        if (reference.Availability == DelphiLiveMeasurementAvailability.NotMature)
            return NotMatureReference(kind, reference.ReasonCode);
        if (reference.Availability != DelphiLiveMeasurementAvailability.Available || reference.Value is not > 0m)
            return UnavailableReference(kind, reference.ReasonCode);
        if (ruler.Availability != DelphiLiveMeasurementAvailability.Available || ruler.Value is not > 0m)
            return UnavailableReference(kind, DelphiLiveReasonCodes.BaselineUnavailable);

        decimal units = (currentClose.RequireValue() / reference.RequireValue() - 1m) /
            ruler.RequireValue();
        DelphiLiveStructureReferenceState state = units >= buffer
            ? DelphiLiveStructureReferenceState.Above
            : units <= -buffer
                ? DelphiLiveStructureReferenceState.Below
                : DelphiLiveStructureReferenceState.AtLevel;
        return new DelphiLiveStructureReferenceJudgment(
            kind,
            state,
            units,
            null,
            state.ToString());
    }

    private static DelphiLiveStructureReferenceJudgment ClassifyPriorRange(
        DelphiLiveScalarMeasurement currentClose,
        DelphiLivePriorRangeMeasurements range,
        DelphiLiveScalarMeasurement ruler,
        decimal buffer)
    {
        const DelphiLiveStructureReferenceKind kind =
            DelphiLiveStructureReferenceKind.PriorTwentyMinuteRange;
        if (currentClose.Availability != DelphiLiveMeasurementAvailability.Available ||
            currentClose.Value is not > 0m)
            return UnavailableReference(kind, currentClose.ReasonCode);
        if (range.Availability == DelphiLiveMeasurementAvailability.NotMature)
            return NotMatureReference(kind, range.ReasonCode);
        if (range.Availability != DelphiLiveMeasurementAvailability.Available ||
            range.High is not > 0m ||
            range.Low is not > 0m ||
            range.Low > range.High)
            return UnavailableReference(kind, range.ReasonCode);
        if (ruler.Availability != DelphiLiveMeasurementAvailability.Available || ruler.Value is not > 0m)
            return UnavailableReference(kind, DelphiLiveReasonCodes.BaselineUnavailable);

        decimal close = currentClose.RequireValue();
        decimal highUnits = (close / range.High.Value - 1m) / ruler.RequireValue();
        decimal lowUnits = (close / range.Low.Value - 1m) / ruler.RequireValue();
        DelphiLiveStructureReferenceState state = highUnits >= buffer
            ? DelphiLiveStructureReferenceState.Breakout
            : lowUnits <= -buffer
                ? DelphiLiveStructureReferenceState.Breakdown
                : DelphiLiveStructureReferenceState.InsideOrAtRange;
        return new DelphiLiveStructureReferenceJudgment(
            kind,
            state,
            highUnits,
            lowUnits,
            state.ToString());
    }

    private static DelphiLiveContextAlignment ClassifyContext(
        DelphiLiveWindowReturnMeasurement measurement,
        DelphiLiveFamilyState familyState)
    {
        DelphiLiveMeasurementAvailability availability = CombineAvailability(
            measurement.StockReturn.Availability,
            measurement.BenchmarkReturn.Availability,
            measurement.ExcessReturn.Availability);
        if (availability == DelphiLiveMeasurementAvailability.NotMature)
            return DelphiLiveContextAlignment.NotMature;
        if (availability != DelphiLiveMeasurementAvailability.Available)
            return DelphiLiveContextAlignment.Unavailable;

        DelphiLivePriceMovementDirection direction = ClassifyDirection(
            measurement.StockReturn.RequireValue(),
            measurement.BenchmarkReturn.RequireValue(),
            measurement.ExcessReturn.RequireValue());
        return Align(direction, familyState);
    }

    private static DelphiLiveContextAlignment ClassifyPreviousCloseContext(
        DelphiLiveScalarMeasurement previousCloseReturn,
        DelphiLiveFamilyState familyState)
    {
        if (previousCloseReturn.Availability == DelphiLiveMeasurementAvailability.NotMature)
            return DelphiLiveContextAlignment.NotMature;
        if (previousCloseReturn.Availability != DelphiLiveMeasurementAvailability.Available)
            return DelphiLiveContextAlignment.Unavailable;

        decimal value = previousCloseReturn.RequireValue();
        if (value == 0m || !IsDirectional(familyState))
            return DelphiLiveContextAlignment.Mixed;
        bool aligned = familyState == DelphiLiveFamilyState.Supportive
            ? value > 0m
            : value < 0m;
        return aligned ? DelphiLiveContextAlignment.Aligned : DelphiLiveContextAlignment.Opposed;
    }

    private static DelphiLiveContextAlignment Align(
        DelphiLivePriceMovementDirection direction,
        DelphiLiveFamilyState familyState)
    {
        if (!IsDirectional(familyState))
            return DelphiLiveContextAlignment.Mixed;
        bool aligned =
            familyState == DelphiLiveFamilyState.Supportive && direction == DelphiLivePriceMovementDirection.AlignedUp ||
            familyState == DelphiLiveFamilyState.Weakening && direction == DelphiLivePriceMovementDirection.AlignedDown;
        if (aligned)
            return DelphiLiveContextAlignment.Aligned;
        bool opposed =
            familyState == DelphiLiveFamilyState.Supportive && direction == DelphiLivePriceMovementDirection.AlignedDown ||
            familyState == DelphiLiveFamilyState.Weakening && direction == DelphiLivePriceMovementDirection.AlignedUp;
        return opposed ? DelphiLiveContextAlignment.Opposed : DelphiLiveContextAlignment.Mixed;
    }

    private static DelphiLivePriceMovementDirection ClassifyDirection(
        decimal stockReturn,
        decimal benchmarkReturn,
        decimal excessReturn)
    {
        if (stockReturn > 0m && stockReturn > benchmarkReturn && excessReturn > 0m)
            return DelphiLivePriceMovementDirection.AlignedUp;
        if (stockReturn < 0m && stockReturn < benchmarkReturn && excessReturn < 0m)
            return DelphiLivePriceMovementDirection.AlignedDown;
        if (stockReturn > 0m && stockReturn < benchmarkReturn && excessReturn < 0m)
            return DelphiLivePriceMovementDirection.RisingButLagging;
        if (stockReturn < 0m && stockReturn > benchmarkReturn && excessReturn > 0m)
            return DelphiLivePriceMovementDirection.FallingButOutperforming;
        return DelphiLivePriceMovementDirection.MixedOrFlat;
    }

    private static DelphiLivePriceMovementWindowJudgment Window(
        TimeSpan horizon,
        DelphiLiveFamilyState state,
        DelphiLivePriceMovementDirection direction,
        decimal rawUnits,
        decimal excessUnits,
        string reasonCode) =>
        new(horizon, state, direction, rawUnits, excessUnits, reasonCode);

    private static DelphiLiveStructureReferenceJudgment UnavailableReference(
        DelphiLiveStructureReferenceKind kind,
        string reasonCode) =>
        new(kind, DelphiLiveStructureReferenceState.Unavailable, null, null, reasonCode);

    private static DelphiLiveStructureReferenceJudgment NotMatureReference(
        DelphiLiveStructureReferenceKind kind,
        string reasonCode) =>
        new(kind, DelphiLiveStructureReferenceState.NotMature, null, null, reasonCode);

    private static DelphiLiveFamilyJudgment Family(
        DelphiLiveSignalFamily family,
        DelphiLiveFamilyState state,
        string reasonCode) =>
        new(family, state, reasonCode);

    private static bool IsDirectional(DelphiLiveFamilyState state) =>
        state is DelphiLiveFamilyState.Supportive or DelphiLiveFamilyState.Weakening;

    private static DelphiLiveMeasurementAvailability CombineAvailability(
        params DelphiLiveMeasurementAvailability[] values)
    {
        bool notMature = false;
        foreach (DelphiLiveMeasurementAvailability value in values)
        {
            if (value == DelphiLiveMeasurementAvailability.Unavailable)
                return DelphiLiveMeasurementAvailability.Unavailable;
            if (value == DelphiLiveMeasurementAvailability.NotMature)
                notMature = true;
        }
        return notMature
            ? DelphiLiveMeasurementAvailability.NotMature
            : DelphiLiveMeasurementAvailability.Available;
    }
}
