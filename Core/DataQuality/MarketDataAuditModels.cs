#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.DataQuality;

public enum AuditSeverity
{
    Warning = 1,
    Error = 2
}

public sealed record AuditFinding(
    AuditSeverity Severity,
    string Code,
    string? Symbol,
    string Message);

public sealed record AuditedSymbol(
    string Symbol,
    string? LongName,
    string? ShortName,
    string SecurityType,
    bool IsActive,
    bool IsLeveragedOrInverseEtp,
    long BarCount,
    DateTime? FirstBarDate,
    DateTime? LatestBarDate,
    long InvalidOhlcBars,
    long NegativeVolumeBars);

public sealed record AuditedSectorMapping(
    string Symbol,
    string Sector,
    string? Industry,
    string? SectorIndexSymbol,
    DateTime LastUpdated);

public sealed record DuplicateDailyBarSummary(
    string Symbol,
    int DuplicateDates,
    long ExtraRows);

public sealed record OrphanDailyBarSummary(
    string Symbol,
    long BarCount,
    DateTime LatestBarDate);

public sealed record SectorIndexAuditSummary(
    string Symbol,
    long BarCount,
    DateTime LatestDate,
    long InvalidPriceRows);

public sealed record MarketDataAuditSnapshot(
    IReadOnlyList<DateTime> BenchmarkSessions,
    IReadOnlyList<AuditedSymbol> Symbols,
    IReadOnlyList<AuditedSectorMapping> SectorMappings,
    IReadOnlyList<DuplicateDailyBarSummary> DuplicateDailyBars,
    IReadOnlyList<OrphanDailyBarSummary> OrphanDailyBars,
    IReadOnlyList<SectorIndexAuditSummary> SectorIndices);

public sealed record MarketDataAuditOptions(
    int StaleWarningSessions = 2,
    int StaleErrorSessions = 5,
    int SectorMappingMaxAgeDays = 14)
{
    public void Validate()
    {
        if (StaleWarningSessions < 1)
            throw new ArgumentOutOfRangeException(nameof(StaleWarningSessions));
        if (StaleErrorSessions < StaleWarningSessions)
            throw new ArgumentOutOfRangeException(nameof(StaleErrorSessions));
        if (SectorMappingMaxAgeDays < 1)
            throw new ArgumentOutOfRangeException(nameof(SectorMappingMaxAgeDays));
    }
}

public sealed record MarketDataAuditReport(
    DateTime? MarketDataAsOf,
    int TotalSymbols,
    int ActiveSymbols,
    int ActiveStocks,
    int ActiveEtfs,
    IReadOnlyList<AuditFinding> Findings)
{
    public int ErrorCount => Findings.Count(f => f.Severity == AuditSeverity.Error);
    public int WarningCount => Findings.Count(f => f.Severity == AuditSeverity.Warning);
}
