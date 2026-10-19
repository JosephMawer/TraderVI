#nullable enable
using Core.Trader.DelphiLive;
using System;

namespace Core.Trader.Promotion;

/// <summary>Bind shared market facts to the approved Live source without importing paper lifecycle.</summary>
public static class BrokerLiveObservationPolicy
{
    public static TradingLiveObservation Bind(DelphiLiveEvaluationInput input, int epoch,
        CompleteTradingStrategy strategy, PaperComparisonAccount? comparison, bool hasConflict)
    {
        if (strategy.Family != TradingStrategyFamily.DelphiLive || input.Policy.PolicyVersionId != strategy.PolicyVersionId || epoch < 1)
            throw new InvalidOperationException("Live observations must use the exact approved policy and continuity epoch.");
        if (input.DailySetup is { } daily)
        {
            if (daily.DailyStrategyVersionId != strategy.Daily.StrategyVersionId)
                throw new InvalidOperationException("Live daily evidence differs from the approved daily strategy.");
            if (comparison is not null)
            {
                var admission = daily.ComparisonAdmission
                    ?? throw new InvalidOperationException("The selected comparison has no identified admission evidence.");
                admission.Validate(daily.DelphiRunId, daily.DailyStrategyVersionId, strategy.PolicyVersionId, input.EvaluatedUtc);
                if (admission.AccountId != comparison.AccountId || admission.BatchId != comparison.BatchId ||
                    admission.PairId != comparison.PairId || admission.Profile != comparison.Profile)
                    throw new InvalidOperationException("Live admission belongs to another account or profile.");
            }
            else if (daily.ComparisonAdmission is not null)
                throw new InvalidOperationException("An original Live strategy cannot inherit a comparison admission.");
        }
        // Do not drop the latest conflicted observation and fall back to an earlier confirmation.
        // Market lifecycle will record the miss; actual holdings/protection are supplied separately.
        return new(input with { ExactPairPersistedOnTime = input.ExactPairPersistedOnTime && !hasConflict,
            IsHeld = false, HasPendingBuy = false, HasPendingSell = false, AveragePurchasePrice = null,
            ProfitProtection = null, PreviousState = DelphiLiveEvaluationState.Initial(input.DailySetup is not null) }, epoch);
    }
}
