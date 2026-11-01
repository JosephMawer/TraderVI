using System;
using System.Collections.Generic;

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

public static class PredictionOutcomeContract
{
    public const int SchemaVersion = 1;
    public const int LabelHorizon = 10;
    public const int PathHorizon = 20;
}
