#nullable enable

using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLivePolicyDefinitionTests
{
    [Fact]
    public void Version1CarriesEveryFrozenIdentityAndOperationalThreshold()
    {
        DelphiLivePolicyDefinition policy = DelphiLivePolicyDefinition.Version1;

        policy.PolicyVersionId.ShouldBe(
            Guid.Parse("C15C1A27-13A1-581A-8912-06C92941A01E"));
        policy.PolicyDefinitionName.ShouldBe("DelphiLivePolicyV1");
        policy.EvaluatorVersion.ShouldBe("DelphiLiveEvaluatorV1");
        policy.CollectorVersion.ShouldBe("IntradayEvidenceCollectorV3");
        policy.DecisionDossierVersion.ShouldBe("DelphiLiveDecisionDossierV1");
        policy.QuoteFillVersion.ShouldBe("DelphiLiveQuoteFillV1");
        policy.ShadowPortfolioVersion.ShouldBe("DelphiLiveShadowPortfolioV1");
        policy.ResearchOutcomeVersion.ShouldBe("LiveObservationOutcomeV1");
        policy.RankingDiagnosticVersion.ShouldBe("DelphiLiveDailyVsLiveTop5V1");
        policy.PromotionProtocolVersion.ShouldBe("DelphiLivePromotionV1");

        policy.BarInterval.ShouldBe(TimeSpan.FromMinutes(5));
        policy.PersistenceObservationCount.ShouldBe(4);
        policy.ImmediateMovementHorizon.ShouldBe(TimeSpan.FromMinutes(20));
        policy.VolatilityRulers.OperationalSessions.ShouldBe(10);
        policy.RawMoveThresholds.ShouldBe(new DelphiLiveThresholdComparisonSet(0.15m, 0.25m, 0.35m));
        policy.ExcessMoveThresholds.ShouldBe(new DelphiLiveThresholdComparisonSet(0.025m, 0.05m, 0.10m));
        policy.DirectionalVolumeThreshold.ShouldBe(0.10m);
        policy.StructureBufferUnits.ShouldBe(0.05m);
        policy.EntryConfirmationCount.ShouldBe(2);
        policy.WeakeningConfirmationCount.ShouldBe(2);
        policy.FastDownsideReturnFloor.ShouldBe(-0.10m);
        policy.MaximumHoldings.ShouldBe(5);
        policy.EntryTargetNavFraction.ShouldBe(0.20m);
        policy.EntryWindowStart.ShouldBe(new TimeOnly(9, 50));
        policy.EntryCutoff.ShouldBe(new TimeOnly(15, 45));
        policy.PrimaryExitReasonOrder.ShouldBe(ImmutableArray.Create(
            DelphiLiveExitRule.HardLoss5Pct,
            DelphiLiveExitRule.FastDownside10Pct,
            DelphiLiveExitRule.ProfitProtectionFloorBreach,
            DelphiLiveExitRule.ConfirmedSupportFailure,
            DelphiLiveExitRule.LiveWeakeningExit));
    }

    [Theory]
    [InlineData("evaluator")]
    [InlineData("horizon")]
    [InlineData("thresholds")]
    [InlineData("profit-floor")]
    [InlineData("portfolio")]
    [InlineData("entry-window")]
    [InlineData("exit-order")]
    [InlineData("coverage")]
    [InlineData("confirmation")]
    [InlineData("rulers")]
    [InlineData("aligned-horizon")]
    public void InvalidOrContradictoryDefinitionsFailClosed(string mutation)
    {
        DelphiLivePolicyDefinition valid = DelphiLivePolicyDefinition.Version1;
        DelphiLivePolicyDefinition invalid = mutation switch
        {
            "evaluator" => valid with { EvaluatorVersion = "UnknownEvaluator" },
            "horizon" => valid with { ImmediateMovementHorizon = TimeSpan.FromMinutes(15) },
            "thresholds" => valid with
            {
                RawMoveThresholds = new DelphiLiveThresholdComparisonSet(0.25m, 0.15m, 0.35m)
            },
            "profit-floor" => valid with
            {
                ProfitFloorActivationGainFraction = 0.06m,
                TrailingActivationGainFraction = 0.05m
            },
            "portfolio" => valid with { EntryTargetNavFraction = 0.21m },
            "entry-window" => valid with
            {
                EntryWindowStart = new TimeOnly(15, 45),
                EntryCutoff = new TimeOnly(15, 45)
            },
            "exit-order" => valid with
            {
                PrimaryExitReasonOrder = ImmutableArray.Create(
                    DelphiLiveExitRule.FastDownside10Pct,
                    DelphiLiveExitRule.HardLoss5Pct,
                    DelphiLiveExitRule.ProfitProtectionFloorBreach,
                    DelphiLiveExitRule.ConfirmedSupportFailure,
                    DelphiLiveExitRule.LiveWeakeningExit)
            },
            "coverage" => valid with { DegradedCoverageFloor = 1m },
            "confirmation" => valid with { EntryConfirmationCount = 3 },
            "rulers" => valid with { VolatilityRulers = new(5, 12, 14, 20) },
            "aligned-horizon" => valid with { SustainedMovementHorizon = TimeSpan.FromMinutes(65) },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        Should.Throw<DelphiLivePolicyValidationException>(() => invalid.Validate());
    }

    [Fact]
    public void StoredDefinitionRejectsUnknownAndMissingFieldsInsteadOfUsingFallbacks()
    {
        string json = JsonSerializer.Serialize(DelphiLivePolicyDefinition.Version1);
        string unknown = json[..^1] + ",\"UnknownThreshold\":1}";
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<DelphiLivePolicyDefinition>(unknown));

        JsonObject missing = JsonNode.Parse(json)!.AsObject();
        missing.Remove(nameof(DelphiLivePolicyDefinition.EvaluatorVersion));
        Should.Throw<JsonException>(() =>
            JsonSerializer.Deserialize<DelphiLivePolicyDefinition>(missing.ToJsonString()));
    }
}
