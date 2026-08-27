#nullable enable

using Core.Runtime;
using Core.Trader;
using Shouldly;
using System;
using System.Text.Json;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiPresentationSnapshotTests
{
    [Fact]
    public void ReportBuilderCreatesTypedPresentationFromTheSameRunFacts()
    {
        var report = new DelphiReportBuilder
        {
            RecommendationDate = new DateTime(2026, 8, 26),
            MarketDataAsOf = new DateTime(2026, 8, 25),
            DiscoveredSymbols = 412,
            LoadedSymbols = 301,
            SkippedStaleHistory = 4,
            StrategyVersionName = "Short swing v1",
            StrategyDescription = "Official paper strategy",
            PatternModels = ["Granville", "A/D breadth"],
            ProfitModels = ["BinaryUp10"]
        };

        DelphiPresentationSnapshot snapshot = report.BuildPresentationSnapshot(
            "Human summary",
            "Diagnostic detail");

        snapshot.SchemaVersion.ShouldBe(DelphiPresentationSchema.CurrentVersion);
        snapshot.IsReconstructed.ShouldBeFalse();
        snapshot.RecommendationDate.ShouldBe(new DateTime(2026, 8, 26));
        snapshot.MarketDataAsOf.ShouldBe(new DateTime(2026, 8, 25));
        snapshot.Recommendation.HasTrade.ShouldBeFalse();
        snapshot.Universe.Discovered.ShouldBe(412);
        snapshot.Universe.Loaded.ShouldBe(301);
        snapshot.Universe.SkippedStaleHistory.ShouldBe(4);
        snapshot.Strategy.VersionName.ShouldBe("Short swing v1");
        snapshot.Strategy.PatternSignals.ShouldContain("Granville");
        snapshot.SummaryReport.ShouldBe("Human summary");
        snapshot.DiagnosticReport.ShouldBe("Diagnostic detail");
    }

    [Fact]
    public void CapturedPresentationRoundTripsThroughCalibrationRunContext()
    {
        DelphiPresentationSnapshot expected = CreateSnapshot();
        string runContextJson = JsonSerializer.Serialize(
            new { presentation = expected },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        DelphiPresentationSnapshot? actual = DelphiPersistedPresentationReader.TryReadCaptured(runContextJson);

        actual.ShouldNotBeNull();
        actual!.Recommendation.Symbol.ShouldBe("NDM");
        actual.Recommendation.SuggestedSize.ShouldBe(140m);
        actual.Strategy.PatternSignals.ShouldBe(["Granville"]);
        actual.RecommendationDate.ShouldBe(expected.RecommendationDate);
        actual.SummaryReport.ShouldBe("Summary");
    }

    [Fact]
    public void RecommendationPresentationUsesTheEvaluatedRankedPickFacts()
    {
        var report = new DelphiReportBuilder
        {
            BestPick = new RankedPick(
                "NDM",
                TradeDirection.Buy,
                0.08,
                0.74,
                0.48,
                0.61,
                0.27,
                0.34,
                [
                    new SignalResult("BreakoutEnhanced", 0.83, TradeDirection.Buy),
                    new SignalResult("VolExpansionRelative10", 0.57, TradeDirection.Buy)
                ]),
            Size = new PositionSizeResult(140m, 0.2, "Within the position limit")
        };

        DelphiRecommendationPresentation recommendation = report
            .BuildPresentationSnapshot("Summary", "Diagnostic")
            .Recommendation;

        recommendation.HasTrade.ShouldBeTrue();
        recommendation.Symbol.ShouldBe("NDM");
        recommendation.UpProbability.ShouldBe(0.61);
        recommendation.DownProbability.ShouldBe(0.27);
        recommendation.DirectionEdge.ShouldBe(0.34);
        recommendation.BreakoutProbability.ShouldBe(0.83);
        recommendation.VolumeExpansionProbability.ShouldBe(0.57);
        recommendation.SuggestedSize.ShouldBe(140m);
    }

    [Fact]
    public void MissingOrUnsupportedPresentationUsesLegacyPath()
    {
        DelphiPersistedPresentationReader.TryReadCaptured("{\"schemaVersion\":3}").ShouldBeNull();

        DelphiPresentationSnapshot unsupported = CreateSnapshot() with { SchemaVersion = 999 };
        string json = JsonSerializer.Serialize(new { presentation = unsupported });

        DelphiPersistedPresentationReader.TryReadCaptured(json).ShouldBeNull();
        DelphiPersistedPresentationReader.TryReadCaptured("not-json").ShouldBeNull();
    }

    private static DelphiPresentationSnapshot CreateSnapshot() => new(
        DelphiPresentationSchema.CurrentVersion,
        false,
        "Captured by Delphi",
        new DateTime(2026, 8, 26),
        new DateTime(2026, 8, 25),
        new DelphiRecommendationPresentation(
            true, "NDM", "Buy", 0.48, 0.38, 0.32, 0.06, 0.83, 0.45,
            140m, 0.2, "Passed the strategy gates", "Confirms"),
        null,
        null,
        [],
        [],
        null,
        null,
        null,
        new DelphiUniversePresentation(412, 301, 10, 4, 5, 2, 80, 10, 1m, 100000, []),
        new DelphiRelativeStrengthPresentation(301, 80, 120, 80, 0, 0, []),
        new DelphiObvPresentation(20, 0.03, 100, 90, 80, 31, []),
        null,
        new DelphiStrategyPresentation(
            "Short swing v1", "Official paper strategy", 0.4, 0.3, 0.5,
            0.4, 0.05, -0.3, 0.1, 5, ["Granville"], ["BinaryUp10"]),
        [],
        [],
        "Summary",
        "Diagnostic");
}
