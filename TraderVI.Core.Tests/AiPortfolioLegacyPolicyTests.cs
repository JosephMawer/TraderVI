#nullable enable
using System;
using System.IO;
using Core.Db;
using Core.Oracle;
using Core.Runtime;
using Core.Trader;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class AiPortfolioLegacyPolicyTests
{
    [Fact]
    public void OriginalUnassignedPolicyKeepsFrozenSettingsAndEstablishedPromotionIdentity()
    {
        var (id, policy) = AiShadowPolicyIdentity.Read(null, null, null, null, null);
        Assert.Equal(TradingRecommendationStorage.Id(SystemShadowVersions.Policy), id);
        Assert.Equal(SystemShadowPolicyConfig.Version1, policy);
        Assert.NotEqual(SystemShadowPolicyConfig.Default, policy);
    }

    [Theory]
    [InlineData(PaperGateProfile.NormalV1)]
    [InlineData(PaperGateProfile.PortfolioRulesOnlyV1)]
    public void FixedLegacyVariantsRetainTheirPinnedPolicyAndValidateAsChoices(PaperGateProfile profile)
    {
        var strategy = TradingStrategyResolutionTests.Strategy();
        var account = new PaperComparisonAccount(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "ContinuationTop3", profile, DateTime.UtcNow, strategy.Daily.StrategyVersionId,
            null, null, EngineStrategySettings.Serialize(SystemShadowPolicyConfig.Version1));
        // A later ordinary assignment cannot replace the comparison's pinned definition.
        var (id, policy) = AiShadowPolicyIdentity.Read(Guid.NewGuid(), "ignored", "ignored", "ignored", account);
        var choice = new AiPortfolioCatalogEntry(account.AccountId, "Legacy variant", "DailyShadow",
            strategy with { PolicyVersionId = id, ShadowPolicy = policy,
                Selection = "FrozenDaily:Continuation", MaximumHoldings = 3 }, account, []);
        choice.Validate();
        Assert.Equal(AiShadowPolicyIdentity.LegacyId, id);
        Assert.Equal(SystemShadowPolicyConfig.Version1, policy);
        Assert.Throws<InvalidDataException>(() => AiShadowPolicyIdentity.Read(null, null, null, null,
            account with { ShadowSettingsJson = EngineStrategySettings.Serialize(SystemShadowPolicyConfig.Default) }));
        Assert.Throws<InvalidDataException>(() => (choice with { Comparison = account with {
            ShadowSettingsJson = EngineStrategySettings.Serialize(SystemShadowPolicyConfig.Default) },
            Strategy = choice.Strategy! with { ShadowPolicy = SystemShadowPolicyConfig.Default } }).Validate());
    }

    [Theory]
    [InlineData("MissingSettings")]
    [InlineData("MissingVersion")]
    [InlineData("WrongHash")]
    [InlineData("WrongFamily")]
    [InlineData("EmptyVersion")]
    public void BrokenExplicitAssignmentsNeverFallBackToLegacy(string defect)
    {
        Guid? id = defect == "MissingVersion" ? null : defect == "EmptyVersion" ? Guid.Empty : Guid.NewGuid();
        string json = EngineStrategySettings.Serialize(SystemShadowPolicyConfig.Default);
        Assert.Throws<InvalidDataException>(() => AiShadowPolicyIdentity.Read(id,
            defect == "WrongFamily" ? "wrong" : EngineStrategySettings.Shadow,
            defect == "MissingSettings" ? null : json,
            defect == "WrongHash" ? "wrong" : EngineStrategySettings.Hash(json), null));
    }

    [Fact]
    public void ExplicitAssignmentKeepsItsExactVersionAndSettings()
    {
        var version = Guid.NewGuid();
        string json = EngineStrategySettings.Serialize(SystemShadowPolicyConfig.Default);
        var result = AiShadowPolicyIdentity.Read(version, EngineStrategySettings.Shadow, json, EngineStrategySettings.Hash(json), null);
        Assert.Equal(version, result.Id);
        Assert.Equal(SystemShadowPolicyConfig.Default, result.Policy);
    }
}
