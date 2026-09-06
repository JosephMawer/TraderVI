#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Trader.DelphiLive;

public sealed record DelphiLiveEvaluationState(
    DelphiLiveDataConfidence Confidence,
    DelphiLiveLifecycleSnapshot Lifecycle,
    DelphiLiveLifecycleSnapshot ResearchLifecycle,
    ImmutableArray<DelphiLiveFamilyJudgment> FamilyJudgments,
    DelphiLiveMomentumJudgment Momentum,
    int? PersistenceScore)
{
    public static DelphiLiveEvaluationState Initial(bool isCurrentCandidate)
    {
        ImmutableArray<DelphiLiveFamilyJudgment> families = Enum.GetValues<DelphiLiveSignalFamily>()
            .Select(family => new DelphiLiveFamilyJudgment(
                family, DelphiLiveFamilyState.NotMature, DelphiLiveReasonCodes.NotMature))
            .ToImmutableArray();
        return new(DelphiLiveDataConfidence.Normal,
            DelphiLiveLifecycleSnapshot.NewSession(isCurrentCandidate),
            DelphiLiveLifecycleSnapshot.NewSession(isCurrentCandidate),
            families, DelphiLiveFamilyCombiner.Combine(families), null);
    }
}

public sealed record DelphiLiveEvaluationInput
{
    public required Guid EvaluationId { get; init; }
    public required Guid SessionId { get; init; }
    public required DateTime BarEndUtc { get; init; }
    public required DateTime EvaluatedUtc { get; init; }
    public required DelphiLiveFiveMinuteSeries Stock { get; init; }
    public required DelphiLiveFiveMinuteSeries Xiu { get; init; }
    public required DelphiLiveVolatilityRulerMeasurements VolatilityRulers { get; init; }
    public required DelphiLiveEvaluationState PreviousState { get; init; }
    public required DelphiLivePolicyDefinition Policy { get; init; }
    public decimal? PreviousStockSessionClose { get; init; }
    public decimal? PreviousXiuSessionClose { get; init; }
    public decimal? MedianFullDayVolume20 { get; init; }
    public DelphiLiveDailySetupQuality? DailySetup { get; init; }
    public bool IsSessionCarryCandidate { get; init; }
    public bool ExactPairPersistedOnTime { get; init; }
    // IsHeld means owned by this Delphi Live policy, not merely observed as a
    // tracked Real, Operator Ghost, or other policy's position.
    public bool IsHeld { get; init; }
    public bool HasPendingBuy { get; init; }
    public bool HasPendingSell { get; init; }
    public decimal? AveragePurchasePrice { get; init; }
    public DelphiLiveProfitProtectionState? ProfitProtection { get; init; }
}

public sealed record DelphiLiveEvaluationResult(
    Guid EvaluationId,
    bool ObservationIsValid,
    bool FamiliesMature,
    Guid? CurrentStockObservationId,
    DelphiLiveEvaluationState NextState,
    DelphiLiveLifecycleDecision Lifecycle,
    bool ConfirmedLiveEligible,
    DateTime? ConfirmationStartedBarEndUtc,
    DelphiLiveRankCandidate? RankCandidate,
    DelphiLivePersistenceMeasurements PersistenceMeasurements,
    DelphiLivePriceMovementMeasurements PriceMovementMeasurements,
    DelphiLiveDirectionalVolumeMeasurements VolumeMeasurements,
    DelphiLivePriceStructureMeasurements StructureMeasurements,
    DelphiLivePersistenceJudgment Persistence,
    DelphiLivePriceMovementJudgment PriceMovement,
    DelphiLiveVolumeSupportJudgment VolumeSupport,
    DelphiLivePriceStructureJudgment PriceStructure,
    ImmutableArray<DelphiLivePriceMovementCounterfactualJudgment> Counterfactuals,
    DelphiLiveSafetyInput SafetyInput,
    DelphiLiveSafetyEvaluation Safety,
    ImmutableDictionary<string, decimal?> RawValues,
    ImmutableDictionary<string, string> DerivedFacts);

