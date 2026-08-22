#nullable enable

using Core.DataQuality;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class MarketDataAuditorTests
{
    private static readonly DateTime Now = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime[] Sessions =
    [
        new(2026, 8, 17),
        new(2026, 8, 18),
        new(2026, 8, 19),
        new(2026, 8, 20),
        new(2026, 8, 21)
    ];

    [Fact]
    public void Analyze_CleanCurrentStock_HasNoFindings()
    {
        MarketDataAuditSnapshot snapshot = Snapshot(
            symbols:
            [
                Symbol("GDI", "Stock", latest: new DateTime(2026, 8, 21))
            ],
            mappings:
            [
                Mapping("GDI", "Industrials", "^TTIN")
            ],
            sectorIndices:
            [
                new SectorIndexAuditSummary("^TTIN", 100, new DateTime(2026, 8, 21), 0)
            ]);

        MarketDataAuditReport report = MarketDataAuditor.Analyze(snapshot, utcNow: Now);

        report.ErrorCount.ShouldBe(0);
        report.WarningCount.ShouldBe(0);
        report.MarketDataAsOf.ShouldBe(new DateTime(2026, 8, 21));
    }

    [Fact]
    public void Analyze_StaleActiveStock_IsErrorAndStillChecksMapping()
    {
        MarketDataAuditSnapshot snapshot = Snapshot(
            symbols:
            [
                Symbol("GDI", "Stock", latest: new DateTime(2026, 8, 17))
            ]);

        MarketDataAuditReport report = MarketDataAuditor.Analyze(
            snapshot,
            new MarketDataAuditOptions(StaleWarningSessions: 2, StaleErrorSessions: 4),
            Now);

        report.Findings.ShouldContain(f =>
            f.Code == "BARS.ACTIVE_SYMBOL_STALE"
            && f.Symbol == "GDI"
            && f.Severity == AuditSeverity.Error);
        report.Findings.ShouldContain(f =>
            f.Code == "SECTOR.ACTIVE_STOCK_MAPPING_MISSING"
            && f.Symbol == "GDI");
    }

    [Fact]
    public void Analyze_FundLikeStockAndUnflaggedLeveragedProduct_AreCandidates()
    {
        AuditedSymbol product = Symbol("TEST", "Stock", latest: new DateTime(2026, 8, 21))
            with { ShortName = "BetaPro Covered Call 2x ETF" };

        MarketDataAuditReport report = MarketDataAuditor.Analyze(
            Snapshot(symbols: [product]),
            utcNow: Now);

        report.Findings.ShouldContain(f => f.Code == "SYMBOL.STOCK_LOOKS_LIKE_FUND");
        report.Findings.ShouldContain(f => f.Code == "SYMBOL.LEVERAGED_FLAG_MISSING");
    }

    [Fact]
    public void Analyze_DatabaseIntegrityProblems_AreErrors()
    {
        AuditedSymbol badBars = Symbol("BAD", "ETF", latest: new DateTime(2026, 8, 21))
            with { InvalidOhlcBars = 2, NegativeVolumeBars = 1 };

        MarketDataAuditSnapshot snapshot = Snapshot(
            symbols: [badBars],
            duplicates: [new DuplicateDailyBarSummary("BAD", 1, 1)],
            orphans: [new OrphanDailyBarSummary("GONE", 20, new DateTime(2026, 8, 21))],
            sectorIndices: [new SectorIndexAuditSummary("^TTEN", 5, new DateTime(2026, 8, 21), 1)]);

        MarketDataAuditReport report = MarketDataAuditor.Analyze(snapshot, utcNow: Now);

        report.ErrorCount.ShouldBe(5);
        report.Findings.Select(f => f.Code).ShouldBe(
        [
            "BARS.DUPLICATE_SYMBOL_DATE",
            "BARS.INVALID_OHLC",
            "BARS.NEGATIVE_VOLUME",
            "BARS.ORPHAN_SYMBOL",
            "SECTOR.INVALID_PRICE"
        ], ignoreOrder: true);
    }

    private static MarketDataAuditSnapshot Snapshot(
        IReadOnlyList<AuditedSymbol>? symbols = null,
        IReadOnlyList<AuditedSectorMapping>? mappings = null,
        IReadOnlyList<DuplicateDailyBarSummary>? duplicates = null,
        IReadOnlyList<OrphanDailyBarSummary>? orphans = null,
        IReadOnlyList<SectorIndexAuditSummary>? sectorIndices = null)
        => new(
            Sessions,
            symbols ?? [],
            mappings ?? [],
            duplicates ?? [],
            orphans ?? [],
            sectorIndices ?? []);

    private static AuditedSymbol Symbol(string symbol, string type, DateTime latest)
        => new(
            symbol,
            symbol,
            symbol,
            type,
            true,
            false,
            100,
            new DateTime(2026, 1, 1),
            latest,
            0,
            0);

    private static AuditedSectorMapping Mapping(string symbol, string sector, string index)
        => new(symbol, sector, null, index, Now.AddDays(-1));
}
