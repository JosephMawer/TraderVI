#nullable enable
using Core.Db;
using Core.ML.Engine.Profit;
using Shouldly;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class ProfitModelPreservationTests
{
    [Fact]
    public void TrainingArtifactWriterCannotOverwritePreviousBytesOrInvokeReplacementWriter()
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "original synthetic bytes");
            bool called = false;
            Should.Throw<IOException>(() => ProfitModelArtifactStore.WriteNew(file, stream => called = true));
            called.ShouldBeFalse();
            File.ReadAllText(file).ShouldBe("original synthetic bytes");
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void PreservationKeepsEarlierBytesAndMetadataWhenOriginalPathChanges()
    {
        string root = Path.Combine(Path.GetTempPath(), "tradervi-model-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "source.zip");
            File.WriteAllText(source, "original synthetic bytes");
            var row = new ModelRegistryInfo { ModelId = Guid.NewGuid(), TaskType = "BinaryUp10", ZipPath = source, ThresholdBuy = 0.7 };
            var preserved = ProfitModelArtifactStore.Preserve(row, Path.Combine(root, "copies"));
            File.WriteAllText(source, "changed synthetic bytes");
            var second = ProfitModelArtifactStore.Preserve(row, Path.Combine(root, "copies"));
            preserved.Registry.ModelId.ShouldBe(row.ModelId);
            preserved.Registry.ThresholdBuy.ShouldBe(0.7);
            File.ReadAllText(preserved.Registry.ZipPath).ShouldBe("original synthetic bytes");
            second.Registry.ZipPath.ShouldNotBe(preserved.Registry.ZipPath);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoredAssignmentRoundTripsAsACompleteVersionBoundSnapshot(bool dated)
    {
        var set = Set(dated);
        var json = JsonSerializer.Serialize(set);
        var restored = StoredProfitModelSet.Read(json, StoredProfitModelSet.HashJson(json));
        restored.Validate(set.StrategyVersionId, dated ? ReviewedProfitInputBinding.DecisionRef : "ADR-0043");
        restored.ToBinding().Models.ShouldBe(set.ToBinding().Models);
        Should.Throw<InvalidDataException>(() => StoredProfitModelSet.Read(json.Replace("fixture-review", "other-review"), StoredProfitModelSet.HashJson(json)));
        Should.Throw<InvalidDataException>(() => restored.Validate(Guid.NewGuid(), ReviewedProfitInputBinding.DecisionRef));
    }

    [Fact]
    public void TrainingSourceArchiveMatchesCapturedFilesAndRejectsAChangedSourceIdentity()
    {
        string root = Path.Combine(Path.GetTempPath(), "tradervi-source-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Core"));
        Directory.CreateDirectory(Path.Combine(root, "ML.Train"));
        try
        {
            string source = Path.Combine(root, "Core", "Fixture.cs");
            File.WriteAllText(source, "// synthetic source");
            string identity = ProfitTrainingSourceIdentity.Capture(root);
            string archive = Path.Combine(root, "source.zip");
            ProfitTrainingSourceIdentity.PreserveArchive(root, archive, identity).ShouldBe(ProfitModelArtifactStore.HashFile(archive));
            File.WriteAllText(source, "// changed synthetic source");
            Should.Throw<InvalidDataException>(() => ProfitTrainingSourceIdentity.PreserveArchive(root, Path.Combine(root, "changed.zip"), identity));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ChangedRoleOrWeightCannotSilentlyChangePreservedRuntimeBehavior()
    {
        var set = Set(true);
        var changed = set with { Models = set.Models.SetItem(0, set.Models[0] with { CompositeWeight = 0.99f }) };
        Should.Throw<InvalidDataException>(() => changed.Validate(set.StrategyVersionId, ReviewedProfitInputBinding.DecisionRef));
    }

    [Fact]
    public void CorrectedStrategyCannotUseLegacyAssignmentAndDatedModelCannotUseLegacyInputs()
    {
        var legacy = Set(false);
        Should.Throw<InvalidDataException>(() => legacy.Validate(legacy.StrategyVersionId, ReviewedProfitInputBinding.DecisionRef));
        var dated = Set(true) with { InputContract = ProfitFeatureInputs.LegacyContract };
        Should.Throw<InvalidDataException>(() => dated.Validate(dated.StrategyVersionId, "ADR-0043"));
    }

    private static StoredProfitModelSet Set(bool dated) => new(Guid.NewGuid(), Guid.NewGuid(),
        dated ? ProfitFeatureInputs.Contract : ProfitFeatureInputs.LegacyContract, "fixture-review", "fixture",
        DateTime.UtcNow, "fixture-source-identity", ProfitModelRegistry.All.Select(model => new ProfitModelSnapshot(new ModelRegistryInfo
        {
            ModelId = Guid.NewGuid(), TaskType = model.TaskType, ModelKind = model.ModelKind.ToString(),
            InputSchema = model.TaskType + "_profit", FeatureSet = model.FeatureBuilder.Name + (dated ? ".XiuDatedV1" : ""),
            LookbackBars = model.Lookback, HorizonBars = model.HorizonBars, ThresholdBuy = 0.6,
            ZipPath = Path.Combine(Path.GetTempPath(), "unused-synthetic.zip")
        }, new string('A', 64))).ToImmutableArray());
}
