#nullable enable
using Core.Calibration;
using System;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Core.ML.Engine.Profit;

public sealed record ProfitCandidateResult(ProfitModelSnapshot Model, string LabelDefinition, ProfitTrainingResult Training);
public sealed record ProfitCandidateManifest(Guid ModelSetId, string InputContract, DateTime CreatedUtc,
    CodeProvenance Code, string SourceSha256, string DataSnapshotPath, string DataSnapshotSha256,
    DateTime MarketDataAsOf, Guid? PredecessorStrategyVersionId, ImmutableArray<ProfitCandidateResult> Models)
{
    public string? SourceArchivePath { get; init; }
    public string? SourceArchiveSha256 { get; init; }
}

public static class ProfitTrainingSourceIdentity
{
    public static string Capture(string repositoryRoot)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string file in SourceFiles(repositoryRoot))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/') + "\0"));
            hash.AppendData(SHA256.HashData(File.ReadAllBytes(file)));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static string PreserveArchive(string repositoryRoot, string path, string expectedSourceHash)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ProfitModelArtifactStore.WriteNew(path, stream =>
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            foreach (string file in SourceFiles(repositoryRoot))
            {
                string relative = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');
                byte[] bytes = File.ReadAllBytes(file);
                hash.AppendData(Encoding.UTF8.GetBytes(relative + "\0"));
                hash.AppendData(SHA256.HashData(bytes));
                using var entry = archive.CreateEntry(relative).Open();
                entry.Write(bytes);
            }
        });
        if (Convert.ToHexString(hash.GetHashAndReset()) != expectedSourceHash)
            throw new InvalidDataException("Training source changed while preserving its archive.");
        return ProfitModelArtifactStore.HashFile(path);
    }

    private static System.Collections.Generic.IEnumerable<string> SourceFiles(string repositoryRoot) =>
        new[] { "Core", "ML.Train" }.SelectMany(project => Directory.EnumerateFiles(Path.Combine(repositoryRoot, project), "*.cs", SearchOption.AllDirectories))
            .Where(path => !Path.GetRelativePath(repositoryRoot, path).Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
            .OrderBy(path => Path.GetRelativePath(repositoryRoot, path), StringComparer.Ordinal);
}
