#nullable enable

using Core.TMX;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.DataQuality;

public static class MarketDataAuditor
{
    private static readonly HashSet<string> KnownSecurityTypes =
        new(StringComparer.OrdinalIgnoreCase) { "Stock", "ETF" };

    public static MarketDataAuditReport Analyze(
        MarketDataAuditSnapshot snapshot,
        MarketDataAuditOptions? options = null,
        DateTime? utcNow = null)
    {
        options ??= new MarketDataAuditOptions();
        options.Validate();

        DateTime now = (utcNow ?? DateTime.UtcNow).Date;
        DateTime comparisonTime = utcNow ?? DateTime.UtcNow;
        DateTime? marketDataAsOf = snapshot.BenchmarkSessions.Count == 0
            ? null
            : snapshot.BenchmarkSessions.Max(d => d.Date);

        var findings = new List<AuditFinding>();
        var symbolsByName = snapshot.Symbols.ToDictionary(
            s => s.Symbol,
            StringComparer.OrdinalIgnoreCase);
        var mappingsBySymbol = snapshot.SectorMappings.ToDictionary(
            m => m.Symbol,
            StringComparer.OrdinalIgnoreCase);
        var sectorIndicesBySymbol = snapshot.SectorIndices.ToDictionary(
            s => s.Symbol,
            StringComparer.OrdinalIgnoreCase);
        var referencedSectorIndices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (marketDataAsOf is null)
        {
            findings.Add(new AuditFinding(
                AuditSeverity.Error,
                "BENCHMARK.XIU_HISTORY_MISSING",
                "XIU",
                "No XIU sessions exist, so trading-session freshness cannot be measured."));
        }

        foreach (AuditedSymbol symbol in snapshot.Symbols)
        {
            if (string.IsNullOrWhiteSpace(symbol.Symbol))
            {
                findings.Add(new AuditFinding(
                    AuditSeverity.Error,
                    "SYMBOL.EMPTY",
                    null,
                    "dbo.Symbols contains an active or historical row with an empty Symbol key."));
                continue;
            }

            if (!KnownSecurityTypes.Contains(symbol.SecurityType))
            {
                findings.Add(new AuditFinding(
                    AuditSeverity.Error,
                    "SYMBOL.SECURITY_TYPE_UNKNOWN",
                    symbol.Symbol,
                    $"SecurityType is '{symbol.SecurityType}', expected Stock or ETF."));
            }

            string combinedName = $"{symbol.LongName} {symbol.ShortName}";

            if (symbol.SecurityType.Equals("Stock", StringComparison.OrdinalIgnoreCase)
                && SecurityNameHeuristics.LooksLikeFund(combinedName))
            {
                findings.Add(new AuditFinding(
                    AuditSeverity.Warning,
                    "SYMBOL.STOCK_LOOKS_LIKE_FUND",
                    symbol.Symbol,
                    "Name looks fund-like, but SecurityType is Stock; verify against an official issuer or exchange source."));
            }

            if (symbol.IsActive
                && symbol.SecurityType.Equals("Stock", StringComparison.OrdinalIgnoreCase)
                && !symbol.IsLeveragedOrInverseEtp
                && SecurityNameHeuristics.LooksLeveragedOrInverse(combinedName))
            {
                findings.Add(new AuditFinding(
                    AuditSeverity.Error,
                    "SYMBOL.LEVERAGED_FLAG_MISSING",
                    symbol.Symbol,
                    "Name looks leveraged/inverse, but IsLeveragedOrInverseEtp is false."));
            }

            if (symbol.InvalidOhlcBars > 0)
            {
                findings.Add(new AuditFinding(
                    AuditSeverity.Error,
                    "BARS.INVALID_OHLC",
                    symbol.Symbol,
                    $"{symbol.InvalidOhlcBars:N0} bar(s) violate positive-price or OHLC range rules."));
            }

            if (symbol.NegativeVolumeBars > 0)
            {
                findings.Add(new AuditFinding(
                    AuditSeverity.Error,
                    "BARS.NEGATIVE_VOLUME",
                    symbol.Symbol,
                    $"{symbol.NegativeVolumeBars:N0} bar(s) have negative volume."));
            }

            if (!symbol.IsActive)
                continue;

            if (symbol.BarCount == 0 || symbol.LatestBarDate is null)
            {
                findings.Add(new AuditFinding(
                    AuditSeverity.Error,
                    "BARS.ACTIVE_SYMBOL_HAS_NO_HISTORY",
                    symbol.Symbol,
                    "Active symbol has no DailyBars history."));
            }
            else
            {
                if (symbol.LatestBarDate.Value.Date > now)
                {
                    findings.Add(new AuditFinding(
                        AuditSeverity.Error,
                        "BARS.FUTURE_DATE",
                        symbol.Symbol,
                        $"Latest bar {symbol.LatestBarDate:yyyy-MM-dd} is later than audit date {now:yyyy-MM-dd}."));
                }

                if (marketDataAsOf.HasValue)
                {
                    int sessionsBehind = MarketDataFreshness.CountSessionsBehind(
                        symbol.LatestBarDate.Value,
                        snapshot.BenchmarkSessions);

                    if (sessionsBehind >= options.StaleErrorSessions)
                    {
                        findings.Add(new AuditFinding(
                            AuditSeverity.Error,
                            "BARS.ACTIVE_SYMBOL_STALE",
                            symbol.Symbol,
                            $"Latest bar {symbol.LatestBarDate:yyyy-MM-dd} is {sessionsBehind} XIU sessions behind {marketDataAsOf:yyyy-MM-dd}."));
                    }
                    else if (sessionsBehind >= options.StaleWarningSessions)
                    {
                        findings.Add(new AuditFinding(
                            AuditSeverity.Warning,
                            "BARS.ACTIVE_SYMBOL_LAGGING",
                            symbol.Symbol,
                            $"Latest bar {symbol.LatestBarDate:yyyy-MM-dd} is {sessionsBehind} XIU sessions behind {marketDataAsOf:yyyy-MM-dd}."));
                    }
                }
            }

            if (!symbol.SecurityType.Equals("Stock", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!mappingsBySymbol.TryGetValue(symbol.Symbol, out AuditedSectorMapping? mapping))
            {
                findings.Add(new AuditFinding(
                    AuditSeverity.Warning,
                    "SECTOR.ACTIVE_STOCK_MAPPING_MISSING",
                    symbol.Symbol,
                    "Active stock has no StockSectorMap row; verify its classification and listing."));
                continue;
            }

            if (mapping.Sector.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(mapping.SectorIndexSymbol))
            {
                findings.Add(new AuditFinding(
                    AuditSeverity.Warning,
                    "SECTOR.ACTIVE_STOCK_UNMAPPED",
                    symbol.Symbol,
                    $"Active stock has sector '{mapping.Sector}' and no usable sector-index mapping; verify its classification."));
            }
            else if (!TsxSectorSymbols.All.ContainsKey(mapping.SectorIndexSymbol))
            {
                findings.Add(new AuditFinding(
                    AuditSeverity.Error,
                    "SECTOR.INDEX_SYMBOL_UNKNOWN",
                    symbol.Symbol,
                    $"Mapped sector index '{mapping.SectorIndexSymbol}' is not in TsxSectorSymbols."));
            }
            else
                referencedSectorIndices.Add(mapping.SectorIndexSymbol);

            DateTime mappingUpdatedUtc = DateTime.SpecifyKind(mapping.LastUpdated, DateTimeKind.Utc);
            if (comparisonTime.ToUniversalTime() - mappingUpdatedUtc
                > TimeSpan.FromDays(options.SectorMappingMaxAgeDays))
            {
                findings.Add(new AuditFinding(
                    AuditSeverity.Warning,
                    "SECTOR.MAPPING_STALE",
                    symbol.Symbol,
                    $"Sector mapping was last refreshed {mapping.LastUpdated:yyyy-MM-dd}; expected within {options.SectorMappingMaxAgeDays} days."));
            }
        }

        foreach (string indexSymbol in referencedSectorIndices)
        {
            if (!sectorIndicesBySymbol.TryGetValue(indexSymbol, out SectorIndexAuditSummary? sectorIndex))
            {
                findings.Add(new AuditFinding(
                    AuditSeverity.Error,
                    "SECTOR.INDEX_HISTORY_MISSING",
                    indexSymbol,
                    "Referenced sector index has no SectorIndices rows."));
                continue;
            }

            if (!marketDataAsOf.HasValue)
                continue;

            int sessionsBehind = MarketDataFreshness.CountSessionsBehind(
                sectorIndex.LatestDate,
                snapshot.BenchmarkSessions);
            if (sessionsBehind >= options.StaleWarningSessions)
            {
                findings.Add(new AuditFinding(
                    sessionsBehind >= options.StaleErrorSessions
                        ? AuditSeverity.Error
                        : AuditSeverity.Warning,
                    "SECTOR.INDEX_HISTORY_STALE",
                    indexSymbol,
                    $"Latest index row {sectorIndex.LatestDate:yyyy-MM-dd} is {sessionsBehind} XIU sessions behind {marketDataAsOf:yyyy-MM-dd}."));
            }
        }

        foreach (DuplicateDailyBarSummary duplicate in snapshot.DuplicateDailyBars)
        {
            findings.Add(new AuditFinding(
                AuditSeverity.Error,
                "BARS.DUPLICATE_SYMBOL_DATE",
                duplicate.Symbol,
                $"{duplicate.DuplicateDates:N0} date(s) contain {duplicate.ExtraRows:N0} extra DailyBars row(s)."));
        }

        foreach (OrphanDailyBarSummary orphan in snapshot.OrphanDailyBars)
        {
            findings.Add(new AuditFinding(
                AuditSeverity.Error,
                "BARS.ORPHAN_SYMBOL",
                orphan.Symbol,
                $"{orphan.BarCount:N0} DailyBars row(s), through {orphan.LatestBarDate:yyyy-MM-dd}, have no dbo.Symbols parent."));
        }

        foreach (AuditedSectorMapping mapping in snapshot.SectorMappings)
        {
            if (!symbolsByName.ContainsKey(mapping.Symbol))
            {
                findings.Add(new AuditFinding(
                    AuditSeverity.Warning,
                    "SECTOR.ORPHAN_MAPPING",
                    mapping.Symbol,
                    "StockSectorMap row has no dbo.Symbols parent."));
            }
        }

        foreach (SectorIndexAuditSummary sectorIndex in snapshot.SectorIndices)
        {
            if (sectorIndex.InvalidPriceRows > 0)
            {
                findings.Add(new AuditFinding(
                    AuditSeverity.Error,
                    "SECTOR.INVALID_PRICE",
                    sectorIndex.Symbol,
                    $"{sectorIndex.InvalidPriceRows:N0} SectorIndices row(s) have non-positive prices."));
            }
        }

        IReadOnlyList<AuditFinding> ordered = findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Code, StringComparer.Ordinal)
            .ThenBy(f => f.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MarketDataAuditReport(
            marketDataAsOf,
            snapshot.Symbols.Count,
            snapshot.Symbols.Count(s => s.IsActive),
            snapshot.Symbols.Count(s => s.IsActive && s.SecurityType.Equals("Stock", StringComparison.OrdinalIgnoreCase)),
            snapshot.Symbols.Count(s => s.IsActive && s.SecurityType.Equals("ETF", StringComparison.OrdinalIgnoreCase)),
            ordered);
    }
}
