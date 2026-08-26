#nullable enable

using Core.TMX;
using Core.TMX.Models.Domain;
using GraphQL;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TmxIntradayRequestContractTests
{
    [Fact]
    public void Request_MatchesCurrentTmxIntradayContract()
    {
        DateTime start = new(2026, 8, 25, 13, 30, 47, DateTimeKind.Utc);
        DateTime end = new(2026, 8, 25, 20, 5, 29, DateTimeKind.Utc);

        GraphQLRequest request = TmxClient.BuildIntradayTimeSeriesRequest(
            " XIU ",
            15,
            start,
            end);

        string query = request.Query ?? throw new InvalidOperationException("Expected a GraphQL query.");
        query.ShouldNotContain("$freq");
        query.ShouldNotContain("freq:");
        query.ShouldContain("interval: $interval");
        query.ShouldContain("startDateTime: $startDateTime");
        query.ShouldContain("endDateTime: $endDateTime");

        object variables = request.Variables!;
        string[] propertyNames = variables.GetType()
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        propertyNames.ShouldBe(new[]
        {
            "symbol", "interval", "startDateTime", "endDateTime"
        });
        GetVariable<string>(variables, "symbol").ShouldBe("XIU");
        GetVariable<int>(variables, "interval").ShouldBe(15);
        GetVariable<int>(variables, "startDateTime").ShouldBe(ToUnixSeconds(
            new DateTime(2026, 8, 25, 13, 30, 0, DateTimeKind.Utc)));
        GetVariable<int?>(variables, "endDateTime").ShouldBe(ToUnixSeconds(
            new DateTime(2026, 8, 25, 20, 5, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Request_RejectsUnsupportedInterval()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            TmxClient.BuildIntradayTimeSeriesRequest(
                "XIU",
                10,
                new DateTime(2026, 8, 25, 13, 30, 0, DateTimeKind.Utc),
                null));
    }

    [Fact]
    public void ResponseGuard_RejectsDailyFallbackBars()
    {
        List<OhlcvBar> bars =
        [
            Bar(new DateTime(2026, 8, 24, 20, 0, 0, DateTimeKind.Utc)),
            Bar(new DateTime(2026, 8, 25, 20, 0, 0, DateTimeKind.Utc))
        ];

        InvalidOperationException exception = Should.Throw<InvalidOperationException>(() =>
            TmxClient.ValidateIntradayResponse(
                bars,
                15,
                new DateTime(2026, 8, 23, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc)));

        exception.Message.ShouldContain("daily fallback");
    }

    [Fact]
    public void ResponseGuard_AcceptsAndSortsIntradayBars()
    {
        List<OhlcvBar> bars =
        [
            Bar(new DateTime(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc)),
            Bar(new DateTime(2026, 8, 25, 13, 45, 0, DateTimeKind.Utc)),
            Bar(new DateTime(2026, 8, 25, 13, 30, 0, DateTimeKind.Utc))
        ];

        List<OhlcvBar> result = TmxClient.ValidateIntradayResponse(
            bars,
            15,
            new DateTime(2026, 8, 25, 13, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc));

        result.Select(bar => bar.TimestampUtc).ShouldBe(new[]
        {
            new DateTime(2026, 8, 25, 13, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 25, 13, 45, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc)
        });
    }

    private static T GetVariable<T>(object variables, string name) =>
        (T)variables.GetType().GetProperty(name)!.GetValue(variables)!;

    private static int ToUnixSeconds(DateTime utc) =>
        checked((int)new DateTimeOffset(utc).ToUnixTimeSeconds());

    private static OhlcvBar Bar(DateTime timestampUtc) =>
        new(timestampUtc, 100m, 101m, 99m, 100m, 1_000);
}
