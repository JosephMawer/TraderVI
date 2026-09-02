using Core.DataQuality;
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
}