/// <summary>
/// Composes the frozen V1 deterministic stages against one persisted checkpoint.
/// It has no I/O and grants no action authority to a host or another portfolio.
/// </summary>
public static class DelphiLiveEvaluationEngine
{
    public static DelphiLiveEvaluationResult Evaluate(DelphiLiveEvaluationInput input)
    {
        Validate(input);
        DelphiLivePolicyDefinition policy = input.Policy;
        DelphiLiveEvaluationState prior = input.PreviousState;
        // Receipt provenance is checked even when the collector declared its pair
        // complete; snapshots containing future receipts cannot become decisions.
        DelphiLiveFiveMinuteSeries stock = KnownSeries(input.Stock, input.EvaluatedUtc);
        DelphiLiveFiveMinuteSeries xiu = KnownSeries(input.Xiu, input.EvaluatedUtc);
        DelphiLiveFiveMinuteBar? current = CurrentOnTime(stock, input.BarEndUtc);
        bool clean = input.ExactPairPersistedOnTime && current is not null &&
            CurrentOnTime(xiu, input.BarEndUtc) is not null;
        DelphiLiveDataConfidence confidence = DelphiLiveDataConfidencePolicy.Advance(
            prior.Confidence, !clean, clean);

        DelphiLivePersistenceMeasurements persistenceFacts = DelphiLiveMeasurements.CalculatePersistence(
            stock, xiu, input.BarEndUtc, policy);
        DelphiLivePriceMovementMeasurements priceFacts = DelphiLiveMeasurements.CalculatePriceMovement(
            stock, xiu, input.BarEndUtc, input.PreviousStockSessionClose, policy,
            input.PreviousXiuSessionClose);
        DelphiLiveDirectionalVolumeMeasurements volumeFacts = DelphiLiveMeasurements.CalculateDirectionalVolume(
            stock, input.BarEndUtc, policy);
        DelphiLivePriceStructureMeasurements structureFacts = DelphiLiveMeasurements.CalculatePriceStructure(
            stock, input.BarEndUtc, input.PreviousStockSessionClose, policy);
        DelphiLivePersistenceJudgment persistence = DelphiLiveFamilyClassifiers.ClassifyPersistence(persistenceFacts);
        DelphiLivePriceMovementJudgment price = DelphiLiveFamilyClassifiers.ClassifyPriceMovement(
            priceFacts, input.VolatilityRulers.Select(policy), policy);
        DelphiLiveVolumeSupportJudgment volume = DelphiLiveFamilyClassifiers.ClassifyVolumeSupport(volumeFacts, policy);
        DelphiLivePriceStructureJudgment structure = DelphiLiveFamilyClassifiers.ClassifyPriceStructure(
            structureFacts, input.VolatilityRulers.Select(policy), policy);
        ImmutableArray<DelphiLiveFamilyJudgment> measuredFamilies =
            [persistence.Family, price.Family, volume.Family, structure.Family];
        bool mature = clean && measuredFamilies.All(family => family.State is not
            (DelphiLiveFamilyState.NotMature or DelphiLiveFamilyState.Unavailable));
        ImmutableArray<DelphiLiveFamilyJudgment> marketFamilies = clean ? measuredFamilies : prior.FamilyJudgments;
        DelphiLiveMomentumJudgment momentum = clean
            ? DelphiLiveFamilyCombiner.Combine(measuredFamilies)
            : prior.Momentum;
        int? persistenceScore = clean ? persistence.Score : prior.PersistenceScore;

        bool priorWeakening = prior.Lifecycle.LastScheduledBarEndUtc is DateTime previousEnd &&
            input.BarEndUtc - previousEnd == policy.BarInterval &&
            prior.Lifecycle.ConsecutiveStrongWeakeningObservations > 0;
        DelphiLiveSafetyInput safetyInput = new(
            input.IsHeld, !mature, input.AveragePurchasePrice, null, null,
            current?.Open, current?.Close,
            structure.SessionVwap.IsAvailable,
            structure.SessionVwap.State == DelphiLiveStructureReferenceState.Below,
            structure.PriorTwentyMinuteRange.IsAvailable,
            structure.PriorTwentyMinuteRange.State == DelphiLiveStructureReferenceState.Breakdown,
            volume.Family, momentum, priorWeakening, input.ProfitProtection);
        DelphiLiveSafetyEvaluation safety = DelphiLiveSafetyPolicy.Evaluate(safetyInput, policy);

        DelphiLiveLifecycleInput lifecycleInput = new(input.BarEndUtc, mature, clean, confidence,
            momentum, safety.EntrySafetyVetoActive, input.IsHeld, input.HasPendingBuy,
            input.HasPendingSell, input.DailySetup is not null);
        DelphiLiveLifecycleDecision lifecycle = DelphiLiveLifecyclePolicy.Advance(prior.Lifecycle, lifecycleInput);
        // The research fact is sampled before portfolio actions. Holdings, cash,
        // portfolio guards, entry caps, and pending actions do not filter it.
        DelphiLiveSafetyEvaluation researchSafety = DelphiLiveSafetyPolicy.Evaluate(safetyInput with
        {
            IsHeld = false, AveragePurchasePrice = null, ProfitProtection = null
        }, policy);
        DelphiLiveLifecycleDecision researchLifecycle = DelphiLiveLifecyclePolicy.Advance(
            prior.ResearchLifecycle, lifecycleInput with
            {
                IsHeld = false, HasPendingBuy = false, HasPendingSell = false,
                SafetyVetoActive = researchSafety.EntrySafetyVetoActive
            });
        bool confirmed = input.DailySetup is not null && clean && mature &&
            confidence.AllowsNewRisk && momentum.IsEntryEligibleStrong &&
            !researchSafety.EntrySafetyVetoActive && researchLifecycle.MayCreateBuyDecision;
        DelphiLiveEvaluationState next = new(confidence, lifecycle.Snapshot,
            researchLifecycle.Snapshot, marketFamilies, momentum, persistenceScore);
        DelphiLiveRankCandidate? rank = input.DailySetup is not null || input.IsSessionCarryCandidate
            ? new(stock.Symbol, momentum, persistenceScore, input.DailySetup, input.IsSessionCarryCandidate)
            : null;
        ImmutableArray<DelphiLivePriceMovementCounterfactualJudgment> counterfactuals =
            DelphiLiveFamilyClassifiers.ClassifyPredeclaredPriceMovementCounterfactuals(
                priceFacts, input.VolatilityRulers, policy);
        return new(input.EvaluationId, clean, mature, current?.ObservationId, next,
            lifecycle, confirmed,
            lifecycle.Snapshot.ConsecutiveStrongObservations > 0
                ? input.BarEndUtc - TimeSpan.FromTicks(policy.BarInterval.Ticks *
                    (lifecycle.Snapshot.ConsecutiveStrongObservations - 1))
                : null,
            rank, persistenceFacts, priceFacts, volumeFacts, structureFacts,
            persistence, price, volume, structure, counterfactuals, safetyInput, safety,
            RawValues(input, current, persistenceFacts, priceFacts, volumeFacts, structureFacts, price, structure),
            DerivedFacts(input, clean, mature, measuredFamilies, price, structure, lifecycle, confirmed));
    }

