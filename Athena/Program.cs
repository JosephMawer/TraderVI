using Core.Calibration;
using Core.Db;
using Core.ML;
using Core.TMX.Models.Domain;
using Core.Trader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

string? scorecardCsvDirectory = null;
if (args.Length > 0)
{
    if (args.Length != 2 || args[0] != "--scorecard-csv" || string.IsNullOrWhiteSpace(args[1]))
        throw new ArgumentException("Usage: Athena [--scorecard-csv DIRECTORY]");
    scorecardCsvDirectory = Path.GetFullPath(args[1]);
}

Console.WriteLine("=== Athena: deterministic calibration evaluator ===");
Console.WriteLine("Local SQL only; no external market services.\n");

var outcomes = new CalibrationOutcomeRepository();
await outcomes.EnsureOutcomeDefinitionsAsync();
var quoteRepository = new QuoteRepository();
var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

var barCache = new Dictionary<string, List<DailyBar>>(StringComparer.OrdinalIgnoreCase);
async Task<List<DailyBar>> GetBarsAsync(string symbol)
{
    if (barCache.TryGetValue(symbol, out var cached)) return cached;
    var loaded = await quoteRepository.GetDailyBarsAsync(symbol);
    barCache[symbol] = loaded;
    return loaded;
}

var labelPending = await outcomes.GetPendingOfficialCandidatesAsync(CalibrationOutcomeRepository.PredictionLabel10DefinitionId);
var pathPending = await outcomes.GetPendingOfficialCandidatesAsync(CalibrationOutcomeRepository.PredictionPath20DefinitionId);
var labelIds = labelPending.Select(x => x.CandidateId).ToHashSet();
var pathIds = pathPending.Select(x => x.CandidateId).ToHashSet();
var candidates = labelPending.Concat(pathPending)
    .GroupBy(x => x.CandidateId)
    .Select(x => x.First())
    .ToList();

var xiuBars = await GetBarsAsync("XIU");

async Task<(int Inspected, int Matured, int NoEntry, int Pending, int Invalid)> EvaluateTradeableDefinitionAsync(
    Guid definitionId,
    Func<DateTime, DateTime, IReadOnlyList<DailyBar>, IReadOnlyList<DailyBar>, SwingOutcomeReadiness> assess,
    Func<DateTime, DateTime, IReadOnlyList<DailyBar>, IReadOnlyList<DailyBar>, object> calculate)
{
    var pendingCandidates = await outcomes.GetPendingPublishedCandidatesAsync(definitionId);
    int matured = 0, noEntry = 0, pending = 0, invalid = 0;

    foreach (var candidate in pendingCandidates)
    {
        var symbolBars = await GetBarsAsync(candidate.Symbol);
        var readiness = assess(candidate.ObservationDate, candidate.RunStartedUtc, symbolBars, xiuBars);

        if (readiness.State == SwingOutcomeReadinessState.Pending)
        {
            pending++;
            continue;
        }

        if (readiness.State == SwingOutcomeReadinessState.Invalid)
        {
            var invalidOutcome = new InvalidSwingOutcomeV1(
                SwingMarkToMarketOutcomeCalculator.SchemaVersion,
                candidate.ObservationDate.Date,
                SwingMarkToMarketOutcomeCalculator.NormalizeUtc(candidate.RunStartedUtc),
                readiness.InitialEligibleSession,
                readiness.EntrySession,
                readiness.EntryDelaySessions,
                readiness.BenchmarkSessionsAvailable,
                readiness.FirstInvalidSession,
                readiness.ReasonCode ?? "InvalidSwingPath");
            if (await outcomes.InsertOutcomeAsync(
                candidate.CandidateId,
                definitionId,
                CalibrationOutcomeMaturityState.Matured,
                JsonSerializer.Serialize(invalidOutcome, jsonOptions),
                CalibrationAuditState.Invalid))
                invalid++;
            continue;
        }

        if (readiness.State == SwingOutcomeReadinessState.NoEntry)
        {
            var noEntryOutcome = new NoEntrySwingOutcomeV1(
                SwingMarkToMarketOutcomeCalculator.SchemaVersion,
                candidate.ObservationDate.Date,
                SwingMarkToMarketOutcomeCalculator.NormalizeUtc(candidate.RunStartedUtc),
                readiness.InitialEligibleSession!.Value,
                SwingMarkToMarketOutcomeCalculator.EntrySessionAllowance,
                readiness.ReasonCode ?? "NoSymbolBarWithinEntryAllowance");
            if (await outcomes.InsertOutcomeAsync(
                candidate.CandidateId,
                definitionId,
                CalibrationOutcomeMaturityState.NoEntry,
                JsonSerializer.Serialize(noEntryOutcome, jsonOptions),
                CalibrationAuditState.Valid))
                noEntry++;
            continue;
        }

        object result = calculate(candidate.ObservationDate, candidate.RunStartedUtc, symbolBars, xiuBars);
        if (await outcomes.InsertOutcomeAsync(
            candidate.CandidateId,
            definitionId,
            CalibrationOutcomeMaturityState.Matured,
            JsonSerializer.Serialize(result, result.GetType(), jsonOptions),
            CalibrationAuditState.Valid))
            matured++;
    }

    return (pendingCandidates.Count, matured, noEntry, pending, invalid);
}

