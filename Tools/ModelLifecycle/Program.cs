using Core.Db;
using Core.ML;
using Core.ML.Engine.Profit;
using Core.Runtime;
using Microsoft.ML;
using Microsoft.ML.Data;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;

// Loads model bytes; never trains, writes SQL, publishes recommendations, or places orders.
if (args.Length == 1 && args[0] == "verify-benchmarks")
{
    var quotes = new QuoteRepository();
    var snapshot = DailyBenchmarkPolicy.Prepare(DateTime.Today, DailyBenchmarkPolicy.LoadCalendar(),
        await quotes.GetDailyBarsAsync("XIU"), await quotes.GetDailyBarsAsync("SPY"));
    Console.WriteLine(JsonSerializer.Serialize(snapshot.Evidence));
    Console.WriteLine("Observed XIU/SPY benchmark prerequisites passed. No recommendations or SQL writes.");
    return 0;
}

if (args.Length == 2 && args[0] == "prepare-benchmark-switch")
{
    var previous = await new StrategyVersionRepository().GetActiveVersion()
        ?? throw new InvalidDataException("No active predecessor.");
    if (previous.DecisionRef != ReviewedProfitInputBinding.DecisionRef)
        throw new InvalidDataException("This reviewed transition requires the dated-input predecessor.");
    var oldSet = await new StrategyModelBindingRepository().GetAsync(previous.VersionId)
        ?? throw new InvalidDataException("No preserved predecessor model set.");
    oldSet.Validate(previous.VersionId, previous.DecisionRef);
    foreach (var model in oldSet.Models)
        if (ProfitModelArtifactStore.HashFile(model.Registry.ZipPath) != model.ArtifactSha256)
            throw new InvalidDataException("A selected artifact changed.");
    var quotes = new QuoteRepository();
    var snapshot = DailyBenchmarkPolicy.Prepare(DateTime.Today, DailyBenchmarkPolicy.LoadCalendar(),
        await quotes.GetDailyBarsAsync("XIU"), await quotes.GetDailyBarsAsync("SPY"));
    var newSet = oldSet with { StrategyVersionId = Guid.NewGuid(),
        ReviewReference = "Docs/adr/0058-daily-ingestion-and-observed-benchmark-confirmation.md; operator-authorized benchmark repair; same four models",
        ReviewedBy = "Codex technical review; operator authorized repair", ReviewedUtc = DateTime.UtcNow,
        SourceIdentity = "working-tree-sha256:" + ProfitTrainingSourceIdentity.Capture(Environment.CurrentDirectory) };
    newSet.Validate(newSet.StrategyVersionId, DailyBenchmarkPolicy.DecisionRef);
    string output = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(output);
    foreach (var item in new[] { (Name: "previous-model-set.json", Set: oldSet), (Name: "corrected-model-set.json", Set: newSet) })
        ProfitModelArtifactStore.WriteNew(Path.Combine(output, item.Name), stream => JsonSerializer.Serialize(stream, item.Set));
    ProfitModelArtifactStore.WriteNew(Path.Combine(output, "previous-strategy.json"), stream => JsonSerializer.Serialize(stream, previous));
    ProfitModelArtifactStore.WriteNew(Path.Combine(output, "benchmark-evidence.json"), stream => JsonSerializer.Serialize(stream, snapshot.Evidence));
    ProfitTrainingSourceIdentity.PreserveArchive(Environment.CurrentDirectory, Path.Combine(output, "runtime-source.zip"), newSet.SourceIdentity.Split(':')[1]);
    Console.WriteLine("Prepared ADR-0058 assignment with unchanged model bytes and thresholds; no strategy selection or recommendations.");
    return 0;
}

