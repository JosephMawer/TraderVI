#nullable enable
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using Core.Db;
using Core.Oracle;
using Core.Runtime;
using Core.Trader;
using Core.Trader.DelphiLive;
using Core.Trader.Promotion;
using Core.Trader.Risk;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class AiPortfolioPacketBuilderTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tradervi-ai-packet-tests", Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Date = new(2026, 9, 25);
    private static readonly DateTime At = ReviewedTsxSessionCalendar.At(Date, new(10, 0));
    private static readonly DateTimeOffset Now = new(At.AddMinutes(1));
    private readonly Guid managed = Guid.NewGuid();
    private readonly AiPortfolioRiskPolicy policy = new(new ReviewedTsxSessionCalendar(new("test", "synthetic", Date, Date, [Date])));
    private AiPortfolioRiskJournal Journal() => new(directory, new(managed, managed, AiRiskAccountMode.Paper), policy, true);
    private AiRiskObservationResult Start(AiPortfolioRiskJournal journal, params string[] blocks)
    {
        var snapshot = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), managed,
            DelphiLivePolicyDefinition.Version1.PolicyVersionId, "OperationalChampion", null, 12345m, "CAD",
            Date, At.AddMinutes(-30), At.AddDays(-1), "Test", "Synthetic"));
        snapshot = snapshot with { Revision = 1, CurrentSession = Date, UpdatedUtc = At,
            Marks = [new(Guid.NewGuid(), Date, DelphiLivePortfolioMarkKind.Checkpoint, At, true, 12345m, [], "Complete")] };
        return new DelphiLiveAiPortfolioRiskMonitor(journal, policy, TimeSpan.FromMinutes(3)).Start(snapshot, Now, false, [.. blocks]);
    }

    private static AiPortfolioCatalogEntry Choice() => new(Guid.NewGuid(), "Continuation · 3 positions", "DailyShadow",
        TradingStrategyResolutionTests.Strategy(), null, []);
    private static AiPortfolioCatalog Catalog(params AiPortfolioCatalogEntry[] choices) => new(Now, Now.AddMinutes(5), [.. choices]);
    private static AiStrategyDecisionContext Context(AiPortfolioCatalogEntry choice) => new(choice.Id, choice.Version, Now,
        Now.AddMinutes(1), true, [new("breadth", false, "Observed breadth below the usual discretionary threshold")],
        [new("daily", Now, "Synthetic dated engine observation")]);
    private static string Proposal(AiPortfolioPacket packet, AiPortfolioCatalogEntry choice, string action = "select", AiRuleOverride[]? overrides = null) =>
        JsonSerializer.Serialize(new AiPortfolioProposal(packet.PacketId, action, choice.Id, choice.Version, "Based on saved evidence",
            [choice.Id + ":daily"], overrides ?? [], "Recheck if evidence changes", []), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

    [Fact]
    public void BuildsUsableReviewInputsWithSeparateManagedFundsAndExactChoiceVersion()
    {
        using var journal = Journal();
        var observation = Start(journal);
        var choice = Choice();
        var packet = AiPortfolioPacketBuilder.Build(Catalog(choice), journal, observation, null, [Context(choice)], Now);
        Assert.True(packet.Candidates.Single().Eligible);
        Assert.Equal(choice.Version, packet.Candidates[0].StrategyVersion);
        Assert.Equal(Now.AddMinutes(1), packet.ExpiresUtc);
        using var account = JsonDocument.Parse(packet.Evidence.Single(e => e.Id == "managed-account").Description);
        Assert.Equal(managed, account.RootElement.GetProperty("Scope").GetProperty("PortfolioId").GetGuid());
        Assert.Equal(12345m, account.RootElement.GetProperty("SavedBook").GetProperty("Cash").GetDecimal());
        Assert.DoesNotContain("Cash", packet.Evidence.Single(e => e.Id == "strategy:" + choice.Id).Description);
        Assert.Equal("select", AiPortfolioReviewer.ParseAndValidate(Proposal(packet, choice), packet).Action);
        Assert.Single(AiPortfolioReviewer.ParseAndValidate(Proposal(packet, choice, overrides: [new("breadth", "Explained discretion")]), packet).RuleOverrides);
        Assert.Throws<InvalidDataException>(() => AiPortfolioReviewer.ParseAndValidate(
            Proposal(packet, choice, overrides: [new("app-protection", "Want more room")]), packet));
        // Missing AI review is the reason to request this proposal; it is still not buying permission.
        Assert.Equal(0m, observation.Assessment.NewBuyAllowanceMultiplier);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("WrongVersion")]
    [InlineData("Stale")]
    [InlineData("Future")]
    [InlineData("NoEvidence")]
    [InlineData("EngineBlocked")]
    public void MissingOrUnqualifiedEngineContextKeepsChoiceVisibleButUnavailable(string defect)
    {
        using var journal = Journal();
        var observation = Start(journal);
        var choice = Choice();
        var context = Context(choice);
        context = defect switch
        {
            "WrongVersion" => context with { StrategyVersion = "different" },
            "Stale" => context with { ExpiresUtc = Now },
            "Future" => context with { ObservedUtc = Now.AddSeconds(1) },
            "NoEvidence" => context with { Evidence = [] },
            "EngineBlocked" => context with { CanPropose = false },
            _ => context
        };
        var packet = AiPortfolioPacketBuilder.Build(Catalog(choice), journal, observation, null, defect == "Missing" ? [] : [context], Now);
        Assert.False(packet.Candidates.Single().Eligible);
        Assert.Contains("Unavailable", packet.Candidates[0].Summary);
        Assert.Throws<InvalidDataException>(() => AiPortfolioReviewer.ParseAndValidate(Proposal(packet, choice), packet));
    }

    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, -1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public void BothFamiliesAndFixedGateVariantsPreserveTheirDefinitions(bool live, int profile)
    {
        var choice = Choice();
        if (live)
        {
            var definition = DelphiLivePolicyDefinition.Version1;
            choice = choice with { EngineFamily = "DelphiLive", Strategy = choice.Strategy! with { Family = TradingStrategyFamily.DelphiLive,
                ShadowPolicy = null, LivePolicy = definition, PolicyVersionId = definition.PolicyVersionId,
                Selection = CompleteTradingStrategy.LiveSelection, MaximumHoldings = definition.MaximumHoldings } };
        }
        if (profile >= 0)
        {
            var strategy = choice.Strategy!;
            choice = choice with { Comparison = new(choice.PaperAccountId, Guid.NewGuid(), Guid.NewGuid(), live ? "DelphiLive" : "ContinuationTop3",
                (PaperGateProfile)profile, At.AddDays(-1), strategy.Daily.StrategyVersionId, live ? strategy.PolicyVersionId : null,
                live ? null : strategy.PolicyVersionId, live ? null : EngineStrategySettings.Serialize(strategy.ShadowPolicy!)) };
        }
        choice.Validate();
        var changed = profile < 0 ? choice with { Strategy = choice.Strategy! with { MaximumHoldings = live ? 7 : 5 } }
            : choice with { Comparison = choice.Comparison! with { Profile = profile == 0 ? PaperGateProfile.PortfolioRulesOnlyV1 : PaperGateProfile.NormalV1 } };
        Assert.NotEqual(choice.Version, changed.Version);
        Assert.Equal(choice.Version, (choice with { Name = "Renamed display" }).Version);
    }

    [Theory]
    [InlineData("Account")]
    [InlineData("DailyVersion")]
    [InlineData("PolicyVersion")]
    [InlineData("Settings")]
    public void BadComparisonIdentityIsUnavailableInsteadOfDisappearing(string defect)
    {
        var choice = Choice();
        var strategy = choice.Strategy!;
        var comparison = new PaperComparisonAccount(choice.PaperAccountId, Guid.NewGuid(), Guid.NewGuid(), "ContinuationTop3",
            PaperGateProfile.NormalV1, At.AddDays(-1), strategy.Daily.StrategyVersionId, null, strategy.PolicyVersionId,
            EngineStrategySettings.Serialize(strategy.ShadowPolicy!));
        comparison = defect switch
        {
            "Account" => comparison with { AccountId = Guid.NewGuid() },
            "DailyVersion" => comparison with { DailyStrategyVersionId = Guid.NewGuid() },
            "PolicyVersion" => comparison with { ShadowStrategyVersionId = Guid.NewGuid() },
            _ => comparison with { ShadowSettingsJson = "invalid" }
        };
        var unavailable = AiPortfolioCatalogRepository.Create(choice.PaperAccountId, choice.Name, TradingStrategyFamily.DailyShadow,
            comparison, () => strategy, []);
        Assert.Equal(choice.Id, unavailable.Id);
        Assert.Null(unavailable.Strategy);
        Assert.NotEmpty(unavailable.UnavailableReasons);
    }

    [Fact]
    public void CatalogueGrowthAndSameNamesDoNotMergeDistinctAccounts()
    {
        using var journal = Journal();
        var observation = Start(journal);
        var choices = Enumerable.Range(0, 16).Select(_ => Choice()).ToArray();
        var packet = AiPortfolioPacketBuilder.Build(Catalog(choices), journal, observation, choices[0].Id, [.. choices.Select(Context)], Now);
        Assert.Equal(16, packet.Candidates.Length);
        Assert.All(packet.Candidates, c => Assert.True(c.Eligible));
        Assert.Equal(16, packet.Evidence.Count(e => e.Id.EndsWith(":daily")));
        Assert.Equal("switch", AiPortfolioReviewer.ParseAndValidate(Proposal(packet, choices[1], "switch"), packet).Action);
    }

    [Theory]
    [InlineData("RejectedRead")]
    [InlineData("StaleValue")]
    [InlineData("StaleCatalogue")]
    [InlineData("UnknownActive")]
    [InlineData("DuplicateChoice")]
    [InlineData("DuplicateContext")]
    [InlineData("FutureEvidence")]
    [InlineData("ChangedJournal")]
    public void InconsistentInputsCannotProduceAReviewPacket(string defect)
    {
        using var journal = Journal();
        var observation = Start(journal);
        var choice = Choice();
        var catalog = Catalog(choice);
        ImmutableArray<AiStrategyDecisionContext> contexts = [Context(choice)];
        string? active = null;
        var now = Now;
        switch (defect)
        {
            case "RejectedRead": observation = observation with { Accepted = false }; break;
            case "StaleValue": now = Now.AddMinutes(3); break;
            case "StaleCatalogue": catalog = catalog with { ExpiresUtc = Now }; break;
            case "UnknownActive": active = Guid.NewGuid().ToString(); break;
            case "DuplicateChoice": catalog = Catalog(choice, choice); break;
            case "DuplicateContext": contexts = [contexts[0], contexts[0]]; break;
            case "FutureEvidence": contexts = [contexts[0] with { Evidence = [new("future", Now.AddSeconds(1), "Not yet observed")] }]; break;
            case "ChangedJournal":
                var value = observation.Snapshot.State!.LastValuation with { EvidenceId = Guid.NewGuid(), At = Now, Value = 12346m };
                journal.Apply(new(Guid.NewGuid(), observation.Snapshot.Revision, AiRiskTransitionKind.Observe, value), Now);
                break;
        }
        Assert.Throws<InvalidDataException>(() => AiPortfolioPacketBuilder.Build(catalog, journal, observation, active, contexts, now));
    }

    [Fact]
    public void IndependentHoldBlocksSelectionAndCannotBeOverridden()
    {
        using var journal = Journal();
        var observation = Start(journal, "CapitalReviewRequired");
        var choice = Choice();
        var packet = AiPortfolioPacketBuilder.Build(Catalog(choice), journal, observation, null, [Context(choice)], Now);
        Assert.False(packet.Candidates[0].Eligible);
        Assert.Contains("CapitalReviewRequired", packet.Candidates[0].Summary);
    }

    [Fact]
    public void UnsupportedFutureFamilyStaysVisibleWithItsReason()
    {
        using var journal = Journal();
        var observation = Start(journal);
        var choice = new AiPortfolioCatalogEntry(Guid.NewGuid(), "Future portfolio", "FutureEngine", null, null, ["Engine adapter not yet integrated"]);
        var packet = AiPortfolioPacketBuilder.Build(Catalog(choice), journal, observation, null, [], Now);
        Assert.Single(packet.Candidates);
        Assert.False(packet.Candidates[0].Eligible);
        Assert.Contains("Engine adapter not yet integrated", packet.Candidates[0].Summary);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