int labelsWritten = 0, pathsWritten = 0, immature = 0, invalidWritten = 0;

foreach (var candidate in candidates)
{
    var symbolBars = await GetBarsAsync(candidate.Symbol);
    int observationIndex = symbolBars.FindIndex(x => x.Date.Date == candidate.ObservationDate.Date);
    int xiuObservationIndex = xiuBars.FindIndex(x => x.Date.Date == candidate.ObservationDate.Date);
    var future = symbolBars.Where(x => x.Date.Date > candidate.ObservationDate.Date).ToList();
    var futureXiu = xiuBars.Where(x => x.Date.Date > candidate.ObservationDate.Date).ToList();
    var labelReadiness = PredictionOutcomeCalculator.AssessReadiness(
        candidate.ObservationDate, future, futureXiu, PredictionOutcomeCalculator.LabelHorizon);
    var pathReadiness = PredictionOutcomeCalculator.AssessReadiness(
        candidate.ObservationDate, future, futureXiu, PredictionOutcomeCalculator.PathHorizon);

    async Task WriteInvalidAsync(Guid definitionId, PredictionOutcomeReadiness readiness)
    {
        var invalidOutcome = new InvalidPredictionOutcomeV1(
            PredictionOutcomeCalculator.SchemaVersion,
            candidate.ObservationDate.Date,
            readiness.RequiredSessions,
            readiness.BenchmarkSessionsAvailable,
            readiness.AlignedSymbolSessions,
            readiness.FirstInvalidSession,
            readiness.ReasonCode ?? "ObservationSessionMismatch");
        if (await outcomes.InsertOutcomeAsync(
            candidate.CandidateId,
            definitionId,
            CalibrationOutcomeMaturityState.Matured,
            JsonSerializer.Serialize(invalidOutcome, jsonOptions),
            CalibrationAuditState.Invalid))
            invalidWritten++;
    }

    if (labelIds.Contains(candidate.CandidateId) && labelReadiness.State == PredictionOutcomeReadinessState.Invalid)
        await WriteInvalidAsync(CalibrationOutcomeRepository.PredictionLabel10DefinitionId, labelReadiness);
    if (pathIds.Contains(candidate.CandidateId) && pathReadiness.State == PredictionOutcomeReadinessState.Invalid)
        await WriteInvalidAsync(CalibrationOutcomeRepository.PredictionPath20DefinitionId, pathReadiness);

    bool labelMatured = labelIds.Contains(candidate.CandidateId) && labelReadiness.State == PredictionOutcomeReadinessState.Matured;
    bool pathMatured = pathIds.Contains(candidate.CandidateId) && pathReadiness.State == PredictionOutcomeReadinessState.Matured;
    if (!labelMatured && !pathMatured)
    {
        if (labelReadiness.State == PredictionOutcomeReadinessState.Pending) immature++;
        continue;
    }

    if (observationIndex < 0 || xiuObservationIndex < 0)
    {
        var mismatch = new PredictionOutcomeReadiness(
            PredictionOutcomeReadinessState.Invalid,
            0,
            futureXiu.Count,
            0,
            candidate.ObservationDate.Date,
            "ObservationSessionMismatch");
        if (labelMatured) await WriteInvalidAsync(CalibrationOutcomeRepository.PredictionLabel10DefinitionId, mismatch);
        if (pathMatured) await WriteInvalidAsync(CalibrationOutcomeRepository.PredictionPath20DefinitionId, mismatch);
        continue;
    }

    var window = symbolBars.Take(observationIndex + 1).ToList();
    var result = PredictionOutcomeCalculator.Calculate(window, future, xiuBars[xiuObservationIndex].Close, futureXiu);

    if (labelMatured)
    {
        if (await outcomes.InsertOutcomeAsync(candidate.CandidateId, CalibrationOutcomeRepository.PredictionLabel10DefinitionId,
            CalibrationOutcomeMaturityState.Matured,
            JsonSerializer.Serialize(result, jsonOptions), CalibrationAuditState.Valid))
            labelsWritten++;
    }

    if (pathMatured)
    {
        if (await outcomes.InsertOutcomeAsync(candidate.CandidateId, CalibrationOutcomeRepository.PredictionPath20DefinitionId,
            CalibrationOutcomeMaturityState.Matured,
            JsonSerializer.Serialize(result, jsonOptions), CalibrationAuditState.Valid))
            pathsWritten++;
    }
}

