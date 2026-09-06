#nullable enable

using System;

namespace Core.Trader;

public static class SystemShadowVersions
{
    public const string Policy = "SystemShadowV1";
    public const string SelectionActor = "System";
    public const string ExecutionMode = "Ghost";
}

public sealed record SystemShadowPolicyConfig
{
    public decimal InitialAllocationFraction { get; init; } = 0.75m;
    public decimal AddOnAllocationFraction { get; init; } = 0.25m;
    public decimal HardLossFraction { get; init; } = 0.05m;
    public decimal TrailingLossFraction { get; init; } = 0.05m;
    public decimal DailyLossGuardFraction { get; init; } = 0.03m;
    public decimal DrawdownReviewFraction { get; init; } = 0.10m;
    public decimal EntryFrictionRate { get; init; } = 0.0025m;
    public decimal ExitFrictionRate { get; init; } = 0.0025m;
    public int MaximumSameDayEntriesPerSymbol { get; init; } = 2;

    public static SystemShadowPolicyConfig Version1 { get; } = new();
}

public enum SystemShadowEntryReason
{
    Qualified,
    BelowPreviousSessionClose,
    FallingFromPreviousFiveMinuteClose,
    MissingEvidence,
    LateEvidence,
    ConflictingEvidence,
    MarketClosed
}

public sealed record SystemShadowEntryEvidence(
    decimal PreviousSessionClose,
    decimal PreviousFiveMinuteClose,
    decimal LatestFiveMinuteClose,
    DateTime LatestBarEndUtc,
    DateTime ReceivedUtc,
    bool IsComplete = true,
    bool IsLate = false,
    bool IsConflicting = false);

public sealed record SystemShadowEntryDecision(
    bool IsEligible,
    SystemShadowEntryReason Reason);

public enum SystemShadowExitReason
{
    None,
    HardLoss,
    TrailingProfit,
    SessionTwoUnprofitable
}

public sealed record SystemShadowTrailingState(
    decimal AverageCost,
    decimal HighestCompletedFifteenMinuteClose,
    bool ProfitProtectionArmed,
    decimal? TrailingStopPrice,
    DateTime? LastProcessedFifteenMinuteBarUtc = null)
{
    public static SystemShadowTrailingState Open(decimal averageCost)
    {
        if (averageCost <= 0m)
            throw new ArgumentOutOfRangeException(nameof(averageCost));
        return new SystemShadowTrailingState(averageCost, averageCost, false, null);
    }
}

public sealed record SystemShadowTrailingDecision(
    SystemShadowTrailingState State,
    SystemShadowExitReason ExitReason,
    decimal? TriggerPrice);

public sealed record SystemShadowGuardDecision(
    bool DailyBuyingPaused,
    bool CapitalReviewRequired,
    decimal DailyReturn,
    decimal DrawdownFromHighWater);

public static class SystemShadowPolicy
{
    public static SystemShadowEntryDecision EvaluateEntry(
        SystemShadowEntryEvidence? evidence)
    {
        if (evidence is null || !evidence.IsComplete)
            return new(false, SystemShadowEntryReason.MissingEvidence);
        ValidateEvidence(evidence);
        if (evidence.IsConflicting)
            return new(false, SystemShadowEntryReason.ConflictingEvidence);
        if (evidence.IsLate)
            return new(false, SystemShadowEntryReason.LateEvidence);
        if (evidence.LatestFiveMinuteClose < evidence.PreviousSessionClose)
            return new(false, SystemShadowEntryReason.BelowPreviousSessionClose);
        if (evidence.LatestFiveMinuteClose < evidence.PreviousFiveMinuteClose)
            return new(false, SystemShadowEntryReason.FallingFromPreviousFiveMinuteClose);
        return new(true, SystemShadowEntryReason.Qualified);
    }

    public static decimal PositionTarget(
        decimal portfolioValue,
        int maximumPositions)
    {
        if (portfolioValue <= 0m)
            throw new ArgumentOutOfRangeException(nameof(portfolioValue));
        if (maximumPositions is not (3 or 5))
            throw new ArgumentOutOfRangeException(nameof(maximumPositions));
        return decimal.Round(
            portfolioValue / maximumPositions,
            2,
            MidpointRounding.ToEven);
    }

