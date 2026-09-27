#nullable enable
using Core.Runtime;
using Core.Trader;
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Immutable;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class EngineSettingsTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 15, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(9700, false)]
    [InlineData(9301, false)]
    [InlineData(9300, true)]
    [InlineData(9299, true)]
    public void NewEngineDefaultsPauseBuyingAtSevenPercent(int nav, bool paused)
    {
        var live = (DelphiLivePolicyDefinition)EngineStrategySettings.Defaults(EngineStrategySettings.Live);
        var shadow = (SystemShadowPolicyConfig)EngineStrategySettings.Defaults(EngineStrategySettings.Shadow);
        var liveGuard = DelphiLivePortfolioPolicy.EvaluateGuards(nav, 10_000m, 10_000m, false, false, live);
        var shadowGuard = SystemShadowPolicy.EvaluateGuards(nav, 10_000m, 10_000m, shadow);
        liveGuard.DailyBuyingPaused.ShouldBe(paused);
        shadowGuard.DailyBuyingPaused.ShouldBe(paused);
        liveGuard.CapitalReviewRequired.ShouldBeFalse();
        shadowGuard.CapitalReviewRequired.ShouldBeFalse();
    }

    [Fact]
    public void NewDefaultsPreserveFrozenPoliciesAndAllOtherSettings()
    {
        var live = (DelphiLivePolicyDefinition)EngineStrategySettings.Defaults(EngineStrategySettings.Live);
        var shadow = (SystemShadowPolicyConfig)EngineStrategySettings.Defaults(EngineStrategySettings.Shadow);
        live.DailyLossGuardFraction.ShouldBe(.07m);
        shadow.DailyLossGuardFraction.ShouldBe(.07m);
        live.PolicyVersionId.ShouldNotBe(DelphiLivePolicyDefinition.Version1.PolicyVersionId);
        (live with { PolicyVersionId = DelphiLivePolicyDefinition.Version1.PolicyVersionId, DailyLossGuardFraction = .03m })
            .ShouldBe(DelphiLivePolicyDefinition.Version1);
        (shadow with { DailyLossGuardFraction = .03m }).ShouldBe(SystemShadowPolicyConfig.Version1);

        var savedLive = (DelphiLivePolicyDefinition)EngineStrategySettings.Read(EngineStrategySettings.Live,
            EngineStrategySettings.Serialize(DelphiLivePolicyDefinition.Version1));
        var savedShadow = (SystemShadowPolicyConfig)EngineStrategySettings.Read(EngineStrategySettings.Shadow,
            EngineStrategySettings.Serialize(SystemShadowPolicyConfig.Version1));
        DelphiLivePortfolioPolicy.EvaluateGuards(9700m, 10_000m, 10_000m, false, false, savedLive).DailyBuyingPaused.ShouldBeTrue();
        SystemShadowPolicy.EvaluateGuards(9700m, 10_000m, 10_000m, savedShadow).DailyBuyingPaused.ShouldBeTrue();
    }

    [Fact]
    public void SevenPercentDefaultDoesNotClearExistingBuyingPause()
    {
        var live = (DelphiLivePolicyDefinition)EngineStrategySettings.Defaults(EngineStrategySettings.Live);
        DelphiLivePortfolioPolicy.EvaluateGuards(9900m, 10_000m, 10_000m, true, false, live).DailyBuyingPaused.ShouldBeTrue();
        var prior = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), Guid.NewGuid(), DelphiLivePolicyDefinition.Version1.PolicyVersionId,
            "OperationalChampion", null, 10_000m, "CAD", DateOnly.FromDateTime(Now), Now, Now.AddDays(-1), "test", "fixture"));
        prior = prior with { Guards = new(true, false, -.03m, -.03m, 10_000m) };
        EngineStrategyReassignment.Rebase(prior, live, Now).Guards.ShouldBe(prior.Guards);
    }

    [Theory]
    [InlineData(EngineStrategySettings.Live)]
    [InlineData(EngineStrategySettings.Shadow)]
    [InlineData(EngineStrategySettings.Tracked)]
    public void DefaultsRoundTripAndEditorCoversOnlyExecutedFields(string family)
    {
        object source = EngineStrategySettings.Defaults(family);
        string json = EngineStrategySettings.Serialize(source);
        var read = EngineStrategySettings.Read(family, json);
        EngineStrategySettings.Validate(family, read);
        var fields = EngineStrategySettings.Fields(family, read);
        fields.ShouldNotBeEmpty();
        fields.Select(f => f.Key).Intersect(["PolicyVersionId", "EvaluatorVersion", "PollIntervalMinutes", "CreatedUtc"]).ShouldBeEmpty();
        Guid id = source is DelphiLivePolicyDefinition live ? live.PolicyVersionId : Guid.NewGuid();
        EngineStrategySettings.Serialize(EngineStrategySettings.Edit(family, read, fields, id)).ShouldBe(json);
    }

    [Fact]
    public void EditorRejectsUnknownMissingNonfiniteAndIncompatibleValues()
    {
        var source = EngineStrategySettings.Defaults(EngineStrategySettings.Shadow);
        var fields = EngineStrategySettings.Fields(EngineStrategySettings.Shadow, source);
        Should.Throw<ArgumentException>(() => EngineStrategySettings.Edit(EngineStrategySettings.Shadow, source, fields.Skip(1), Guid.NewGuid()));
        Should.Throw<ArgumentException>(() => EngineStrategySettings.Edit(EngineStrategySettings.Shadow, source,
            fields.Append(new("Unknown", "Unknown", "", "1")), Guid.NewGuid()));
        Should.Throw<ArgumentException>(() => EngineStrategySettings.Edit(EngineStrategySettings.Shadow, source,
            fields.Select(f => f.Key == "HardLossFraction" ? f with { Value = "NaN" } : f), Guid.NewGuid()));
        Should.Throw<ArgumentException>(() => EngineStrategySettings.Edit(EngineStrategySettings.Shadow, source,
            fields.Select(f => f.Key == "HardLossFraction" ? f with { Value = "0,05" } : f), Guid.NewGuid()));
        Should.Throw<ArgumentException>(() => EngineStrategySettings.Validate(EngineStrategySettings.Shadow,
            SystemShadowPolicyConfig.Version1 with { InitialAllocationFraction = .5m }));
        Should.Throw<ArgumentException>(() => EngineStrategySettings.Validate(EngineStrategySettings.Tracked,
            DelayedIntradaySwingPolicyConfig.Version1 with { StrongBreakoutProbability = 1.01 }));
    }

    [Fact]
    public void SavedSnapshotRejectsChecksumChanges()
    {
        string json = EngineStrategySettings.Serialize(SystemShadowPolicyConfig.Version1);
        var saved = new EngineStrategyVersion(Guid.NewGuid(), EngineStrategySettings.Shadow, "Test", json, EngineStrategySettings.Hash(json), Now);
        saved.Read<SystemShadowPolicyConfig>().ShouldBe(SystemShadowPolicyConfig.Version1);
        Should.Throw<InvalidOperationException>(() => (saved with { SettingsJson = json + " " }).Read<SystemShadowPolicyConfig>());
    }

    [Fact]
    public void ShadowNewRulesMayLoosenFloorButCannotEraseObservedHighOrEntry()
    {
        var prior = new SystemShadowTrailingState(100, 120, true, 117.6m, Now.AddMinutes(-15));
        var next = EngineStrategyReassignment.Rebase(prior, SystemShadowPolicyConfig.Version1 with { TrailingLossFraction = .10m });
        next.TrailingStopPrice.ShouldBe(108m);
        next.AverageCost.ShouldBe(prior.AverageCost);
        next.HighestCompletedFifteenMinuteClose.ShouldBe(prior.HighestCompletedFifteenMinuteClose);
        next.LastProcessedFifteenMinuteBarUtc.ShouldBe(prior.LastProcessedFifteenMinuteBarUtc);
        SystemShadowPolicy.EvaluateFiveMinuteRisk(100m, 93m, null,
            SystemShadowPolicyConfig.Version1 with { HardLossFraction = .10m }).ShouldBe(SystemShadowExitReason.None);
        SystemShadowPolicy.EvaluateFiveMinuteRisk(100m, 93m, null).ShouldBe(SystemShadowExitReason.HardLoss);
    }

    [Fact]
    public void LiveReassignmentPreservesFactsAndHistoryButSupersedesBothPendingSides()
    {
        var policy = DelphiLivePolicyDefinition.Version1;
        var prior = DelphiLiveLedgerIntegrity.Create(new(Guid.NewGuid(), Guid.NewGuid(), policy.PolicyVersionId, "OperationalChampion", null,
            1000m, "CAD", DateOnly.FromDateTime(Now), Now, Now.AddDays(-1), "test", "fixture"));
        Guid position = Guid.NewGuid();
        var protection = new DelphiLiveProfitProtectionState(position, 100, 120, DelphiLiveProfitProtectionStage.Trailing, 117.6m, Now.AddMinutes(-5), Now.AddMinutes(-4));
        var holding = new DelphiLiveLedgerPosition(position, "AAA", 2, 100, Now.AddDays(-1), Guid.NewGuid(), "original entry", protection);
        var closed = holding with { PositionId = Guid.NewGuid(), ClosedUtc = Now.AddHours(-1) };
        DelphiLiveLedgerAction Action(string status, DelphiLiveActionSide side) => new(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "AAA", side,
            Now.AddMinutes(-3), Now.AddMinutes(-2), null, 2, null), position, null, DateOnly.FromDateTime(Now), Guid.NewGuid(), "OriginalReason", "original dossier", status, 1, null, null, null, []);
        prior = prior with { Cash = 800, Positions = [holding, closed], Actions = [Action("Pending", DelphiLiveActionSide.Buy),
            Action("ExitPendingOvernight", DelphiLiveActionSide.Sell), Action("Filled", DelphiLiveActionSide.Sell)],
            Guards = new(true, true, -.04m, -.12m, 1100m) };
        var changed = policy with { PolicyVersionId = Guid.NewGuid(), TrailingDistanceFraction = .10m, DailyLossGuardFraction = .08m, CapitalReviewDrawdownFraction = .20m };
        var next = EngineStrategyReassignment.Rebase(prior, changed, Now);
        next.PolicyVersionId.ShouldBe(changed.PolicyVersionId);
        next.Cash.ShouldBe(prior.Cash); next.StartingCapital.ShouldBe(prior.StartingCapital);
        next.Fills.ShouldBe(prior.Fills); next.Marks.ShouldBe(prior.Marks); next.Quotes.ShouldBe(prior.Quotes);
        next.Positions[1].ShouldBe(closed);
        next.Positions[0].ShouldBe(holding with { Protection = next.Positions[0].Protection });
        next.Positions[0].Protection.HighestCompletedFiveMinuteClose.ShouldBe(120m);
        next.Positions[0].Protection.FloorPrice.ShouldBe(108m);
        next.PendingActions.ShouldBeEmpty();
        next.Actions[0].TerminalReason.ShouldBe("StrategyReassigned"); next.Actions[1].TerminalReason.ShouldBe("StrategyReassigned");
        next.Actions[2].ShouldBe(prior.Actions[2]); next.Guards.ShouldBe(prior.Guards);
        next.Revision.ShouldBe(prior.Revision + 1);
        Should.Throw<InvalidOperationException>(() => DelphiLiveLedgerIntegrity.ValidateTransition(prior, next));
    }

    [Fact]
    public void TrackedCutoverDoesNotApplyANewFloorToPastLowsButLaterBarsKeepTheirLows()
    {
        var opened = IntradaySwingPositionState.Open(100, Now.AddHours(-1));
        var before = new DelayedIntradayBar(Now.AddMinutes(-15), Now, Now.AddMinutes(15), 1, false, 110, 121, 90, 120, 10);
        var after = new DelayedIntradayBar(Now, Now.AddMinutes(15), Now.AddMinutes(30), 1, false, 120, 120, 105, 115, 10);
        var config = DelayedIntradaySwingPolicyConfig.Version1 with { TrailingLossFraction = .10m };
        var replay = EngineStrategyReassignment.PrepareTrackedReplay(opened, 125m, [before, after], Now, config);
        replay.State.HighestCompletedClose.ShouldBe(125m);
        replay.Bars[0].Low.ShouldBe(before.Close);
        replay.Bars[1].ShouldBe(after);
        var first = DelayedIntradaySwingExitPolicy.Evaluate(replay.State, replay.Bars[0], config: config);
        first.Directive.ShouldBe(IntradaySwingDirective.Hold);
        DelayedIntradaySwingExitPolicy.Evaluate(first.State, replay.Bars[1], config: config).Reason.ShouldBe(IntradaySwingReason.TrailingProfit);
    }
}
