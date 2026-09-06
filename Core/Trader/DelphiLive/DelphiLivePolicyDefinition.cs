#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Core.Trader.DelphiLive;

public static class DelphiLiveIdentities
{
    public static readonly Guid PolicyVersionId =
        Guid.Parse("C15C1A27-13A1-581A-8912-06C92941A01E");

    public const string PolicyDefinition = "DelphiLivePolicyV1";
    public const int PolicyDefinitionSchemaVersion = 1;
    public const string Evaluator = "DelphiLiveEvaluatorV1";
    public const string Collector = "IntradayEvidenceCollectorV3";
    public const int CollectorSourceContractVersion = 1;
    public const string DecisionDossier = "DelphiLiveDecisionDossierV1";
    public const int DecisionDossierSchemaVersion = 1;
    public const string QuoteFill = "DelphiLiveQuoteFillV1";
    public const string ShadowPortfolio = "DelphiLiveShadowPortfolioV1";
    public const string ResearchOutcome = "LiveObservationOutcomeV1";
    public const string RankingDiagnostic = "DelphiLiveDailyVsLiveTop5V1";
    public const string PromotionProtocol = "DelphiLivePromotionV1";
}

public readonly record struct DelphiLiveThresholdComparisonSet(
    decimal Lower,
    decimal Operational,
    decimal Upper);

public readonly record struct DelphiLiveVolatilityRulerPolicy(
    int DiagnosticShortSessions,
    int OperationalSessions,
    int ChallengerSessions,
    int DiagnosticLongSessions);

public enum DelphiLiveExitRule
{
    HardLoss5Pct,
    FastDownside10Pct,
    ProfitProtectionFloorBreach,
    ConfirmedSupportFailure,
    LiveWeakeningExit
}