Console.WriteLine($"Pending candidates inspected: {candidates.Count:N0}");
Console.WriteLine($"10-session label outcomes:   {labelsWritten:N0}");
Console.WriteLine($"20-session path outcomes:    {pathsWritten:N0}");
Console.WriteLine($"Not yet 10-session mature:   {immature:N0}");
Console.WriteLine($"Prediction invalid outcomes: {invalidWritten:N0}");

var swing = await EvaluateTradeableDefinitionAsync(
    CalibrationOutcomeRepository.SwingMarkToMarket3DefinitionId,
    SwingMarkToMarketOutcomeCalculator.AssessReadiness,
    SwingMarkToMarketOutcomeCalculator.Calculate);
var excursions = await EvaluateTradeableDefinitionAsync(
    CalibrationOutcomeRepository.SwingExcursion3DefinitionId,
    SwingMarkToMarketOutcomeCalculator.AssessExcursionReadiness,
    SwingMarkToMarketOutcomeCalculator.CalculateExcursions);
invalidWritten += swing.Invalid + excursions.Invalid;

var intradayRepository = new IntradayEvidenceRepository();
var calibrationEvidenceRepository = new CalibrationEvidenceRepository();
TimeZoneInfo torontoTimeZone = ResolveTorontoTimeZone();
var delayedPending = await outcomes.GetPendingPublishedCandidatesAsync(
    CalibrationOutcomeRepository.DelayedIntradaySwingDefinitionId);
int delayedMatured = 0, delayedNoEntry = 0, delayedPendingCount = 0, delayedInvalid = 0;
DateTime delayedThroughUtc = DateTime.UtcNow;
DateTime delayedFromUtc = delayedPending.Count == 0
    ? delayedThroughUtc
    : SessionOpenUtc(delayedPending.Min(candidate => candidate.ObservationDate));
var intradayCache = new Dictionary<(string Symbol, int Interval), IReadOnlyList<StoredIntradayOutcomeBar>>();

async Task<IReadOnlyList<StoredIntradayOutcomeBar>> GetIntradayBarsAsync(string symbol, int interval)
{
    var key = (symbol.ToUpperInvariant(), interval);
    if (intradayCache.TryGetValue(key, out var cached)) return cached;
    IReadOnlyList<StoredIntradayOutcomeBar> loaded = await intradayRepository.GetOutcomeBarsAsync(
        key.Item1,
        interval,
        delayedFromUtc,
        delayedThroughUtc);
    intradayCache[key] = loaded;
    return loaded;
}

