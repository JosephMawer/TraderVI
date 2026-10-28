#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;

namespace Core.Trader.DelphiLive;

/// <summary>Explicit prospective definition; never assign this identity to a saved V1 policy.</summary>
public sealed record DelphiLiveMinuteStrategy(DelphiLivePolicyDefinition Rules, string SourceContract, TimeSpan DeadlineOffset)
{
    public const string Evaluator = "DelphiLiveMinuteRecoveredV1";
    [System.Text.Json.Serialization.JsonIgnore]
    public string Identity => Evaluator + ":" + DelphiLiveCollectorBatches.Hash(JsonSerializer.Serialize(this));
    public void Validate()
    {
        Rules.Validate();
        if (string.IsNullOrWhiteSpace(SourceContract) || SourceContract != SourceContract.Trim() || SourceContract.Length > 128 ||
            DeadlineOffset <= TimeSpan.Zero || DeadlineOffset >= TimeSpan.FromMinutes(1) || Rules.EntryConfirmationCount != 2 ||
            Rules.BarInterval != TimeSpan.FromMinutes(5) || Rules.PersistenceObservationCount != 4 || Rules.DirectionalVolumeObservationCount != 4 ||
            Rules.PriorRangeObservationCount != 4 || Rules.ImmediateMovementHorizon != TimeSpan.FromMinutes(20) ||
            Rules.SustainedMovementHorizon != TimeSpan.FromMinutes(60) || Rules.TwoHourContextHorizon != TimeSpan.FromMinutes(120) ||
            Rules.ThreeHourContextHorizon != TimeSpan.FromMinutes(180))
            throw new ArgumentException("Minute successor must preserve the accepted rule horizons and two-observation confirmation.");
    }
}

public sealed record DelphiLiveMinuteEvaluation(DelphiLiveRecoveredJudgment Judgment,
    DelphiLiveMinuteMeasurementsResult Measurements, ImmutableArray<DelphiLiveFamilyJudgment> Families,
    DelphiLiveMomentumJudgment Momentum, DelphiLiveSafetyEvaluation Safety);

public sealed record DelphiLiveMinutePair(DelphiLiveMinuteEvaluation Previous, DelphiLiveMinuteEvaluation Latest,
    DelphiLiveRecoveredConfirmationResult Confirmation);

/// <summary>
/// Evaluates both actual four-family judgments from durable local evidence at the present decision time.
/// No process lifetime, Paper lifecycle, financial state, network or order authority is imported.
/// </summary>
public static class DelphiLiveMinuteEvaluator
{
    public static DelphiLiveMinutePair EvaluatePair(DelphiLiveMinuteStrategy strategy, DelphiLiveMinuteEvidenceBook stock,
        DelphiLiveMinuteEvidenceBook xiu, DelphiLiveVolatilityRulerMeasurements rulers, decimal? previousStockClose,
        decimal? previousXiuClose, DateTime latestEndUtc, DateTime nowUtc)
    {
        strategy.Validate();
        if (latestEndUtc < stock.SessionOpenUtc.AddMinutes(2)) throw new ArgumentException("Two completed endpoints are required.");
        var previous = Evaluate(strategy, stock, xiu, rulers, previousStockClose, previousXiuClose, latestEndUtc.AddMinutes(-1), nowUtc);
        var latest = Evaluate(strategy, stock, xiu, rulers, previousStockClose, previousXiuClose, latestEndUtc, nowUtc);
        return new(previous, latest, DelphiLiveRecoveredConfirmation.Assess(strategy.Identity, stock, xiu, previous.Judgment, latest.Judgment, nowUtc));
    }

    public static DelphiLiveMinuteEvaluation Evaluate(DelphiLiveMinuteStrategy strategy, DelphiLiveMinuteEvidenceBook stock,
        DelphiLiveMinuteEvidenceBook xiu, DelphiLiveVolatilityRulerMeasurements rulers, decimal? previousStockClose,
        decimal? previousXiuClose, DateTime endUtc, DateTime nowUtc)
    {
        strategy.Validate();
        if (stock.SourceContract != strategy.SourceContract || xiu.SourceContract != strategy.SourceContract ||
            stock.Observations.Concat(xiu.Observations).Any(o => o.DeadlineUtc - o.EndUtc != strategy.DeadlineOffset))
            throw new ArgumentException("Evidence does not match the exact reviewed source/deadline contract.");
        var rules = strategy.Rules;
        var facts = DelphiLiveMinuteMeasurements.Calculate(stock, xiu, endUtc, nowUtc, previousStockClose, previousXiuClose, rules);
        var persistence = DelphiLiveFamilyClassifiers.ClassifyPersistence(facts.Persistence);
        var price = DelphiLiveFamilyClassifiers.ClassifyPriceMovement(facts.Price, rulers.Select(rules), rules);
        var volume = DelphiLiveFamilyClassifiers.ClassifyVolumeSupport(facts.Volume, rules);
        var structure = DelphiLiveFamilyClassifiers.ClassifyPriceStructure(facts.Structure, rulers.Select(rules), rules);
        ImmutableArray<DelphiLiveFamilyJudgment> families = [persistence.Family, price.Family, volume.Family, structure.Family];
        var momentum = DelphiLiveFamilyCombiner.Combine(families);
        bool valid = stock.ConflictingStartsUtc.IsEmpty && xiu.ConflictingStartsUtc.IsEmpty &&
            new[] { stock, xiu }.All(b => b.Observations.Any(o => o.EndUtc == endUtc && o.CheckedUtc <= nowUtc));
        bool mature = valid && families.All(f => f.State is not (DelphiLiveFamilyState.NotMature or DelphiLiveFamilyState.Unavailable));
        var current = stock.Observations.SingleOrDefault(o => o.EndUtc == endUtc && o.CheckedUtc <= nowUtc);
        var safety = DelphiLiveSafetyPolicy.Evaluate(new(false, !mature, null, null, null,
            current?.Bar.Open, current?.Bar.Close, structure.SessionVwap.IsAvailable,
            structure.SessionVwap.State == DelphiLiveStructureReferenceState.Below, structure.PriorTwentyMinuteRange.IsAvailable,
            structure.PriorTwentyMinuteRange.State == DelphiLiveStructureReferenceState.Breakdown, volume.Family, momentum, false, null), rules);
        ImmutableArray<Guid> Ids(DelphiLiveMinuteEvidenceBook book) => book.Observations
            .Where(o => o.EndUtc <= endUtc && o.CheckedUtc <= nowUtc).Select(o => o.ObservationId).ToImmutableArray();
        var judgment = new DelphiLiveRecoveredJudgment(strategy.Identity, strategy.SourceContract, stock.Symbol, stock.SessionOpenUtc,
            endUtc, nowUtc, valid, mature, valid, safety.EntrySafetyVetoActive, momentum.SupportiveVotes, Ids(stock), Ids(xiu));
        return new(judgment, facts, families, momentum, safety);
    }
}
