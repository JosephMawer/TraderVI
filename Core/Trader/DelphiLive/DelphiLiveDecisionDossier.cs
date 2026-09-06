#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Core.Trader.DelphiLive;

public sealed record DelphiLiveDossierLensAttribution(
    Guid CalibrationLensEvaluationId,
    string Lens,
    int Rank,
    decimal RankingKey,
    bool Eligible,
    bool Published,
    string? FirstFailure,
    string GateTraceJson);

public sealed record DelphiLiveOriginalEntryThesis(
    Guid EntryDecisionId,
    Guid CalibrationRunId,
    Guid CalibrationCandidateId,
    Guid DailyStrategyVersionId,
    IReadOnlyList<DelphiLiveDossierLensAttribution> SourceLenses);

public sealed record DelphiLiveDecisionDossier(
    int SchemaVersion,
    string DossierVersion,
    Guid DecisionId,
    Guid EvaluationId,
    DateTime DecisionUtc,
    Guid DelphiLiveSessionId,
    Guid? CalibrationRunId,
    Guid? CalibrationCandidateId,
    Guid? DailyStrategyVersionId,
    Guid DelphiLivePolicyVersionId,
    string PolicyDefinitionName,
    string EvaluatorVersion,
    string CollectorVersion,
    string QuoteFillVersion,
    string Symbol,
    DateTime? BarEndUtc,
    IReadOnlyList<DelphiLiveDossierLensAttribution> SourceLenses,
    IReadOnlyList<Guid> EvidenceBarIds,
    IReadOnlyDictionary<string, decimal?> RawValues,
    IReadOnlyDictionary<string, string> DerivedFacts,
    IReadOnlyList<DelphiLiveFamilyJudgment> FamilyJudgments,
    DelphiLiveMomentumJudgment Momentum,
    DelphiLiveDataConfidence ConfidenceBefore,
    DelphiLiveDataConfidence ConfidenceAfter,
    DelphiLiveRecommendationState RecommendationBefore,
    DelphiLiveRecommendationState RecommendationAfter,
    IReadOnlyList<DelphiLiveExitRule> FiredExitRules,
    DelphiLiveExitRule? PrimaryExitRule,
    IReadOnlyList<string> ReasonCodes,
    string RequestedAction,
    string ActionState)
{
    // The original entry thesis is immutable position provenance, never today's
    // rank. Carried holdings may have no current run or source-lens attribution.
    public DelphiLiveOriginalEntryThesis? OriginalEntryThesis { get; init; }
    public IReadOnlyList<Guid> EvidenceQuoteIds { get; init; } = Array.Empty<Guid>();
}

public static class DelphiLiveDecisionDossierBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static DelphiLiveDecisionDossier Validate(
        DelphiLiveDecisionDossier dossier,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(dossier);
        ArgumentNullException.ThrowIfNull(policy);
        DelphiLivePolicyValidator.Validate(policy);
        if (dossier.SchemaVersion != DelphiLiveIdentities.DecisionDossierSchemaVersion ||
            dossier.DossierVersion != DelphiLiveIdentities.DecisionDossier)
            throw new ArgumentException("Unsupported Delphi Live decision-dossier identity.", nameof(dossier));
        if (dossier.DecisionId == Guid.Empty || dossier.EvaluationId == Guid.Empty ||
            dossier.DelphiLiveSessionId == Guid.Empty ||
            dossier.DelphiLivePolicyVersionId == Guid.Empty)
            throw new ArgumentException("Every dossier identity is required.", nameof(dossier));
        if (dossier.DelphiLivePolicyVersionId != policy.PolicyVersionId ||
            dossier.PolicyDefinitionName != policy.PolicyDefinitionName ||
            dossier.EvaluatorVersion != policy.EvaluatorVersion ||
            dossier.CollectorVersion != policy.CollectorVersion ||
            dossier.QuoteFillVersion != policy.QuoteFillVersion)
            throw new ArgumentException("Dossier and policy identities do not match.", nameof(dossier));
        RequireUtc(dossier.DecisionUtc, nameof(dossier.DecisionUtc));
        if (dossier.BarEndUtc is DateTime barEnd)
            RequireUtc(barEnd, nameof(dossier.BarEndUtc));
        if (dossier.BarEndUtc is DateTime completedEnd && dossier.DecisionUtc < completedEnd)
            throw new ArgumentException("A decision cannot precede its completed evidence bar.", nameof(dossier));
        if (string.IsNullOrWhiteSpace(dossier.Symbol) || dossier.Symbol.Length > 20 ||
            string.IsNullOrWhiteSpace(dossier.RequestedAction) ||
            string.IsNullOrWhiteSpace(dossier.ActionState))
            throw new ArgumentException("Dossier symbol and action state are required.", nameof(dossier));
        ArgumentNullException.ThrowIfNull(dossier.SourceLenses);
        ArgumentNullException.ThrowIfNull(dossier.EvidenceBarIds);
        ArgumentNullException.ThrowIfNull(dossier.EvidenceQuoteIds);
        ArgumentNullException.ThrowIfNull(dossier.RawValues);
        ArgumentNullException.ThrowIfNull(dossier.DerivedFacts);
        ArgumentNullException.ThrowIfNull(dossier.FamilyJudgments);
        ArgumentNullException.ThrowIfNull(dossier.Momentum);
        ArgumentNullException.ThrowIfNull(dossier.ConfidenceBefore);
        ArgumentNullException.ThrowIfNull(dossier.ConfidenceAfter);
        ArgumentNullException.ThrowIfNull(dossier.FiredExitRules);
        ArgumentNullException.ThrowIfNull(dossier.ReasonCodes);
        bool hasCurrentThesis = dossier.CalibrationRunId.HasValue ||
            dossier.CalibrationCandidateId.HasValue || dossier.DailyStrategyVersionId.HasValue ||
            dossier.SourceLenses.Count > 0;
        if (hasCurrentThesis)
        {
            ValidateThesis(dossier.CalibrationRunId, dossier.CalibrationCandidateId,
                dossier.DailyStrategyVersionId, dossier.SourceLenses);
        }
        if (dossier.OriginalEntryThesis is { } original)
        {
            if (original.EntryDecisionId == Guid.Empty)
                throw new ArgumentException("Original entry-decision identity is required.", nameof(dossier));
            ValidateThesis(original.CalibrationRunId, original.CalibrationCandidateId,
                original.DailyStrategyVersionId, original.SourceLenses);
        }
        if (!hasCurrentThesis && dossier.OriginalEntryThesis is null)
            throw new ArgumentException("A consequential decision requires current or original daily thesis provenance.", nameof(dossier));
        Guid[] evidence = dossier.EvidenceBarIds.Concat(dossier.EvidenceQuoteIds).ToArray();
        if (evidence.Length == 0 || evidence.Any(id => id == Guid.Empty) ||
            evidence.Distinct().Count() != evidence.Length)
            throw new ArgumentException("Dossier evidence identities must be nonempty and distinct.", nameof(dossier));
        if (dossier.EvidenceBarIds.Count > 0 && !dossier.BarEndUtc.HasValue)
            throw new ArgumentException("Bar evidence requires its completed checkpoint.", nameof(dossier));
        if (dossier.FamilyJudgments.Count != 4 ||
            dossier.FamilyJudgments.Select(x => x.Family).Distinct().Count() != 4)
            throw new ArgumentException("Dossier requires one judgment for every live family.", nameof(dossier));
        if (dossier.PrimaryExitRule.HasValue &&
            !dossier.FiredExitRules.Contains(dossier.PrimaryExitRule.Value))
            throw new ArgumentException("Primary exit rule must appear in fired-rule evidence.", nameof(dossier));
        if (dossier.FiredExitRules.Count > 0 && !dossier.PrimaryExitRule.HasValue)
            throw new ArgumentException("Fired exit rules require one primary explanation.", nameof(dossier));
        if (dossier.ReasonCodes.Count == 0 || dossier.ReasonCodes.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one stable reason code is required.", nameof(dossier));
        return dossier;
    }

    private static void ValidateThesis(
        Guid? runId,
        Guid? candidateId,
        Guid? strategyId,
        IReadOnlyList<DelphiLiveDossierLensAttribution> lenses)
    {
        if (!runId.HasValue || runId == Guid.Empty ||
            !candidateId.HasValue || candidateId == Guid.Empty ||
            !strategyId.HasValue || strategyId == Guid.Empty)
            throw new ArgumentException("Daily thesis identities must be complete.");
        ArgumentNullException.ThrowIfNull(lenses);
        if (lenses.Count is < 1 or > 2 || lenses.Any(lens => lens is null) ||
            lenses.Select(lens => lens.Lens).Distinct(StringComparer.Ordinal).Count() != lenses.Count ||
            lenses.Any(lens => lens.CalibrationLensEvaluationId == Guid.Empty ||
                lens.Lens is not ("Continuation" or "Breakout") ||
                lens.Rank is < 1 or > 25 || !lens.Published || !lens.Eligible ||
                string.IsNullOrWhiteSpace(lens.GateTraceJson)))
            throw new ArgumentException("Daily thesis requires complete and distinct published lens attribution.");
    }

    public static string Serialize(
        DelphiLiveDecisionDossier dossier,
        DelphiLivePolicyDefinition policy) =>
        JsonSerializer.Serialize(Validate(dossier, policy), JsonOptions);

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", parameterName);
    }
}