/// <summary>
/// Immutable settings interpreted by <see cref="DelphiLiveIdentities.Evaluator"/>.
/// Call <see cref="Validate"/> when a definition is loaded and freeze the validated
/// instance for the entire regular session.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DelphiLivePolicyDefinition
{
    public required Guid PolicyVersionId { get; init; }
    public required string PolicyDefinitionName { get; init; }
    public required int PolicyDefinitionSchemaVersion { get; init; }
    public required string EvaluatorVersion { get; init; }
    public required string CollectorVersion { get; init; }
    public required int CollectorSourceContractVersion { get; init; }
    public required string DecisionDossierVersion { get; init; }
    public required int DecisionDossierSchemaVersion { get; init; }
    public required string QuoteFillVersion { get; init; }
    public required string ShadowPortfolioVersion { get; init; }
    public required string ResearchOutcomeVersion { get; init; }
    public required string RankingDiagnosticVersion { get; init; }
    public required string PromotionProtocolVersion { get; init; }

    public required string MarketTimeZone { get; init; }
    public required TimeSpan BarInterval { get; init; }
    public required TimeSpan CollectionOffset { get; init; }
    public required int PersistenceObservationCount { get; init; }
    public required TimeSpan ImmediateMovementHorizon { get; init; }
    public required TimeSpan SustainedMovementHorizon { get; init; }
    public required TimeSpan TwoHourContextHorizon { get; init; }
    public required TimeSpan ThreeHourContextHorizon { get; init; }
    public required int DirectionalVolumeObservationCount { get; init; }
    public required int PriorRangeObservationCount { get; init; }
    public required int MinimumStructureReferences { get; init; }
    public required DelphiLiveVolatilityRulerPolicy VolatilityRulers { get; init; }
    public required DelphiLiveThresholdComparisonSet RawMoveThresholds { get; init; }
    public required DelphiLiveThresholdComparisonSet ExcessMoveThresholds { get; init; }
    public required decimal SelectedRawMoveThreshold { get; init; }
    public required decimal SelectedExcessMoveThreshold { get; init; }
    public required int SelectedRulerSessions { get; init; }
    public required decimal DirectionalVolumeThreshold { get; init; }
    public required decimal StructureBufferUnits { get; init; }
    public required int FullDayVolumeMedianSessionCount { get; init; }

    public required int EntryConfirmationCount { get; init; }
    public required int WeakeningConfirmationCount { get; init; }
    public required decimal HardLossFraction { get; init; }
    public required decimal FastDownsideReturnFloor { get; init; }
    public required decimal ProfitFloorActivationGainFraction { get; init; }
    public required decimal TrailingActivationGainFraction { get; init; }
    public required decimal TrailingDistanceFraction { get; init; }
    public required int MaximumHoldings { get; init; }
    public required decimal EntryTargetNavFraction { get; init; }
    public required int MaximumSameSessionEntriesPerSymbol { get; init; }
    public required decimal DailyLossGuardFraction { get; init; }
    public required decimal CapitalReviewDrawdownFraction { get; init; }
    public required int QuoteAttemptCount { get; init; }
    public required TimeSpan QuoteAttemptWindow { get; init; }
    public required TimeOnly EntryWindowStart { get; init; }
    public required TimeOnly EntryCutoff { get; init; }
    public required ImmutableArray<DelphiLiveExitRule> PrimaryExitReasonOrder { get; init; }

    public required ImmutableArray<decimal> OpportunityThresholds { get; init; }
    public required ImmutableArray<int> ResearchSessionHorizons { get; init; }
    public required int EngineeringShakedownSessionCount { get; init; }
    public required int DiscoverySessionCount { get; init; }
    public required int UntouchedConfirmationSessionCount { get; init; }
    public required int PromotionBootstrapResampleCount { get; init; }
    public required int PromotionBootstrapBlockSessionCount { get; init; }
    public required decimal PromotionConfidenceLevel { get; init; }
    public required decimal DegradedCoverageFloor { get; init; }
    public required decimal ReadyCoverage { get; init; }
    public required int MaximumActiveNonChampionPolicies { get; init; }

    public static DelphiLivePolicyDefinition Version1 { get; } = CreateVersion1();

    public DelphiLivePolicyDefinition Validate()
    {
        DelphiLivePolicyValidator.Validate(this);
        return this;
    }

    private static DelphiLivePolicyDefinition CreateVersion1()
    {
        var definition = new DelphiLivePolicyDefinition
        {
            PolicyVersionId = DelphiLiveIdentities.PolicyVersionId,
            PolicyDefinitionName = DelphiLiveIdentities.PolicyDefinition,
            PolicyDefinitionSchemaVersion = DelphiLiveIdentities.PolicyDefinitionSchemaVersion,
            EvaluatorVersion = DelphiLiveIdentities.Evaluator,
            CollectorVersion = DelphiLiveIdentities.Collector,
            CollectorSourceContractVersion = DelphiLiveIdentities.CollectorSourceContractVersion,
            DecisionDossierVersion = DelphiLiveIdentities.DecisionDossier,
            DecisionDossierSchemaVersion = DelphiLiveIdentities.DecisionDossierSchemaVersion,
            QuoteFillVersion = DelphiLiveIdentities.QuoteFill,
            ShadowPortfolioVersion = DelphiLiveIdentities.ShadowPortfolio,
            ResearchOutcomeVersion = DelphiLiveIdentities.ResearchOutcome,
            RankingDiagnosticVersion = DelphiLiveIdentities.RankingDiagnostic,
            PromotionProtocolVersion = DelphiLiveIdentities.PromotionProtocol,

            MarketTimeZone = "America/Toronto",
            BarInterval = TimeSpan.FromMinutes(5),
            CollectionOffset = TimeSpan.FromMinutes(2),
            PersistenceObservationCount = 4,
            ImmediateMovementHorizon = TimeSpan.FromMinutes(20),
            SustainedMovementHorizon = TimeSpan.FromHours(1),
            TwoHourContextHorizon = TimeSpan.FromHours(2),
            ThreeHourContextHorizon = TimeSpan.FromHours(3),
            DirectionalVolumeObservationCount = 4,
            PriorRangeObservationCount = 4,
            MinimumStructureReferences = 2,
            VolatilityRulers = new(5, 10, 14, 20),
            RawMoveThresholds = new(0.15m, 0.25m, 0.35m),
            ExcessMoveThresholds = new(0.025m, 0.05m, 0.10m),
            SelectedRawMoveThreshold = 0.25m,
            SelectedExcessMoveThreshold = 0.05m,
            SelectedRulerSessions = 10,
            DirectionalVolumeThreshold = 0.10m,
            StructureBufferUnits = 0.05m,
            FullDayVolumeMedianSessionCount = 20,

            EntryConfirmationCount = 2,
            WeakeningConfirmationCount = 2,
            HardLossFraction = 0.05m,
            FastDownsideReturnFloor = -0.10m,
            ProfitFloorActivationGainFraction = 0.03m,
            TrailingActivationGainFraction = 0.05m,
            TrailingDistanceFraction = 0.02m,
            MaximumHoldings = 5,
            EntryTargetNavFraction = 0.20m,
            MaximumSameSessionEntriesPerSymbol = 2,
            DailyLossGuardFraction = 0.03m,
            CapitalReviewDrawdownFraction = 0.10m,
            QuoteAttemptCount = 3,
            QuoteAttemptWindow = TimeSpan.FromSeconds(60),
            EntryWindowStart = new TimeOnly(9, 50),
            EntryCutoff = new TimeOnly(15, 45),
            PrimaryExitReasonOrder = ImmutableArray.Create(
                DelphiLiveExitRule.HardLoss5Pct,
                DelphiLiveExitRule.FastDownside10Pct,
                DelphiLiveExitRule.ProfitProtectionFloorBreach,
                DelphiLiveExitRule.ConfirmedSupportFailure,
                DelphiLiveExitRule.LiveWeakeningExit),

            OpportunityThresholds = ImmutableArray.Create(
                0.01m,
                0.02m,
                0.03m,
                0.05m,
                0.10m,
                0.15m),
            ResearchSessionHorizons = ImmutableArray.Create(1, 3, 5),
            EngineeringShakedownSessionCount = 10,
            DiscoverySessionCount = 30,
            UntouchedConfirmationSessionCount = 30,
            PromotionBootstrapResampleCount = 10_000,
            PromotionBootstrapBlockSessionCount = 5,
            PromotionConfidenceLevel = 0.95m,
            DegradedCoverageFloor = 0.95m,
            ReadyCoverage = 1m,
            MaximumActiveNonChampionPolicies = 2
        };

        return definition.Validate();
    }
}

