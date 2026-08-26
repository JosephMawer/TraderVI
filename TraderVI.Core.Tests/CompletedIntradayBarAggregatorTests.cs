using Core.TMX.Models.Domain;
using Core.Trader;
using Shouldly;
using System;
using System.Collections.Generic;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class CompletedIntradayBarAggregatorTests
{
    [Fact]
    public void AggregateFiveMinuteBars_UsesOnlyCompleteExactTriplets()
    {
        DateTime openUtc = new(2026, 8, 26, 13, 30, 0, DateTimeKind.Utc);
        DateTime receivedUtc = openUtc.AddMinutes(31);
        List<OhlcvBar> source =
        [
            Bar(openUtc, 100m, 102m, 99m, 101m, 10),
            Bar(openUtc.AddMinutes(5), 101m, 103m, 100m, 102m, 20),
            Bar(openUtc.AddMinutes(10), 102m, 104m, 101m, 103m, 30),
            Bar(openUtc.AddMinutes(15), 103m, 105m, 102m, 104m, 40),
            Bar(openUtc.AddMinutes(20), 104m, 106m, 103m, 105m, 50),
            Bar(openUtc.AddMinutes(25), 105m, 107m, 104m, 106m, 60),
            Bar(openUtc.AddMinutes(30), 106m, 108m, 105m, 107m, 70)
        ];

        IReadOnlyList<OhlcvBar> result =
            CompletedIntradayBarAggregator.AggregateFiveMinuteBars(
                source,
                receivedUtc);

        result.Count.ShouldBe(2);
        result[0].ShouldBe(Bar(openUtc, 100m, 104m, 99m, 103m, 60));
        result[1].ShouldBe(
            Bar(openUtc.AddMinutes(15), 103m, 107m, 102m, 106m, 150));
    }

    [Fact]
    public void AggregateFiveMinuteBars_SkipsBucketWithMissingComponent()
    {
        DateTime openUtc = new(2026, 8, 26, 13, 30, 0, DateTimeKind.Utc);
        List<OhlcvBar> source =
        [
            Bar(openUtc, 100m, 102m, 99m, 101m, 10),
            Bar(openUtc.AddMinutes(10), 101m, 103m, 100m, 102m, 20)
        ];

        CompletedIntradayBarAggregator.AggregateFiveMinuteBars(
            source,
            openUtc.AddMinutes(20)).ShouldBeEmpty();
    }

    [Fact]
    public void BuildPolicyBars_StartsAfterEntryAndAssignsSessionMetadata()
    {
        DateTime firstOpenUtc =
            new(2026, 8, 26, 13, 30, 0, DateTimeKind.Utc);
        DateTime nextOpenUtc = firstOpenUtc.AddDays(1);
        List<OhlcvBar> bars =
        [
            Bar(firstOpenUtc, 100m, 102m, 99m, 101m, 10),
            Bar(firstOpenUtc.AddMinutes(15), 101m, 103m, 100m, 102m, 20),
            Bar(firstOpenUtc.AddHours(6).AddMinutes(15), 102m, 104m, 101m, 103m, 30),
            Bar(nextOpenUtc, 103m, 105m, 102m, 104m, 40)
        ];

        IReadOnlyList<DelayedIntradayBar> result =
            CompletedIntradayBarAggregator.BuildPolicyBars(
                bars,
                nextOpenUtc.AddMinutes(20),
                firstOpenUtc.AddMinutes(7));

        result.Count.ShouldBe(3);
        result[0].StartUtc.ShouldBe(firstOpenUtc.AddMinutes(15));
        result[0].TradingSessionOrdinal.ShouldBe(1);
        result[0].IsSessionClosingBar.ShouldBeFalse();
        result[1].IsSessionClosingBar.ShouldBeTrue();
        result[1].TradingSessionOrdinal.ShouldBe(1);
        result[2].TradingSessionOrdinal.ShouldBe(2);
    }

    private static OhlcvBar Bar(
        DateTime timestampUtc,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume) =>
        new(timestampUtc, open, high, low, close, volume);
}
