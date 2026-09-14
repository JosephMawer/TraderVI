#nullable enable
using Core.ML.Engine.Profit;
using Core.Runtime;
using Core.TMX.Models.Domain;
using Core.Trader;
using Core.Trader.DelphiLive;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Core.Practice;

public sealed record PracticeSource(string Source, DateTime AcquiredUtc, string RevisionAndAdjustment,
    string UniverseProvenance, string Limitations);
public sealed record PracticeShadowPolicy(string PortfolioCode, Guid VersionId, SystemShadowPolicyConfig Settings);

public sealed record PracticeDatasetDocument
{
    public string Format { get; init; } = "TraderVI.Practice.DatasetV1";
    public string Label { get; init; } = "Practice / Exploratory";
    public DateOnly FirstSession { get; init; } = new(2026, 8, 10);
    public DateOnly LastSession { get; init; } = new(2026, 9, 4);
    public required ReviewedTsxCalendarDocument Calendar { get; init; }
    public required DelphiHistoricalInputs Daily { get; init; }
    public required PracticeSource Provenance { get; init; }
    public required string CoreAssemblySha256 { get; init; }
    public required Guid ShadowPolicyId { get; init; }
    public SystemShadowPolicyConfig ShadowPolicy { get; init; } = SystemShadowPolicyConfig.Version1;
    public IReadOnlyList<PracticeShadowPolicy> PortfolioPolicies { get; init; } = [];
    public int AssumedBarDelayMinutes { get; init; } = 2;
    public IReadOnlyList<TmxIntradayBatch> Intraday { get; init; } = [];
}

