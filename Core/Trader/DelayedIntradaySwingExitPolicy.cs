#nullable enable

using System;

namespace Core.Trader;

public enum IntradaySwingDirective
{
    Hold,
    HoldUnderStrongBreakoutException,
    ExitAlert
}

public enum IntradaySwingReason
{
    None,
    StrongBreakoutException,
    ConditionalLossLimit,
    AbsoluteLossLimit,
    TrailingProfit,
    FiveSessionUnprofitable,
    TenSessionMaximum
}

/// <summary>
/// Version-1 paper defaults from ADR-0028. These values describe a challenger policy;
/// they do not activate live trading or change Delphi's ranking behavior.
/// </summary>
public sealed record DelayedIntradaySwingPolicyConfig
{
    public int PollIntervalMinutes { get; init; } = 15;
    public int ExpectedSourceDelayMinutes { get; init; } = 15;
    public int LateDataAgeMinutes { get; init; } = 45;
    public decimal ConditionalLossFraction { get; init; } = 0.10m;
    public decimal AbsoluteLossFraction { get; init; } = 0.20m;
    public decimal TrailingLossFraction { get; init; } = 0.05m;
    public decimal EntryCostRate { get; init; } = 0.0025m;
    public decimal ExitCostRate { get; init; } = 0.0025m;
    public int OrdinaryMaximumSessions { get; init; } = 5;
    public int AbsoluteMaximumSessions { get; init; } = 10;
    public double StrongBreakoutProbability { get; init; } = 0.60;
    public double StrongDirectionEdge { get; init; } = 0.10;
    public double MaximumStrongDownProbability { get; init; } = 0.35;

    public static DelayedIntradaySwingPolicyConfig Version1 { get; } = new();
}

/// <summary>
/// A normalized completed intraday bar. Event time and receipt time are separate because
/// the selected source is delayed. Session ordinal counts the entry session as one.
/// </summary>
public sealed record DelayedIntradayBar(
    DateTime StartUtc,
    DateTime EndUtc,
    DateTime ReceivedUtc,
    int TradingSessionOrdinal,
    bool IsSessionClosingBar,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);

/// <summary>
/// Latest available post-entry Delphi evidence used only for the conditional 10% stop
/// exception. The original entry recommendation is deliberately insufficient.
/// </summary>
public sealed record DelayedIntradayBreakoutEvidence(
    DateTime RunStartedUtc,
    bool IsLatestAvailableOfficialRun,
    bool IsValid,
    bool IsBreakoutPublished,
    double BreakoutProbability,
    double DirectionEdge,
    double DownProbability);

public sealed record IntradaySwingPositionState(
    decimal EntryPrice,
    DateTime EntryUtc,
    decimal HighestCompletedClose,
    decimal? TrailingStopPrice,
    bool ProfitProtectionArmed,
    DateTime? LastProcessedBarEndUtc,
    int LastTradingSessionOrdinal)
{
    public static IntradaySwingPositionState Open(decimal entryPrice, DateTime entryUtc)
    {
        if (entryPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(entryPrice), "Entry price must be positive.");
        RequireUtc(entryUtc, nameof(entryUtc));

        return new IntradaySwingPositionState(
            entryPrice,
            entryUtc,
            entryPrice,
            null,
            false,
            null,
            0);
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", parameterName);
    }
}

public sealed record IntradaySwingDecision(
    IntradaySwingDirective Directive,
    IntradaySwingReason Reason,
    IntradaySwingPositionState State,
    DateTime DetectedUtc,
    TimeSpan DataAge,
    bool IsLate,
    decimal? TriggerPrice,
    decimal ObservedClose,
    bool StrongBreakoutQualified);