public sealed record DelphiLiveReportSnapshot(
    DateOnly TradingDate,
    DateTime BarEndUtc,
    string Symbol,
    Guid? DailyStrategyVersionId,
    Guid DelphiLivePolicyVersionId,
    IReadOnlyList<DelphiLiveDossierLensAttribution> SourceLenses,
    IReadOnlyDictionary<string, decimal?> RawValues,
    IReadOnlyList<DelphiLiveFamilyJudgment> FamilyJudgments,
    DelphiLiveMomentumJudgment Momentum,
    DelphiLiveDataConfidence Confidence,
    DelphiLiveRecommendationState Recommendation,
    DelphiLivePresentationActivity Presentation,
    DelphiLiveSafetyEvaluation Safety,
    string ActionState,
    IReadOnlyList<string> CoverageWarnings)
{
    public DelphiLiveOriginalEntryThesis? OriginalEntryThesis { get; init; }
}

public sealed class DelphiLiveReportBuilder
{
    public string BuildDiagnostic(
        DelphiLiveReportSnapshot snapshot,
        DelphiLivePolicyDefinition policy)
    {
        Validate(snapshot, policy);
        var output = new StringBuilder();
        output.AppendLine("DELPHI LIVE DIAGNOSTIC");
        output.AppendLine($"Session: {snapshot.TradingDate:yyyy-MM-dd}");
        output.AppendLine($"Checkpoint UTC: {snapshot.BarEndUtc:O}");
        output.AppendLine($"Symbol: {snapshot.Symbol}");
        output.AppendLine($"Current daily strategy: {snapshot.DailyStrategyVersionId?.ToString("D") ?? "No current frozen thesis"}");
        if (snapshot.OriginalEntryThesis is { } original)
            output.AppendLine($"Original entry: decision={original.EntryDecisionId:D}; daily strategy={original.DailyStrategyVersionId:D}; run={original.CalibrationRunId:D}; attribution retained separately");
        output.AppendLine($"Live policy: {snapshot.DelphiLivePolicyVersionId:D} ({policy.PolicyDefinitionName}; {policy.EvaluatorVersion})");
        output.AppendLine($"Collector / dossier / quote fill: {policy.CollectorVersion} / {policy.DecisionDossierVersion} / {policy.QuoteFillVersion}");
        output.AppendLine("Source lenses:");
        foreach (DelphiLiveDossierLensAttribution lens in snapshot.SourceLenses.OrderBy(x => x.Lens, StringComparer.Ordinal))
            output.AppendLine($"  {lens.Lens}: rank {lens.Rank}; key {lens.RankingKey:0.######}; published={lens.Published}; eligible={lens.Eligible}; firstFailure={lens.FirstFailure ?? "none"}");
        output.AppendLine("Raw values:");
        foreach ((string name, decimal? value) in snapshot.RawValues.OrderBy(x => x.Key, StringComparer.Ordinal))
            output.AppendLine($"  {name}: {(value.HasValue ? value.Value.ToString("0.########") : "Unavailable")}");
        output.AppendLine("Family judgments:");
        foreach (DelphiLiveFamilyJudgment family in snapshot.FamilyJudgments.OrderBy(x => x.Family))
            output.AppendLine($"  {family.Family}: {family.State} ({family.ReasonCode})");
        output.AppendLine($"Combined: {snapshot.Momentum.State}; tier={snapshot.Momentum.StrongTier}; detail={snapshot.Momentum.NeutralDetail}; S={snapshot.Momentum.SupportiveVotes}; W={snapshot.Momentum.WeakeningVotes}; persistence tie evidence retained");
        output.AppendLine($"Data confidence: {snapshot.Confidence.State} ({snapshot.Confidence.ConsecutiveMisses} consecutive miss(es))");
        output.AppendLine($"Recommendation / presentation / action: {snapshot.Recommendation} / {snapshot.Presentation} / {snapshot.ActionState}");
        output.AppendLine($"Safety veto: {snapshot.Safety.EntrySafetyVetoActive}; primary exit={snapshot.Safety.PrimaryExitRule?.ToString() ?? "none"}; fired={string.Join(",", snapshot.Safety.FiredExitRules)}");
        output.AppendLine($"Coverage: {(snapshot.CoverageWarnings.Count == 0 ? "complete" : string.Join("; ", snapshot.CoverageWarnings))}");
        return output.ToString();
    }

    public string BuildSummary(
        DelphiLiveReportSnapshot snapshot,
        DelphiLivePolicyDefinition policy)
    {
        Validate(snapshot, policy);
        string lenses = string.Join(
            " + ",
            snapshot.SourceLenses
                .OrderBy(x => x.Lens, StringComparer.Ordinal)
                .Select(x => $"{x.Lens} #{x.Rank}"));
        if (snapshot.SourceLenses.Count == 0)
            lenses = "No current frozen thesis";
        string families = string.Join(
            ", ",
            snapshot.FamilyJudgments
                .OrderBy(x => x.Family)
                .Select(x => $"{x.Family} {x.State}"));
        string warning = snapshot.CoverageWarnings.Count == 0
            ? ""
            : $" | Coverage warning: {string.Join("; ", snapshot.CoverageWarnings)}";
        return $"{snapshot.Symbol} | {lenses} | Live {snapshot.Momentum.State} ({families}) | " +
               $"Confidence {snapshot.Confidence.State} | {snapshot.Recommendation} / {snapshot.ActionState} | " +
               $"Safety {(snapshot.Safety.PrimaryExitRule?.ToString() ?? (snapshot.Safety.EntrySafetyVetoActive ? "Entry veto" : "Clear"))} | " +
               $"Policy {policy.PolicyDefinitionName} ({policy.PolicyVersionId:D}){warning}";
    }

    private static void Validate(
        DelphiLiveReportSnapshot snapshot,
        DelphiLivePolicyDefinition policy)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(policy);
        DelphiLivePolicyValidator.Validate(policy);
        if (snapshot.DailyStrategyVersionId == Guid.Empty ||
            snapshot.DelphiLivePolicyVersionId != policy.PolicyVersionId)
            throw new ArgumentException("Report strategy/policy identity is incomplete.", nameof(snapshot));
        if (snapshot.BarEndUtc.Kind != DateTimeKind.Utc || string.IsNullOrWhiteSpace(snapshot.Symbol))
            throw new ArgumentException("Report symbol and UTC checkpoint are required.", nameof(snapshot));
        if (snapshot.FamilyJudgments.Count != 4 ||
            snapshot.DailyStrategyVersionId.HasValue != (snapshot.SourceLenses.Count > 0))
            throw new ArgumentException("Report must expose all four families and consistent current-source attribution.", nameof(snapshot));
    }
}