foreach (PendingTradeableCalibrationCandidate candidate in delayedPending)
{
    List<DailyBar> symbolBars = await GetBarsAsync(candidate.Symbol);
    SwingOutcomeReadiness entryReadiness = SwingMarkToMarketOutcomeCalculator.AssessReadiness(
        candidate.ObservationDate,
        candidate.RunStartedUtc,
        symbolBars,
        xiuBars);

    if (entryReadiness.State == SwingOutcomeReadinessState.Invalid)
    {
        var invalidOutcome = new InvalidSwingOutcomeV1(
            DelayedIntradayOutcomeCalculator.SchemaVersion,
            candidate.ObservationDate.Date,
            SwingMarkToMarketOutcomeCalculator.NormalizeUtc(candidate.RunStartedUtc),
            entryReadiness.InitialEligibleSession,
            entryReadiness.EntrySession,
            entryReadiness.EntryDelaySessions,
            entryReadiness.BenchmarkSessionsAvailable,
            entryReadiness.FirstInvalidSession,
            entryReadiness.ReasonCode ?? "InvalidDelayedIntradayEntry");
        if (await outcomes.InsertOutcomeAsync(
            candidate.CandidateId,
            CalibrationOutcomeRepository.DelayedIntradaySwingDefinitionId,
            CalibrationOutcomeMaturityState.Matured,
            JsonSerializer.Serialize(invalidOutcome, jsonOptions),
            CalibrationAuditState.Invalid))
            delayedInvalid++;
        continue;
    }

    if (entryReadiness.State == SwingOutcomeReadinessState.NoEntry)
    {
        var noEntryOutcome = new NoEntrySwingOutcomeV1(
            DelayedIntradayOutcomeCalculator.SchemaVersion,
            candidate.ObservationDate.Date,
            SwingMarkToMarketOutcomeCalculator.NormalizeUtc(candidate.RunStartedUtc),
            entryReadiness.InitialEligibleSession!.Value,
            SwingMarkToMarketOutcomeCalculator.EntrySessionAllowance,
            entryReadiness.ReasonCode ?? "NoSymbolBarWithinEntryAllowance");
        if (await outcomes.InsertOutcomeAsync(
            candidate.CandidateId,
            CalibrationOutcomeRepository.DelayedIntradaySwingDefinitionId,
            CalibrationOutcomeMaturityState.NoEntry,
            JsonSerializer.Serialize(noEntryOutcome, jsonOptions),
            CalibrationAuditState.Valid))
            delayedNoEntry++;
        continue;
    }

    if (entryReadiness.EntrySession is null)
    {
        delayedPendingCount++;
        continue;
    }

    DateTime entrySession = entryReadiness.EntrySession.Value.Date;
    DailyBar entryBar = symbolBars.Single(bar => bar.Date.Date == entrySession);
    DailyBar xiuEntryBar = xiuBars.Single(bar => bar.Date.Date == entrySession);
    DateTime entryUtc = SessionOpenUtc(entrySession);
    IReadOnlyList<StoredIntradayOutcomeBar> storedPolicy = (await GetIntradayBarsAsync(candidate.Symbol, 15))
        .Where(bar => bar.EventUtc >= entryUtc)
        .ToList();
    IReadOnlyList<StoredIntradayOutcomeBar> storedFive = (await GetIntradayBarsAsync(candidate.Symbol, 5))
        .Where(bar => bar.EventUtc >= entryUtc)
        .ToList();
    IReadOnlyList<StoredIntradayOutcomeBar> storedXiuFive = (await GetIntradayBarsAsync("XIU", 5))
        .Where(bar => bar.EventUtc >= entryUtc)
        .ToList();

    var sessionOrdinals = xiuBars
        .Select(bar => bar.Date.Date)
        .Distinct()
        .Where(date => date >= entrySession)
        .OrderBy(date => date)
        .Select((date, index) => (date, ordinal: index + 1))
        .ToDictionary(item => item.date, item => item.ordinal);
    List<DelayedIntradayBar> policyBars = storedPolicy
        .Select(bar =>
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(bar.EventUtc, torontoTimeZone);
            return sessionOrdinals.TryGetValue(local.Date, out int ordinal)
                ? new DelayedIntradayBar(
                    bar.EventUtc,
                    bar.EventUtc.AddMinutes(15),
                    bar.FirstReceivedUtc,
                    ordinal,
                    local.TimeOfDay == new TimeSpan(15, 45, 0),
                    bar.Open,
                    bar.High,
                    bar.Low,
                    bar.Close,
                    bar.Volume)
                : null;
        })
        .Where(bar => bar is not null)
        .Cast<DelayedIntradayBar>()
        .OrderBy(bar => bar.StartUtc)
        .ToList();

    IReadOnlyList<FreshDelphiBreakoutEvidenceSnapshot> timeline = policyBars.Count == 0
        ? Array.Empty<FreshDelphiBreakoutEvidenceSnapshot>()
        : await calibrationEvidenceRepository.GetValidOfficialBreakoutTimelineAsync(
            candidate.Symbol,
            entryUtc,
            policyBars.Max(bar => bar.StartUtc));
    IReadOnlyDictionary<DateTime, DelayedIntradayBreakoutEvidence?> breakoutByBar = policyBars
        .ToDictionary(
            bar => bar.StartUtc,
            bar => FreshDelphiBreakoutEvidenceResolver.Resolve(timeline, entryUtc, bar.StartUtc));

    DelayedIntradayOutcomeAssessment assessment = DelayedIntradayOutcomeCalculator.Assess(
        (decimal)entryBar.Open,
        (decimal)xiuEntryBar.Open,
        entryUtc,
        policyBars,
        storedFive.Select(ToOhlcvBar).ToList(),
        storedXiuFive.Select(ToOhlcvBar).ToList(),
        breakoutByBar);
    if (assessment.State == DelayedIntradayOutcomeState.Pending)
    {
        delayedPendingCount++;
        continue;
    }

    if (assessment.State == DelayedIntradayOutcomeState.Invalid)
    {
        var invalidOutcome = new InvalidDelayedIntradayOutcomeV1(
            DelayedIntradayOutcomeCalculator.SchemaVersion,
            IntradayEvidenceVersions.Policy,
            entryUtc,
            assessment.FirstInvalidEventUtc ?? entryUtc,
            assessment.ReasonCode ?? "InvalidDelayedIntradayEvidence");
        if (await outcomes.InsertOutcomeAsync(
            candidate.CandidateId,
            CalibrationOutcomeRepository.DelayedIntradaySwingDefinitionId,
            CalibrationOutcomeMaturityState.Matured,
            JsonSerializer.Serialize(invalidOutcome, jsonOptions),
            CalibrationAuditState.Invalid))
            delayedInvalid++;
        continue;
    }

    if (await outcomes.InsertOutcomeAsync(
        candidate.CandidateId,
        CalibrationOutcomeRepository.DelayedIntradaySwingDefinitionId,
        CalibrationOutcomeMaturityState.Matured,
        JsonSerializer.Serialize(assessment.Outcome!, jsonOptions),
        CalibrationAuditState.Valid))
        delayedMatured++;
}

