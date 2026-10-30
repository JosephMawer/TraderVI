using Core.Runtime;
using System;
using TraderVI.WPF.Viewmodels;
using Xunit;

namespace TraderVI.Core.Tests;

public class DashboardEngineOverviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static IndependentEngineStatus Receipt(string message) => new(Guid.NewGuid(), "Running", Now, [])
    {
        Notices = [new(Now.AddHours(-1), "Information", "IntegrityCoverage", message), new(Now, "Information", "Other", "Unrelated latest notice.")]
    };

    [Fact]
    public void Information_notice_does_not_hide_incomplete_or_overdue_coverage()
    {
        var result = DashboardEngineOverview.From(Receipt("Integrity: 0 unresolved incident(s); 4 incomplete latest audit(s). Older archive verified through 2026-10-23 03:43 UTC (overdue)."), Now);
        Assert.Equal("0 incidents · 4 incomplete · archive overdue", result.History);
        Assert.Contains("Unrelated latest notice.", result.Detail);
        Assert.Contains("Reported ", result.HistoryDetail);
        Assert.Contains("07:00:00", result.HistoryDetail);
    }

    [Theory]
    [InlineData(-121)]
    [InlineData(1)]
    public void Stale_or_future_receipts_do_not_keep_running_or_coverage_claims(int offset)
    {
        var result = DashboardEngineOverview.From(Receipt("test") with { CheckedAt = Now.AddSeconds(offset) }, Now);
        Assert.Equal("Unable to verify", result.State);
        Assert.Equal("Unable to verify", result.History);
    }

    [Fact]
    public void Missing_receipt_and_unknown_history_stay_explicit()
    {
        Assert.Equal("Unable to verify", DashboardEngineOverview.From(null, Now).State);
        Assert.Equal("No current check received", DashboardEngineOverview.From(Receipt("test") with { CheckedAt = default }, Now).Checked);
        var result = DashboardEngineOverview.From(Receipt("Temporary Paper continuation; historical integrity remains unverified."), Now);
        Assert.Equal("Review latest check", result.History);
        Assert.Contains("historical integrity remains unverified", result.HistoryDetail);
        Assert.Equal("Awaiting status", DashboardEngineOverview.From(new(Guid.NewGuid(), "Running", Now, []), Now).History);
    }

    [Fact]
    public void Missing_archive_and_engine_restrictions_remain_visible()
    {
        var status = Receipt("Integrity: 0 unresolved incident(s); 0 incomplete latest audit(s). Older archive has no completed audit.") with
        { VerificationOnly = true, Work = [new("Real", "Blocked", null, null, 1)] };
        var result = DashboardEngineOverview.From(status, Now);
        Assert.Equal("0 incidents · 0 incomplete · archive unverified", result.History);
        Assert.Contains("verification only", result.State);
        Assert.Contains("needs attention", result.State);
        Assert.Contains("Real", result.Detail);
    }
}
