#nullable enable
using Core.Calibration;
using Core.Db;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.ML.Engine.Profit;

public sealed record ReviewedProfitModelBinding(Guid ModelId, string TaskType, string ArtifactSha256,
    string InputSchema, string FeatureSet, int LookbackBars, int HorizonBars, string ModelKind,
    double ThresholdBuy, double ThresholdSell);

/// <summary>
/// Explicit compatibility attestation for a newly registered strategy. This
/// selects exact model identities without enabling registry rows or approving
/// strategy promotion. Creating it requires a real, separately authorized review.
/// </summary>
public sealed record ReviewedProfitInputBinding(Guid StrategyVersionId, string InputContract,
    string ReviewReference, string ReviewedBy, DateTime ReviewedUtc, ImmutableArray<ReviewedProfitModelBinding> Models)
{
    public const string DecisionRef = "ADR-0056";

    public static bool RequiresDatedInputs(string? decisionRef) =>
        decisionRef == DecisionRef || Core.Runtime.DailyBenchmarkPolicy.IsRequired(decisionRef);

    public static ReviewedProfitInputBinding? Resolve(Guid? strategyVersionId, string? decisionRef, string? json)
    {
        if (json is null)
        {
            if (RequiresDatedInputs(decisionRef))
                throw new InvalidOperationException("The corrected strategy requires an explicit reviewed model-input binding; legacy inputs cannot be substituted.");
            return null;
        }
        var binding = JsonSerializer.Deserialize<ReviewedProfitInputBinding>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidOperationException("A reviewed model-input binding is required.");
        binding.ValidateStrategy(strategyVersionId, decisionRef);
        return binding;
    }

    public void ValidateStrategy(Guid? strategyVersionId, string? decisionRef)
    {
        if (!RequiresDatedInputs(decisionRef) || InputContract != ProfitFeatureInputs.Contract)
            throw new InvalidOperationException("Dated model inputs require a new ADR-0056 strategy and an explicit compatibility review binding.");
        ValidateIdentity(strategyVersionId);
    }

    internal void ValidatePreservedLegacy(Guid strategyVersionId)
    {
        if (InputContract != ProfitFeatureInputs.LegacyContract)
            throw new InvalidOperationException("Only a preserved legacy assignment may use legacy inputs.");
        ValidateIdentity(strategyVersionId);
    }

    private void ValidateIdentity(Guid? strategyVersionId)
    {
        if (StrategyVersionId == Guid.Empty || strategyVersionId != StrategyVersionId || string.IsNullOrWhiteSpace(ReviewReference) ||
            string.IsNullOrWhiteSpace(ReviewedBy) || ReviewedUtc.Kind != DateTimeKind.Utc || ReviewedUtc == default || Models.IsDefaultOrEmpty)
            throw new InvalidOperationException("Dated model inputs require a new ADR-0056 strategy and an explicit compatibility review binding.");
        var expected = ProfitModelRegistry.All.Select(model => model.TaskType).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (Models.Any(model => model is null) ||
            !Models.Select(model => model.TaskType).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(expected) ||
            Models.Select(model => model.ModelId).Distinct().Count() != expected.Length ||
            Models.Any(model => model.ModelId == Guid.Empty || model.ArtifactSha256 is not { Length: 64 } ||
                !model.ArtifactSha256.All(Uri.IsHexDigit)))
            throw new InvalidOperationException("A reviewed binding must identify exactly one hashed artifact for every active profit task.");
    }

    public void ValidateRows(IReadOnlyList<ModelRegistryInfo> rows)
    {
        if (rows.Count != Models.Length || rows.Select(row => row.ModelId).Distinct().Count() != Models.Length)
            throw new InvalidOperationException("The reviewed model set is missing or duplicated in the registry.");
        foreach (var expected in Models)
        {
            var row = rows.SingleOrDefault(row => row.ModelId == expected.ModelId);
            var definition = ProfitModelRegistry.GetByTaskType(expected.TaskType);
            if (row is null || definition is null || row.TaskType != expected.TaskType || row.InputSchema != expected.InputSchema ||
                row.FeatureSet != expected.FeatureSet || row.ModelKind != expected.ModelKind || row.LookbackBars != expected.LookbackBars ||
                row.HorizonBars != expected.HorizonBars || row.ThresholdBuy != expected.ThresholdBuy || row.ThresholdSell != expected.ThresholdSell ||
                string.IsNullOrWhiteSpace(row.InputSchema) ||
                (row.FeatureSet != definition.FeatureBuilder.Name && row.FeatureSet != definition.FeatureBuilder.Name + ".XiuDatedV1") ||
                row.LookbackBars != definition.Lookback || row.HorizonBars != definition.HorizonBars || row.ModelKind != definition.ModelKind.ToString() ||
                !double.IsFinite(row.ThresholdBuy) || !double.IsFinite(row.ThresholdSell))
                throw new InvalidOperationException($"Reviewed model metadata does not match the runtime contract for {expected.TaskType}.");
        }
    }

    public void ValidateArtifact(ModelArtifactProvenance artifact)
    {
        var expected = Models.SingleOrDefault(model => model.ModelId == artifact.ModelId);
        if (expected is null || expected.TaskType != artifact.TaskType ||
            !string.Equals(expected.ArtifactSha256, artifact.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The loaded bytes do not match the reviewed compatible artifact.");
    }
}