/// <summary>
/// Pure, deterministic evaluator for ADR-0028. It emits an advisory decision only;
/// it neither assigns a fill nor changes a position or brokerage account.
/// </summary>
public static class DelayedIntradaySwingExitPolicy
{
    public static IntradaySwingDecision Evaluate(
        IntradaySwingPositionState state,
        DelayedIntradayBar bar,
        DelayedIntradayBreakoutEvidence? breakoutEvidence = null,
        DelayedIntradaySwingPolicyConfig? config = null)
    {
        config ??= DelayedIntradaySwingPolicyConfig.Version1;
        Validate(config, state, bar);

        TimeSpan dataAge = bar.ReceivedUtc - bar.EndUtc;
        bool isLate = dataAge > TimeSpan.FromMinutes(config.LateDataAgeMinutes);
        decimal absoluteStop = state.EntryPrice * (1 - config.AbsoluteLossFraction);
        decimal conditionalStop = state.EntryPrice * (1 - config.ConditionalLossFraction);
        bool strongBreakoutQualified = QualifiesForStrongBreakoutException(
            state,
            bar,
            breakoutEvidence,
            config);

        var processedState = state with
        {
            LastProcessedBarEndUtc = bar.EndUtc,
            LastTradingSessionOrdinal = bar.TradingSessionOrdinal
        };

        if (bar.Low <= absoluteStop)
        {
            return Decision(
                IntradaySwingDirective.ExitAlert,
                IntradaySwingReason.AbsoluteLossLimit,
                processedState,
                bar,
                dataAge,
                isLate,
                absoluteStop,
                strongBreakoutQualified);
        }

        bool conditionalStopCrossed = bar.Low <= conditionalStop;
        if (conditionalStopCrossed && !strongBreakoutQualified)
        {
            return Decision(
                IntradaySwingDirective.ExitAlert,
                IntradaySwingReason.ConditionalLossLimit,
                processedState,
                bar,
                dataAge,
                isLate,
                conditionalStop,
                false);
        }

        // Test only a trail established by earlier completed bars. The current bar's high
        // and low have no known order, so its close may ratchet the trail only afterward.
        if (state.ProfitProtectionArmed &&
            state.TrailingStopPrice is decimal existingTrail &&
            bar.Low <= existingTrail)
        {
            return Decision(
                IntradaySwingDirective.ExitAlert,
                IntradaySwingReason.TrailingProfit,
                processedState,
                bar,
                dataAge,
                isLate,
                existingTrail,
                strongBreakoutQualified);
        }

        decimal breakEvenExitPrice = BreakEvenExitPrice(state.EntryPrice, config);
        decimal highestClose = System.Math.Max(state.HighestCompletedClose, bar.Close);
        bool profitProtectionArmed = state.ProfitProtectionArmed || bar.Close >= breakEvenExitPrice;
        decimal? trailingStop = state.TrailingStopPrice;

        if (profitProtectionArmed)
        {
            decimal candidateTrail = System.Math.Max(
                breakEvenExitPrice,
                highestClose * (1 - config.TrailingLossFraction));
            trailingStop = trailingStop.HasValue
                ? System.Math.Max(trailingStop.Value, candidateTrail)
                : candidateTrail;
        }

        processedState = processedState with
        {
            HighestCompletedClose = highestClose,
            ProfitProtectionArmed = profitProtectionArmed,
            TrailingStopPrice = trailingStop
        };

        if (bar.IsSessionClosingBar &&
            bar.TradingSessionOrdinal >= config.AbsoluteMaximumSessions)
        {
            return Decision(
                IntradaySwingDirective.ExitAlert,
                IntradaySwingReason.TenSessionMaximum,
                processedState,
                bar,
                dataAge,
                isLate,
                null,
                strongBreakoutQualified);
        }

        bool profitableAfterCosts = bar.Close >= breakEvenExitPrice;
        if (bar.IsSessionClosingBar &&
            bar.TradingSessionOrdinal >= config.OrdinaryMaximumSessions &&
            !profitableAfterCosts)
        {
            return Decision(
                IntradaySwingDirective.ExitAlert,
                IntradaySwingReason.FiveSessionUnprofitable,
                processedState,
                bar,
                dataAge,
                isLate,
                breakEvenExitPrice,
                strongBreakoutQualified);
        }

        return Decision(
            conditionalStopCrossed
                ? IntradaySwingDirective.HoldUnderStrongBreakoutException
                : IntradaySwingDirective.Hold,
            conditionalStopCrossed
                ? IntradaySwingReason.StrongBreakoutException
                : IntradaySwingReason.None,
            processedState,
            bar,
            dataAge,
            isLate,
            conditionalStopCrossed ? conditionalStop : null,
            strongBreakoutQualified);
    }

    public static decimal BreakEvenExitPrice(
        decimal entryPrice,
        DelayedIntradaySwingPolicyConfig? config = null)
    {
        config ??= DelayedIntradaySwingPolicyConfig.Version1;
        if (entryPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(entryPrice), "Entry price must be positive.");
        ValidateConfig(config);

        return entryPrice * (1 + config.EntryCostRate) / (1 - config.ExitCostRate);
    }

    public static bool QualifiesForStrongBreakoutException(
        IntradaySwingPositionState state,
        DelayedIntradayBar bar,
        DelayedIntradayBreakoutEvidence? evidence,
        DelayedIntradaySwingPolicyConfig? config = null)
    {
        config ??= DelayedIntradaySwingPolicyConfig.Version1;
        ValidateConfig(config);
        if (evidence is null)
            return false;

        return evidence.RunStartedUtc.Kind == DateTimeKind.Utc &&
               evidence.RunStartedUtc > state.EntryUtc &&
               evidence.RunStartedUtc <= bar.StartUtc &&
               evidence.IsLatestAvailableOfficialRun &&
               evidence.IsValid &&
               evidence.IsBreakoutPublished &&
               double.IsFinite(evidence.BreakoutProbability) &&
               double.IsFinite(evidence.DirectionEdge) &&
               double.IsFinite(evidence.DownProbability) &&
               evidence.BreakoutProbability >= config.StrongBreakoutProbability &&
               evidence.DirectionEdge >= config.StrongDirectionEdge &&
               evidence.DownProbability < config.MaximumStrongDownProbability;
    }

