#nullable enable
using Core.Runtime;
using Core.Trader;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace TraderVI.WPF.Viewmodels;

/// <summary>Presentation of the existing status receipt; performs no reads or commands.</summary>
public sealed record DashboardEngineOverview(string State, string Detail, string Checked, string History, string HistoryDetail)
{
    public static DashboardEngineOverview From(IndependentEngineStatus? status, DateTimeOffset now)
    {
        if (status is null || status.CheckedAt == default) return new("Unable to verify", "Engine status unavailable. Reconnect before assuming trading or protection is running.",
            "No current check received", "Unable to verify", "History-check status is unavailable. Open History checks to review it.");
        var checkedAt = "Checked " + PaperTradingMonitor.ToToronto(status.CheckedAt.UtcDateTime).ToString("MMM d, HH:mm:ss") + " Toronto";
        if (status.CheckedAt > now || now - status.CheckedAt > TimeSpan.FromMinutes(2))
            return new("Unable to verify", "Engine status is stale or its time is inconsistent. Reconnect before relying on it.", checkedAt,
                "Unable to verify", "The engine status receipt is not current. Open History checks to review coverage.");
        var blocked = status.Work.Where(w => w.State == "Blocked").Select(w => w.Name).ToArray();
        var notices = status.Notices.IsDefaultOrEmpty ? Array.Empty<EngineNotice>() : status.Notices.ToArray();
        var history = notices.LastOrDefault(n => n.Code == "IntegrityCoverage");
        // Compact only the existing, known summary format. Preserve the complete notice and
        // its actual observation time; Information severity does not imply complete coverage.
        var match = Regex.Match(history?.Message ?? "", @"^Integrity: (\d+) unresolved incident\(s\); (\d+) incomplete latest audit\(s\)\. ");
        string headline = history is null ? "Awaiting status" : "Review latest check";
        if (match.Success)
        {
            headline = $"{match.Groups[1].Value} incidents · {match.Groups[2].Value} incomplete";
            if (history!.Message.Contains("(overdue).", StringComparison.Ordinal)) headline += " · archive overdue";
            else if (history.Message.Contains("Older archive has no completed audit.", StringComparison.Ordinal)) headline += " · archive unverified";
        }
        var latestOtherNotice = notices.LastOrDefault(n => n.Code != "IntegrityCoverage");
        string detail = $"Engine: {status.State}." + (status.VerificationOnly ? " Verification mode: real trading is disabled." : "") +
            (blocked.Length == 0 ? "" : $" Needs attention: {string.Join(", ", blocked)}.") +
            (latestOtherNotice is null ? "" : " " + latestOtherNotice.Message);
        string state = status.State + (status.VerificationOnly ? " · verification only" : "") + (blocked.Length == 0 ? "" : " · needs attention");
        return new(state, detail, checkedAt, headline, history is null ? "No history coverage notice received. Open History checks for details." :
            history.Message + "\nReported " + PaperTradingMonitor.ToToronto(history.ObservedAt.UtcDateTime).ToString("MMM d, HH:mm:ss") + " Toronto");
    }
}
