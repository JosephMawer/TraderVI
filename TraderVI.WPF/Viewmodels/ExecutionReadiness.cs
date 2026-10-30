#nullable enable
using System;
using Core.Trader;
using Core.Trader.Promotion;

namespace TraderVI.WPF.Viewmodels;

/// <summary>Display-only interpretation of the existing host receipt; never execution authority.</summary>
public sealed record ExecutionReadiness(string Title, string Detail, string Checked)
{
    public static ExecutionReadiness From(BrokerExecutionHostStatus? status, DateTimeOffset now)
    {
        if (status is null || status.CheckedAt == default)
            return new("Unable to verify", "Waiting for an execution status check. Open execution setup if this persists.", "No check received");
        string checkedAt = "Last checked " + PaperTradingMonitor.ToToronto(status.CheckedAt.UtcDateTime).ToString("MMM d, HH:mm:ss") + " Toronto";
        if (status.CheckedAt > now || now - status.CheckedAt > TimeSpan.FromMinutes(2))
            return new("Unable to verify", "Execution status is stale or its time is inconsistent. Last reported: " + status.Message, checkedAt);

        // Exact known messages are a compatibility boundary with the installed engine. Unknown
        // messages stay neutral; Running alone must never become a claim of buy readiness.
        string title = status.State switch
        {
            "Off" => "Execution stopped",
            "Paused" => "Execution paused",
            "Blocked" => "Blocked",
            "Waiting" when status.Message is
                "Execution is waiting for a reviewed regular TSX session." or
                "Execution is waiting for regular trading hours. No opening order is submitted outside the session."
                => "Waiting for market",
            "Running" or "Waiting" when status.Message is
                "Waiting for verified shared market inputs; no current-session market input has been evaluated." or
                "Waiting for fresh shared market inputs; previously evaluated inputs are stale."
                => "Waiting for fresh data",
            "Running" when status.Message == "Fresh shared market inputs evaluated; no confirmed entry signal at this evaluation."
                => "Ready — no qualifying signal",
            "Running" when status.Message == "New automatic buys are stopped; ordinary exit rules remain enabled."
                => "New buys paused",
            "Running" => "Checking execution",
            "Waiting" => "Waiting for checks",
            _ => "Unable to verify"
        };
        string detail = status.Message;
        if (title == "Waiting for market")
            detail = "The host is waiting for regular trading hours. Fresh market data, account, order and risk checks are still required before trading.";
        if (title == "Ready — no qualifying signal")
            detail = "The last evaluation used fresh inputs and found no confirmed entry signal. This does not certify every stock's data or guarantee the next order checks will pass.";
        return new(title, detail, checkedAt);
    }
}
