#nullable enable

using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveDecisionDossierTests
{
    private static readonly DelphiLivePolicyDefinition Policy = DelphiLivePolicyDefinition.Version1;

    [Fact]
    public void CarriedHoldingQuoteExitRetainsOriginalThesisWithoutInventingCurrentDailyRanks()
    {
        DelphiLiveDecisionDossier dossier = QuoteExit();

        string json = DelphiLiveDecisionDossierBuilder.Serialize(dossier, Policy);

        dossier.CalibrationRunId.ShouldBeNull();
        dossier.SourceLenses.ShouldBeEmpty();
        dossier.BarEndUtc.ShouldBeNull();
        json.ShouldContain("\"originalEntryThesis\"");
        json.ShouldContain("WarmupHardLoss5Pct");
        json.ShouldContain(dossier.EvidenceQuoteIds[0].ToString());
    }

    [Fact]
    public void DossierRejectsPartialCurrentThesisAndMissingOriginalProvenance()
    {
        DelphiLiveDecisionDossier dossier = QuoteExit();

        Should.Throw<ArgumentException>(() => DelphiLiveDecisionDossierBuilder.Validate(
            dossier with { CalibrationRunId = Guid.NewGuid() }, Policy));
        Should.Throw<ArgumentException>(() => DelphiLiveDecisionDossierBuilder.Validate(
            dossier with { OriginalEntryThesis = null }, Policy));
    }

    [Fact]
    public void ReselectedHoldingKeepsNewThesisSeparateFromOriginalEntryThesis()
    {
        DelphiLiveDecisionDossier original = QuoteExit();
        Guid newRun = Guid.NewGuid();
        DelphiLiveDecisionDossier reselected = original with
        {
            CalibrationRunId = newRun,
            CalibrationCandidateId = Guid.NewGuid(),
            DailyStrategyVersionId = Guid.NewGuid(),
            SourceLenses = [Lens(8)]
        };

        DelphiLiveDecisionDossierBuilder.Validate(reselected, Policy);

        reselected.CalibrationRunId.ShouldBe(newRun);
        reselected.SourceLenses[0].Rank.ShouldBe(8);
        reselected.OriginalEntryThesis.ShouldBe(original.OriginalEntryThesis);
        reselected.OriginalEntryThesis!.SourceLenses[0].Rank.ShouldBe(2);
    }

    private static DelphiLiveDecisionDossier QuoteExit()
    {
        DelphiLiveFamilyJudgment[] families = Enum.GetValues<DelphiLiveSignalFamily>()
            .Select(family => new DelphiLiveFamilyJudgment(family, DelphiLiveFamilyState.NotMature, "NotMature"))
            .ToArray();
        return new(
            Policy.DecisionDossierSchemaVersion, Policy.DecisionDossierVersion,
            Guid.NewGuid(), Guid.NewGuid(), new DateTime(2026, 9, 8, 13, 31, 0, DateTimeKind.Utc),
            Guid.NewGuid(), null, null, null, Policy.PolicyVersionId,
            Policy.PolicyDefinitionName, Policy.EvaluatorVersion, Policy.CollectorVersion,
            Policy.QuoteFillVersion, "AAA", null, [], [],
            new Dictionary<string, decimal?> { ["bid"] = 94m, ["averagePurchasePrice"] = 100m },
            new Dictionary<string, string> { ["sessionThesis"] = "HeldNotReselected" },
            families, DelphiLiveFamilyCombiner.Combine(families),
            DelphiLiveDataConfidence.Normal, DelphiLiveDataConfidence.Normal,
            DelphiLiveRecommendationState.Held, DelphiLiveRecommendationState.ExitPending,
            [DelphiLiveExitRule.HardLoss5Pct], DelphiLiveExitRule.HardLoss5Pct,
            ["HardLoss5Pct", "WarmupHardLoss5Pct"], "Sell", "Pending")
        {
            OriginalEntryThesis = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [Lens(2)]),
            EvidenceQuoteIds = [Guid.NewGuid()]
        };
    }

    private static DelphiLiveDossierLensAttribution Lens(int rank) =>
        new(Guid.NewGuid(), "Continuation", rank, 1m, true, true, null, "{}");
}
