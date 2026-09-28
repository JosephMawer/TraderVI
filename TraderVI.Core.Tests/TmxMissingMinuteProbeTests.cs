using Core.TMX;
using Core.TMX.Models.Domain;
using Core.TMX.Models.Dto;
using Sandbox.Probes;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class TmxMissingMinuteProbeTests
{
    private static readonly DateTime Start = new(2026, 9, 28, 13, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void DistinguishesAbsentNullAndExplicitZeroVolumeWithoutInventingPrices()
    {
        var slots = TmxMissingMinuteProbe.Classify([
            new() { dateTime = "2026-09-28 9:30:00 AM", open = 10, high = 10, low = 10, close = 10, volume = 0 },
            new() { dateTime = "2026-09-28 9:31:00 AM" },
            new() { dateTime = "2026-09-28 9:32:00 AM", volume = 0 },
            new() { dateTime = "2026-09-28 9:33:00 AM", close = 10, volume = 100 }
        ], Start, Start.AddMinutes(5), 1);
        Assert.Equal("Complete", slots[0].Status);
        Assert.True(slots[0].ZeroVolume);
        Assert.Equal("AllNull", slots[1].Status);
        Assert.False(slots[1].ZeroVolume);
        Assert.Equal("PartialNull", slots[2].Status);
        Assert.True(slots[2].ZeroVolume);
        Assert.Equal("PartialNull", slots[3].Status);
        Assert.True(slots[3].PositiveVolume);
        Assert.Equal("Absent", slots[4].Status);
        Assert.False(slots[4].VolumePresent);
    }

    [Fact]
    public void ExcludesEndBoundaryAndRejectsDuplicateOrInvalidTimes()
    {
        TmxTimeSeriesPointDto point = new() { dateTime = "2026-09-28 9:30:00 AM" };
        Assert.Throws<InvalidOperationException>(() => TmxMissingMinuteProbe.Classify([point, point], Start, Start.AddMinutes(1), 1));
        Assert.Throws<InvalidOperationException>(() => TmxMissingMinuteProbe.Classify([new() { dateTime = "bad" }], Start, Start.AddMinutes(1), 1));
        Assert.Throws<InvalidOperationException>(() => TmxMissingMinuteProbe.Classify([new() { dateTime = "2026-09-28 9:30:01 AM" }], Start, Start.AddMinutes(1), 1));
        var slots = TmxMissingMinuteProbe.Classify([new() { dateTime = "2026-09-28 9:35:00 AM" }], Start, Start.AddMinutes(5), 5);
        Assert.Equal("Absent", Assert.Single(slots).Status);
    }

    [Fact]
    public async Task DiagnosticPreservesRawRowsWhileOrdinaryBatchStillFiltersThem()
    {
        const string payload = """
            {"data":{"getTimeSeriesData":[
              {"dateTime":"2026-09-28 9:30:00 AM","open":10,"high":10,"low":10,"close":10,"volume":0},
              {"dateTime":"2026-09-28 9:31:00 AM","open":null,"high":null,"low":null,"close":null,"volume":null},
              {"dateTime":"2026-09-28 9:32:00 AM","open":null,"high":null,"low":null,"close":null,"volume":50}
            ]}}
            """;
        var handler = new Handler(payload);
        using var client = new TmxClient(handler);
        var diagnostic = await client.GetIntradayTimeSeriesDiagnosticAsync("TEST", 1, Start, Start.AddMinutes(3));
        Assert.True(diagnostic.SourceDataPresent);
        Assert.Equal(3, diagnostic.SourcePoints.Count);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(Start, Assert.Single(diagnostic.Batch.Bars).TimestampUtc);
        var ordinary = await client.GetIntradayTimeSeriesBatchAsync("TEST", 1, Start, Start.AddMinutes(3));
        Assert.Equal(diagnostic.Batch.Bars, ordinary.Bars);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(1, ordinary.RequestCount);
        Assert.Equal(1, ordinary.AttemptCount);
    }

    [Theory]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{}}")]
    [InlineData("{\"data\":{\"getTimeSeriesData\":null}}")]
    public async Task MissingResponseIsNotEvidenceOfNoTrades(string payload)
    {
        using var client = new TmxClient(new Handler(payload));
        var result = await client.GetIntradayTimeSeriesDiagnosticAsync("TEST", 1, Start, Start.AddMinutes(1));
        Assert.False(result.SourceDataPresent);
        Assert.Empty(result.SourcePoints);
        Assert.Empty(result.Batch.Bars);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetIntradayTimeSeriesBatchAsync("TEST", 1, Start, Start.AddMinutes(1)));
    }

    [Theory]
    [InlineData("{\"errors\":[{\"message\":\"synthetic error\"}]}")]
    [InlineData("{\"data\":{\"getTimeSeriesData\":[{\"dateTime\":\"2026-09-28 9:30:00 AM\",\"open\":10,\"high\":9,\"low\":8,\"close\":10,\"volume\":100}]}}")]
    public async Task DiagnosticCannotBypassGraphQlOrPriceValidation(string payload)
    {
        var handler = new Handler(payload);
        using var client = new TmxClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetIntradayTimeSeriesDiagnosticAsync("TEST", 1, Start, Start.AddMinutes(1)));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void ReportedTradeInMissingMinuteIsAContradictionAndOldestMinuteMayBeTruncated()
    {
        TmxRecentTradeDto Trade(int minute) => new() { datetime = $"2026-09-28T09:{30 + minute}:10-04:00", price = 10, volume = 100 };
        var batch = new TmxIntradayBatch("TEST", 1, Start, Start.AddMinutes(5), Start.AddMinutes(6), Start.AddMinutes(6), 1, 1,
            [new(Start, 10, 10, 10, 10, 200), new(Start.AddMinutes(2), 10, 10, 10, 10, 100)]);
        var result = TmxMissingMinuteProbe.CompareTrades([Trade(4), Trade(2), Trade(0)], batch);
        Assert.Equal(1, result.TradeMinutesWithoutBar);
        Assert.Equal(2, result.AbsentMinutesBetweenTradeTimes);
        Assert.Equal(1, result.InteriorVolumeMatches);
        Assert.Equal(0, result.InteriorVolumeMismatches); // Oldest minute's 200 vs 100 is not a mismatch.
        Assert.Throws<InvalidOperationException>(() => TmxMissingMinuteProbe.CompareTrades([
            new() { datetime = "2026-09-28T09:30:10", price = 10, volume = 100 }], batch));
    }

    [Fact]
    public async Task RecentTradeDiagnosticUsesBoundedPublicQueryAndPreservesOffset()
    {
        var handler = new Handler("""
            {"data":{"getCompanyMostRecentTrades":[{"datetime":"2026-09-28T09:30:10-04:00","price":10,"volume":100,"exchangeCode":"TSX"}]}}
            """);
        using var client = new TmxClient(handler);
        var result = await client.GetRecentTradesDiagnosticAsync(" TEST ");
        Assert.Equal("2026-09-28T09:30:10-04:00", Assert.Single(result.Trades).datetime);
        Assert.Equal(1, result.AttemptCount);
        Assert.Contains("\"limit\":25", handler.LastRequest);
        Assert.Contains("\"symbol\":\"TEST\"", handler.LastRequest);
    }

    private sealed class Handler(string payload) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string LastRequest { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastRequest = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