public sealed class DelphiLivePolicyValidationException : ArgumentException
{
    public DelphiLivePolicyValidationException(string message)
        : base(message, "definition")
    {
    }
}

public static class DelphiLivePolicyValidator
{
    private static readonly ImmutableArray<DelphiLiveExitRule> ExpectedExitOrder =
        ImmutableArray.Create(
            DelphiLiveExitRule.HardLoss5Pct,
            DelphiLiveExitRule.FastDownside10Pct,
            DelphiLiveExitRule.ProfitProtectionFloorBreach,
            DelphiLiveExitRule.ConfirmedSupportFailure,
            DelphiLiveExitRule.LiveWeakeningExit);

    public static void Validate(DelphiLivePolicyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        Require(definition.PolicyVersionId != Guid.Empty, "Policy version identity is required.");
        RequireExact(definition.PolicyDefinitionName, DelphiLiveIdentities.PolicyDefinition, "policy definition");
        Require(
            definition.PolicyDefinitionSchemaVersion == DelphiLiveIdentities.PolicyDefinitionSchemaVersion,
            "Unsupported policy definition schema version.");
        RequireExact(definition.EvaluatorVersion, DelphiLiveIdentities.Evaluator, "evaluator");
        RequireExact(definition.CollectorVersion, DelphiLiveIdentities.Collector, "collector");
        Require(
            definition.CollectorSourceContractVersion == DelphiLiveIdentities.CollectorSourceContractVersion,
            "Unsupported collector source-contract version.");
        RequireExact(definition.DecisionDossierVersion, DelphiLiveIdentities.DecisionDossier, "decision dossier");
        Require(
            definition.DecisionDossierSchemaVersion == DelphiLiveIdentities.DecisionDossierSchemaVersion,
            "Unsupported decision-dossier schema version.");
        RequireExact(definition.QuoteFillVersion, DelphiLiveIdentities.QuoteFill, "quote-fill contract");
        RequireExact(definition.ShadowPortfolioVersion, DelphiLiveIdentities.ShadowPortfolio, "Shadow portfolio");
        RequireExact(definition.ResearchOutcomeVersion, DelphiLiveIdentities.ResearchOutcome, "research outcome");
        RequireExact(definition.RankingDiagnosticVersion, DelphiLiveIdentities.RankingDiagnostic, "ranking diagnostic");
        RequireExact(definition.PromotionProtocolVersion, DelphiLiveIdentities.PromotionProtocol, "promotion protocol");
        RequireExact(definition.MarketTimeZone, "America/Toronto", "market time zone");

        // These values determine V1 state categories and endpoint contracts in
        // code. A stored setting cannot silently ask this evaluator to use a
        // different number of votes, confirmations, or research horizons.
        Require(definition.BarInterval == TimeSpan.FromMinutes(5) &&
            definition.PersistenceObservationCount == 4 &&
            definition.DirectionalVolumeObservationCount == 4 &&
            definition.PriorRangeObservationCount == 4 &&
            definition.EntryConfirmationCount == 2 && definition.WeakeningConfirmationCount == 2,
            "Unsupported V1 bar, rolling-observation, or confirmation counts.");
        Require(definition.ImmediateMovementHorizon == TimeSpan.FromMinutes(20) &&
            definition.SustainedMovementHorizon == TimeSpan.FromHours(1) &&
            definition.TwoHourContextHorizon == TimeSpan.FromHours(2) &&
            definition.ThreeHourContextHorizon == TimeSpan.FromHours(3),
            "Unsupported V1 price-movement horizons.");
        Require(definition.VolatilityRulers == new DelphiLiveVolatilityRulerPolicy(5, 10, 14, 20),
            "Unsupported V1 volatility-ruler definitions.");
        Require(definition.RawMoveThresholds == new DelphiLiveThresholdComparisonSet(0.15m, 0.25m, 0.35m) &&
            definition.ExcessMoveThresholds == new DelphiLiveThresholdComparisonSet(0.025m, 0.05m, 0.10m),
            "V1 threshold comparison sets are predeclared and immutable.");
        Require(definition.SelectedRawMoveThreshold is 0.15m or 0.25m or 0.35m &&
            definition.SelectedExcessMoveThreshold is 0.025m or 0.05m or 0.10m &&
            definition.SelectedRulerSessions is 10 or 14,
            "An assigned policy may select only a predeclared threshold and the ten- or fourteen-session ruler.");
        Require(definition.ResearchSessionHorizons.AsSpan().SequenceEqual(new[] { 1, 3, 5 }),
            "Unsupported V1 research session horizons.");

        Require(definition.BarInterval > TimeSpan.Zero, "Bar interval must be positive.");
        Require(definition.CollectionOffset > TimeSpan.Zero, "Collection offset must be positive.");
        Require(
            definition.CollectionOffset < definition.BarInterval,
            "Collection offset must be shorter than the bar interval.");
        Require(definition.PersistenceObservationCount > 0, "Persistence observation count must be positive.");
        Require(
            definition.ImmediateMovementHorizon ==
            Multiply(definition.BarInterval, definition.PersistenceObservationCount),
            "Persistence count and immediate movement horizon contradict each other.");
        Require(
            definition.DirectionalVolumeObservationCount == definition.PersistenceObservationCount,
            "Directional Volume and Persistence must use the same rolling observation count in V1.");
        Require(
            definition.PriorRangeObservationCount == definition.PersistenceObservationCount,
            "Prior range and Persistence must use the same rolling observation count in V1.");
        Require(
            definition.SustainedMovementHorizon > definition.ImmediateMovementHorizon &&
            definition.TwoHourContextHorizon > definition.SustainedMovementHorizon &&
            definition.ThreeHourContextHorizon > definition.TwoHourContextHorizon,
            "Price Movement horizons must be strictly increasing.");
        RequireAligned(definition.SustainedMovementHorizon, definition.BarInterval, "sustained movement horizon");
        RequireAligned(definition.TwoHourContextHorizon, definition.BarInterval, "two-hour context horizon");
        RequireAligned(definition.ThreeHourContextHorizon, definition.BarInterval, "three-hour context horizon");
        Require(
            definition.MinimumStructureReferences is >= 2 and <= 3,
            "Price Structure requires two or three available references.");

        ValidateRulers(definition.VolatilityRulers);
        ValidateThresholds(definition.RawMoveThresholds, "raw-move");
        ValidateThresholds(definition.ExcessMoveThresholds, "excess-move");
        RequireFraction(definition.DirectionalVolumeThreshold, "Directional Volume threshold");
        RequireFraction(definition.StructureBufferUnits, "Price Structure buffer");
        Require(
            definition.FullDayVolumeMedianSessionCount > 0,
            "Full-day volume median session count must be positive.");

        Require(definition.EntryConfirmationCount > 0, "Entry confirmation count must be positive.");
        Require(definition.WeakeningConfirmationCount > 0, "Weakening confirmation count must be positive.");
        RequireFraction(definition.HardLossFraction, "hard-loss fraction");
        Require(
            definition.FastDownsideReturnFloor > -1m && definition.FastDownsideReturnFloor < 0m,
            "Fast-downside return floor must be between -1 and zero.");
        RequireFraction(definition.ProfitFloorActivationGainFraction, "profit-floor activation gain");
        RequireFraction(definition.TrailingActivationGainFraction, "trailing activation gain");
        RequireFraction(definition.TrailingDistanceFraction, "trailing distance");
        Require(
            definition.ProfitFloorActivationGainFraction < definition.TrailingActivationGainFraction,
            "Break-even activation must precede trailing activation.");
        Require(definition.MaximumHoldings > 0, "Maximum holdings must be positive.");
        RequireFraction(definition.EntryTargetNavFraction, "entry NAV target");
        Require(
            definition.MaximumHoldings * definition.EntryTargetNavFraction <= 1m,
            "Entry targets cannot require more than the portfolio NAV.");
        Require(
            definition.MaximumSameSessionEntriesPerSymbol > 0,
            "Same-session entry limit must be positive.");
        RequireFraction(definition.DailyLossGuardFraction, "daily loss guard");
        RequireFraction(definition.CapitalReviewDrawdownFraction, "capital-review drawdown");
        Require(definition.QuoteAttemptCount > 0, "Quote attempt count must be positive.");
        Require(definition.QuoteAttemptWindow > TimeSpan.Zero, "Quote attempt window must be positive.");
        Require(definition.EntryWindowStart < definition.EntryCutoff, "Entry window must end after it starts.");
        RequireSequence(definition.PrimaryExitReasonOrder, ExpectedExitOrder, "primary exit-reason order");

        RequireStrictlyIncreasingFractions(definition.OpportunityThresholds, "opportunity thresholds");
        RequireStrictlyIncreasingPositive(definition.ResearchSessionHorizons, "research session horizons");
        Require(definition.EngineeringShakedownSessionCount > 0, "Engineering shakedown count must be positive.");
        Require(definition.DiscoverySessionCount > 0, "Discovery count must be positive.");
        Require(
            definition.UntouchedConfirmationSessionCount > 0,
            "Untouched confirmation count must be positive.");
        Require(definition.PromotionBootstrapResampleCount > 0, "Bootstrap resample count must be positive.");
        Require(
            definition.PromotionBootstrapBlockSessionCount > 0,
            "Bootstrap block size must be positive.");
        RequireFraction(definition.PromotionConfidenceLevel, "promotion confidence level");
        Require(
            definition.DegradedCoverageFloor > 0m &&
            definition.DegradedCoverageFloor < definition.ReadyCoverage &&
            definition.ReadyCoverage == 1m,
            "Coverage thresholds must be 0 < degraded < ready = 1.");
        Require(
            definition.MaximumActiveNonChampionPolicies == 2,
            "V1 permits exactly two active non-champion policy slots.");
    }