Console.WriteLine("\n=== Three-session swing mark-to-market ===");
Console.WriteLine($"Published candidates inspected: {swing.Inspected:N0}");
Console.WriteLine($"Matured outcomes written:      {swing.Matured:N0}");
Console.WriteLine($"No-entry outcomes written:     {swing.NoEntry:N0}");
Console.WriteLine($"Not yet mature:                {swing.Pending:N0}");
Console.WriteLine($"Invalid outcomes written:      {swing.Invalid:N0}");

Console.WriteLine("\n=== Three-session swing MFE/MAE excursions ===");
Console.WriteLine($"Published candidates inspected: {excursions.Inspected:N0}");
Console.WriteLine($"Matured outcomes written:      {excursions.Matured:N0}");
Console.WriteLine($"No-entry outcomes written:     {excursions.NoEntry:N0}");
Console.WriteLine($"Not yet mature:                {excursions.Pending:N0}");
Console.WriteLine($"Invalid outcomes written:      {excursions.Invalid:N0}");

Console.WriteLine("\n=== Delayed intraday swing exits ===");
Console.WriteLine($"Published candidates inspected: {delayedPending.Count:N0}");
Console.WriteLine($"Matured outcomes written:      {delayedMatured:N0}");
Console.WriteLine($"No-entry outcomes written:     {delayedNoEntry:N0}");
Console.WriteLine($"Not yet mature:                {delayedPendingCount:N0}");
Console.WriteLine($"Invalid outcomes written:      {delayedInvalid:N0}");
invalidWritten += delayedInvalid;

