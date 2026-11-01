#nullable enable
using Core.Trader.DelphiLive;
using System;
using System.Globalization;
using System.IO;

namespace Core.Runtime;

/// <summary>ADR-0114: a preparation date is an output key, never an observation timestamp.</summary>
public static class NightlyPreparationDate
{
    public const string EnvironmentVariable = "TRADERVI_PREPARATION_DATE";

    public static DateTime FromEnvironment() => Resolve(
        Environment.GetEnvironmentVariable(EnvironmentVariable), DateTimeOffset.UtcNow,
        DailyBenchmarkPolicy.LoadCalendar());

    public static DateTime Resolve(string? requested, DateTimeOffset now, ReviewedTsxSessionCalendar calendar)
    {
        var local = TimeZoneInfo.ConvertTime(now, ReviewedTsxSessionCalendar.Toronto);
        var today = DateOnly.FromDateTime(local.DateTime);
        DateOnly date = today;
        if (requested is not null && !DateOnly.TryParseExact(requested, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            throw new InvalidDataException("Preparation date must be yyyy-MM-dd.");
        if (date < today || date > today.AddDays(1))
            throw new InvalidDataException("Preparation date must be today or tomorrow in Toronto.");

        var preceding = calendar.GetImmediatelyPrecedingSession(date);
        if (now.UtcDateTime < calendar.GetSessionBounds(preceding).CloseUtc)
            throw new InvalidDataException("The preparation date's preceding TSX session has not closed.");
        // US index collection also includes today's bars on an early run. Even on a TSX
        // holiday, wait for the regular North American close; no intraday preview is eligible.
        if (date > today && local.TimeOfDay < new TimeSpan(16, 0, 0))
            throw new InvalidDataException("Early preparation must wait until 16:00 Toronto.");
        return date.ToDateTime(TimeOnly.MinValue);
    }
}
