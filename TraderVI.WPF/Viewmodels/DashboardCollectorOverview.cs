#nullable enable
using System;
using System.IO;
using System.Text.Json;
using Core.Trader;

namespace TraderVI.WPF.Viewmodels;

public sealed record DashboardCollectorOverview(string State, string Color, string Detail)
{
    private const string Neutral = "#E8EEF6", Warning = "#F5B94C", Alert = "#FF7D87", Good = "#55D68B";
    private sealed record Tick(string? State);
    private sealed record Receipt(DateTimeOffset ObservedUtc, Tick? Result);

    public static DashboardCollectorOverview Read(string pathFile, TimeProvider clock)
    {
        try
        {
            var path = File.ReadAllText(pathFile).Trim();
            if (!Path.IsPathFullyQualified(path)) return Unavailable();
            // The collector atomically replaces this file on each tick.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var receipt = JsonSerializer.Deserialize<Receipt>(stream);
            if (receipt?.Result?.State is not { Length: > 0 } state || receipt.ObservedUtc == default) return Unavailable();
            var age = clock.GetUtcNow() - receipt.ObservedUtc;
            var detail = $"Last collector cycle: {state}. Reported {PaperTradingMonitor.ToToronto(receipt.ObservedUtc.UtcDateTime):MMM d, HH:mm:ss} Toronto.";
            if (age < TimeSpan.Zero) return new("Unverified", Warning, "Collector receipt is future-dated. " + detail);
            if (age > TimeSpan.FromMinutes(2)) return new("Stale", Warning, detail);
            var (label, color) = state switch
            {
                "ClosedSession" or "OutsideSession" => ("Outside session", Neutral),
                "Collected" => ("Collected", Good),
                "Busy" => ("Working", Neutral),
                "NoNewRequestAdmitted" or "WaitingForCompletedMinute" => ("Waiting", Neutral),
                "StartupTransportCooldown" => ("Starting", Neutral),
                "PartialCollection" => ("Partial", Warning),
                "MinuteDeadlineElapsed" => ("Deadline elapsed", Warning),
                "CollectorAttention" or "SourceAttention" or "OtherCollectorOwnsLease" or "LeaseLost"
                    or "SubscriptionBudgetExceeded" => ("Needs attention", Alert),
                "Stopped" => ("Stopped", Alert),
                _ => ("Unverified", Warning)
            };
            return new(label, color, detail);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        { return Unavailable(); }
    }

    private static DashboardCollectorOverview Unavailable() => new("Unavailable", Warning, "Cannot read the configured collector status receipt.");
}