var coverageRows = await outcomes.GetOutcomeCoverageAsync();
Console.WriteLine("\n=== Outcome coverage scorecard ===");
foreach (var counts in coverageRows)
{
    var scorecard = CalibrationCoverageCalculator.Build(counts);
    Console.WriteLine($"{counts.DefinitionName} v{counts.DefinitionVersion} ({counts.DefinitionKind})");
    Console.WriteLine($"  Cohorts:    {counts.MaturedCohorts:N0}/{counts.TotalCohorts:N0} matured ({counts.OfficialRuns:N0} official runs)");
    Console.WriteLine($"  Candidates: {counts.ExpectedCandidates:N0} expected | {counts.ValidOutcomes:N0} valid | {counts.DegradedOutcomes:N0} degraded | {counts.InvalidOutcomes:N0} invalid | {counts.PendingOutcomes:N0} pending");
    Console.WriteLine($"  Coverage:   {scorecard.UsableCoverage:P1} usable | {scorecard.CompletionCoverage:P1} complete | primary score {(scorecard.PrimaryScoreAvailable ? "available" : "BLOCKED")}");
    Console.WriteLine($"  State:      {scorecard.State}");
}

var predictionEvidence = await outcomes.GetOfficialPredictionScorecardEvidenceAsync();
OfficialPredictionScorecard predictionScorecard =
    OfficialPredictionScorecardCalculator.Build(predictionEvidence);
Console.WriteLine("\n=== Advanced official prediction scorecard ===");
Console.WriteLine($"Definition: {predictionScorecard.Definition.DefinitionName} v{predictionScorecard.Definition.DefinitionVersion}");
Console.WriteLine($"Coverage:   {predictionScorecard.Coverage.UsableCoverage:P1} usable | " +
                  $"{predictionScorecard.Coverage.CompletionCoverage:P1} complete | " +
                  $"performance {(predictionScorecard.Coverage.PrimaryScoreAvailable ? "available" : "BLOCKED")}");
Console.WriteLine($"Evidence:   {predictionScorecard.Coverage.Counts.ExpectedCandidates:N0} candidates | " +
                  $"{predictionScorecard.Coverage.Counts.MaturedCohorts:N0}/{predictionScorecard.Coverage.Counts.TotalCohorts:N0} matured cohorts | " +
                  $"{predictionScorecard.Coverage.Counts.OfficialRuns:N0} official runs");

