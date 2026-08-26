#nullable enable

using Core.TMX.Models.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Trader;

/// <summary>
/// Deterministically converts completed five-minute TMX evidence into the
/// fifteen-minute bars consumed by ADR-0028's version-1 policy.
/// </summary>
public static class CompletedIntradayBarAggregator
{
    private const int SourceIntervalMinutes = 5;
    private const int PolicyIntervalMinutes = 15;
    private static readonly TimeZoneInfo TorontoTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public static IReadOnlyList<OhlcvBar> AggregateFiveMinuteBars(
        IReadOnlyCollection<OhlcvBar> sourceBars,
        DateTime receivedUtc)
    {
        ArgumentNullException.ThrowIfNull(sourceBars);
        RequireUtc(receivedUtc, nameof(receivedUtc));
        if (sourceBars.Any(bar => bar.TimestampUtc.Kind != DateTimeKind.Utc))
        {
            throw new ArgumentException(
                "Every source-bar timestamp must have DateTimeKind.Utc.",
                nameof(sourceBars));
        }

        IReadOnlyDictionary<DateTime, OhlcvBar> completed = sourceBars
            .Where(bar =>
                bar.TimestampUtc.AddMinutes(SourceIntervalMinutes) <= receivedUtc)
            .ToDictionary(bar => bar.TimestampUtc);
        var aggregated = new List<OhlcvBar>();

        foreach (OhlcvBar first in completed.Values.OrderBy(bar => bar.TimestampUtc))
        {
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(
                first.TimestampUtc,
                TorontoTimeZone);
            int minutesFromOpen =
                (int)local.TimeOfDay.TotalMinutes - (9 * 60 + 30);
            if (minutesFromOpen < 0 ||
                minutesFromOpen >= 390 ||
                minutesFromOpen % PolicyIntervalMinutes != 0)
            {
                continue;
            }

            if (!completed.TryGetValue(
                    first.TimestampUtc.AddMinutes(SourceIntervalMinutes),
                    out OhlcvBar? second) ||
                !completed.TryGetValue(
                    first.TimestampUtc.AddMinutes(SourceIntervalMinutes * 2),
                    out OhlcvBar? third))
            {
                continue;
            }

            aggregated.Add(new OhlcvBar(
                first.TimestampUtc,
                first.Open,
                System.Math.Max(first.High, System.Math.Max(second.High, third.High)),
                System.Math.Min(first.Low, System.Math.Min(second.Low, third.Low)),
                third.Close,
                checked(first.Volume + second.Volume + third.Volume)));
        }

        return aggregated.AsReadOnly();
    }

    public static IReadOnlyList<DelayedIntradayBar> BuildPolicyBars(
        IReadOnlyCollection<OhlcvBar> completedFifteenMinuteBars,
        DateTime receivedUtc,
        DateTime entryUtc)
    {
        ArgumentNullException.ThrowIfNull(completedFifteenMinuteBars);
        RequireUtc(receivedUtc, nameof(receivedUtc));
        RequireUtc(entryUtc, nameof(entryUtc));

        List<OhlcvBar> ordered = completedFifteenMinuteBars
            .OrderBy(bar => bar.TimestampUtc)
            .ToList();
        DateTime entryLocalDate = TimeZoneInfo.ConvertTimeFromUtc(
            entryUtc,
            TorontoTimeZone).Date;
        List<DateTime> sessionDates = ordered
            .Select(bar => TimeZoneInfo.ConvertTimeFromUtc(
                bar.TimestampUtc,
                TorontoTimeZone).Date)
            .Where(date => date >= entryLocalDate)
            .Append(entryLocalDate)
            .Distinct()
            .OrderBy(date => date)
            .ToList();
        var sessionOrdinals = sessionDates
            .Select((date, index) => (date, ordinal: index + 1))
            .ToDictionary(item => item.date, item => item.ordinal);

        var policyBars = new List<DelayedIntradayBar>();
        foreach (OhlcvBar bar in ordered)
        {
            if (bar.TimestampUtc < entryUtc)
                continue;

            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(
                bar.TimestampUtc,
                TorontoTimeZone);
            if (!sessionOrdinals.TryGetValue(local.Date, out int ordinal))
                continue;

            policyBars.Add(new DelayedIntradayBar(
                bar.TimestampUtc,
                bar.TimestampUtc.AddMinutes(PolicyIntervalMinutes),
                receivedUtc,
                ordinal,
                local.TimeOfDay == new TimeSpan(15, 45, 0),
                bar.Open,
                bar.High,
                bar.Low,
                bar.Close,
                bar.Volume));
        }

        return policyBars.AsReadOnly();
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must have DateTimeKind.Utc.", parameterName);
    }
}