    public static DelphiLiveDecisionDossier CreateDossier(
        DelphiLiveEvaluationInput input,
        DelphiLiveEvaluationResult result,
        Guid decisionId,
        DateTime decisionUtc,
        string requestedAction,
        string actionState,
        IReadOnlyList<DelphiLiveDossierLensAttribution> sourceLenses,
        DelphiLiveOriginalEntryThesis? originalEntryThesis = null)
    {
        if (input.EvaluationId != result.EvaluationId || decisionUtc < input.EvaluatedUtc)
            throw new ArgumentException("The decision must follow its own persisted evaluation.");
        var dossier = new DelphiLiveDecisionDossier(
            input.Policy.DecisionDossierSchemaVersion, input.Policy.DecisionDossierVersion,
            decisionId, input.EvaluationId, decisionUtc, input.SessionId,
            input.DailySetup?.DelphiRunId, input.DailySetup?.CandidateId,
            input.DailySetup?.DailyStrategyVersionId, input.Policy.PolicyVersionId,
            input.Policy.PolicyDefinitionName, input.Policy.EvaluatorVersion,
            input.Policy.CollectorVersion, input.Policy.QuoteFillVersion,
            input.Stock.Symbol, input.BarEndUtc, sourceLenses,
            input.Stock.Bars.Concat(input.Xiu.Bars)
                .Where(bar => bar.EndUtc <= input.BarEndUtc && bar.ReceivedUtc <= input.EvaluatedUtc &&
                    bar.Disposition == DelphiLiveEvidenceDisposition.OperationalOnTime)
                .Select(bar => bar.ObservationId).Distinct().ToArray(),
            result.RawValues, result.DerivedFacts, result.NextState.FamilyJudgments,
            result.NextState.Momentum, input.PreviousState.Confidence, result.NextState.Confidence,
            input.PreviousState.Lifecycle.State, result.NextState.Lifecycle.State,
            result.Safety.FiredExitRules, result.Safety.PrimaryExitRule,
            new[] { result.Lifecycle.Snapshot.ReasonCode }
                .Concat(result.Safety.FiredExitRules.Select(rule => rule.ToString())).Distinct().ToArray(),
            requestedAction, actionState)
        {
            OriginalEntryThesis = originalEntryThesis
        };
        return DelphiLiveDecisionDossierBuilder.Validate(dossier, input.Policy);
    }