    public static decimal InitialBudget(
        decimal positionTarget,
        SystemShadowPolicyConfig? config = null)
    {
        config ??= SystemShadowPolicyConfig.Version1;
        ValidateConfig(config);
        if (positionTarget <= 0m)
            throw new ArgumentOutOfRangeException(nameof(positionTarget));
        return decimal.Round(
            positionTarget * config.InitialAllocationFraction,
            2,
            MidpointRounding.ToEven);
    }

    public static decimal AddOnBudget(
        decimal positionTarget,
        SystemShadowPolicyConfig? config = null)
    {
        config ??= SystemShadowPolicyConfig.Version1;
        ValidateConfig(config);
        if (positionTarget <= 0m)
            throw new ArgumentOutOfRangeException(nameof(positionTarget));
        return decimal.Round(
            positionTarget * config.AddOnAllocationFraction,
            2,
            MidpointRounding.ToEven);
    }

    public static int WholeSharesForBuy(
        decimal budget,
        decimal rawFillPrice,
        SystemShadowPolicyConfig? config = null)
    {
        config ??= SystemShadowPolicyConfig.Version1;
        ValidateConfig(config);
        if (budget < 0m)
            throw new ArgumentOutOfRangeException(nameof(budget));
        decimal adjusted = AdjustedBuyPrice(rawFillPrice, config);
        return decimal.ToInt32(decimal.Floor(budget / adjusted));
    }

    public static decimal AdjustedBuyPrice(
        decimal rawFillPrice,
        SystemShadowPolicyConfig? config = null)
    {
        config ??= SystemShadowPolicyConfig.Version1;
        ValidateConfig(config);
        RequirePrice(rawFillPrice, nameof(rawFillPrice));
        return decimal.Round(
            rawFillPrice * (1m + config.EntryFrictionRate),
            6,
            MidpointRounding.AwayFromZero);
    }

    public static decimal AdjustedSellPrice(
        decimal rawFillPrice,
        SystemShadowPolicyConfig? config = null)
    {
        config ??= SystemShadowPolicyConfig.Version1;
        ValidateConfig(config);
        RequirePrice(rawFillPrice, nameof(rawFillPrice));
        return decimal.Round(
            rawFillPrice * (1m - config.ExitFrictionRate),
            6,
            MidpointRounding.AwayFromZero);
    }

    public static decimal BreakEvenExitPrice(
        decimal averageCost,
        SystemShadowPolicyConfig? config = null)
    {
        config ??= SystemShadowPolicyConfig.Version1;
        ValidateConfig(config);
        RequirePrice(averageCost, nameof(averageCost));
        return averageCost / (1m - config.ExitFrictionRate);
    }

    public static decimal HardStopPrice(
        decimal averageCost,
        SystemShadowPolicyConfig? config = null)
    {
        config ??= SystemShadowPolicyConfig.Version1;
        ValidateConfig(config);
        RequirePrice(averageCost, nameof(averageCost));
        return decimal.Round(
            averageCost * (1m - config.HardLossFraction),
            6,
            MidpointRounding.AwayFromZero);
    }

    public static SystemShadowExitReason EvaluateFiveMinuteRisk(
        decimal averageCost,
        decimal completedBarLow,
        decimal? existingTrailingStop,
        SystemShadowPolicyConfig? config = null)
    {
        config ??= SystemShadowPolicyConfig.Version1;
        ValidateConfig(config);
        RequirePrice(averageCost, nameof(averageCost));
        RequirePrice(completedBarLow, nameof(completedBarLow));
        if (completedBarLow <= HardStopPrice(averageCost, config))
            return SystemShadowExitReason.HardLoss;
        if (existingTrailingStop is > 0m && completedBarLow <= existingTrailingStop.Value)
            return SystemShadowExitReason.TrailingProfit;
        return SystemShadowExitReason.None;
    }