if (args.Length == 1 && args[0] == "verify-active")
{
    var strategy = await new StrategyVersionRepository().GetActiveVersion()
        ?? throw new InvalidDataException("No active strategy.");
    var activeSet = await new StrategyModelBindingRepository().GetAsync(strategy.VersionId)
        ?? throw new InvalidDataException("The active strategy has no preserved model assignment.");
    activeSet.Validate(strategy.VersionId, strategy.DecisionRef);
    if (DailyBenchmarkPolicy.IsRequired(strategy.DecisionRef))
    {
        var quotes = new QuoteRepository();
        var benchmark = DailyBenchmarkPolicy.Prepare(DateTime.Today, DailyBenchmarkPolicy.LoadCalendar(),
            await quotes.GetDailyBarsAsync("XIU"), await quotes.GetDailyBarsAsync("SPY"));
        Console.WriteLine($"Benchmark policy {benchmark.Evidence.PolicyVersion}: XIU and SPY verified through {benchmark.Evidence.MarketSession:yyyy-MM-dd}.");
    }
    var xiu = (await new QuoteRepository().GetDailyBarsAsync("XIU")).Where(bar => bar.Date.Date < DateTime.Today).OrderBy(bar => bar.Date).ToArray();
    if (xiu.Length == 0) throw new InvalidDataException("No completed XIU input.");
    var activeInputs = activeSet.InputContract == ProfitFeatureInputs.Contract
        ? new ProfitFeatureInputs(xiu, xiu.Select(bar => bar.Date.Date).Distinct().ToArray()) : null;
    activeInputs?.ValidateBenchmark(xiu[^1].Date.Date, ProfitModelRegistry.All.Max(model => model.Lookback));
    var engine = await DelphiBootstrap.BuildTradeDecisionEngineFromRegistry(strategy.ToConfig(), TextWriter.Null,
        activeInputs, activeInputs is null ? null : xiu[^1].Date.Date,
        activeInputs is null ? null : activeSet.ToBinding(), activeSet);
    if (engine.LoadedModelProvenance.Length != 4) throw new InvalidDataException("Incomplete loaded active set.");
    Console.WriteLine($"Active strategy: {strategy.VersionName}; contract: {activeSet.InputContract}; all four exact model artifacts loaded by Delphi bootstrap.");
    Console.WriteLine("Read-only check completed. No recommendations, trading rows, market collection or broker actions were produced.");
    return 0;
}

if (args.Length == 3 && args[0] == "prepare-switch")
{
    string manifestPath = Path.GetFullPath(args[1]);
    var candidate = JsonSerializer.Deserialize<ProfitCandidateManifest>(File.ReadAllText(manifestPath))!;
    string sourcePath;
    string sourceHash;
    if (candidate.SourceArchivePath is not null && candidate.SourceArchiveSha256 is not null)
    {
        sourcePath = candidate.SourceArchivePath;
        sourceHash = candidate.SourceArchiveSha256;
    }
    else
    {
        using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(manifestPath)!, "source-archive-receipt.json")));
        if (receipt.RootElement.GetProperty("CandidateManifestSha256").GetString() != ProfitModelArtifactStore.HashFile(manifestPath) ||
            receipt.RootElement.GetProperty("SourceSha256").GetString() != candidate.SourceSha256)
            throw new InvalidDataException("Source archive receipt does not identify this training run.");
        sourcePath = receipt.RootElement.GetProperty("SourceArchivePath").GetString()!;
        sourceHash = receipt.RootElement.GetProperty("SourceArchiveSha256").GetString()!;
    }
    if (ProfitModelArtifactStore.HashFile(sourcePath) != sourceHash) throw new InvalidDataException("Training source archive changed.");
    if (candidate.InputContract != ProfitFeatureInputs.Contract || candidate.Models.Length != ProfitModelRegistry.All.Count ||
        candidate.Models.Any(model => !model.Training.Success || model.Model.Registry.IsEnabled) ||
        ProfitModelArtifactStore.HashFile(candidate.DataSnapshotPath) != candidate.DataSnapshotSha256)
        throw new InvalidDataException("Candidate set is incomplete, selected already, or has changed input evidence.");
    using var predecessor = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(manifestPath)!, "predecessor.json")));
    var previous = predecessor.RootElement.GetProperty("strategy").Deserialize<StrategyVersionInfo>()!;
    var previousModels = predecessor.RootElement.GetProperty("models").Deserialize<ImmutableArray<ProfitModelSnapshot>>();
    var existingSet = predecessor.RootElement.GetProperty("modelSet").ValueKind == JsonValueKind.Null ? null
        : predecessor.RootElement.GetProperty("modelSet").Deserialize<StoredProfitModelSet>();
    if (candidate.PredecessorStrategyVersionId != previous.VersionId) throw new InvalidDataException("Predecessor identity mismatch.");
    foreach (var model in candidate.Models.Select(item => item.Model).Concat(previousModels))
        if (ProfitModelArtifactStore.HashFile(model.Registry.ZipPath) != model.ArtifactSha256)
            throw new InvalidDataException("A preserved or candidate artifact has changed.");
    foreach (var model in candidate.Models)
    {
        var prior = previousModels.Single(item => item.Registry.TaskType == model.Model.Registry.TaskType).Registry;
        if (prior.ThresholdBuy != model.Model.Registry.ThresholdBuy || prior.ThresholdSell != model.Model.Registry.ThresholdSell)
            throw new InvalidDataException("This correction must preserve existing signal thresholds.");
    }
    const string review = "Docs/reviews/model-input-cutover-20260906.md; operator-authorized correctness repair";
    var oldSet = existingSet ?? new StoredProfitModelSet(previous.VersionId, Guid.NewGuid(), ProfitFeatureInputs.LegacyContract,
        review + "; preserved previous inputs, no compatibility claim", "Codex technical review", DateTime.UtcNow,
        "preserved-at-working-tree-sha256:" + candidate.SourceSha256, previousModels);
    var newSet = new StoredProfitModelSet(Guid.NewGuid(), candidate.ModelSetId, candidate.InputContract, review,
        "Codex technical review; operator authorized selection", DateTime.UtcNow,
        "working-tree-sha256:" + ProfitTrainingSourceIdentity.Capture(Environment.CurrentDirectory), candidate.Models.Select(item => item.Model).ToImmutableArray());
    oldSet.Validate(previous.VersionId, previous.DecisionRef);
    newSet.Validate(newSet.StrategyVersionId, ReviewedProfitInputBinding.DecisionRef);
    string output = Path.GetFullPath(args[2]);
    Directory.CreateDirectory(output);
    foreach (var item in new[] { (Name: "previous-model-set.json", Set: oldSet), (Name: "corrected-model-set.json", Set: newSet) })
        ProfitModelArtifactStore.WriteNew(Path.Combine(output, item.Name), stream => JsonSerializer.Serialize(stream, item.Set));
    ProfitModelArtifactStore.WriteNew(Path.Combine(output, "previous-strategy.json"), stream => JsonSerializer.Serialize(stream, previous));
    Console.WriteLine("Prepared previous and corrected assignments; all artifact/data hashes and unchanged thresholds verified. No database selection performed.");
    return 0;
}

