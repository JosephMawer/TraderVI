#nullable enable
using Core.Db;
using System;
using System.IO;
using System.Security.Cryptography;

namespace Core.ML.Engine.Profit;

public static class ProfitModelArtifactStore
{
    public static void WriteNew(string path, Action<Stream> write)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        write(stream);
        stream.Flush(flushToDisk: true);
    }

    public static ProfitModelSnapshot Preserve(ModelRegistryInfo row, string directory)
    {
        byte[] bytes = File.ReadAllBytes(row.ZipPath);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        Directory.CreateDirectory(directory);
        string target = Path.Combine(Path.GetFullPath(directory), hash + ".zip");
        if (!File.Exists(target)) WriteNew(target, stream => stream.Write(bytes));
        if (!string.Equals(HashFile(target), hash, StringComparison.Ordinal))
            throw new InvalidDataException("Preserved model bytes failed verification; no model was selected.");
        return new(new ModelRegistryInfo
        {
            ModelId = row.ModelId, Name = row.Name, TaskType = row.TaskType, ModelKind = row.ModelKind,
            Family = row.Family, TimeFrame = row.TimeFrame, LookbackBars = row.LookbackBars,
            HorizonBars = row.HorizonBars, InputSchema = row.InputSchema, FeatureSet = row.FeatureSet,
            ZipPath = target, ThresholdBuy = row.ThresholdBuy, ThresholdSell = row.ThresholdSell,
            IsEnabled = row.IsEnabled, CreatedUtc = row.CreatedUtc, TrainedFromUtc = row.TrainedFromUtc,
            TrainedToUtc = row.TrainedToUtc, Notes = row.Notes
        }, hash);
    }

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
