#nullable enable
using Core.Calibration;
using Core.Db;
using System;
using System.IO;
using System.Security.Cryptography;

namespace Core.ML.Engine.Profit;

/// <summary>Loads and fingerprints the same private byte snapshot, with the registry row resolved by the caller.</summary>
internal static class LoadedModelArtifact
{
    internal static T Load<T>(ModelRegistryInfo registry, Func<Stream, ModelArtifactProvenance, T> create)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(create);
        byte[] bytes = File.ReadAllBytes(registry.ZipPath);
        var provenance = new ModelArtifactProvenance(registry.ModelId, registry.TaskType, registry.ModelKind,
            registry.InputSchema, registry.FeatureSet, registry.TrainedFromUtc, registry.TrainedToUtc,
            Convert.ToHexString(SHA256.HashData(bytes)));
        using var stream = new MemoryStream(bytes, writable: false);
        return create(stream, provenance);
    }
}
