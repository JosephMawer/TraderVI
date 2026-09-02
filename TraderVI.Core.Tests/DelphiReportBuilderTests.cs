using Core.DataQuality;
using Core.Indicators.Granville;
using Core.Runtime;
using Shouldly;
using System;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiReportBuilderTests
{
    [Fact]
    public void BuildDiagnostic_DistinguishesRecommendationDateAndMarketDataDate()
    {
        var report = new DelphiReportBuilder
        {
            RecommendationDate = new DateTime(2026, 8, 22),
            MarketDataAsOf = new DateTime(2026, 8, 21)
        };

        string diagnostic = report.BuildDiagnostic();

        diagnostic.ShouldContain("Recommendation date: 2026-08-22");
        diagnostic.ShouldContain("Market data as of:    2026-08-21");
        diagnostic.ShouldContain("database PickDate/EvalDate");
    }

    [Fact]
    public void Reports_NameTheStrategyDecisionAndCodeBoundary()
    {
        var report = new DelphiReportBuilder
        {
            StrategyVersionName = "v3.1-rs-date-aligned",
            StrategyDecisionRef = "ADR-0041",
            StrategyInitialCodeCommit = "c51c0849fd1311b3797cc664a19988e553bbe122"
        };

        report.BuildDiagnostic().ShouldContain(
            "v3.1-rs-date-aligned  (ADR-0041 · c51c0849fd13)");
        report.BuildSummary().ShouldContain(
            "Strategy: v3.1-rs-date-aligned  |  ADR-0041 · c51c0849fd13");
    }

    [Fact]
    public void BuildDiagnostic_ListsFallbackSymbolsInStableOrder()
    {
        var report = new DelphiReportBuilder
        {
            RsFallbackToXiuCount = 3,
            RsFallbackSymbols = ["SOFY", "BIGY", "GDI"]
        };

        string diagnostic = report.BuildDiagnostic();

        diagnostic.ShouldContain("Fallback to XIU:       3");
        diagnostic.ShouldContain("Fallback symbols:      BIGY, GDI, SOFY");
        diagnostic.ShouldContain("no usable sector-index series");
    }

    [Fact]
    public void BuildDiagnostic_ListsDateAlignmentGapSymbolsInStableOrder()
    {
        var report = new DelphiReportBuilder
        {
            RsAlignmentGapCount = 2,
            RsAlignmentGapSymbols = ["ZZZ", "AAA"]
        };

        string diagnostic = report.BuildDiagnostic();

        diagnostic.ShouldContain("Date-alignment gaps:   2");
        diagnostic.ShouldContain("Gap symbols:           AAA, ZZZ");
        diagnostic.ShouldContain("metrics requiring those sessions stay null");
        diagnostic.ShouldContain("unaffected metrics remain valid");
    }

    [Fact]
    public void Reports_SurfaceStaleHistoryGateAndStableExclusionDetails()
    {
        var report = new DelphiReportBuilder
        {
            MarketDataAsOf = new DateTime(2026, 8, 21),
            SkippedStaleHistory = 2,
            StaleHistoryExclusions =
            [
                new("ZZZ", new DateTime(2026, 8, 19), 2, "Latest bar is 2 completed TSX session(s) behind."),
                new("AAA", new DateTime(2026, 8, 20), 1, "Latest bar is 1 completed TSX session(s) behind.")
            ]
        };

        string diagnostic = report.BuildDiagnostic();
        string summary = report.BuildSummary();

        diagnostic.ShouldContain("Skipped (stale):       2");
        diagnostic.IndexOf("AAA", StringComparison.Ordinal).ShouldBeLessThan(
            diagnostic.IndexOf("ZZZ", StringComparison.Ordinal));
        summary.ShouldContain("2 stale");
        summary.ShouldContain("Freshness gate: 2 symbol(s) excluded");
        summary.ShouldContain("XIU session 2026-08-21");
    }

    [Fact]
    public void Reports_SurfaceUnavailableLeadershipMoversWithoutCallingThemZeroBreadth()
    {
        var report = new DelphiReportBuilder
        {
            Granville = new GranvilleDailyForecast([], 0, 0, 0, 0),
            LeadershipHistoryDays = 42,
            LeadershipActiveBreadthDays = 3,
            LeadershipActiveBreadthRequired = 12
        };

        string diagnostic = report.BuildDiagnostic();
        string summary = report.BuildSummary();

        diagnostic.ShouldContain("Stored history:          42 day(s)");
        diagnostic.ShouldContain("Contiguous observations: 3/12 trailing session(s)");
        diagnostic.ShouldContain("N/A — insufficient mover observations");
        diagnostic.ShouldContain("missing data is not zero or falling breadth");
        summary.ShouldContain("Leadership movers: N/A — 3/12 contiguous observations (42 stored days)");
        summary.ShouldContain("mover-dependent evidence is neutral/no-data");
    }

    [Fact]
    public void Reports_SurfaceSufficientContiguousLeadershipMoverCoverage()
    {
        var report = new DelphiReportBuilder
        {
            Granville = new GranvilleDailyForecast([], 0, 0, 0, 0),
            LeadershipHistoryDays = 50,
            LeadershipActiveBreadthDays = 14,
            LeadershipActiveBreadthRequired = 12
        };

        report.BuildDiagnostic().ShouldContain("Status:                  available");
        report.BuildSummary().ShouldContain(
            "Leadership movers: 14/12 contiguous observations (50 stored days)");
    }
}
