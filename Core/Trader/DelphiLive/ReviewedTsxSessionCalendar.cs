#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Core.Trader.DelphiLive;

public sealed record ReviewedTsxCalendarDocument(
    [property: System.Text.Json.Serialization.JsonRequired] string Version,
    [property: System.Text.Json.Serialization.JsonRequired] string SourceReference,
    [property: System.Text.Json.Serialization.JsonRequired] DateOnly FirstCoveredDate,
    [property: System.Text.Json.Serialization.JsonRequired] DateOnly LastCoveredDate,
    [property: System.Text.Json.Serialization.JsonRequired] IReadOnlyList<DateOnly> RegularSessionDates);

/// <summary>
/// An explicit reviewed official-calendar snapshot. Missing coverage is an error,
/// never a weekday guess or a provider call from the trading workflow.
/// </summary>
public sealed class ReviewedTsxSessionCalendar : ITsxSessionCalendar
{
    public static TimeZoneInfo Toronto { get; } = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
    private readonly ReviewedTsxCalendarDocument document;
    private readonly ImmutableSortedSet<DateOnly> dates;
    public string Version => document.Version;

    public ReviewedTsxSessionCalendar(ReviewedTsxCalendarDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(document.Version) || string.IsNullOrWhiteSpace(document.SourceReference) ||
            document.LastCoveredDate < document.FirstCoveredDate || document.RegularSessionDates is null || document.RegularSessionDates.Count == 0 ||
            document.RegularSessionDates.Distinct().Count() != document.RegularSessionDates.Count ||
            document.RegularSessionDates.Any(d => d < document.FirstCoveredDate || d > document.LastCoveredDate))
            throw new ArgumentException("A versioned official-calendar source and complete declared coverage are required.");
        this.document = document;
        dates = document.RegularSessionDates.ToImmutableSortedSet();
    }

    public static ReviewedTsxSessionCalendar Load(string path) => new(
        JsonSerializer.Deserialize<ReviewedTsxCalendarDocument>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
        ?? throw new InvalidDataException("The reviewed TSX calendar is empty."));

    public bool IsRegularSession(DateOnly tradingDate)
    {
        RequireCoverage(tradingDate);
        return dates.Contains(tradingDate);
    }

    public DateOnly GetImmediatelyPrecedingSession(DateOnly tradingDate)
    {
        RequireCoverage(tradingDate);
        DateOnly[] prior = dates.Where(d => d < tradingDate).TakeLast(1).ToArray();
        return prior.Length == 1 ? prior[0] : throw new InvalidOperationException("Prior-session calendar coverage is unavailable.");
    }

    public DateOnly GetNextSession(DateOnly afterDate)
    {
        RequireCoverage(afterDate);
        foreach (DateOnly date in dates)
            if (date > afterDate) return date;
        throw new InvalidOperationException("Next-session calendar coverage is unavailable.");
    }

    public int GetSessionOrdinal(DateOnly date)
    {
        if (!IsRegularSession(date)) throw new InvalidOperationException("A cohort ordinal requires a reviewed regular session.");
        return dates.TakeWhile(d => d < date).Count();
    }

    public DelphiLiveSessionBounds GetSessionBounds(DateOnly tradingDate)
    {
        if (!IsRegularSession(tradingDate)) throw new InvalidOperationException("This date is not a regular TSX session.");
        return new(tradingDate, At(tradingDate, new(9, 30)), At(tradingDate, new(16, 0)));
    }

    public static DateTime At(DateOnly date, TimeOnly time) => TimeZoneInfo.ConvertTimeToUtc(
        DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified), Toronto);

    private void RequireCoverage(DateOnly date)
    {
        if (date < document.FirstCoveredDate || date > document.LastCoveredDate)
            throw new InvalidOperationException("The reviewed official TSX calendar does not cover this date.");
    }
}
