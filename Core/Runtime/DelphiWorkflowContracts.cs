#nullable enable

using Core.Calibration;
using Core.Db;
using System;
using System.Collections.Generic;

namespace Core.Runtime;

public sealed record DelphiWorkflowOptions(
    CalibrationRunPurpose Purpose = CalibrationRunPurpose.OfficialPaper,
    DateTime? RecommendationDate = null,
    decimal AvailableCapital = 700m,
    int MaxSymbolsToScan = 500,
    int TopPicksToSave = 25,
    bool SaveToDatabase = true)
{
    public void Validate()
    {
        if (Purpose is not (
            CalibrationRunPurpose.OfficialPaper or
            CalibrationRunPurpose.ExploratoryReplay))
        {
            throw new ArgumentOutOfRangeException(nameof(Purpose));
        }
        if (AvailableCapital <= 0m)
            throw new ArgumentOutOfRangeException(nameof(AvailableCapital));
        if (MaxSymbolsToScan is < 1 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(MaxSymbolsToScan));
        if (TopPicksToSave is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(TopPicksToSave));
    }
}

public sealed record DelphiWorkflowRunResult(
    bool Succeeded,
    string Status,
    CalibrationRunPurpose Purpose,
    DateTime RecommendationDate,
    DateTime? MarketDataAsOf,
    DateTime StartedUtc,
    DateTime CompletedUtc,
    int ContinuationPickCount,
    int BreakoutPickCount,
    string? DiagnosticReport,
    string? SummaryReport)
{
    public static DelphiWorkflowRunResult Failed(
        DelphiWorkflowOptions options,
        DateTime startedUtc,
        DateTime recommendationDate,
        string status,
        DateTime? marketDataAsOf = null) =>
        new(
            false,
            status,
            options.Purpose,
            recommendationDate,
            marketDataAsOf,
            startedUtc,
            DateTime.UtcNow,
            0,
            0,
            null,
            null);
}

public sealed record DelphiPublishedRecommendations(
    DateTime PickDate,
    DateTime LatestCreatedUtc,
    IReadOnlyList<DailyPickInfo> Continuation,
    IReadOnlyList<DailyPickInfo> Breakout);