/// <summary>Content-verified package. Model paths are relative inside dataset.json and resolved only
/// inside its directory. Acquisition timestamps remain on saved batches; simulated receipts are separate.</summary>
public sealed class PracticeDataset
{
    public static JsonSerializerOptions Json { get; } = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public PracticeDatasetDocument Document { get; }
    public string Identity { get; }
    public ReviewedTsxSessionCalendar Calendar { get; }
    public IReadOnlyList<DateOnly> Sessions { get; }
    internal PracticeDataset(PracticeDatasetDocument document, string identity)
    {
        Document = document;
        Identity = identity;
        Calendar = new(document.Calendar);
        if (document.Format != "TraderVI.Practice.DatasetV1" || document.Label != "Practice / Exploratory" ||
            document.FirstSession > document.LastSession || document.AssumedBarDelayMinutes is < 0 or > 30)
            throw new InvalidDataException("Unsupported practice dataset contract.");
        Sessions = document.Calendar.RegularSessionDates.Where(d => d >= document.FirstSession && d <= document.LastSession).Order().ToArray();
        if (!Calendar.IsRegularSession(document.FirstSession) || !Calendar.IsRegularSession(document.LastSession) || Sessions.Count == 0)
            throw new InvalidDataException("The run boundaries must be covered regular sessions.");
        Calendar.GetImmediatelyPrecedingSession(document.FirstSession);
        if (document.Daily.ExploratoryCurrentUniverse is { } current &&
            (current.AsOf == default || current.AsOf.Kind != DateTimeKind.Utc))
            throw new InvalidDataException("An exploratory current universe requires its actual UTC capture time.");
        SystemShadowPolicy.ValidateConfig(document.ShadowPolicy);
        if (document.PortfolioPolicies.Select(x => x.PortfolioCode).Distinct().Count() != document.PortfolioPolicies.Count ||
            document.PortfolioPolicies.Any(x => x.VersionId == Guid.Empty || !SystemShadowPortfolioDefinition.Version1.Any(p => p.Code == x.PortfolioCode)))
            throw new InvalidDataException("Invalid per-portfolio policy snapshot.");
        foreach (var assignment in document.PortfolioPolicies) SystemShadowPolicy.ValidateConfig(assignment.Settings);
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static string CurrentCoreHash() => Hash(File.ReadAllBytes(typeof(PracticeDataset).Assembly.Location));

    public static PracticeDataset Load(string directory)
    {
        string root = Path.GetFullPath(directory);
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, "dataset.json"));
        string identity = Hash(bytes);
        if (!string.Equals(identity, File.ReadAllText(Path.Combine(root, "dataset.sha256")).Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Practice dataset checksum mismatch.");
        var document = JsonSerializer.Deserialize<PracticeDatasetDocument>(bytes, Json) ?? throw new InvalidDataException("Empty dataset.");
        if (document.CoreAssemblySha256 != CurrentCoreHash())
            throw new InvalidDataException("This dataset pins a different Core build. Prepare a new package explicitly for this build.");
        var models = document.Daily.Models.Models.Select(model =>
        {
            string path = Inside(root, model.Registry.ZipPath);
            if (!string.Equals(Hash(File.ReadAllBytes(path)), model.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pinned practice model checksum mismatch.");
            return model with { Registry = Relocate(model, path).Registry };
        }).ToImmutableArray();
        document = document with { Daily = document.Daily with { Models = document.Daily.Models with { Models = models },
            Daily = new(document.Daily.Daily, StringComparer.OrdinalIgnoreCase),
            Obv = new(document.Daily.Obv, StringComparer.OrdinalIgnoreCase),
            UsIndices = new(document.Daily.UsIndices, StringComparer.OrdinalIgnoreCase) } };
        document.Daily.Models.Validate(document.Daily.Strategy.VersionId, document.Daily.Strategy.DecisionRef);
        if (document.ShadowPolicyId == Guid.Empty || string.IsNullOrWhiteSpace(document.Provenance.Source) ||
            string.IsNullOrWhiteSpace(document.Provenance.RevisionAndAdjustment) || string.IsNullOrWhiteSpace(document.Provenance.UniverseProvenance))
            throw new InvalidDataException("Pinned policy and source provenance are required.");
        // A missing bar is data. Conflicting identities or malformed timestamps are a corrupt package.
        var identities = new HashSet<(string Symbol, int Interval, DateTime Start)>();
        foreach (var batch in document.Intraday)
        {
            if (batch.IntervalMinutes is not (5 or 15) || batch.ReceivedUtc.Kind != DateTimeKind.Utc)
                throw new InvalidDataException("Practice requires normalized five/fifteen minute batches with recorded acquisition times.");
            foreach (var bar in batch.Bars)
                if (bar.TimestampUtc.Kind != DateTimeKind.Utc || !identities.Add((batch.Symbol.ToUpperInvariant(), batch.IntervalMinutes, bar.TimestampUtc)))
                    throw new InvalidDataException("Duplicate intraday identity or non-UTC event time.");
        }
        return new(document, identity);
    }

    /// <summary>Explicit local preparation, never called by replay. Input contains normalized batches,
    /// dated daily inputs, and absolute paths to preserved models; no SQL or provider is consulted.</summary>
    public static void Pack(string inputJson, string destination)
    {
        string root = Path.GetFullPath(destination);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Refusing to replace an existing package.");
        var input = JsonSerializer.Deserialize<PracticeDatasetDocument>(File.ReadAllText(inputJson), Json) ?? throw new InvalidDataException("Empty input.");
        input.Daily.Models.Validate(input.Daily.Strategy.VersionId, input.Daily.Strategy.DecisionRef);
        // Verify all sources before creating the destination.
        var artifacts = input.Daily.Models.Models.Select(m => (Model: m, Bytes: File.ReadAllBytes(m.Registry.ZipPath))).ToArray();
        if (artifacts.Any(x => !string.Equals(Hash(x.Bytes), x.Model.ArtifactSha256, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Input model checksum mismatch.");
        Directory.CreateDirectory(Path.Combine(root, "models"));
        var relocated = artifacts.Select(x =>
        {
            string relative = "models/" + x.Model.Registry.ModelId.ToString("N") + ".zip";
            File.WriteAllBytes(Inside(root, relative), x.Bytes);
            return Relocate(x.Model, relative);
        }).ToImmutableArray();
        var output = input with { CoreAssemblySha256 = CurrentCoreHash(), Daily = input.Daily with { Models = input.Daily.Models with { Models = relocated } } };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(output, Json);
        File.WriteAllBytes(Path.Combine(root, "dataset.json"), bytes);
        File.WriteAllText(Path.Combine(root, "dataset.sha256"), Hash(bytes));
        Load(root);
    }

    private static ProfitModelSnapshot Relocate(ProfitModelSnapshot model, string path)
    {
        var node = JsonSerializer.SerializeToNode(model.Registry)!;
        node[nameof(model.Registry.ZipPath)] = path;
        return model with { Registry = node.Deserialize<Core.Db.ModelRegistryInfo>()! };
    }
    private static string Inside(string root, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A package reference must stay inside the package.");
        for (var parent = new FileInfo(path).Directory; parent is not null && parent.FullName.Length >= root.Length; parent = parent.Parent)
            if (parent.Exists && parent.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Package links are not supported.");
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Package links are not supported.");
        return path;
    }
}
