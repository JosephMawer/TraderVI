#nullable enable
using Core.Db;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Core.ML.Engine.Profit;

public sealed record ProfitModelSnapshot(ModelRegistryInfo Registry, string ArtifactSha256)
{
    public string Role { get; init; } = ProfitModelRegistry.GetByTaskType(Registry.TaskType)?.Role.ToString() ?? "Unknown";
    public float CompositeWeight { get; init; } = ProfitModelRegistry.GetByTaskType(Registry.TaskType)?.CompositeWeight ?? 0;
}

/// <summary>Append-only assignment of preserved model bytes and metadata to one strategy version.</summary>
public sealed record StoredProfitModelSet(Guid StrategyVersionId, Guid ModelSetId, string InputContract,
    string ReviewReference, string ReviewedBy, DateTime ReviewedUtc, string SourceIdentity,
    ImmutableArray<ProfitModelSnapshot> Models)
{
    public ReviewedProfitInputBinding ToBinding() => new(StrategyVersionId, InputContract, ReviewReference,
        ReviewedBy, ReviewedUtc, Models.Select(model => new ReviewedProfitModelBinding(model.Registry.ModelId,
            model.Registry.TaskType, model.ArtifactSha256, model.Registry.InputSchema, model.Registry.FeatureSet!,
            model.Registry.LookbackBars, model.Registry.HorizonBars, model.Registry.ModelKind,
            model.Registry.ThresholdBuy, model.Registry.ThresholdSell)).ToImmutableArray());

    public void Validate(Guid strategyId, string? decisionRef)
    {
        if (strategyId != StrategyVersionId || ModelSetId == Guid.Empty || string.IsNullOrWhiteSpace(SourceIdentity) ||
            Models.IsDefaultOrEmpty || Models.Any(model => model?.Registry is null || !Path.IsPathFullyQualified(model.Registry.ZipPath)))
            throw new InvalidDataException("Incomplete stored strategy/model assignment.");
        var binding = ToBinding();
        if (InputContract == ProfitFeatureInputs.Contract)
            binding.ValidateStrategy(strategyId, decisionRef);
        else if (InputContract == ProfitFeatureInputs.LegacyContract && !ReviewedProfitInputBinding.RequiresDatedInputs(decisionRef))
            binding.ValidatePreservedLegacy(strategyId);
        else throw new InvalidDataException("Unsupported strategy/model input contract.");
        binding.ValidateRows(Models.Select(model => model.Registry).ToArray());
        if (Models.Any(model => model.Role != ProfitModelRegistry.GetByTaskType(model.Registry.TaskType)!.Role.ToString() ||
            model.CompositeWeight != ProfitModelRegistry.GetByTaskType(model.Registry.TaskType)!.CompositeWeight))
            throw new InvalidDataException("Runtime model roles or weights differ from the stored strategy assignment.");
        if (InputContract == ProfitFeatureInputs.LegacyContract && Models.Any(model => model.Registry.FeatureSet!.EndsWith(".XiuDatedV1", StringComparison.Ordinal)))
            throw new InvalidDataException("A dated candidate cannot be assigned to legacy inputs.");
    }

    // SQL Server hashes NVARCHAR as UTF-16LE. Match its CHECK constraint exactly.
    public static string HashJson(string json) => Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(json)));

    public static StoredProfitModelSet Read(string json, string expectedHash)
    {
        if (!string.Equals(HashJson(json), expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Stored strategy/model assignment checksum mismatch.");
        return JsonSerializer.Deserialize<StoredProfitModelSet>(json)
            ?? throw new InvalidDataException("Stored strategy/model assignment is empty.");
    }
}