Console.WriteLine("\nProbability calibration (lower Brier/ECE is better; higher AUC/lift is better):");
foreach (ProbabilityCalibrationReport model in predictionScorecard.Models)
{
    Console.WriteLine($"  {model.TaskType}: {model.UsablePredictions:N0}/{model.ExpectedCandidates:N0} usable predictions " +
                      $"({model.PredictionCoverage:P1})");
    if (!model.MetricsAvailable)
    {
        Console.WriteLine("    Metrics BLOCKED by outcome or model-specific coverage.");
        continue;
    }

    Console.WriteLine($"    Brier {model.BrierScore:0.0000} | AUC {(model.AreaUnderRocCurve.HasValue ? model.AreaUnderRocCurve.Value.ToString("0.0000") : "unsupported: one class")} | " +
                      $"ECE {model.ExpectedCalibrationError:0.0000} | top-decile event lift {model.TopDecileEventLift:+0.0%;-0.0%;0.0%}");
    foreach (ProbabilityReliabilityBucket bucket in model.Reliability)
    {
        Console.WriteLine($"    P{bucket.LowerBound:P0}-{bucket.UpperBound:P0}: predicted {bucket.MeanProbability:P1} | " +
                          $"observed {bucket.ObservedEventRate:P1} | n={bucket.Observations:N0} | cohorts={bucket.ContributingCohorts:N0}");
    }
}

Console.WriteLine("\nLens ranking quality (positive IC/lift means higher-ranked eligible candidates did better):");
foreach (LensRankPerformanceReport lens in predictionScorecard.Lenses)
{
    Console.WriteLine($"  {lens.Lens}: {lens.EligibleObservations:N0} eligible observations | " +
                      $"{lens.ContributingCohorts:N0} cohorts | " +
                      $"rank IC {(lens.SpearmanRankInformationCoefficient.HasValue ? lens.SpearmanRankInformationCoefficient.Value.ToString("+0.000;-0.000;0.000") : "BLOCKED/unsupported")}");
    foreach (LensRankSelectionReport selection in lens.Selections)
    {
        Console.WriteLine($"    {selection.Selection,-10} return {selection.MeanReturn10:+0.00%;-0.00%;0.00%} | " +
                          $"excess {selection.MeanExcessReturn10:+0.00%;-0.00%;0.00%} | " +
                          $"lift {selection.ReturnLiftVersusEligibleBaseline:+0.00%;-0.00%;0.00%}");
    }
}

if (predictionScorecard.Slices.Count > 0)
{
    Console.WriteLine("\nDiagnostic slices (descriptive only; never changes a weight automatically):");
    foreach (PredictionSliceReport slice in predictionScorecard.Slices)
    {
        Console.WriteLine($"  {slice.Dimension}/{slice.Value}: n={slice.Observations:N0} | cohorts={slice.ContributingCohorts:N0} | " +
                          $"return {slice.MeanReturn10:+0.00%;-0.00%;0.00%} | excess {slice.MeanExcessReturn10:+0.00%;-0.00%;0.00%} | " +
                          $"up/down/breakout/vol {slice.UpEventRate:P0}/{slice.DownEventRate:P0}/{slice.BreakoutEventRate:P0}/{slice.VolExpansionEventRate:P0}");
    }
}