    private static void ValidateRulers(DelphiLiveVolatilityRulerPolicy rulers)
    {
        Require(
            rulers.DiagnosticShortSessions > 0 &&
            rulers.DiagnosticShortSessions < rulers.OperationalSessions &&
            rulers.OperationalSessions < rulers.ChallengerSessions &&
            rulers.ChallengerSessions < rulers.DiagnosticLongSessions,
            "Volatility ruler session counts must be positive and strictly increasing.");
    }

    private static void ValidateThresholds(
        DelphiLiveThresholdComparisonSet thresholds,
        string name)
    {
        Require(
            thresholds.Lower > 0m &&
            thresholds.Lower < thresholds.Operational &&
            thresholds.Operational < thresholds.Upper &&
            thresholds.Upper < 1m,
            $"The {name} comparison set must be positive, below one, and strictly increasing.");
    }

    private static void RequireFraction(decimal value, string name) =>
        Require(value > 0m && value < 1m, $"The {name} must be between zero and one.");

    private static void RequireStrictlyIncreasingFractions(
        ImmutableArray<decimal> values,
        string name)
    {
        Require(!values.IsDefaultOrEmpty, $"The {name} are required.");
        decimal prior = 0m;
        foreach (decimal value in values)
        {
            Require(value > prior && value < 1m, $"The {name} must be strictly increasing fractions.");
            prior = value;
        }
    }

