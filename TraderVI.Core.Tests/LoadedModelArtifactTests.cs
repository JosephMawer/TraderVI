#nullable enable
using Core.Calibration;
using Core.Db;
using Core.ML;
using Core.ML.Engine.Profit;
using Core.Trader;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class LoadedModelArtifactTests
{
    [Fact]
    public void ReplacedPathCannotChangeBytesOrIdentityAlreadyLoadedIntoEngine()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] original = Encoding.UTF8.GetBytes("synthetic original payload, not an ML model");
            byte[] replacement = Encoding.UTF8.GetBytes("synthetic replacement payload");
            File.WriteAllBytes(path, original);
            var row = Row(path);
            var model = LoadedModelArtifact.Load(row, (stream, identity) =>
            {
                // Simulate a path replacement between artifact capture and model
                // construction. The constructor receives the captured bytes only.
                File.WriteAllBytes(path, replacement);
                stream.CanWrite.ShouldBeFalse();
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                copy.ToArray().ShouldBe(original);
                return new SyntheticProfitModel(row.TaskType, identity);
            });
            var engine = new TradeDecisionEngine(Array.Empty<IStockSignalModel>(), new IProfitSignalModel[] { model });
            var captured = engine.LoadedModelProvenance.Single();
            captured.ModelId.ShouldBe(row.ModelId);
            captured.ArtifactSha256.ShouldBe(Convert.ToHexString(SHA256.HashData(original)));
            captured.ArtifactSha256.ShouldNotBe(Convert.ToHexString(SHA256.HashData(replacement)));
            captured.InputSchema.ShouldBe(row.InputSchema);
            captured.TrainedToUtc.ShouldBe(row.TrainedToUtc);
            File.Delete(path);
            // Evidence remains available even after the path no longer exists.
            CalibrationRunAuditPolicy.Evaluate(new("abc123", "Git", "Clean"), engine.LoadedModelProvenance,
                engine.LoadedProfitTaskTypes, [row.TaskType]).State.ShouldBe(CalibrationAuditState.Valid);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void FailedLoaderDoesNotReturnSuccessfulArtifactEvidence()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "invalid synthetic artifact");
            Should.Throw<InvalidDataException>(() => LoadedModelArtifact.Load<object>(Row(path),
                (_, _) => throw new InvalidDataException("Synthetic loader rejection")));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void EngineWithoutBoundArtifactCannotPassOfficialAudit()
    {
        var engine = new TradeDecisionEngine(Array.Empty<IStockSignalModel>(),
            new IProfitSignalModel[] { new SyntheticProfitModel("Synthetic", null) });
        CalibrationRunAuditPolicy.Evaluate(new("abc123", "Git", "Dirty"), engine.LoadedModelProvenance,
            engine.LoadedProfitTaskTypes, ["Synthetic"]).State.ShouldBe(CalibrationAuditState.Invalid);
    }

    private static ModelRegistryInfo Row(string path) => new()
    {
        ModelId = Guid.NewGuid(), TaskType = "Synthetic", ModelKind = "BinaryClassification", ZipPath = path,
        InputSchema = "SyntheticSchema", FeatureSet = "SyntheticFeatures", TrainedToUtc = new DateTime(2026, 1, 1)
    };

    private sealed class SyntheticProfitModel(string name, ModelArtifactProvenance? provenance) : IProfitSignalModel
    {
        public string Name => name;
        public SignalRole Role => SignalRole.Setup;
        public float CompositeWeight => 1f;
        public ModelArtifactProvenance? ArtifactProvenance => provenance;
        public SignalResult Evaluate(IReadOnlyList<DailyBar> history) => new(Name, 0.5, TradeDirection.Hold);
    }
}
