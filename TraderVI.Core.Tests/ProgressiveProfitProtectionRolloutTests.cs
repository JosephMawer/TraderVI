#nullable enable
using Core.Runtime;
using Core.Trader;
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class ProgressiveProfitProtectionRolloutTests
{
    [Fact]
    public void RolloutChangesOnlyTheOptInAndProspectiveLiveIdentity()
    {
        var old = DelphiLivePolicyDefinition.Version1;
        var next = old with { PolicyVersionId = Guid.NewGuid(), UseProgressiveProfitProtection = true };
        ProgressiveProfitProtectionRollout.ValidateChange(old, next);
        Should.Throw<ArgumentException>(() => ProgressiveProfitProtectionRollout.ValidateChange(old, next with { DailyLossGuardFraction = 0.07m }));
        Should.Throw<ArgumentException>(() => ProgressiveProfitProtectionRollout.ValidateChange(old, next with { UseProgressiveProfitProtection = false }));
        var shadow = SystemShadowPolicyConfig.Version1;
        ProgressiveProfitProtectionRollout.ValidateChange(shadow, shadow with { UseProgressiveProfitProtection = true });
        Should.Throw<ArgumentException>(() => ProgressiveProfitProtectionRollout.ValidateChange(shadow,
            shadow with { UseProgressiveProfitProtection = true, TrailingLossFraction = 0.10m }));
    }

    [Fact]
    public void DefaultAndFrozenPoliciesRemainOffAndFalseIsAbsentFromSavedJson()
    {
        DelphiLivePolicyDefinition.Version1.UseProgressiveProfitProtection.ShouldBeFalse();
        DelphiLivePolicyDefinition.Default.UseProgressiveProfitProtection.ShouldBeFalse();
        SystemShadowPolicyConfig.Version1.UseProgressiveProfitProtection.ShouldBeFalse();
        SystemShadowPolicyConfig.Default.UseProgressiveProfitProtection.ShouldBeFalse();
        EngineStrategySettings.Serialize(DelphiLivePolicyDefinition.Version1).ShouldNotContain("UseProgressiveProfitProtection");
        EngineStrategySettings.Serialize(SystemShadowPolicyConfig.Version1).ShouldNotContain("UseProgressiveProfitProtection");
    }

    [Fact]
    public void StrongerExistingStopsSurviveBothEngineHandovers()
    {
        var at = new DateTime(2026, 10, 16, 14, 0, 0, DateTimeKind.Utc);
        var live = DelphiLiveProfitProtectionState.Open(Guid.NewGuid(), 100m) with
            { HighestCompletedFiveMinuteClose = 105m, FloorPrice = 104.90m, FloorPersistedUtc = at.AddMinutes(-1), Stage = DelphiLiveProfitProtectionStage.Trailing };
        var next = EngineStrategyReassignment.Rebase(live, DelphiLivePolicyDefinition.Version1 with
            { PolicyVersionId = Guid.NewGuid(), UseProgressiveProfitProtection = true }, at);
        next.FloorPrice.ShouldBe(104.90m);
        next.Progressive!.TightestGap.ShouldBe(0.005m);
        var shadow = SystemShadowTrailingState.Open(100m) with
            { HighestCompletedFifteenMinuteClose = 105m, TrailingStopPrice = 104.90m, ProfitProtectionArmed = true };
        EngineStrategyReassignment.Rebase(shadow, SystemShadowPolicyConfig.Version1 with
            { UseProgressiveProfitProtection = true }).TrailingStopPrice.ShouldBe(104.90m);
    }
}
