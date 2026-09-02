#nullable enable

using Core.Runtime;
using Core.Trader;
using Core.Indicators.Granville;
using Shouldly;
using System;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            ProfitModels = ["BinaryUp10"],
            RsAlignmentGapCount = 2,
            RsAlignmentGapSymbols = ["ZZZ", "AAA"],
            RsFullCoverageCount = 299,
            Granville = new GranvilleDailyForecast([], 0, 0, 0, 0),
            LeadershipHistoryDays = 50,
            LeadershipActiveBreadthDays = 12,
            LeadershipActiveBreadthRequired = 12
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
        snapshot.RelativeStrength.AlignmentGapCount.ShouldBe(2);
        snapshot.RelativeStrength.AlignmentGapSymbols.ShouldBe(["AAA", "ZZZ"]);
        snapshot.RelativeStrength.FullCoverageCount.ShouldBe(299);
        snapshot.Granville.ShouldNotBeNull();
        snapshot.Granville!.LeadershipHistoryDays.ShouldBe(50);
        snapshot.Granville.LeadershipActiveBreadthDays.ShouldBe(12);
        snapshot.Granville.LeadershipActiveBreadthRequired.ShouldBe(12);
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
        actual.RelativeStrength.AlignmentGapCount.ShouldBe(2);
        actual.RelativeStrength.AlignmentGapSymbols.ShouldBe(["AAA", "ZZZ"]);
        actual.RelativeStrength.FullCoverageCount.ShouldBe(299);
        actual.Granville.ShouldNotBeNull();
        actual.Granville!.LeadershipHistoryDays.ShouldBe(50);
        actual.Granville.LeadershipActiveBreadthDays.ShouldBe(12);
        actual.Granville.LeadershipActiveBreadthRequired.ShouldBe(12);
        actual.SummaryReport.ShouldBe("Summary");
    }

    [Fact]
    public void CapturedSchemaV1WithoutNewRelativeStrengthCoverageFieldsStillLoads()
    {
        string json = JsonSerializer.Serialize(
            new { presentation = CreateSnapshot() },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        JsonObject relativeStrength = root["presentation"]!["relativeStrength"]!.AsObject();
        relativeStrength.Remove("alignmentGapCount");
        relativeStrength.Remove("alignmentGapSymbols");
        relativeStrength.Remove("fullCoverageCount");
        JsonObject granville = root["presentation"]!["granville"]!.AsObject();
        granville.Remove("leadershipHistoryDays");
        granville.Remove("leadershipActiveBreadthDays");
        granville.Remove("leadershipActiveBreadthRequired");

        DelphiPresentationSnapshot? actual = DelphiPersistedPresentationReader.TryReadCaptured(root.ToJsonString());

        actual.ShouldNotBeNull();
        actual!.SchemaVersion.ShouldBe(1);
        actual.RelativeStrength.Computed.ShouldBe(301);
        actual.RelativeStrength.AlignmentGapCount.ShouldBeNull();
        actual.RelativeStrength.AlignmentGapSymbols.ShouldBeNull();
        actual.RelativeStrength.FullCoverageCount.ShouldBeNull();
        actual.Granville.ShouldNotBeNull();
        actual.Granville!.LeadershipHistoryDays.ShouldBe(0);
        actual.Granville.LeadershipActiveBreadthDays.ShouldBe(0);
        actual.Granville.LeadershipActiveBreadthRequired.ShouldBe(0);
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
        new DelphiGranvillePresentation(0, 0, 0, 0, [])
        {
            LeadershipHistoryDays = 50,
            LeadershipActiveBreadthDays = 12,
            LeadershipActiveBreadthRequired = 12
        },
        null,
        new DelphiUniversePresentation(412, 301, 10, 4, 5, 2, 80, 10, 1m, 100000, []),
        new DelphiRelativeStrengthPresentation(
            301, 80, 120, 61, 0, 0, [],
            2, ["AAA", "ZZZ"], 299),
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