if (scorecardCsvDirectory is not null)
{
    IReadOnlyList<OfficialPredictionScorecardCsvArtifact> artifacts =
        OfficialPredictionScorecardCsv.Build(predictionScorecard);
    string[] paths = artifacts
        .Select(artifact => Path.Combine(scorecardCsvDirectory, artifact.FileName))
        .ToArray();
    string? existing = paths.FirstOrDefault(File.Exists);
    if (existing is not null)
        throw new IOException($"Refusing to overwrite an existing scorecard export: {existing}");

    Directory.CreateDirectory(scorecardCsvDirectory);
    for (int index = 0; index < artifacts.Count; index++)
    {
        await using var stream = new FileStream(
            paths[index],
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(artifacts[index].Content);
    }
    Console.WriteLine($"\nVersioned scorecard CSV files written to {scorecardCsvDirectory}");
}

var lensEvidence = await outcomes.GetLensTradeabilityEvidenceAsync();
var lensReports = LensTradeabilityReportCalculator.BuildReports(lensEvidence);
Console.WriteLine("\n=== Continuation vs Breakout tradeability ===");
foreach (var report in lensReports)
{
    LensTradeabilityCoverage coverage = report.Coverage;
    Console.WriteLine(report.Lens);
    Console.WriteLine($"  Cohorts:        {coverage.MaturedCohorts:N0}/{coverage.TotalCohorts:N0} matured ({coverage.OfficialRuns:N0} official runs)");
    Console.WriteLine($"  Recommendations:{coverage.ExpectedRecommendations,8:N0} expected | {coverage.EnteredRecommendations:N0} entered | {coverage.NoEntryRecommendations:N0} no-entry | {coverage.InvalidRecommendations:N0} invalid | {coverage.PendingRecommendations:N0} pending");
    Console.WriteLine($"  Coverage:       {coverage.UsableCoverage:P1} usable | {coverage.CompletionCoverage:P1} complete | performance {(coverage.PrimaryScoreAvailable ? "available" : "BLOCKED")}");
    Console.WriteLine($"  State:          {coverage.State}");
    if (!coverage.PrimaryScoreAvailable)
        continue;

    Console.WriteLine($"  No-entry rate:  {report.NoEntryRate:P1} (cohort-weighted)");
    foreach (var horizon in report.Horizons)
    {
        if (horizon.MeanNetReturn is null)
        {
            Console.WriteLine($"  Session {horizon.Sessions}: no entered recommendations");
            continue;
        }

        Console.WriteLine($"  Session {horizon.Sessions}: net {horizon.MeanNetReturn:P2} | profitable {horizon.ProfitableRate:P1} | net excess {horizon.MeanNetExcessReturn:P2} | MFE {horizon.MeanMfeReturn:P2} | MAE {horizon.MeanMaeReturn:P2}");
        Console.WriteLine($"             mean extreme session: MFE {horizon.MeanMfeSessionOrdinal:0.00} | MAE {horizon.MeanMaeSessionOrdinal:0.00} ({horizon.ContributingCohorts:N0} cohorts)");
    }
}

IReadOnlyList<DelayedIntradayLensReport> delayedLensReports =
    DelayedIntradayLensReportCalculator.Build(await outcomes.GetDelayedIntradayLensEvidenceAsync());
Console.WriteLine("\n=== Delayed intraday exit results by lens ===");
foreach (DelayedIntradayLensReport report in delayedLensReports)
{
    Console.WriteLine($"{report.Lens}: {report.MaturedRecommendations:N0} matured | " +
                      $"{report.NoEntryRecommendations:N0} no-entry | {report.InvalidRecommendations:N0} invalid | " +
                      $"{report.PendingRecommendations:N0} pending | usable {report.UsableCoverage:P1}");
    if (!report.MetricsAvailable)
    {
        Console.WriteLine("  Metrics BLOCKED by the 95% usable-coverage floor.");
        continue;
    }
    Console.WriteLine($"  Raw gross {report.MeanGrossReturn:+0.00%;-0.00%;0.00%} | " +
                      $"25-bps sensitivity {report.MeanConservativeNetReturn:+0.00%;-0.00%;0.00%}");
    Console.WriteLine($"  Raw excess {report.MeanGrossExcessReturn:+0.00%;-0.00%;0.00%} | " +
                      $"sensitivity excess {report.MeanConservativeNetExcessReturn:+0.00%;-0.00%;0.00%} | " +
                      $"{report.ContributingCohorts:N0} cohorts");
}

Environment.ExitCode = invalidWritten > 0 ? 2 : 0;

static OhlcvBar ToOhlcvBar(StoredIntradayOutcomeBar bar) =>
    new(bar.EventUtc, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume);

static DateTime SessionOpenUtc(DateTime sessionDate)
{
    DateTime local = DateTime.SpecifyKind(
        sessionDate.Date.AddHours(9).AddMinutes(30),
        DateTimeKind.Unspecified);
    return TimeZoneInfo.ConvertTimeToUtc(local, ResolveTorontoTimeZone());
}

static TimeZoneInfo ResolveTorontoTimeZone()
{
    foreach (string id in new[] { "America/Toronto", "Eastern Standard Time" })
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { }
    }
    throw new TimeZoneNotFoundException(
        "Neither America/Toronto nor Eastern Standard Time is available.");
}
