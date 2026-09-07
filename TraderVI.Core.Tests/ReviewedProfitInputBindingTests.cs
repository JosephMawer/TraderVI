#nullable enable
using Core.Db;
using Core.ML;
using Core.ML.Engine.Profit;
using Shouldly;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class ReviewedProfitInputBindingTests
{
    [Fact]
    public void ExistingStrategyKeepsLegacyPathButCorrectedStrategyCannotFallBack()
    {
        ReviewedProfitInputBinding.Resolve(Guid.NewGuid(), "ADR-0042", null).ShouldBeNull();
        ReviewedProfitInputBinding.Resolve(null, null, null).ShouldBeNull();
        Should.Throw<InvalidOperationException>(() =>
            ReviewedProfitInputBinding.Resolve(Guid.NewGuid(), ReviewedProfitInputBinding.DecisionRef, null));
    }

    [Theory]
    [InlineData("ADR-0056")]
    [InlineData("ADR-0058")]
    public void ExplicitBindingRoundTripsAndRequiresTheReviewedStrategyIdentity(string decisionRef)
    {
        var binding = Binding();
        var json = JsonSerializer.Serialize(binding);
        var resolved = ReviewedProfitInputBinding.Resolve(binding.StrategyVersionId, decisionRef, json)!;
        resolved.Models.ShouldBe(binding.Models);
        Should.Throw<InvalidOperationException>(() => ReviewedProfitInputBinding.Resolve(Guid.NewGuid(), ReviewedProfitInputBinding.DecisionRef, json));
        Should.Throw<InvalidOperationException>(() => ReviewedProfitInputBinding.Resolve(binding.StrategyVersionId, "ADR-0042", json));
        Should.Throw<JsonException>(() => ReviewedProfitInputBinding.Resolve(binding.StrategyVersionId, ReviewedProfitInputBinding.DecisionRef,
            json.Insert(1, "\"UnknownContractField\":true,")));
    }

    [Theory]
    [InlineData("missing-task")]
    [InlineData("duplicate-id")]
    [InlineData("bad-hash")]
    [InlineData("no-reviewer")]
    [InlineData("wrong-contract")]
    [InlineData("undated-review")]
    [InlineData("null-model")]
    public void IncompleteCompatibilityEvidenceIsRejected(string fault)
    {
        var binding = Binding();
        binding = fault switch
        {
            "missing-task" => binding with { Models = binding.Models.RemoveAt(0) },
            "duplicate-id" => binding with { Models = binding.Models.SetItem(0, binding.Models[0] with { ModelId = binding.Models[1].ModelId }) },
            "bad-hash" => binding with { Models = binding.Models.SetItem(0, binding.Models[0] with { ArtifactSha256 = new string('Z', 64) }) },
            "no-reviewer" => binding with { ReviewedBy = " " },
            "wrong-contract" => binding with { InputContract = ProfitFeatureInputs.LegacyContract },
            "undated-review" => binding with { ReviewedUtc = default },
            "null-model" => binding with { Models = binding.Models.SetItem(0, null!) },
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        Should.Throw<InvalidOperationException>(() => binding.ValidateStrategy(binding.StrategyVersionId, ReviewedProfitInputBinding.DecisionRef));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReviewedLegacyOrDatedArtifactsCanBeSelectedWithoutEnablingRegistryRows(bool dated)
    {
        var binding = Binding(dated);
        var rows = binding.Models.Select(model => Row(model)).ToArray();
        binding.ValidateRows(rows);
        rows.All(row => !row.IsEnabled).ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => binding.ValidateRows(rows.Skip(1).ToArray()));
        Should.Throw<InvalidOperationException>(() => binding.ValidateRows(rows.Skip(1).Append(rows[1]).ToArray()));
    }

    [Theory]
    [InlineData("threshold")]
    [InlineData("feature-order")]
    [InlineData("lookback")]
    [InlineData("horizon")]
    [InlineData("schema")]
    [InlineData("kind")]
    public void RegistryMetadataDriftCannotChangeReviewedBehavior(string fault)
    {
        var binding = Binding();
        var changed = binding.Models[0];
        changed = fault switch
        {
            "threshold" => changed with { ThresholdBuy = changed.ThresholdBuy + 0.01 },
            "feature-order" => changed with { FeatureSet = "OtherFeatures" },
            "lookback" => changed with { LookbackBars = changed.LookbackBars + 1 },
            "horizon" => changed with { HorizonBars = changed.HorizonBars + 1 },
            "schema" => changed with { InputSchema = "OtherSchema" },
            "kind" => changed with { ModelKind = "Regression" },
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        var rows = binding.Models.Select((model, index) => Row(index == 0 ? changed : model)).ToArray();
        Should.Throw<InvalidOperationException>(() => binding.ValidateRows(rows));
    }

    [Fact]
    public void ArtifactMismatchIsRejectedByActualPredictionLoaderBeforeMlDeserialization()
    {
        var binding = Binding();
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "synthetic unreviewed bytes, not an ML artifact");
            var failure = Should.Throw<InvalidOperationException>(() => UnifiedProfitSignalModel.FromRegistryInfo(
                Row(binding.Models[0], path), new ProfitFeatureInputs([], []), new DateTime(2026, 9, 4), binding));
            failure.Message.ShouldBe("The loaded bytes do not match the reviewed compatible artifact.");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DatedCandidateCannotBeLoadedThroughLegacyPathEvenWhenFileDoesNotExist()
    {
        var row = Row(Binding(dated: true).Models[0]);
        var failure = Should.Throw<InvalidOperationException>(() => UnifiedProfitSignalModel.FromRegistryInfo(row));
        failure.Message.ShouldContain("reviewed corrected-input path");
    }

    private static ReviewedProfitInputBinding Binding(bool dated = false) => new(Guid.NewGuid(), ProfitFeatureInputs.Contract,
        "Synthetic review fixture only", "Fixture reviewer", new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc),
        ProfitModelRegistry.All.Select(model => new ReviewedProfitModelBinding(Guid.NewGuid(), model.TaskType,
            new string('A', 64), model.TaskType + "_profit", model.FeatureBuilder.Name + (dated ? ".XiuDatedV1" : ""),
            model.Lookback, model.HorizonBars, model.ModelKind.ToString(), 0.6, 0)).ToImmutableArray());

    private static ModelRegistryInfo Row(ReviewedProfitModelBinding model, string path = "unused-synthetic-path") => new()
    {
        ModelId = model.ModelId, TaskType = model.TaskType, InputSchema = model.InputSchema, FeatureSet = model.FeatureSet,
        LookbackBars = model.LookbackBars, HorizonBars = model.HorizonBars, ModelKind = model.ModelKind,
        ThresholdBuy = model.ThresholdBuy, ThresholdSell = model.ThresholdSell, IsEnabled = false, ZipPath = path
    };
}