if (args.Length != 2 || args[0] is not ("inspect-registry" or "verify-set"))
{
    Console.Error.WriteLine("Usage: ModelLifecycle inspect-registry <registry-json> | verify-set <stored-set-json>");
    return 2;
}
var context = new MLContext(seed: 0);
if (args[0] == "inspect-registry")
{
    var rows = JsonSerializer.Deserialize<List<ModelRegistryInfo>>(File.ReadAllText(args[1]))!;
    foreach (var definition in ProfitModelRegistry.All)
    {
        var row = rows.Single(item => item.IsEnabled && item.TaskType == definition.TaskType);
        context.Model.Load(row.ZipPath, out var schema);
        var vector = schema["Features"].Type as VectorDataViewType;
        bool shapeMatches = vector?.Size == definition.FeatureBuilder.FeatureCount(definition.Lookback);
        Console.WriteLine($"{row.TaskType}: ML artifact loads; feature shape matches={shapeMatches}; named slots={schema["Features"].Annotations.Schema.Any(column => column.Name == "SlotNames")}");
    }
    Console.WriteLine("Shape validation cannot establish historical input semantics without matching training-contract evidence.");
    return 0;
}

var set = JsonSerializer.Deserialize<StoredProfitModelSet>(File.ReadAllText(args[1]))!;
set.Validate(set.StrategyVersionId, set.InputContract == ProfitFeatureInputs.Contract ? ReviewedProfitInputBinding.DecisionRef : "PreservedLegacy");
var stock = new List<DailyBar>();
var market = new List<DailyBar>();
for (int i = 0; i < 80; i++)
{
    DateTime day = new DateTime(2025, 1, 1).AddDays(i);
    stock.Add(new() { Date = day, Open = 100 + i, High = 102 + i, Low = 99 + i, Close = 101 + i, Volume = 100000 + i });
    market.Add(new() { Date = day, Open = 100 + i / 2f, High = 102 + i / 2f, Low = 99 + i / 2f, Close = 101 + i / 2f, Volume = 100000 });
}
var inputs = set.InputContract == ProfitFeatureInputs.Contract ? new ProfitFeatureInputs(market, market.Select(bar => bar.Date).ToArray()) : null;
foreach (var snapshot in set.Models)
{
    var model = UnifiedProfitSignalModel.FromRegistryInfo(snapshot.Registry, inputs, inputs is null ? null : stock[^1].Date, set.ToBinding())!;
    var result = model.Evaluate(stock);
    if (!double.IsFinite(result.Score)) throw new InvalidDataException("Synthetic model validation produced a non-finite result.");
    Console.WriteLine($"{snapshot.Registry.TaskType}: reviewed bytes loaded; synthetic prediction finite.");
}
return 0;