    private static ImmutableDictionary<string, decimal?> RawValues(
        DelphiLiveEvaluationInput input, DelphiLiveFiveMinuteBar? current,
        DelphiLivePersistenceMeasurements persistence, DelphiLivePriceMovementMeasurements price,
        DelphiLiveDirectionalVolumeMeasurements volume, DelphiLivePriceStructureMeasurements structure,
        DelphiLivePriceMovementJudgment priceJudgment, DelphiLivePriceStructureJudgment structureJudgment)
    {
        var values = new Dictionary<string, decimal?>(StringComparer.Ordinal)
        {
            ["Open"] = current?.Open, ["High"] = current?.High, ["Low"] = current?.Low,
            ["Close"] = current?.Close, ["Volume"] = current?.Volume,
            ["PersistenceScore"] = persistence.Score,
            ["DirectionalVolumeBalance20"] = volume.Balance.Value,
            ["DirectionalVolumeTotal20"] = volume.TotalVolume,
            ["PreviousSessionClose"] = structure.PreviousClose.Value,
            ["SessionVwap"] = structure.SessionVwap.Value,
            ["PriorTwentyMinuteHigh"] = structure.PriorTwentyMinuteRange.High,
            ["PriorTwentyMinuteLow"] = structure.PriorTwentyMinuteRange.Low,
            ["PreviousCloseReturn"] = price.PreviousCloseReturn.Value,
            ["PreviousCloseXiuReturn"] = price.PreviousCloseBenchmarkReturn.Value,
            ["PreviousCloseExcessReturn"] = price.PreviousCloseExcessReturn.Value,
            ["RawMoveUnits20"] = priceJudgment.TwentyMinute.RawMoveUnits,
            ["ExcessUnits20"] = priceJudgment.TwentyMinute.ExcessUnits,
            ["RawMoveUnits60"] = priceJudgment.OneHour.RawMoveUnits,
            ["ExcessUnits60"] = priceJudgment.OneHour.ExcessUnits,
            ["PreviousCloseDistanceUnits"] = structureJudgment.PreviousClose.PrimaryDistanceUnits,
            ["SessionVwapDistanceUnits"] = structureJudgment.SessionVwap.PrimaryDistanceUnits,
            ["PriorRangeHighDistanceUnits"] = structureJudgment.PriorTwentyMinuteRange.PrimaryDistanceUnits,
            ["PriorRangeLowDistanceUnits"] = structureJudgment.PriorTwentyMinuteRange.SecondaryDistanceUnits
        };
        // Full-day volume fraction is optional context, never a family vote or
        // a same-clock volume-pace estimate. An incomplete path stays null.
        DelphiLiveFiveMinuteBar[] sessionPath = input.Stock.Bars.Where(bar =>
            bar.EndUtc <= input.BarEndUtc && bar.ReceivedUtc <= input.EvaluatedUtc &&
            bar.Disposition == DelphiLiveEvidenceDisposition.OperationalOnTime).ToArray();
        bool completeVolumePath = sessionPath.Length ==
                (input.BarEndUtc - input.Stock.SessionOpenUtc).Ticks / input.Policy.BarInterval.Ticks &&
            sessionPath.Select((bar, index) => bar.EndUtc == input.Stock.SessionOpenUtc +
                TimeSpan.FromTicks(input.Policy.BarInterval.Ticks * (index + 1))).All(aligned => aligned);
        values["FullDayVolumeFraction20"] = input.MedianFullDayVolume20 is > 0m && completeVolumePath
            ? sessionPath.Sum(bar => (decimal)bar.Volume) / input.MedianFullDayVolume20.Value
            : null;
        foreach (DelphiLiveWindowReturnMeasurement window in new[] { price.TwentyMinute, price.OneHour, price.TwoHour, price.ThreeHour })
        {
            string suffix = ((int)window.Horizon.TotalMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture);
            values["RawReturn" + suffix] = window.StockReturn.Value;
            values["XiuReturn" + suffix] = window.BenchmarkReturn.Value;
            values["ExcessReturn" + suffix] = window.ExcessReturn.Value;
        }
        foreach (DelphiLiveTrueRangeRulerMeasurement ruler in new[]
                 { input.VolatilityRulers.FiveSession, input.VolatilityRulers.TenSession,
                   input.VolatilityRulers.FourteenSession, input.VolatilityRulers.TwentySession })
            values["MedianTrueRangePct" + ruler.SessionCount] = ruler.MedianTrueRangePct.Value;
        return values.ToImmutableDictionary(StringComparer.Ordinal);
    }

