using Core.Runtime;
using Core.ML.Engine.Profit;
using Core.Trader;
using Core.Trader.Promotion;
using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TradingStrategyResolutionTests
{
    [Fact]
    public void NoApprovalDoesNotResolveDailyModelsOrPaperChampion() =>
        Assert.Null(TradingStrategyResolution.AtBoundary([], "TFSA", Utc));

    [Fact]
    public void ExactBoundaryAndAccountDetermineTheRecommendationSource()
    {
        var first = Assignment(Strategy(), Utc);
        var second = Assignment(Strategy(), Utc.AddMinutes(1), first.PromotionId);
        var other = Assignment(Strategy(), Utc) with { AccountLabel = "RRSP" };
        ImmutableArray<TradingStrategyAssignment> history = [other, second, first];
        Assert.Null(TradingStrategyResolution.AtBoundary(history, "TFSA", Utc.AddTicks(-1)));
        Assert.Equal(first, TradingStrategyResolution.AtBoundary(history, "tfsa", Utc));
        Assert.Equal(first, TradingStrategyResolution.AtBoundary(history, "TFSA", second.EffectiveUtc.AddTicks(-1)));
        Assert.Equal(second, TradingStrategyResolution.AtBoundary(history, "TFSA", second.EffectiveUtc));
        Assert.Equal(other, TradingStrategyResolution.AtBoundary(history, "RRSP", second.EffectiveUtc));
    }

    [Fact]
    public void ReturningToEarlierVersionAppendsHistory()
    {
        var first = Assignment(Strategy(), Utc);
        var second = Assignment(Strategy(), Utc.AddMinutes(1), first.PromotionId);
        var back = Assignment(first.Strategy, Utc.AddMinutes(2), second.PromotionId);
        var history = ImmutableArray.Create(first, second, back);
        Assert.Equal(back, TradingStrategyResolution.AtBoundary(history, "TFSA", back.EffectiveUtc));
        Assert.Equal(first, TradingStrategyResolution.AtBoundary(history, "TFSA", Utc));
        Assert.NotEqual(first.PromotionId, back.PromotionId);
    }

    [Fact]
    public void BrokenPriorChainAndCollidingBoundariesAreRejected()
    {
        var first = Assignment(Strategy(), Utc);
        var broken = Assignment(Strategy(), Utc.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => TradingStrategyResolution.AtBoundary([first, broken], "TFSA", Utc));
        var colliding = Assignment(Strategy(), Utc, first.PromotionId);
        Assert.Throws<InvalidOperationException>(() => TradingStrategyResolution.AtBoundary([first, colliding], "TFSA", Utc));
    }

    [Fact]
    public void CompleteIdentityIncludesSelectionPolicyModelsAndCode()
    {
        var strategy = Strategy();
        strategy.Validate();
        Assert.NotEqual(strategy.Fingerprint, (strategy with { MaximumHoldings = 5 }).Fingerprint);
        Assert.NotEqual(strategy.Fingerprint, (strategy with { Selection = "FrozenDaily:Breakout" }).Fingerprint);
        Assert.NotEqual(strategy.Fingerprint, (strategy with { ImplementationIdentity = "different-code" }).Fingerprint);
        Assert.NotEqual(strategy.Fingerprint, (strategy with { ShadowPolicy = new SystemShadowPolicyConfig { HardLossFraction = .04m } }).Fingerprint);
        Assert.NotEqual(strategy.Fingerprint, (strategy with { Daily = strategy.Daily with { ModelSetSha256 = new string('B',64) } }).Fingerprint);
        Assert.Equal(strategy.Fingerprint, (strategy with { Name = "A clearer display name", VersionId = Guid.NewGuid() }).Fingerprint);
    }

    [Fact]
    public void IncompleteModelSetAndCrossFamilyPolicyAreRejected()
    {
        var strategy = Strategy();
        Assert.Throws<ArgumentException>(() => (strategy with { Daily = strategy.Daily with { Models = [] } }).Validate());
        Assert.Throws<ArgumentException>(() => (strategy with { Selection = CompleteTradingStrategy.LiveSelection }).Validate());
        Assert.Throws<ArgumentException>(() => (strategy with { RecommendationContract = "BrokerExecution" }).Validate());
    }
    [Fact]
    public void FrozenDailyRunMustMatchExactArtifactsAndConfigurationNotJustTheAssignmentName()
    {
        var daily = Strategy().Daily;
        Assert.True(daily.Matches(daily.Configuration, daily.Models.Reverse().ToImmutableArray()));
        Assert.False(daily.Matches(daily.Configuration with { MinCompositeScore = .99 }, daily.Models));
        Assert.False(daily.Matches(daily.Configuration, daily.Models.SetItem(0,daily.Models[0] with { ArtifactSha256 = new string('B',64) })));
        Assert.False(daily.Matches(daily.Configuration, daily.Models.SetItem(0,daily.Models[0] with { ModelId = Guid.NewGuid() })));
        Assert.False(daily.Matches(daily.Configuration, daily.Models.RemoveAt(0)));
    }

    internal static readonly DateTime Utc = new(2026, 9, 8, 14, 0, 0, DateTimeKind.Utc);
    internal static CompleteTradingStrategy Strategy() => new(Guid.NewGuid(), Guid.NewGuid(), "Continuation Top 3",
        TradingStrategyFamily.DailyShadow,
        new(Guid.NewGuid(), StrategyConfig.Default, new string('A',64), "DatedInputs",
            ProfitModelRegistry.All.Select(m => new TradingModelIdentity(Guid.NewGuid(), m.TaskType, new string('A',64))).ToImmutableArray()),
        Guid.NewGuid(), SystemShadowPolicyConfig.Version1, null,
        "FrozenDaily:Continuation", 3, "reviewed-code", CompleteTradingStrategy.Contract);
    internal static TradingStrategyAssignment Assignment(CompleteTradingStrategy strategy, DateTime boundary, Guid? prior = null) =>
        new(Guid.NewGuid(), "TFSA", strategy, Guid.NewGuid(), Guid.NewGuid(), prior, boundary, "reviewer", "Reviewed evidence");
}

