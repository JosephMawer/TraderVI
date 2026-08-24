using Core.Calibration;
using Core.Db;
using Core.ML;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

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
Console.WriteLine($"Invalid outcomes written:    {invalidWritten:N0}");

var swingPending = await outcomes.GetPendingPublishedCandidatesAsync(
    CalibrationOutcomeRepository.SwingMarkToMarket3DefinitionId);
int swingWritten = 0, swingNoEntryWritten = 0, swingImmature = 0;

foreach (var candidate in swingPending)
{
    var symbolBars = await GetBarsAsync(candidate.Symbol);
    var readiness = SwingMarkToMarketOutcomeCalculator.AssessReadiness(
        candidate.ObservationDate,
        candidate.RunStartedUtc,
        symbolBars,
        xiuBars);

    if (readiness.State == SwingOutcomeReadinessState.Pending)
    {
        swingImmature++;
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
            CalibrationOutcomeRepository.SwingMarkToMarket3DefinitionId,
            CalibrationOutcomeMaturityState.Matured,
            JsonSerializer.Serialize(invalidOutcome, jsonOptions),
            CalibrationAuditState.Invalid))
            invalidWritten++;
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
            CalibrationOutcomeRepository.SwingMarkToMarket3DefinitionId,
            CalibrationOutcomeMaturityState.NoEntry,
            JsonSerializer.Serialize(noEntryOutcome, jsonOptions),
            CalibrationAuditState.Valid))
            swingNoEntryWritten++;
        continue;
    }

    var result = SwingMarkToMarketOutcomeCalculator.Calculate(
        candidate.ObservationDate,
        candidate.RunStartedUtc,
        symbolBars,
        xiuBars);
    if (await outcomes.InsertOutcomeAsync(
        candidate.CandidateId,
        CalibrationOutcomeRepository.SwingMarkToMarket3DefinitionId,
        CalibrationOutcomeMaturityState.Matured,
        JsonSerializer.Serialize(result, jsonOptions),
        CalibrationAuditState.Valid))
        swingWritten++;
}

Console.WriteLine("\n=== Three-session swing mark-to-market ===");
Console.WriteLine($"Published candidates inspected: {swingPending.Count:N0}");
Console.WriteLine($"Matured outcomes written:      {swingWritten:N0}");
Console.WriteLine($"No-entry outcomes written:     {swingNoEntryWritten:N0}");
Console.WriteLine($"Not yet mature:                {swingImmature:N0}");

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

Environment.ExitCode = invalidWritten > 0 ? 2 : 0;
