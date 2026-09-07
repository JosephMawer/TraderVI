#nullable enable
using Core.Db;
using Core.ML.Engine.Profit;
using Core.Runtime;
using Shouldly;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiSettingsTests
{
    [Fact]
    public void SelectingPreservedSetLoadsItsStrategyWithoutCreatingAnEditedCopy()
    {
        var active = Option(true);
        var source = Option(false, .42);
        var change = Prepare(active, source);
        change.CreatesVersion.ShouldBeFalse();
        change.TargetId.ShouldBe(source.Strategy.VersionId);
        change.Gates.MinCompositeScore.ShouldBe(.42);
        change.ValidateCurrent(new([active, source]));
    }

    [Fact]
    public void EditedThresholdCreatesNewIdentityAndKeepsModelsAndInputPolicy()
    {
        var active = Option(true);
        string before = JsonSerializer.Serialize(active);
        var gates = DelphiGateSettings.From(active.Strategy.ToConfig()) with { MaxDownProb = .27 };
        var change = DelphiSettingsChange.Prepare(new([active]), active, gates, "candidate-gates", "Review fixture", "assembly-mvid:fixture");
        change.CreatesVersion.ShouldBeTrue();
        change.TargetId.ShouldNotBe(active.Strategy.VersionId);
        var binding = change.CreateBinding(DateTime.UtcNow);
        binding.Validate(change.TargetId, active.Strategy.DecisionRef);
        binding.ModelSetId.ShouldBe(active.ModelSet!.ModelSetId);
        binding.Models.ShouldBe(active.ModelSet.Models);
        binding.InputContract.ShouldBe(ProfitFeatureInputs.Contract);
        JsonSerializer.Serialize(active).ShouldBe(before);
        change.Gates.MaxDownProb.ShouldBe(.27);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-.01)]
    [InlineData(1.01)]
    public void InvalidProbabilityCannotBeReviewed(double probability)
    {
        var active = Option(true);
        var gates = DelphiGateSettings.From(active.Strategy.ToConfig()) with { MinUpProb = probability };
        Should.Throw<ArgumentException>(() => DelphiSettingsChange.Prepare(new([active]), active, gates,
            "new-version", "fixture", "assembly-mvid:fixture"));
    }

    [Fact]
    public void UnchangedActiveSelectionIsRejectedBeforeAnyWrite()
    {
        var active = Option(true);
        Should.Throw<InvalidOperationException>(() => Prepare(active, active));
    }

    [Fact]
    public void UnboundAndCorruptAssignmentsCannotBeSelected()
    {
        var active = Option(true);
        var source = Option(false);
        Should.Throw<InvalidOperationException>(() => Prepare(active, source with { ModelSet = null }));
        Should.Throw<InvalidOperationException>(() => Prepare(active, source with { UnavailableReason = "checksum mismatch" }));
    }

    [Fact]
    public void ChangedActiveStrategyRejectsPreviouslyReviewedSwitch()
    {
        var active = Option(true);
        var source = Option(false);
        var change = Prepare(active, source);
        Should.Throw<InvalidOperationException>(() => change.ValidateCurrent(new([Option(true), source])));
    }

    [Fact]
    public void ChangedSourceSettingsOrModelChecksumRejectPreviouslyReviewedSwitch()
    {
        var active = Option(true);
        var source = Option(false);
        var change = Prepare(active, source);
        var changed = source with { Strategy = new StrategyVersionInfo
        {
            VersionId = source.Strategy.VersionId, VersionName = source.Strategy.VersionName,
            MinCompositeScore = .99, InitialCodeCommit = "fixture-source", DecisionRef = DailyBenchmarkPolicy.DecisionRef
        }};
        Should.Throw<InvalidOperationException>(() => change.ValidateCurrent(new([active, changed])));
        Should.Throw<InvalidOperationException>(() => change.ValidateCurrent(new([active, source with { ModelSetHash = new string('B', 64) }])));
    }

    [Fact]
    public void ChangedActiveThresholdsRejectReviewEvenIfActiveIdIsUnchanged()
    {
        var active = Option(true);
        var source = Option(false);
        var change = Prepare(active, source);
        var modified = active with { Strategy = new StrategyVersionInfo
        {
            VersionId = active.Strategy.VersionId, VersionName = active.Strategy.VersionName, IsActive = true,
            MinCompositeScore = .99, InitialCodeCommit = "fixture-source", DecisionRef = DailyBenchmarkPolicy.DecisionRef
        }};
        Should.Throw<InvalidOperationException>(() => change.ValidateCurrent(new([modified, source])));
    }

    [Fact]
    public void EditingRequiresUniqueNameAndReasonAndRejectsRaceForName()
    {
        var active = Option(true);
        var gates = DelphiGateSettings.From(active.Strategy.ToConfig()) with { MinDirectionEdge = .2 };
        Should.Throw<ArgumentException>(() => DelphiSettingsChange.Prepare(new([active]), active, gates,
            active.Strategy.VersionName, "fixture", "fixture-code"));
        Should.Throw<ArgumentException>(() => DelphiSettingsChange.Prepare(new([active]), active, gates,
            "new-version", " ", "fixture-code"));
        var change = DelphiSettingsChange.Prepare(new([active]), active, gates, "new-version", "fixture", "fixture-code");
        var conflict = Option(false) with { Strategy = new StrategyVersionInfo { VersionId = Guid.NewGuid(), VersionName = "NEW-VERSION" } };
        Should.Throw<InvalidOperationException>(() => change.ValidateCurrent(new([active, conflict])));
    }

    [Fact]
    public void MultipleActiveVersionsFailClosed()
    {
        var a = Option(true); var b = Option(true);
        Should.Throw<InvalidOperationException>(() => DelphiSettingsChange.Prepare(new([a, b]), b,
            DelphiGateSettings.From(b.Strategy.ToConfig()), "", "fixture", "fixture-code"));
    }

    [Fact]
    public void ModelBytesChangedAfterRegistrationCannotPassReview()
    {
        var active = Option(true); var source = Option(false);
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "synthetic altered bytes");
            var model = source.ModelSet!.Models[0];
            // Clone via JSON to preserve all metadata while replacing only the synthetic fixture path.
            string json = JsonSerializer.Serialize(source.ModelSet).Replace(
                JsonSerializer.Serialize(model.Registry.ZipPath), JsonSerializer.Serialize(file));
            var altered = StoredProfitModelSet.Read(json, StoredProfitModelSet.HashJson(json));
            var candidate = source with { ModelSet = altered, ModelSetHash = StoredProfitModelSet.HashJson(json) };
            var change = Prepare(active, candidate);
            Should.Throw<InvalidOperationException>(() => change.VerifyArtifacts()).Message.ShouldContain("loaded bytes");
        }
        finally { File.Delete(file); }
    }

    private static DelphiSettingsChange Prepare(DelphiStrategySettings active, DelphiStrategySettings source) =>
        DelphiSettingsChange.Prepare(new(active == source ? [active] : [active, source]), source,
            DelphiGateSettings.From(source.Strategy.ToConfig()), "", "fixture review", "fixture-code");

    private static DelphiStrategySettings Option(bool active, double composite = .35)
    {
        var id = Guid.NewGuid();
        var strategy = new StrategyVersionInfo { VersionId = id, VersionName = id.ToString("N"), IsActive = active,
            MinCompositeScore = composite, InitialCodeCommit = "fixture-code", DecisionRef = DailyBenchmarkPolicy.DecisionRef };
        var set = new StoredProfitModelSet(id, Guid.NewGuid(), ProfitFeatureInputs.Contract, "fixture", "fixture",
            DateTime.UtcNow, "fixture-source", ProfitModelRegistry.All.Select(model => new ProfitModelSnapshot(new ModelRegistryInfo
            {
                ModelId = Guid.NewGuid(), TaskType = model.TaskType, ModelKind = model.ModelKind.ToString(),
                InputSchema = model.TaskType + "_profit", FeatureSet = model.FeatureBuilder.Name + ".XiuDatedV1",
                LookbackBars = model.Lookback, HorizonBars = model.HorizonBars, ThresholdBuy = .6,
                ZipPath = Path.Combine(Path.GetTempPath(), "unused-synthetic.zip")
            }, new string('A', 64))).ToImmutableArray());
        return new(strategy, set, StoredProfitModelSet.HashJson(JsonSerializer.Serialize(set)), null);
    }
}
