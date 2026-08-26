using Core.TMX;
using Core.TMX.Models.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Probes;

/// <summary>
/// Read-only market-hours freshness probe for the ADR-0028 XIU source.
///
/// Thesis: a 15-minute polling cadence can observe newly completed XIU bars
/// with stable event timestamps and an operationally acceptable delay.
///
/// Assumptions: TMX timestamps label the start of each 15-minute bar; the
/// corrected no-freq request remains valid; repeated event timestamps are
/// expected and must not be treated as new evidence.
///
/// Window: five polls spaced fifteen minutes apart (one hour total), each using
/// a rolling two-calendar-day request. The probe refuses to call TMX outside a
/// regular Monday-Friday 9:30 a.m.-4:00 p.m. Toronto window.
///
/// Side effects: bounded external TMX GraphQL reads and console output only.
/// No SQL connection, file write, position mutation, alert, or order occurs.
///
/// Exit signal: record whether each poll exposes a new completed timestamp,
/// repeats an unchanged bar, or revises the same timestamp, together with its
/// completion-to-receipt age and transport attempts. Use the result to settle
/// scheduling/freshness defaults before persistence is designed.
/// </summary>
public sealed class TmxXiuMarketHoursPollingProbe : IProbe
{
    private const string Symbol = "XIU";
    private const int IntervalMinutes = 15;
    private const int PollCount = 5;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeZoneInfo TorontoTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    public string Slug => "tmx-xiu-market-hours";
    public string Description =>
        "Read-only XIU market-hours probe — five 15-minute polls with event/receipt timing.";

    public async Task RunAsync()
    {
        DateTime startedUtc = DateTime.UtcNow;
        DateTime startedLocal = ToToronto(startedUtc);

        Console.WriteLine("=== TMX XIU market-hours freshness probe ===");
        Console.WriteLine($"Started: {startedUtc:O} UTC / {startedLocal:yyyy-MM-dd HH:mm:ss} Toronto");
        Console.WriteLine($"Plan: {PollCount} polls, {PollInterval.TotalMinutes:N0} minutes apart, interval={IntervalMinutes}");
        Console.WriteLine("Side effects: external reads and console output only; no SQL, files, positions, alerts, or orders.");
        Console.WriteLine();

        if (!IsRegularMarketWindow(startedLocal))
        {
            Console.WriteLine("Result: not run because Toronto is outside the regular Monday-Friday 9:30-16:00 market window.");
            return;
        }

        using var tmx = new TmxClient();
        OhlcvBar previousLatestCompleted = null;
        IReadOnlyDictionary<DateTime, OhlcvBar> previousBars =
            new Dictionary<DateTime, OhlcvBar>();

        Console.WriteLine(
            $"{"Poll",6} {"Receipt",12} {"Latest returned",18} {"Latest complete",18} " +
            $"{"Age",12} {"Complete state",18} {"Prior snapshot",18} {"Forming",9} " +
            $"{"Attempts",9} {"Bars",7}");
        Console.WriteLine(new string('─', 144));

        for (int poll = 1; poll <= PollCount; poll++)
        {
            DateTime requestedEndUtc = DateTime.UtcNow;
            TmxIntradayBatch batch = await tmx.GetIntradayTimeSeriesBatchAsync(
                Symbol,
                IntervalMinutes,
                requestedEndUtc.AddDays(-2),
                requestedEndUtc);
            OhlcvBar latestReturned = batch.Bars.LastOrDefault();
            OhlcvBar latestCompleted = batch.LatestCompletedBarAtReceipt;

            string state = Classify(previousLatestCompleted, latestCompleted);
            string priorSnapshot = ClassifyPriorSnapshot(previousBars, latestCompleted);
            string latestReturnedLocal = FormatLocal(latestReturned?.TimestampUtc);
            string latestCompletedLocal = FormatLocal(latestCompleted?.TimestampUtc);
            string age = FormatAge(batch.LatestCompletedEvidenceAgeAtReceipt);
            string forming = batch.HasFormingBarAtReceipt ? "yes" : "no";

            Console.WriteLine(
                $"{poll,6} {ToToronto(batch.ReceivedUtc),12:HH:mm:ss} " +
                $"{latestReturnedLocal,18} {latestCompletedLocal,18} {age,12} " +
                $"{state,18} {priorSnapshot,18} {forming,9} " +
                $"{batch.AttemptCount,9} {batch.Bars.Count,7:N0}");

            previousLatestCompleted = latestCompleted;
            previousBars = batch.Bars.ToDictionary(bar => bar.TimestampUtc);
            if (poll < PollCount)
                await Task.Delay(PollInterval);
        }

        Console.WriteLine();
        Console.WriteLine("Result: polling sequence complete; review completed-bar timing separately from the still-forming bar.");
    }

    private static bool IsRegularMarketWindow(DateTime local) =>
        local.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday &&
        local.TimeOfDay >= new TimeSpan(9, 30, 0) &&
        local.TimeOfDay < new TimeSpan(16, 0, 0);

    private static string Classify(OhlcvBar previous, OhlcvBar current)
    {
        if (current is null)
            return "No bars";
        if (previous is null)
            return "Initial";
        if (current.TimestampUtc > previous.TimestampUtc)
            return "New timestamp";
        if (current.TimestampUtc == previous.TimestampUtc && current == previous)
            return "Repeated unchanged";
        if (current.TimestampUtc == previous.TimestampUtc)
            return "Repeated revised";
        return "Timestamp regressed";
    }

    private static string ClassifyPriorSnapshot(
        IReadOnlyDictionary<DateTime, OhlcvBar> previousBars,
        OhlcvBar current)
    {
        if (current is null)
            return "No completed bar";
        if (!previousBars.TryGetValue(current.TimestampUtc, out OhlcvBar previous))
            return "Not seen before";
        return current == previous
            ? "Unchanged"
            : "Revised";
    }

    private static string FormatLocal(DateTime? utc) =>
        utc.HasValue ? ToToronto(utc.Value).ToString("HH:mm:ss") : "—";

    private static DateTime ToToronto(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            TorontoTimeZone);

    private static string FormatAge(TimeSpan? age)
    {
        if (!age.HasValue)
            return "—";
        return age.Value.ToString("g");
    }
}
