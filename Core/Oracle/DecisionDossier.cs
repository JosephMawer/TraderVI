using System;
using System.Collections.Generic;
using Core.Trader;
using Core.Trader.Gates;

namespace Core.Oracle;

/// <summary>
/// Structured, audit-grade snapshot of a single trading decision.
///
/// The dossier is the **contract** between the deterministic pipeline and the
/// downstream LLM layer (see <c>Docs/oracle-rules.md</c>). Every numeric value
/// the LLM is allowed to cite must live here — anything not in the dossier is
/// treated as hallucination.
///
/// This type is intentionally pure-data and JSON-serializable. It is produced
/// by <see cref="DecisionDossierBuilder"/> from a <see cref="RankedPick"/>
/// plus market context, persisted to <c>[dbo].[DecisionDossier]</c>, and read
/// back by Oracle phases 2+.
/// </summary>
/// <param name="SchemaVersion">
/// Bump on every breaking change to the shape. Prompt templates declare the
/// minimum version they accept (Rule R8).
/// </param>
public sealed record DecisionDossier(
    int SchemaVersion,
    DateTime PickDate,
    Guid PickId,
    string Symbol,
    int Rank,
    DecisionSummary Decision,
    MarketContext Market,
    MlSignalBreakdown MlSignals,
    GranvilleBreakdown? Granville,
    RelativeStrengthBreakdown? RelativeStrength,
    SizingSnapshot? Sizing,
    IReadOnlyList<GateTraceRecord> Gates,
    StrategyVersionRef? Strategy
)
{
    /// <summary>Current schema version. Bump on every breaking change.</summary>
    public const int CurrentSchemaVersion = 1;
}

public sealed record DecisionSummary(
    string Direction,
    double CompositeScore,
    double Confidence,
    double DirectionProbability,
    double DownProbability,
    double DirectionEdge,
    double ExpectedReturn,
    decimal LastPrice
);

public sealed record MarketContext(
    bool? IsBenchmarkUptrend,
    bool? IsBenchmark20dPositive,
    bool? IsVolatilityNormal,
    double? BenchmarkReturn20d,
    double? BenchmarkMA50,
    double? BenchmarkMA200,
    bool? IsSpyUptrend,
    bool? IsSpy20dPositive,
    double? BreadthScore,
    double? BreadthVetoThreshold
);

public sealed record MlSignalBreakdown(
    double BreakoutProb,
    double UpProb,
    double DownProb,
    double DirectionEdge,
    double VolExpansionProb,
    double RelStrengthProb,
    IReadOnlyList<SignalContribution> Signals
);

public sealed record SignalContribution(
    string Name,
    double Score,
    string? Hint,
    string? Notes
);

public sealed record GranvilleBreakdown(
    int NetPoints,
    double CompositeAdjustment,
    IReadOnlyList<GranvilleIndicatorRecord> Indicators
);

public sealed record GranvilleIndicatorRecord(
    int IndicatorNumber,
    string Category,
    string Name,
    string Signal,
    int GranvillePoints,
    string Description
);

public sealed record RelativeStrengthBreakdown(
    double? CompositeScore,
    double? Return5d,
    double? Return10d,
    double? Return20d,
    double? Return60d,
    double? Z5d,
    double? Z10d,
    double? Z20d,
    double? Z60d,
    string? SectorSymbol
);

public sealed record SizingSnapshot(
    decimal? SuggestedSize,
    double? AllocationPercent,
    int? Shares,
    string? Reason
);

public sealed record GateTraceRecord(
    string GateName,
    bool Passed,
    string? Reason
)
{
    public static GateTraceRecord From(GateTraceEntry entry)
        => new(entry.GateName, entry.Passed, entry.Reason);
}

public sealed record StrategyVersionRef(
    Guid VersionId,
    string VersionName
);
