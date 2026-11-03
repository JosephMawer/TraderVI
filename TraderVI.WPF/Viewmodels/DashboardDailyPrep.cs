#nullable enable
using Core.Runtime;
using Core.Trader.DelphiLive;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TraderVI.WPF.Viewmodels;

public sealed record DashboardDailyPrep(string Label, string Detail, string Color)
{
    private sealed record Stage(string Name, string State, DateTimeOffset? CompletedUtc);
    private sealed record Receipt(DateOnly? PreparationDate, DateOnly? LocalRunDate, string State,
        DateTimeOffset? StartedUtc, DateTimeOffset? CompletedUtc, Stage[]? Stages);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const string Neutral = "#E8EEF6", Amber = "#F5B94C", Red = "#FF7D87", Green = "#55D68B";

    public static DashboardDailyPrep Read()
    {
        try
        {
            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TraderVI", "Nightly", "status.json");
            return Read(file, DailyBenchmarkPolicy.LoadCalendar(), DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or JsonException)
        { return new("Unverified", "The reviewed TSX calendar is unavailable for this session.", Amber); }
    }

    public static DashboardDailyPrep Read(string path, ReviewedTsxSessionCalendar calendar, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, ReviewedTsxSessionCalendar.Toronto).DateTime);
        var session = calendar.IsRegularSession(today) && now.UtcDateTime < calendar.GetSessionBounds(today).CloseUtc
            ? today : calendar.GetNextSession(today);
        var date = session.ToString("MMM d", CultureInfo.InvariantCulture);
        DashboardDailyPrep Show(string state, string detail, string color) =>
            new($"{date} · {state}", $"Trading session {session:yyyy-MM-dd} (Toronto).\n{detail}", color);

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var run = JsonSerializer.Deserialize<Receipt>(stream, Json);
            if (run is null || (run.PreparationDate ?? run.LocalRunDate) is not { } prepared ||
                run.StartedUtc > now || run.CompletedUtc > now || run.CompletedUtc < run.StartedUtc)
                return Show("Unverified", "The nightly receipt is incomplete or its timestamps are inconsistent.", Amber);

            var stages = run.Stages ?? [];
            var last = stages.Where(s => s is { CompletedUtc: not null } && s.CompletedUtc <= now)
                .OrderBy(s => s.CompletedUtc).LastOrDefault();
            var detail = $"Nightly run for {prepared:yyyy-MM-dd}: {run.State}.";
            if (last?.CompletedUtc is { } at)
                detail += $"\nLast finished step: {last.Name} ({last.State}), {TimeZoneInfo.ConvertTime(at, ReviewedTsxSessionCalendar.Toronto):MMM d HH:mm} Toronto.";
            var flagged = stages.Where(s => s?.State is "Attention" or "Failed").Select(s => $"{s.Name} ({s.State})").ToArray();
            if (flagged.Length > 0) detail += "\nReview: " + string.Join(", ", flagged) + ".";
            if (prepared > session) return Show("Unverified", detail, Amber);
            if (prepared < session)
                return now.UtcDateTime >= calendar.GetSessionBounds(session).OpenUtc
                    ? Show("Overdue", detail, Red) : Show("Pending", detail, Neutral);
            if (run.State == "Failed") return Show("Failed", detail, Red);
            if (run.State == "Running" && run.StartedUtc is { } start && run.CompletedUtc is null)
            {
                var current = stages.LastOrDefault(s => s?.State == "Running");
                if (current != null) detail += $"\nCurrent step: {current.Name}.";
                return now - start > TimeSpan.FromHours(10) ? Show("Overdue", detail, Red) : Show("Preparing", detail, Amber);
            }
            if (run.CompletedUtc != null && run.StartedUtc != null)
            {
                if (run.State == "Attention") return Show("Attention", detail, Amber);
                if (run.State == "Succeeded" && stages.Any(s => s is { Name: "Delphi", State: "Succeeded", CompletedUtc: not null }
                        && s.CompletedUtc >= run.StartedUtc && s.CompletedUtc <= run.CompletedUtc))
                    return Show("Ready", detail, Green);
            }
            return Show("Unverified", detail, Amber);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return Show("Unverified", "No readable nightly preparation receipt is available.", Amber); }
    }
}