public sealed class SystemShadowProtectionTests
{
    [Fact]
    public void SharedCalculationRetainsHardLossPriorityAndUpdatesTrail()
    {
        var time = TradingStrategyResolutionTests.Utc;
        var result = SystemShadowProtection.Evaluate(SystemShadowTrailingState.Open(100m),
            new(time, 94m, 106m), new(time, 99m, 106m), null, SystemShadowPolicyConfig.Version1);
        Assert.Equal(SystemShadowExitReason.HardLoss, result.ExitReason);
        Assert.True(result.State.ProfitProtectionArmed);
        Assert.Equal(106m, result.State.HighestCompletedFifteenMinuteClose);
    }

    [Fact]
    public void AssignmentDoesNotApplyNewFloorToEarlierIntrabarLow()
    {
        var time = TradingStrategyResolutionTests.Utc;
        var result = SystemShadowProtection.Evaluate(SystemShadowTrailingState.Open(100m),
            new(time, 94m, 100m), null, time.AddMinutes(1), SystemShadowPolicyConfig.Version1);
        Assert.Equal(SystemShadowExitReason.None, result.ExitReason);
    }

    [Fact]
    public void RepeatedFifteenMinuteEvidenceCannotRaiseFloorOrTriggerItTwice()
    {
        var time = TradingStrategyResolutionTests.Utc;
        var result = SystemShadowProtection.Evaluate(SystemShadowTrailingState.Open(100m),
            new(time, 104m, 106m), new(time, 100m, 106m), null, SystemShadowPolicyConfig.Version1);
        var repeat = SystemShadowProtection.Evaluate(result.State,
            new(time.AddMinutes(5), 106m, 107m), new(time, 100m, 106m), null, SystemShadowPolicyConfig.Version1);
        Assert.Equal(result.State, repeat.State);
        Assert.Equal(SystemShadowExitReason.None, repeat.ExitReason);
    }
}
