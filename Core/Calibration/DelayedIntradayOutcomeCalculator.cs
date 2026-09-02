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
    Invalid,
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

public sealed record InvalidDelayedIntradayOutcomeV1(
    int SchemaVersion,
    string PolicyVersion,
    DateTime EntryUtc,
    DateTime FirstInvalidEventUtc,
    string ReasonCode);

public sealed record DelayedIntradayOutcomeAssessment(
    DelayedIntradayOutcomeState State,
    string? ReasonCode,
    DateTime? FirstInvalidEventUtc,
    DelayedIntradayOutcomeV1? Outcome);

/// <summary>
/// Replays ADR-0028 from immutable completed bars. An alert receives the open
/// of the exact first five-minute boundary at or after detection. The raw return
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
        var validatedPolicyBars = new List<DelayedIntradayBar>();

        foreach (DelayedIntradayBar bar in orderedPolicyBars)
        {
            validatedPolicyBars.Add(bar);
            DelayedIntradayOutcomeAssessment? continuityFailure =
                ValidatePolicyContinuity(entryUtc, validatedPolicyBars, config);
            if (continuityFailure is not null)
                return continuityFailure;

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

        DateTime? expectedFill = DetermineExpectedFillUtc(
            exit.DetectedUtc,
            symbolFiveMinuteBars,
            xiuFiveMinuteBars);
        if (expectedFill is null)
            return Pending("AwaitingPostDetectionSymbolFillBar");
        DateTime expectedFillUtc = expectedFill.Value;
        List<OhlcvBar> fillMatches = symbolFiveMinuteBars
            .Where(bar => bar.TimestampUtc == expectedFillUtc)
            .ToList();
        if (fillMatches.Count > 1)
            return Invalid("DuplicateExpectedSymbolFillBar", expectedFillUtc);
        if (fillMatches.Count == 0)
        {
            return symbolFiveMinuteBars.Any(bar =>
                       bar.TimestampUtc.Kind == DateTimeKind.Utc &&
                       bar.TimestampUtc > expectedFillUtc)
                ? Invalid("MissingExpectedSymbolFillBar", expectedFillUtc)
                : Pending("AwaitingPostDetectionSymbolFillBar");
        }

        List<OhlcvBar> xiuFillMatches = xiuFiveMinuteBars
            .Where(bar => bar.TimestampUtc == expectedFillUtc)
            .ToList();
        if (xiuFillMatches.Count > 1)
            return Invalid("DuplicateAlignedXiuFillBar", expectedFillUtc);
        if (xiuFillMatches.Count == 0)
        {
            return xiuFiveMinuteBars.Any(bar =>
                       bar.TimestampUtc.Kind == DateTimeKind.Utc &&
                       bar.TimestampUtc > expectedFillUtc)
                ? Invalid("MissingAlignedXiuFillBar", expectedFillUtc)
                : Pending("AwaitingAlignedPostDetectionXiuBar");
        }

        OhlcvBar fillBar = fillMatches[0];
        OhlcvBar xiuFillBar = xiuFillMatches[0];

        try
        {
            ValidateFillBar(fillBar, nameof(symbolFiveMinuteBars));
        }
        catch (ArgumentException)
        {
            return Invalid("InvalidExpectedSymbolFillBar", expectedFillUtc);
        }
        try
        {
            ValidateFillBar(xiuFillBar, nameof(xiuFiveMinuteBars));
        }
        catch (ArgumentException)
        {
            return Invalid("InvalidAlignedXiuFillBar", expectedFillUtc);
        }

        decimal adjustedEntry = rawEntryPrice * (1m + ExecutionFrictionRatePerSide);
        decimal adjustedExit = fillBar.Open * (1m - ExecutionFrictionRatePerSide);
        double grossReturn = (double)(fillBar.Open / rawEntryPrice - 1m);
        double conservativeNetReturn = (double)(adjustedExit / adjustedEntry - 1m);
        double xiuReturn = (double)(xiuFillBar.Open / xiuRawEntryPrice - 1m);

        return new DelayedIntradayOutcomeAssessment(
            DelayedIntradayOutcomeState.Matured,
            null,
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
        new(DelayedIntradayOutcomeState.Pending, reason, null, null);

    private static DelayedIntradayOutcomeAssessment Invalid(string reason, DateTime firstInvalidEventUtc) =>
        new(DelayedIntradayOutcomeState.Invalid, reason, firstInvalidEventUtc, null);

    private static DelayedIntradayOutcomeAssessment? ValidatePolicyContinuity(
        DateTime entryUtc,
        IReadOnlyList<DelayedIntradayBar> bars,
        DelayedIntradaySwingPolicyConfig config)
    {
        TimeZoneInfo toronto = ResolveTorontoTimeZone();
        TimeSpan interval = TimeSpan.FromMinutes(config.PollIntervalMinutes);
        DelayedIntradayBar first = bars[0];
        if (first.StartUtc.Kind != DateTimeKind.Utc)
            return Invalid("InvalidPolicyBarTimestamp", entryUtc);
        if (first.TradingSessionOrdinal != 1)
            return Invalid("FirstPolicySessionOrdinalNotOne", first.StartUtc);
        if (first.StartUtc != entryUtc)
            return Invalid("FirstPolicyBarDoesNotMatchEntry", entryUtc);

        DelayedIntradayBar? previous = null;
        DateTime previousLocalDate = default;
        foreach (DelayedIntradayBar bar in bars)
        {
            if (bar.StartUtc.Kind != DateTimeKind.Utc ||
                bar.EndUtc.Kind != DateTimeKind.Utc ||
                bar.ReceivedUtc.Kind != DateTimeKind.Utc)
                return Invalid("InvalidPolicyBarTimestamp", entryUtc);

            DateTime localStart = TimeZoneInfo.ConvertTimeFromUtc(bar.StartUtc, toronto);
            TimeSpan localTime = localStart.TimeOfDay;
            bool onRegularGrid = localTime >= new TimeSpan(9, 30, 0) &&
                                 localTime <= new TimeSpan(15, 45, 0) &&
                                 (localTime - new TimeSpan(9, 30, 0)).Ticks % interval.Ticks == 0;
            if (!onRegularGrid)
                return Invalid("PolicyBarOutsideRegularSessionGrid", bar.StartUtc);

            bool isClosingBar = localTime == new TimeSpan(15, 45, 0);
            if (bar.IsSessionClosingBar != isClosingBar)
                return Invalid("IncorrectPolicySessionClosingFlag", bar.StartUtc);

            if (previous is not null)
            {
                if (bar.ReceivedUtc < previous.ReceivedUtc)
                    return Invalid("PolicyReceiptOrderConflict", bar.StartUtc);

                if (localStart.Date == previousLocalDate)
                {
                    if (bar.TradingSessionOrdinal != previous.TradingSessionOrdinal)
                        return Invalid("PolicySessionOrdinalChangedWithinSession", bar.StartUtc);

                    DateTime expectedStart = previous.StartUtc + interval;
                    if (bar.StartUtc != expectedStart)
                        return Invalid("MissingExpectedPolicyBar", expectedStart);
                }
                else
                {
                    if (!previous.IsSessionClosingBar)
                        return Invalid("MissingPriorPolicySessionClose", previous.StartUtc + interval);
                    if (bar.TradingSessionOrdinal != previous.TradingSessionOrdinal + 1)
                        return Invalid("NonConsecutivePolicySessionOrdinal", bar.StartUtc);
                    if (localTime != new TimeSpan(9, 30, 0))
                        return Invalid("MissingPolicySessionOpen", bar.StartUtc);
                }
            }

            try
            {
                // The policy validator supplies the remaining timestamp, duration,
                // receipt, OHLC, volume, and state-order checks.
                var validationState = previous is null
                    ? IntradaySwingPositionState.Open(1m, entryUtc)
                    : IntradaySwingPositionState.Open(1m, entryUtc) with
                    {
                        LastProcessedBarEndUtc = previous.EndUtc,
                        LastTradingSessionOrdinal = previous.TradingSessionOrdinal
                    };
                DelayedIntradaySwingExitPolicy.Evaluate(validationState, bar, config: config);
            }
            catch (ArgumentException)
            {
                return Invalid("InvalidPolicyBar", bar.StartUtc);
            }

            previous = bar;
            previousLocalDate = localStart.Date;
        }

        return null;
    }

    private static DateTime CeilingToFiveMinutes(DateTime timestampUtc)
    {
        RequireUtc(timestampUtc, nameof(timestampUtc));
        long intervalTicks = TimeSpan.FromMinutes(5).Ticks;
        long remainder = timestampUtc.Ticks % intervalTicks;
        return remainder == 0
            ? timestampUtc
            : new DateTime(timestampUtc.Ticks + intervalTicks - remainder, DateTimeKind.Utc);
    }

    private static DateTime? DetermineExpectedFillUtc(
        DateTime detectedUtc,
        IReadOnlyCollection<OhlcvBar> symbolBars,
        IReadOnlyCollection<OhlcvBar> xiuBars)
    {
        TimeZoneInfo toronto = ResolveTorontoTimeZone();
        DateTime localDetected = TimeZoneInfo.ConvertTimeFromUtc(detectedUtc, toronto);
        TimeSpan localTime = localDetected.TimeOfDay;
        TimeSpan open = new(9, 30, 0);
        TimeSpan lastFiveMinuteStart = new(15, 55, 0);

        if (localTime < open)
            return ToUtc(localDetected.Date + open, toronto);
        if (localTime <= lastFiveMinuteStart)
            return CeilingToFiveMinutes(detectedUtc);

        DateTime? firstLaterEvidence = symbolBars
            .Concat(xiuBars)
            .Where(bar => bar.TimestampUtc.Kind == DateTimeKind.Utc &&
                          bar.TimestampUtc > detectedUtc &&
                          IsRegularFiveMinuteStart(bar.TimestampUtc, toronto))
            .Select(bar => (DateTime?)bar.TimestampUtc)
            .OrderBy(timestamp => timestamp)
            .FirstOrDefault();
        if (firstLaterEvidence is null)
            return null;

        DateTime firstLaterLocal = TimeZoneInfo.ConvertTimeFromUtc(firstLaterEvidence.Value, toronto);
        return ToUtc(firstLaterLocal.Date + open, toronto);
    }

    private static bool IsRegularFiveMinuteStart(DateTime timestampUtc, TimeZoneInfo toronto)
    {
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(timestampUtc, toronto);
        TimeSpan time = local.TimeOfDay;
        TimeSpan open = new(9, 30, 0);
        return time >= open &&
               time <= new TimeSpan(15, 55, 0) &&
               (time - open).Ticks % TimeSpan.FromMinutes(5).Ticks == 0;
    }

    private static DateTime ToUtc(DateTime localTimestamp, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(localTimestamp, DateTimeKind.Unspecified),
            timeZone);

    private static TimeZoneInfo ResolveTorontoTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }

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