    public static SystemShadowTrailingDecision EvaluateFifteenMinuteClose(
        SystemShadowTrailingState state,
        DateTime completedBarStartUtc,
        decimal completedBarLow,
        decimal completedBarClose,
        SystemShadowPolicyConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        config ??= SystemShadowPolicyConfig.Version1;
        ValidateConfig(config);
        if (completedBarStartUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Completed bar start must be UTC.", nameof(completedBarStartUtc));
        RequirePrice(completedBarLow, nameof(completedBarLow));
        RequirePrice(completedBarClose, nameof(completedBarClose));

        if (state.LastProcessedFifteenMinuteBarUtc is DateTime processedUtc &&
            completedBarStartUtc <= processedUtc)
        {
            return new(state, SystemShadowExitReason.None, null);
        }

        SystemShadowTrailingState processedState = state with
        {
            LastProcessedFifteenMinuteBarUtc = completedBarStartUtc
        };

        if (state.ProfitProtectionArmed &&
            state.TrailingStopPrice is decimal establishedTrail &&
            completedBarLow <= establishedTrail)
        {
            return new(processedState, SystemShadowExitReason.TrailingProfit, establishedTrail);
        }

        decimal breakEven = BreakEvenExitPrice(state.AverageCost, config);
        decimal highWater = System.Math.Max(
            state.HighestCompletedFifteenMinuteClose,
            completedBarClose);
        bool armed = state.ProfitProtectionArmed || completedBarClose >= breakEven;
        decimal? trail = state.TrailingStopPrice;
        if (armed)
        {
            decimal candidate = System.Math.Max(
                breakEven,
                highWater * (1m - config.TrailingLossFraction));
            trail = trail.HasValue ? System.Math.Max(trail.Value, candidate) : candidate;
        }

        return new(
            processedState with
            {
                HighestCompletedFifteenMinuteClose = highWater,
                ProfitProtectionArmed = armed,
                TrailingStopPrice = trail
            },
            SystemShadowExitReason.None,
            null);
    }

    public static bool ShouldReplaceAtSessionTwoOpening(
        decimal latestPrice,
        decimal averageCost,
        SystemShadowEntryDecision incumbentMomentum,
        bool contenderQualifies,
        int tradingSessionOrdinal,
        SystemShadowPolicyConfig? config = null)
    {
        config ??= SystemShadowPolicyConfig.Version1;
        ValidateConfig(config);
        RequirePrice(latestPrice, nameof(latestPrice));
        RequirePrice(averageCost, nameof(averageCost));
        return tradingSessionOrdinal == 2 &&
               latestPrice < BreakEvenExitPrice(averageCost, config) &&
               !incumbentMomentum.IsEligible &&
               contenderQualifies;
    }

    public static bool ShouldExitAtSessionTwoClose(
        decimal closingPrice,
        decimal averageCost,
        int tradingSessionOrdinal,
        SystemShadowPolicyConfig? config = null)
    {
        config ??= SystemShadowPolicyConfig.Version1;
        ValidateConfig(config);
        RequirePrice(closingPrice, nameof(closingPrice));
        RequirePrice(averageCost, nameof(averageCost));
        return tradingSessionOrdinal >= 2 &&
               closingPrice < BreakEvenExitPrice(averageCost, config);
    }

    public static SystemShadowGuardDecision EvaluateGuards(
        decimal currentValue,
        decimal sessionOpeningValue,
        decimal highestClosingValue,
        SystemShadowPolicyConfig? config = null)
    {
        config ??= SystemShadowPolicyConfig.Version1;
        ValidateConfig(config);
        RequirePrice(currentValue, nameof(currentValue));
        RequirePrice(sessionOpeningValue, nameof(sessionOpeningValue));
        RequirePrice(highestClosingValue, nameof(highestClosingValue));
        decimal dailyReturn = currentValue / sessionOpeningValue - 1m;
        decimal drawdown = currentValue / highestClosingValue - 1m;
        return new(
            dailyReturn <= -config.DailyLossGuardFraction,
            drawdown <= -config.DrawdownReviewFraction,
            dailyReturn,
            drawdown);
    }

    public static DateTime EarliestFiveMinuteFillBoundary(DateTime signalReceivedUtc)
    {
        if (signalReceivedUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Signal receipt must be UTC.", nameof(signalReceivedUtc));
        DateTime minute = new(
            signalReceivedUtc.Year,
            signalReceivedUtc.Month,
            signalReceivedUtc.Day,
            signalReceivedUtc.Hour,
            signalReceivedUtc.Minute,
            0,
            DateTimeKind.Utc);
        int remainder = minute.Minute % 5;
        DateTime boundary = remainder == 0 ? minute : minute.AddMinutes(5 - remainder);
        if (boundary <= signalReceivedUtc)
            boundary = boundary.AddMinutes(5);
        return boundary;
    }

    public static DateTime EarliestObservableFillBoundary(
        DateTime signalReceivedUtc,
        DateTime hostStartedUtc)
    {
        if (hostStartedUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Host start must be UTC.", nameof(hostStartedUtc));
        DateTime signalBoundary = EarliestFiveMinuteFillBoundary(signalReceivedUtc);
        DateTime hostBoundary = EarliestFiveMinuteFillBoundary(hostStartedUtc);
        return signalBoundary > hostBoundary ? signalBoundary : hostBoundary;
    }

    public static SystemShadowPendingBuyAction EvaluatePendingBuyFill(
        DateTime earliestFillUtc,
        DateTime hostStartedUtc,
        DateTime? latestCompletedFiveMinuteBarUtc)
    {
        if (earliestFillUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Earliest fill must be UTC.", nameof(earliestFillUtc));
        if (hostStartedUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Host start must be UTC.", nameof(hostStartedUtc));
        if (latestCompletedFiveMinuteBarUtc is DateTime latestUtc && latestUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Completed bar start must be UTC.", nameof(latestCompletedFiveMinuteBarUtc));

        if (EarliestFiveMinuteFillBoundary(hostStartedUtc) > earliestFillUtc)
            return SystemShadowPendingBuyAction.Requalify;
        if (!latestCompletedFiveMinuteBarUtc.HasValue ||
            latestCompletedFiveMinuteBarUtc.Value < earliestFillUtc)
        {
            return SystemShadowPendingBuyAction.Wait;
        }
        return latestCompletedFiveMinuteBarUtc.Value == earliestFillUtc
            ? SystemShadowPendingBuyAction.Fill
            : SystemShadowPendingBuyAction.Requalify;
    }

    public static bool CanEnterAgainToday(
        int entriesAlreadyMadeToday,
        bool mostRecentExitWasPriceBased,
        SystemShadowPolicyConfig? config = null)
    {
        config ??= SystemShadowPolicyConfig.Version1;
        ValidateConfig(config);
        if (entriesAlreadyMadeToday < 0)
            throw new ArgumentOutOfRangeException(nameof(entriesAlreadyMadeToday));
        return entriesAlreadyMadeToday == 0 ||
               (entriesAlreadyMadeToday < config.MaximumSameDayEntriesPerSymbol &&
                mostRecentExitWasPriceBased);
    }

    private static void ValidateEvidence(SystemShadowEntryEvidence evidence)
    {
        RequirePrice(evidence.PreviousSessionClose, nameof(evidence.PreviousSessionClose));
        RequirePrice(evidence.PreviousFiveMinuteClose, nameof(evidence.PreviousFiveMinuteClose));
        RequirePrice(evidence.LatestFiveMinuteClose, nameof(evidence.LatestFiveMinuteClose));
        if (evidence.LatestBarEndUtc.Kind != DateTimeKind.Utc ||
            evidence.ReceivedUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Evidence timestamps must be UTC.", nameof(evidence));
        if (evidence.ReceivedUtc < evidence.LatestBarEndUtc)
            throw new ArgumentException("Evidence cannot be received before its bar completes.", nameof(evidence));
    }

    private static void ValidateConfig(SystemShadowPolicyConfig config)
    {
        if (config.InitialAllocationFraction <= 0m ||
            config.AddOnAllocationFraction <= 0m ||
            config.InitialAllocationFraction + config.AddOnAllocationFraction != 1m)
            throw new ArgumentOutOfRangeException(nameof(config), "Allocation tranches must be positive and total one.");
        if (config.HardLossFraction <= 0m || config.HardLossFraction >= 1m ||
            config.TrailingLossFraction <= 0m || config.TrailingLossFraction >= 1m ||
            config.DailyLossGuardFraction <= 0m || config.DailyLossGuardFraction >= 1m ||
            config.DrawdownReviewFraction <= 0m || config.DrawdownReviewFraction >= 1m ||
            config.EntryFrictionRate < 0m || config.EntryFrictionRate >= 1m ||
            config.ExitFrictionRate < 0m || config.ExitFrictionRate >= 1m ||
            config.MaximumSameDayEntriesPerSymbol < 1)
            throw new ArgumentOutOfRangeException(nameof(config), "Risk, friction, or re-entry settings are invalid.");
    }

    private static void RequirePrice(decimal value, string parameterName)
    {
        if (value <= 0m)
            throw new ArgumentOutOfRangeException(parameterName, "Price or value must be positive.");
    }
}
