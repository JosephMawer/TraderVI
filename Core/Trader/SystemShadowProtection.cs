#nullable enable
using System;

namespace Core.Trader;

public sealed record SystemShadowCompletedPrice(DateTime StartUtc, decimal Low, decimal Close);
public sealed record SystemShadowProtectionDecision(SystemShadowTrailingState State, SystemShadowExitReason ExitReason);

/// <summary>
/// The same deterministic protection calculation can serve a paper holding or a reported Real holding.
/// It produces a reason and derived state; the caller owns persistence and execution authority.
/// </summary>
public static class SystemShadowProtection
{
    public static SystemShadowProtectionDecision Evaluate(SystemShadowTrailingState state,
        SystemShadowCompletedPrice fiveMinute, SystemShadowCompletedPrice? fifteenMinute,
        DateTime? assignedUtc, SystemShadowPolicyConfig policy)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(fiveMinute);
        if (fiveMinute.StartUtc.Kind != DateTimeKind.Utc ||
            fifteenMinute is { StartUtc.Kind: not DateTimeKind.Utc } ||
            assignedUtc is { Kind: not DateTimeKind.Utc })
            throw new ArgumentException("Protection evidence and assignment boundaries must be UTC.");
        var reason = SystemShadowPolicy.EvaluateFiveMinuteRisk(state.AverageCost,
            fiveMinute.StartUtc < assignedUtc ? fiveMinute.Close : fiveMinute.Low, state.TrailingStopPrice, policy);
        if (fifteenMinute is not null)
        {
            var trail = SystemShadowPolicy.EvaluateFifteenMinuteClose(state, fifteenMinute.StartUtc,
                fifteenMinute.StartUtc < assignedUtc ? fifteenMinute.Close : fifteenMinute.Low, fifteenMinute.Close, policy);
            state = trail.State;
            if (reason == SystemShadowExitReason.None) reason = trail.ExitReason;
        }
        return new(state, reason);
    }
}
