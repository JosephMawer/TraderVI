#nullable enable

using Core.TMX.Models.Domain;
using Core.Trader;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Calibration;

public enum DelayedIntradayOutcomeState
{
    Pending,
    Matured
}

public sealed record DelayedIntradayOutcomeV1(
    int SchemaVersion,
    string PolicyVersion,
    DateTime EntryUtc,
    decimal RawEntryPrice,
    decimal AdjustedEntryPrice,
    decimal XiuRawEntryPrice,
    IntradaySwingReason ExitReason,
    decimal? TriggerPrice,
    DateTime DecisionBarStartUtc,
    DateTime DecisionBarEndUtc,
    DateTime DetectedUtc,
    double DataAgeMinutes,
    bool IsLate,
    DateTime FillBarStartUtc,
    double FillLagMinutes,
    decimal RawExitPrice,
    decimal AdjustedExitPrice,
    decimal XiuRawExitPrice,
    double GrossReturn,
    double ConservativeNetReturn,
    double XiuReturn,
    double GrossExcessReturn,
    double ConservativeNetExcessReturn,
    decimal ExecutionFrictionRatePerSide,
    string FillConvention,
    DelayedIntradayBreakoutEvidence? FreshBreakoutEvidence);

public sealed record DelayedIntradayOutcomeAssessment(
    DelayedIntradayOutcomeState State,
    string? PendingReason,
    DelayedIntradayOutcomeV1? Outcome);

/// <summary>
/// Replays ADR-0028 from immutable completed bars. An alert receives the open
/// of the first five-minute bar beginning at or after detection. The raw return
/// represents a zero-commission fill; the conservative return separately
/// applies the accepted spread/slippage sensitivity.
/// </summary>
public static class DelayedIntradayOutcomeCalculator
{
    public const int SchemaVersion = 1;
    public const decimal ExecutionFrictionRatePerSide = 0.0025m;
    public const string FillConvention = "FirstFiveMinuteBarOpenAtOrAfterDetection";

    public static DelayedIntradayOutcomeAssessment Assess(
        decimal rawEntryPrice,
        decimal xiuRawEntryPrice,
        DateTime entryUtc,
        IReadOnlyCollection<DelayedIntradayBar> policyBars,
        IReadOnlyCollection<OhlcvBar> symbolFiveMinuteBars,
        IReadOnlyCollection<OhlcvBar> xiuFiveMinuteBars,
        IReadOnlyDictionary<DateTime, DelayedIntradayBreakoutEvidence?>? breakoutEvidenceByBar = null,
        DelayedIntradaySwingPolicyConfig? config = null)
    {
        if (rawEntryPrice <= 0m)
            throw new ArgumentOutOfRangeException(nameof(rawEntryPrice));
        if (xiuRawEntryPrice <= 0m)
            throw new ArgumentOutOfRangeException(nameof(xiuRawEntryPrice));
        RequireUtc(entryUtc, nameof(entryUtc));
        ArgumentNullException.ThrowIfNull(policyBars);
        ArgumentNullException.ThrowIfNull(symbolFiveMinuteBars);
        ArgumentNullException.ThrowIfNull(xiuFiveMinuteBars);
        config ??= DelayedIntradaySwingPolicyConfig.Version1;

        List<DelayedIntradayBar> orderedPolicyBars = policyBars
            .OrderBy(bar => bar.StartUtc)
            .ToList();
        if (orderedPolicyBars.Count == 0)
            return Pending("NoCompletedPolicyBars");

        IntradaySwingPositionState state =
            IntradaySwingPositionState.Open(rawEntryPrice, entryUtc);
        IntradaySwingDecision? exit = null;
        DelayedIntradayBar? decisionBar = null;
        DelayedIntradayBreakoutEvidence? decisionEvidence = null;

        foreach (DelayedIntradayBar bar in orderedPolicyBars)
        {
            breakoutEvidenceByBar?.TryGetValue(bar.StartUtc, out decisionEvidence);
            IntradaySwingDecision decision = DelayedIntradaySwingExitPolicy.Evaluate(
                state,
                bar,
                decisionEvidence,
                config);
            state = decision.State;
            if (decision.Directive != IntradaySwingDirective.ExitAlert)
                continue;

            exit = decision;
            decisionBar = bar;
            break;
        }

        if (exit is null || decisionBar is null)
            return Pending("NoExitAlertYet");

        OhlcvBar? fillBar = symbolFiveMinuteBars
            .Where(bar => bar.TimestampUtc.Kind == DateTimeKind.Utc &&
                          bar.TimestampUtc >= exit.DetectedUtc)
            .OrderBy(bar => bar.TimestampUtc)
            .FirstOrDefault();
        if (fillBar is null)
            return Pending("AwaitingPostDetectionSymbolFillBar");

        OhlcvBar? xiuFillBar = xiuFiveMinuteBars
            .Where(bar => bar.TimestampUtc == fillBar.TimestampUtc)
            .SingleOrDefault();
        if (xiuFillBar is null)
            return Pending("AwaitingAlignedPostDetectionXiuBar");

        ValidateFillBar(fillBar, nameof(symbolFiveMinuteBars));
        ValidateFillBar(xiuFillBar, nameof(xiuFiveMinuteBars));

        decimal adjustedEntry = rawEntryPrice * (1m + ExecutionFrictionRatePerSide);
        decimal adjustedExit = fillBar.Open * (1m - ExecutionFrictionRatePerSide);
        double grossReturn = (double)(fillBar.Open / rawEntryPrice - 1m);
        double conservativeNetReturn = (double)(adjustedExit / adjustedEntry - 1m);
        double xiuReturn = (double)(xiuFillBar.Open / xiuRawEntryPrice - 1m);

        return new DelayedIntradayOutcomeAssessment(
            DelayedIntradayOutcomeState.Matured,
            null,
            new DelayedIntradayOutcomeV1(
                SchemaVersion,
                IntradayEvidenceVersions.Policy,
                entryUtc,
                rawEntryPrice,
                adjustedEntry,
                xiuRawEntryPrice,
                exit.Reason,
                exit.TriggerPrice,
                decisionBar.StartUtc,
                decisionBar.EndUtc,
                exit.DetectedUtc,
                exit.DataAge.TotalMinutes,
                exit.IsLate,
                fillBar.TimestampUtc,
                (fillBar.TimestampUtc - exit.DetectedUtc).TotalMinutes,
                fillBar.Open,
                adjustedExit,
                xiuFillBar.Open,
                grossReturn,
                conservativeNetReturn,
                xiuReturn,
                grossReturn - xiuReturn,
                conservativeNetReturn - xiuReturn,
                ExecutionFrictionRatePerSide,
                FillConvention,
                decisionEvidence));
    }

    private static DelayedIntradayOutcomeAssessment Pending(string reason) =>
        new(DelayedIntradayOutcomeState.Pending, reason, null);

    private static void ValidateFillBar(OhlcvBar bar, string parameterName)
    {
        RequireUtc(bar.TimestampUtc, parameterName);
        if (bar.TimestampUtc.Minute % 5 != 0 || bar.TimestampUtc.Second != 0)
            throw new ArgumentException("Fill bars must be aligned completed five-minute bars.", parameterName);
        if (bar.Open <= 0m)
            throw new ArgumentException("Fill-bar open must be positive.", parameterName);
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", parameterName);
    }
}