    private static ImmutableDictionary<string, string> DerivedFacts(
        DelphiLiveEvaluationInput input, bool clean, bool mature,
        ImmutableArray<DelphiLiveFamilyJudgment> families,
        DelphiLivePriceMovementJudgment price, DelphiLivePriceStructureJudgment structure,
        DelphiLiveLifecycleDecision lifecycle, bool confirmed)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Observation"] = clean ? "CompleteExactPair" : "MarketObservationMiss",
            ["MarketJudgment"] = clean ? "Current" : "FrozenPreviousJudgment",
            ["FamiliesMature"] = mature.ToString(),
            ["ConfirmedLiveEligible"] = confirmed.ToString(),
            ["LifecycleReason"] = lifecycle.Snapshot.ReasonCode,
            ["PriceMovement20Reason"] = price.TwentyMinute.ReasonCode,
            ["PriceMovement60Reason"] = price.OneHour.ReasonCode,
            ["PriceMovement20Direction"] = price.TwentyMinute.Direction.ToString(),
            ["PriceMovement60Direction"] = price.OneHour.Direction.ToString(),
            ["TwoHourContext"] = price.TwoHourContext.ToString(),
            ["ThreeHourContext"] = price.ThreeHourContext.ToString(),
            ["PreviousCloseContext"] = price.PreviousCloseContext.ToString(),
            ["FullDayVolumeFraction20Role"] = "NonVotingFullDayFractionNotVolumePace",
            ["PreviousCloseStructure"] = structure.PreviousClose.State.ToString(),
            ["SessionVwapStructure"] = structure.SessionVwap.State.ToString(),
            ["PriorRangeStructure"] = structure.PriorTwentyMinuteRange.State.ToString(),
            ["OperationalContinuityStartUtc"] = input.Stock.OperationalContinuityStartUtc.ToString("O"),
            ["FrozenRulerSourceThrough"] = input.VolatilityRulers.Select(input.Policy).SourceThroughSession?.ToString("yyyy-MM-dd") ?? "Unavailable",
            ["SelectedRulerSessions"] = input.Policy.SelectedRulerSessions.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SelectedRawMoveThreshold"] = input.Policy.SelectedRawMoveThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SelectedExcessMoveThreshold"] = input.Policy.SelectedExcessMoveThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SessionThesis"] = input.DailySetup is not null ? "FrozenDailyCandidate" :
                input.IsHeld ? "HeldNotReselected" : input.IsSessionCarryCandidate ? "SessionCarryCandidate" : "ObservedHolding"
        };
        foreach (DelphiLiveFamilyJudgment family in families)
        {
            facts[family.Family + "CurrentState"] = family.State.ToString();
            facts[family.Family + "CurrentReason"] = family.ReasonCode;
        }
        return facts.ToImmutableDictionary(StringComparer.Ordinal);
    }

    private static DelphiLiveFiveMinuteBar? CurrentOnTime(DelphiLiveFiveMinuteSeries series, DateTime end) =>
        series.Bars.SingleOrDefault(bar => bar.EndUtc == end &&
            bar.Disposition == DelphiLiveEvidenceDisposition.OperationalOnTime);

    private static DelphiLiveFiveMinuteSeries KnownSeries(DelphiLiveFiveMinuteSeries series, DateTime asOf) =>
        new(series.Symbol, series.SessionDate, series.SessionOpenUtc, series.OperationalContinuityStartUtc,
            series.Bars.Where(bar => bar.ReceivedUtc <= asOf));

    private static void Validate(DelphiLiveEvaluationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Stock);
        ArgumentNullException.ThrowIfNull(input.Xiu);
        ArgumentNullException.ThrowIfNull(input.VolatilityRulers);
        ArgumentNullException.ThrowIfNull(input.PreviousState);
        input.Policy.Validate();
        DelphiLiveFiveMinuteBar.RequireUtc(input.BarEndUtc, nameof(input.BarEndUtc));
        DelphiLiveFiveMinuteBar.RequireUtc(input.EvaluatedUtc, nameof(input.EvaluatedUtc));
        if (input.EvaluationId == Guid.Empty || input.SessionId == Guid.Empty ||
            input.EvaluatedUtc <= input.BarEndUtc || input.Xiu.Symbol != "XIU" || input.Stock.Symbol == "XIU")
            throw new ArgumentException("An evaluation needs identities, a completed stock checkpoint, and exact XIU evidence.");
        if (input.DailySetup is not null && input.IsSessionCarryCandidate)
            throw new ArgumentException("A carry candidate must not reuse a current daily ranking.");
        if (input.PreviousState.Lifecycle.LastScheduledBarEndUtc is DateTime previous && input.BarEndUtc <= previous)
            throw new ArgumentException("A canonical evaluation must advance to a new scheduled checkpoint.");
        foreach (DelphiLiveTrueRangeRulerMeasurement ruler in new[]
                 { input.VolatilityRulers.FiveSession, input.VolatilityRulers.TenSession,
                   input.VolatilityRulers.FourteenSession, input.VolatilityRulers.TwentySession })
        {
            if (ruler.MedianTrueRangePct.Availability == DelphiLiveMeasurementAvailability.Available &&
                (ruler.SourceThroughSession is not DateOnly source || source >= input.Stock.SessionDate))
                throw new ArgumentException("A frozen ruler must retain a completed earlier source-through session.");
        }
    }
}
