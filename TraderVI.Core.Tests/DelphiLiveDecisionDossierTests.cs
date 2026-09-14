#nullable enable

using Core.Trader.DelphiLive;
using Core.Trader;
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

    [Fact]
    public void ExplicitEngineeringSessionPreservesFailedEligibilityInAValidDossier()
    {
        var source = EngineeringDossier();
        Should.Throw<ArgumentException>(() => DelphiLiveDecisionDossierBuilder.Validate(source, Policy));
        var admitted = DelphiLiveDecisionDossierBuilder.AnnotateEngineeringAdmission(source);
        var json = DelphiLiveDecisionDossierBuilder.Serialize(admitted, Policy);
        admitted.SourceLenses[0].Eligible.ShouldBeFalse();
        admitted.SourceLenses[0].FirstFailure.ShouldBe("Breadth");
        json.ShouldContain(DelphiLiveDecisionDossierBuilder.EngineeringTrialId.ToString("D"));
    }

    [Fact]
    public void EngineeringAdmissionDoesNotPermitOtherSessionsRunsDatesOrFailedGates()
    {
        var admitted = DelphiLiveDecisionDossierBuilder.AnnotateEngineeringAdmission(EngineeringDossier());
        foreach (var invalid in new[] {
            admitted with { DelphiLiveSessionId = Guid.NewGuid() },
            admitted with { CalibrationRunId = Guid.NewGuid() },
            admitted with { DailyStrategyVersionId = Guid.NewGuid() },
            admitted with { DecisionUtc = new DateTime(2026,9,9,18,0,0,DateTimeKind.Utc) },
            admitted with { SourceLenses = [admitted.SourceLenses[0] with { FirstFailure = "HistoryFreshness" }] },
            admitted with { SourceLenses = [admitted.SourceLenses[0] with { Published = false }] } })
            Should.Throw<ArgumentException>(() => DelphiLiveDecisionDossierBuilder.Validate(invalid, Policy));
    }

    [Fact]
    public void CarriedEngineeringEntryRetainsItsAdmissionForLaterProtection()
    {
        var entry = EngineeringDossier();
        var exit = QuoteExit() with {
            DecisionUtc = new DateTime(2026,9,9,14,0,0,DateTimeKind.Utc),
            OriginalEntryThesis = new(Guid.NewGuid(),entry.CalibrationRunId!.Value,
                entry.CalibrationCandidateId!.Value,entry.DailyStrategyVersionId!.Value,entry.SourceLenses) };
        Should.Throw<ArgumentException>(() => DelphiLiveDecisionDossierBuilder.Validate(exit,Policy));
        DelphiLiveDecisionDossierBuilder.Validate(
            DelphiLiveDecisionDossierBuilder.AnnotateEngineeringAdmission(exit),Policy);
    }

    [Fact]
    public void CarriedComparisonHoldingRetainsFailedDailyFactsAndAdmissionForProtection()
    {
        var exit = QuoteExit();
        var original = exit.OriginalEntryThesis!;
        var admission = new PaperGateAdmission(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            PaperGateProfile.PortfolioRulesOnlyV1, Guid.NewGuid(), original.CalibrationRunId,
            original.DailyStrategyVersionId, Policy.PolicyVersionId, exit.DecisionUtc.AddDays(-1));
        var carried = exit with { OriginalEntryThesis = original with {
            SourceLenses = [Lens(2) with { Eligible = false, FirstFailure = "Granville" }], ComparisonAdmission = admission } };
        DelphiLiveDecisionDossierBuilder.Serialize(carried, Policy).ShouldContain("Granville");
        carried.FiredExitRules.ShouldContain(DelphiLiveExitRule.HardLoss5Pct);
        foreach (var invalid in new[] { admission with { Profile = PaperGateProfile.NormalV1 },
            admission with { PolicyVersionId = Guid.NewGuid() }, admission with { RunId = Guid.NewGuid() } })
            Should.Throw<ArgumentException>(() => DelphiLiveDecisionDossierBuilder.Validate(
                carried with { OriginalEntryThesis = carried.OriginalEntryThesis! with { ComparisonAdmission = invalid } }, Policy));
    }

    private static DelphiLiveDecisionDossier EngineeringDossier() => QuoteExit() with {
        DelphiLiveSessionId = DelphiLiveDecisionDossierBuilder.EngineeringSessionId,
        CalibrationRunId = DelphiLiveDecisionDossierBuilder.EngineeringRunId,
        CalibrationCandidateId = Guid.NewGuid(),
        DailyStrategyVersionId = DelphiLiveDecisionDossierBuilder.EngineeringStrategyId,
        DecisionUtc = new DateTime(2026,9,8,19,10,0,DateTimeKind.Utc),
        OriginalEntryThesis = null,
        SourceLenses = [Lens(2) with { Eligible = false, FirstFailure = "Breadth" }] };
}