    private static void RequireStrictlyIncreasingPositive(
        ImmutableArray<int> values,
        string name)
    {
        Require(!values.IsDefaultOrEmpty, $"The {name} are required.");
        int prior = 0;
        foreach (int value in values)
        {
            Require(value > prior, $"The {name} must be strictly increasing positive values.");
            prior = value;
        }
    }

    private static void RequireSequence<T>(
        ImmutableArray<T> actual,
        ImmutableArray<T> expected,
        string name)
        where T : struct, Enum
    {
        Require(!actual.IsDefault && actual.Length == expected.Length, $"The {name} is incomplete.");
        for (int index = 0; index < expected.Length; index++)
            Require(EqualityComparer<T>.Default.Equals(actual[index], expected[index]), $"The {name} is unsupported.");
    }

    private static void RequireAligned(TimeSpan horizon, TimeSpan interval, string name) =>
        Require(
            horizon > TimeSpan.Zero && horizon.Ticks % interval.Ticks == 0,
            $"The {name} must be a positive whole number of bars.");

    private static TimeSpan Multiply(TimeSpan value, int multiplier) =>
        TimeSpan.FromTicks(checked(value.Ticks * multiplier));

    private static void RequireExact(string? actual, string expected, string name) =>
        Require(string.Equals(actual, expected, StringComparison.Ordinal), $"Unsupported {name} identity.");

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new DelphiLivePolicyValidationException(message);
    }
}
