#nullable enable
using Core.Trader;
using Core.Trader.DelphiLive;
using System;

namespace Core.Runtime;

/// <summary>The explicit ADR-0090 exception changes profit protection only, including fixed-gate accounts.</summary>
public static class ProgressiveProfitProtectionRollout
{
    public static void ValidateChange(object original, object proposed)
    {
        object normalized = (original, proposed) switch
        {
            (SystemShadowPolicyConfig old, SystemShadowPolicyConfig { UseProgressiveProfitProtection: true } next) =>
                next with { UseProgressiveProfitProtection = old.UseProgressiveProfitProtection },
            (DelphiLivePolicyDefinition old, DelphiLivePolicyDefinition { UseProgressiveProfitProtection: true } next)
                when old.PolicyVersionId != next.PolicyVersionId => next with
                { PolicyVersionId = old.PolicyVersionId, UseProgressiveProfitProtection = old.UseProgressiveProfitProtection },
            _ => throw new ArgumentException("This rollout can only enable progressive protection in a prospective strategy version.")
        };
        if (EngineStrategySettings.Serialize(original) != EngineStrategySettings.Serialize(normalized))
            throw new ArgumentException("The profit-protection rollout cannot change any other saved strategy rule.");
    }
}