    private static IntradaySwingDecision Decision(
        IntradaySwingDirective directive,
        IntradaySwingReason reason,
        IntradaySwingPositionState state,
        DelayedIntradayBar bar,
        TimeSpan dataAge,
        bool isLate,
        decimal? triggerPrice,
        bool strongBreakoutQualified) =>
        new(
            directive,
            reason,
            state,
            bar.ReceivedUtc,
            dataAge,
            isLate,
            triggerPrice,
            bar.Close,
            strongBreakoutQualified);

    private static void Validate(
        DelayedIntradaySwingPolicyConfig config,
        IntradaySwingPositionState state,
        DelayedIntradayBar bar)
    {
        ValidateConfig(config);
        if (state.EntryPrice <= 0 || state.HighestCompletedClose <= 0)
            throw new ArgumentException("Position prices must be positive.", nameof(state));
        if (state.HighestCompletedClose < state.EntryPrice)
            throw new ArgumentException("The completed-close high-water mark cannot be below the entry price.", nameof(state));
        if (state.ProfitProtectionArmed != state.TrailingStopPrice.HasValue ||
            state.TrailingStopPrice is <= 0)
            throw new ArgumentException("Trailing state is inconsistent.", nameof(state));
        RequireUtc(state.EntryUtc, nameof(state.EntryUtc));
        if (state.LastProcessedBarEndUtc.HasValue)
            RequireUtc(state.LastProcessedBarEndUtc.Value, nameof(state.LastProcessedBarEndUtc));
        if (state.LastProcessedBarEndUtc.HasValue && bar.EndUtc <= state.LastProcessedBarEndUtc.Value)
            throw new ArgumentException("Bars must be processed once in increasing event-time order.", nameof(bar));
        if (bar.TradingSessionOrdinal < 1 ||
            bar.TradingSessionOrdinal < state.LastTradingSessionOrdinal)
            throw new ArgumentOutOfRangeException(nameof(bar), "Trading-session ordinal cannot decrease and must start at one.");
        RequireUtc(bar.StartUtc, nameof(bar.StartUtc));
        RequireUtc(bar.EndUtc, nameof(bar.EndUtc));
        RequireUtc(bar.ReceivedUtc, nameof(bar.ReceivedUtc));
        if (bar.StartUtc >= bar.EndUtc)
            throw new ArgumentException("Bar start must be earlier than bar end.", nameof(bar));
        if (bar.StartUtc < state.EntryUtc)
            throw new ArgumentException("A position cannot consume intraday evidence from before its entry.", nameof(bar));
        if (bar.EndUtc - bar.StartUtc != TimeSpan.FromMinutes(config.PollIntervalMinutes))
            throw new ArgumentException("Bar duration must match the configured polling interval.", nameof(bar));
        if (bar.ReceivedUtc < bar.EndUtc)
            throw new ArgumentException("A bar cannot be received before its market-event end time.", nameof(bar));
        if (bar.Open <= 0 || bar.High <= 0 || bar.Low <= 0 || bar.Close <= 0)
            throw new ArgumentException("OHLC prices must be positive.", nameof(bar));
        if (bar.Low > System.Math.Min(bar.Open, bar.Close) ||
            bar.High < System.Math.Max(bar.Open, bar.Close) ||
            bar.Low > bar.High)
            throw new ArgumentException("OHLC prices are inconsistent.", nameof(bar));
        if (bar.Volume < 0)
            throw new ArgumentOutOfRangeException(nameof(bar), "Volume cannot be negative.");
    }

    private static void ValidateConfig(DelayedIntradaySwingPolicyConfig config)
    {
        if (config.PollIntervalMinutes <= 0 ||
            config.ExpectedSourceDelayMinutes < 0 ||
            config.LateDataAgeMinutes < config.ExpectedSourceDelayMinutes)
            throw new ArgumentOutOfRangeException(nameof(config), "Polling and delay values are inconsistent.");
        if (config.ConditionalLossFraction <= 0 ||
            config.AbsoluteLossFraction <= config.ConditionalLossFraction ||
            config.AbsoluteLossFraction >= 1 ||
            config.TrailingLossFraction <= 0 ||
            config.TrailingLossFraction >= 1)
            throw new ArgumentOutOfRangeException(nameof(config), "Loss and trailing fractions are inconsistent.");
        if (config.EntryCostRate < 0 || config.EntryCostRate >= 1 ||
            config.ExitCostRate < 0 || config.ExitCostRate >= 1)
            throw new ArgumentOutOfRangeException(nameof(config), "Cost rates must be within [0, 1).");
        if (config.OrdinaryMaximumSessions < 1 ||
            config.AbsoluteMaximumSessions <= config.OrdinaryMaximumSessions)
            throw new ArgumentOutOfRangeException(nameof(config), "The absolute session limit must exceed the ordinary limit.");
        if (!double.IsFinite(config.StrongBreakoutProbability) ||
            !double.IsFinite(config.StrongDirectionEdge) ||
            !double.IsFinite(config.MaximumStrongDownProbability))
            throw new ArgumentOutOfRangeException(nameof(config), "Signal thresholds must be finite.");
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", parameterName);
    }
}
