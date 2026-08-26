#nullable enable

using Core.TMX;
using Core.TMX.Models.Domain;
using GraphQL;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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

    [Fact]
    public void ResponseGuard_RejectsInvalidOhlcAndNegativeVolume()
    {
        DateTime timestamp = new(2026, 8, 25, 13, 30, 0, DateTimeKind.Utc);
        var invalidOhlc = new OhlcvBar(timestamp, 100m, 99m, 98m, 100m, 1_000);
        var negativeVolume = new OhlcvBar(timestamp, 100m, 101m, 99m, 100m, -1);

        Should.Throw<InvalidOperationException>(() => Validate([invalidOhlc]))
            .Message.ShouldContain("invalid OHLC");
        Should.Throw<InvalidOperationException>(() => Validate([negativeVolume]))
            .Message.ShouldContain("negative volume");
    }

    [Fact]
    public void ResponseGuard_RejectsNonUtcAndMisalignedTimestamps()
    {
        OhlcvBar nonUtc = Bar(new DateTime(
            2026, 8, 25, 13, 30, 0, DateTimeKind.Unspecified));
        OhlcvBar misaligned = Bar(new DateTime(
            2026, 8, 25, 13, 37, 0, DateTimeKind.Utc));

        Should.Throw<InvalidOperationException>(() => Validate([nonUtc]))
            .Message.ShouldContain("not UTC");
        Should.Throw<InvalidOperationException>(() => Validate([misaligned]))
            .Message.ShouldContain("does not align");
    }

    [Fact]
    public void ChunkWindows_UseFiveDayBoundariesAndMinutePrecision()
    {
        IReadOnlyList<IntradayRequestWindow> windows =
            TmxClient.BuildIntradayRequestWindows(
                new DateTime(2026, 5, 28, 13, 30, 45, DateTimeKind.Utc),
                new DateTime(2026, 6, 9, 14, 0, 29, DateTimeKind.Utc));

        windows.ShouldBe(new[]
        {
            new IntradayRequestWindow(
                new DateTime(2026, 5, 28, 13, 30, 0, DateTimeKind.Utc),
                new DateTime(2026, 6, 2, 13, 30, 0, DateTimeKind.Utc)),
            new IntradayRequestWindow(
                new DateTime(2026, 6, 2, 13, 30, 0, DateTimeKind.Utc),
                new DateTime(2026, 6, 7, 13, 30, 0, DateTimeKind.Utc)),
            new IntradayRequestWindow(
                new DateTime(2026, 6, 7, 13, 30, 0, DateTimeKind.Utc),
                new DateTime(2026, 6, 9, 14, 0, 0, DateTimeKind.Utc))
        });
    }

    [Fact]
    public void ChunkMerge_DeduplicatesEqualBoundariesButRejectsConflicts()
    {
        OhlcvBar boundary = Bar(new DateTime(
            2026, 8, 25, 13, 30, 0, DateTimeKind.Utc));
        OhlcvBar later = Bar(new DateTime(
            2026, 8, 25, 13, 45, 0, DateTimeKind.Utc));

        List<OhlcvBar> merged = TmxClient.MergeChunkBars(
            [boundary, later, boundary]);
        merged.ShouldBe([boundary, later]);

        OhlcvBar conflict = boundary with { Close = 100.50m };
        Should.Throw<InvalidOperationException>(() =>
            TmxClient.MergeChunkBars([boundary, conflict]))
            .Message.ShouldContain("conflicting bars");
    }

    [Fact]
    public async Task Retry_RetriesTransientFailuresWithBoundedDelays()
    {
        int calls = 0;
        var delays = new List<TimeSpan>();

        RetryResult<int> result = await TmxClient.ExecuteWithRetryAsync(
            _ =>
            {
                calls++;
                return calls < 3
                    ? Task.FromException<int>(new HttpRequestException("temporary"))
                    : Task.FromResult(42);
            },
            CancellationToken.None,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        result.Value.ShouldBe(42);
        result.AttemptCount.ShouldBe(3);
        calls.ShouldBe(3);
        delays.ShouldBe([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]);
    }

    [Fact]
    public async Task Retry_DoesNotRetryApplicationErrorsOrCallerCancellation()
    {
        int applicationCalls = 0;
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await TmxClient.ExecuteWithRetryAsync<int>(
                _ =>
                {
                    applicationCalls++;
                    return Task.FromException<int>(new InvalidOperationException("bad query"));
                },
                CancellationToken.None,
                (_, _) => Task.CompletedTask));
        applicationCalls.ShouldBe(1);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await TmxClient.ExecuteWithRetryAsync(
                _ => Task.FromResult(1),
                cancellation.Token,
                (_, _) => Task.CompletedTask));
    }

    [Fact]
    public void Retry_OnlyTreatsRetryableHttpStatusesAsTransient()
    {
        TmxClient.IsTransientTransportFailure(
            new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests),
            CancellationToken.None).ShouldBeTrue();
        TmxClient.IsTransientTransportFailure(
            new HttpRequestException("server", null, HttpStatusCode.ServiceUnavailable),
            CancellationToken.None).ShouldBeTrue();
        TmxClient.IsTransientTransportFailure(
            new HttpRequestException("bad request", null, HttpStatusCode.BadRequest),
            CancellationToken.None).ShouldBeFalse();
    }

    [Fact]
    public void BatchMetadata_DistinguishesEventCompletionFromReceiptTime()
    {
        DateTime eventUtc = new(2026, 8, 25, 19, 45, 0, DateTimeKind.Utc);
        var batch = new TmxIntradayBatch(
            "XIU",
            15,
            eventUtc.AddDays(-1),
            eventUtc.AddMinutes(30),
            eventUtc.AddMinutes(31),
            eventUtc.AddMinutes(35),
            AttemptCount: 1,
            RequestCount: 1,
            Bars: [Bar(eventUtc)]);

        batch.LatestEventUtc.ShouldBe(eventUtc);
        batch.LatestIntervalCompletedUtc.ShouldBe(eventUtc.AddMinutes(15));
        batch.LatestEvidenceAgeAtReceipt.ShouldBe(TimeSpan.FromMinutes(20));
        batch.LatestCompletedBarAtReceipt.ShouldBe(Bar(eventUtc));
        batch.LatestCompletedEventUtc.ShouldBe(eventUtc);
        batch.LatestCompletedEvidenceAgeAtReceipt.ShouldBe(TimeSpan.FromMinutes(20));
        batch.HasFormingBarAtReceipt.ShouldBeFalse();
    }

    [Fact]
    public void BatchMetadata_SeparatesFormingBarFromLatestCompletedBar()
    {
        DateTime completedEventUtc =
            new(2026, 8, 26, 13, 30, 0, DateTimeKind.Utc);
        DateTime formingEventUtc = completedEventUtc.AddMinutes(15);
        var batch = new TmxIntradayBatch(
            "XIU",
            15,
            completedEventUtc,
            formingEventUtc.AddMinutes(10),
            formingEventUtc.AddMinutes(8),
            formingEventUtc.AddMinutes(9),
            AttemptCount: 1,
            RequestCount: 1,
            Bars: [Bar(completedEventUtc), Bar(formingEventUtc)]);

        batch.LatestEventUtc.ShouldBe(formingEventUtc);
        batch.LatestEvidenceAgeAtReceipt.ShouldBe(TimeSpan.FromMinutes(-6));
        batch.LatestCompletedBarAtReceipt.ShouldBe(Bar(completedEventUtc));
        batch.LatestCompletedEventUtc.ShouldBe(completedEventUtc);
        batch.LatestCompletedEvidenceAgeAtReceipt.ShouldBe(TimeSpan.FromMinutes(9));
        batch.HasFormingBarAtReceipt.ShouldBeTrue();
    }

    private static T GetVariable<T>(object variables, string name) =>
        (T)variables.GetType().GetProperty(name)!.GetValue(variables)!;

    private static int ToUnixSeconds(DateTime utc) =>
        checked((int)new DateTimeOffset(utc).ToUnixTimeSeconds());

    private static OhlcvBar Bar(DateTime timestampUtc) =>
        new(timestampUtc, 100m, 101m, 99m, 100m, 1_000);

    private static List<OhlcvBar> Validate(IReadOnlyCollection<OhlcvBar> bars) =>
        TmxClient.ValidateIntradayResponse(
            bars,
            15,
            new DateTime(2026, 8, 25, 13, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc));
}
