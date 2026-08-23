using Core.ML;
using Core.ML.Engine.Profit;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Calibration;

public sealed record PredictionEventOutcome(
    string TaskType,
    string Labeler,
    bool EventOccurred);

public sealed record PredictionOutcomeV1(
    int SchemaVersion,
    DateTime ObservationDate,
    int MaturedSessions,
    double? Return1,
    double? Return5,
    double? Return10,
    double? Return20,
    double? XiuReturn10,
    double? ExcessReturn10,
    IReadOnlyList<PredictionEventOutcome> Events);

public static class PredictionOutcomeCalculator
{
    public const int SchemaVersion = 1;
    public const int LabelHorizon = 10;
    public const int PathHorizon = 20;

    public static PredictionOutcomeV1 Calculate(
        IReadOnlyList<DailyBar> observationWindow,
        IReadOnlyList<DailyBar> futureBars,
        float xiuObservationClose,
        IReadOnlyList<DailyBar> futureXiuBars)
    {
        if (observationWindow.Count == 0) throw new ArgumentException("Observation window is required.");
        if (observationWindow[^1].Close <= 0) throw new ArgumentException("Observation close must be positive.");

        DateTime observationDate = observationWindow[^1].Date.Date;
        var xiu = futureXiuBars.Where(x => x.Date.Date > observationDate).OrderBy(x => x.Date).ToList();
        var futureByDate = futureBars
            .Where(x => x.Date.Date > observationDate)
            .GroupBy(x => x.Date.Date)
            .ToDictionary(x => x.Key, x => x.Single());
        var future = new List<DailyBar>();
        foreach (var benchmarkSession in xiu)
        {
            if (!futureByDate.TryGetValue(benchmarkSession.Date.Date, out var symbolBar)) break;
            future.Add(symbolBar);
        }
        int matured = future.Count;
        float observationClose = observationWindow[^1].Close;

        double? ReturnAt(int horizon) => matured >= horizon && future[horizon - 1].Close > 0
            ? future[horizon - 1].Close / observationClose - 1.0
            : null;

        double? xiuReturn10 = xiuObservationClose > 0 && xiu.Count >= LabelHorizon && xiu[LabelHorizon - 1].Close > 0
            ? xiu[LabelHorizon - 1].Close / xiuObservationClose - 1.0
            : null;
        double? return10 = ReturnAt(10);

        var events = new List<PredictionEventOutcome>();
        if (matured >= LabelHorizon)
        {
            foreach (var definition in ProfitModelRegistry.All)
            {
                if (definition.HorizonBars != LabelHorizon) continue;
                var label = definition.Labeler.ComputeLabel(observationWindow, future);
                if (!label.IsValid)
                    throw new InvalidOperationException($"Production labeler {definition.TaskType} rejected a mature 10-session window.");
                events.Add(new PredictionEventOutcome(
                    definition.TaskType,
                    definition.Labeler.Name,
                    label.ThreeWayClass == ThreeWayLabel.Buy));
            }
        }

        return new PredictionOutcomeV1(
            SchemaVersion,
            observationDate,
            matured,
            ReturnAt(1),
            ReturnAt(5),
            return10,
            ReturnAt(20),
            xiuReturn10,
            return10.HasValue && xiuReturn10.HasValue ? return10 - xiuReturn10 : null,
            events);
    }
}
