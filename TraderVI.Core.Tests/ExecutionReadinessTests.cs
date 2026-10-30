using System;
using Core.Trader.Promotion;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public class ExecutionReadinessTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 13, 15, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Waiting", "Execution is waiting for regular trading hours. No opening order is submitted outside the session.", "Waiting for market")]
    [InlineData("Waiting", "Execution is waiting for a reviewed regular TSX session.", "Waiting for market")]
    [InlineData("Running", "Waiting for fresh shared market inputs; previously evaluated inputs are stale.", "Waiting for fresh data")]
    [InlineData("Running", "Waiting for verified shared market inputs; no current-session market input has been evaluated.", "Waiting for fresh data")]
    [InlineData("Running", "Fresh shared market inputs evaluated; no confirmed entry signal at this evaluation.", "Ready — no qualifying signal")]
    [InlineData("Blocked", "Waiting for fresh shared market inputs; previously evaluated inputs are stale.", "Blocked")]
    [InlineData("Running", "No confirmed entry signal at this evaluation.", "Checking execution")]
    [InlineData("Running", "Last decision received broker acknowledgement; consult order status for confirmed fills.", "Checking execution")]
    [InlineData("Running", "New automatic buys are stopped; ordinary exit rules remain enabled.", "New buys paused")]
    [InlineData("Waiting", "Execution is waiting for the connected account and the shared manual-order controls.", "Waiting for checks")]
    [InlineData("Off", "Stopped by operator", "Execution stopped")]
    [InlineData("Paused", "Paused by operator", "Execution paused")]
    [InlineData("NewState", "Unknown", "Unable to verify")]
    public void ClassifiesOnlyKnownEvidence(string state, string message, string title)
    {
        var display = ExecutionReadiness.From(new(state, message, Now), Now);
        Assert.Equal(title, display.Title);
        Assert.Contains("Toronto", display.Checked);
        if (state == "Blocked" || title == "Waiting for checks") Assert.Equal(message, display.Detail);
    }

    [Theory]
    [InlineData(-121)]
    [InlineData(1)]
    public void OldOrFutureSuccessCannotLookReady(int seconds)
    {
        var status = new BrokerExecutionHostStatus("Running", "Fresh shared market inputs evaluated; no confirmed entry signal at this evaluation.", Now.AddSeconds(seconds));
        Assert.Equal("Unable to verify", ExecutionReadiness.From(status, Now).Title);
    }

    [Fact]
    public void MissingReceiptIsNotOffOrReady()
    {
        Assert.Equal("Unable to verify", ExecutionReadiness.From(null, Now).Title);
        Assert.Equal("Unable to verify", ExecutionReadiness.From(new("Running", "Ready", default), Now).Title);
    }
}
