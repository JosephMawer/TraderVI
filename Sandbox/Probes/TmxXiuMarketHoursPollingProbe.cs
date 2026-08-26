using Core.TMX;
using Core.TMX.Models.Domain;
using System;
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
        OhlcvBar previousLatest = null;

        Console.WriteLine(
            $"{"Poll",6} {"Receipt local",20} {"Latest event",20} {"Completed",20} " +
            $"{"Age",12} {"State",20} {"Attempts",9} {"Bars",7}");
        Console.WriteLine(new string('─', 124));

        for (int poll = 1; poll <= PollCount; poll++)
        {
            DateTime requestedEndUtc = DateTime.UtcNow;
            TmxIntradayBatch batch = await tmx.GetIntradayTimeSeriesBatchAsync(
                Symbol,
                IntervalMinutes,
                requestedEndUtc.AddDays(-2),
                requestedEndUtc);
            OhlcvBar latest = batch.Bars.LastOrDefault();

            string state = Classify(previousLatest, latest);
            string eventLocal = latest is null
                ? "—"
                : ToToronto(latest.TimestampUtc).ToString("MM-dd HH:mm:ss");
            string completedLocal = batch.LatestIntervalCompletedUtc.HasValue
                ? ToToronto(batch.LatestIntervalCompletedUtc.Value).ToString("MM-dd HH:mm:ss")
                : "—";
            string age = FormatAge(batch.LatestEvidenceAgeAtReceipt);

            Console.WriteLine(
                $"{poll,6} {ToToronto(batch.ReceivedUtc),20:MM-dd HH:mm:ss} " +
                $"{eventLocal,20} {completedLocal,20} {age,12} {state,20} " +
                $"{batch.AttemptCount,9} {batch.Bars.Count,7:N0}");

            previousLatest = latest;
            if (poll < PollCount)
                await Task.Delay(PollInterval);
        }

        Console.WriteLine();
        Console.WriteLine("Result: polling sequence complete; review new/repeated/revised states and completion-to-receipt ages above.");
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

    private static DateTime ToToronto(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            TorontoTimeZone);

    private static string FormatAge(TimeSpan? age)
    {
        if (!age.HasValue)
            return "—";
        if (age.Value < TimeSpan.Zero)
            return $"future {age.Value.Duration():g}";
        return age.Value.ToString("g");
    }
}
