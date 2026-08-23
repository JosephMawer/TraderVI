#nullable enable

using Core.DataQuality;
using Shouldly;
using System;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class HistoryFreshnessEligibilityTests
{
    private static readonly DateTime MarketDataAsOf = new(2026, 8, 21);
    private static readonly DateTime[] Sessions =
    [
        new(2026, 8, 17),
        new(2026, 8, 18),
        new(2026, 8, 19),
        new(2026, 8, 20),
        new(2026, 8, 21)
    ];

    [Fact]
    public void Evaluate_LatestBarMatchesReferenceSession_IsEligible()
    {
        HistoryFreshnessDecision result = HistoryFreshnessEligibility.Evaluate(
            new DateTime(2026, 8, 21, 16, 0, 0),
            MarketDataAsOf,
            Sessions);

        result.IsEligible.ShouldBeTrue();
        result.SessionsBehind.ShouldBe(0);
    }

    [Fact]
    public void Evaluate_LatestBarIsPriorSession_IsIneligible()
    {
        HistoryFreshnessDecision result = HistoryFreshnessEligibility.Evaluate(
            new DateTime(2026, 8, 20),
            MarketDataAsOf,
            Sessions);

        result.IsEligible.ShouldBeFalse();
        result.SessionsBehind.ShouldBe(1);
        result.Reason.ShouldContain("1 completed TSX session");
    }

    [Fact]
    public void Evaluate_CalendarGap_CountsBenchmarkSessionsNotDays()
    {
        DateTime monday = new(2026, 8, 24);
        DateTime[] sessions = [new(2026, 8, 21), monday];

        HistoryFreshnessDecision result = HistoryFreshnessEligibility.Evaluate(
            new DateTime(2026, 8, 21),
            monday,
            sessions);

        result.IsEligible.ShouldBeFalse();
        result.SessionsBehind.ShouldBe(1);
    }

    [Fact]
    public void Evaluate_LatestBarAfterReferenceSession_IsIneligible()
    {
        HistoryFreshnessDecision result = HistoryFreshnessEligibility.Evaluate(
            new DateTime(2026, 8, 24),
            MarketDataAsOf,
            Sessions);

        result.IsEligible.ShouldBeFalse();
        result.SessionsBehind.ShouldBe(0);
        result.Reason.ShouldContain("after the canonical market-data session");
    }
}
