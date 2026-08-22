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
}
