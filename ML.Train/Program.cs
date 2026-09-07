using Core.Db;
using Core.Calibration;
using Core.ML;
using Core.ML.Engine.Profit;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

// Training always creates separate candidates. Selection is a reviewed operation.
string outputRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraderVI", "Models");
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--dated-candidates") continue;
    if (args[i] == "--output-root" && i + 1 < args.Length) outputRoot = args[++i];
    else { Console.Error.WriteLine("Usage: Hercules [--output-root <private-model-directory>]"); return 2; }
}
Guid setId = Guid.NewGuid();
string directory = Path.Combine(Path.GetFullPath(outputRoot), "candidates", setId.ToString("N"));
Directory.CreateDirectory(directory);
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
void SaveNew(string name, object value) => ProfitModelArtifactStore.WriteNew(Path.Combine(directory, name),
    stream => JsonSerializer.Serialize(stream, value, jsonOptions));

try
{
    string sourceIdentity = ProfitTrainingSourceIdentity.Capture(Environment.CurrentDirectory);
    string sourceArchivePath = Path.Combine(directory, "training-source.zip");
    string sourceArchiveHash = ProfitTrainingSourceIdentity.PreserveArchive(Environment.CurrentDirectory, sourceArchivePath, sourceIdentity);
    var code = CalibrationProvenance.ResolveCode(Environment.CurrentDirectory);
    var registry = new ModelRegistryRepository();
    var strategy = await new StrategyVersionRepository().GetActiveVersion();
    var currentSet = strategy is null ? null : await new StrategyModelBindingRepository().GetAsync(strategy.VersionId);
    currentSet?.Validate(strategy!.VersionId, strategy.DecisionRef);
    if (currentSet is not null && currentSet.Models.Any(model => ProfitModelArtifactStore.HashFile(model.Registry.ZipPath) != model.ArtifactSha256))
        throw new InvalidDataException("The predecessor assignment no longer matches its preserved bytes.");
    var allowed = ProfitModelRegistry.All.Select(model => model.TaskType).ToHashSet(StringComparer.Ordinal);
    var currentRows = currentSet?.Models.Select(model => model.Registry).ToList()
        ?? (await registry.GetEnabledModels()).Where(row => allowed.Contains(row.TaskType)).ToList();
    if (currentRows.Count != allowed.Count || currentRows.Select(row => row.TaskType).Distinct().Count() != allowed.Count)
        throw new InvalidOperationException("A complete unambiguous predecessor set is required to preserve signal thresholds.");
    var preserved = currentRows.Select(row => ProfitModelArtifactStore.Preserve(row, Path.Combine(directory, "predecessor"))).ToImmutableArray();
    SaveNew("predecessor.json", new { strategy, modelSet = currentSet, models = preserved });
    Console.WriteLine($"Preserved {preserved.Length} predecessor artifacts. Training candidates; no activation.");

    var quoteRepo = new QuoteRepository();
    var market = (await quoteRepo.GetDailyBarsAsync("XIU")).Where(bar => bar.Date.Date < DateTime.Today).OrderBy(bar => bar.Date).ToList();
    if (market.Count == 0) throw new InvalidOperationException("No completed XIU history is available.");
    DateTime asOf = market[^1].Date.Date;
    var inputs = new ProfitFeatureInputs(market, market.Select(bar => bar.Date.Date).Distinct().ToArray());
    inputs.ValidateBenchmark(asOf, ProfitModelRegistry.All.Max(model => model.Lookback));
    const int maxSymbols = 494; // Existing cap, unchanged.
    var symbols = (await new SymbolsRepository().GetEquitiesAsync()).Select(row => row.Symbol)
        .Where(symbol => !string.IsNullOrWhiteSpace(symbol)).Take(maxSymbols).ToArray();
    var barsBySymbol = new Dictionary<string, List<DailyBar>>(StringComparer.OrdinalIgnoreCase);
    foreach (string symbol in symbols)
    {
        var bars = (await quoteRepo.GetDailyBarsAsync(symbol)).Where(bar => bar.Date.Date <= asOf).ToList();
        if (bars.Count > 0) barsBySymbol.Add(symbol, bars);
    }
    string dataPath = Path.Combine(directory, "training-data.json.gz");
    ProfitModelArtifactStore.WriteNew(dataPath, stream =>
    {
        using var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true);
        JsonSerializer.Serialize(gzip, new { inputContract = ProfitFeatureInputs.Contract, marketDataAsOf = asOf, market, barsBySymbol });
    });
    string dataHash = ProfitModelArtifactStore.HashFile(dataPath);
    SaveNew("started.json", new { setId, code, sourceIdentity, sourceArchiveHash, asOf, dataHash, inputContract = ProfitFeatureInputs.Contract });

    var results = ImmutableArray.CreateBuilder<ProfitCandidateResult>();
    foreach (var definition in ProfitModelRegistry.All)
    {
        var model = inputs.Bind(definition);
        string path = Path.Combine(directory, model.TaskType + ".zip");
        var result = UnifiedProfitTrainer.Train(model, barsBySymbol, path);
        SaveNew(model.TaskType + ".training.json", result);
        if (!result.Success) throw new InvalidOperationException($"{model.TaskType} did not produce a valid candidate. Existing selection is unchanged.");
        var previous = preserved.Single(item => item.Registry.TaskType == model.TaskType).Registry;
        // OptimalThreshold remains a research metric. Do not retune signal thresholds in this correction.
        Guid id = await registry.InsertModel(model.TaskType + " (dated candidate)", model.TaskType, model.ModelKind.ToString(),
            "Profit", "Daily", model.Lookback, model.HorizonBars, model.TaskType + "_profit", model.FeatureBuilder.Name,
            path, previous.ThresholdBuy, previous.ThresholdSell, isEnabled: false,
            result.TrainingWindowFrom, result.TrainingWindowTo,
            $"Candidate set {setId:D}; {ProfitFeatureInputs.Contract}; SourceSHA256={sourceIdentity}; DataSHA256={dataHash}; thresholds preserved.");
        var row = (await registry.GetModelsById([id])).Single();
        var snapshot = new ProfitModelSnapshot(row, ProfitModelArtifactStore.HashFile(path));
        results.Add(new(snapshot, model.Labeler.Name, result));
        await new ModelExperimentRepository().InsertExperiment(model.TaskType, "Dated candidate", model.Labeler.Name,
            model.FeatureBuilder.Name, model.FeatureBuilder.FeatureCount(model.Lookback), result.TrainWindows, result.TestWindows,
            auc: result.PrimaryMetric, f1AtDefault: result.SecondaryMetric, f1AtOptimal: result.F1AtOptimal,
            optimalThreshold: result.OptimalThreshold, precisionAtOpt: result.PrecisionAtOptimal, recallAtOpt: result.RecallAtOptimal,
            decision: "Candidate", notes: $"ModelId={id:D}; SetId={setId:D}; SHA256={snapshot.ArtifactSha256}; SourceSHA256={sourceIdentity}; DataSHA256={dataHash}");
    }
    if (sourceIdentity != ProfitTrainingSourceIdentity.Capture(Environment.CurrentDirectory))
        throw new InvalidOperationException("Training source changed during the run; candidates were not completed or selected.");
    SaveNew("candidate-set.json", new ProfitCandidateManifest(setId, ProfitFeatureInputs.Contract, DateTime.UtcNow, code,
        sourceIdentity, dataPath, dataHash, asOf, strategy?.VersionId, results.ToImmutable())
        { SourceArchivePath = sourceArchivePath, SourceArchiveSha256 = sourceArchiveHash });
    Console.WriteLine($"Completed {results.Count} candidate models. All registry rows remain disabled. Manifest: {Path.Combine(directory, "candidate-set.json")}");
    return 0;
}
catch (Exception failure)
{
    SaveNew("failed.json", new { failedUtc = DateTime.UtcNow, message = failure.Message });
    Console.Error.WriteLine($"Candidate training failed: {failure.Message}");
    return 1;
}
