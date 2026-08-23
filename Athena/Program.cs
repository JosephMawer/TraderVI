using Core.Calibration;
using Core.Db;
using System;
using System.Linq;
using System.Text.Json;

Console.WriteLine("=== Athena: deterministic calibration evaluator ===");
Console.WriteLine("Local SQL only; no external market services.\n");

var outcomes = new CalibrationOutcomeRepository();
await outcomes.EnsurePredictionDefinitionsAsync();
var quoteRepository = new QuoteRepository();
var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

var labelPending = await outcomes.GetPendingOfficialCandidatesAsync(CalibrationOutcomeRepository.PredictionLabel10DefinitionId);
var pathPending = await outcomes.GetPendingOfficialCandidatesAsync(CalibrationOutcomeRepository.PredictionPath20DefinitionId);
var pathIds = pathPending.Select(x => x.CandidateId).ToHashSet();
var candidates = labelPending.Concat(pathPending)
    .GroupBy(x => x.CandidateId)
    .Select(x => x.First())
    .ToList();

var xiuBars = await quoteRepository.GetDailyBarsAsync("XIU");
int labelsWritten = 0, pathsWritten = 0, immature = 0, invalid = 0;

foreach (var candidate in candidates)
{
    var symbolBars = await quoteRepository.GetDailyBarsAsync(candidate.Symbol);
    int observationIndex = symbolBars.FindIndex(x => x.Date.Date == candidate.ObservationDate.Date);
    int xiuObservationIndex = xiuBars.FindIndex(x => x.Date.Date == candidate.ObservationDate.Date);
    if (observationIndex < 0 || xiuObservationIndex < 0)
    {
        invalid++;
        continue;
    }

    var window = symbolBars.Take(observationIndex + 1).ToList();
    var future = symbolBars.Skip(observationIndex + 1).ToList();
    var futureXiu = xiuBars.Skip(xiuObservationIndex + 1).ToList();
    var result = PredictionOutcomeCalculator.Calculate(window, future, xiuBars[xiuObservationIndex].Close, futureXiu);

    if (labelPending.Any(x => x.CandidateId == candidate.CandidateId) && result.MaturedSessions >= PredictionOutcomeCalculator.LabelHorizon)
    {
        await outcomes.InsertMaturedOutcomeAsync(candidate.CandidateId, CalibrationOutcomeRepository.PredictionLabel10DefinitionId,
            JsonSerializer.Serialize(result, jsonOptions), CalibrationAuditState.Valid);
        labelsWritten++;
    }

    if (pathIds.Contains(candidate.CandidateId) && result.MaturedSessions >= PredictionOutcomeCalculator.PathHorizon)
    {
        await outcomes.InsertMaturedOutcomeAsync(candidate.CandidateId, CalibrationOutcomeRepository.PredictionPath20DefinitionId,
            JsonSerializer.Serialize(result, jsonOptions), CalibrationAuditState.Valid);
        pathsWritten++;
    }

    if (result.MaturedSessions < PredictionOutcomeCalculator.LabelHorizon) immature++;
}

Console.WriteLine($"Pending candidates inspected: {candidates.Count:N0}");
Console.WriteLine($"10-session label outcomes:   {labelsWritten:N0}");
Console.WriteLine($"20-session path outcomes:    {pathsWritten:N0}");
Console.WriteLine($"Not yet 10-session mature:   {immature:N0}");
Console.WriteLine($"Invalid session joins:       {invalid:N0}");

Environment.ExitCode = invalid > 0 ? 2 : 0;
